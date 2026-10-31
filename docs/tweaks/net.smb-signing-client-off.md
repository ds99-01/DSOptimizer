# net.smb-signing-client-off – Nie wymagaj podpisywania SMB po stronie klienta

## Werdykt 26H2
Portować z ograniczeniem, jako `risk=caution`. Nowa pozycja (decyzja autora z 2026-10-18, rozbicie `lan_godmode`): MS opisuje podpisywanie i dostęp gościa jako osobne ustawienia, a na Windows 11 24H2 Pro/Enterprise/Education dostęp gościa wymaga też wyłączenia wymagania podpisywania („Requiring SMB signing also disables guest access to shares”). MS jednocześnie odradza wyłączanie podpisywania. Dotyczy tylko klienta SMB (połączenia wychodzące); serwer SMB nie jest ruszany (v3.2 też go nie ruszała).

## Co zmienia
- Rejestr (`registry[]`, zapis przez API C#): `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters`, wartość `RequireSecuritySignature`, **DWord**, `0`. `scope=machine`, bez `requiresReboot` (MS: zmiana polityki podpisywania działa bez restartu; zapis przez API rejestru jest widoczny w `Get-SmbClientConfiguration` bez restartu: potwierdził przebieg VM 2026-10-18, zob. „Test VM”). Ścieżkę i typ ustalił odczyt na VM po zmianie oficjalnym cmdletem `Set-SmbClientConfiguration -RequireSecuritySignature $false`.
- Skrypt (`script.detect`): tylko odczyt, `Get-SmbClientConfiguration` przez funkcję pomocniczą `Get-DsoSmbClientSetting`, wartość logiczna `RequireSecuritySignature`. Niczego nie zapisuje.
- Nie dotyka: `EnableSecuritySignature` (dotyczy tylko SMB1, MS ostrzega przed jego używaniem), serwera SMB (`Set-SmbServerConfiguration`), polityk, `AllowInsecureGuestAuth`.
- Decyzja o `registry[]` + `script.detect` jak w `net.smb-guest-access` (agregacja w `StateAggregator`; rozjazd rejestru i cmdletu daje `Partial`). Zachowanie pod faktyczną polityką (np. Group Policy) nie zostało sprawdzone: nie sprawdzono, czy `Get-SmbClientConfiguration` uwzględnia politykę; jeśli nie, pozycja może pokazać zgodność mimo polityki (CARRY: weryfikacja na VM z polityką).

## Źródła
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-signing – MS Learn, dostęp 2026-10-18 (ms.date 2025-08-13): wyłączenie przez politykę „Microsoft network client: Digitally sign communications (always)” = Disabled albo `Set-SmbClientConfiguration -RequireSecuritySignature $false`; weryfikacja `Get-SmbClientConfiguration | FL RequireSecuritySignature` (True/False); „Caution: We don't recommend disabling SMB signing as a workaround for third-party servers. We also don't recommend trying to sign with guest accounts.”; „Requiring SMB signing also disables guest access to shares.” Strona mówi też, że Windows 11 24H2 Home nie wymaga podpisywania, co **nie potwierdziło się na VM** (zob. niżej).
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-security-hardening – MS Learn, dostęp 2026-10-18: od Windows 11 24H2 wszystkie połączenia wychodzące i przychodzące SMB muszą być domyślnie podpisane (bez rozróżnienia edycji).
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/microsoft-network-client-digitally-sign-communications-always – MS (archiwum), dostęp 2026-10-18: wartość `RequireSecuritySignature` jako odpowiednik polityki, zmiana bez restartu; ścieżki klucza strona nie podaje.
- https://learn.microsoft.com/en-us/powershell/module/smbshare/set-smbclientconfiguration?view=windowsserver2025-ps – MS Learn, dostęp 2026-10-18: parametr `-RequireSecuritySignature`.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c3-zrodla.md`, „Wyniki VM”, sekcja 3+4).

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), build 26300.9457, 2026-10-18 (Home = Pro):
- `Get-SmbClientConfiguration`: `RequireSecuritySignature` = **True** na obu edycjach (dane **nie potwierdzają** zdania MS, że Home nie wymaga podpisywania; sprzeczność tekstów MS zostaje w dokumentach, pomiar to True). `EnableSecuritySignature` = True.
- `Parameters\RequireSecuritySignature` w rejestrze: **brak wartości** (Home i Pro). Brak wartości oznacza więc wartość domyślną: wymagane.
- Klucz `Policies\Microsoft\Windows\LanmanWorkstation` nie istnieje; cmdlet go nie zapisuje.

