# Autostart popularnych programów: pomiar na VM (2026-11-02)

- System: VM Hyper-V `DSO-26H2-Pro`, Windows 11 Pro 26H2 (rejestr: `EditionID` = `Professional`, build **26300.9457**), polski. Konto `dso` (administrator lokalny, autologowanie).
- Każda partia od checkpointu `czysta-26H2`, sieć przez `Default Switch` tylko na czas pomiaru. Po pracy: revert do `czysta-26H2`, karta odłączona, VM wyłączona.
- winget 1.11.510, zawsze `install --id <id> --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity`, jako `dso` z zadania Harmonogramu (Interactive, **podniesiony token**, jak po UAC). Spotify odmawia instalacji z podniesieniem (`0x8A150056`), więc zainstalowany bez podniesienia.
- Pierwsze uruchomienie: skrót z menu Start (dla Teams `shell:AppsFolder\MSTeams_8wekyb3d8bbwe!MSTeams`) jako `dso` **bez podniesienia**, 60 s, potem zamknięcie okna i zabicie pozostałych nowych procesów. Nigdzie nie logowano się do kont usług (Steam, Spotify, Discord itd.) – po zalogowaniu programy mogą dopisać więcej.
- Restart (potwierdzony `LastBootUpTime` + ulotny klucz), autologowanie, odczyt ok. 150 s po starcie, kopia `wdi\LogFiles\StartupInfo\<SID>_StartupInfo<N>.xml`.
- CPU/dysk: z najnowszego StartupInfo konta `dso`; „własny” = proces uruchomiony przez Windows (rodzic `explorer.exe`, `runonce.exe` albo `sihost.exe`), „z drzewem” = suma z potomkami (po PID/ParentPID w pliku). Okno pomiaru Windows to ok. 90 s.

## Wersje programów

| Pakiet winget | Wersja z winget / wpisu Uninstall | Uwagi |
|---|---|---|
| Valve.Steam | 2.10.91.91 | `steam.exe` po samoaktualizacji: wersja pliku 11.15.70.27 |
| EpicGames.EpicGamesLauncher | 1.3.210.0 | `EpicGamesLauncher.exe` 20.3.7-0+UE5; doinstalował Epic Online Services 5.8.1 |
| ElectronicArts.EADesktop | 13.813.1.6327 | |
| Blizzard.BattleNet | 2.0.0.4232 (instalator) | wymaga `--location`; aplikacja 2.53.6.17952 |
| Ubisoft.Connect | 175.0.13393 | |
| Spotify.Spotify | 1.3.6.1921.g7086c071a617 | tylko bez podniesienia |
| Discord.Discord | 1.0.9266 | |
| Zoom.Zoom | 7.2.51704 („Zoom Workplace (64-bit)”) | |
| Telegram.TelegramDesktop | 7.3.3 | |
| Microsoft.Teams | 26276.513.5241.9675 (pakiet `MSTeams_8wekyb3d8bbwe`) | |
| Apple.iTunes | 12.13.12.3 | |
| Dropbox.Dropbox | 270.4.3312 → sam zaktualizował się do 276.4.5325 przed restartem | |
| Google.GoogleDrive | 133.0.2.0 | |
| Opera.Opera | 139.0.6053.22 | instalacja na konto (`%LOCALAPPDATA%\Programs\Opera`) |
| Brave.Brave | 156.1.98.63 | instalacja na konto |
| Google.Chrome | – | **nie zainstalowany**: `0x8A150011` (hash instalatora MSI ≠ manifest, dwie próby); zamiast niego `Google.Chrome.EXE` 156.0.8078.80 – zainstalował się **na konto** (`%LOCALAPPDATA%\Google\Chrome`) |
| Piriform.CCleaner (CCleaner 7) | – | **nie zainstalowany**: `0x8A150011` (dwie próby); zamiast niego `Piriform.CCleaner.Slim` 6.41, który przy następnym logowaniu sam przeszedł na **CCleaner 7 7.7.1313.1667** i odinstalował 6.41 |
| Adobe.Acrobat.Reader.64-bit | 26.002.22031 („Adobe Acrobat (64-bit)”) | |
| Oracle.JavaRuntimeEnvironment | 8.0.5110.32 (Java 8 Update 511) | + „Java Auto Updater” 2.8.511.32 |
| qBittorrent.qBittorrent | 5.3.0 | |
| Logitech.OptionsPlus | 2.9.994631 | **pominięty** – patrz niżej |

## Wpisy autostartu (Run, RunOnce, foldery, StartupTask)

