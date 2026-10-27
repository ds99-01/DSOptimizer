# net.smb-guest-access – Zezwól na logowanie gościa do udziałów sieciowych

## Werdykt 26H2
Portować z ograniczeniem, jako `risk=caution`: mechanizm jest udokumentowany przez Microsoft (`Set-SmbClientConfiguration -EnableInsecureGuestLogons`, polityka „Enable insecure guest logons”), ale MS wprost odradza włączanie gościa. Pozycja obniża zabezpieczenia cudzego komputera, więc ostrzeżenia są dosłownie z MS. Opis z v3.2 („komputery widoczne w sieci”) nie wchodzi: MS nie łączy dostępu gościa z widocznością komputerów w LAN. Alias v3.2: `lan_godmode`. Rozbicie z v3.2: ustawienia gościa i podpisywania są osobnymi pozycjami (`net.smb-signing-client-off`); usługi odnajdywania, NetBIOS, profil sieci i strona serwera SMB nie wchodzą (brak dowodu MS, że są potrzebne).

## Co zmienia
- Rejestr (`registry[]`, zapis przez API C#): `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters`, wartość `AllowInsecureGuestAuth`, **DWord**, `1`. `scope=machine`, bez `requiresReboot` (zapis przez API rejestru jest widoczny w `Get-SmbClientConfiguration` bez restartu: potwierdził przebieg VM 2026-10-18, zob. „Test VM”). Ścieżkę i typ ustalił odczyt na VM po zmianie oficjalnym cmdletem `Set-SmbClientConfiguration -EnableInsecureGuestLogons` (MS Learn nie podaje klucza, zob. Źródła).
- Skrypt (`script.detect`): tylko odczyt, `Get-SmbClientConfiguration` przez funkcję pomocniczą `Get-DsoSmbClientSetting`, wartość logiczna `EnableInsecureGuestLogons`. Skrypt niczego nie zapisuje.
- Nie dotyka: polityk (`Software\Policies\...`), `RequireSecuritySignature`, usług odnajdywania, NetBIOS, profilu sieciowego, serwera SMB.

### Decyzja: `registry[]` + `script.detect`, a nie sam skrypt
Stan tweaka składa się z wszystkich operacji biorących udział w detekcji (`StateAggregator.Aggregate`): `Applied` tylko gdy każda daje `Match`, `NotApplied` gdy żadna, w pozostałych przypadkach `Partial`. Wpis rejestru wyraża to, co narzędzie zapisało (i co journal potrafi cofnąć), a odczyt cmdletu to stan efektywny. Zamierzony projekt: rozjazd rejestru i cmdletu daje `Partial`, a apply, po którym stan nie jest `Applied`, nie jest zgłaszany jako sukces (rejestr wycofany). Zachowanie pod faktyczną polityką (np. Group Policy) nie zostało sprawdzone: nie sprawdzono, czy `Get-SmbClientConfiguration` uwzględnia politykę; jeśli nie, pozycja może pokazać zgodność mimo polityki (CARRY: weryfikacja na VM z polityką).

## Źródła
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/enable-insecure-guest-logons-smb2-and-smb3 – MS Learn, dostęp 2026-10-18 (ms.date 2025-08-13): domyślnie klient SMB2/3 nie pozwala na gościa; włączenie: polityka „Enable insecure guest logons” albo `Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -Force`; ostrzeżenia: spoofed server bez monitu o poświadczenia, ransomware, ataki AitM, „We recommend that you don't enable insecure guest logons”, „Guest logons don't support standard security features such as SMB signing and SMB encryption”. Strona **nie podaje** nazwy ani ścieżki klucza rejestru i nie mówi, jaki jest stan domyślny Windows 11 Home.
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-signing – MS Learn, dostęp 2026-10-18: „Requiring SMB signing also disables guest access to shares. In these cases, you must disable SMB signing manually to restore access for guest accounts.”
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-security-hardening – MS Learn, dostęp 2026-10-18: Windows 11 Pro domyślnie nie pozwala na gościa; podpisywanie wymagane domyślnie od 24H2.
- https://learn.microsoft.com/en-us/powershell/module/smbshare/set-smbclientconfiguration?view=windowsserver2025-ps – MS Learn, dostęp 2026-10-18: parametr `-EnableInsecureGuestLogons` (Boolean).
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/networking/access-denied-access-smb-file-share – MS Learn, dostęp 2026-10-18 (ms.date 2026-07-09): „Do not re-enable insecure guest logons as a fix except as a temporary, risk-accepted workaround”.
- Odczyt VM 26H2 Home i Pro, 2026-10-18 (`docs/zrodla/2026-10-18-partia-c3-zrodla.md`, „Wyniki VM”, sekcja 3+4): nazwa i typ wartości w rejestrze, domyślne odczyty, brak restartu.

## Wartości domyślne na świeżym 26H2
Odczyt z VM DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), build 26300.9457, 2026-10-18:

