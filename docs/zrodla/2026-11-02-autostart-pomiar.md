# Autostart: pomiar na VM 26H2 (2026-11-02)

Dokument źródłowy dla modułu „Autostart”. Zawiera wyłącznie to, co zaobserwowano na maszynie wirtualnej; czego nie sprawdzono, jest wprost oznaczone jako nieustalone.

## Środowisko

- VM Hyper-V `DSO-26H2-Pro`: Windows 11 Pro (rejestr: `ProductName` = „Windows 10 Pro”, `EditionID` = `Professional`), `DisplayVersion` 26H2, build 26300.9457, język polski.
- Porównanie: `DSO-26H2-Home` (`EditionID` = `Core`), ten sam build 26300.9457 – tylko inwentarz czystego stanu i manifesty AppX.
- Start każdej serii od checkpointu `czysta-26H2`, VM bez sieci. Konto `dso` (administrator lokalny) loguje się automatycznie po każdym rozruchu.
- Odczyt i zapis rejestru: PowerShell Direct jako wbudowany Administrator (`HKU\<SID dso>`, `HKU\<SID dso>_Classes`, `HKLM`).
- Działania „jak użytkownik”: skrypt w sesji interaktywnej `dso` (zadanie Harmonogramu, logowanie Interactive, najwyższe uprawnienia) steruje Menedżerem zadań (`Taskmgr.exe /0 /startup`) i Ustawieniami (`ms-settings:startupapps`) przez UI Automation: zaznaczenie wiersza i przycisk „Wyłącz”/„Włącz” w Menedżerze zadań, przełącznik `ToggleSwitch` w Ustawieniach.
- Restart: `Restart-Computer`, potwierdzony późniejszym `LastBootUpTime` i zniknięciem ulotnego klucza w `HKLM`. Odczyt po rozruchu po ok. 150 s (po zapisie pomiaru Windows).
- Uwaga praktyczna: przy wygaszonym ekranie VM Ustawienia nie rysują listy (UI Automation widzi tylko ramę okna). Przed pomiarem w VM wyłączono wygaszanie (`powercfg /change monitor-timeout-ac 0`) i budzono ekran klawiszem przez `Msvm_Keyboard`.

### Sondy testowe