`<SID>` = SID konta `dso`. „Kiedy” = czy wpis był już po samej instalacji, czy pojawił się dopiero po 1. uruchomieniu. Folderów Autostart (użytkownika i wspólnego) **żaden** program nie użył.

| Program | Miejsce | Nazwa | Polecenie (dokładnie) | Plik: CompanyName / FileDescription | Kiedy | Po restarcie | CPU / dysk własny; z drzewem |
|---|---|---|---|---|---|---|---|
| Steam | `HKU\<SID>\…\Run` | `Steam` | `"C:\Program Files (x86)\Steam\steam.exe" -silent` | `Valve Corporation` / `Steam` | instalacja | tak (explorer) | 602 ms / 58,2 MB; 4 270 ms / 98,2 MB |
| EA app | `HKU\<SID>\…\Run` | `EADM` | `"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EALauncher.exe" -silentOs` | `Electronic Arts` / `EA` | instalacja | tak (explorer); w StartupInfo ścieżka `…\EA Desktop\13.813.1.6327\EA Desktop\EALauncher.exe` | 95 ms / 16,2 MB; 3 652 ms / 208,0 MB |
| Battle.net | `HKU\<SID>\…\Run` | `Battle.net` | `"C:\Program Files (x86)\Battle.net\Battle.net.exe" --autostarted` | `Blizzard Entertainment` / `Battle․net` (uwaga: znak U+2024, nie kropka) | instalacja (instalator sam uruchomił aplikację) | tak (explorer) | 4 271 ms / 233,7 MB; 8 388 ms / 785,8 MB (z `C:\ProgramData\Battle.net\Agent\Agent.exe`) |
| Epic Games Launcher | `HKU\<SID>\…\Run` | `EpicGamesLauncher` | `"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe" -silent -launchcontext=boot` | `Epic Games, Inc.` / `EpicGamesLauncher` | **po 1. uruchomieniu** | tak (explorer) | 35 ms / 12,1 MB; 4 904 ms / 1 163 MB (w tym jednorazowa instalacja Epic Online Services przy tym starcie) |
| iTunes | `HKLM\…\Run` (64-bit) | `iTunesHelper` | `"C:\Program Files\iTunes\iTunesHelper.exe"` | `Apple Inc.` / `iTunesHelper` | instalacja | tak (explorer) | 27 ms / 0,1 MB; bez potomków |
| Discord | `HKU\<SID>\…\Run` | `Discord` | `"C:\Users\dso\AppData\Local\Discord\Discord.exe" --processStart Discord.exe --process-start-args "--start-inactive"` | `Discord Inc.` / `Discord` | instalacja | tak (explorer) | 19 ms / 2,2 MB; 5 098 ms / 129,9 MB |
| Discord (instalator maszynowy) | `HKLM\SOFTWARE\WOW6432Node\…\Run` | `Discord` | `C:\ProgramData\SquirrelMachineInstalls\Discord.exe --checkInstall` | `Discord Inc.` / `Discord - https://discord.com/` (OriginalFilename `Setup.exe`) | instalacja (bo winget działał z podniesieniem) | tak (runonce) | 18 ms / 2,3 MB; bez potomków |
| Dropbox | `HKLM\SOFTWARE\WOW6432Node\…\Run` | `Dropbox` | `"C:\Program Files (x86)\Dropbox\Client\Dropbox.exe" /systemstartup` | `Dropbox, Inc.` / `Dropbox` | instalacja | tak (runonce) | 4 088 ms / 326,3 MB; 4 803 ms / 376,9 MB |
| Google Drive | `HKU\<SID>\…\Run` | `GoogleDriveFS` | `"C:\Program Files\Google\Drive File Stream\133.0.2.0\GoogleDriveFS.exe" --startup_mode` (ścieżka z numerem wersji) | `Google LLC.` / `Google Drive` | instalacja | tak (explorer) | 47 ms / 15,4 MB; 117 ms / 18,1 MB |
| Spotify | `HKU\<SID>\…\Run` | `Spotify` | `"C:\Users\dso\AppData\Roaming\Spotify\Spotify.exe" --autostart --minimized` | `Spotify Ltd` / `Spotify` | **po 1. uruchomieniu** | tak (explorer) | 1 181 ms / 309,9 MB; 2 344 ms / 346,9 MB |
| Teams (nowy) | `HKU\<SID>\…\Run` | `Teams` | `"C:\Users\dso\AppData\Local\Microsoft\WindowsApps\MSTeams_8wekyb3d8bbwe\ms-teams.exe" msteams:system-initiated` | alias wykonania AppX – brak wersji pliku; proces w StartupInfo: `C:\Program Files\WindowsApps\MSTeams_26276.513.5241.9675_x64__8wekyb3d8bbwe\ms-teams.exe` (metadanych tego pliku nie odczytano) | **po 1. uruchomieniu** | tak (explorer) | 831 ms / 28,7 MB; 5 149 ms / 227,9 MB |
| Teams (nowy) | StartupTask AppX, rodzina `MSTeams_8wekyb3d8bbwe` | TaskId `TeamsTfwStartupTask` (DisplayName `Microsoft Teams`, `Enabled="true"` w manifeście) | `Executable="msteams_autostarter.exe"`; stan `HKU\<SID>_Classes\…\SystemAppData\MSTeams_8wekyb3d8bbwe\TeamsTfwStartupTask`: `State=2`, `UserEnabledStartupOnce=0` | `Microsoft Corporation` / `Microsoft Teams AutoStarter` | manifest od instalacji; klucz stanu dopiero po 1. uruchomieniu | tak (sihost) – obok wpisu Run, czyli Teams startuje dwiema drogami | 122 ms / 1,8 MB; 181 ms / 1,9 MB |
| Java 8 | `HKLM\SOFTWARE\WOW6432Node\…\Run` | `SunJavaUpdateSched` | `C:\Program Files (x86)\Common Files\Java\Java Update\jusched.exe` (bez cudzysłowu) | `Oracle Corporation` / `Java Update Scheduler` | instalacja | tak (runonce) | 17 ms / 1,0 MB; bez potomków (2. rozruch: 13 ms / 1,7 MB) |
| Opera | `HKU\<SID>\…\Run` | `Opera Stable` | `"C:\Users\dso\AppData\Local\Programs\Opera\opera.exe" --autostart` | `Opera Software` / `Opera Internet Browser` | instalacja | tak (explorer) | 1 840 ms / 43,9 MB; 3 960 ms / 84,2 MB |
| Opera | `HKU\<SID>\…\Run` | `Opera Browser Assistant` | `"C:\Users\dso\AppData\Local\Programs\Opera\assistant\browser_assistant.exe"` | `Opera Software` / `Opera Browser Assistant` | instalacja; instalator **sam zapisał** `StartupApproved\Run` = `02` + 11×`00` | tak (explorer) | 76 ms / 8,3 MB; 448 ms / 300,5 MB (potomkowie `opera.exe --stream`) |
| Brave | `HKU\<SID>\…\Run` | `BraveSoftware Update` | `"C:\Users\dso\AppData\Local\BraveSoftware\Update\1.3.361.151\BraveUpdateCore.exe"` (ścieżka z numerem wersji) | `BraveSoftware Inc.` / `BraveSoftware Update` | instalacja | tak (explorer) | 27 ms / 5,9 MB; 61 ms / 7,9 MB |
| CCleaner 6.41 (Slim) | `HKLM\SOFTWARE\WOW6432Node\…\RunOnce` | `ccleaner_update_helper` | `C:\Program Files\CCleaner\ccleaner_update_helper.exe` | `Piriform` / `CCleaner update helper tool` (podpis Gen Digital Inc.) | instalacja / 1. uruchomienie (był przed restartem) | tak (runonce, jednorazowo): zainstalował CCleaner 7 i odinstalował 6.41 | 9 ms / 0 MB; 4 347 ms / 64,5 MB |

