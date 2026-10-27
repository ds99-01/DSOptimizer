# net.smb-harden – Wyłącz logowanie gościa i wymagaj podpisywania SMB

## Werdykt 26H2
Portować z ograniczeniem, jako `kind=action`, `risk=caution` (decyzja Dawida z 2026-10-18: tylko klient SMB, `caution` zamiast `safe` ze specu). Akcja ustawia dwa ustawienia klienta SMB na wartości, które Microsoft podaje jako bezpieczne: niezabezpieczone logowanie gościa wyłączone, podpisywanie wymagane. Stara pozycja `lan_panic` z v3.2 robiła więcej (wszystkie profile sieci na Public, zatrzymanie usług odnajdywania, NetBIOS), czego Microsoft nie opisuje jako utwardzania, więc tego nie przenosimy; opis „zabetonowanie usług sieciowych” był nieprawdziwy względem tego, co robi ta wersja.

Akcja nie ma detekcji, journala ani undo (`TweakEngine.Execute` uruchamia tylko `script.apply`). Jedynym dowodem skutku jest **odczyt zwrotny** w samym skrypcie: gdy nie zgadza się z oczekiwanym, skrypt kończy się kodem ≠ 0 i silnik zwraca `Failed` z komunikatem, a nie zielone `Executed`.

## Co zmienia
- Skrypt (`script.apply`, `-File`, PowerShell 5.1): przez funkcję pomocniczą `Set-DsoSmbClientSetting` wywołuje `Set-SmbClientConfiguration -EnableInsecureGuestLogons $false -RequireSecuritySignature $true -Force`, a potem przez `Get-DsoSmbClientSetting` czyta z `Get-SmbClientConfiguration` wartości logiczne `EnableInsecureGuestLogons` i `RequireSecuritySignature`.
- Skutek w rejestrze (zmierzony na VM po tym samym cmdlecie, `docs/zrodla/2026-10-18-partia-c3-zrodla.md`, „Wyniki VM”): DWORD `AllowInsecureGuestAuth` = 0 i `RequireSecuritySignature` = 1 w `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters`. Zapisuje je cmdlet; skrypt nie dotyka rejestru bezpośrednio.
- Odczyt zwrotny: obie wartości muszą być typu bool i równe oczekiwanym (gość `False`, podpisywanie `True`). Brak wartości logicznej (błąd cmdletu, `$null`, tekst) albo rozjazd = wyjątek, kod wyjścia 1, komunikat na stderr („Odczyt zwrotny nie zgadza się z oczekiwanym: …”).
- Nie dotyka: serwera SMB (`Set-SmbServerConfiguration`), usług odnajdywania (`fdPHost`, `FDResPub`, `SSDPSRV`), NetBIOS, profilu sieci, polityk.

## Źródła
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-signing – MS Learn, dostęp 2026-10-18: `Set-SmbClientConfiguration -RequireSecuritySignature $true` („Control SMB signing behavior”), weryfikacja `Get-SmbClientConfiguration`. Strona mówi, że Windows 11 24H2 Home nie wymaga podpisywania.
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-security-hardening – MS Learn, dostęp 2026-10-18: od Windows 11 24H2 wszystkie połączenia SMB muszą być domyślnie podpisane (bez rozróżnienia edycji). **Teksty Microsoftu są sprzeczne co do Home; zmierzone dane wygrywają** (zob. niżej).
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/enable-insecure-guest-logons-smb2-and-smb3 – MS Learn, dostęp 2026-10-18: „Windows disables insecure guest logons by default” (Windows 11 Pro/Enterprise/Education), „We recommend that you don't enable insecure guest logons.”
- https://learn.microsoft.com/en-us/powershell/module/smbshare/set-smbclientconfiguration?view=windowsserver2025-ps – MS Learn, dostęp 2026-10-18: `-EnableInsecureGuestLogons`, `-RequireSecuritySignature`, `-Force`.
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/microsoft-network-client-digitally-sign-communications-always – MS (archiwum), dostęp 2026-10-18: „Best practice: Enable Microsoft network client: Digitally sign communications (always)”.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (sekcja „Wyniki VM” w pliku research).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), build 26300.9457, 2026-10-18:

| | Home | Pro |
|---|---|---|
| `EnableInsecureGuestLogons` (`Get-SmbClientConfiguration`) | **True** | **False** |
| `RequireSecuritySignature` (`Get-SmbClientConfiguration`) | True | True |
| `Parameters\AllowInsecureGuestAuth` w rejestrze | brak wartości | brak wartości |
| `Parameters\RequireSecuritySignature` w rejestrze | brak wartości | brak wartości |

Wniosek dla tej akcji: na **Pro** stan docelowy (gość False, podpisywanie True) jest już stanem domyślnym, więc akcja tylko zapisuje jawne wartości w rejestrze. Na **Home** podpisywanie też jest już True, więc jedyną realną zmianą jest wyłączenie gościa (z True na False): to zaostrzenie względem domyślnego stanu Home, nie jego przywrócenie. Zdanie z MS Learn, że Home nie wymaga podpisywania, **nie potwierdziło się** na VM (jest też sprzeczne z „SMB security hardening”).