| | Home | Pro |
|---|---|---|
| `Get-SmbClientConfiguration`: `EnableInsecureGuestLogons` | **True** | **False** |
| `Get-SmbClientConfiguration`: `RequireSecuritySignature` | True | True |
| `Parameters\AllowInsecureGuestAuth` w rejestrze | brak wartości | brak wartości |
| `Policies\Microsoft\Windows\LanmanWorkstation` | klucz nie istnieje | klucz nie istnieje |

Klucz `LanmanWorkstation\Parameters` istnieje na obu edycjach. Wniosek: wartość domyślna zależy od edycji, a nie od wartości w rejestrze (jej brak oznacza „domyślną edycji”). Na Home ustawienie `EnableInsecureGuestLogons` jest domyślnie True, na Pro False; przy wymaganym podpisywaniu (True na obu) połączenie z gościem i tak jest wg MS blokowane. Wynik MS „Home nie wymaga podpisywania” nie potwierdził się: podpisywanie jest wymagane na obu (zob. `net.smb-signing-client-off`).

`Set-SmbClientConfiguration -EnableInsecureGuestLogons $x -Force` zapisuje **DWORD `AllowInsecureGuestAuth`** (1/0) w `LanmanWorkstation\Parameters`, nie dotyka Policies, a zmiana zapisana tym cmdletem była od razu widoczna w `Get-SmbClientConfiguration` (bez restartu; zmierzono tylko zapis cmdletem). Przywrócenie cmdletem zostawia jawny DWORD tam, gdzie wcześniej wartości nie było (Home: 1, Pro: 0), dlatego cofnięcie narzędzia używa snapshotu i usuwa wartość, zamiast ustawiać ją jawnie.

## Detekcja i undo
Stan składa się z dwóch sygnałów (zob. „Decyzja”), oba logiczne, bez parsowania tekstu zależnego od języka:
1. Konfiguracja (`registry[]`): `AllowInsecureGuestAuth` = DWORD 1 → `Match`; brak wartości, 0 albo inny typ → `Mismatch`.
2. Stan efektywny (`script.detect`): `EnableInsecureGuestLogons -eq $true` → `Applied`, `$false` → `NotApplied`. Błąd cmdletu albo odpowiedź, która nie jest wartością logiczną (`$null`, tekst, liczba), kończy skrypt błędem → stan `Unknown`, nigdy `Applied`.

Wynik na świeżym systemie (nie błąd, tylko konsekwencja domyślnych edycji):
- **Pro**: rejestr `Mismatch` + cmdlet `False` → `NotApplied`; po apply `Applied`.
- **Home**: rejestr `Mismatch` (brak wartości) + cmdlet `True` → **`Partial`**. To uczciwy wynik: ustawienie `EnableInsecureGuestLogons` jest już True (domyślne Home), ale konfiguracja nie jest zapisana jawnie przez to narzędzie, a przy wymaganym podpisywaniu połączenie z gościem i tak jest blokowane. Apply zapisuje 1 i daje `Applied`; undo wraca do `Partial`.
- Polityka przesłaniająca rejestr (rejestr 1, cmdlet `False`) → `Partial` w modelu testów jednostkowych (założenie projektu, że cmdlet pokazuje wartość efektywną). Ten scenariusz nie był zmierzony na VM; zachowanie pokrywają testy jednostkowe na modelu.
- **Obserwacja z VM (eksploracja C4, sonda 7, 2026-10-18; Pro):** wartość polityki `HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation\AllowInsecureGuestAuth` (DWORD) zmienia odczyt cmdletu: `EnableInsecureGuestLogons` False przed → **True** po zapisie polityki = 1 → False po jej usunięciu; na Home True przez cały czas (domyślnie True). Polityka wpływa więc na widok cmdletu niezależnie od `Parameters\AllowInsecureGuestAuth`. Konsekwencja dla agregacji wyżej (opis, bez nowych pomiarów): przy polityce 1 i braku wartości w `Parameters` rejestr daje `Mismatch`, a cmdlet True, więc stan to `Partial` (jak na świeżym Home), a nie `Applied`.

Undo: silnik przywraca `AllowInsecureGuestAuth` ze snapshotu z journala (poprzednia wartość albo jej brak); gdy wartości nie było, usuwa ją (`<RemoveEntry>`), a klucz `Parameters` istniał przed apply i zostaje. `original` w katalogu = `<RemoveEntry>`.