### Programy bez wpisu autostartu

„Brak autostartu po instalacji i 1. uruchomieniu” (Run/RunOnce/foldery/StartupTask):

- **Ubisoft Connect** – tylko zadanie dzienne i usługa ręczna (tabela niżej).
- **Zoom Workplace** – tylko usługa `ZoomCptService` (Auto).
- **Telegram Desktop** – nic.
- **Google Chrome** (`Google.Chrome.EXE`, instalacja na konto) – nic, także brak zadań i usług aktualizatora Google.
- **Adobe Acrobat Reader** – brak wpisu Run; startuje przez zadanie + usługę (tabela niżej).
- **qBittorrent** – nic.
- **CCleaner 7** (po migracji z 6.41) – brak Run; autostart przez zadanie logowania (tabela niżej).

### Pominięty

- **Logi Options+ (Logitech.OptionsPlus)** – manifest winget ma `Installer Type: portable`: winget zapisał tylko plik instalatora jako polecenie `logioptionsplus_installer` (wpis Uninstall „Logi Options+” w `HKU`, katalog `…\WinGet\Packages\Logitech.OptionsPlus_…`). Sama aplikacja się nie zainstalowała, a uruchomienie instalatora to kreator wymagający interakcji – nie uruchamiano.

## Zadania Harmonogramu i usługi doinstalowane przez programy (tylko nazwy)