## Detekcja i undo
Brak detekcji (`kind=action`): w UI nie ma wiersza stanu, a ponowne uruchomienie zawsze wykonuje zapis i odczyt zwrotny. Jedyny dowód skutku to kod wyjścia i komunikat. Stdout akcji trafia do pola `message` raportu CLI (`Executed`), np. „Klient SMB: EnableInsecureGuestLogons = False, RequireSecuritySignature = True (odczyt zwrotny zgodny).”.

**Brak cofnięcia** (z założenia, brak journala). Ręcznie, oficjalnym cmdletem Microsoftu, np. `Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -RequireSecuritySignature $false -Force` (tylko dla wartości, które chcesz przywrócić; domyślne wartości edycji są w tabeli wyżej). Cmdlet zostawia jawne DWORD-y w rejestrze (zmierzone na VM: po `Set-SmbClientConfiguration` wartość nie wraca do „brak wartości”).

**Współdziałanie z `net.smb-guest-access` i `net.smb-signing-client-off`** (tweaki z journalem, ten sam klucz rejestru): akcja nadpisuje ich stan. Jeśli któryś był zastosowany, jego późniejsze cofnięcie przywróci snapshot z journala sprzed jego zastosowania (poprzednią wartość albo brak wartości), czyli zdejmie utwardzenie. Silnik tego nie wykrywa; ostrzeżenie w katalogu mówi o tym wprost.

## Ryzyka i ostrzeżenia
- `risk=caution`. Ostrzeżenia (zgodne z polem `warnings` w katalogu): „Stare NAS-y i dyski sieciowe bez podpisywania albo wpuszczające tylko gościa mogą przestać się łączyć.” oraz „Późniejsze cofnięcie pozycji logowania gościa albo podpisywania SMB zdejmie to zabezpieczenie.”
- Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj): brak cofnięcia z poleceniem ręcznego cofnięcia; Microsoft „Windows disables insecure guest logons by default” + „We recommend that you don't enable insecure guest logons.” i zmiana względem stanu domyślnego Home; podpisywanie (już True na obu edycjach, ale wymuszenie może zablokować połączenia z urządzeniami bez podpisywania: `STATUS_INVALID_SIGNATURE`, „security policies block unauthenticated guest access”); nadpisanie stanu tweaków SMB z journala; zakres tylko klient; zachowanie pod polityką (GPO) nie sprawdzone: zamierzenie to `Failed`, gdy odczyt zwrotny się nie zgadza, ale nie sprawdzono, czy `Get-SmbClientConfiguration` uwzględnia politykę; jeśli nie, akcja może zgłosić `Executed` (odczyt zwrotny zgodny) mimo polityki, która nadal przesłania ustawienie. Ograniczenie; weryfikacja na VM z polityką ustawioną oficjalnym narzędziem to CARRY do kolejnej partii.
- Skutek w sieci lokalnej nie jest zmierzony.

## Test VM
Przebieg VM z 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba bez błędów (278 zaliczonych / 0 błędów / 17 pominiętych na każdej VM). Raporty lokalne: `tests/vm/out/<data>/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`).

Przypadek `net-smb-harden` (`Flow=Execute`, `Catalog=BuiltIn`), **Passed** na Home i Pro (Pester main 191/0/17):
- Sprawdzono: `execute` kod 0, wynik `Executed`; pole `message` raportu CLI zawiera stdout akcji: „Klient SMB: EnableInsecureGuestLogons = False, RequireSecuritySignature = True (odczyt zwrotny zgodny).” `Check` (niezależny od skryptu akcji): **Passed**: cmdlet daje gość `False` i podpisywanie `True`, a DWORD-y w rejestrze to 0 i 1.
- `Cleanup` bez błędu (`CleanupError` brak): skrypt sprzątania ustawia wartości edycji cmdletem, usuwa jawne DWORD-y i sam rzuca wyjątek, gdy odczyt nie zgadza się z domyślną edycji, więc brak błędu oznacza, że **po usunięciu DWORD-ów odczyt cmdletu zgadza się z domyślną edycji** (Home: gość True, Pro: False; podpisywanie True). Ograniczenie: `Cleanup` ustawia przed usunięciem wartości już równe domyślnym, więc sam ten odczyt nie odróżnia „wróciło do domyślnej po usunięciu” od „odczyt się nie zmienił”. Mocniejszy dowód (zmiana odczytu po usunięciu wartości) dają `net.smb-guest-access` (Pro) i `net.smb-signing-client-off` (Home i Pro).
- `Snapshot` (`AllowInsecureGuestAuth`, `RequireSecuritySignature`): brak wartości przed i po `Cleanup`, `SnapshotDiff` pusty, czyli po przypadku nie zostały ślady w rejestrze.
- **Nie testowane na VM: brak sieci i serwera SMB.** Nie sprawdzono: połączeń z serwerem wymagającym lub niewspierającym podpisywania, połączeń z gościem, zachowania pod polityką (brak sieci i serwera SMB na VM).

## Reddit
Do uzupełnienia w części E.
