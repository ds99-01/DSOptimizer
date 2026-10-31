# Partia C5 – źródła i mechanizmy (research 2026-10-19)

Zakres (spec Etapu 2, sekcja 5, partia 5, oraz sekcja 11): `panels.*` (aliasy `tool_vis`, `tool_pow`, `tool_network` + panele legacy z WinUtil),
`tools.restart-explorer` (alias `tool_restart_explorer`), `tools.store-repair` (alias `tool_msstore`), `tools.winutil` (alias `tool_winutil`).
Spec: specyfikacja w archiwum autora. Format i legenda jak w `2026-10-18-partia-c3-zrodla.md` i `2026-10-18-partia-c4-zrodla.md`.
Zasada: dowodem jest MS Learn / dokumentacja MS albo test na VM 26H2. Kod WinUtil (MIT) służy jako źródło listy i wskazówka, nie dowód skutku.
Wszystkie URL: data dostępu 2026-10-19. Rodzaj źródła: **MS** = learn.microsoft.com / support.microsoft.com (strona bieżąca); **MS-archiwum** = stara
dokumentacja MS (może być nieaktualna); **MS-Q&A** = odpowiedź społeczności na learn.microsoft.com/answers (to NIE jest dokumentacja, tylko hint);
**WinUtil** = repo ChrisTitusTech/winutil (konkretny plik i commit); **hint** = prasa, fora.
Oznaczenia: „wniosek” = interpretacja źródła; „brak źródła” = nie znalazłem dokumentu; „hipoteza” = do potwierdzenia na VM 26H2 przed wpisem do katalogu.
Nie wykonywano żadnych poleceń na hoście (tylko pobranie stron i plików tekstowych). Wartości domyślne „z VM” nie są jeszcze zmierzone.

## 0. Co robił v3.2 (poprzednia, niepubliczna wersja 3.2)

- `tool_vis`: `Start-Process 'control.exe' -ArgumentList 'sysdm.cpl,,3'` (core_models.py:99). Opis: „Otwiera natywny panel dostosowywania wyglądu Windows”.
- `tool_pow`: `Start-Process 'control' -ArgumentList 'powercfg.cpl'` (core_models.py:107). Opis obiecywał „Ultimate Performance Plan” (panel go nie tworzy).
- `tool_network`: `Start-Process 'ncpa.cpl'` (core_models.py:115). CHANGELOG_v3.2.md:47: panele zmienione na `Start-Process`, żeby nie blokowały
  powershell.exe (stąd wymóg „bez `-Wait`”).
- `tool_restart_explorer` (core_models.py:422-433): `taskkill /f /im explorer.exe`, `Start-Sleep -Milliseconds 1000`, `start explorer.exe`, zawsze
  komunikat „Explorer zrestartowany!”. Błąd z review v3.2 (mapa drogowa autora, poz. 22 i 94): Explorer startowany z procesu admina działa z tokenem admina.
- `tool_msstore` (core_models.py:75): `wsreset.exe -i`, opis „Przywraca Microsoft Store na LTSC”. Mapa drogowa autora, poz. 89: „`wsreset -i` zawodne na LTSC”.
- `tool_winutil` (core_models.py:91): `irm https://christitus.com/win | iex`, `run_detached=True`, ostrzeżenie „Pobiera kod z christitus.com”.

## 0b. Co silnik umie – rzeczy istotne dla paneli i narzędzi (sprawdzone w kodzie, nie ze źródeł MS)

- Model ma `TweakKind.Panel` (`src/DSOptimizer.Core/Model/TweakKind.cs:11`, „Otwarcie panelu systemowego”). CLI `--execute <id>` obsługuje
  `action|panel` (zasady repo). W katalogu wbudowanym nie ma jeszcze żadnej pozycji `kind=panel` (grep po `"kind": "panel"` w `src/DSOptimizer.Catalog/catalog` pusty).
- Walidator: akcja i panel wymagają `script.apply` i nie mogą mieć `registry[]`/`services[]` (`CatalogValidator.cs:166`, `:171`).
  Wzorzec akcji: `net.json` (`net.smb-harden`, `net.network-private`: pola `risk`, `scope`, `warnings[]`, `script`).
- `TweakEngine.Execute` uruchamia `script.apply` przez `IScriptRunner` (`TweakEngine.Execute.cs:32`); kod 0 = `Executed` (stdout trafia do `message`),
  inny = `Failed` z treścią stderr.
- `PowerShellScriptRunner` obejmuje proces powershell.exe Job Objectem z `KILL_ON_JOB_CLOSE` (`Native/JobObject.cs:8`, `:22`;
  `PowerShellScriptRunner.cs:159`, `:201`) i po wyjściu głównego procesu woła `job.Terminate()` (`PowerShellScriptRunner.cs:234`, komentarz:
  „job zabija resztę drzewa”).
- **Hipoteza (brak źródła MS w tym researchu, do potwierdzenia testem):** proces uruchomiony ze skryptu przez `Start-Process` należy do tego samego
  Job Objectu, więc okno panelu (albo osobne okno PowerShell dla WinUtil) zostanie zabite zaraz po zakończeniu skryptu. Wyjątek możliwy, gdy powłoka
  przekaże otwarcie do już działającego procesu (np. `explorer.exe`) – niezmierzone. To dotyczy wszystkich pozycji C5, które mają zostawić żyjący proces.

## 1. Lista paneli legacy z WinUtil (źródło listy)

