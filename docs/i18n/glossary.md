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
| Zresetuj Windows Update i usuń blokady | Reset Windows Update and remove blocks | Repair item title quoted in the Windows Update reset result. **do audytu (Etap 5)** – must match the catalog's English title |
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
| Opcje zasilania | Power Options | Control Panel name. **do audytu (Etap 5)** |
| Właściwości systemu | System Properties | **do audytu (Etap 5)** |
| Połączenia sieciowe | Network Connections | **do audytu (Etap 5)** |
| Urządzenia i drukarki | Devices and Printers | **do audytu (Etap 5)** |
| Zabezpieczenia i konserwacja | Security and Maintenance | **do audytu (Etap 5)** |
| Home / Pro / Enterprise / Education | Home / Pro / Enterprise / Education | Windows editions, not translated; "Enterprise i Education" → "Enterprise and Education" |