**Bez wpisu w journalu** (undo z wartości domyślnych) pozycja usuwa wartość `AllowInsecureGuestAuth`, czyli wraca wartość domyślna edycji (Home: `EnableInsecureGuestLogons` True, Pro: False; to odczyty stanu fabrycznego z VM, przy braku wartości w rejestrze). Nie obniża to ochrony poniżej stanu domyślnego Windows i nie zgaduje jednej wartości dla obu edycji. Usunięcie wartości przywraca domyślną edycji: potwierdził przebieg VM 2026-10-18 (`CheckAfterUndo`, zob. „Test VM”; dowód rozróżniający tylko na Pro, bo na Home zarówno brak wartości, jak i wartość 1 dają odczyt True). Wniosek „brak wartości = domyślna edycji” opiera się na odczycie stanu fabrycznego z VM (wartości nie było, cmdlet dawał Home True, Pro False) i na tym przebiegu. **Nie sprawdzono:** zachowania apply/undo pod polityką Group Policy ani po restarcie (zmierzony został tylko wpływ polityki na odczyt cmdletu, sonda 7 wyżej).

## Ryzyka i ostrzeżenia
- `risk=caution`. Ostrzeżenia w katalogu: „Microsoft odradza tę zmianę: podstawiony serwer w sieci może przejąć połączenie bez pytania o hasło, a ruch nie jest szyfrowany.” oraz „Zwykle trzeba też zastosować „Nie wymagaj podpisywania SMB po stronie klienta” – bez tego Windows i tak zablokuje gościa.”
- Podstawa – cytaty MS: „enabling guest logons can pose a security threat” – podstawiony serwer (spoofed server) bez monitu o poświadczenia, ransomware, AitM; „We recommend that you don't enable insecure guest logons.”; „Guest logons don't support standard security features such as SMB signing and SMB encryption”.
- Na Windows 11 24H2 Pro/Enterprise/Education samo zezwolenie na gościa nie wystarcza: „Requiring SMB signing also disables guest access to shares”. Na świeżym 26H2 podpisywanie klienta jest wymagane na Home i Pro (odczyt VM), więc do połączenia z gościem potrzebna jest też `net.smb-signing-client-off`.
- Brak obietnicy widoczności komputerów w LAN. Preferowane rozwiązanie wg MS: poświadczenia.
- Na Home pozycja początkowo wyświetla `Partial` (zob. Detekcja).

## Test VM
Przebieg VM z 2026-10-18 (Home 19:58, Pro 20:10; build 26300.9457), DSO-26H2-Home (Core) i DSO-26H2-Pro (Professional), oba bez błędów (278 zaliczonych / 0 błędów / 17 pominiętych na każdej VM). Raporty lokalne: `tests/vm/out/<data>/<VM>/<RunLabel>/report.json` (katalog w `.gitignore`).

Przypadek `net-smb-guest-access` (`Flow=Full`, `Catalog=BuiltIn`, bez restartu), **Passed** na Home i Pro (Pester main 191/0/17):
- Sprawdzono (Pro): `detect-before` = `NotApplied`, `apply` = `Applied` (`requiresReboot` = false; snapshot po apply: DWORD 1), `detect-after-apply` = `Applied`, `undo` = `Undone`, `detect-after-undo` = `NotApplied`. **Home:** `detect-before` = `Partial` (rejestr bez wartości, cmdlet już True), po apply `Applied`, po undo `Partial` (zgodnie z sekcją „Detekcja i undo”). Stan końcowy = stan przed: wartość nieobecna, klucz `Parameters` zostaje (`KeyPresentAfterUndo`), `SnapshotDiff` pusty.
- `CheckAfterApply` (`(Get-SmbClientConfiguration).EnableInsecureGuestLogons -eq $true`, zaraz po apply, bez restartu): **Passed** na Home i Pro. Zapis przez API rejestru w C# jest więc widoczny w cmdlecie bez restartu. Rozstrzygający jest wynik na **Pro** (przed apply False, po apply True); na Home odczyt był True także przed apply, więc sam ten wynik na Home niczego nie dowodzi.
- `CheckAfterUndo` (odczyt cmdletu równy domyślnej edycji, edycja z `EditionID`: Home True, Pro False): **Passed** na obu. Po usunięciu wartości (undo ze snapshotu bez wartości) cmdlet wraca do domyślnej edycji. Rozstrzygający jest **Pro** (po apply True, po undo False); na Home brak rozróżnienia (True w obu stanach).
- `net-smb-lan-effect`: **Skipped** („brak sieci i serwera SMB na VM”), raportowane jako pominięte, nie zaliczone.
- **Nie testowane na VM: brak sieci i serwera SMB.** Nie sprawdzono: skutku w LAN (połączenie z gościem, widoczność komputerów), trwałości po restarcie oraz zachowania samej pozycji (apply/undo/detekcji) pod polityką Group Policy; zmierzony został tylko wpływ wartości polityki na odczyt cmdletu (zob. „Detekcja i undo”, sonda 7).

## Reddit
Do uzupełnienia w części E.
