# sec.take-ownership-menu – Dodaj „Przejmij na własność” do menu kontekstowego

## Werdykt 26H2
Portować jako `caution`: mechanizm (statyczne verby w `HKLM\SOFTWARE\Classes`, podmenu z `ExtendedSubCommandsKey`, `Extended`, `AppliesTo`) jest opisany przez MS, a **konkretną strukturę, która działa na 26H2, rozstrzygnęły VM i ręczny test autora z UAC** (niżej). Pozycja dodaje wyłącznie wpisy menu; sama niczego nie przejmuje. Aliasy v3.2: `sec_take_ownership` (dodaj) i `sec_take_ownership_remove` (usuń) trafiają w tę jedną pozycję (apply = dodaj, undo = usuń). Całość w `registry[]` (API C#), bez skryptu, bez `reg.exe`, bez klucza `runas` na najwyższym poziomie.

## Co zmienia
Pod każdą z dwóch klas powłoki: `*` (pliki) i `Directory` (foldery). Wszystko REG_SZ, ścieżki w `HKLM\SOFTWARE\Classes\<Klasa>\shell\DSOptimizer.TakeOwnership` (nie `HKCR\`, bo `HKCR` to widok łączony `HKLM\Software\Classes` i `HKCU\Software\Classes`, a tweak ma `scope=machine`):

| Klucz | Wartość | Dane |
|---|---|---|
| `...\DSOptimizer.TakeOwnership` | `MUIVerb` | `Przejmij na własność` |
| `...\DSOptimizer.TakeOwnership` | `Extended` | pusty REG_SZ (pozycja tylko z Shift) |
| `...\DSOptimizer.TakeOwnership` (tylko `Directory`) | `AppliesTo` | `NOT (System.ItemPathDisplay:="C:\Windows" OR System.ItemPathDisplay:~<"C:\Windows\" OR System.ItemPathDisplay:="C:\")` |
| `...\DSOptimizer.TakeOwnership` | `ExtendedSubCommandsKey` | `<Klasa>\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey` (ścieżka względem HKCR, np. `*\shell\DSOptimizer.TakeOwnership\ExtendedSubCommandsKey`) |
| `...\ExtendedSubCommandsKey\Shell\runas` | `MUIVerb` | `Przejmij na własność (administrator)` |
| `...\ExtendedSubCommandsKey\Shell\runas` | `HasLUAShield` | pusty REG_SZ (tarcza UAC, tylko wizualna) |
| `...\ExtendedSubCommandsKey\Shell\runas\command` | (domyślna) | plik (`*`): `cmd.exe /c takeown /f "%1" /a && icacls "%1" /grant *S-1-5-32-544:F /c /l /q` |
| | | folder (`Directory`): `cmd.exe /c takeown /f "%1" /a /r /d Y && icacls "%1" /grant *S-1-5-32-544:F /t /c /l /q` |

- Grupa Administratorzy zawsze przez SID `S-1-5-32-544` (`*S-1-5-32-544`, gwiazdka oznacza SID w `icacls`), nigdy przez nazwę (polski Windows: „Administratorzy”).
- `runas` występuje **tylko** w podmenu własnego klucza; na najwyższym poziomie `shell\runas` nie jest ani tworzony, ani dotykany.
- `AppliesTo` jest **tylko na `Directory`**. Na `*` go nie ma, bo wykluczenie plików pod `C:\Windows` nie było testowane na VM (patrz ostrzeżenia).
- Nazwa menu (`MUIVerb`) jest w języku programu w chwili zastosowania: po polsku `Przejmij na własność` i `Przejmij na własność (administrator)` (jak w tabeli), po angielsku `Take ownership` i `Take ownership (administrator)` (pole `localized` tych 4 wpisów rejestru; tekst z tabeli tłumaczeń katalogu). Zapis przez API rejestru (Unicode), bez `reg.exe` i bez argv.
- Co robi komenda: `takeown /a` daje własność grupie Administratorzy (a nie bieżącemu użytkownikowi), `icacls /grant *S-1-5-32-544:F` dodaje tej grupie pełną kontrolę; foldery: `/r /d Y` i `/t` obejmują całe drzewo, `/c` kontynuuje po błędach, `/l` działa na dowiązaniu, a nie na jego celu, `/q` wycisza komunikaty.

## Źródła
- MS Learn, „Creating Shortcut Menu Handlers” (ms.date 2018-05-31, aktualizacja strony 2025-07-03; dostęp 2026-10-18): https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers
  - statyczny verb `<ProgID>\shell\<verb>` z podkluczem `command`; `Extended` (REG_SZ bez danych, verb tylko z Shift); `MUIVerb`; `HasLUAShield`; `AppliesTo` (AQS, tylko „fast properties”); menu kaskadowe z rejestru.
  - **Niespójność źródła MS:** krok 3 opisu menu kaskadowego mówi „add an `ExtendedSubCommandsKey` subkey” (sam podklucz), a we wstępie jest mowa o ponownym użyciu zestawów verbów przez „specifying the same `ExtendedSubCommandsKey`” (wartość wskazująca ścieżkę). **Rozstrzygnęła VM** (test ręczny, niżej): wartość `ExtendedSubCommandsKey` **plus** podklucz działa, a sam podklucz (dosłowny krok 3) nie tworzy podmenu.
  - MS nie twierdzi, że verb `runas` w podmenu podnosi uprawnienia; to wynik testu na VM.
- MS Learn, `ShellExecuteW`, verb `runas`: „Launches an application as Administrator. User Account Control (UAC) will prompt the user for consent”: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecutew
- MS Learn, „Merged View of HKEY_CLASSES_ROOT”: https://learn.microsoft.com/en-us/windows/win32/sysinfo/merged-view-of-hkey-classes-root
- MS Learn, `takeown` (ms.date 2025-11-04): `/a` – własność dla grupy Administratorzy; `/r`; `/d {Y|N}` tylko z `/r`: „Y – … The directory permissions are replaced with permission granting the user full control only”: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/takeown
- MS Learn, `icacls` (ms.date 2025-06-06): SID numeryczny z gwiazdką; `/grant`, `/t`, `/c`, `/l`, `/q`: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/icacls
- MS Learn, Advanced Query Syntax i `System.ItemPathDisplay` (do `AppliesTo`): https://learn.microsoft.com/en-us/windows/win32/search/-search-3x-advancedquerysyntax, https://learn.microsoft.com/en-us/windows/win32/properties/props-system-itempathdisplay
- Research partii C4: `docs/zrodla/2026-10-18-partia-c4-zrodla.md`, sekcja 3, „Wyniki VM” i „Test ręczny UAC (autor, 2026-10-18/19)”. Surowe wyniki: poza gitem.

### Wyniki VM (eksploracja C4, VM `DSO-26H2-Home` i `DSO-26H2-Pro`, build 26300.9457, pl-PL)
- **(3) kolizje (2026-10-18):** w `HKLM\SOFTWARE\Classes\{*,Directory,Drive,Folder,AllFileSystemObjects}\shell` nie ma `runas` ani `DSOptimizer.TakeOwnership` (Home i Pro). Na `*` są tylko `pintohomefile`, `removeproperties`, `UpdateEncryptionSettingsWork`; na `Directory`: `cmd`, `find`, `Powershell`, `UpdateEncryptionSettings`.
- **(4a) widoczność (przebieg 2, 2026-10-18, `Shell.Application` `Verbs()`):** `Extended` ukrywa pozycję w `Verbs()` (zachowuje się jak menu bez Shift), a `Verbs()` nie rozwija menu kaskadowych; wpis podrzędny z `ExtendedSubCommandsKey` nigdy nie pojawia się na liście. **Dlatego widoczności i działania podmenu nie da się sprawdzić przez `Verbs()` i rozstrzygnął ją ręczny test w Explorerze.** Korzeń `C:\`: rejestracja pod `*` i `Directory` **nie** daje pozycji na `C:\` (pojawia się tylko przy `Drive` lub `Folder`).
- **(4b) `AppliesTo` (przebieg 2, 2026-10-18, tylko `Directory`):** kontrola pozytywna (`System.ItemName:="folder testowy"`) działa. Warianty A i C (`NOT ...:="C:\Windows"`) wykluczają tylko sam `C:\Windows`. **Wariant B** (ten w tabeli wyżej) wykluczył `C:\Windows`, `System32`, `C:\Windows\Temp` i `C:\`, a zostawił folder testowy. Ograniczenia: ścieżka `C:\Windows` jest stała, porównanie tekstowe; wynik dotyczy folderów (`Directory`), pliki pod `C:\Windows` nie były filtrowane przez AQS.
- **(5) rekurencja (eksploracja C4, 2026-10-18):** `takeown /f <folder> /a /r /d Y` na polskim 26H2: exit 0, bez monitu, objął folder, plik, „podfolder bez list” (`Deny ListDirectory`) i plik w nim (Home i Pro). `icacls /setowner ... /t /c /l` zostawił plik w folderze bez List Folder przy poprzednim właścicielu (exit 0, ale „Odmowa dostępu”), więc do rekurencji wybrano `takeown`.
- **(6) nazwy specjalne (przebieg 2, 2026-10-18):** spacje, polskie litery, `&`, `^`, `!`: exit 0, właściciel `S-1-5-32-544` i jawna pełna kontrola Administratorów na celu (plik albo folder i jego dziecko), bez zmian czegokolwiek innego. Nazwy z `%` (także mieszane): **exit 1, nic się nie zmienia** (`takeown`: „File or Directory not found”; mechanizm: `cmd.exe` rozwija `%...%` także w cudzysłowie – wniosek, nie odczyt).
- **Test ręczny UAC (autor, VM Pro, 2026-10-18/19; wyniki w `c4-manual-uac-{prepare,finish}.json`):**
  - Wariant **sam podklucz** `ExtendedSubCommandsKey\Shell\runas` (dosłownie krok 3 MS): Explorer pokazał pozycję **bez strzałki podmenu**; kliknięcie dało komunikat „Z tym plikiem nie jest skojarzona aplikacja…”; nic się nie zmieniło (właściciel nadal `dso`). **Nie do użycia.**
  - Wariant **wartość `ExtendedSubCommandsKey` + podklucz** z `Extended`, **bez `AppliesTo`** i z etykietami „DSO: …” (zakres testu: tylko ta kombinacja, rejestrowana pod `*` i `Directory`): strzałka podmenu widoczna, po wyborze wpisu monit UAC, po zgodzie właściciel `S-1-5-32-544` na pliku, folderze, pliku w folderze, „podfolderze bez list” i pliku w nim. Jawne `(A;;FA;;;BA)` mają plik, folder i plik w folderze; „podfolder bez list” ma ACL `PAI(A;OI;FA;;;BA)(A;CI;FA;;;BA)` (patrz punkt „Podmiana ACL” niżej), a plik w nim `(A;;FA;;;BA)(A;ID;FA;;;BA)`. **Ta struktura jest podstawą wpisu do katalogu.**
  - **Zakres potwierdzeń:** `AppliesTo` (wariant B) sprawdzono sondą (4b) w `Verbs()`, na strukturach Plain i Subkey, bez `Extended`. Dokładnie wysyłana kombinacja (wartość + podklucz + `Extended` + `AppliesTo` na `Directory` + polskie etykiety) została potwierdzona jednym kliknięciem end-to-end na prawdziwej pozycji katalogu (2026-10-19). **Potwierdzone klikiem autora (VM Pro, sesja `dso`, po `--apply sec.take-ownership-menu` = `Applied`, 2026-10-19):** A1 plik w `C:\r14` → podmenu i monit UAC: tak; A2 folder: tak; A3 `C:\Windows` i `C:\Windows\System32` (foldery): pozycji brak, zgodnie z wykluczeniem `AppliesTo`; A4 plik w `C:\Windows`: pozycja jest (znane ograniczenie: wykluczenie dotyczy tylko folderów). Zgodne z opisem w tym dokumencie.

## Wartości domyślne na świeżym 26H2
Odczyt z VM Home i Pro (2026-10-18): żaden z kluczy `...\shell\DSOptimizer.TakeOwnership` (ani `...\shell\runas`) nie istnieje, więc `original` każdej wartości = `<RemoveEntry>` (brak). Klucze `HKLM\SOFTWARE\Classes\*\shell` i `...\Directory\shell` istnieją na obu VM (patrz (3)).

## Detekcja i undo
- Detekcja: wszystkie wartości z `registry[]` równe docelowym -> `Applied`; brak -> `NotApplied`; część -> `Partial`. To uczciwe, bo pozycja obiecuje tylko wpisy menu; czy menu faktycznie się wyświetla i czy komenda zadziała, nie jest wykrywane (zależy od Explorera i UAC).
- Nazwa menu (`MUIVerb`) jest zapisywana w języku programu w chwili zastosowania, a detekcja uznaje obie wersje (polską i angielską, typ REG_SZ, dokładny tekst): menu zastosowane po polsku zostaje `Applied` po przełączeniu programu na angielski i odwrotnie. Inny tekst albo inny typ wartości = niezgodność. Cofnięcie usuwa wpisy jak dotąd (przywraca snapshot, niezależnie od języka).
- Undo z journala przywraca snapshot (brak wartości = usunięcie). Reguła A1 usuwa **puste klucze utworzone przez Apply** (od najgłębszego: `command`, `runas`, `Shell`, `ExtendedSubCommandsKey`, `DSOptimizer.TakeOwnership`), nigdy poniżej 2 segmentów pod hive. Klucze `*\shell` i `Directory\shell` istniały wcześniej i zostają.
- **Cudzych kluczy undo nie dotyka:** istniejący `*\shell\runas` (albo inny verb) przed apply jest ten sam po undo (test silnika na fake'ach). Gdy ktoś dopisał wartość do naszego klucza, klucz zostaje, a undo zgłasza ostrzeżenie „Klucz … pozostał po cofnięciu (nie jest pusty)”.
- Undo **nie cofa** zmian właściciela ani uprawnień zrobionych z menu: usuwa tylko pozycję menu.
- Aliasy `sec_take_ownership` i `sec_take_ownership_remove` wskazują tę samą pozycję; „usuń” w v3.2 = undo tej pozycji.

## Ryzyka i ostrzeżenia
- `risk=caution`. Ostrzeżenia w katalogu: „Zdejmuje ochronę pliku: pełny dostęp dla administratorów. Na plikach systemowych i w WindowsApps może zepsuć aktualizacje i aplikacje.” oraz „Cofnięcie usuwa tylko wpis z menu; zmienionych właścicieli i uprawnień plików nie przywraca.”
- Szczegóły ryzyk (część w szczegółach technicznych programu, część tylko tutaj):
  - Zmiana właściciela plików systemowych może zepsuć aktualizacje i Windows Resource Protection; stanu TrustedInstaller nie da się łatwo przywrócić.
  - Cofnij usuwa tylko menu i **nie cofa** zmian właściciela ani uprawnień.
  - **Podmiana ACL:** na folderze, którego administrator nie może wylistować, `takeown /r /d Y` **zastępuje całą listę ACL** (VM: po komendzie SDDL „podfolderu bez list” to `O:BAG:S-1-5-21-…-513D:PAI(A;OI;FA;;;BA)(A;CI;FA;;;BA)`: tylko Administratorzy, dziedziczenie wyłączone; SYSTEM i Users utracili dostęp). Przejęcie takich folderów, np. `C:\Program Files\WindowsApps`, może zepsuć aplikacje.
  - Rekurencja na dużym drzewie trwa długo.
  - Pozycja widoczna tylko z Shift w klasycznym menu („Pokaż więcej opcji”) i wymaga zgody w UAC.
  - **Ścieżka `C:\Windows` jest wpisana na stałe** w wykluczeniu (`AppliesTo`): Windows na innej literze dysku nie jest wykluczony. Wykluczenie działa tylko dla folderów; **pliki w `C:\Windows` nie są wykluczone** (nie testowano na VM).
  - **Znak `%`:** nazwy zawierające `%` kończą się błędem (exit 1) i nic nie zmieniają, bo `cmd.exe` rozwija `%...%`. Spacje, polskie litery i `& ^ !` działają.
  - Korzeń dysku (`C:\`) nie dostaje pozycji, bo wpisy są pod `*` i `Directory`, a nie pod `Drive` (VM, (4a)).
- Bez obietnic bezpieczeństwa ani wydajności.

## Test VM
Pełny przebieg VM `vm-run-20261019-002807` (2026-10-19, ok. 00:28–00:46, build 26300.9457, HEAD `d589405`), Home i Pro. Raporty lokalne poza gitem (`tests/vm/out/...`).

- `sec-take-ownership-menu` (`Flow=Full`, `Catalog=BuiltIn`, `Snapshot` wszystkich wartości, `KeyAbsentAfterUndo` obu kluczy `DSOptimizer.TakeOwnership`, wartownik `KeyPresentAfterUndo` pod `*\shell`): **Passed** na Home i Pro. Sprawdza strukturę rejestru (apply, detekcja `Applied`, undo, stan końcowy = przed, obce `*\shell\runas` nietknięte), bo `Extended` ukrywa pozycję w `Verbs()`, a `Verbs()` nie rozwija podmenu.
- `sec-take-ownership-menu-effect`: **Skipped** na Home i Pro (powód z `cases.psd1`: wymaga kliknięcia w klasycznym menu Explorera (Shift) i zgody w UAC; PowerShell Direct działa jako Administrator z pełnym tokenem bez UAC, a `Verbs()` nie pokazuje pozycji z `Extended` ani podmenu); raportowane jako pominięte, nie zaliczone. Działanie z menu (UAC) potwierdził ręczny test autora (wyżej; wariant wartość + podklucz + `Extended`, 2026-10-18/19).
- Podsumowanie przebiegu: Home 309 zaliczonych / 0 błędów / 21 pominiętych, Pro 308 / 1 / 21 (jedyny błąd: stary `Check` `sec-bitlocker-decrypt-c`, poprawiony w `962ae0e`). Pominięcia z wcześniejszych partii: `disk-hibernation`, `net-smb-lan-effect`, `net-network-private-change`.
- **Potwierdzone klikiem autora (VM Pro, sesja `dso`, po `--apply sec.take-ownership-menu` = `Applied`, 2026-10-19):** A1 plik w `C:\r14` → podmenu i monit UAC: tak; A2 folder: tak; A3 `C:\Windows` i `C:\Windows\System32` (foldery): pozycji brak, zgodnie z wykluczeniem `AppliesTo`; A4 plik w `C:\Windows`: pozycja jest (znane ograniczenie: wykluczenie dotyczy tylko folderów). Zgodne z opisem w tym dokumencie. Nadal nie potwierdzone: `takeown /d Y` sprawdzone na polskim 26H2 (pl-PL), na innych językach nie sprawdzano; nie testowano nazw z `%` w menu ani rekurencji na dużych drzewach.
- `sec-take-ownership-menu-en` (2026-10-29, przebieg `tests/vm/out/20261029-083728`, build 26300.9457, Home i Pro; tylko ten przypadek i `sec-take-ownership-menu`): ten sam przebieg co `sec-take-ownership-menu` (`Flow=Full`, `Catalog=BuiltIn`, ten sam `Setup` i `Snapshot`), ale CLI z `--lang en`. **Passed** na Home i Pro (oba przypadki; po 28 zaliczonych, 0 błędów, 0 pominiętych na VM): apply zapisał `MUIVerb` = `Take ownership` (`*` i `Directory`) i `Take ownership (administrator)` (oba `...\runas`), REG_SZ – niezależny odczyt `CheckAfterApply` i `AfterApplySnapshot`; detekcja po apply `Applied`, undo usunął oba klucze `DSOptimizer.TakeOwnership`, wartownik `*\shell\runas` został, stan końcowy = przed.