Zadania – tylko z wyzwalaczem logowania/rozruchu, chyba że zaznaczono inaczej. Usługi – nowe, typ startu w nawiasie.

| Program | Zadania | Usługi |
|---|---|---|
| Steam | – | `Steam Client Service` (Manual) |
| Epic Games Launcher | – | `EpicGamesUpdater` (Manual), `EpicOnlineServices` (Manual) |
| EA app | – | `EABackgroundService` (Manual, działała) |
| Battle.net | – | – |
| Ubisoft Connect | `\Ubisoft\Ubisoft Connect Background Update` (tylko Daily) | `UpcElevationService` (Manual) |
| (partia A, nie ustalono który) | – | `GameInputRedistService` (Manual) z pakietem Microsoft GameInput |
| Zoom | – | `ZoomCptService` (**Auto**) |
| Dropbox | `\DropboxSystem\DropboxUpdater\DropboxUpdaterTaskSystem<wersja>{GUID}` (Logon + Daily; po samoaktualizacji dwa) | `DbxSvc` (**Auto**, plik `C:\Windows\System32\DbxSvc.exe`), `DropboxUpdaterService<wersja>` i `DropboxUpdaterInternalService<wersja>` (**Auto**), `DropboxElevationService` (Manual); dodatkowo pakiet AppX `DropboxInc.Dropbox_wkt425jdc3sga` |
| Google Drive | `\GoogleSystem\GoogleUpdater\GoogleUpdaterTaskSystem<wersja>{GUID}` (Logon + Daily) | `GoogleUpdaterService<wersja>`, `GoogleUpdaterInternalService<wersja>` (**Auto**) |
| Teams | – | – (dodatkowo wpis Uninstall „Microsoft Teams Meeting Add-in for Microsoft Office”) |
| Spotify, Discord, Telegram, iTunes | – | – |
| Opera | `\Opera scheduled Autoupdate <liczba>` (Daily + Logon, RunLevel Highest), `\Opera scheduled assistant Autoupdate <liczba>` (Daily + Logon) | – |
| Brave | `\BraveSoftwareUpdateTaskUser<SID>Core{GUID}`, `\BraveSoftwareUpdateTaskUser<SID>UA{GUID}` (tylko Daily) | – |
| Adobe Acrobat Reader | `\Adobe Acrobat Update Task` (Logon + Daily) | `AdobeARMservice` (**Auto**, działała) |
| CCleaner 6.41 (przed migracją) | `\CCleaner Update` (Daily + Boot), `\CCleanerCrashReporting` (Daily), `\CCleanerSkipUAC - dso` (bez wyzwalacza) – usunięte przy przejściu na 7 | – |
| CCleaner 7 | `\Piriform\CCleaner 7 - <SID dso>` i `\Piriform\CCleaner 7 - <SID Administratora -500>` (Logon, `"C:\Program Files\Piriform\CCleaner 7\CCleaner.exe" /bg`, Highest), `\Piriform\CCleaner 7 BugReport`, `\Piriform\CCleaner 7 Update` (Boot + Daily) | `CCleaner7` (**Auto**) |
| Java 8, qBittorrent, Chrome (na konto) | – | – |

CCleaner 7 po drugim restarcie: `CCleaner.exe /bg` działał (rodzic: usługa Harmonogramu, start ok. 10 s po rozruchu, przed `explorer.exe`), ale **nie ma go w StartupInfo**.

## Obserwacje pomocnicze

- `StartupApproved`: instalatory poza Operą nic nie zapisują. Po pomiarze partii C pojawiło się `02` + 11×`00` w `HKU\<SID>\…\StartupApproved\Run` dla `OneDrive`, `Opera Stable`, `BraveSoftware Update`, `CCleaner Smart Cleaning` (bez odpowiadającej wartości w `Run`) i w `HKLM\…\StartupApproved\Run32` dla `SunJavaUpdateSched` – między instalacją a odczytem po restarcie, tylko w partii z CCleanerem; kto zapisał – nieustalone.
- RunOnce w `WOW6432Node` uruchamia się przez `runonce.exe` tak jak `WOW6432Node\Run`.
- Szum Windows wykluczony z tabel: OneDrive (`OneDriveSetup` → `OneDrive`, zadania `OneDrive … Task-<SID>`), `msedgewebview2` `setup.exe … --on-logon` przez `runonce.exe`, `PcaSvc` Manual → Auto (opóźniony), usługi per użytkownik `*_<hex>`, aktualizacje pakietów AppX Microsoft.

## Surowe dowody

Surowe odczyty są przechowywane poza repozytorium.