Pięć małych programów `C:\dsotest\Dso*.exe` (C#, `csc.exe` z .NET Framework, bez sieci). Każdy przy starcie dopisuje linię `nazwa|czas UTC|konto|sesja` do `C:\dsotest\probe.log`, zużywa ok. 1,5 s CPU, zapisuje i czyta 16 MB w `%TEMP%`, potem czeka godzinę (proces widoczny). Każdy ma własne `AssemblyTitle` („`<nazwa> probe`”) i `AssemblyCompany` („DsoTest Publisher”).

| Wpis | Miejsce | Wartość StartupApproved |
|---|---|---|
| `DsoRunUser` | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\Run` | `HKU\<SID>\…\Explorer\StartupApproved\Run` |
| `DsoRunHklm` | `HKLM\Software\Microsoft\Windows\CurrentVersion\Run` | `HKLM\…\StartupApproved\Run` |
| `DsoRunWow` | `HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run` | `HKLM\…\StartupApproved\Run32` |
| `DsoLnkUser.lnk` | `C:\Users\dso\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup` | `HKU\<SID>\…\StartupApproved\StartupFolder` |
| `DsoLnkCommon.lnk` | `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp` | `HKLM\…\StartupApproved\StartupFolder` |
| `DsoMissing` | `HKU\<SID>\…\Run`, ścieżka do nieistniejącego pliku | `HKU\<SID>\…\StartupApproved\Run` |

Nazwa wartości w `StartupApproved` = nazwa wartości w `Run` albo nazwa pliku skrótu z rozszerzeniem (`DsoLnkUser.lnk`).

## 1. Inwentarz czystego 26H2

Pro i Home identycznie:

- `HKLM\…\Run`: `SecurityHealth` = `%windir%\system32\SecurityHealthSystray.exe` (REG_EXPAND_SZ).
- `HKLM\…\WOW6432Node\…\Run`: klucz istnieje, pusty. `RunOnce` (HKLM, WOW6432Node): puste.
- `HKU\<SID dso>\…\Run`: `OneDriveSetup` = `C:\Windows\System32\OneDriveSetup.exe /thfirstsetup`. Po pierwszym logowaniu instalator OneDrive sam podmienia ją na `OneDrive` = `"…\OneDrive\OneDrive.exe" /background`. `HKU\<SID>\…\RunOnce` nie istnieje.
- `HKLM\…\Explorer\StartupApproved\Run`: tylko `SecurityHealth` = `04 00 00 00 00 00 00 00 00 00 00 00`.
- `HKLM\…\StartupApproved\Run32` i `\StartupFolder`: **nie istnieją**.
- `HKU\<SID>\…\Explorer\StartupApproved`: **nie istnieje** (żaden z podkluczy).
- Foldery Autostart (użytkownika i wspólny): tylko ukryty `desktop.ini`.
- `C:\Windows\System32\wdi\LogFiles\StartupInfo`: **nie istnieje** na czystym checkpoincie.
- Klucz zadania startowego Terminala w `HKU\<SID>_Classes` tworzy sam Windows po zalogowaniu, bez Menedżera zadań i Ustawień (w zmierzonych rozruchach 15–92 s po starcie): `State` = 0, `ShowNotification` = 1, `UserEnabledStartupOnce` = 0 (Pro i Home, pkt 8). Odczyt ok. 20 s po rozruchu może go jeszcze nie zastać.

Wniosek dla modułu: brak klucza `StartupApproved` (albo podklucza, albo wartości) to stan normalny = pozycja włączona. Moduł musi umieć utworzyć `Run32`, `StartupFolder` i cały `StartupApproved` w HKU.

## 2. Format StartupApproved

Zapis przez Menedżer zadań i Ustawienia, odczytany z rejestru przed i po (12 bajtów, `REG_BINARY`):

| Akcja | Narzędzie | Przed | Po |
|---|---|---|---|
| Wyłącz `DsoRunUser` (HKU Run) | Menedżer zadań | `02` + 11×`00` | `03 00 00 00` + FILETIME 08:37:53.977 UTC |
| Wyłącz `DsoRunHklm` (HKLM Run) | Menedżer zadań | `02` + zera | `03 00 00 00` + FILETIME 08:37:56.411 |
| Wyłącz `DsoLnkCommon.lnk` (HKLM StartupFolder) | Menedżer zadań | `02` + zera | `03 00 00 00` + FILETIME 08:37:58.821 |
| Wyłącz `SecurityHealth` (HKLM Run) | Menedżer zadań | `06` + zera | `07 00 00 00` + FILETIME 08:38:01.250 |
| Wyłącz `DsoLnkUser.lnk` (HKU StartupFolder) | Ustawienia | `02` + zera | `03 00 00 00` + FILETIME 08:38:16.324 |
| Wyłącz `DsoRunWow` (HKLM Run32) | Ustawienia | `02` + zera | `03 00 00 00` + FILETIME 08:38:18.112 |
| Włącz `DsoLnkUser.lnk`, `DsoRunWow`, `DsoRunUser` | Menedżer zadań | `03` + FILETIME | `02` + 11×`00` |
| Włącz `DsoRunHklm`, `DsoLnkCommon.lnk` | Ustawienia | `03` + FILETIME | `02` + 11×`00` |
| Włącz `SecurityHealth` | Ustawienia | `07` + FILETIME | `06` + 11×`00` |

- Długość zawsze 12 bajtów. Bajty 1–3 zawsze `00`.
- Bajt 0: bit 0 = wyłączone. Wyłączenie ustawia bit 0 i zachowuje pozostałe bity (`02`→`03`, `06`→`07`); włączenie czyści bit 0 (`03`→`02`, `07`→`06`).
- Bajty 4–11: FILETIME (UTC, little-endian) chwili wyłączenia. Wartość mieści się w oknie wywołania przycisku/przełącznika zmierzonym zegarem VM (np. przycisk wywołany między 08:37:53.958 a 08:37:53.977, zapisano 08:37:53.977). Włączenie zeruje bajty 4–11.
- Ścieżki zapisu: pozycje z HKLM (Run, Run32, wspólny folder) – `HKLM\…\StartupApproved\{Run,Run32,StartupFolder}`; pozycje użytkownika – `HKU\<SID>\…\StartupApproved\{Run,StartupFolder}`. Oba narzędzia piszą tak samo.
- Znaczenie bitów 1–2 (`02` vs `04` vs `06`): nieustalone. `SecurityHealth` ma na czystym systemie `04`; samo otwarcie Menedżera zadań zmienia to na `06` (stan nadal „Włączona”).

### Zapis przy samym otwarciu GUI (bez klikania)

- Menedżer zadań przy wyświetleniu listy dopisuje `02` + zera dla każdej pozycji bez wartości (także dla `DsoMissing`), tworząc brakujące klucze `Run32`, `StartupFolder` i `StartupApproved` w HKU, oraz zmienia `SecurityHealth` z `04` na `06`.
- Ustawienia przy otwarciu strony zapisują `03` + FILETIME chwili otwarcia dla wpisu z nieistniejącym plikiem (patrz pkt 7).
- Explorer przy logowaniu nic nie zapisuje: nowy wpis `DsoRunUserB` dodany bez otwierania GUI wystartował po restarcie i nadal nie miał wartości w `StartupApproved`; `02` pojawiło się dopiero po otwarciu Menedżera zadań/Ustawień.
- Czy Ustawienia też dopisują `02` przy samym wyświetleniu: nieustalone (w obu seriach Menedżer zadań był otwarty wcześniej).

## 3. Realny skutek po restarcie

Każdy wiersz: restart, automatyczne logowanie `dso`, odczyt procesów i `probe.log` po ok. 150 s.

| Stan przed restartem | Co wystartowało | Co nie wystartowało |
|---|---|---|
| 5 pozycji wyłączonych przez GUI (`03`/`07` + FILETIME), nowy `DsoRunUserB` bez wartości | tylko `DsoRunUserB` | 5 sond, `SecurityHealthSystray` |
| Te same pozycje włączone przez GUI (`02`/`06` + zera), Terminal włączony w Ustawieniach | 5 sond, `SecurityHealthSystray`, `WindowsTerminal.exe` | – |
| Nasz zapis: `03 00 00 00` + FILETIME(teraz) dla 5 pozycji; Terminal wyłączony w Ustawieniach | `SecurityHealthSystray` | 5 sond, Terminal |
| Nasz zapis: `DsoRunUser`, `DsoRunHklm` = `02` + zera; `DsoLnkUser.lnk`, `DsoRunWow`, `DsoLnkCommon.lnk` = wartość usunięta; Terminal `State` = 2 zapisane bezpośrednio | 5 sond, Terminal | – |
| Nasz zapis: `DsoRunUser` = `03` + 11×`00` (bez FILETIME); Terminal `State` = 1 zapisane bezpośrednio | pozostałe 4 sondy | `DsoRunUser`, Terminal |

- Wyłączenie działa dla wszystkich pięciu miejsc (HKU Run, HKLM Run, HKLM Run32, folder użytkownika, folder wspólny), bez usuwania wpisu `Run` ani skrótu.
- Przywrócenie działa w obu wariantach: `02` + zera oraz usunięcie wartości.
- Do wyłączenia wystarcza bit 0; zerowy FILETIME nie przeszkadza.
- Menedżer zadań i Ustawienia pokazują nasz zapis zgodnie ze skutkiem („Wyłączona”/przełącznik Off dla `03`, „Włączona”/On dla `02` i dla braku wartości). Samo ich otwarcie nie nadpisało naszych wartości `03`; usunięte wartości zastąpiło `02`.
- Kolejność startu przez Explorer (zegar procesów, rozruch bez zakłóceń): HKLM Run (+8–11 s od rozruchu), HKU Run, Terminal (rodzic `sihost.exe`), folder wspólny i Run32 (rodzic `runonce.exe`), folder użytkownika (+16–19 s).
- Uboczna obserwacja: przy bardzo szybkim rozruchu VM wpisy dodane ok. 14 s po rozruchu (przed przetworzeniem Run przez Explorer) wystartowały jeszcze w tym samym logowaniu.

## 4. Pomiar Windows (StartupInfo)

- Ścieżka: `C:\Windows\System32\wdi\LogFiles\StartupInfo\<SID użytkownika>_StartupInfo<N>.xml`, kodowanie UTF-16. `N` rośnie o 1 z każdym rozruchem (1, 2, 3, 4 w czterech kolejnych rozruchach). Limit rotacji: nieustalony.
- Katalog nie istnieje na czystym checkpoincie; powstaje już w pierwszym rozruchu z zalogowaniem. Plik pojawia się ok. 95 s po starcie rozruchu (czas zapisu = `BootEndTime` zdarzenia 100), w każdym zmierzonym rozruchu.
- Uprawnienia katalogu: właściciel LocalService; Administratorzy – pełna kontrola z odmową wykonywania; SYSTEM – pełna; `NT SERVICE\DPS` i `NT SERVICE\WdiServiceHost` – zapis/odczyt; brak wpisu dla zwykłych użytkowników. Plik ma dodatkowo jawny wpis „pełna kontrola” dla SID użytkownika, którego dotyczy. Odczyt jako administrator działa (kopiowanie przez PowerShell Direct). Odczyt przez proces bez podniesionych uprawnień: nieustalony (nie testowano).
- Schemat (`StartupData IntervalStartMs IntervalEndMs`, okno ok. 90 s śladu rozruchu):

```xml
<StartupData IntervalStartMs="3460" IntervalEndMs="93460">
  <Process Name="C:\dsotest\DsoRunUser.exe" PID="8216" StartedInTraceSec="12.809">
    <StartTime>2026/11/02:08:43:45.0360860</StartTime>
    <CommandLine><![CDATA["C:\dsotest\DsoRunUser.exe" ]]></CommandLine>
    <DiskUsage Units="bytes">3002368</DiskUsage>
    <CpuUsage Units="us">1534474</CpuUsage>
    <ParentPID>…</ParentPID>
    <ParentStartTime>…</ParentStartTime>
    <ParentName>explorer.exe</ParentName>
  </Process>
  …
  <ReadAheadAnalysisTime>0</ReadAheadAnalysisTime>
  <RurLegacyResourceAttribution>0</RurLegacyResourceAttribution>
</StartupData>
```

- `Name` = pełna ścieżka pliku procesu (to klucz do powiązania z wpisem: ścieżka z `Run` albo cel skrótu). `ParentName`: `explorer.exe` dla Run i folderów Autostart, `runonce.exe` dla `WOW6432Node\Run`, `sihost.exe` dla zadania startowego AppX. W pliku są też procesy potomne (np. `OpenConsole.exe` i `powershell.exe` z rodzicem `WindowsTerminal.exe`, łańcuch `OneDriveSetup.exe`).
- Wszystkie pięć sond jest w pliku w rozruchach, w których były włączone; wyłączonych nie ma. Proces uruchomiony po `IntervalEndMs` nie trafia do pliku.
- Kolumna „Wpływ na uruchamianie” zgadza się z liczbami z pliku i z progami: wysoki = CPU > 1 s albo dysk > 3 MB, średni = CPU 0,3–1 s albo dysk 0,3–3 MB, niski = poniżej obu. Zaobserwowane: sondy 1,53 s CPU przy 19 KB–3 MB dysku → „Wysoki”/„Duży wpływ” (sam próg CPU); `SecurityHealthSystray` 19 ms CPU i 538 KB dysku → „Średni”; `OneDrive.exe` 0,43 s i 48 MB → „Wysoki”. Kategorii „Niski” nie zaobserwowano.
- Wyjątki w GUI: przed zapisem pliku w bieżącym rozruchu pozycje mają „Nie zmierzono”; pozycje wyłączone i wpis z brakującym plikiem – „Brak”; Terminal (zadanie AppX) – „Nie zmierzono”, mimo że `WindowsTerminal.exe` jest w pliku (2,5 s CPU). Czy Menedżer zadań sumuje procesy potomne: nieustalone.

## 5. Dziennik Diagnostics-Performance (informacyjnie)

- `Microsoft-Windows-Diagnostics-Performance/Operational`: włączony, tryb cykliczny, na czystym checkpoincie pusty.
- Po każdym rozruchu jedno zdarzenie 100 („System Windows został uruchomiony”), zapisane w tej samej chwili co plik StartupInfo.
- Pola `EventData`: `BootTsVersion`, `BootStartTime`, `BootEndTime`, `SystemBootInstance`, `UserBootInstance`, `BootTime`, `MainPathBootTime`, `BootKernelInitTime`, `BootDriverInitTime`, `BootDevicesInitTime`, `BootPrefetchInitTime`, `BootPrefetchBytes`, `BootAutoChkTime`, `BootSmssInitTime`, `BootCriticalServicesInitTime`, `BootUserProfileProcessingTime`, `BootMachineProfileProcessingTime`, `BootExplorerInitTime`, `BootNumStartupApps`, `BootPostBootTime`, `BootIsRebootAfterInstall`, `BootRootCause*Bits`, `BootIsDegradation`, `BootIsStepDegradation`, `BootIsGradualDegradation`, `BootImprovementDelta`, `BootDegradationDelta`, `BootIsRootCauseIdentified`, `OSLoaderDuration`, `BootPNPInitStartTimeMS`, `BootPNPInitDuration`, `OtherKernelInitDuration`, `SystemPNPInitStartTimeMS`, `SystemPNPInitDuration`, `SessionInitStartTimeMS`, `Session0InitDuration`, `Session1InitDuration`, `SessionInitOtherDuration`, `WinLogonStartTimeMS`, `OtherLogonInitActivityDuration`, `UserLogonWaitDuration`.
- Brak danych per pozycja autostartu – do kolumny wpływu się nie nadaje.

## 6. Aplikacje ze Sklepu (windows.startupTask)

- Na czystym 26H2 Pro i Home (po 104 pakiety) jedyny pakiet z rozszerzeniem `windows.startupTask` to `Microsoft.WindowsTerminal_8wekyb3d8bbwe` (1.24.11911.0): `uap5:StartupTask TaskId="StartTerminalOnLoginTask" Enabled="false" DisplayName="ms-resource:AppName"`. Próbka bez sieci i bez Sklepu.
- Stan: `HKU\<SID>_Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\Microsoft.WindowsTerminal_8wekyb3d8bbwe\StartTerminalOnLoginTask` (ta sama gałąź co `HKU\<SID>\Software\Classes\Local Settings\…`).
  - Klucz tworzy sam Windows po zalogowaniu, 15–92 s po starcie (pkt 1 i 8): `State` = 0, `ShowNotification` = 1, `UserEnabledStartupOnce` = 0 (DWORD). Brak klucza = domyślna wartość z manifestu.
  - Włączenie w Ustawieniach: `State` = 2, `UserEnabledStartupOnce` = 1, `ShowNotification` usunięte.
  - Wyłączenie w Ustawieniach: `State` = 1, dodane `LastDisabledTime` (QWORD, sekundy Unix UTC: 1793609249 = 2026-11-02 08:47:29, chwila przełączenia).
- Skutek: `State` = 2 → po restarcie `WindowsTerminal.exe` startuje (rodzic `sihost.exe`); `State` = 1 → nie startuje. Bezpośredni zapis samej wartości `State` (2 albo 1) przez rejestr daje ten sam skutek po restarcie, a Menedżer zadań i Ustawienia pokazują zgodny stan.
- Menedżer zadań i Ustawienia pokazują Terminal z wydawcą „Microsoft Corporation”; wpływ „Nie zmierzono” przy włączonym, „Brak” przy wyłączonym.
- Stany `DisabledByPolicy`/`EnabledByPolicy` (3/4) i aplikacje z `Enabled="true"`: nieprzetestowane (brak próbki na czystym obrazie).

## 7. Wpis wskazujący na nieistniejący plik

- Menedżer zadań, zanim Ustawienia go zobaczą: nazwa = nazwa wartości (`DsoMissing`), pusty wydawca, „Włączona”, „Nie zmierzono”; przy wyświetleniu zapisuje `02` + zera.
- Ustawienia: wiersz bez nazwy programu, z ogólną ikoną i tekstem „Lokalizacja rejestru: HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run\DsoMissing | Brak wpływu”, przełącznik **Off**. Samo otwarcie strony zapisuje `03 00 00 00` + FILETIME chwili otwarcia, czyli Ustawienia **same wyłączają** taki wpis (powtórzone w dwóch seriach: zapis 08:20:01 i 08:34:04, zgodny z chwilą otwarcia strony).
- Po tym Menedżer zadań pokazuje „Wyłączona”, wpływ „Brak”.

## 8. Zapis od zera (pomiar 2026-11-02)

Pytanie: czy zapis przełącznika działa, gdy w rejestrze nie ma jeszcze nic, co pisze Menedżer zadań albo Ustawienia – brak kluczy `StartupApproved` w HKU, brak `Run32` i `StartupFolder` w HKLM, brak wartości pozycji, brak klucza zadania startowego Terminala. Menedżera zadań ani Ustawień w tym pomiarze ani razu nie otwierano.

### Środowisko

- `DSO-26H2-Pro` (`EditionID` = `Professional`) i `DSO-26H2-Home` (`EditionID` = `Core`), oba `DisplayVersion` 26H2, build 26300.9457, VM bez sieci, każdy przebieg od checkpointu `czysta-26H2`, automatyczne logowanie `dso`.
- Zapis rejestru jak w programie: .NET `RegistryKey.OpenBaseKey(…, Registry64)` + `CreateSubKey` (brakujące klucze powstają przy zapisie), z PowerShell Direct jako wbudowany Administrator.
- Sondy jak w „Sondy testowe” oraz w każdym z pięciu miejsc sonda kontrolna `<nazwa>Ctl` (`DsoRunUserCtl`, `DsoRunHklmCtl`, `DsoRunWowCtl`, `DsoLnkUserCtl.lnk`, `DsoLnkCommonCtl.lnk`), której wartości w `StartupApproved` nikt nie zapisuje. Sonda kontrolna potwierdza, że dane miejsce było w danym logowaniu przetworzone.
- Restart z potwierdzeniem jak w pkt 3, odczyt ok. 180 s po zalogowaniu. Start sondy = linia w `probe.log` późniejsza niż `LastBootUpTime`.

### Klucz zadania Terminala tworzy system

Na czystym checkpoincie klucz `StartTerminalOnLoginTask` w `HKU\<SID>_Classes\…\SystemAppData\Microsoft.WindowsTerminal_8wekyb3d8bbwe` pojawia się sam po zalogowaniu, bez Menedżera zadań i Ustawień, z wartościami `State` = 0, `ShowNotification` = 1, `UserEnabledStartupOnce` = 0 (DWORD). Zmierzone (czas pojawienia się albo ostatniego zapisu klucza wobec `LastBootUpTime`): 15–92 s po rozruchu – zwykle 15–22 s, dłużej (50–92 s), gdy w VM trwała kompilacja sond albo obie VM startowały jednocześnie; Pro i Home tak samo. Usunięty w trakcie sesji nie wrócił przez 120 s; wraca przy następnym logowaniu, jeśli go nie ma. Istniejącego klucza (nasz zapis `State` = 2 albo 1, albo przywrócony stan systemu) system przy logowaniu nie nadpisał – czas ostatniego zapisu pozostał sprzed restartu.

Wniosek: stan „brak klucza” trwa tylko kilkanaście–kilkadziesiąt sekund po logowaniu. Typowy stan u użytkownika to klucz z `State` = 0.

### Procedura

Przebieg B (5 miejsc + zadanie Terminala od zera), osobno na Pro i Home:

1. Sondy i wpisy, odczyt `00`: brak `HKU\<SID>\…\Explorer\StartupApproved`, brak `HKLM\…\StartupApproved\Run32` i `\StartupFolder`, w `HKLM\…\StartupApproved\Run` tylko `SecurityHealth`.
2. Wyłączenie: `03 00 00 00` + FILETIME(teraz) dla `DsoRunUser`, `DsoRunHklm`, `DsoRunWow`, `DsoLnkUser.lnk`, `DsoLnkCommon.lnk`. Zapis utworzył klucze HKU `StartupApproved`, HKU `\Run`, HKLM `\Run32`, HKU `\StartupFolder`, HKLM `\StartupFolder` (HKLM `\Run` już istniał – nowa była tylko wartość).
3. Terminal: odczekanie na klucz utworzony przez system i usunięcie go, potem utworzenie klucza od zera z `State` = 2 i `UserEnabledStartupOnce` = 1. Restart → odczyt `01`.
4. Usunięcie pięciu wartości (klucze zostają); Terminal `State` = 1 + `LastDisabledTime` (QWORD, sekundy Unix). Restart → odczyt `02`.
5. Terminal: usunięcie wartości i pustego klucza zadania. Restart → odczyt `03`.

Przebieg C (zadanie Terminala na kluczu utworzonym przez system), osobno na Pro i Home, od czystego checkpointu, bez sond:

1. Odczekanie na klucz systemu (`State` = 0, `ShowNotification` = 1, `UserEnabledStartupOnce` = 0), zapis włączenia jak Ustawienia: `State` = 2, `UserEnabledStartupOnce` = 1, usunięcie `ShowNotification`. Restart → odczyt `01c`.
2. `State` = 1 + `LastDisabledTime`. Restart → odczyt `02c`.
3. Przywrócenie stanu systemu: `State` = 0, `ShowNotification` = 1, `UserEnabledStartupOnce` = 0, bez `LastDisabledTime`. Restart → odczyt `03c`.

### Wyniki

Przebieg B – w każdej komórce: sonda / sonda kontrolna (`tak` = wystartowała w tym logowaniu); identycznie na Pro i Home.

| Miejsce | `01` po wyłączeniu od zera | `02` po usunięciu wartości | `03` |
|---|---|---|---|
| HKU Run (`DsoRunUser`) | nie / tak | tak / tak | tak / tak |
| HKLM Run (`DsoRunHklm`) | nie / tak | tak / tak | tak / tak |
| HKLM Run32 (`DsoRunWow`) | nie / tak | tak / tak | tak / tak |
| Folder użytkownika (`DsoLnkUser.lnk`) | nie / tak | tak / tak | tak / tak |
| Folder wspólny (`DsoLnkCommon.lnk`) | nie / tak | tak / tak | tak / tak |

Zadanie Terminala (`WindowsTerminal.exe`, Pro i Home):

| Przebieg | Krok | Klucz zadania przed restartem | Po restarcie |
|---|---|---|---|
| B | `01` | utworzony od zera: `State` = 2, `UserEnabledStartupOnce` = 1 | działa, rodzic `sihost.exe` (Pro 18 s, Home 18 s po rozruchu) |
| B | `02` | `State` = 1 + `LastDisabledTime` | nie działa |
| B | `03` | usunięty | nie działa; klucz odtworzony przez system 15 s (Pro) i 16 s (Home) po rozruchu z `State` = 0, `ShowNotification` = 1, `UserEnabledStartupOnce` = 0 |
| C | `01c` | klucz systemu → `State` = 2, `UserEnabledStartupOnce` = 1, bez `ShowNotification` | działa, rodzic `sihost.exe` |
| C | `02c` | `State` = 1 + `LastDisabledTime` | nie działa |
| C | `03c` | przywrócony stan systemu (`State` = 0, `ShowNotification` = 1, `UserEnabledStartupOnce` = 0) | nie działa (manifest: `Enabled="false"`); klucz bez zmian |

### Wniosek per źródło

| Źródło | Zapis od zera |
|---|---|
| HKU Run | potwierdzone |
| HKLM Run | potwierdzone |
| HKLM Run32 (WOW6432Node) | potwierdzone |
| Folder Autostart użytkownika | potwierdzone |
| Folder Autostart wspólny | potwierdzone |
| Zadanie startowe AppX (Terminal), klucz nieistniejący | potwierdzone |
| Zadanie startowe AppX (Terminal), klucz systemu ze `State` = 0 | potwierdzone (włączenie, wyłączenie i powrót do stanu systemu) |

- Wyłączenie od zera działa bez wcześniejszego otwarcia Menedżera zadań i Ustawień: wystarcza utworzenie brakujących kluczy i wartości `03 00 00 00` + FILETIME. Usunięcie samej wartości (klucze zostają) przywraca start.
- Wszystko na Pro i Home jednakowo.

## Dowody

Surowe odczyty (JSON, eksporty `.reg`, kopie StartupInfo, zrzuty ekranu VM) są w katalogu pomiaru, po jednym podkatalogu lub pliku na krok: `01-clean-early`, `06-boot1-late`, `08a-noGui` … `19-reboot-terminal-off-zeroft`, `20-appx-home`, `21-home-clean`. Pomiar z pkt 8 ma osobny katalog z podkatalogiem na edycję (`pro`, `home`): odczyty `00-before`, `01-after-disable-zero`, `02-after-undo`, `03-after-terminal-undo`, `00c-before`, `01c-after-enable-state0`, `02c-after-disable`, `03c-after-baseline`, wyniki zapisów i dziennik przebiegu. Po pomiarze obie VM przywrócone do `czysta-26H2` i wyłączone, karta sieciowa niepodłączona.