**Plik:** `config/feature.json`, repo https://github.com/ChrisTitusTech/winutil, gałąź `main` (WinUtil).
Pobrany z commitu `144d51675190ffcb2553b7ba5313f8d602c8ecf9` (2026-07-31T20:29:34Z, „chore: Update generated dev docs (#4895)”; GitHub API
`commits?path=config/feature.json`).
URL: https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json
Poprzedni commit zmieniający plik: `ac11c73b6406f2d641f9401176bc9eb20de2e3e6` (2026-07-13T16:37:22Z), „feat(legacy-panels): add Firewall, Security,
Programs, and Mouse to L… (#4809)” (temat uciął API) – ten commit dodał cztery ostatnie panele. Licencja repo: MIT, „Copyright (c) 2022 CT Tech Group LLC”
(https://raw.githubusercontent.com/ChrisTitusTech/winutil/main/LICENSE).

Wpisy mają `"category": "Legacy Windows Panels"`, `"panel": "2"`, `"Type": "Button"`; polecenie jest w `InvokeScript`. Dosłownie z pliku:

| Klucz WinUtil | Content (etykieta) | InvokeScript (dosłownie) |
|---|---|---|
| WPFPanelComputer | Computer Management | `compmgmt.msc` |
| WPFPanelControl | Control Panel | `control` |
| WPFPanelMouse | Mouse Properties | `main.cpl` |
| WPFPanelNetwork | Network Connections | `ncpa.cpl` |
| WPFPanelPower | Power Panel | `powercfg.cpl` |
| WPFPanelPrinter | Printer Panel | `Start-Process 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'` |
| WPFPanelPrograms | Programs and Features | `appwiz.cpl` |
| WPFPanelRegion | Region | `intl.cpl` |
| WPFPanelSecurity | Security and Maintenance | `wscui.cpl` |
| WPFPanelSound | Sound Settings | `mmsys.cpl` |
| WPFPanelSystem | System Properties | `sysdm.cpl` |
| WPFPanelTimedate | Time and Date | `timedate.cpl` |
| WPFPanelFirewall | Windows Defender Firewall | `firewall.cpl` |
| WPFPanelRestore | Windows Restore | `rstrui.exe` |

Pozostałe wpisy pliku (`panel` 1, kategorie Fixes, Features) to nie panele legacy i nie wchodzą do C5.
Uwaga o formie: `InvokeScript` to jednolinijkowy tekst wykonywany przez WinUtil jako PowerShell (bez argumentów, np. samo `ncpa.cpl`).
Dla DSOptimizera to wzorzec danych (nazwy plików, CLSID), nie kod do kopiowania; wg zasad repo sam wzorzec danych nie wymaga wpisu w `THIRD_PARTY_NOTICES.md`.

**Wnioski**
- Lista legacy z WinUtil ma 14 pozycji. Dwie są tymi samymi panelami co v3.2 (Network = `tool_network`, Power = `tool_pow`), więc pozycji legacy bez
  aliasu jest 12. System Properties (`sysdm.cpl`) jest pokrewny `tool_vis`, ale to inne okno (WinUtil otwiera `sysdm.cpl` bez zakładki, `tool_vis` nie ma
  odpowiednika w WinUtil).
- CLSID `{A8A91A66-3A7D-4424-8D24-04E180695C7A}` to według MS Learn „Devices and Printers” (sekcja 2.2), czyli „Printer Panel” z WinUtil otwiera
  Urządzenia i drukarki, nie folder Drukarki.

## 2. Mechanizmy otwierania paneli (MS Learn)

### 2.1 Jak MS każe uruchamiać elementy Panelu sterowania
Źródło (MS): „Executing Control Panel Items” (ms.date 2018-05-31, updated_at 2025-03-11),
https://learn.microsoft.com/en-us/windows/win32/shell/executing-control-panel-items
- „To start a Control Panel item with its canonical name, use: "%systemroot%\system32\control.exe /name *canonicalName*"”.
- „When a Control Panel item is opened through a command line, you can instruct it to open to a particular tab in the item. Due to the addition and
  removal of certain tabs in some Windows Vista Control Panel items, the numbering of the tabs might have changed from that in Windows XP.”
  Przykład z tej strony: „control.exe sysdm.cpl,,3” (MS: czwarta zakładka w XP, trzecia w Vista).
- „Performance: %windir%\system32\SystemPropertiesPerformance.exe” (sekcja „System”); dalej „Remote access: …SystemPropertiesRemote.exe”,
  „Advanced system properties: …SystemPropertiesAdvanced.exe”.
- „Power Options”: „There is no canonical command for the Advanced Settings page, it is accessed in the older manner: %windir%\system32\control.exe powercfg.cpl,,3”.
- „Add or remove programs: %windir%\system32\control.exe /name Microsoft.ProgramsAndFeatures”.
- Legacy: „control.exe netconnections” – „Displays the **Network Connections** folder”.
- Strona dokumentuje Windows Vista; nie wspomina Windows 11/26H2.

**Wnioski**
- `tool_vis`: v3.2 użyło `sysdm.cpl,,3`. MS nie podaje, którą zakładką jest „3” w Windows 11 (numeracja się zmieniała – cytat wyżej), a udokumentowany
  mechanizm dla wydajności to `SystemPropertiesPerformance.exe` (okno „Opcje wydajności”). Że to okno zawiera przełączniki „Efekty wizualne”, wynika
  z nazwy panelu w MS; dosłownego cytatu o zawartości okna brak → odczyt na VM (pytanie 1).
- `tool_pow`: `powercfg.cpl` jest w WinUtil; MS Learn zna `control.exe /name Microsoft.PowerOptions` (+ strony `pageGlobalSettings`, `pagePlanSettings`).
- `tool_network`: `ncpa.cpl` (WinUtil, v3.2); MS: `control.exe netconnections` (legacy).

### 2.2 Nazwy kanoniczne i moduły
Źródło (MS): „Canonical Names of Control Panel Items” (ms.date 2018-05-31, updated_at 2025-04-15),
https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names
Strona wymienia elementy „available in Windows 8.1” (nie 11/26H2).

| Panel | Nazwa kanoniczna | Moduł / GUID (z MS) |
|---|---|---|
| Power Options | `Microsoft.PowerOptions` | `@%SystemRoot%\System32\powercpl.dll,-1`, `{025A5937-A6BE-4686-A844-36FE4BEC8B6D}` |
| Network and Sharing Center | `Microsoft.NetworkAndSharingCenter` | `netcenter.dll` (to inny panel niż „Połączenia sieciowe”) |
| System | `Microsoft.System` | `systemcpl.dll,-1` |
| Sound | `Microsoft.Sound` | `mmsys.cpl,-300` |
| Region | `Microsoft.RegionAndLanguage` | `intl.cpl,-1` |
| Date and Time | `Microsoft.DateAndTime` | `timedate.cpl,-51` |
| Mouse | `Microsoft.Mouse` | `main.cpl,-100` |
| Programs and Features | `Microsoft.ProgramsAndFeatures` | `appwiz.cpl,-159` |
| Windows Firewall | `Microsoft.WindowsFirewall` | `FirewallControlPanel.dll,-12122` |
| Devices and Printers | `Microsoft.DevicesAndPrinters` | `{A8A91A66-3A7D-4424-8D24-04E180695C7A}` (= CLSID z WinUtil „Printer Panel”) |
| Windows Defender (stara nazwa) | `Microsoft.WindowsDefender` | – |
| Action Center | `Microsoft.ActionCenter` | tylko „Windows 7, Windows 8, Windows 8.1” |

- „Not all Control Panel items are present in all varieties of Windows.” i „Some Control Panel items only appear if the right hardware is detected on the system.”
- Tabela „Deprecated”: `Microsoft.SecurityCenter` – „Maps to Microsoft.ActionCenter as of Windows 7”; `Microsoft.Language` – „Removed as of Windows 10,
  version 1803”; uwaga: „Remappings exist for backward compatibility. You should not use deprecated values in new code.”

**Wnioski**
- Na liście nazw kanonicznych nie ma „Network Connections” (`ncpa.cpl`): to folder powłoki, nie element z nazwą kanoniczną. Uruchamianie przez
  `ncpa.cpl` (WinUtil, v3.2) albo `control.exe netconnections` (MS, legacy).
- „Security and Maintenance” (`wscui.cpl`) wiąże się z Action Center (`Microsoft.ActionCenter`, w dokumencie „Windows 7, 8, 8.1”); nazwy dla Windows 11
  MS nie dokumentuje → brak źródła; ryzyko pustego panelu (pytanie 4).

### 2.3 Panele przenoszone do Ustawień (26H2)
- MS: „System configuration tools in Windows”
  (https://support.microsoft.com/en-us/windows/system-configuration-tools-in-windows-f8a49657-b038-43b8-82d3-28bea0c5666b):
  „Many of the settings in Control Panel are in the process of being migrated to the Settings app, which offers a more modern and streamlined experience.”
  „Tip: while the Control Panel still exists for compatibility reasons and to provide access to some settings that have not yet migrated, you're encouraged
  to use the Settings app, whenever possible.” Strona o „Advanced System Settings”: „a system utility that you can use to access and modify settings that
  are not typically available in Settings or Control Panel”.
- MS: „Removed features” (ms.date 2026-08-25, https://learn.microsoft.com/en-us/windows/whats-new/removed-features), wiersz: „Language control in the
  Control Panel | Use the Settings app to change your language settings. | 1803”.
- MS: „Deprecated features” (ms.date 2026-09-23, https://learn.microsoft.com/en-us/windows/whats-new/deprecated-features): w przejrzanej treści
  (grep po „control panel”, „26H2”) jedyny wiersz Panelu to „System Image Backup (SIB) Solution … known as the **Backup and Restore (Windows 7)** legacy
  control panel” (1709). **Brak źródła MS** wskazującego konkretny panel z C5 jako usunięty lub przekierowany w 26H2. Zastrzeżenie: stronę przejrzano
  grepem, nie linia po linii.
- Hint (nie dowód, tylko trop do VM): Windows Latest, 2026-04-07, cytat Marcha Rogersa (Partner Director of Design, Microsoft; odpowiedź na wpis na X):
  „working our way through migrating all the old control panel controls into the modern Settings apps”; artykuł mówi, że niektóre pozycje nadal otwierają
  Panel (ręczne dodanie drukarki, niektóre ustawienia sieci, Menedżer urządzeń):
  https://windowslatest.com/2026/04/07/microsoft-explains-why-it-still-cant-fully-kill-control-panel-in-windows-11 .
  Hint: Windows Latest 2021-12-17 / Windows Central – Programs and Features i zaawansowane udostępnianie przeniesione/przekierowane do Ustawień w niektórych
  buildach (tylko tytuły artykułów, treści nie czytałem). Hint: TechRadar / Winaero – opcje myszy migrują do Ustawień (tylko tytuły).

**Wnioski**
- MS potwierdza tylko tyle: migracja trwa, Panel istnieje dla zgodności. Nie wolno obiecywać, że `appwiz.cpl`, `main.cpl`, `ncpa.cpl` itd. otwierają okno
  klasyczne, a nie Ustawienia (albo nic). Każdy panel w katalogu musi mieć odczyt z VM (czy proces `control.exe`/`rundll32`/`mmc` tworzy okno, jaki ma
  tytuł i klasę).
- Opis w katalogu: „Otwiera panel systemowy Windows (klasyczny, jeśli system go zawiera)”; skutek „otwarte okno” sprawdza test VM.
- Fallback do Ustawień (jeśli autor zechce, decyzja 2): MS „Launch the Windows Settings app” (ms.date 2026-09-26,
  https://learn.microsoft.com/en-us/windows/uwp/launch-resume/launch-settings-app) podaje m.in. `ms-settings:powersleep` („Power & sleep”),
  `ms-settings:network-status`, `ms-settings:network-advancedsettings`, `ms-settings:appsfeatures`, `ms-settings:about`.

## 3. panels.* – pozycje i źródła

### 3.1 `panels.visual-effects` (alias `tool_vis`)
- Źródła: v3.2 `sysdm.cpl,,3` (core_models.py:99); MS „Executing Control Panel Items” (2.1): `SystemPropertiesPerformance.exe`; ten sam dokument:
  numeracja zakładek zmienia się między wersjami.
- Wniosek: preferować `SystemPropertiesPerformance.exe` (nazwany plik z dokumentacji) zamiast zgadywanej zakładki `,,3`. Plik jest w `%windir%\system32` (MS).
  Na VM: czy istnieje na Home i Pro i czy otwiera okno „Opcje wydajności”.
- Bez `-Wait`. Kod wyjścia 0 oznacza tylko uruchomienie procesu, nie „okno widoczne”.

### 3.2 `panels.power-options` (alias `tool_pow`)
- Źródła: v3.2 `control powercfg.cpl`; WinUtil `powercfg.cpl`; MS: `control.exe /name Microsoft.PowerOptions` oraz „control.exe powercfg.cpl,,3” dla
  Advanced Settings (cytat w 2.1).
- Wniosek: dwie drogi udokumentowane przez MS (nazwa kanoniczna i `powercfg.cpl`). Brak źródła MS, czy na Windows 11 (w tym 26H2) otwiera to Panel,
  czy Ustawienia → VM (pytanie 3). Opis bez „Ultimate Performance Plan” z v3.2.

### 3.3 `panels.network-connections` (alias `tool_network`)
- Źródła: v3.2 `ncpa.cpl`; WinUtil `ncpa.cpl`; MS (legacy) `control.exe netconnections` – „Displays the **Network Connections** folder”.
- Wniosek: `ncpa.cpl` ma dwa niezależne źródła (WinUtil, v3.2) + ekwiwalent MS `netconnections`. Opis v3.2 („Wyłącz IPv6, zmień prędkość karty”) to
  czynności ręczne użytkownika w panelu (tweaki są w `net.*`), nie obietnica skutku.

### 3.4 Panele legacy z WinUtil (kandydaci)
Wszystkie 14 z tabeli w sekcji 1 (12 bez aliasu). Podział według siły źródeł (moja klasyfikacja):
- A. Źródło MS (nazwa kanoniczna lub moduł) + WinUtil: Power, Mouse, Network (netconnections), Programs and Features, Region, Sound, Time and Date,
  System Properties, Printer (przez CLSID Devices and Printers), Firewall.
- B. Tylko WinUtil, bez nazwy w MS Learn dla Windows 11: Security and Maintenance (`wscui.cpl`), Windows Restore (`rstrui.exe`), Computer Management
  (`compmgmt.msc`; MMC), Control Panel (`control`).
  `rstrui.exe` to narzędzie Przywracania systemu – otwiera kreator, nie wykonuje przywrócenia; brak źródła MS w tym researchu. Odczyt z repo (mapa drogowa autora,
  dziennik 2026-10-16, spike VM): na VM „Ochrona systemu na C: wyłączona, 0 punktów przywracania” – na VM sprawdzić, co pokazuje kreator w tym stanie.
- Ryzyka wspólne: `.cpl` może zostać w systemie jako stub przekierowujący do Ustawień (2.3). Test VM ma wtedy wykryć „okno nie jest klasycznym panelem”,
  a pozycja dostaje opis zgodny z faktem albo odpada (spec 11: „Pozycja odpada”).
- `kind=panel`, `risk=safe` (spec): nie zmienia stanu systemu, tylko uruchamia proces. Kryterium „wykonano” = proces wystartował i (jeśli test VM to
  potwierdzi) pojawiło się okno; sam kod 0 to za mało dla pozycji, które w 26H2 mogą być stubem. Zobacz też 0b (Job Object).

## 4. tools.restart-explorer (alias `tool_restart_explorer`)

**Źródła**
- MS-archiwum: Windows Server 2003 Technical Reference, „AutoRestartShell: Core Services” (ms.date 2009-10-08, updated_at 2021-11-12, `is_archived: true`),
  https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc776814(v=ws.10)
  - Klucz: „HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon”, typ `REG_DWORD`, zakres „0 | 1”, domyślnie „1”.
  - „Specifies whether the Windows user interface (typically, Explorer.exe) restarts automatically if it stops unexpectedly.”
  - Wartość 1: „…the interface restarts automatically.” Wartość 0: „…the user must restart the interface.”
  - To samo źródło: użytkownik może uruchomić ręcznie „Explorer.exe” przez Menedżer zadań > Plik > Uruchom nowe zadanie (Run).
  - Strona dotyczy Windows Server 2003; **brak źródła MS dla Windows 11/26H2** (wyszukiwanie zwróciło tylko archiwalne strony Windows 2000/2003).
- Odczyt z repo (mapa drogowa autora, dziennik 2026-10-16): na VM Explorer działa jako `dso`, UAC włączony (`EnableLUA=1`), PowerShell Direct łączy się jako wbudowany
  `Administrator` z pełnym tokenem.

**Wnioski i luki**
- Mechanizm „zatrzymaj Explorera, winlogon go podniesie” opiera się na archiwalnym opisie. Czy na 26H2 działa tak samo i czy wartość domyślnie istnieje
  (czy jej brak = 1) – **brak źródła, odczyt na VM** (pytania 5, 6).
- Twierdzenie „Explorer odrodzony przez winlogon dostaje token użytkownika, a nie admina” (mapa drogowa autora, poz. 94; spec) – **brak źródła MS**; hipoteza do pomiaru
  na VM (pytanie 7), bo cały sens pozycji na tym stoi.
- Ryzyko: przy `AutoRestartShell` = 0 zabicie Explorera zostawi pusty pulpit. Aplikacja nie powinna zmieniać tej wartości (zapis rejestru poza zakresem
  pozycji `safe`); wniosek: najpierw odczyt, przy 0 odmowa z jawnym komunikatem.
- v3.2 zabijał `explorer.exe` globalnie (`taskkill /f /im`) i zawsze pisał „zrestartowany”. Wniosek dla 4.0: weryfikacja = nowy PID Explorera w sesji
  zalogowanego użytkownika, inaczej `Failed`. Ostrzeżenie dla UI: otwarte okna Explorera zostaną zamknięte.
- Hipoteza do VM: aplikacja działa jako admin, często innym kontem niż zalogowany użytkownik (zasady repo, pułapki) – zatrzymać trzeba tylko Explorera
  sesji zalogowanego użytkownika (SessionId, właściciel), nie wszystkich.

## 5. tools.store-repair (alias `tool_msstore`)

**Źródła**
- MS: `Add-AppxPackage` (ms.date 2023-05-15, updated_at 2025-05-14), https://learn.microsoft.com/en-us/powershell/module/appx/add-appxpackage
  - Przykład 3: „Add a disabled app package in development mode”:
    `$InstallLocation = Get-AppxPackage -Name '*WindowsCalculator*' | Select-Object -ExpandProperty InstallLocation; $ManifestPath = $InstallLocation + '\Appxmanifest.xml'; Add-AppxPackage -Path $ManifestPath -Register -DisableDevelopmentMode`.
  - „You can use **DisableDevelopmentMode** to register an application that's staged by the **StagePackageAsync** API, has been disabled, or has become corrupted during testing.”
  - `-DisableDevelopmentMode`: „Indicates that this cmdlet registers an existing app package installation that has been disabled, didn't register, or has become corrupted.”
  - `-Register`: „To register an existing app package installation, you must specify the **DisableDevelopmentMode** parameter and the **Register** parameter.”
  - Parametry `-ForceApplicationShutdown`, `-ForceUpdateFromAnyVersion` istnieją (niepotrzebne w minimalnej wersji).
- MS: `Get-AppxPackage` (ms.date 2023-05-15), https://learn.microsoft.com/en-us/powershell/module/appx/get-appxpackage – parametr `-AllUsers`
  (wymaga podniesienia uprawnień; dosłownego cytatu o wymaganiach nie pobrałem – brak cytatu).
- MS: „What's new in Windows 11 Enterprise LTSC 2024” (ms.date 2026-07-15), https://learn.microsoft.com/en-us/windows/whats-new/ltsc/whats-new-windows-11-2024 :
  „Support for LTSC by apps and tools, such as in-box apps and Microsoft Store, that are designed for the general availability channel release of Windows
  might be limited.” Jedyny dosłowny cytat MS o Store na LTSC, który znalazłem; nie mówi „nie ma Store”, mówi „wsparcie może być ograniczone”.
- MS-Q&A: „Get-AppXPackage *WindowsStore* -AllUsers | Foreach {Add-AppxPackage -DisableDevelopmentMode -Register "$($_.InstallLocation)\AppXManifest.xml"}” –
  odpowiedź „Independent Advisor” (nie pracownik MS), 2024-08-06,
  https://learn.microsoft.com/en-us/answers/questions/3983846/how-to-reinstall-microsoft-store-app-in-windows-11 . W tym samym wątku inni użytkownicy
  zgłaszają błędy: „Cannot find path 'C:\AppXManifest.xml'” i 0x80072EFD. Strona wspomina też `wsreset.exe -i` jako krok resetu cache.
- `wsreset.exe`: MS „Microsoft Store doesn't launch – reset the cache”
  (https://support.microsoft.com/en-au/windows/microsoft-store-doesn-t-launch-reset-the-cache-df2aea11-6338-418a-9515-434c5e910adb) opisuje `wsreset.exe`
  z okna Uruchom i puste okno wiersza polecenia, które po około dziesięciu sekundach się zamyka, a Store się otwiera – przekaz z wyników wyszukiwania;
  pobrana strona zwróciła stronę główną, **brak dosłownego cytatu**. Przełącznik `-i`: **brak źródła MS** (pojawia się tylko w odpowiedziach społeczności).
- Hint: obejście dla LTSC przez społecznościowe skrypty (np. repo `kkkgo/LTSC-Add-MicrosoftStore` wg wyników Microsoft Q&A) – nieoficjalne,
  **nie źródło do katalogu**.

**Wnioski**
- Udokumentowany przez MS mechanizm „ponowna rejestracja istniejącego pakietu” = `Add-AppxPackage -Register -DisableDevelopmentMode` z manifestu
  zainstalowanej paczki (Przykład 3). To odpowiada specowi („ponowna rejestracja Appx Store”).
- Nazwa pakietu Store (`Microsoft.WindowsStore`) i położenie manifestu – z `Get-AppxPackage` w czasie wykonania, nie z pamięci. **Brak źródła MS** dla
  nazwy pakietu (pojawia się w poście społeczności jako `*WindowsStore*`) → VM (pytanie 8).
- Co robi (a czego nie): rejestruje istniejącą instalację; nie instaluje Store, gdy pakietu nie ma na dysku. Na LTSC (brak pakietu) akcja nie ma czego
  rejestrować → jawny komunikat „Store nie jest zainstalowany; ta akcja go nie zainstaluje” (spec: „niepewne/wymaga sieci”). Obietnica v3.2
  „Przywraca Microsoft Store na LTSC” nie ma źródła i w 4.0 nie wraca.
- Kontekst użytkownika: opis cmdletu mówi „adds a signed app package to a user account”. Aplikacja działa jako admin, często innym kontem niż
  zalogowany użytkownik → rejestracja może trafić do złego konta. Hipoteza do pomiaru na VM (pytanie 9), ewentualnie `-AllUsers` w `Get-AppxPackage`
  tylko do odczytu manifestów, rejestracja dla bieżącego konta.
- Weryfikacja sukcesu: `Get-AppxPackage Microsoft.WindowsStore` po akcji (`Status` i pola pakietu) zamiast kodu wyjścia. Na VM bez sieci nie da się
  sprawdzić, czy Store się uruchamia i loguje (pytanie 10).
- Ryzyko `caution`: zmienia rejestrację pakietu systemowego; błąd może zostawić Store niezarejestrowany; brak undo (akcja, nie tweak) – ostrzeżenie w `warnings[]`.

## 6. tools.winutil (alias `tool_winutil`)

**Źródła**
- WinUtil: README (https://raw.githubusercontent.com/ChrisTitusTech/winutil/main/README.md), sekcja „Quick Start”:
  „> **WinUtil must be run as Administrator** because it performs system-wide changes.”
  „Open PowerShell or Terminal as admin, then run:” – „**Stable Branch (recommended)**”: dosłownie `irm https://christitus.com/win | iex`;
  „**Development Branch**”: `irm https://christitus.com/windev | iex`.
  Wariant z presetem (README): `& ([ScriptBlock]::Create((irm https://christitus.com/win))) -Preset Standard` (presety: `Standard`, `Minimal`, …).
- Licencja: MIT (`LICENSE`, „Copyright (c) 2022 CT Tech Group LLC”).
- v3.2: `irm https://christitus.com/win | iex` w `run_detached`; opis: „Unikaj zakładki 'Tweaks' na LTSC!” (porada z v3.2, **brak źródła**).

**Wnioski**
- Komenda oficjalna jest dosłownie taka sama jak w v3.2. Kod pobierany w czasie uruchomienia = **kod z internetu wykonywany jako admin, niezweryfikowalny
  przez DSOptimizer**. Zgodnie z zasadą projektu („nie wymyślamy skutków”) opis może obiecać tylko „otwiera osobne okno PowerShell i uruchamia polecenie
  z README WinUtil”.
- Wymogi ze specu: osobne okno (`powershell.exe` z `-NoExit` i `-Command` jako zwykły łańcuch argumentów – **bez** `-EncodedCommand`), ostrzeżenie
  „uruchamiasz cudzy kod z internetu jako admin”, bez blokowania. Zasady repo każą uruchamiać skrypty przez `-File`; tu polecenie to jedna linijka z README,
  więc `-Command` z literalnym tekstem albo mały plik `.ps1` z BOM (decyzja implementacyjna). Zobacz też 0b: osobne okno nie może należeć do Job Objectu skryptu.
- Na VM bez sieci pozycja jest niesprawdzalna: można zmierzyć tylko, że powstaje osobne okno PowerShell z oczekiwaną linią poleceń (parametry procesu),
  a nie że WinUtil działa. Wynik testu skutku = `Skipped` z powodem (spec 11).
- Ryzyko supply chain: `christitus.com/win` przekierowuje do aktualnego `winutil.ps1`; zawartość zmienia się bez wersjonowania po stronie DSOptimizera.
  Czy i jak przekierowanie jest podpisane – brak źródła.
- README poleca „PowerShell or Terminal as admin”; DSOptimizer jest już adminem (manifest), więc wniosek: osobne okno dziedziczy podniesienie, bez `-Verb RunAs`.

## Tabela zbiorcza

| Pozycja katalogu | Alias | Kind / risk | Mechanizm (źródło) | Status źródeł | Weryfikacja na VM (bez sieci) |
|---|---|---|---|---|---|
| `panels.visual-effects` | `tool_vis` | panel / safe | `SystemPropertiesPerformance.exe` (MS); v3.2: `control.exe sysdm.cpl,,3` | MS: nazwa pliku; brak: czy to nadal okno w 26H2 | proces + okno, Home i Pro |
| `panels.power-options` | `tool_pow` | panel / safe | `powercfg.cpl` (WinUtil, v3.2) lub `control.exe /name Microsoft.PowerOptions` (MS) | MS + WinUtil; brak: zachowanie w 26H2 | proces + okno |
| `panels.network-connections` | `tool_network` | panel / safe | `ncpa.cpl` (WinUtil, v3.2); MS: `control.exe netconnections` | WinUtil + MS (legacy) | proces + okno |
| `panels.*` legacy (12 pozycji z 3.4) | brak aliasu | panel / safe | polecenia z `config/feature.json`, commit `144d516…` (2026-07-31) | WinUtil (konkretny plik); część z nazwami MS | każdy osobno: okno klasyczne czy Ustawienia/nic |
| `tools.restart-explorer` | `tool_restart_explorer` | action / safe | zatrzymanie `explorer.exe`, restart przez winlogon (`AutoRestartShell`, MS-archiwum 2003) | MS: tylko archiwum; brak: token użytkownika, 26H2 | nowy PID, token (pytanie 7), `AutoRestartShell` |
| `tools.store-repair` | `tool_msstore` | action / caution | `Add-AppxPackage -Register -DisableDevelopmentMode` z manifestu (MS, Przykład 3) | MS: mechanizm; MS-Q&A: nazwa/komenda; brak: `wsreset -i` | `Get-AppxPackage` przed/po; ścieżka „brak pakietu” |
| `tools.winutil` | `tool_winutil` | action / caution | `irm https://christitus.com/win \| iex` (README WinUtil) w osobnym oknie | README dosłownie; MIT | tylko linia poleceń okna; skutek `Skipped` (brak sieci) |

## Otwarte pytania dla eksploracji VM (zbiorczo)

Wszystko tylko w VM 26H2 Home i Pro (nigdy na hoście autora), bez sieci.
1. `Test-Path "$env:windir\System32\SystemPropertiesPerformance.exe"`; po uruchomieniu odczyt tytułu okna (`(Get-Process SystemPropertiesPerformance).MainWindowTitle`)
   i czy są przełączniki efektów (UI Automation lub zrzut). Porównanie z `control.exe sysdm.cpl,,3` – którą zakładkę otwiera na 26H2.
   → rozstrzygnięte: plik istnieje, okno „Opcje wydajności” (`#32770`); `control.exe sysdm.cpl,,3` otwiera „Właściwości systemu” (`SystemPropertiesAdvanced.exe`), nie to okno. Karta Zaawansowane to wniosek z nazwy procesu; przełączników efektów automat nie czytał (klik autora: „Efekty wizualne”).

2. Dla każdego panelu z 3.1–3.4: uruchomienie polecenia i po 3 s `Get-Process | Where-Object MainWindowTitle` (nazwa procesu i tytuł); odróżnić klasyczne
   okno (proces `control`, `rundll32`, `mmc`, `explorer`, `SystemPropertiesPerformance`) od Ustawień (`SystemSettings.exe`, `ApplicationFrameHost`). Home i Pro osobno.
   → rozstrzygnięte: tabela paneli w sekcji „Wyniki VM”; Home = Pro. Jedyne przekierowanie do Ustawień: `control.exe /name Microsoft.DevicesAndPrinters`.

3. `control.exe /name Microsoft.PowerOptions` vs `powercfg.cpl` vs `control.exe netconnections` vs `ncpa.cpl`: czy wynik taki sam i które dają okno (tytuł).
   → rozstrzygnięte: `powercfg.cpl`, `control.exe powercfg.cpl` i `control.exe /name Microsoft.PowerOptions` dają to samo okno; tak samo `ncpa.cpl` i `control.exe netconnections`. Różnice tylko w procesie żyjącym w K1.

4. `wscui.cpl`, `rstrui.exe`, `firewall.cpl`, `main.cpl`, `appwiz.cpl`: klasyczne okno, przekierowanie do `ms-settings:` czy nic (kody wyjścia, brak procesu).
   Przekierowanie → w docs „nie portowane” albo opis „otwiera Ustawienia” (decyzja 2).
   → rozstrzygnięte: wszystkie pięć daje klasyczne okno w K2, żadne nie przekierowuje do Ustawień.

5. `(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon').AutoRestartShell` na świeżym 26H2 Home i Pro: czy wartość istnieje
   i jaka (jeśli brak – czy po `Stop-Process` Explorer i tak wraca).
   → rozstrzygnięte: wartość istnieje (DWORD 1) na Home i Pro. → nie rozstrzygnięte: zachowanie przy braku wartości (przypadek nie wystąpił).

6. Po zakończeniu procesu Explorera: czas do nowego PID (`Get-Process explorer` z SessionId) i do pojawienia się paska zadań; czy działa przy
   `Stop-Process -Force` i `taskkill /f`.
   → rozstrzygnięte dla `Stop-Process -Force`: powłoka wraca w ≤ 1,42 s (pasek ≤ 1,39 s). → nie rozstrzygnięte: `taskkill /f` (wada sondy trafiła w proces `/factory`).

7. Token nowego Explorera (stan podniesienia procesu, np. `TokenElevation`/`TokenElevationType` z poziomu sondy) w trzech przypadkach: Explorer odrodzony
   przez winlogon, Explorer uruchomiony przez `Start-Process explorer.exe` z procesu admina (jak v3.2), Explorer po logowaniu.
   → rozstrzygnięte: powłoka odrodzona przez winlogon i po logowaniu ma token `Limited`; `Start-Process explorer.exe` z K1 tworzy osobny, podniesiony Explorer Administratora w sesji 0, z K2 przekazuje otwarcie istniejącej powłoce.

8. `Get-AppxPackage -AllUsers | Where-Object Name -like '*Store*'` na świeżym Home i Pro: dokładna `Name`, `InstallLocation`, czy `AppxManifest.xml`
   istnieje, `Status`. Potwierdzić nazwę `Microsoft.WindowsStore`.
   → rozstrzygnięte: `Microsoft.WindowsStore`, `Status` = `Ok`, manifest istnieje; filtr `*Store*` łapie też `Microsoft.StorePurchaseApp`.

9. Re-register `Add-AppxPackage -Register -DisableDevelopmentMode` z manifestu w kontekście PS Direct (wbudowany `Administrator`) i w kontekście konta
   interaktywnego `dso`: czy kończy się bez błędu, dla którego konta działa. Odczyt `Get-AppxPackage Microsoft.WindowsStore` przed/po
   (PackageUserInformation, Status).
   → rozstrzygnięte: rejestracja kończy się bez błędu, ale działa dla konta procesu (z K1 dopisuje Store Administratorowi, wpis `dso` bez zmian).

10. Stan „Store niezainstalowany”: czy `Get-AppxPackage Microsoft.WindowsStore` zwraca pusto dla konta bez pakietu. Symulacja przez `Remove-AppxPackage`
    (tylko w VM, po checkpoincie) – decyzja, czy testować; inaczej ścieżka „brak pakietu” tylko na mockach.
   → rozstrzygnięte częściowo: brak pakietu dla konta (Administrator) to pusty wynik bez wyjątku. → nie rozstrzygnięte: brak pakietu na dysku (LTSC) i `Remove-AppxPackage` – tylko mocki.

11. `wsreset.exe -i`: czy przełącznik istnieje na 26H2 (kod wyjścia, komunikat). Tylko informacyjnie – w 4.0 bez źródła MS nie używamy.
   → rozstrzygnięte informacyjnie: `WSReset.exe` istnieje, kod wyjścia 0 mimo „Error: 0x80070520” (K1) / „Error: 0x80072ee7” (K2); w 4.0 nieużywane. → nie rozstrzygnięte: znaczenie kodów HRESULT.

12. WinUtil: czy `powershell.exe` (5.1) uruchomiony z `-NoExit -Command 'irm https://christitus.com/win | iex'` tworzy osobne okno z poprawnie cytowanym `|`
    (PS Direct nie cytuje `-ArgumentList` – zasady repo). Odczyt `Win32_Process.CommandLine` bez sieci; oczekiwany błąd `irm` w oknie to `Skipped`, nie zaliczenie.
   → rozstrzygnięte: `|` przechodzi dosłownie do `CommandLine`, okno „Administrator: Windows PowerShell” w K2, proces żyje po 10 s; 0 profili sieci. Skutek `Skipped`.

## Wartości do ręcznego sprawdzenia (przed wpisaniem do katalogu)

- Polecenie każdego panelu (plik, argumenty, CLSID) – z tabeli w sekcji 1 i ze stron MS w 2.1/2.2, porównane znak po znaku; wybór wariantu z VM (pytania 2–4).
- Nazwa i typ `AutoRestartShell` – ze źródła MS-archiwum i z odczytu VM (pytanie 5).
- Nazwa pakietu Store – z odczytu VM (pytanie 8), nie z posta społeczności.
- Linia poleceń WinUtil – dosłownie z README (gałąź stabilna), bez presetu i bez `windev`.

## Decyzje (maks. 6)

1. **Które panele legacy brać?** Rekomendacja: wszystkie 14 z `config/feature.json` (klasy A i B) po wyniku VM: okno klasyczne = zostaje; przekierowanie
   do Ustawień albo brak okna = „nie portowane”, nie z góry. Alternatywa: tylko mocna szóstka (Network, Power, System/Performance, Sound, Region, Time/Date).
2. **Fallback `ms-settings:`, gdy panel przekierowuje do Ustawień?** Rekomendacja: nie. Pozycja nazywa się „panel” i ma uczciwy opis; osobna pozycja
   „Ustawienia: …” (źródło: MS „Launch the Windows Settings app”) tylko jeśli VM pokaże, że klasycznego panelu już nie ma.
3. **`tools.restart-explorer` a `AutoRestartShell`.** Rekomendacja: tylko odczyt; przy 0 odmowa z komunikatem, bez zapisu rejestru (pozycja `safe`).
   Pomiar tokena na VM (pytanie 7) jest warunkiem wpisu do katalogu; jeśli się nie potwierdzi, pozycja idzie z ostrzeżeniem „działa jak restart Explorera,
   bez gwarancji obniżenia tokena”.
4. **`tools.store-repair` a LTSC.** Rekomendacja: brak pakietu = jawny komunikat „pakiet Store nie jest zainstalowany; akcja go nie instaluje”
   (kod 1, `Failed`, nie sukces), cytat MS o ograniczonym wsparciu Store na LTSC w docs. Bez obietnicy „przywraca Store na LTSC” i bez nieoficjalnych skryptów.
5. **`tools.winutil`: portować?** Rekomendacja: tak, jako cienki launcher (osobne okno, komenda dosłownie z README, ostrzeżenie o cudzym kodzie z internetu
   jako admin), `caution`, bez presetów i bez gałęzi `windev`; test skutku na VM = `Skipped` (brak sieci). Alternatywa: nie portować (zero kodu z internetu
   w narzędziu admina) – to decyzja o ryzyku, nie techniczna.
6. **Nazewnictwo i aliasy.** Rekomendacja: `panels.visual-effects`, `panels.power-options`, `panels.network-connections` z aliasami v3.2; panele legacy
   bez aliasów (test A5 liczy tylko aliasy v3.2). „Printer Panel” jako „Urządzenia i drukarki” (MS: CLSID `{A8A91A66-…}` = Devices and Printers).

---

## Wyniki VM (eksploracja 2026-10-19, Home i Pro)

Źródła danych (tylko odczyt, `ConvertFrom-Json`): zapis pomiaru w archiwum autora (start 08:41:49, koniec 08:55:19)
i zapis dla Pro (08:55:20–09:08:50); zapis sondy uzupełniającej w archiwum autora.
Skrypt: `tests\vm\explore\Invoke-C5Exploration.ps1`. Notacja: `probes.<sonda>[<Name części>].Data…`; „H/P” = Home/Pro.

Przebieg: `Invoke-C5Exploration.ps1 -Probe panels,winutil,store,explorer`, obie VM z checkpointu `czysta-26H2`, kod 0.
System: `meta.CurrentBuild` = 26300, `meta.UBR` = 9457, `meta.DisplayVersion` = 26H2, `meta.EditionID` = `Core` (Home) / `Professional` (Pro),
`meta.Culture` = pl-PL (uwaga: `meta.ProductName` w rejestrze to „Windows 10 Home/Pro” – znana pozostałość nazwy, nie błąd odczytu).
K1: `token.UserName` = `DSO-26H2-*\Administrator`, `token.SessionId` = 0, `token.Token.ElevationTypeName` = `Default`, `Elevated` = true.
K2: każde zadanie `Available` = true, `ChildSessionId` = 1, `ChildToken.Token.ElevationTypeName` = `Full` (dso podniesiony, Interactive/Highest).
Powłoka dso przy starcie: `userShell` = `explorer.exe PID 5632, sesja 1` (H) / `PID 5532, sesja 1` (P).

**Ważne dla interpretacji K1:** wszystko, co K1 uruchamia, powstaje w **sesji 0 jako Administrator** (np. `probes.panels[panele-k1].Data{…}.At3s.NewProcesses[].SessionId` = 0,
`OwnerUser` = `Administrator`), więc K1 nie daje okna widocznego dla dso – mówi tylko, czy proces wystartował i żyje. Okno ocenia wyłącznie K2.

### Rekordy błędów sond i „K2 niedostępne”
- Żadna część nie ma `Error`, `Skipped`, `ChildError`, `UnregisterError`; wszystkie zadania K2 mają `Available` = true, `Reason` = null (H i P). Brak rekordów BLAD-SONDY.
- Błędy na poziomie rekordu (dane, nie awaria sondy), identyczne na H i P:
  - `probes.panels[panele-k1].Data{panels.devices-printers, winutil}.Launch.Error` = „This command cannot be run due to the error: Wykonanie serwera nie powiodło się.”,
    `StartCallMs` = 108366 (H) / 108412 (P) – `Start-Process 'shell:::{…}'` blokował ok. 108 s.
  - `probes.panels[panele-k2].Data{panels.devices-printers, winutil}.Launch.Error` = „This command cannot be run completely because the system cannot find all the
    information required.”, `StartCallMs` = 49 (H i P).
- `OwnerError` tylko dla procesów-szumów (P: „GetOwner ReturnValue=2” ×1, „proces już nie istnieje” ×2) – bez wpływu na wnioski.
- **Wada sondy (nie błąd w JSON):** `probes.explorer[explorer-taskkill-k1]` zatrzymał nie powłokę, tylko proces `explorer.exe /factory,{5BD95610-9434-43C2-886C-57852CC8A120} -Embedding`
  (`Old.CommandLine`, `Old.ParentName` = `svchost.exe`; PID 6176 H / 7504 P). `Get-C5ShellProcess` wybiera najstarszy `explorer.exe` dso, a po wcześniejszym
  `Stop-Process` nowa powłoka była młodsza od procesu `/factory`. Pomiar `taskkill /f` na powłoce **nie istnieje**.

### Odpowiedzi na „Otwarte pytania” 1–12

1. **`SystemPropertiesPerformance.exe` vs `control.exe sysdm.cpl,,3`.**
   - Plik istnieje: `…Data{panels.visual-effects, ms-exe}.FileExists` = true (H, P). K2: `At3s/At10s.NewWindows` = `'Opcje wydajności'`, klasa `#32770`,
     proces `SystemPropertiesPerformance.exe` (sesja 1, dso), żyje po 10 s (`LaunchedHasExited` = false). K1: proces żyje po 10 s (sesja 0).
   - Przełączników efektów automat nie czytał (bez UI Automation). Klik autora (zapis pomiaru w archiwum autora, B15): w oknie widać „Efekty wizualne”.
   - `control.exe sysdm.cpl,,3`: `control.exe` kończy się kodem 1 (`At3s.LaunchedExitCode` = 1), a nowym procesem jest
     `"C:\Windows\System32\SystemPropertiesAdvanced.exe"` (K1: sesja 0; K2: sesja 1, okno `'Właściwości systemu'`, `#32770`). Karty z tytułu nie da się odczytać;
     **wniosek** z nazwy procesu: MS („Executing Control Panel Items”) nazywa `SystemPropertiesAdvanced.exe` „Advanced system properties”, czyli karta Zaawansowane.
     To nie jest okno „Opcje wydajności” (efekty wizualne są o jedno kliknięcie dalej). m-10: rozstrzygnięte co do procesu, karta = wniosek.
2. **Każdy panel – proces i okno (K1/K2, H/P).** Tabela niżej. Home i Pro dały ten sam wynik w każdym wariancie (te same tytuły, klasy, procesy-hosty).
   Okna klasy `CabinetWClass` należą do `explorer.exe` dso (proces `/factory,{5BD95610-…} -Embedding` z rodzicem `svchost.exe` albo główna powłoka), a nie do
   uruchomionego `control.exe` – ten kończy się kodem 1 w ciągu 3 s. Okna `#32770` należą do `rundll32.exe` (`Shell32.dll,Control_RunDLL …`),
   `SystemProperties*.exe` albo `rstrui.exe`; `compmgmt.msc` – `mmc.exe` (`MMCMainFrame`).
3. **Warianty Power i Network.** `powercfg.cpl`, `control.exe powercfg.cpl`, `control.exe /name Microsoft.PowerOptions` – w K2 to samo okno `'Opcje zasilania'`
   (`CabinetWClass`, `explorer.exe`). `ncpa.cpl` i `control.exe netconnections` – to samo okno `'Połączenia sieciowe'` (`CabinetWClass`). Różnica tylko w K1:
   `.cpl` przez ShellExecute → żyje `rundll32.exe` (np. `"C:\Windows\system32\rundll32.exe" Shell32.dll,Control_RunDLL "C:\Windows\System32\ncpa.cpl",`),
   `control.exe /name …` i `control.exe netconnections` → żyje sam `control.exe` (sesja 0).
4. **`wscui.cpl`, `rstrui.exe`, `firewall.cpl`, `main.cpl`, `appwiz.cpl`.** Wszystkie dały w K2 klasyczne okno, żadne nie przekierowało do Ustawień:
   `'Zabezpieczenia i konserwacja'` (`CabinetWClass`), `'Przywracanie systemu'` (`#32770`, `rstrui.exe`), `'Zapora Windows Defender'` (`CabinetWClass`),
   `'Właściwości: Mysz'` (`#32770`, `rundll32.exe`), `'Programy i funkcje'` (`CabinetWClass`). Jedyne przekierowanie do Ustawień w całym przebiegu:
   `control.exe /name Microsoft.DevicesAndPrinters` – w K1 `At3s.SettingsHosts` = `SystemSettings.exe` (`-ServerName:microsoft.windows.immersivecontrolpanel`)
   i `ApplicationFrameHost.exe`, oba `IsNew` = true, **w sesji 1 jako dso** (H: PID 9440/1264, P: 568/9180). W K2 ten wariant nie dał nowego procesu ani okna
   (okno „Ustawienia” z K1 już było otwarte – `ShellWindows` `'Ustawienia'` `ApplicationFrameWindow`).
   `rstrui.exe` przy stanie ochrony: `probes.panels[ochrona-systemu].Data.RestorePointCount` = 0, `SystemRestoreKey.Values`: `RPSessionInterval` = 0,
   `SRInitDone` = 1 (H i P) – kreator otwiera się mimo braku punktów (zgodne z klikiem autora B18). Uboczny skutek w K1: start usługi `wbengine.exe`
   (`…{panels.system-restore, winutil}.At3s.NewProcesses`, H i P).
5. **`AutoRestartShell`.** `probes.explorer[autorestartshell].Data`: `KeyExists` = true, `Exists` = true, `Kind` = `DWord`, `Value` = 1 (H i P).
   Przypadek „brak wartości” nie wystąpił → zachowanie przy braku **nie rozstrzygnięte**.
6. **Czas powrotu powłoki.** `Stop-Process -Force` (powłoka dso, `Old.CommandLine` = `C:\Windows\Explorer.EXE`):
   - K1: `ElapsedMs` = 1305 (H) / 1416 (P), `Polls` = 2; K2: `ElapsedMs` = 136 (H) / 1310 (P); pasek zadań (`Shell_TrayWnd` nowego PID) w K2:
     `TaskbarElapsedMs` = 217 (H) / 1386 (P). Rozdzielczość pętli 500 ms.
   - `taskkill /f` na powłoce: **nie rozstrzygnięte** (sonda trafiła w proces `/factory`, patrz wyżej; po jego zabiciu `Found` = false, `Polls` = 53/56 – nowa
     powłoka nie powstaje, czego należało oczekiwać dla procesu, który nie jest powłoką).
   - Klik autora (zapis pomiaru w archiwum autora, C): „Zakończ zadanie” w Menedżerze zadań → pulpit NIE wraca. To inny mechanizm niż `Stop-Process -Force`; przyczyny dane nie wyjaśniają.
7. **Token Explorera.**
   - Po logowaniu: `probes.explorer[explorer-po-logowaniu].Data[0].Token.ElevationTypeName` = `Limited`, `Elevated` = false, właściciel `dso`, sesja 1.
   - Po powrocie (winlogon): `…[explorer-stop-process-k1].Data.New` i `…[explorer-stop-process-k2].Data.New`: nowy PID (H: 7092, 2228; P: 9096, 6456),
     `SessionId` = 1, `OwnerUser` = `dso`, `OwnerSid` = SID dso (`…-1000`), `ParentName` = `userinit.exe`, `CommandLine` = `C:\Windows\Explorer.EXE`,
     `Token.ElevationTypeName` = `Limited`, `Elevated` = false. Brak różnicy H/P.
   - v3.2 (`Start-Process C:\Windows\explorer.exe`): K1 – `NewExplorers[0]`: osobny proces, `SessionId` = 0, `OwnerUser` = `Administrator`, `ParentName` =
     `powershell.exe`, `Token.Elevated` = true (`Default`), `LaunchedHasExited` = false po 5 s (żyje; zamknięty przez sondę). K2 – `LaunchedHasExited` = true,
     `LaunchedExitCode` = 1, `NewExplorers` = [] (brak nowego procesu), `NewWindows` = `'Widok główny — Eksplorator plików'` (`CabinetWClass`) w PID istniejącej
     powłoki (2228 H / 6456 P, token `Limited`) → przekazanie do istniejącej powłoki. `ShellAliveAfter` = true (H, P).
8. **Pakiet Store.** `probes.store[store-pakiety-wszyscy-k1].Data.Items`: `Name` = `Microsoft.WindowsStore`, `PackageFullName` =
   `Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe`, `Status` = `Ok`, `InstallLocation` =
   `C:\Program Files\WindowsApps\Microsoft.WindowsStore_22506.1400.2.0_x64__8wekyb3d8bbwe`, `ManifestExists` = true (K1 i K2), `Users` = `…-1000 [dso]: Installed`.
   Filtr `*Store*` łapie też `Microsoft.StorePurchaseApp` (22509.1400.2.0) → w katalogu dokładna nazwa, nie wzorzec. H = P.
9. **Re-register z K1 i K2.**
   - K1 (Administrator): `store-rejestracja-k1.Data.Before.Count` = 0 (bieżące konto nie ma Store), `Register.Ok` = true, `JobState` = `Completed`,
     `DurationMs` = 1845 (H) / 2123 (P); `After.Count` = 1; `AfterAllUsers…Users` = `[dso]: Installed; […-500 [Administrator]: Installed]` →
     **rejestracja z K1 dodała Store kontu Administrator**, wpis dso bez zmian.
   - K2 (dso): `Before.Count` = 2, `Register.Ok` = true, `DurationMs` = 1689 (H) / 1644 (P), `After` bez zmian (`Status` = `Ok`), `AfterAllUsers` – dso `Installed`.
     Rejestracja w koncie dso kończy się bez błędu; zmiany nie widać, bo pakiet był już zarejestrowany.
   - `PackageUserInformation` (`Users`) jest puste w odczycie bez `-AllUsers` (`store-pakiety-k2`, `Before/After`) – informacja o kontach tylko z `-AllUsers`.
10. **„Store niezainstalowany” dla konta.** Naturalna ścieżka: Administrator w K1 – `store-pakiety-biezace-k1.Data`: `Ok` = true, `Error` = null, `Count` = 0
    (pusto, bez wyjątku). Brak pakietu na dysku (LTSC) i `Remove-AppxPackage` – **nie testowane** (tylko mocki).
11. **`wsreset.exe -i`.** Plik `C:\Windows\System32\WSReset.exe` istnieje. K1: `Native.ExitCode` = 0, `Stdout` = „Error: 0x80070520”; K2: `ExitCode` = 0,
    `Stdout` = „Error: 0x80072ee7” (H i P). Kod wyjścia 0 mimo błędu → kod nie świadczy o skutku. Znaczenie kodów HRESULT – bez źródła w tym przebiegu.
    Zgodnie z D4 w 4.0 nieużywane.
12. **WinUtil.** Sonda **uruchomiona** (nie pominięta): `probes.winutil[siec].Data.NoNetwork` = true, `Reason` = „Get-NetConnectionProfile: 0 profili” (H i P).
    `Win32_Process.CommandLine` (`…Probe.At3s.NewProcesses[powershell.exe].CommandLine`):
    - wariant `cudzyslow`: `"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" -NoExit -Command "irm https://christitus.com/win | iex"`
    - wariant `bez-cudzyslowu`: `"…\powershell.exe" -NoExit -Command irm https://christitus.com/win | iex`
    Znak `|` przechodzi dosłownie w obu. Proces żyje po 10 s (`At10s.LaunchedHasExited` = false) w K1 (sesja 0, Administrator, bez widocznego okna)
    i w K2 (sesja 1, `NewWindows` = `'Administrator: Windows PowerShell'`, `ConsoleWindowClass`, host `conhost.exe`, nie Windows Terminal).
    Treści okna (błąd `irm` bez sieci) sonda nie czyta → skutek `Skipped`.

### Tabela paneli (Home = Pro we wszystkich wierszach)

K1 = co żyje po 10 s w sesji 0 (`At10s.NewProcesses`, nazwa hosta); K2 = host okna w sesji 1; okno = `At3s/At10s.NewWindows` w K2 (tytuł, klasa).

| Pozycja | Wariant (`file` + argumenty) | K1 (10 s) | K2 (host) | Okno K2 | Wniosek |
|---|---|---|---|---|---|
| visual-effects | `SystemPropertiesPerformance.exe` (MS) | `SystemPropertiesPerformance.exe` żyje | ten sam proces, żyje | `Opcje wydajności` / `#32770` | **wchodzi** |
| visual-effects | `control.exe sysdm.cpl,,3` (v3.2) | control kod 1; żyje `SystemPropertiesAdvanced.exe` | `SystemPropertiesAdvanced.exe` | `Właściwości systemu` / `#32770` | odrzucony dla tej pozycji (inne okno – karta Zaawansowane) |
| power-options | `powercfg.cpl` (WinUtil) | `rundll32` żyje | `explorer.exe /factory` | `Opcje zasilania` / `CabinetWClass` | klasyczne |
| power-options | `control.exe powercfg.cpl` (v3.2) | `rundll32` żyje | `explorer.exe` | `Opcje zasilania` / `CabinetWClass` | klasyczne |
| power-options | `control.exe /name Microsoft.PowerOptions` (MS) | `control.exe` żyje | `explorer.exe` | `Opcje zasilania` / `CabinetWClass` | **wchodzi** (remis → nazwa kanoniczna) |
| network-connections | `ncpa.cpl` (WinUtil) | `rundll32` żyje | `explorer.exe` | `Połączenia sieciowe` / `CabinetWClass` | klasyczne |
| network-connections | `control.exe netconnections` (MS) | `control.exe` żyje | `explorer.exe` | `Połączenia sieciowe` / `CabinetWClass` | **wchodzi** (remis → MS) |
| computer-management | `compmgmt.msc` → `"…\mmc.exe" "C:\Windows\System32\compmgmt.msc"` | `mmc.exe` żyje | `mmc.exe` (dso, podniesiony) | `Zarządzanie komputerem` / `MMCMainFrame` | **wchodzi** |
| control-panel | `control.exe` (WinUtil `control`) | `control.exe` żyje | `explorer.exe /factory` | `Panel sterowania` / `CabinetWClass` | **wchodzi** |
| mouse | `main.cpl` (WinUtil) | `rundll32` żyje | `rundll32.exe` | `Właściwości: Mysz` / `#32770` | klasyczne |
| mouse | `control.exe /name Microsoft.Mouse` (MS) | control kod 1; `rundll32` żyje | `rundll32.exe` | `Właściwości: Mysz` / `#32770` | **wchodzi** |
| devices-printers | `shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}` (WinUtil, ShellExecute) | błąd po ~108 s, brak procesu | błąd po 49 ms; okno w głównej powłoce (wniosek, niżej) | `Urządzenia i drukarki — Eksplorator plików` / `CabinetWClass` (pośrednio) | warunkowo – mechanizm do zmiany |
| devices-printers | `control.exe /name Microsoft.DevicesAndPrinters` (MS) | control kod 1; nowe `SystemSettings.exe` + `ApplicationFrameHost.exe` (sesja 1) | brak nowego procesu/okna | brak (Ustawienia) | odrzucony (Ustawienia, D2) |
| programs-features | `appwiz.cpl` (WinUtil) | `rundll32` żyje | `explorer.exe /factory` | `Programy i funkcje` / `CabinetWClass` | klasyczne |
| programs-features | `control.exe /name Microsoft.ProgramsAndFeatures` (MS) | `control.exe` żyje | `explorer.exe` | `Programy i funkcje` / `CabinetWClass` | **wchodzi** |
| region | `intl.cpl` (WinUtil) | `rundll32` żyje | `rundll32.exe` | `Region` / `#32770` | klasyczne |
| region | `control.exe /name Microsoft.RegionAndLanguage` (MS) | control kod 1; `rundll32` żyje | `rundll32.exe` | `Region` / `#32770` | **wchodzi** |
| security-maintenance | `wscui.cpl` (WinUtil, ShellExecute) | `rundll32` żyje | `explorer.exe /factory` | `Zabezpieczenia i konserwacja` / `CabinetWClass` | klasyczne; forma `control.exe` niezmierzona |
| sound | `mmsys.cpl` (WinUtil) | `rundll32` żyje | `rundll32.exe` | `Dźwięk` / `#32770` | klasyczne |
| sound | `control.exe /name Microsoft.Sound` (MS) | control kod 1; `rundll32` żyje | `rundll32.exe` | `Dźwięk` / `#32770` | **wchodzi** (klik autora: brak odpowiedzi – rozbieżność) |
| system-properties | `sysdm.cpl` (WinUtil) | brak nowego procesu | brak nowego procesu/okna | brak | nie rozstrzygnięte (patrz niżej) |
| system-properties | `control.exe /name Microsoft.System` (MS) | `control.exe` żyje | nowy `explorer.exe /factory`, bez okna | brak | odrzucony (brak okna) |
| date-time | `timedate.cpl` (WinUtil) | `rundll32` żyje | `rundll32.exe` | `Data i godzina` / `#32770` | klasyczne |
| date-time | `control.exe /name Microsoft.DateAndTime` (MS) | control kod 1; `rundll32` żyje | `rundll32.exe` | `Data i godzina` / `#32770` | **wchodzi** |
| firewall | `firewall.cpl` (WinUtil) | `rundll32` żyje | `explorer.exe /factory` | `Zapora Windows Defender` / `CabinetWClass` | klasyczne |
| firewall | `control.exe /name Microsoft.WindowsFirewall` (MS) | `control.exe` żyje | `explorer.exe` | `Zapora Windows Defender` / `CabinetWClass` | **wchodzi** |
| system-restore | `rstrui.exe` (WinUtil) | `rstrui.exe` żyje (+ start `wbengine.exe`) | `rstrui.exe` | `Przywracanie systemu` / `#32770` | **wchodzi** |

Szczegóły do tabeli:
- **`shell:::{A8A91A66-…}` (K2):** `Launch.Error` po 49 ms, `At3s/At10s` = null (sonda nie obserwowała), `Cleanup.LeftProcesses` = [] (żaden nowy proces).
  Okno `'Urządzenia i drukarki — Eksplorator plików'` (`CabinetWClass`, PID głównej powłoki 5632 H / 5532 P) jest w `ShellWindows` następnego wariantu
  (`{devices-printers, ms-name}.At3s/At10s.ShellWindows`), a NIE w jego `NewWindows`, i nie było go w `ShellWindows` wariantu poprzedniego (`{mouse, ms-name}.At10s`).
  **Wniosek:** okno powstało między tymi odczytami, czyli po `Start-Process 'shell:::…'` – mimo błędu zgłoszonego wywołującemu. Bezpośredniego odczytu brak.
  Klik autora (B8): klasyczne okno. W K1 `shell:::` nie działa (błąd serwera po ~108 s).
- **`sysdm.cpl` – dlaczego nie rozstrzygnięte:** wariant `{visual-effects, v32-sysdm-3}` zostawił otwarte `SystemPropertiesAdvanced.exe` w obu kontekstach
  (`Cleanup.Stopped` = [], `Cleanup.LeftProcesses` zawiera `SystemPropertiesAdvanced.exe` – nie było na liście `Get-C5PanelAllowNames`). **Hipoteza:** okno
  „Właściwości systemu” jest jednoinstancyjne i `sysdm.cpl` aktywował istniejące okno (brak nowego procesu i uchwytu). Dane tego nie potwierdzają ani nie wykluczają.
  Klik autora (B13, czysty stan): `sysdm.cpl` → klasyczne okno.
- **Okna `CabinetWClass`** z wywołania podniesionego (K2) trafiają do `explorer.exe` dso z tokenem `Limited` (proces `/factory` ma token `Limited`:
  `explorer-po-logowaniu.Data[1].Token`), czyli działają bez podniesienia; okna `#32770` w `rundll32`/`SystemProperties*`/`rstrui` i `mmc` są dziećmi procesu
  podniesionego. Uruchamiany `control.exe` kończy się kodem 1 także wtedy, gdy okno się otworzyło → kod wyjścia `control.exe` nie jest sygnałem błędu.

### Różnice Home vs Pro
Brak różnic merytorycznych: te same procesy, tytuły, klasy, tokeny, `AutoRestartShell`, nazwa i wersja pakietu Store, wynik `wsreset`, wynik WinUtil.
Różnice tylko w czasach (`Stop-Process` K2: 136 ms H / 1310 ms P; pasek: 217 / 1386 ms; rejestracja Store 1,6–2,1 s) i w PID-ach.

### Bramki

- **G1 – spełniona dla `Stop-Process -Force`.** Powłoka wraca sama (winlogon → `userinit.exe` jako rodzic) w ≤ 1,42 s, nowy PID, sesja 1, właściciel `dso`,
  token `Limited` (nie podniesiony), pasek zadań ≤ 1,39 s; Home = Pro. Wariant v3.2 (`Start-Process explorer.exe`) z K1 tworzy osobny, podniesiony
  `explorer.exe` Administratora w sesji 0 (żyje), z K2 przekazuje otwarcie istniejącej powłoce (proces kończy się kodem 1, brak nowego procesu, nowe okno
  w powłoce). `AutoRestartShell` = DWORD 1 (istnieje) na H i P. `taskkill /f` na powłoce: nie rozstrzygnięte (wada sondy). Ręczne „Zakończ zadanie”
  w Menedżerze zadań nie przywraca powłoki (klik autora).
- **G2 – Appx z K1 NIE rejestruje dla `dso`:** rejestruje dla konta wywołującego (dopisany `Administrator: Installed`), wpis dso bez zmian. W K2 (dso,
  podniesiony) rejestracja kończy się `Ok`, `Status` po = `Ok`. Nazwa `Microsoft.WindowsStore`, manifest czytelny z procesu podniesionego. `wsreset -i`:
  kod 0, stdout „Error: 0x80070520” (K1) / „Error: 0x80072ee7” (K2).
- **G3 – bezpośrednio odczytane klasyczne okno w K2 (H i P) ma 14 z 15 pozycji**: 12 z wariantem zgodnym z przyjętym mechanizmem uruchamiania (w tabeli **wchodzi**),
  `security-maintenance` tylko przez ShellExecute `wscui.cpl` (forma `control.exe` niezmierzona), `system-properties` tylko przez `control.exe sysdm.cpl,,3`
  (`sysdm.cpl` nierozstrzygnięty – kontaminacja, `/name Microsoft.System` bez okna). 15. pozycja, `devices-printers`, ma okno tylko pośrednio i wyłącznie w K2.
  Jedyne przekierowanie do Ustawień: `control.exe /name Microsoft.DevicesAndPrinters` (D2: wariant odpada, bez fallbacku `ms-settings:`).
  K1 dla testu VM: każdy wybrany wariant poza `shell:::` zostawia żywy proces w sesji 0 po 10 s (tabela, kolumna K1).

---

## Sonda uzupełniająca (2026-10-19)

Skrypt `Invoke-C5Exploration.ps1` @ `01368c9`, `-Probe panels -Variant security-maintenance-control,system-properties-control,devices-printers-explorer`.
Dane: zapis pomiaru w archiwum autora (H: 09:35:02–09:36:30, P: 09:36:30–09:37:52; build 26300.9457).
Uruchomienie: CreateProcess (`Start-C5CreateProcess`: `ProcessStartInfo.UseShellExecute` = `$false`), tak jak launcher. Powłoka dso: PID 5576 (H) / 5236 (P), sesja 1.
Brak błędów: `Error`, `Launch.Error`, `ChildError`, `UnregisterError` puste; `panele-k2.Available` = true, `ChildToken` `Full`, `ChildSessionId` = 1 (H i P).
Żaden wariant nie uruchomił Ustawień: `At3s/At10s.SettingsHosts` puste w K1 i K2 (H i P). Home = Pro we wszystkich polach poza PID-ami.

| Wariant (`Variant` / CreateProcess) | K1 (10 s, sesja 0) | K2: okno (tytuł / klasa) | K2: właściciel okna | Wniosek |
|---|---|---|---|---|
| `security-maintenance-control`: `C:\Windows\System32\control.exe wscui.cpl` | `control.exe` kod 1; żyje `"C:\Windows\system32\rundll32.exe" Shell32.dll,Control_RunDLL wscui.cpl` | `Zabezpieczenia i konserwacja` / `CabinetWClass` | nowy `explorer.exe /factory,{5BD95610-9434-43C2-886C-57852CC8A120} -Embedding`, dso, sesja 1 (token tego procesu nie mierzony w tej sondzie) | **wchodzi** |
| `system-properties-control`: `C:\Windows\System32\control.exe sysdm.cpl` | `control.exe` kod 1; żyje `"C:\Windows\System32\SystemPropertiesComputerName.exe"` | `Właściwości systemu` / `#32770` | `SystemPropertiesComputerName.exe`, dso, sesja 1 (token nie mierzony) | **wchodzi** |
| `devices-printers-explorer`: `C:\Windows\explorer.exe shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}` | `explorer.exe` (uruchomiony) **żyje**: sesja 0, `Administrator`, rodzic `powershell.exe`, token `Default`, `Elevated` = true; brak okna dla dso | `Urządzenia i drukarki — Eksplorator plików` / `CabinetWClass` | nowy `explorer.exe /factory,{75dff2b7-6936-4c06-a8bb-676a7b00b24b} -Embedding`, rodzic `svchost.exe`, dso, sesja 1, token `Limited`, `IsUserShell` = false | wchodzi tylko z `Skip` w K1 (niżej) |

Szczegóły (ścieżki `probes.panels[panele-k1|panele-k2].Data{Name}`):
- `security-maintenance-control`: K2 `At3s/At10s.LaunchedHasExited` = true, `LaunchedExitCode` = 1; `NewWindows` = `'Zabezpieczenia i konserwacja'` w PID 9484 (H) / 8176 (P).
  K1 `At10s.NewProcesses` – `rundll32.exe` (8472 H / 8820 P), sesja 0. To samo okno co ShellExecute `wscui.cpl` z głównej sondy.
- `system-properties-control`: `PreClean.Stopped` = [], `PreExisting` = [] – w tym przebiegu nie było wcześniejszego `SystemPropertiesAdvanced.exe` (czysty stan).
  K2: `control.exe` kod 1, `NewWindows` = `'Właściwości systemu'` w `SystemPropertiesComputerName.exe` (9716 H / 10060 P). K1: `SystemPropertiesComputerName.exe` żyje
  (5624 H / 9740 P). **Wniosek** z nazwy procesu: karta „Nazwa komputera” (domyślna karta Właściwości systemu); tytuł karty nie rozstrzyga, a cytatu MS o
  `SystemPropertiesComputerName.exe` w research nie ma. Wynik potwierdza hipotezę z głównej sondy: tam `sysdm.cpl` nie dał okna, bo `SystemPropertiesAdvanced.exe`
  z wariantu `v32-sysdm-3` był nadal otwarty (pośrednio – brak bezpośredniego odczytu jednoinstancyjności).
- `devices-printers-explorer`:
  - K2: uruchomiony `explorer.exe` (9476 H / 9700 P) kończy się przed 3 s (`At3s.LaunchedHasExited` = true, `LaunchedExitCode` = 1). `NewExplorers` = jeden proces
    `C:\Windows\explorer.exe /factory,{75dff2b7-6936-4c06-a8bb-676a7b00b24b} -Embedding` (9508 H / 9844 P), `OwnerUser` = `dso`, `SessionId` = 1, `ParentName` =
    `svchost.exe`, `Token.ElevationTypeName` = `Limited`, `Elevated` = false. `WindowOwners[0]`: ten proces, `IsUserShell` = false (`UserShellPid` = 5576 H / 5236 P),
    `IsNew` = true. Okno `CabinetWClass` `'Urządzenia i drukarki — Eksplorator plików'` (uchwyt 262594 H / 196738 P).
  - K1: `At3s/At10s.LaunchedHasExited` = false; `NewExplorers` = uruchomiony proces (8944 H / 9212 P), sesja 0, `Administrator`, `Default`, `Elevated` = true,
    CommandLine `"C:\Windows\explorer.exe" shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}`; zatrzymany przez sprzątanie (`Cleanup.Stopped`). Nowe okno dla dso: brak.

**Werdykty**
- `security-maintenance-control` – klasyczne okno w K2 (H i P), w K1 żyje `rundll32.exe`, bez Ustawień → wchodzi z `launch` = `control.exe` + `wscui.cpl`.
- `system-properties-control` – klasyczne okno w K2 (H i P), w K1 żyje `SystemPropertiesComputerName.exe`, bez Ustawień → wchodzi z `launch` = `control.exe` + `sysdm.cpl`.
- `devices-printers-explorer` – K2: klasyczne okno, uruchomiony Explorer kończy się kodem 1, okno należy do **nowego procesu Explorera dso** (`/factory,{75dff2b7-…}`,
  token `Limited`), a nie do PID powłoki. K1: powstaje nowy, podniesiony `explorer.exe` Administratora w sesji 0 i żyje.
- Pozytywnie w tym, co chroni (okno działa jako dso, sesja użytkownika, token `Limited`, uruchomiony proces kończy się); z korektą sformułowania:
  właścicielem okna jest proces `explorer.exe /factory` konta dso (ten sam wzorzec co okna `CabinetWClass` innych paneli), nie powłoka (`IsUserShell` = false).
  W kontekście bez powłoki użytkownika w tej samej sesji (K1) zachowanie jest negatywne: podniesiony Explorer żyje dalej.

## Wnioski (rozstrzygnięcia po VM)

Decyzje autora po analizie wyników powyżej (szczegóły w zapisie partii w archiwum autora).
- **`tools.restart-explorer` (bramka G1 spełniona):** wchodzi; zatrzymanie wyłącznie `Stop-Process -Force`, powłoka wybierana po `OwnerSid`, sesji i linii poleceń bez `/factory`; `AutoRestartShell` brak albo 0 → `Failed` bez zatrzymania; limit nowego PID 10 s.
- **`tools.store-repair` (bramka G2):** SID procesu ≠ `$UserSid` → `Failed` bez rejestracji (K1 rejestruje dla Administratora, nie dla `dso`); pakiet po dokładnej nazwie `Microsoft.WindowsStore`, odczyt zwrotny `Status` = `Ok`, brak pakietu → `Failed`, `wsreset -i` nieużywane.
- **Bramka G3:** wchodzi 12 paneli z tabeli „Panele wchodzące” (polecenia z wariantów, które dały klasyczne okno; przy remisie nazwa kanoniczna MS); trzy warunkowe poszły do sondy uzupełniającej.
- **`shell:::`:** ShellExecute `shell:::` jest fałszywie negatywny w K2 i zawiesza ~108 s w K1, więc odpada; sonda uzupełniająca oceniła CreateProcess `explorer.exe shell:::{GUID}` pozytywnie (okno w procesie `/factory` konta, token `Limited`).
- **`tools.winutil`:** `launch` = `powershell.exe` z `-NoExit -Command "irm https://christitus.com/win | iex"` (zmierzone), pod warunkiem zgodności ciągu z README.
- **15 paneli i strażnik:** wchodzi 15 paneli (12 + `security-maintenance` `control.exe wscui.cpl`, `system-properties` `control.exe sysdm.cpl`, `devices-printers` przez `%SystemRoot%\explorer.exe shell:::{GUID}`); launcher przed startem Explorera sprawdza, że w bieżącej sesji działa `explorer.exe` tego samego konta, inaczej `Failed` bez startu. Strażnik liczy tylko powłokę (explorer.exe bez `/factory` w linii poleceń), bo procesy `/factory` przeżywają śmierć powłoki. Przypadek VM dla `devices-printers`: przyjęto wariant `Failed` + `MessageContains` (odmowa strażnika w K1 przez PS Direct) zamiast `Skip`.

## Wartości liczbowe stałych tokena (uzupełnienie, data dostępu 2026-10-19)

Poniżej rozdzielono wartości podane wprost na stronie MS Learn od wartości wynikających z kolejności enumu i od tych, których strona nie podaje.

| Stała | Wartość | Skąd |
|---|---|---|
| `TokenUser` | 1 | **wprost** (MS Learn, `ne-winnt-token_information_class`: „Value: 1”) |
| `TokenElevationType` | 18 | **z kolejności enumu** (lista składni od `TokenUser = 1`: Groups 2, Privileges 3, Owner 4, PrimaryGroup 5, DefaultDacl 6, Source 7, Type 8, ImpersonationLevel 9, Statistics 10, RestrictedSids 11, SessionId 12, GroupsAndPrivileges 13, SessionReference 14, SandBoxInert 15, AuditPolicy 16, Origin 17, ElevationType 18) |
| `TokenElevation` | 20 | **z kolejności enumu** (LinkedToken 19, Elevation 20); liczby w nagłówku SDK (`winnt.h`), nie na stronie MS Learn |
| `TokenElevationTypeDefault` | 1 | **wprost** (MS Learn, `ne-winnt-token_elevation_type`: „Value: 1”) |
| `TokenElevationTypeFull` / `TokenElevationTypeLimited` | 2 / 3 | **z kolejności enumu** (`Default = 1, Full, Limited`) |
| `TOKEN_QUERY` | 0x0008 | **liczba nie występuje na stronie MS Learn** (`access-rights-for-access-token-objects` podaje tylko „Required to query an access token.”); wartość wyłącznie z `winnt.h` |
| `PROCESS_QUERY_LIMITED_INFORMATION` | 0x1000 | **wprost** (MS Learn, `process-security-and-access-rights`: „PROCESS_QUERY_LIMITED_INFORMATION (0x1000)”) |