`Set-SmbClientConfiguration -RequireSecuritySignature $false` zapisuje **DWORD `RequireSecuritySignature` = 0** w `LanmanWorkstation\Parameters`, widoczne od razu w cmdlecie (zmierzono tylko zapis cmdletem); przywrócenie cmdletem (`$true`) zostawia jawny DWORD 1 tam, gdzie wcześniej wartości nie było. Dlatego cofnięcie narzędzia używa snapshotu i usuwa wartość.

## Detekcja i undo
Stan składa się z dwóch sygnałów, oba logiczne, bez parsowania tekstu zależnego od języka:
1. Konfiguracja (`registry[]`): `RequireSecuritySignature` = DWORD 0 → `Match`; brak wartości, 1 albo inny typ → `Mismatch`.
2. Stan efektywny (`script.detect`): `RequireSecuritySignature -eq $false` → `Applied`, `$true` → `NotApplied`. Błąd cmdletu albo odpowiedź niebędąca wartością logiczną kończy skrypt błędem → `Unknown`, nigdy `Applied`.

Na świeżym 26H2 (Home i Pro): rejestr `Mismatch` + cmdlet `True` → `NotApplied`; po apply `Applied`. Rejestr 0, a cmdlet `True` → `Partial` (zamierzony projekt; czy polityka daje taki rozjazd, nie sprawdzono); apply w takiej sytuacji nie jest zgłaszany jako sukces.

Undo: snapshot z journala (poprzednia wartość albo brak; brak wraca jako brak, klucz `Parameters` zostaje). `original` = `<RemoveEntry>`. **Bez wpisu w journalu** pozycja usuwa wartość `RequireSecuritySignature`, czyli wraca wartość domyślna (na świeżym 26H2 Home i Pro odczyt przy braku wartości to True, czyli podpisywanie wymagane); zakładamy, że to przywraca stan domyślny, więc ochrona nie spada poniżej fabrycznej. Potwierdzone na VM 2026-10-18 (Home i Pro, „Test VM”): zapis przez API rejestru w C# jest od razu widoczny w `Get-SmbClientConfiguration` bez restartu, a po usunięciu wartości odczyt wraca do domyślnej (True). **Nie sprawdzono:** zachowania pod polityką ani po restarcie.

## Ryzyka i ostrzeżenia
- `risk=caution`. Dosłownie z MS: „We don't recommend disabling SMB signing as a workaround for third-party servers. We also don't recommend trying to sign with guest accounts.” oraz „Requiring SMB signing also disables guest access to shares.”
- Pozycja zmienia tylko wymaganie po stronie klienta; serwer SMB i to, czy serwer podpisuje połączenia, pozostają bez zmian. Pozycja sama nie włącza gościa (do tego `net.smb-guest-access`).
- Skutek w sieci lokalnej nie jest zmierzony.

## Test VM
Przebieg VM z 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba bez błędów (278 zaliczonych / 0 błędów / 17 pominiętych na każdej VM). Raporty lokalne: `tests/vm/out/<data>/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`).

Przypadek `net-smb-signing-client-off` (`Flow=Full`, `Catalog=BuiltIn`, bez restartu), **Passed** na Home i Pro (Pester main 191/0/17), obie edycje identycznie:
- Sprawdzono: `detect-before` = `NotApplied` (wartości brak, cmdlet True), `apply` = `Applied` (`requiresReboot` = false; snapshot po apply: DWORD 0), `detect-after-apply` = `Applied`, `undo` = `Undone`, `detect-after-undo` = `NotApplied`. Stan końcowy = stan przed: wartość nieobecna, klucz `Parameters` zostaje, `SnapshotDiff` pusty.
- `CheckAfterApply` (`RequireSecuritySignature -eq $false`, zaraz po apply, bez restartu): **Passed**: zapis przez API rejestru jest widoczny w cmdlecie bez restartu (zmiana z True na False). `CheckAfterUndo` (`-eq $true`): **Passed**: po usunięciu wartości odczyt wraca do domyślnego True.
- `net-smb-lan-effect`: **Skipped** („brak sieci i serwera SMB na VM”).
- **Nie testowane na VM: brak sieci i serwera SMB.** Nie sprawdzono: połączenia z serwerem wymagającym lub niewspierającym podpisywania, zachowania pod polityką, trwałości po restarcie.
