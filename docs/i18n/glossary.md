# DSOptimizer glossary (Polish → English)

This is the single source of truth for terms in the English interface of DSOptimizer. Polish stays the source
language: every English text is a translation of a Polish original, and the same Polish text always gets the same
English text everywhere in the program. When you add a translation that uses a term not listed here, add the term
in the same change.

How to read the tables:

- Terms are shown as they appear in the program. Values like `{0}` or `N` are placeholders.
- `code spans` mark texts whose leading or trailing spaces matter – keep them exactly.
- Entries marked **do audytu (Etap 5)** are uncertain and must be checked in the translation audit before release.
- Windows names link to English Microsoft documentation (Microsoft Learn or Microsoft Support) as evidence.

## Style rules

1. **Plain English for everyday users.** Common IT words are fine (restore point, driver, debloat). Avoid trade
   abbreviations in the interface (write "restore point", not "RP"; "Group Policy", not "GPO"). Tool and file names
   that the user sees in Windows stay as they are (winget, SFC, DISM, MSI, SHA256, BitLocker).
2. **Sentence case** in titles, labels, buttons, menu items and column headers: only the first word and proper
   nouns are capitalized ("Change history", "Save report", "Restore point"). Windows feature names keep their own
   capitalization (Device Manager, System Protection).
   Source: [Microsoft Style Guide – Capitalization](https://learn.microsoft.com/en-us/style-guide/capitalization).
3. **No "please".** Messages state what happened and what to do: "Try again in a moment.", not "Please try again".
   Source: [Microsoft Style Guide – please](https://learn.microsoft.com/en-us/style-guide/a-z-word-list-term-collections/p/please).
4. **Impersonal, short failure messages with contractions:** "Couldn't save the selected language: {0}", not
   "We couldn't…" and not "Failed to…". Use contractions consistently and never mix them with the spelled-out
   form in the interface (always "couldn't", "can't", "isn't", "doesn't").
   Source: [Microsoft Style Guide – Use contractions](https://learn.microsoft.com/en-us/style-guide/word-choice/use-contractions).
5. **Dashes and punctuation follow the Polish original.** Use the en dash with spaces " – " exactly where the
   Polish text has it, and nowhere else. Leading and trailing whitespace of the English text must match the Polish
   text character for character (the translation table check compares them). Keep "…" (one character) where Polish
   has it. Keep the "›" path separator in "Settings › Windows Update" paths and "→" in "Repairs → Repair winget".
6. **Quotation marks.** Polish „…” becomes straight double quotes "…" in English (same as the existing command-line
   texts).
7. **Spelling: US English** (Minimize, color, optimize), matching Windows names and Microsoft documentation. Dates and
   numbers use the British format (28/10/2026, decimal point) – that is handled by the program's culture setting,
   not by the translation.
8. **Numbers and plurals:** "0 items", "1 item", "2 items" – the number always comes first, the noun is plural for
   everything except 1.
9. **Windows names exactly as in English Windows 11.** Check the evidence column below; do not invent new names for
   Windows features.
10. **"Computer", not "PC" or "device"**, in the program's own sentences ("Restart the computer").
11. **"Sign in" / "signed-in account"**, not "log in" / "logged-in", for Windows accounts.
12. **Checkboxes:** "checkbox" (one word); "select" and "clear" for checking and unchecking.
    Source: [Microsoft Style Guide – check, checkbox, check mark](https://learn.microsoft.com/en-us/style-guide/a-z-word-list-term-collections/c/check-checkbox-check-mark).

## Key decisions

- **"pozycja" = item.** One entry in a list: a setting, an action, a panel, or an app on the install list. "Item" is
  neutral for all kinds, plain for everyday users and already used in the English README. "Tweak" stays a technical
  word (`kind=tweak` in the catalog and command-line help); the interface does not use it, because the Polish
  interface does not use "tweak" either – it says "ustawienie" (setting).
- **"seria" = batch.** Several selected items run one after another from one "Apply (N)", "Undo (N)", "Remove (N)"
  or "Install (N)" click. "Batch" is plain and short; "queue" suggests waiting, "series" sounds unnatural.
- **"Co zyskasz" = What you gain.** Matches the README ("what it changes and what you gain") and keeps the friendly,
  second-person tone of the Polish heading. "Benefits" was rejected as marketing-like.
- **"Szczegóły techniczne" = Technical details.**
- **"Wymagany restart." = Restart required.** "Restart" (not "reboot") as in Windows.
- **"Uruchom ponownie teraz" = Restart now** – one shared text for restarting the program (language change) and the
  computer (restart banner). It is already used for the program restart in the language settings.
- **States vs results.** The state of an item ("Zastosowane") and the result of an operation ("Zastosowano") both
  become "Applied" – English has no such distinction and both read correctly in context.
- **"Dziennik" = Log.** The on-screen log panel; the file on disk is the "log file".
- **"Ustawienia" = Settings** both for the program's own page and for the Windows Settings app (same Polish word,
  one English word). Where it is unclear, the sentence names the app: "Open Settings › Windows Update."

## Pages and menu

| Polski | English | Notes/source |
|---|---|---|
| Prywatność i AI | Privacy and AI | Category; English README |
| Wydajność | Performance | Category; English README |
| Dysk i system plików | Disk and file system | Category; English README |
| Sieć i LAN | Network and LAN | Category; LAN is a common word, kept |
| Bezpieczeństwo i szyfrowanie | Security and encryption | Category; English README |
| Explorer i wygoda | Explorer and convenience | Category; English README. Short form of File Explorer, kept as in Polish |
| Panele systemowe | System panels | Category; English README |
| Naprawy | Repairs | Category and log component; English README |
| Narzędzia | Tools | Category; English README |
| Specyfikacja | Specification | Page and log component; English README |
| Przygotowanie | Preparation | Page and log component; English README. Not the same text as "Przygotowanie…" (see Concepts) |
| Aplikacje | Apps | Page; English README |
| Instalacja | Install | Page and log component; English README |
| Historia zmian | Change history | Page; English README |
| Ustawienia | Settings | Program page and log component; also the section headers of the item list (see "Akcje" in Concepts); also the Windows Settings app (see Windows names) |
| Kategorie | Categories | Menu heading |
| Wyniki wyszukiwania | Search results | Page title during search |
| Licencje komponentów | Component licenses | Window title |
| Wygląd i język | Appearance and language | Settings section |
| Zmiany w systemie | System changes | Settings section |
| Motyw | Theme | Settings |
| Jasny / Ciemny / Systemowy | Light / Dark / System | Theme options |
| Język | Language | Already translated (language settings) |
| Uruchomienie | Startup | Log component name |
| Seria | Batch | Log component name; see Key decisions |

## Buttons and commands

| Polski | English | Notes/source |
|---|---|---|
| Zastosuj | Apply | Operation name; also inside sentences in quotes: "Apply" |
| Cofnij | Undo | Operation name |
| Usuń | Remove | Operation name (apps) |
| Zainstaluj | Install | Operation name |
| Uruchom | Run | Runs an action or opens a panel |
| Zastosuj (N) / Cofnij (N) / Usuń (N) / Zainstaluj (N) | Apply (N) / Undo (N) / Remove (N) / Install (N) | Batch buttons; count in brackets, no change |
| Zastosuj zaznaczone / Cofnij zaznaczone | Apply selected / Undo selected | |
| Usuń zaznaczone / Zainstaluj zaznaczone | Remove selected / Install selected | |
| Uruchom ponownie teraz | Restart now | Shared for program and computer restart; already translated |
| Uruchom ponownie | Restart | |
| Zatrzymaj po tym kroku | Stop after this step | Long repair window |
| Zatrzymam po bieżącym kroku… | Stopping after the current step… | |
| Zatrzymaj po tej aplikacji | Stop after this app | Install batch |
| Zatrzymam po bieżącej aplikacji… | Stopping after the current app… | |
| Zatrzymaj / Zatrzymywanie… | Stop / Stopping… | |
| Anuluj | Cancel | |
| Zamknij | Close | |
| Minimalizuj | Minimize | US spelling (Style rule 7) |
| Maksymalizuj / Przywróć | Maximize / Restore | Window caption buttons (tooltip and screen reader name). **do audytu (Etap 5)** – Windows 11 itself says "Restore Down" |
| Kopiuj ścieżkę / Kopiuj cały dziennik / Otwórz lokalizację pliku | Copy path / Copy entire log / Open file location | Log panel context menu and link buttons |
| Wyczyść (Esc) / Wyczyść wyszukiwanie | Clear (Esc) / Clear search | Search box clear button (tooltip / screen reader name) |
| Kopiuj / Kopiuj wszystko / Kopiuj wynik | Copy / Copy all / Copy result | |
| Zapisz raport | Save report | Specification page |
| Odśwież / Odśwież stan / Odśwież listę | Refresh / Refresh status / Refresh list | |
| Odznacz wszystko | Clear selection | "Clear" per Style rule 12 |
| Pokaż wszystkie / Pokaż które | Show all / Show which | |
| Tak, kontynuuj | Yes, continue | |
| Szukaj / Szukaj też w winget | Search / Also search winget | |
| Przejdź do: {0} | Go to: {0} | E.g. "Go to: Apps", "Go to: Repair winget" |
| Otwórz w Eksploratorze | Open in File Explorer | |
| Otwórz Windows Update | Open Windows Update | Prep page link |
| Otwórz aktualizacje opcjonalne | Open optional updates | Prep page link; lower case "optional updates" mid-phrase |

## States and results

| Polski | English | Notes/source |
|---|---|---|
| Zastosowane | Applied | Item state (`Applied`) |
| Niezastosowane | Not applied | Item state (`NotApplied`) |
| Częściowo | Partially applied | Item state (`Partial`); "Partial" alone is unclear in a list |
| Nieobsługiwane w tym systemie | Not supported on this system | State and result (`NotSupported`) |
| Nieznany (błąd detekcji) | Unknown (check failed) | Item state (`Unknown`); "check" instead of "detection" (plain English) |
| Stan nie został jeszcze sprawdzony / Sprawdzanie stanu… | Status not checked yet / Checking status… | |
| Stan | Status | Column and detail label |
| Zastosowano | Applied | Result (`Applied`); see Key decisions |
| Już zastosowane | Already applied | Result (`AlreadyApplied`) |
| Zastosowano – wymaga restartu | Applied – restart required | Result (`PendingReboot`) |
| Błąd | Error | Result (`Failed`) and log level; one Polish word = one English word |
| Błąd – zmiany wycofane | Error – changes rolled back | Result (`FailedRolledBack`) |
| Błąd – wycofanie niepełne | Error – rollback incomplete | Result (`FailedRollbackIncomplete`) |
| Cofnięto | Undone | Result (`Undone`) |
| Cofnięcie niepełne | Undo incomplete | Result (`UndoIncomplete`) |
| Cofnięto do wartości domyślnych | Undone to defaults | Result (`UndoneFromDefaults`) |
| Odmowa | Refused | Result (`Refused`): the program refused to run the step (safety check) |
| Wykonano | Done | Result (`Executed`) for actions and panels |
| Pominięto | Skipped | Result (`Skipped`) |
| Usunięto | Removed | Result (`Removed`) |
| Zainstalowano / Już zainstalowane | Installed / Already installed | Results (`Installed`, `AlreadyInstalled`) |
| Zainstalowano (zgłoszone przez winget) | Installed (reported by winget) | Result (`InstalledUnverified`) |
| Nie potwierdzono | Not confirmed | Result (`NotConfirmed`); also as a prefix: `Nie potwierdzono – ` → `Not confirmed – ` |
| Nie wykonano | Not done | History state and prefix of install failures: `Nie wykonano – ` → `Not done – `. **do audytu (Etap 5)** – check that it reads well before every reason |
| Aktywna / Cofnięta / Przerwana | Active / Undone / Interrupted | Change history row states |
| Usunięta / Zainstalowana | Removed / Installed | Change history row states (apps) |
| Czeka na restart / Czeka na ponowne uruchomienie. | Waiting for restart / Waiting for a restart. | |
| W toku / w toku | In progress / in progress | |
| Zakończono / Zakończono z błędem | Finished / Finished with an error | |
| bezpieczne / ostrożnie / eksperymentalne | safe / caution / experimental | Risk levels, lower case because they are inserted after "Risk: " |
| Ryzyko / Ryzyko: {0} | Risk / Risk: {0} | Column header, tooltip |
| Wszystkie / Bez eksperymentalnych / Tylko bezpieczne | All / No experimental / Safe only | Risk filter |
| [Błąd] / [Ostrzeżenie] / [Info] | [Error] / [Warning] / [Info] | Log line levels |
| Zastosowane i sprawdzone / Cofnięte i sprawdzone / Usunięte i sprawdzone | Applied and verified / Undone and verified / Removed and verified | Batch step status; "sprawdzone/sprawdzono" = verified (read back after the change) |
| Było już zastosowane / Była już zainstalowana / Nie było już na komputerze | Was already applied / Was already installed / Was no longer on the computer | Batch step status |
| Pominięte / Pominięte – zatrzymano / zatrzymano | Skipped / Skipped – stopped / stopped | Batch step status; "Zatrzymano" (title) = Stopped |
| Utworzony / Nie powstał | Created / Not created | Restore point step status |
| Nie udało się | Failed | App removal result status (`Failed`; the removal page says this instead of "Error"); also as a prefix: `Nie udało się – ` → `Failed – ` |
| Niemożliwe | Not possible | "Undo" fact and detail line for app removal (removal can't be undone) |
| Zastosowanie: {0} / Cofnięcie: {0} | Apply: {0} / Undo: {0} | Change history event with a result name |

## Concepts

| Polski | English | Notes/source |
|---|---|---|
| pozycja | item | See Key decisions. "1 item", "2 items" |
| ustawienie (rodzaj pozycji) | setting | Item kind `tweak`; "0 of 13 settings applied". "Tweak" not used in the interface |
| akcja | action | Item kind `action` |
| kategoria | category | **Warning:** the key "{0} kategorii" is today the locative singular ("w 1 kategorii" → "in 1 category", search summary). Never bind the genitive plural ("5 kategorii" → "5 categories") to the same key – use a separate sentence |
| panel | panel | Item kind `panel` |
| aplikacja | app | Windows 11 uses "app"; "34 apps" |
| program (rodzaj aplikacji) | Program | Desktop app kind next to "Store" |
| Sklep / Aplikacja ze Sklepu | Store / Store app | Short and long app kind; in sentences "from Microsoft Store" |
| seria | batch | See Key decisions. "Anulowano serię" → "Batch canceled" |
| zmiana / aktywna zmiana | change / active change | "3 active changes" |
| Co zyskasz | What you gain | See Key decisions |
| Co zmienia | What it changes | Detail panel heading |
| Szczegóły / Szczegóły techniczne / Ukryj szczegóły techniczne | Details / Technical details / Hide technical details | |
| Dziennik | Log | On-screen log panel; see Key decisions |
| plik logu | log file | |
| Wysokość dziennika | Log height | Settings |
| Sugerowane / Sugerowane do usunięcia | Suggested / Suggested for removal | |
| Chronione / Pozostałe | Protected / Other | App groups |
| Wymagany restart. | Restart required. | Banner title |
| `Wymagany restart. ` | `Restart required. ` | Trailing space kept (sentence continues) |
| Restart po zmianie | Restart after change | Detail label; values "Required" / "Not required" / "Not needed" (Wymagany / Niewymagany / Niepotrzebny) |
| Zadziała po ponownym uruchomieniu. | Takes effect after a restart. | Pattern for "zadziała (dopiero) po ponownym uruchomieniu komputera" → "takes effect (only) after the computer restarts" |
| Komputer uruchomi się ponownie. Zapisz pracę w otwartych programach. | The computer will restart. Save your work in open programs. | Restart confirmation |
| Przygotowanie… | Preparing… | Progress stage, not the page name |
| Krok {0} z {1} / Dalej: {0} | Step {0} of {1} / Next: {0} | Long repair window |
| krok {0} z {1} / Aplikacja {0} z {1} | step {0} of {1} / App {0} of {1} | Batch window subtitle (lower case "step" as in Polish) |
| W serii / Nie w serii | In the batch / Not in the batch | Detail panel line |
| Przebieg | Events | Change history detail section: the journal events of one entry |
| Polecenie / Gdzie / Zmiana | Command / Where / Change | Technical details labels in the confirmation window; "{0} na {1}" = "{0} to {1}" |
| Cofanie | Undo | Confirmation window fact name (whether the change can be undone) |
| Wymagany dla {N} pozycji / Niepotrzebny | Required for {N} items / Not needed | Confirmation window "Restart" fact |
| Ukryte przez filtr: {0} | Hidden by the filter: {0} | Note under the list |
| Skopiowano adres / Skopiowano ścieżkę | Address copied / Path copied | Log lines after copying to the clipboard |
| Nieoczekiwany błąd | Unexpected error | Log and result messages |
| journal | journal | The file behind Change history that keeps the state from before each change; a technical word kept as in the Polish engine messages ("Couldn't read the journal: {0}"). **do audytu (Etap 5)** |
| usługa / typ startu | service / startup type | Windows service in engine messages and the change preview ("Service SysMain", "startup type"), as in Services |
| Anulowano | Canceled | US spelling, as in "Batch canceled" |
| detekcja (stanu) | status check | "Detekcja … przerwana" → "Status check of … was interrupted"; same word as in "Unknown (check failed)" |
| Zaznaczono {N} pozycji | {N} items selected | Selection bar; plural rule 8 |
| Kliknij dwukrotnie wiersz albo pole wyboru, … | Double-click a row or checkbox, … | "pole wyboru" = checkbox |
| Nie udało się … | Couldn't … | Style rule 4 |
| nic nie zmieniono | nothing was changed | Common ending after " – " |
| Trwa inna operacja – spróbuj za chwilę. | Another operation is in progress – try again in a moment. | |
| Brak wyników / Nic nie pasuje do „{0}”. | No results / Nothing matches "{0}". | |
| konto / zalogowane konto / wszystkie konta | account / signed-in account / all accounts | Style rule 11 |
| cały komputer | whole computer | Change history "Account" column |
| obraz systemu | system image | Store apps provisioned for new accounts. **do audytu (Etap 5)** |
| deinstalator / instalator | uninstaller / installer | "manufacturer's uninstaller" |
| cichy deinstalator / deinstalator producenta | silent uninstaller / manufacturer's uninstaller | Program removal mode (no window / the program's own window) |
| wersja próbna | trial version | App group detail: "Suggested for removal (trial version)" |
| Odczyt listy: {0}, {1} | List read: {0}, {1} | Apps page subtitle: "List read: 14:05, 21 apps" |
| wydawca / producent / producent komputera | publisher / manufacturer / computer manufacturer | |
| sterownik / sterowniki chipsetu / karta graficzna | driver / chipset drivers / graphics card | "Devices without a driver" (README) |
| Numer seryjny / Producent i model | Serial number / Manufacturer and model | Specification and Prep |
| Maszyna wirtualna | Virtual machine | |
| źródło winget | winget source | "the official winget source (Microsoft)" |
| Zdalna pomoc | Remote support | Install group (RustDesk). **do audytu (Etap 5)** – not to be confused with Windows Quick Assist |
| Przeglądarki | Browsers | Install group |
| Komunikatory / Multimedia / Dokumenty / Gry / Środowiska uruchomieniowe | Messaging / Multimedia / Documents / Games / Runtimes | Install groups (with Browsers, Tools, Remote support) |
| instalacja (proces) | installation | "The previous installation is still running"; the page and log component stay "Install" |
| Pobieranie / Instalowanie / Sprawdzanie instalacji | Downloading / Installing / Verifying the installation | Install batch stages; "Pobieranie (z GitHuba)" = "Downloading (from GitHub)", "Sprawdzanie podpisu" = "Checking the signature" |
| Identyfikator winget | winget ID | Confirmation window fact; "nieprawidłowy identyfikator winget" = "invalid winget ID" |
| `Log winget: ` / `Log MSI: ` | `winget log: ` / `MSI log: ` | Log path marker after a result ("… Code 0x8A150010. winget log: C:\…"); the program recognizes both the English and the Polish marker |
| podpis wydawcy / skrót SHA256 | publisher signature / SHA256 hash | RustDesk download check ("no publisher signature", "SHA256 hash doesn't match GitHub") |
| Repozytorium GitHub / GitHub RustDesk | GitHub repository / RustDesk's GitHub | RustDesk source in the confirmation window and selection bar |
| Wymaga internetu. | Requires an internet connection. | Install selection bar and batch warning |
| Instalacja aplikacji / Usunięcie aplikacji | App install / App removal | Change history kinds |
| Działa w edycjach: {0}. | Works in editions: {0}. | "Home, Pro and Enterprise and Education" lists keep edition names unchanged |
| wykonano / pominięto – / błąd – | done / skipped – / error – | Lower case step results after the step name in long repairs ("Component store: done (…)", "Account thumbnails: skipped – …") |
| Zwolniono: {0}. | Freed: {0}. | Disk space freed by a repair; sizes "1.5 GB", "255 MB", "less than 1 MB" (decimal separator from the program's culture) |
| odczyt zwrotny | verification | Reading the state back after a change ("Verification: services are running as before the reset, …") |
| operacja serwisowania | servicing operation | DISM, SFC or cleanmgr still running ("The previous servicing operation is still in progress – …") |
| magazyn składników | component store | DISM term, as in Microsoft documentation; repair step "Component store" |
| kopia (folderu) / kopia (polityk, typów startu) | copy / backup | Renamed folder copies "…\SoftwareDistribution.dso-…" are "copies"; exported `.reg` and text files are "backups" |
| polityki Windows Update / blokady aktualizacji | Windows Update policies / update blocks | Policy values under `Policies\Microsoft\Windows\WindowsUpdate` |
| Pliki tymczasowe systemu / Pliki tymczasowe konta / Pobrane aktualizacje / Miniatury konta | System temporary files / Account temporary files / Downloaded updates / Account thumbnails | Disk cleanup step names (the program's own labels) |
| program obsługi (Oczyszczania dysku) | handler | Disk Cleanup handler; the handler name "Previous Installations" is not translated |
| folder pobrań / wydanie (GitHub) | downloads folder / release | WinUtil and RustDesk downloads; "test release (prerelease)", "release draft" |
| Sprawdzenie | Check | Step name after Disk Cleanup in the Windows.old repair |
| DSOptimizer – punkt ręczny | DSOptimizer – manual restore point | Description of the restore point created on demand; shown by System Restore |
| Zresetuj Windows Update i usuń blokady | Reset Windows Update and remove blocks | Repair item title quoted in the Windows Update reset result. |
| Procesor i pamięć / Grafika, ekran i dźwięk / Dyski / Sieć / System i zabezpieczenia | Processor and memory / Graphics, display and sound / Disks / Network / System and security | Specification groups (screen and report) |
| Procesor / Pamięć / Płyta główna / Karta sieciowa / Dźwięk / Dysk {0} | Processor / Memory / Motherboard / Network adapter / Sound / Disk {0} | Specification keys; "Karta graficzna" = Graphics card |
| {0} rdzeń / {0} wątek / {0} cal | {0} core / {0} thread / {0} inch | Plural rule 8: "1 core", "8 cores", "16 threads", "27 inches" |
| {0} z {1} gniazd | {0} of {1} slots | Memory slots: "2 of 4 slots", "1 of 1 slot" |
| Wczytywanie… / brak danych / brak | Loading… / no data / none | Reading states in Specification and Preparation |
| Nie udało się odczytać / Nie wiadomo | Couldn't read / Unknown | Preparation reading states |
| Włączony / Wyłączony | On / Off | Secure Boot and BitLocker state, as Windows shows them ("Secure Boot State: On") |
| włączony / wyłączony (TPM, VBS) | enabled / disabled | "2.0, enabled", "VBS enabled, not running" (System Information: "Enabled but not running") |
| BIOS Legacy | Legacy BIOS | Firmware mode; "Not available (Legacy BIOS)" for Secure Boot |
| zdrowy / ostrzeżenie / uszkodzony / nieznany | healthy / warning / unhealthy / unknown | Disk health after "Status: ", as in Windows storage health (Healthy / Warning / Unhealthy) |
| połączona / rozłączona | connected / disconnected | Network adapter |
| zaszyfrowany / niezaszyfrowany / szyfrowanie w toku / szyfrowanie wstrzymane / ochrona włączona | encrypted / not encrypted / encryption in progress / encryption paused / protection on | BitLocker per drive (also "decryption …", "protection off") |
| Aktywowany / Nieaktywowany / Okres próbny / Wymaga aktywacji | Active / Not active / Grace period / Activation required | Windows activation state, as in Settings › System › Activation ("Active"); "Okres próbny" = license grace period. **do audytu (Etap 5)** |
| Komputer / Opcjonalne aktualizacje sterowników / Sterowniki chipsetu / Grafika / Wsparcie producenta / Urządzenia bez sterownika | Computer / Optional driver updates / Chipset drivers / Graphics / Manufacturer support / Devices without a driver | Preparation steps 1 and 3–7 (step 2 is Windows Update); English README |
| Strona sterowników {0} / Strona wsparcia {0} / Karta {0} | {0} drivers page / {0} support page / {0} card | "Intel drivers page", "Lenovo support page", "NVIDIA card" |
| Szukaj sterownika / Szukaj strony wsparcia / Szukaj w Google | Search for a driver / Search for a support page / Search Google | Preparation links |
| Urządzenia z problemem / brak sterownika / wyłączone / problem, kod {0} | Devices with a problem / no driver / disabled / problem, code {0} | Device Manager error codes 28 and 22, and any other code |
| katalog (pozycji) / katalog (na dysku) | catalog / folder | Same Polish word, two meanings: the item catalog (`--catalog`, "external catalog") vs a folder on disk ("working folder", "journal folder") |
| niezaufany właściciel lub uprawnienia / odłożony | untrusted owner or permissions / set aside | Program folders and files with a foreign owner or ACL are renamed (`.untrusted-*`) and replaced: "… was set aside. A new one was created." |
| Nie można … / Brak dostępu do … / Brak uprawnień do … | Can't … / Access denied to … / No permission to … | Windows layer errors; "Błąd zapisu / odczytu / usuwania" = "Error writing / reading / deleting" |
| klucz / podklucz / drzewo kluczy | key / subkey / key tree | Registry |
| punkt ponownej analizy | reparse point | Junction or symbolic link (Windows term) |
| potok / Job Object | pipe / Job Object | Process plumbing in Windows layer errors; "Job Object" kept as the Windows name |
| Ponowne uruchomienie programu: {0} | Restarting the program: {0} | Startup log line before the program restarts itself |
| Ostatnie sprawdzenie: {0} | Last check: {0} | Category subtitle after a status check: "Last check: 14:05" (24-hour time in both languages) |
| Akcje (sekcja listy) | Actions | Section header in the item list (actions and panels), next to "Ustawienia" = Settings (see Pages and menu) |
| Identyfikator | ID | Detail panel label (item ID) |
| Dostępne / Niedostępne / Nie dotyczy | Available / Not available / Not applicable | Detail panel "Undo" values |
| Administrator | Administrator | Brand bar badge: the program runs as administrator |
| {0}, użytkownik {1} | {0}, user {1} | Brand bar: "PC-1, Windows 11 Pro 26100.1, user Dawid" |
| DSOptimizer – uwaga / DSOptimizer – błąd | DSOptimizer – warning / DSOptimizer – error | Startup and error message box titles |
| Nieobsłużony wyjątek | Unhandled exception | Log lines before the error message box |
| obszar roboczy (monitora) | work area | Startup window size log lines ("the primary monitor's work area"), as in the Windows API |
| Szukaj aplikacji / Grupy aplikacji / Aplikacje do instalacji | Search apps / App groups / Apps to install | Screen reader names of the search box and lists on the Apps and Install pages |
| Grupa / Rodzaj / Wydawca / Wersja / Konto / Usuwanie / Cofanie | Group / Type / Publisher / Version / Account / Removal / Undo | Apps page detail labels; "Rodzaj" = Type (Program or Store) |
| Pakiet / Wpis odinstalowania / Tryb / Polecenie: {0} | Package / Uninstall entry / Mode / Command: {0} | Apps page technical details |
| Narzędzie producenta do resztek | Manufacturer's cleanup tool | Apps page technical details: the manufacturer's tool that removes leftovers |
| Zmiany zapisują się od razu. | Changes are saved right away. | Settings page subtitle |
| Twórz punkt przywracania przed zmianami | Create a restore point before changes | Settings checkbox |
| Autor / Licencja / Wersja {0} | Author / License / Version {0} | "About" column in Settings |
| Otwórz github.com/… | Open github.com/… | Screen reader name of the repository link |
| grafitowy (pasek marki, menu) | graphite | Theme hint: "The brand bar and the menu stay graphite." |

## Catalog

Rules for the texts of catalog items (title, "What you gain", "What it changes", technical details, warnings) and of
the app lists. The English texts follow the same copy rules as the Polish ones; the repository tests check every
translated field.

| Polski | English | Notes/source |
|---|---|---|
| Co zyskasz | What you gain | Heading of the benefit field (see Key decisions) |
| Co zmienia | What it changes | Heading of the description field |
| Szczegóły techniczne | Technical details | Collapsed section with the technical description |

- **Quoting another item.** When a text refers to another catalog item by its title (Polish „Ochrona systemu”), the
  English text quotes the English title of that item exactly, in straight double quotes. The test
  `QuotedCatalogTitles_UseEnglishTitle` compares the quote with the title in the English catalog.
- **Windows names inside catalog texts** (Settings paths, Device Manager, Computer Management, services) follow English
  Windows 11 exactly, as in the "Windows names" table below; a new Windows term goes there with its source address.
- **Identifiers stay as they are:** registry paths, service names, cmdlets, file names, `shell:::{GUID}` and `0x…`
  codes are copied character for character. Product names (titles of the install list) are not translated.
- **Limits are the same as in Polish:** title up to 60 characters, summary up to 160, "What you gain" up to 220
  characters and 2 sentences, at most 2 warnings of up to 140 characters each. The restart warning and the "can't be
  undone" warning stay among the warnings in English too.
- **Not in visible texts:** measurements, test machines, Windows build numbers, dates of checks and quotes from
  Microsoft – the same forbidden phrases as in Polish.
- **Warnings** are the short notes that change the decision (restart, no undo, something stops working); the
  interface has no heading for them, so there is no fixed term.

### Catalog – system panels

Titles of the "System panels" items are the English names of the Windows windows they open, as Windows 11 shows them.
Commands, `*.msc`/`*.cpl` file names, canonical names (`Microsoft.PowerOptions`) and `shell:::{GUID}` stay unchanged.
Recurring sentences: "Otwiera panel systemowy Windows: klasyczne okno „…”" = "Opens a Windows system panel: the classic
"…" window"; "DSOptimizer nie cofa zmian zrobionych w tym oknie." = "DSOptimizer doesn't undo changes made in this window."

| Polski | English | Notes/source |
|---|---|---|
| Efekty wizualne (Opcje wydajności) | Visual effects (Performance Options) | "Visual Effects" tab of the Performance Options window (SystemPropertiesPerformance.exe); [Tips to improve PC performance in Windows](https://support.microsoft.com/en-us/windows/experience/performance-optimization/tips-to-improve-pc-performance-in-windows) ("On the Visual Effects tab…"). Window title "Performance Options": no official page found, only [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/2184811/how-to-change-performance-settings-in-gpo). **do audytu (Etap 5)** |
| Opcje zasilania | Power Options | [Canonical Names of Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names); [Automatic maintenance](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-maintenence) ("Control Panel > Hardware and Sound > Power Options") |
| Połączenia sieciowe | Network Connections | `control.exe netconnections` "Displays the Network Connections folder"; [Executing Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items) |
| Zarządzanie komputerem | Computer Management | See "Windows names" |
| Panel sterowania | Control Panel | See "Windows names" |
| Właściwości: Mysz | Mouse Properties | `control.exe mouse` "Launches the Mouse Properties window"; [Executing Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items) |
| Urządzenia i drukarki | Devices and Printers | [Canonical Names of Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names) (`Microsoft.DevicesAndPrinters`) |
| Programy i funkcje | Programs and Features | See "Windows names"; also [Canonical Names of Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names) |
| Region | Region | Same text in both languages; [Canonical Names of Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names) (`Microsoft.RegionAndLanguage` opens the Region item) |
| Zabezpieczenia i konserwacja | Security and Maintenance | [Automatic maintenance](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-maintenence) ("Control Panel > System and Security > Security and Maintenance") |
| Dźwięk | Sound | Shared with the Specification key "Sound"; [Canonical Names of Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names) (`Microsoft.Sound`) |
| Właściwości systemu / karta „Nazwa komputera” | System Properties / "Computer Name" tab | Tab opened by SystemPropertiesComputerName.exe ([Executing Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items)); tab label only in [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/6008607/renaming-my-computer). **do audytu (Etap 5)** |
| Data i godzina | Date and Time | [Canonical Names of Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names) (`Microsoft.DateAndTime`) |
| Zapora Windows Defender | Windows Defender Firewall | See "Windows names" (same audit note) |
| Przywracanie systemu | System Restore | See "Windows names"; the panel opens the System Restore wizard (rstrui.exe) |
| kreator | wizard | "the System Restore wizard" |
| karta sieciowa (w Połączeniach sieciowych) | network adapter | Same as in Specification |

### Catalog – privacy, Explorer, performance

Names used in the items of "Privacy and AI", "Explorer and convenience" and "Performance". Settings paths keep the
separator of the Polish text (">"), and use the English page names exactly.

| Polski | English | Notes/source |
|---|---|---|
| Personalizacja > Pasek zadań / Personalizacja > Start / Personalizacja > Tło | Personalization > Taskbar / Personalization > Start / Personalization > Background | Settings pages; "Settings > Personalization > Background" in the Windows spotlight warning |
| Widżety | Widgets | Taskbar toggle and Win+W board |
| Prywatność > Wyszukaj | Privacy & security > Search | Settings page; the English text uses the full page name |
| Zezwalaj aplikacjom wyszukiwania na wyświetlanie wyników | Let search apps show results | Toggle on Privacy & security > Search. **do audytu (Etap 5)** – no Microsoft page found with this label; literal match of the Polish Windows label |
| Rekomendacje i oferty w ustawieniach | Recommendations and offers in Settings | Privacy & security > Recommendations & offers; [Privacy settings for Recommendations & offers in Windows 11](https://support.microsoft.com/windows/807608ee-3de2-4498-8e7c-eb10d655567f) |
| Spersonalizowane oferty | Personalized offers | Replaces "Tailored experiences"; same source and [Diagnostics, feedback, and privacy in Windows](https://support.microsoft.com/en-us/windows/diagnostics-feedback-and-privacy-in-windows-28808a2b-a31b-dd73-dcd3-4559a5199319) |
| identyfikator reklamowy | advertising ID | Same source as "Recommendations and offers in Settings" |
| Pokaż powiadomienia w ustawieniach | Show notifications in Settings | Account messages in the Settings app. **do audytu (Etap 5)** – only third-party evidence ([Eleven Forum](https://www.elevenforum.com/t/enable-or-disable-show-notifications-in-settings-app-in-windows-11.25835)) |
| Uzyskaj porady i sugestie podczas korzystania z systemu Windows | Get tips and suggestions when using Windows | Checkbox under System > Notifications > Additional settings; [Notifications and Do Not Disturb in Windows](https://support.microsoft.com/help/4014934). **do audytu (Etap 5)** – some builds say "…when I use Windows" |
| zalecenia (menu Start) / sekcja „Proponowane” | recommendations / "Recommended" section | Start menu section name "Recommended" |
| Windows Spotlight / W centrum uwagi Windows / Obraz | Windows spotlight / "Windows spotlight" / "Picture" | Background options in Personalization > Background; [Windows spotlight](https://learn.microsoft.com/en-us/windows/configuration/windows-spotlight/) |
| Dowiedz się więcej o tym zdjęciu | Learn about this picture | Desktop icon added by Windows spotlight. **do audytu (Etap 5)** – third-party evidence only |
| zdjęcia dnia | pictures of the day | Program's own description of Windows spotlight |
| Prywatność i zabezpieczenia > Diagnostyka i opinie | Privacy & security > Diagnostics & feedback | [Diagnostics, feedback, and privacy in Windows](https://support.microsoft.com/en-us/windows/diagnostics-feedback-and-privacy-in-windows-28808a2b-a31b-dd73-dcd3-4559a5199319) |
| Częstotliwość opinii | Feedback frequency | Same source |
| dane diagnostyczne wymagane / opcjonalne | required / optional diagnostic data | Same source |
| niektórymi ustawieniami zarządza organizacja | some settings are managed by your organization | Message in Settings when a policy is set (Windows: "Some of these settings are managed by your organization"). Variant on Privacy & security > Diagnostics & feedback: "Some settings are hidden or managed by your organization." ([Diagnostics, feedback, and privacy in Windows](https://support.microsoft.com/en-us/windows/diagnostics-feedback-and-privacy-in-windows-28808a2b-a31b-dd73-dcd3-4559a5199319)). **do audytu (Etap 5)** – which text 26H2 shows on which page |
| Opcjonalne dane diagnostyczne (przełącznik) | Send optional diagnostic data | The documents avoid the label ("the optional diagnostic data toggle"). The Microsoft page above names only the "Diagnostic data" setting; no evidence of this label found. **do audytu (Etap 5)** |
| Pokaż środowisko powitalne / Zaproponuj sposoby… | Show the Windows welcome experience / Suggest ways… | Old notification settings, absent in 26H2 (`privacy.welcome-experience`). English labels unverified. **do audytu (Etap 5)** |
| migawki (Recall) | snapshots | Recall term. **do audytu (Etap 5)** – no Microsoft page checked |
| Środowiska użytkownika połączonego i telemetria | Connected User Experiences and Telemetry | Display name of the DiagTrack service in Services |
| typ startu Wyłączony | startup type Disabled | Services; "startup type" already in Concepts |
| System > Zaawansowane | System > Advanced | Settings page (formerly For developers); [Advanced Windows Settings](https://learn.microsoft.com/en-us/windows/advanced-settings/) |
| Zakończ zadanie | End task | Taskbar menu item and its toggle on System > Advanced (Microsoft Learn writes "End Task"; the program uses the menu item's sentence case). **do audytu (Etap 5)** |
| nie skonfigurowano | Not Configured | Group Policy state |
| izolacja rdzenia / integralność pamięci | Core isolation / memory integrity | Windows Security > Device security > Core isolation details; [Core isolation](https://support.microsoft.com/en-us/windows/core-isolation-e30ed737-17d8-42f3-a2a9-87521df09b78). "memory integrity" lower case mid-sentence, as in the item texts |
| plik stronicowania | paging file | Windows memory term |
| Eksplorator (restart) | File Explorer | "restarting File Explorer", "after File Explorer restarts" (see Windows names) |
| ponowne zalogowanie | sign in again | Style rule 11: "after you sign in again" |
| Wymaga restartu komputera. | Requires a computer restart. | Restart warning of items with "Restart required" |

### Catalog – app lists

Texts of `apps-bloat.json` (summary, warning, reason of a protected app) and `apps.json` (summary, warning). Titles on the
install list are product names and are not translated. Microsoft app names follow English Windows 11 and Microsoft Store;
the Polish text sometimes uses a descriptive name, the English text then uses the name the user sees in English Windows.

| Polski | English | Notes/source |
|---|---|---|
| Kalkulator / Notatnik / Zdjęcia / Aparat / Zegar | Calculator / Notepad / Photos / Camera / Clock | Inbox apps; [Policy-based removal of pre-installed Microsoft Store apps](https://support.microsoft.com/en-us/servicing/os/windows/docs/2025/08/policy-based-removal-of-pre-installed-microsoft-store-apps) (package list), [Clock app](https://support.microsoft.com/en-us/windows/apps/how-to-use-alarms-and-timers-in-the-clock-app-in-windows) |
| Odtwarzacz multimediów / Filmy i TV | Media Player / Movies & TV | Packages `Microsoft.ZuneMusic` / `Microsoft.ZuneVideo`; [Movies & TV app](https://support.microsoft.com/en-us/windows/apps/watch-movies-and-tv-shows-on-the-microsoft-movies-tv-app) |
| Narzędzie Wycinanie / Rejestrator dźwięku | Snipping Tool / Sound Recorder | [Sound Recorder app for Windows: FAQ](https://support.microsoft.com/en-us/windows/sound-recorder-app-for-windows-faq-5c208478-2141-bd07-fe1d-d6d1356c1d56) |
| Terminal Windows / Instalator aplikacji (winget) | Windows Terminal / App Installer (winget) | App Installer: see "Windows names" |
| Centrum opinii / Porady / Szybka pomoc / Pogoda | Feedback Hub / Tips / Quick Assist / Weather | Inbox apps (packages `WindowsFeedbackHub`, `Getstarted`, `QuickAssist`, `BingWeather`) |
| Pasek gry / pasek gier (Game Bar) | Game Bar | Store app name: [Game Bar](https://apps.microsoft.com/detail/9NZKPSTSNW4P); package `Microsoft.XboxGamingOverlay`. Older builds showed "Xbox Game Bar". **do audytu (Etap 5)** |
| Aplikacja Microsoft 365 (Office Hub) | Microsoft 365 Copilot app (Office Hub) | Package `Microsoft.MicrosoftOfficeHub`; Store listing: [Microsoft 365 Copilot](https://apps.microsoft.com/detail/9WZDNCRD29V9). **do audytu (Etap 5)** – the Polish source still uses the old name "Aplikacja Microsoft 365" |
| Nowy Outlook dla Windows | the new Outlook for Windows | |
| Zakupy w Microsoft Store | Microsoft Store purchases | Descriptive, as in Polish; Windows shows the package `Microsoft.StorePurchaseApp` as "Store Experience Host". **do audytu (Etap 5)** |
| Ulepszenia zgodności aplikacji | Windows Application Compatibility Enhancements | [Microsoft Store listing](https://apps.microsoft.com/detail/9PCSD6N03BKV) |
| Podłączanie pulpitu zdalnego | Remote Desktop Connection | The Win32 entry name in English Windows (matched by the list as well) |
| Rozszerzenie obrazów HEIF / WebP / RAW | HEIF Image Extension / WebP Image Extension / Raw Image Extension | Store names: [HEIF](https://apps.microsoft.com/detail/9PMMSR1CGPWG), [WebP](https://apps.microsoft.com/detail/9PG2DK419DRG), [Raw](https://apps.microsoft.com/detail/9NCTDW2W1BH8) |
| Rozszerzenie wideo HEVC / AV1 / MPEG-2 / VP9 | HEVC Video Extensions / AV1 Video Extension / MPEG-2 Video Extension / VP9 Video Extensions | Store names: [HEVC](https://apps.microsoft.com/detail/9NMZLZ57R3T7), [AV1](https://apps.microsoft.com/detail/9MVZQVXJBQ9V), [MPEG-2](https://apps.microsoft.com/detail/9N95Q1ZZPMH4), [VP9](https://apps.microsoft.com/detail/9N4D0MSMP0PT) – singular/plural as Microsoft writes them |
| Rozszerzenie kodera AVC | AVC Encoder Video Extension | Package `Microsoft.AVCEncoderVideoExtension`. **do audytu (Etap 5)** – Store page not reachable when checked |
| Rozszerzenia multimediów sieci Web | Web Media Extensions | [Store listing](https://apps.microsoft.com/detail/9N5TDP8VCMHS) |
| Biblioteki Visual C++ | Visual C++ libraries | Microsoft Visual C++ Redistributable |
| Środowisko .NET 8 | .NET 8 runtime | |
| podstawowe narzędzie systemu / składnik systemu | a basic system tool / part of the system | Reasons of protected apps; "składnik usługi Windows Update" = "a component of the Windows Update service" |
| oprogramowanie firmowe / administrator firmy | company software / the company's administrator | Corporate McAfee and Trellix entries |
| aplikacja producenta / asystent producenta | the manufacturer's app / the manufacturer's assistant | Manufacturer tools (Lenovo, ASUS, Acer, HP, Dell) |
| wersja próbna antywirusa … preinstalowana przez producenta komputera | trial version of the … antivirus, preinstalled by the computer manufacturer | |
| Własny deinstalator potrafi zostawić resztki. | Its own uninstaller can leave leftovers behind. | |
| Instaluje się tylko dla bieżącego konta Windows. | Installs only for the signed-in Windows account. | Install list warning (`catalog-apps.json`); Style rule 11 |
| pakiet aplikacji / menu kontekstowe | app package / context menu | Install list warnings ("Also adds the … app package") |
| Autostart (wszystkich kont) | Startup (for all accounts) | RustDesk warning: the Startup folder |

### Catalog – repairs

Titles of the "Repairs" items (other texts quote them exactly) and the terms of their descriptions and script messages.

| Polski | English | Notes/source |
|---|---|---|
| Wyczyść pamięć DNS / Zresetuj ustawienia sieci | Clear the DNS cache / Reset network settings | Item titles |
| Zresetuj Windows Update / Zresetuj Windows Update i usuń blokady | Reset Windows Update / Reset Windows Update and remove blocks | Item titles; the second is quoted in the Windows Update reset result and in the description of the first |
| Utwórz punkt przywracania teraz / Ochrona systemu | Create a restore point now / System Protection | Item titles; both are quoted in warnings of the other item |
| Sprawdź i napraw pliki systemowe / Wyczyść dysk / Usuń poprzednią instalację Windows | Check and repair system files / Clean up the disk / Remove the previous Windows installation | Item titles; "Clean up the disk" is the program's own item, not the Windows tool Disk Cleanup |
| Napraw winget (Instalator aplikacji) / Napraw Microsoft Store na tym koncie | Repair winget (App Installer) / Repair Microsoft Store on this account | Item titles; "Repair winget" as in "Go to: Repair winget" |
| Bez cofania – … / Bez cofania w DSOptimizerze – … | No undo – … / No undo in DSOptimizer – … | Uniform start of the "no undo" sentence in repair descriptions and warnings |
| skutku nie potwierdzono | the effect wasn't confirmed | Script messages after a failed verification; see "odczyt zwrotny" = verification ("(odczyt zwrotny)" = "(verified)") |
| Sukces dopiero po … | It counts as success only after … | Descriptions |
| pamięć DNS | DNS cache | [Clear-DnsClientCache](https://learn.microsoft.com/en-us/powershell/module/dnsclient/clear-dnsclientcache) ("Clears the contents of the DNS client cache") |
| brama / maska / karta (sieciowa) | gateway / mask / adapter | IP configuration lines of the network reset ("gateway: none", "(mask 255.255.255.0)"); "brak" = "none" as elsewhere |
| tak / nie (wartość DHCP) | yes / no | Lower case values in the network reset log lines; not the same key as "Nie" = "No" |
| zasady grupy | Group Policy | Style rule 1 |
| typ uruchamiania | startup type | Same as "typ startu" (Concepts) |
| Kosz / folder Pobrane | Recycle Bin / Downloads folder | [Free up drive space in Windows](https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/free-up-drive-space-in-windows) |
| okno „Ochrona systemu” (Konfiguruj → Usuń) | "System Protection" window (Configure → Delete) | "Configure..." on the System Protection tab: [System Protection](https://support.microsoft.com/en-us/windows/experience/backup-recovery/system-protection); the "Delete" button of that dialog is not named on that page. **do audytu (Etap 5)** |
| „Zatrzymaj” (w uwagach długich napraw) | "Stop" | Short form of the "Stop after this step" button, as the Polish warning shortens it |
| program „optymalizujący” | "optimizing" program | |
| komunikat, że ustawieniami zarządza organizacja | Windows says that your organization manages the settings | Windows shows "Some of these settings are managed by your organization". **do audytu (Etap 5)** |
| Sklep (Microsoft Store) | the Store | Short form after "Microsoft Store" was named in the same text |
| nośnik instalacyjny / nazwa odwrotna (DNS) | installation media / reverse name | Program texts (`catalog-repair.json`, `repair.json`): "their reverse names (*.in-addr.arpa…)" |
| katalog Winsock / stos IP | Winsock catalog / IP stack | Network reset; `netsh winsock show catalog` |
| naruszenia integralności | integrity violations | SFC result ("did not find any integrity violations") |
| kopia w tle / magazyn kopii w tle | shadow copy / shadow storage | VSS terms (`vssadmin list shadows`, `list shadowstorage`) |
| aktualizacja funkcji | feature update | Windows servicing term |
| uchwyt / dowiązanie symboliczne / junction | handle / symbolic link / junction | Win32 terms; see also "punkt ponownej analizy" (Concepts) |
| karencja (`/StartComponentCleanup`) | grace period | The 30-day period of the component store cleanup task |

### Catalog – disk, network, security, tools

| Polski | English | Notes/source |
|---|---|---|
| Wyłącz … (pozycja) | Turn off … | Item titles and messages use "turn on / turn off" for Windows features, as Microsoft does ("Turn off hibernation", "Turn off IPv6") |
| hibernacja / uśpienie / hybrydowy stan uśpienia | hibernation / sleep / hybrid sleep | [Distinguishing fast startup from wake-from-hibernation](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/distinguishing-fast-startup-from-wake-from-hibernation) |
| Szybkie uruchamianie | fast startup | Lower case mid-sentence, as on Microsoft Learn (same source); Control Panel shows "Turn on fast startup (recommended)" |
| rezerwa miejsca (na aktualizacje) | reserved storage | [How reserved storage works in Windows](https://support.microsoft.com/en-gb/windows/how-reserved-storage-works-in-windows-5bc98443-0711-8038-4621-6a18ddc904f2) |
| czas ostatniego dostępu | last access time | [fsutil behavior](https://learn.microsoft.com/windows-server/administration/windows-commands/fsutil-behavior) (`disablelastaccess`) |
| krótkie nazwy plików 8.3 | 8.3 short file names | Same source (`disable8dot3`); "DOS-style short name" in plain sentences |
| wolumin / dysk / partycja | volume / drive / partition | "the C: drive", "the C: volume" (technical details) |
| udział sieciowy / dysk sieciowy / NAS | network share / network drive / NAS | "old NAS devices" for the plural |
| logowanie gościa / podpisywanie SMB | guest logon / SMB signing | [Enable insecure guest logons in SMB2 and SMB3](https://learn.microsoft.com/en-us/windows-server/storage/file-server/enable-insecure-guest-logons-smb2-and-smb3) |
| po stronie klienta | on the client | "Don't require SMB signing on the client" |
| profil publiczny / profil prywatny (sieci) | public network profile / private profile | Settings › Network & internet › Wi-Fi › Network profile type; [Make a Wi-Fi network public or private in Windows](https://support.microsoft.com/en-us/kb/4043043). Category names in technical details stay `Public` / `Private` / `DomainAuthenticated` |
| wykrywalny w sieci | discoverable on the network | Same source |
| zapora Windows | Windows Defender Firewall | Single name for the firewall: see "Zapora Windows Defender" in Windows names |
| automatyczne szyfrowanie (urządzenia, dysku) | automatic (device, drive) encryption | [Device Encryption in Windows](https://support.microsoft.com/en-us/windows/security/encryption/device-encryption-in-windows); the technical name "BitLocker automatic device encryption" stays as it is |
| odszyfrowywanie / odszyfrowuje | decryption / decrypts | BitLocker; "Decryption of C: in progress" |
| klucze automatycznego odblokowania | auto-unlock keys | BitLocker "Turn on auto-unlock"; [Clear-BitLockerAutoUnlock](https://learn.microsoft.com/en-us/powershell/module/bitlocker/clear-bitlockerautounlock) |
| protektory klucza | key protectors | BitLocker term, same source family |
| menu kontekstowe / menu prawego przycisku | context menu / right-click menu | |
| Pokaż więcej opcji | Show more options | Classic Windows 11 context menu entry |
| Przejmij na własność | take ownership | The menu text itself is a registry value written in Polish by the item (`MUIVerb`), so the English description names it as "Przejmij na własność" (take ownership). **do audytu (Etap 5)** |
| Administratorzy (grupa) | Administrators | Built-in group, SID S-1-5-32-544 |
| UAC (zgoda w UAC) | UAC prompt ("after you approve the UAC prompt") | The documents use the noun "UAC approval" ("requires UAC approval"), which matches the program text "After you approve the UAC prompt" (`catalog-sec.json`) |
| powłoka (Eksploratora) / pulpit | shell / desktop | "the File Explorer shell (explorer.exe)" |
| Menedżer zadań > Plik > Uruchom nowe zadanie | Task Manager > Run new task | "Run new task" as in Task Manager. **do audytu (Etap 5)** – the redesigned Windows 11 Task Manager shows "Run new task" in the command bar, not in a File menu (the Polish text says "Plik"). **do audytu (Etap 5):** polski tekst też do poprawy |
| wylogowanie | signing out | Style rule 11 |
| wydanie testowe / szkic (GitHub) | test release / draft | WinUtil description |
| tryb zarządzany przez system / przez użytkownika | system-managed / user-managed (mode) | `NtfsDisableLastAccessUpdate` modes, as in the program text (`catalog-disk.json`) |

## Documentation

English versions of the item documents and lists live in `docs/en/` (`docs/en/tweaks/<id>.md`, `docs/en/apps/`,
`docs/en/prep/`); the Polish files in `docs/tweaks/`, `docs/apps/` and `docs/prep/` stay the source. The section headings
come from the template `docs/en/tweaks/README.md`, in the same order as in Polish.

- **Placeholders in angle brackets** are written in backticks (`<date>`), because GitHub hides a bare `<date>` as an HTML
  tag. Inside the English text they are translated: `<data>` → `<date>`, `<czas>` → `<time>`, `<profil>` → `<profile>`,
  `<znacznik>` → `<marker>`, `<klucz>` → `<key>`, `<Klasa>` → `<Class>`, `<kultura>` → `<culture>`,
  `<rrrrMMdd-HHmmss>` → `<yyyyMMdd-HHmmss>`, `<SID zalogowanego>` → `<SID of the signed-in user>`.
- **Polish texts read on the test machines** (Windows labels and messages, program output recorded on Polish Windows)
  stay verbatim in Polish with the English text in brackets – `"Odmowa dostępu" (Access is denied)` – only in the
  measurement passages ("VM test", "Defaults on a fresh 26H2", "Observed effect") and in quoted error messages.
  Everywhere else the English Windows name is used without the Polish text.
- **Section names of the Polish-only records** in `docs/zrodla/` are quoted in Polish with a gloss, so the reader can
  find them: `"Wyniki VM" (VM results)`, `"Sonda uzupełniająca" (follow-up probe)`.

| Polski | English | Notes/source |
|---|---|---|
| Werdykt 26H2 | Verdict for 26H2 | Template heading |
| Co zmienia | What it changes | Template heading; same as the detail panel heading (Concepts) |
| Źródła | Sources | Template heading |
| Wartości domyślne na świeżym 26H2 | Defaults on a fresh 26H2 | Template heading |
| Detekcja i undo | Detection and undo | Template heading |
| Ryzyka i ostrzeżenia | Risks and warnings | Template heading |
| Test VM | VM test | Template heading |
| Obserwacja skutku | Observed effect | Template heading; optional, as in Polish (older documents don't have it) |
| Reddit | Reddit | Template heading |
| Do uzupełnienia w części E. | To be added in part E. | Placeholder sentence in the "Reddit" section |
| do uzupełnienia | to be added | Placeholder value |
| brak (wartość rejestru) / brak klucza | none / key missing | Values in "Defaults on a fresh 26H2" |
| Nie sprawdzono: | Not checked: | Start of a line listing what wasn't checked |
| Sprawdzono: | Checked: | Start of a line listing what was checked |
| Odczyt VM 26H2 Home i Pro | Reading from 26H2 Home and Pro VMs | Source type in "Sources" |
| `dostęp <data>` | `accessed <date>` | Access date of a source; the date stays ISO ("accessed 2026-10-18") |
| odwołanie do `docs/zrodla/…` | `docs/zrodla/…` (in Polish) | Added after every reference to a file in `docs/zrodla/`; those records exist only in Polish |
| Etap N / część E / partia X | Stage N / part E / batch X | Project stages in the documents: "Stage 4a", "batch 1", "Task 9 of the batch", "Batch C4 research". The mark **do audytu (Etap 5)** in this glossary stays Polish |
| decyzja R14 / decyzja projektowa / `decyzja <imię> z <data>` | ruling R14 / design decision / `<name>'s decision of <date>` | Ruling ids (R…) are not changed |
| klasa B (research) | class B | Evidence class of a research source |
| Portować / `Portować jako <x>` / Portować z ograniczeniem | Port / `Port as <x>` / Port with a restriction | Verdict; as in the template ("port / don't port / placebo") |
| nie wchodzi / Nie portowane | Not ported | Verdict; capitalized at the start of the verdict ("Not ported:") |
| Pozycji nie ma w katalogu, nic nie zmienia. Rozważany wpis: | The item isn't in the catalog and changes nothing. Entry considered: | Verdict of an item that didn't make it into the catalog |
| Nie dotyczy: / Nie dotyczy (pozycja nie jest w katalogu). | Not applicable: / Not applicable (the item isn't in the catalog). | Start of "Detection and undo" for actions and for items outside the catalog |
| Odrzucone warianty / Działająca alternatywa (opisana, bez własnej pozycji katalogu) | Rejected variants / Working alternative (described, without its own catalog item) | Recurring headings and lines |
| remis rozstrzygnięty na korzyść nazwy kanonicznej MS | the tie was resolved in favor of the MS canonical name | System panels documents |
| Hint co do listy i polecenia, nie dowód skutku | A hint for the list and the command, not evidence of the effect | WinUtil source line |
| Bez ostrzeżeń w katalogu. | No warnings in the catalog. | "Risks and warnings" |
| Ostrzeżenie w katalogu: / Ostrzeżenia w katalogu: | Warning in the catalog: / Warnings in the catalog: | "Risks and warnings"; not "Catalog warning(s)" |
| Poza uwagami w programie (w szczegółach technicznych albo tylko tutaj) | Beyond the notes in the program (in the technical details or only here) | Recurring sentence in "Risks and warnings" |
| skutek uboczny / odmowa (przed zmianą) | side effect / refusal | |
| Przebieg łącznie: | Whole run: | Intro of the run summary in "VM test" |
| `Przebieg VM z <data>` / `Przebieg na VM <data>` | `VM run of <date>` / `VM run on <date>` | Two Polish forms, two English forms |
| przebieg (VM, testu) / przebieg CLI / przebieg `main` | run / CLI run / `main` run | "first run" |
| zaliczone / 0 błędów / pominięte / `Pester zaliczył N` | passed / 0 failed / skipped / `Pester passed N` | Run counts: "722 passed / 0 failed / 25 skipped", "Pester passed 140, 0 failed, 10 skipped"; not "0 failures" |
| przypadek / sonda / sonda uzupełniająca / główna sonda | case / probe / follow-up probe / main probe | "Case `net-smb-harden`", "probe 7" |
| eksploracja | exploration | "C4 exploration", "The exploration of 2026-10-21 passed" |
| prawdziwy restart / ulotny znacznik | real restart / volatile marker | Restart proof in "VM test" (later `LastBootUpTime`, the marker disappears) |
| bezpieczniki (w testach harnessu) | tripwires | Commands that throw in a test when they are called without a mock |
| smoke / mock / fake | smoke test / mock / fake | Test doubles: "engine tests on fakes" |
| strażnik / plik-strażnik / wartownik | guard / guard file / sentinel | `ServicingGuard`, the shell guard; "wartownik" = a sentinel value (`KeyPresentAfterUndo`) |
| funkcja pomocnicza | helper function | |
| stan po apply potwierdzony niezależnym odczytem | state after apply confirmed by an independent reading | |
| stan końcowy = stan przed | final state = state before | "VM test"; capitalized at the start of a sentence ("Final state = state before"); not "end state" |
| undo z wartości domyślnych | undo to defaults | Same family as "Undone to defaults" |
| stan efektywny / rozjazd / odczyt stanu fabrycznego | effective state / mismatch / factory-state reading | |
| proces żyje / odpytuje w pętli | the process is running / polls in a loop | `Check` wording of panel items |
| zapis przez API rejestru | write through the registry API | "written through the registry API in C#" |
| HKCU procesu Administratora | the `HKCU` of the Administrator process | Per-user items tested as Administrator |
| podniesiona sesja / zadanie interaktywne podniesione | elevated session / elevated interactive task | |
| zadanie Harmonogramu | Task Scheduler task | |
| Nie testowane na VM | Not tested on the VM | Next to "Checked:" / "Not checked:" |
| niezmierzone / do potwierdzenia na sprzęcie | not measured / to be confirmed on hardware | Caveats; bold kept where Polish has it |
| pomiar / za pomiarem | measurement / as cited in the measurement | "za pomiarem" = the address comes from the measurement record |
| oględziny | visual check | |
| `Raporty lokalne: <ścieżka> (katalog w .gitignore)` | `Local reports: <path> (folder in .gitignore)` | "katalog" = folder (Concepts) |
| raporty poza gitem / surowe wyniki poza gitem | reports outside git / raw results outside git | |
| Raporty lokalne poza gitem. | Reports are local, outside git. | "VM test"; not "Local reports outside git" (different from `Local reports: <path>` above) |
| Odczyt z VM DSO-26H2-Home i DSO-26H2-Pro / Odczyt hosta deweloperskiego | Reading from the DSO-26H2-Home and DSO-26H2-Pro VMs / Reading from the development host | Source types in "Sources" |
| MS-archiwum / archiwalny blog / hint | MS archive / archived blog / hint | Source types; "hint" = a non-Microsoft source |
| pobrana dwukrotnie | fetched twice | "Sources" |
| j.w. / jw. | as above | |
| Niepotwierdzone / Odrzucone (z powodem) | Unconfirmed / Rejected (with reason) | Sections: "Niepotwierdzone" in `docs/prep`, "Odrzucone (z powodem)" in `docs/apps/install-README.md` |
| Cel / Źródło / Sprawdzono / Producent | Purpose / Source / Checked / Manufacturer | `docs/prep` column headers |
| chipset / grafika / wsparcie / wsparcie-numer | chipset / graphics / support / support-number | `docs/prep` purpose values |
| Lista – pozycje z dowodem (N) | List – entries with evidence (N) | `docs/apps/install-README.md` heading |
| zamiast tego `install.rustdesk` z GitHuba | use `install.rustdesk` from GitHub instead | `docs/apps/install-README.md` rejection reason |
| Decyzje na granicy / Źródła rozpoznania | Borderline decisions / Identification sources | `docs/apps` headings |
| wchodzi (werdykt w tabeli listy) / zakres: komputer / konto | in / scope: computer / account | `docs/apps` tables |
| zależności uruchomieniowe / ponowienie / faza `network` | runtime dependencies / retry / `network` phase | `docs/apps`; as "Środowiska uruchomieniowe" = Runtimes |
| kłódka | padlock | |

## Windows names

| Polski | English | Notes/source |
|---|---|---|
| Ustawienia (aplikacja Windows) | Settings | [System configuration tools in Windows](https://support.microsoft.com/en-us/windows/experience/system-configuration-tools-in-windows) |
| Windows Update | Windows Update | [Install Windows Updates](https://support.microsoft.com/en-us/windows/deployment/updates-lifecycle/install-windows-updates) |
| Opcje zaawansowane | Advanced options | Settings › Windows Update › Advanced options; [Automatically get recommended and updated hardware drivers](https://support.microsoft.com/en-gb/windows/hardware/drivers/automatically-get-recommended-and-updated-hardware-drivers) |
| Aktualizacje opcjonalne | Optional updates | Same source; "Under Additional options, select Optional updates" |
| Opcjonalne aktualizacje sterowników | Optional driver updates | Program's own label; same source |
| Menedżer urządzeń | Device Manager | [Update drivers through Device Manager in Windows](https://support.microsoft.com/en-us/windows/update-drivers-through-device-manager-in-windows-ec62f46c-ff14-c91d-eead-d7126dc1f7b6) |
| Ochrona systemu | System Protection | Capitalized as a feature name, also mid-sentence; [System Protection](https://support.microsoft.com/en-us/windows/experience/backup-recovery/system-protection) |
| Przywracanie systemu | System Restore | Same source |
| punkt przywracania | restore point | Lower case mid-sentence; same source |
| Microsoft Store | Microsoft Store | [Fix problems with apps from Microsoft Store](https://support.microsoft.com/en-us/accounts-billing/fix-problems-with-apps-from-microsoft-store) |
| Instalator aplikacji | App Installer | Package that delivers winget; [Use WinGet to install and manage applications](https://learn.microsoft.com/en-us/windows/package-manager/winget/) |
| winget | winget | Same source. Microsoft now writes "WinGet"; the program keeps "winget" as the command name, same as in Polish. **do audytu (Etap 5)** |
| Zabezpieczenia Windows | Windows Security | [Stay protected with the Windows Security app](https://support.microsoft.com/en-us/windows/security/windows-security/stay-protected-with-the-windows-security-app) |
| Microsoft Defender | Microsoft Defender | Same source |
| Zapora Windows Defender | Windows Defender Firewall | [Firewall and network protection in the Windows Security app](https://support.microsoft.com/en-us/windows/security/windows-security/firewall-and-network-protection-in-the-windows-security-app). **do audytu (Etap 5)** – newer builds may show "Windows Firewall" |
| Harmonogram zadań | Task Scheduler | [Task Scheduler](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-start-page) |
| Panel sterowania | Control Panel | [System configuration tools in Windows](https://support.microsoft.com/en-us/windows/experience/system-configuration-tools-in-windows) |
| Zarządzanie komputerem | Computer Management | Same source |
| Podgląd zdarzeń | Event Viewer | Same source |
| Menedżer zadań | Task Manager | Same source |
| Usługi | Services | [Fix problems in Windows Search](https://learn.microsoft.com/en-us/troubleshoot/windows-client/shell-experience/fix-problems-in-windows-search) ("open Services") |
| Oczyszczanie dysku | Disk Cleanup | [Free up drive space in Windows](https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/free-up-drive-space-in-windows) |
| Optymalizacja dostarczania | Delivery Optimization | Disk cleanup step and Windows feature; [Delivery Optimization for Windows updates](https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization) |
| Poprzednie instalacje systemu Windows | Previous Windows installation(s) | Disk Cleanup option; the program also matches the handler name "Previous Installations" in Windows – that text is not translated. **do audytu (Etap 5)** |
| BitLocker | BitLocker | [BitLocker overview](https://support.microsoft.com/en-us/windows/security/encryption/bitlocker-overview) |
| klucz odzyskiwania | recovery key | [Find your BitLocker recovery key](https://support.microsoft.com/en-us/windows/security/encryption/find-your-bitlocker-recovery-key) |
| Szyfrowanie urządzenia | Device encryption | [Device Encryption in Windows](https://support.microsoft.com/en-us/windows/security/encryption/device-encryption-in-windows) |
| Eksplorator | File Explorer | [File Explorer in Windows](https://support.microsoft.com/en-us/windows/experience/fileexplorer/file-explorer-in-windows) |
| pasek zadań | taskbar | Lower case. **do audytu (Etap 5)** |
| Programy i funkcje | Programs and Features | [Uninstall or remove apps and programs in Windows](https://support.microsoft.com/en-us/windows/uninstall-or-remove-apps-and-programs-in-windows-4b55f974-2cc6-2d2b-d092-5905080eaf98) |
| Zainstalowane aplikacje | Installed apps | Settings › Apps › Installed apps; same source |
| Instalator Windows | Windows Installer | MSI service; [Windows Installer](https://learn.microsoft.com/en-us/windows/win32/msi/windows-installer-portal) |
| Podstawowa karta graficzna Microsoft | Microsoft Basic Display Adapter | [Microsoft basic display adapter in Windows](https://support.microsoft.com/en-us/windows/hardware/display-graphics/microsoft-basic-display-adapter-in-windows) |
| Aktywacja Windows | Windows activation | Settings › System › Activation; [Activate Windows](https://support.microsoft.com/en-us/windows/activation/activate-windows) |
| klucz produktu | product key | Same source |
| Administrator Protection | Administrator protection | Sentence case as on Microsoft Learn (Polish text keeps its own form); [Administrator protection](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/administrator-protection/) |
| Opcje zasilania | Power Options | Control Panel name; see "Catalog – system panels" |
| Właściwości systemu | System Properties | See "Catalog – system panels" |
| Połączenia sieciowe | Network Connections | See "Catalog – system panels" |
| Urządzenia i drukarki | Devices and Printers | See "Catalog – system panels" |
| Zabezpieczenia i konserwacja | Security and Maintenance | See "Catalog – system panels" |
| Home / Pro / Enterprise / Education | Home / Pro / Enterprise / Education | Windows editions, not translated; "Enterprise i Education" → "Enterprise and Education" |
| Uzyskaj pomoc | Get Help | Inbox app (`Microsoft.GetHelp`); [Get Help for Microsoft personal account users](https://support.microsoft.com/en-us/support/get-help/get-help-for-microsoft-personal-account-users) ("you can use the Get Help app") |
| Łącze z telefonem | Phone Link | Inbox app; [Setting up photos in the Phone Link](https://support.microsoft.com/en-us/windows/apps/phonelink/setting-up-photos-in-the-phone-link) |
| Urządzenia przenośne | Mobile devices | Settings › Bluetooth & devices › Mobile devices; [Manage Mobile devices in Windows](https://support.microsoft.com/en-us/windows/apps/phonelink/manage-mobile-devices-in-windows) |
| Eksplorator Windows (proces w Menedżerze zadań) | Windows Explorer | The `explorer.exe` entry in Task Manager – not the app name File Explorer; [Troubleshooting blank screens in Windows](https://support.microsoft.com/en-us/windows/hardware/display-graphics/troubleshooting-blank-screens-in-windows) ("Find Windows Explorer, right-click it, and select Restart.") |
| Włącz ochronę systemu | Turn on system protection | Option in the System Protection › Configure dialog; [System Protection](https://support.microsoft.com/en-us/windows/experience/backup-recovery/system-protection) ("Select Turn on system protection.") |
| Historia aktualizacji / Sprawdź aktualizacje | Update history / Check for updates | Settings › Windows Update; [Windows Update: FAQ](https://support.microsoft.com/en-us/windows/windows-update-faq-8a903416-6f45-0718-f5c7-375e92dddeb2) |
| Aktualizacje definicji | Definition updates | Group in Update history. **do audytu (Etap 5)** – only community answers found |
| karta „Zaawansowane” (Właściwości systemu) | "Advanced" tab | `SystemPropertiesAdvanced.exe` ("Advanced system properties", [Executing Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items)); tab label only in Microsoft Q&A. **do audytu (Etap 5)** |
| Usługi kryptograficzne | Cryptographic Services | Service display name (`CryptSvc`); [Security guidelines for system services](https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server) |
| Centrum sieci i udostępniania / Centrum akcji | Network and Sharing Center / Action Center | [Canonical Names of Control Panel Items](https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names) (`Microsoft.NetworkAndSharingCenter`; `Microsoft.ActionCenter` is Windows 7–8.1) |
| Odmowa dostępu. / Nie można odnaleźć określonego pliku. | Access is denied. / The system cannot find the file specified. | Windows error messages (`ERROR_ACCESS_DENIED`, `ERROR_FILE_NOT_FOUND`); [System Error Codes (0-499)](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-) |
| nie można odnaleźć pliku (Uruchamianie, Win+R) | cannot find the file | Short gloss in `panels.visual-effects`; the exact English text of the Run dialog wasn't checked. **do audytu (Etap 5)** |
| Wykonanie serwera nie powiodło się | Server execution failed | COM error 0x80080005 (`CO_E_SERVER_EXEC_FAILURE`); [Error code 80080005 when you start many COM+ applications](https://learn.microsoft.com/en-us/troubleshoot/windows-server/application-management/error-8008005-when-start-complus-applications) ("server execution failed") |
