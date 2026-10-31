# Etap 4a – źródła: prywatność, AI, reklamy (research 2026-10-20)

Zakres (mapa drogowa autora, Etap 4a): Copilot, Recall/Click to Do, Widgets, Bing w Start, reklamy i sugestie, telemetria przez polityki,
klasyczne menu kontekstowe, End Task na pasku. Research tylko ze źródeł Microsoftu (MS Learn, support.microsoft.com, blogi MS),
pobranych 2026-10-20. Nic nie było uruchamiane na hoście ani na VM. Każdy mechanizm oznaczony „hipoteza” wymaga przed wpisaniem
do katalogu odczytu na VM 26H2 (Home i Pro) po zmianie oficjalnym narzędziem (zasada ze spec Etapu 2, pkt 11).

Autor sprawdził wyrywkowo trzy kluczowe twierdzenia bezpośrednio w MS Learn (2026-10-20):
- `manage-windows-copilot`: TurnOffWindowsCopilot „is subject to near-term deprecation”. MS zaleca zamiast tej polityki AppLocker (pakiet `MICROSOFT.COPILOT`) albo `Get-AppxPackage -Name "Microsoft.Copilot"` + `Remove-AppxPackage`.
- `configure-windows-diagnostic-data-in-your-organization`: poziom „Diagnostic data off (Security)” „is only available on Windows Server, Windows Enterprise, and Windows Education editions”.
- `policy-csp-experience`: `AllowWindowsConsumerFeatures` ma w tabeli edycji „❌ Pro”; rejestr `Software\Policies\Microsoft\Windows\CloudContent`, wartość `DisableWindowsConsumerFeatures`.

**Najważniejsze dla planu Etapu 4a:**
- **Home:** w tabelach edycji Policy CSP nie występuje. Każda polityka HKLM `Policies` wymaga sprawdzenia skutku na VM Home.
- **Pro nie obsługuje wielu „oczywistych” polityk.** Dotyczy to consumer experiences, całego Spotlight, Tips, sugestii w Ustawieniach, `DoNotUseWebResults` (Bing w Start) i poziomu telemetrii 0. Zapis do rejestru może przejść bez żadnego skutku, a katalog nie może wtedy obiecywać efektu.
- **Bez dokumentacji MS:** klasyczne menu kontekstowe (CLSID w HKCU), `TaskbarEndTask`, `TaskbarDa`, `BingSearchEnabled`, klucze ContentDeliveryManager. Według zasad repo („nie wymyślaj kluczy”) mogą wejść tylko po decyzji autora i z dowodem z VM, inaczej wypadają.
- **Utrata danych przy wyłączaniu Recall:** `AllowRecallEnablement=0` / `DisableAIDataAnalysis=1` kasują migawki. Przed taką zmianą potrzebne jest ostrzeżenie w katalogu.

## Etap 4a, część 1 – Copilot, Recall/Click to Do, Widgets, Bing w Start (research 2026-10-20)

Źródła pobrane 2026-10-20 (MS Learn, daty `ms.date` przy źródłach). Dowodem jest MS Learn; skutek w rejestrze/stanie ustala odczyt na VM 26H2
(Home i Pro). „hipoteza” = źródło nie-MS albo MS tego nie dokumentuje, wymaga VM przed wpisaniem do katalogu. Nic nie uruchamiano na hoście ani VM.
Uwaga ogólna: w kolumnie „Editions” stron Policy CSP **Home nie występuje nigdy** (kolumny: Pro / Enterprise / Education / IoT Enterprise).
Brak Home znaczy „MS nie deklaruje wsparcia”, nie „nie działa”. Wartości w kluczach Policies da się zapisać także na Home, ale czy Home je honoruje, MS nie mówi → VM.

### 0. Co robił v3.2 (poprzednia, niepubliczna wersja 3.2)

Grep (copilot, recall, bing, TaskbarDa, ClickToDo, NewsAndInterests, Dsh) po `*.py`: **zero trafień**. Trafienia „widget” to wyłącznie klasy Qt
(`QWidget`, `tweak_widget.py`), niezwiązane z Widżetami Windows. v3.2 niczego z tych 4 tematów nie robił – wszystko nowe, brak aliasów do zachowania.

### 1. Microsoft Copilot (aplikacja, klawisz, TurnOffWindowsCopilot)

**Źródła**
- Policy CSP WindowsAI, ms.date 2026-09-10: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai
- „Updated Windows and Microsoft Copilot Chat experience”, ms.date 2025-11-20: https://learn.microsoft.com/en-us/windows/client-management/manage-windows-copilot

**Mechanizm A – TurnOffWindowsCopilot (legacy, deprecated)**
- CSP: `./User/Vendor/MSFT/Policy/Config/WindowsAI/TurnOffWindowsCopilot`, zakres **tylko User**, int 0/1 (1 = „Disable Copilot”).
- GPO: User Configuration > Administrative Templates > Windows Components > Windows Copilot > „Turn off Windows Copilot”.
- ADMX (MS podaje): `SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot`, wartość `TurnOffWindowsCopilot` (hive HKCU wynika z zakresu User; DWORD to wniosek z „int”).
- Edycje wg MS: Pro, Enterprise, Education, IoT Enterprise (Home brak). Applicable: Windows 11 22H2 od 10.0.22621.2361, 23H2 (10.0.22631) „and later”.
- Cytat: „This policy is deprecated and may be removed in a future release.” Oraz: „The TurnOffWindowsCopilot policy isn't for the new Copilot experience that's in some Windows Insider builds and that will be gradually rolling out to Windows 11 and Windows 10 devices.”
- Cytat (manage-windows-copilot): „AppLocker policy should be used instead of the Turn Off Windows Copilot legacy policy setting and its MDM equivalent, TurnOffWindowsCopilot. The policy is subject to near-term deprecation.”
- **Wniosek:** na 24H2+/26H2 polityka dotyczy starego panelu „Copilot in Windows”, zastąpionego aplikacją. MS nie pisze wprost, że nie działa na aplikacji; pisze, że nie jest do „nowego doświadczenia” i jest do wycofania. Efekt na 26H2 → VM.

**Mechanizm B – RemoveMicrosoftCopilotApp (polityka odinstalowania)**
- CSP: `./Device/...` i `./User/Vendor/MSFT/Policy/Config/WindowsAI/RemoveMicrosoftCopilotApp`, int 0/1 (1 = „Removal Enabled”). Applicable: Windows 11 24H2 [10.0.26100] i nowsze.
- GPO: ścieżka „WindowsAI > AT > WindowsComponents > WindowsAI”; **MS nie podaje Registry Key/Value Name** (brak sekcji ADMX mapping z kluczem) → nie wymyślać.
- **Edycje: Pro ❌ w tabeli**, Enterprise/Education/IoT ✅. Tekst: „This setting applies to Enterprise, Professional and Education client SKUs only.” (sprzeczność w stronie: tabela ❌ Pro, tekst „Professional”).
- Cytat: „It will apply to devices/users that meet the below conditions: Microsoft 365 Copilot and Microsoft Copilot are both installed; the Microsoft Copilot app wasn't installed by the user; the Microsoft Copilot app wasn't launched in the last 28 days.” Oraz: „Users can still re-install if they choose to.”
- **Wniosek:** nieprzydatna dla narzędzia na świeżych Home/Pro (warunki rzadko spełnione, brak Home, brak klucza do detekcji).

**Mechanizm C – usunięcie pakietu (udokumentowane)**
- MS podaje skrypt: `Get-AppxPackage -Name "Microsoft.Copilot" | Select-Object -ExpandProperty PackageFullName` → `Remove-AppxPackage -Package $packageFullName`. Uninstall z UI: Settings > Apps > Installed Apps.
- Hipoteza (Q&A, nie-dokument): usunięcie dotyczy tylko bieżącego użytkownika, inni użytkownicy mogą dostać pakiet ponownie. `-AllUsers` / pakiet provisioned (`Get-AppxProvisionedPackage`) – **MS tej strony tego nie opisuje**.
- Undo: MS nie opisuje; reinstalacja ze Sklepu (https://apps.microsoft.com/detail/9NHT9RB2F4HD), wymaga sieci. Repo ma helpery `Get-DsoStorePackage`/`Register-DsoStorePackage` (zasady repo), ale nie dotyczą instalacji ze Sklepu → `reversible` raczej częściowo/false.

**Mechanizm D – AppLocker (udokumentowane)**
- Reguła packaged app: Publisher `CN=MICROSOFT CORPORATION, O=MICROSOFT CORPORATION, L=REDMOND, S=WASHINGTON, C=US`, Package name `MICROSOFT.COPILOT`, wersja „* (and above)”. Cytat: „Prevent the consumer app from being installed if it isn't already on the device. Block the consumer app from being launched if it's already installed.”
- Dostępność AppLocker na Home/Pro: **hipoteza** (nie czytano dokumentacji AppLocker). Polityka systemowa, trudna detekcja/undo – nie nadaje się na tweak z pendrive'a bez osobnej decyzji.

**Mechanizm E – klawisz Copilot**
- `SetCopilotHardwareKey`: `./User/Vendor/MSFT/Policy/Config/WindowsAI/SetCopilotHardwareKey`, zakres **User**, string (AUMID, np. `Microsoft.MicrosoftOfficeHub_8wekyb3d8bbwe!Microsoft.MicrosoftOfficeHub`). GPO: User Configuration > Administrative Templates > Windows Components > Windows Copilot > „Set Copilot Hardware Key”. ADMX: klucz `SOFTWARE\Policies\Microsoft\Windows\CopilotKey` (nazwa wartości nie podana). Pro/Ent/Edu. Applicable: Win 11 22H2 od 10.0.22621.4391.
- Cytat: „If the policy isn't configured, Copilot will open if it's available in that country or region.” Użytkownik może zmienić sam: `ms-settings:personalization-textinput-copilot-hardwarekey`. To mapowanie klawisza, nie wyłączenie Copilota.
- Win+C: MS pisze, że klawisz (lub Win+C) otwiera „lightweight prompt box”; polityki wyłączającej Win+C nie znaleziono (**MS nie dokumentuje**).

**Detekcja:** C – obecność pakietu `Microsoft.Copilot` (nie rejestr). A – `TurnOffWindowsCopilot`=1 w HKU\{UserSid}.
**Undo:** A – `RemoveEntry` wg snapshotu. C – reinstalacja pakietu (nieudokumentowana).
**Ryzyka:** A ustawia wartość, ale MS mówi, że dotyczy starego doświadczenia i jest do wycofania → opis pozycji może obiecać tylko „ustawia wartość polityki”. C – per-user, może wrócić; nie pomylić z pakietem aplikacji Microsoft 365 (MS wprost: aplikacja M365 Copilot to inna rzecz).
**Do VM:** Home i Pro 26H2: czy `Microsoft.Copilot` jest zainstalowany po świeżej instalacji; czy `TurnOffWindowsCopilot`=1 cokolwiek zmienia (ikona, Win+C); `Remove-AppxPackage` bieżący użytkownik vs `-AllUsers`; czy pakiet wraca po restarcie.

### 2. Recall i Click to Do

**Źródła**
- Policy CSP WindowsAI (jw.), ms.date 2026-09-10.
- „Manage Recall for Windows clients”, ms.date 2025-12-10: https://learn.microsoft.com/en-us/windows/client-management/manage-recall
- „Manage Click to Do for Windows clients”, ms.date 2025-12-10: https://learn.microsoft.com/en-us/windows/client-management/manage-click-to-do

**Czy na zwykłym PC bez NPU jest coś do wyłączenia?**
- Recall (cytat): „A Copilot+ PC that meets the Secured-core standard”, „40 TOPs NPU”, „16 GB RAM”, „8 logical processors”, „256 GB storage capacity”; plus Device Encryption/BitLocker i Windows Hello ESS z biometrią.
- Click to Do (cytat): „A Copilot+ PC or eligible Cloud PC”, „40 TOPs NPU”, 16 GB RAM, 8 logical processors, 256 GB.
- **MS nie pisze wprost, co się dzieje na sprzęcie niespełniającym wymagań** (czy funkcja opcjonalna Recall istnieje w obrazie, czy polityki są ignorowane). Hipoteza: na PC bez NPU funkcje są niedostępne, a zapis polityk jest „prewencyjny” (przydatny po zmianie sprzętu/na Copilot+). Do VM.

**2a. AllowRecallEnablement**
- CSP: `./Device/Vendor/MSFT/Policy/Config/WindowsAI/AllowRecallEnablement`, zakres **tylko Device**, int: 0 = „Recall isn't available.”, 1 (domyślna) = „Recall is available.”
- GPO: Computer Configuration > Administrative Templates > Windows Components > Windows AI > „Allow Recall to be enabled”.
- ADMX (MS): `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI`, wartość `AllowRecallEnablement` (WindowsCopilot.admx; DWORD to wniosek).
- Edycje: Pro ✅, Enterprise ✅, Education ✅, IoT ✅ (Home brak). Applicable: Windows 11 24H2 z KB5055627 [10.0.26100.3915] i nowsze.
- Cytat: „If this policy is disabled, the Recall component will be in disabled state and the bits for Recall will be removed from the device. If snapshots were previously saved on the device, they'll be deleted when this policy is disabled. Removing Recall requires a device restart.”
- Domyślnie (manage-recall): „By default, Recall is disabled and removed on managed devices.”; na niezarządzanych Copilot+: „Recall is available by default but a user has to opt in to save snapshots.”
- **Wniosek:** 0 usuwa komponent i kasuje migawki (nieodwracalne dla danych); `requiresReboot=true` (MS podaje).

**2b. DisableAIDataAnalysis („Turn off saving snapshots for Recall”)**
- CSP: `./Device/...` i `./User/Vendor/MSFT/Policy/Config/WindowsAI/DisableAIDataAnalysis`, zakres **Device i User**, int: 0 (domyślna) = „Enable Saving Snapshots for Recall.”, 1 = „Disable Saving Snapshots for Recall.”
- GPO: Computer i User Configuration > Administrative Templates > Windows Components > Windows AI > „Turn off saving snapshots for Recall”.
- ADMX: `SOFTWARE\Policies\Microsoft\Windows\WindowsAI`, wartość `DisableAIDataAnalysis` (HKLM dla Device, HKCU dla User).
- Edycje: Pro ✅ Ent ✅ Edu ✅ IoT ✅ (Home brak). Applicable: 24H2 KB5055627 [10.0.26100.3915] i nowsze.
- Cytat: „If you enable this policy, snapshots won't be saved for use with Recall. If snapshots were previously saved on the device, they'll be deleted when this policy is enabled.” Oraz: „The choice to enable saving snapshots requires individual user opt-in consent.”
- **Wniosek:** `1` to kierunek bezpieczny (blokuje zapis, kasuje istniejące migawki); niczego nie włącza użytkownikowi.

**2c. Funkcja opcjonalna Recall**
- MS (manage-recall) podaje: `Enable-WindowsOptionalFeature -Online -FeatureName "Recall"` oraz `Disable-WindowsOptionalFeature -Online -FeatureName "Recall" -Remove`. Cytat: „If a user prefers to remove the Recall bits from their device after an IT admin has enabled Allow Recall to be enabled, they can use the following PowerShell command”.
- Nazwa funkcji `Recall` udokumentowana. Detekcja: `Get-WindowsOptionalFeature -Online -FeatureName Recall` (skutek na sprzęcie bez NPU → VM). Undo po `-Remove` wymaga źródła pakietu (MS nie opisuje) → odwracalność niepewna. `*-WindowsOptionalFeature` nie jest na liście bezpieczników harnessu (zasady repo wymieniają `dism`) – sprawdzić przy implementacji.
- Powiązanie z C4: MS „We do not recommend setting this registry key [PreventDeviceEncryption] on devices with Recall feature”.

**2d. DisableClickToDo**
- CSP: `./Device/...` i `./User/Vendor/MSFT/Policy/Config/WindowsAI/DisableClickToDo`, **Device i User**, int: 0 (domyślna) = „Click to Do is enabled.”, 1 = „Click to Do is disabled.”
- GPO: Computer i User Configuration > Administrative Templates > Windows Components > Windows AI > „Disable Click to Do”.
- ADMX: `SOFTWARE\Policies\Microsoft\Windows\WindowsAI`, wartość `DisableClickToDo`.
- Edycje: Pro ✅ Ent ✅ Edu ✅ IoT ✅ (Home brak). **Applicable OS na stronie CSP (2026-09-10): „Windows Insider Preview” – brak numeru buildu GA.** Strona zarządzania (2025-12-10) opisuje politykę normalnie, bez buildu. Czy działa na wydaniu 26H2 build 26300 → niepewne, VM.
- Cytat: „When the policy is enabled, the Click to Do component and entry points won't be available to users.” Oraz: „This policy doesn't affect Click to Do in Recall.”
- Użytkownik może wyłączyć sam: Settings > Privacy & security > Click to Do.

**Detekcja / Undo / Ryzyka (sekcja 2)**
- Detekcja rejestrowa: `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` → `AllowRecallEnablement`, `DisableAIDataAnalysis`, `DisableClickToDo`; dla User: HKU\{UserSid}. Uczciwa obietnica: „ustawia politykę”, nie „wyłącza Recall”.
- Undo: snapshot z journala; `original` = `RemoveEntry` (MS: „isn't configured” = domyślnie). `AllowRecallEnablement`=0 i `DisableAIDataAnalysis`=1 kasują migawki (nieodwracalne) – na PC bez Recall bez znaczenia, na Copilot+ RED FLAG.
- Ryzyko: `safe` dla DisableAIDataAnalysis/DisableClickToDo; `caution` dla AllowRecallEnablement=0 (komponent, restart).
- Do VM: czy na Home/Pro VM (brak NPU) istnieje funkcja `Recall`; czy zapis kluczy zmienia coś w Ustawieniach (Privacy & security > Recall & snapshots / Click to Do).

### 3. Widgets (Tablica / Widżety)

**Źródła**
- Policy CSP NewsAndInterests, ms.date 2025-10-03: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests
- Taskbar overview, ms.date 2025-04-03: https://learn.microsoft.com/en-us/windows/configuration/taskbar/ (wymienia Widgets jako element paska; bez polityk ani TaskbarDa)
- `TaskbarDa`: **brak dokumentacji MS** w przeszukanych stronach (CSP, taskbar docs).

**3a. AllowNewsAndInterests („Allow widgets”)**
- CSP: `./Device/Vendor/MSFT/Policy/Config/NewsAndInterests/AllowNewsAndInterests`, zakres **tylko Device**, int: 0 = „Not allowed.”, 1 (domyślna) = „Allowed.”
- GPO: Computer Configuration > Administrative Templates > Windows Components > Widgets > „Allow widgets”.
- ADMX (MS): `HKLM\SOFTWARE\Policies\Microsoft\Dsh`, wartość `AllowNewsAndInterests` (NewsAndInterests.admx; DWORD to wniosek).
- Edycje: Pro ✅ Ent ✅ Edu ✅ IoT ✅ (Home brak). Applicable: Windows 11 21H2 [10.0.22000] i nowsze.
- Cytat: „This policy specifies whether the widgets feature is allowed on the device.” Oraz: „This policy applies to the entire widgets experience, including content on the taskbar.”
- **Wniosek:** jedyny udokumentowany przez MS mechanizm wyłączenia Widżetów, obejmuje przycisk na pasku. Home: MS nie deklaruje; źródła nie-MS (winaero, cloudinfra) twierdzą, że klucz działa na Home – **hipoteza, VM Home**.
- Hipoteza z forów (nie-MS): na 24H2+ UCPD może blokować zapis z `reg.exe` dla niektórych kluczy; czy dotyczy `Policies\Microsoft\Dsh` – nieznane, test przez nasze API C#.

**3b. DisableWidgetsBoard / DisableWidgetsOnLockScreen**
- `./Device/.../NewsAndInterests/DisableWidgetsBoard` i `DisableWidgetsOnLockScreen`, int 0/1, `HKLM\SOFTWARE\Policies\Microsoft\Dsh` wartości o tych samych nazwach; GPO „Windows Components > Widgets”. Pro+ ✅. Applicable: **„Windows Insider Preview”** (bez buildu GA).
- Cytat: „If you enable this policy setting, you won't be able to invoke the Widgets board and its entry point will no longer appear on the taskbar.” → kandydaci dopiero po VM 26H2.

**3c. TaskbarDa**
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDa`: **MS nie dokumentuje.** Jedyna wskazówka z domeny MS to wątek Q&A (https://learn.microsoft.com/en-us/answers/questions/5638954/protected-registry-keys, 2025-11-28; odpowiedź moderatora „Microsoft External Staff”, nie dokumentacja): „Many registry values that previously controlled these behaviors - like ShellFeedsTaskbarViewMode or TaskbarDa - are now system-managed. They still exist in the registry but no longer act as writable switches.” oraz „At this time, there is no supported method to force-set these shell UI values through the registry.” Bez numeru buildu i bez cytowanego źródła → hipoteza.
- Starsze wątki Q&A: zapis 0/1 ukrywał/pokazywał przycisk. GitHub (nie-MS): „TaskbarDa is locked on 24H2+”.
- **Wniosek:** nie wpisywać TaskbarDa do katalogu bez wyniku VM 26H2 (czy zapis HKCU przez API C# się utrzymuje po restarcie Explorera/wylogowaniu); w razie niepowodzenia ścieżka = 3a.

**Detekcja / Undo / Ryzyka (sekcja 3)**
- Detekcja: `HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests`=0. Obietnica: „ustawia politykę Widgets”; zniknięcie ikony bez potwierdzenia VM niegwarantowane (MS nie mówi o restarcie Explorera/wylogowaniu).
- Undo: `RemoveEntry` (MS: „1 (Default) Allowed”).
- Ryzyka: `safe`; polityka blokuje całą funkcję (użytkownik nie włączy z Ustawień) – napisać w opisie; na Home może nie być honorowana.
- Do VM: Home i Pro 26H2: czy `AllowNewsAndInterests`=0 chowa przycisk i panel; czy trzeba restartu Explorera/sesji; TaskbarDa: czy istnieje po świeżej instalacji, czy zapis HKCU się utrzymuje, czy UCPD cokolwiek blokuje nasz proces.

### 4. Bing / wyszukiwanie w sieci w menu Start

**Źródła**
- Policy CSP Search, ms.date 2025-03-12: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-search
- Policy CSP ADMX_WindowsExplorer, ms.date 2025-03-12: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-windowsexplorer (czytano pierwsze 100 000 znaków z ~176 000; DisableSearchBoxSuggestions znaleziono)

**4a. DoNotUseWebResults (GPO „Don't search the web or display web results in Search”)**
- CSP: `./Device/Vendor/MSFT/Policy/Config/Search/DoNotUseWebResults`, zakres **tylko Device**, int. Semantyka: 0 = „Not allowed. Queries won't be performed on the web and web results won't be displayed when a user performs a query in Search.”, 1 (domyślna) = „Allowed.”
- GPO: Computer Configuration > Administrative Templates > Windows Components > Search > „Don't search the web or display web results in Search”.
- ADMX (MS): `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search`, **wartość `ConnectedSearchUseWeb`** (nazwa w rejestrze ≠ nazwa CSP). Search.admx. Mapowanie 0 CSP ↔ wartość rejestru 0 to wniosek (MS podaje tylko nazwę wartości) – potwierdzić na VM.
- Edycje: **Pro ❌**, Enterprise ✅, Education ✅, IoT Enterprise ✅. Applicable: Windows 10 1803 [10.0.17134] i nowsze (strona nie wymienia osobno Windows 11).
- Cytat: „This policy setting allows you to control whether or not Search can perform queries on the web, and if the web results are displayed in Search.”
- **Wniosek:** MS dokumentuje politykę dla Enterprise/Education, bez Pro i Home. Skutek w menu Start Windows 11 24H2+/26H2 – MS nie potwierdza; fora twierdzą, że działa – hipoteza → VM.
- „Do not allow web search” (`DisableWebSearch`) nie występuje na stronie CSP Search → **MS Learn jej nie opisuje**; hipoteza z forów.

**4b. DisableSearchBoxSuggestions**
- CSP: `./User/Vendor/MSFT/Policy/Config/ADMX_WindowsExplorer/DisableSearchBoxSuggestions`, zakres **tylko User**, format string (ADMX-backed, SyncML). Edycje: Pro, Enterprise, Education, IoT. Applicable: Windows 11 21H2 [10.0.22000] i nowsze.
- GPO: User Configuration > Administrative Templates > Windows Components > File Explorer > „Turn off display of recent search entries in the File Explorer search box”.
- ADMX: `HKCU\Software\Policies\Microsoft\Windows\Explorer`, wartość `DisableSearchBoxSuggestions` (WindowsExplorer.admx; DWORD=1 to wniosek).
- Cytat: „Disables suggesting recent queries for the Search Box and prevents entries into the Search Box from being stored in the registry for future references.”
- **KLUCZOWE:** MS opisuje tę politykę jako dotyczącą pola wyszukiwania **File Explorera**, nie menu Start ani Bing. Poradniki (guidingtech, woshub, nie-MS) twierdzą, że wyłącza sugestie Bing w Start Win 11 – **hipoteza (źródło nie-MS / do potwierdzenia na VM)**. Nie opisywać pozycji jako „wyłącz Bing w Start” bez wyniku VM.

**4c. BingSearchEnabled**
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Search` → `BingSearchEnabled`=0: **brak dokumentacji MS** (wyniki tylko Q&A, sprzeczne: część twierdzi, że działa po wylogowaniu, inna że nie ma efektu). **Hipoteza (źródło nie-MS / do potwierdzenia na VM).**

**4d. Pokrewne udokumentowane (poza zakresem zadania)**
- `AllowSearchHighlights`: `./Device/.../Search/AllowSearchHighlights`, int 0/1; ADMX: `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search`, wartość **`EnableDynamicContentInWSB`**; GPO „Allow search highlights”; Pro+; Win 11 21H2 z KB5023774 [22000.1761] i 22H2+. Cytat: „Disabling this setting turns off search highlights in the start menu search box and in search home.”
- `AllowCloudSearch`, `AllowSearchToUseLocation` (wartość `AllowSearchToUseLocation`), `ConfigureSearchOnTaskbarMode` (24H2+, 0 = Hide) – opcjonalne.

**Detekcja / Undo / Ryzyka (sekcja 4)**
- Detekcja: rejestr (`ConnectedSearchUseWeb` w HKLM; `DisableSearchBoxSuggestions` w HKU\{UserSid}). Skutek (brak Bing w Start) niewykrywalny z rejestru – weryfikacja manualna na VM.
- Undo: `RemoveEntry` wg snapshotu (MS: „not configured” = wybór użytkownika).
- Ryzyka: `safe`; DoNotUseWebResults na Pro = zapis bez gwarancji MS; łatwo pomylić semantykę 0/1 (odwrócona wobec nazwy „DoNot…”).
- Do VM (Home i Pro): (1) świeży system: czy Start pokazuje Bing przy regionie PL (zasady dla EEA nie sprawdzane tutaj – nieweryfikowane); (2) po `ConnectedSearchUseWeb`=0 w HKLM znikają wyniki sieciowe?; (3) po `DisableSearchBoxSuggestions`=1 zmiana w Start czy tylko w Eksploratorze?; (4) czy `BingSearchEnabled` istnieje po świeżej instalacji.

### 5. Tabela kandydatów

| Kandydat (id roboczy) | Mechanizm | Home? | Ryzyko | Pewność źródła |
|---|---|---|---|---|
| privacy.copilot-turnoff-policy | HKU `...\Policies\Microsoft\Windows\WindowsCopilot` `TurnOffWindowsCopilot`=1 | MS: Pro+; Home brak, skutek nieznany | caution (deprecated, stary panel) | MS (klucz/wartość); skutek na 26H2 = hipoteza/VM |
| privacy.copilot-remove-app | `Remove-AppxPackage` pakietu `Microsoft.Copilot` (skrypt MS) | prawdopodobnie tak (hipoteza) | caution (per-user, wraca, undo = reinstalacja) | MS (komenda); zakres/undo = hipoteza |
| privacy.copilot-remove-policy | `WindowsAI\RemoveMicrosoftCopilotApp`=1 | nie (Pro ❌, Ent/Edu) | nie do katalogu | MS (CSP); klucz rejestru NIE podany |
| privacy.copilot-key-remap | `SetCopilotHardwareKey` (AUMID) | MS: Pro+ | safe, ale wymaga wyboru aplikacji | MS (CSP/GPO); nazwa wartości nie podana |
| privacy.recall-disallow | HKLM `...\WindowsAI` `AllowRecallEnablement`=0 | MS: Pro+; Home brak | caution (usuwa komponent, kasuje dane, restart) | MS (klucz+wartość+opis) |
| privacy.recall-no-snapshots | HKLM/HKU `...\WindowsAI` `DisableAIDataAnalysis`=1 | MS: Pro+; Home brak | safe (kasuje istniejące migawki) | MS (klucz+wartość) |
| privacy.recall-feature-remove (akcja) | `Disable-WindowsOptionalFeature -Online -FeatureName "Recall" -Remove` | nieznane (VM) | caution (undo wymaga źródła) | MS (komenda, nazwa funkcji); skutek bez NPU = hipoteza |
| privacy.clicktodo-off | `...\WindowsAI` `DisableClickToDo`=1 (HKLM/HKU) | MS: Pro+; Home brak | safe (funkcja tylko Copilot+) | MS (klucz); Applicable = Insider → VM |
| privacy.widgets-off | HKLM `Policies\Microsoft\Dsh` `AllowNewsAndInterests`=0 | MS: Pro+; Home brak (fora: działa – hipoteza) | safe | MS (klucz+wartość+opis) |
| privacy.widgets-board-off | `Dsh` `DisableWidgetsBoard`=1 / `DisableWidgetsOnLockScreen`=1 | MS: Pro+ | safe | MS; Applicable = Insider → VM |
| privacy.widgets-taskbarda (nie dodawać) | HKCU `Explorer\Advanced` `TaskbarDa`=0 | n/d | do ustalenia | **hipoteza (MS nie dokumentuje; Q&A: „system-managed”)** |
| privacy.search-web-off | HKLM `Policies\Microsoft\Windows\Windows Search` `ConnectedSearchUseWeb`=0 (CSP DoNotUseWebResults) | MS: Pro ❌, Ent/Edu ✅ | safe (semantyka odwrócona) | MS (klucz/wartość); skutek w Start Win 11 = hipoteza/VM |
| privacy.search-explorer-suggestions | HKU `Policies\Microsoft\Windows\Explorer` `DisableSearchBoxSuggestions`=1 | MS: Pro+ | safe | MS (opis = pole Eksploratora); Bing w Start = hipoteza |
| privacy.search-bing-hkcu (nie dodawać) | HKCU `...\Search` `BingSearchEnabled`=0 | n/d | do ustalenia | **hipoteza (źródło nie-MS / do potwierdzenia na VM)** |
| privacy.search-highlights-off | HKLM `...\Windows Search` `EnableDynamicContentInWSB`=0 | MS: Pro+ | safe | MS (klucz+wartość) |

### 6. Pytania do autora (max 5)

1. **Home vs Pro:** MS nie deklaruje żadnej z tych polityk dla Home. Czy pozycje z kluczami Policies mają być dostępne także na Home (zapis „na próbę”, skutek potwierdzony na VM), czy na Home ukrywać/oznaczać „bez wsparcia MS”? (decyzja projektowa)
2. **Copilot:** czy wystarczy `TurnOffWindowsCopilot` jako zapis polityki (MS: przestarzała, stary panel), czy chcesz też akcję usunięcia pakietu `Microsoft.Copilot` (komenda MS, ale per-user, może wrócić, brak prostego undo)? Czy dopuszczasz usunięcie bez undo?
3. **Recall/Click to Do:** na PC bez NPU polityki najpewniej nic nie zmieniają. Robić jedną pozycję „prewencyjną” (Recall + Click to Do), czy wpisać dopiero, gdy VM pokaże efekt? Czy akcja `Disable-WindowsOptionalFeature Recall -Remove` jest akceptowalna (kasuje dane, undo niejasne)?
4. **Widgets:** jeśli `TaskbarDa` na 26H2 okaże się niezapisywalny, czy wystarcza `AllowNewsAndInterests` (blokuje całą funkcję, użytkownik nie włączy z Ustawień)?
5. **Bing w Start:** MS dokumentuje tylko `DoNotUseWebResults` (bez Pro) i `DisableSearchBoxSuggestions` (opis dotyczy Eksploratora). Czy wpisy oparte na forach (`BingSearchEnabled`, `DisableWebSearch`) wchodzą po wyniku VM jako „bez źródła MS”, czy zasada „źródło MS” ma pozostać twarda i te pozycje odpadają?

## Etap 4a, część 2 – reklamy/sugestie, telemetria (polityki), klasyczne menu PPM, End Task (research 2026-10-20)

Źródła pobrane 2026-10-20: strony Policy CSP (ms.date 2026-09-10), „Manage connections..." (ms.date 2026-02-12), „Configure Windows diagnostic data..." (2025-09-11),
„Advanced Windows Settings" (2026-01-22), „Add a File Explorer context menu command..." (2026-07-16). Wszystko oznaczone „hipoteza" wymaga potwierdzenia na VM.

**Uwaga ogólna o edycjach (ważne dla Home):** w tabelach Policy CSP kolumna „Editions" wymienia tylko Pro / Enterprise / Education / IoT Enterprise – **Home nie występuje w żadnej z tych tabel**
(ani jako ✅, ani jako ❌). MS nie podaje więc wprost, czy dana polityka działa na Home. Policy CSP to API MDM, a Edytor Zasad Grupy na Home nie istnieje.
To, że klucz `Software\Policies\...` na Home zadziała po zapisie do rejestru, MS nie dokumentuje -> „hipoteza, do potwierdzenia na VM Home". Edycje poniżej cytuję z tabel (Pro ✅/❌).
Uwaga o renderze: tabele odczytano z pobranej strony (konwersja do markdown); kolumna Home mogła zostać pominięta przez konwersję, więc werdykt „Home brak w tabeli" też wymaga rzutu oka na stronę w przeglądarce.

### 0. Co robił v3.2 (poprzednia, niepubliczna wersja 3.2)
Grep po: telemetry, DiagTrack, consumer, advertis, spotlight, context, menu, EndTask, AdvertisingInfo, ContentDelivery, Recommend – **zero trafień dla tematów 1–4**.
Jedyne okolice: `ux_menu` (MenuShowDelay, nie dotyczy), `sec_take_ownership*` (część C4, nie ta). Nic do portowania, wszystko od zera.

### 1. Reklamy i sugestie

#### 1a. „Turn off Microsoft consumer experiences" (AllowWindowsConsumerFeatures)
**Źródła**
- Policy CSP Experience, https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience
  - CSP: `./Device/Vendor/MSFT/Policy/Config/Experience/AllowWindowsConsumerFeatures`, zakres **Device** (przed 1803 był User), Windows 10 1607+ (10.0.14393).
  - Edycje wg tabeli: **❌ Pro, ✅ Enterprise, ✅ Education, ✅ IoT Enterprise**. Home – brak w tabeli.
  - „This policy allows IT admins to turn on experiences that are typically for consumers only, such as Start suggestions, Membership notifications, Post-OOBE app install and redirect tiles. Most restricted value is 0."
    Wartości: 0 = Not allowed, 1 = Allowed (domyślnie). Zależność (DependsOn): `User/.../Experience/AllowWindowsSpotlight` = 1.
  - GPO: Computer Configuration > Administrative Templates > Windows Components > Cloud Content > „Turn off Microsoft consumer experiences" (CloudContent.admx).
  - ADMX: `HKLM\Software\Policies\Microsoft\Windows\CloudContent`, wartość `DisableWindowsConsumerFeatures` (semantyka odwrócona: 1 = wyłączone).
- „Manage connections...", https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services
  - „Create a REG_DWORD registry setting named DisableWindowsConsumerFeatures in HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\CloudContent with a value of 1". Strona „Applies to": Windows 11 Enterprise, Windows 10 Enterprise 1607+, Server 2016/2019.
- Odpowiedź na pytanie o Home/Pro: **MS dokumentuje tę politykę jako NIE-Pro (❌ Pro)**; Home w ogóle nie jest wymieniony. Komentarze pod starym blogiem MS (Niehaus, archiwum Learn) są ze sobą sprzeczne – to nie dokumentacja.
  Czy zapis klucza działa na Pro/Home w 26H2 = **hipoteza, do VM** (sprawdzać skutek: aplikacje promowane po świeżym profilu, sugestie w Start).

**Mechanizm:** `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent\DisableWindowsConsumerFeatures` DWORD 1 (rejestr podaje MS).
**Detekcja:** rejestr, `Applied` = 1. Uczciwy opis: „ustawia politykę, którą MS dokumentuje dla Enterprise/Education".
**Undo:** snapshot z journala; `original` = `<RemoveEntry>` (domyślnie wartości brak – do potwierdzenia na VM).
**Ryzyka:** na Home/Pro wartość może być ignorowana (MS: ❌ Pro) -> pozycja „zapisana, ale bez efektu" łamie zasadę „sukces = realny stan". Efekt dotyczy głównie nowych profili (post-OOBE).
**Do VM:** Home i Pro 26H2: zapis, nowy użytkownik, czy sugestie/aplikacje promowane znikają; brak efektu na Pro/Home -> pozycja tylko dla Enterprise/Education albo wcale.

#### 1b. Windows Spotlight (wszystkie / Ustawienia / Tips / sugestie zewnętrzne)
**Źródło:** ta sama strona Policy CSP Experience; GPO w Windows Components > Cloud Content; klucz ADMX `Software\Policies\Microsoft\Windows\CloudContent` (hive HKCU dla „User Configuration", HKLM dla „Computer Configuration").

| Polityka (CSP) | Zakres | Edycje wg MS | Wartość rej. (ADMX) |
|---|---|---|---|
| `./User/.../Experience/AllowWindowsSpotlight` | User | ❌ Pro, ✅ Ent/Edu | `DisableWindowsSpotlightFeatures` |
| `./User/.../Experience/AllowSpotlightCollection` (Win11 21H2+) | User | ❌ Pro, ✅ Ent/Edu | `DisableSpotlightCollectionOnDesktop` |
| `./User/.../Experience/AllowWindowsSpotlightOnSettings` (1803+) | User | ❌ Pro, ✅ Ent/Edu | `DisableWindowsSpotlightOnSettings` |
| `./User/.../Experience/AllowThirdPartySuggestionsInWindowsSpotlight` (1607+) | User | ✅ Pro, ✅ Ent/Edu | `DisableThirdPartySuggestions` |
| `./Device/.../Experience/AllowWindowsTips` (1607+) | Device | ❌ Pro, ✅ Ent/Edu | `DisableSoftLanding` |
| `./Device/.../Experience/DisableCloudOptimizedContent` (Win11 21H2+) | Device | ❌ Pro, ✅ Ent/Edu | `DisableCloudOptimizedContent` |

- Cytaty: AllowWindowsSpotlight – „Specifies whether to turn off all Windows spotlight features at once." oraz „Windows spotlight on lock screen, Windows Tips, Microsoft consumer features and other related features will be turned off."
  AllowThirdPartySuggestionsInWindowsSpotlight – „Specifies whether to allow app and content suggestions from third-party software publishers in Windows spotlight features like lock screen spotlight, suggested apps in the Start menu, and Windows tips. Users may still see suggestions for Microsoft features, apps, and services."
  AllowWindowsSpotlightOnSettings – „This policy allows IT admins to turn off Suggestions in Settings app." (UI: Settings -> Privacy -> General -> Show me suggested content in Settings app.)
- Poza ThirdParty wszystkie mają zależność DependsOn `AllowWindowsSpotlight` = 1. Wartości MS: 0 = Not allowed (najbardziej restrykcyjne), 1 = Allowed (domyślne); w ADMX semantyka odwrócona (`Disable*` = 1).
- **Pro: ❌ prawie wszędzie; ✅ Pro tylko `AllowThirdPartySuggestionsInWindowsSpotlight` i (1d) `AllowTailoredExperiencesWithDiagnosticData`.** Home – brak w tabeli.
- Jedyna droga użytkownika Home/Pro to przełączniki w Ustawieniach (Personalizacja/Prywatność), ale ich klucze (ContentDeliveryManager `SubscribedContent-*`, `SystemPaneSuggestionsEnabled`, `SilentInstalledAppsEnabled` itp.) **nie występują w żadnym przejrzanym źródle MS** -> hipoteza (źródło nie-MS). Nie wpisywać do katalogu bez VM i decyzji autora.
**Detekcja/Undo:** rejestr + snapshot. Wartości w HKCU: tweak z zakresem User, API C# na `HKU\{UserSid}` (zasada repo: bez `HKCU:` w skryptach).
**Ryzyka:** polityki Cloud Content na Pro/Home mogą być ignorowane; w Ustawieniach może pojawić się „zarządzane przez organizację".
**Do VM:** Home/Pro: czy po zapisie `DisableWindowsSpotlightFeatures`/`DisableThirdPartySuggestions` Personalizacja pokazuje komunikat o organizacji i czy Spotlight na ekranie blokady znika.

#### 1c. Rekomendacje w Start (Recommended section)
**Źródło:** Policy CSP Start, https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-start
- `HideRecommendedSection`: `./User/Vendor/MSFT/Policy/Config/Start/HideRecommendedSection` **i** `./Device/Vendor/MSFT/Policy/Config/Start/HideRecommendedSection`, Windows 11 22H2+ (10.0.22621). Edycje wg tabeli: **✅ Pro, ✅ Enterprise, ✅ Education, ✅ IoT Enterprise** (Home brak).
  „This policy allows you to prevent the Start Menu from displaying a list of recommended applications and files. If you enable this policy setting, the Start Menu will no longer show the section containing a list of recommended files and apps."
  Wartości: 0 = shown (domyślnie), 1 = hidden. GPO (Computer **i** User Configuration): Administrative Templates > Start Menu and Taskbar > „Remove Recommended section from Start Menu" (StartMenu.admx). ADMX: `Software\Policies\Microsoft\Windows\Explorer`, wartość `HideRecommendedSection`.
- `HideRecommendedPersonalizedSites` (Win11 22H2 + KB5027303, 10.0.22621.1928): `Start/HideRecommendedPersonalizedSites`, Device i User, ✅ Pro. „Remove Personalized Website Recommendations from the Recommended section in the Start Menu." Wartość `HideRecommendedPersonalizedSites` w tym samym kluczu `Explorer`.
- Zastrzeżenia typu „efekt tylko na SKU Education/Enterprise" znam z forów, nie z tabeli MS -> **hipoteza (źródło nie-MS), do VM**; z tabeli CSP wynika ✅ Pro.
**Mechanizm:** `HKLM\Software\Policies\Microsoft\Windows\Explorer\HideRecommendedSection` = 1 (Device) lub wariant User `HKU\{SID}` (MS podaje oba zakresy).
**Detekcja:** rejestr; skutek wizualny (sekcja znika) zrzutem Start na VM.
**Undo:** snapshot; `original` = brak wartości.
**Ryzyka:** do efektu może być potrzebny restart `explorer.exe`/wylogowanie (hipoteza); Home – nieznane.
**Do VM:** Home i Pro 26H2: efekt po wylogowaniu; ewentualny wymóg Education.

#### 1d. Identyfikator reklamowy, „Tailored experiences"
**Źródła**
- `DisableAdvertisingId`: Policy CSP Privacy, https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy
  `./Device/Vendor/MSFT/Policy/Config/Privacy/DisableAdvertisingId`, **Device only**, Windows 10 1607+. Edycje wg tabeli: **✅ Pro, ✅ Enterprise, ✅ Education, ✅ IoT Enterprise**; Home brak.
  Wartości: 0 Disabled, 1 Enabled, 65535 not configured (domyślnie). „This policy setting turns off the advertising ID, preventing apps from using the ID for experiences across apps."
  GPO: Computer Configuration > System > User Profiles > „Turn off the advertising ID" (UserProfiles.admx); ADMX: `Software\Policies\Microsoft\Windows\AdvertisingInfo`, wartość `DisabledByGroupPolicy`.
- „Manage connections..." (18.1 General): „Create a REG_DWORD registry setting named Enabled in HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo with a value of 0 (zero)" lub „DisabledByGroupPolicy in HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo with a value of 1 (one)". Oraz: „When you turn this feature off in the UI, it turns off the advertising ID, not just resets it." (Strona dotyczy Enterprise; Pro/Home nie wymienione.) Rozszerzone o resztę strony Privacy CSP (bajty 200000+) nie czytałem.
  Ścieżki UI dla advertising ID nie znalazłem na przejrzanych stronach MS (support.microsoft.com „Diagnostics, feedback..." jej nie zawiera) -> hipoteza: Ustawienia > Prywatność i zabezpieczenia > Ogólne.
- `AllowTailoredExperiencesWithDiagnosticData` (Experience CSP): `./User/Vendor/MSFT/Policy/Config/Experience/AllowTailoredExperiencesWithDiagnosticData`, **User**, Windows 10 1703+, **✅ Pro**, Ent/Edu ✅. „This policy allows you to prevent Windows from using diagnostic data to provide customized experiences to the user." Wartości: 0 Not allowed, 1 Allowed (domyślne). GPO: User Configuration > Windows Components > Cloud Content > „Do not use diagnostic data for tailored experiences"; `HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent\DisableTailoredExperiencesWithDiagnosticData` = 1 (potwierdza też Manage connections). DependsOn `AllowWindowsSpotlight`=1 (na Pro, gdzie Spotlight jest ❌, zależność jest niejasna -> do VM).
  UI: Settings > Privacy & security > Diagnostics & feedback > „Tailored experiences" (support.microsoft.com/windows/28808a2b-a31b-dd73-dcd3-4559a5199319): „we will use your Windows diagnostic data to offer you personalized tips, ads, and recommendations".
**Detekcja:** rejestr (HKLM dla ID reklamowego, HKU\{SID} dla Tailored). **Undo:** snapshot.
**Ryzyka:** wariant `Enabled`=0 w HKLM nie jest opisany jako zastępujący ustawienie per-user (per-user `HKCU\...\AdvertisingInfo\Enabled` -> **hipoteza (źródło nie-MS)**); rekomendowany wariant polityki (`DisabledByGroupPolicy`). Efekt: aplikacje UWP dostają inny/pusty ID – niskie ryzyko.
**Do VM:** Home i Pro: czy przełącznik w Ustawieniach staje się szary po `DisabledByGroupPolicy`=1; czy `Enabled`=0 odzwierciedla UI.

### 2. Telemetria przez polityki

#### 2a. Allow Diagnostic Data (AllowTelemetry)
**Źródła**
- Policy CSP System, https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system
  - `./Device/Vendor/MSFT/Policy/Config/System/AllowTelemetry` oraz `./User/.../System/AllowTelemetry`, Windows 10 1507+, **✅ Pro**, Ent/Edu ✅ (Home brak).
  - Wartości na stronie: **0** Security – „This value is only applicable to Windows 10 Enterprise, Windows 10 Education... Using this setting on other devices is equivalent to setting the value of 1."; **1 (domyślna)** Basic/Required; **3** Full/Optional. (Wartość 2 Enhanced: strona diagnostic data – niedostępna na Windows 11.)
  - „Diagnostic data off (not recommended). Using this value, no diagnostic data is sent from the device. This value is only supported on Enterprise, Education, and Server editions." oraz „Send required diagnostic data... Using this value disables the 'Optional diagnostic data' control in the Settings app." oraz „If you disable or don't configure this policy setting, the device will send required diagnostic data and the end user can choose whether to send optional diagnostic data from the Settings app."
  - GPO: Computer Configuration > Administrative Templates > Windows Components > Data Collection and Preview Builds > „Allow Diagnostic Data" (DataCollection.admx); klucz `Software\Policies\Microsoft\Windows\DataCollection` (w tabeli ADMX brak wiersza „Registry Value Name" dla tej polityki; nazwę `AllowTelemetry` podaje Manage connections).
- „Configure Windows diagnostic data in your organization", https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization (2025-09-11): „This is only available on Windows Server, Windows Enterprise, and Windows Education editions." Wartości: 0 off, 1 Required, 2 Enhanced, 3 Optional. „If your organization relies on Windows Update, the minimum recommended setting is Required diagnostic data. Because no Windows Update information is collected when diagnostic data is off, important information about update failures isn't sent." „When both the Computer Configuration policy and User Configuration policies are set, the more restrictive policy is used."
- Manage connections 18.16: „Create a REG_DWORD registry setting in HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\DataCollection\AllowTelemetry with a value of 0". „The Security option is only available in Windows 10 and Windows 11 Enterprise edition."
**Wniosek:** na Home/Pro **najniższy efektywny poziom = 1 (Required)** – domyślny; 0 działa tam jak 1. Realna operacja dla Home/Pro: ustaw 1 jawnie (blokuje podniesienie do Optional w Ustawieniach). Nie obiecywać „wyłączenia telemetrii".
**Mechanizm:** `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry` DWORD 1 (klucz i typ: MS).
**Detekcja:** rejestr; `Applied` = 1 (na Ent/Edu opcjonalnie 0; edycja z `EditionID` jak w repo).
**Undo:** snapshot; `original` = brak wartości (domyślnie Required, użytkownik wybiera Optional w UI).
**Ryzyka:** `caution`: polityka szarzeje „Optional diagnostic data" w Ustawieniach; MS ostrzega, że przy off Windows Update traci dane o błędach.
**Do VM:** Home i Pro 26H2: zapis, czy Ustawienia > Prywatność > Diagnostyka pokazują „zarządzane"; czy `AllowTelemetry`=0 na Pro zachowuje się jak 1.

#### 2b. Configure diagnostic data opt-in settings user interface / change notifications
- `ConfigureTelemetryOptInSettingsUx` – `./Device/Vendor/MSFT/Policy/Config/System/ConfigureTelemetryOptInSettingsUx`, Device, Win 10 1803+, ✅ Pro. „This policy setting determines whether an end user can change diagnostic data settings in the Settings app." Wartości 0 (domyślna) włączone, 1 = wyłączone. GPO „Configure diagnostic data opt-in settings user interface"; klucz `Software\Policies\Microsoft\Windows\DataCollection` (nazwa wartości w tabeli MS niepodana -> hipoteza, do VM).
- `ConfigureTelemetryOptInChangeNotification` – 0 = powiadomienia włączone (domyślnie), 1 = wyłączone; ✅ Pro, Win10 1803+. „...diagnostic data opt-in change notifications won't appear." Nazwa wartości rej. niepodana w tabeli MS -> hipoteza.
- Ryzyka: `OptInSettingsUx` = 1 blokuje użytkownikowi zmianę w Ustawieniach (negatywne dla klienta). Decyzja -> pytania.

#### 2c. Limit Diagnostic Log / Dump collection, nazwa urządzenia, Diagnostic Data Viewer
- `AllowDeviceNameInDiagnosticData`: Device, 1809+, ✅ Pro; 0 (domyślna) = nie wysyła, 1 = wysyła. „If you disable or don't configure this policy setting, then device name won't be sent to Microsoft as part of Windows diagnostic data." -> **domyślnie już wyłączone; tweak zbędny**.
- `LimitDiagnosticLogCollection`: Device, Windows 11 21H2+, ✅ Pro. „By enabling this policy setting, diagnostic logs won't be collected." (1 = włączone). `LimitDumpCollection` (Win11): „If this policy is enabled, Windows Error Reporting will send only kernel mini dumps and user mode triage dumps." Dotyczą tylko ścieżki Optional diagnostic data. Nazwy wartości w kluczu `DataCollection` -> hipoteza, do VM.
- `DisableDiagnosticDataViewer`: Device, 1809+, ✅ Pro – blokuje podgląd danych (szkodliwe dla przejrzystości, nie rekomenduję).
**Do VM (2a–2c):** jak wyżej plus odczyt `Get-ItemProperty` i zrzut Ustawień.

#### 2d. Feedback frequency
- `DoNotShowFeedbackNotifications`: `./Device/Vendor/MSFT/Policy/Config/Experience/DoNotShowFeedbackNotifications`, Device, Win 10 1607+, **✅ Pro**. „This policy setting allows an organization to prevent its devices from showing feedback questions from Microsoft." „If you enable this policy setting, users will no longer see feedback notifications through the Windows Feedback app." Wartości 0 (domyślna) / 1. GPO: Computer Configuration > Windows Components > Data Collection and Preview Builds > „Do not show feedback notifications" (FeedbackNotifications.admx), `HKLM\Software\Policies\Microsoft\Windows\DataCollection\DoNotShowFeedbackNotifications` = 1 (potwierdza Manage connections).
- Ustawienie użytkownika (Settings > Privacy & security > Diagnostics & feedback > „Feedback frequency"): Manage connections podaje `HKCU\Software\Microsoft\Siuf\Rules\PeriodInNanoSeconds` i `NumberOfSIUFInPeriod` (REG_DWORD): Never = 0 / 0; Once a day = 864000000000 / 1; Once a week = 6048000000000 / 1; Always = 100000000 / usunąć; Automatically = usunąć obie. (Strona dotyczy Enterprise; Pro/Home do VM.)
**Detekcja/Undo:** rejestr; snapshot (Siuf: wartości mogą nie istnieć -> `<RemoveEntry>`). **Ryzyka:** niskie.

#### 2e. Usługa DiagTrack (Connected User Experiences and Telemetry)
**MS (w przejrzanych stronach) nie dokumentuje wyłączania usługi jako metody konfiguracji.** Strona diagnostic data nazywa komponent „Connected User Experiences and Telemetry" i jako sposoby zarządzania podaje wyłącznie polityki (`System/AllowTelemetry`, `LimitDumpCollection`, `LimitDiagnosticLogCollection`) i GPO. W „Manage connections" (przeczytałem ok. 100 tys. z 120 tys. znaków) nie ma opisu stop/disable usługi `DiagTrack`.
wyszukiwarka zwróciła tylko strony nie-MS oraz wątek Microsoft Q&A (to nie dokumentacja produktowa), który odradza wyłączanie i proponuje wyłączenie zadania „Compatibility Appraiser".
-> **Adnotacja: brak dokumentacji MS** dla wyłączania DiagTrack; `Set-Service DiagTrack -StartupType Disabled` = hipoteza (źródło nie-MS). Rekomendacja: **nie dodawać** pozycji „wyłącz DiagTrack"; zostać przy polityce AllowTelemetry (komponent jest wykorzystywany przez Windows Update – patrz cytat w 2a). Decyzja -> pytania.

### 3. Klasyczne menu kontekstowe Windows 11 („Show more options")
**Czy MS dokumentuje przełącznik/politykę?** W przejrzanych źródłach MS nie znalazłem żadnej polityki, wartości rejestru ani ustawienia przywracającego klasyczne menu jako domyślne. Sprawdzone: Policy CSP Start (pokrewne tylko `DisableContextMenus` – blokuje menu **w Start**, nie w Eksploratorze), support File Explorer context menu, blog deweloperski, strona o rozszerzeniach.
**Znany mechanizm – klucz CLSID `{86ca1aa0-...}` z podkluczem `InprocServer32` w `HKCU\Software\Classes\CLSID` (pusta wartość domyślna) – jest NIEudokumentowany przez MS.** Pochodzi z forów/blogów nie-MS; pełnego GUID-a nie weryfikowałem w żadnym źródle MS, więc w pliku podaję tylko prefiks z zadania. -> hipoteza (źródło nie-MS / do potwierdzenia na VM); do katalogu dopiero po decyzji autora (zasada repo: źródło MS albo test na VM).
**Co MS mówi o menu**
- Blog Windows Developer (2021-07-19), https://blogs.windows.com/windowsdeveloper/2021/07/19/extending-the-context-menu-and-share-dialog-in-windows-11/ : „'Show more options' loads the Windows 10 context menu as-is" (dla rzadkich czasowników powłoki i aplikacji w trakcie portowania); „Shift-F10 or the keyboard menu key will also load the Windows 10 context menu."; „No commands have been removed entirely."; „Apps extend the menu with IExplorerCommand + app identity."; „Unpackaged Win32 apps can use Sparse Manifests." Blog nie opisuje trwałego przywrócenia klasycznego menu.
- MS Learn, https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer (2026-07-16): „Windows 11 apps extend the modern File Explorer context menu by implementing IExplorerCommand and registering the command with app identity." oraz „Use Show more options only to inspect legacy context-menu extensions." (rejestracja: `windows.comServer` + `windows.fileExplorerContextMenus` w manifeście pakietu).
- Support MS, https://support.microsoft.com/en-us/windows/finding-items-in-the-file-explorer-context-menu-2c458eb5-d27a-4b69-9301-60df221caaa0 : „Not all extensions from Windows 10 will be available in the streamlined Windows 11 context menu," ale „they'll always be available when selecting Show more options". Strona nie wspomina Shift+F10 ani Shift+klik (Shift+klik znam tylko z Q&A -> hipoteza).
**Mechanizm (hipoteza):** zapis w `HKU\{UserSid}\Software\Classes\CLSID\{...}\InprocServer32`, zakres User; efekt po restarcie `explorer.exe`.
**Detekcja:** istnienie klucza `...\InprocServer32` (z pustą wartością domyślną) w HKU\{SID}.
**Undo:** usunięcie klucza utworzonego przez narzędzie (snapshot: wcześniej nie istniał).
**Ryzyka:** `caution`; poza dokumentacją MS (może przestać działać w buildzie 26300+); zmienia zachowanie Eksploratora użytkownika końcowego.
**Do VM:** Home i Pro 26H2: zapis + restart Eksploratora -> czy menu klasyczne; czy przeżywa aktualizację; zrzut.

### 4. „End Task" na pasku zadań
**Źródła**
- MS Learn „Advanced Windows Settings" (2026-01-22), https://learn.microsoft.com/en-us/windows/advanced-settings/ : „The Advanced settings page in Windows (formerly known as For Developers)... System > Advanced". Tabela: **Taskbar – End Task – „Enable end task in taskbar by right click"**. „In Windows 11, version 25H2 and later, the former For developers settings are now surfaced under the Advanced page." „Devices managed by an organization may apply policies that disable Advanced settings toggles." (link do Group Policy dotyczy Dev Drive, nie End Task). Strona linkuje do repo https://github.com/microsoft/windowsAdvancedSettings – README bez wzmianki o rejestrze/politykach (katalog `src` nie sprawdzony).
- Blog Windows Developer (2023-09-26), https://blogs.windows.com/windowsdeveloper/2023/09/26/new-experiences-designed-to-make-every-developer-more-productive-on-windows-11/ : „You can quickly and easily shut down applications with a simple right-click on the app directly from the taskbar" ... „This setting can be enabled within the Developer settings page to then have 'End task' appear". Bez numeru buildu, rejestru i polityki.
**Klucz rejestru/polityka:** **brak dokumentacji MS** (sprawdzone: Policy CSP Experience i Start, strona Advanced Settings, blog). Wyszukiwanie wskazało tylko strony nie-MS (ElevenForum, cloudinfra.net, prajwaldesai.com): `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings`, DWORD `TaskbarEndTask` = 1/0 -> **hipoteza (źródło nie-MS / do potwierdzenia na VM)**. O ADMX/GPO dla tej funkcji nic nie znalazłem.
**Mechanizm (hipoteza):** `HKU\{UserSid}`, DWORD `TaskbarEndTask`; per-user; czy potrzebny restart Eksploratora – źródła nie-MS się różnią -> VM.
**Detekcja:** odczyt wartości, porównanie z przełącznikiem w Ustawieniach > System > Zaawansowane (25H2+; wcześniej Dla deweloperów).
**Undo:** snapshot; `original` = brak lub 0.
**Ryzyka:** `safe` (funkcja wbudowana); ścieżka UI zależy od buildu.
**Do VM:** Home i Pro 26H2: czy po zapisie przełącznik w Ustawieniach pokazuje stan i czy w menu PPM ikony pojawia się „End task"; z restartem Eksploratora i bez.

### Tabela: kandydat -> mechanizm -> Home? -> ryzyko -> pewność źródła
| Kandydat | Mechanizm | Home? | Ryzyko | Pewność |
|---|---|---|---|---|
| Wyłącz Microsoft consumer experiences | HKLM Policies\...\CloudContent\DisableWindowsConsumerFeatures=1 | MS: ❌ Pro, Home brak -> nieznane | caution (może nie działać) | klucz: MS; efekt Home/Pro: hipoteza |
| Wyłącz Spotlight (wszystkie funkcje) | HKU\{SID} ...\CloudContent\DisableWindowsSpotlightFeatures=1 | MS: ❌ Pro | caution | klucz: MS (ADMX); efekt: hipoteza |
| Wyłącz sugestie 3rd-party w Spotlight | HKU\{SID} ...\CloudContent\DisableThirdPartySuggestions=1 | MS: ✅ Pro, Home brak | safe | MS |
| Wyłącz sugestie w Ustawieniach | HKU\{SID} ...\CloudContent\DisableWindowsSpotlightOnSettings=1 | MS: ❌ Pro | safe | klucz: MS; efekt: hipoteza |
| Wyłącz Windows Tips | HKLM ...\CloudContent\DisableSoftLanding=1 | MS: ❌ Pro | safe | klucz: MS; efekt: hipoteza |
| Ukryj sekcję Recommended w Start | Policies\Microsoft\Windows\Explorer\HideRecommendedSection=1 (HKLM lub HKU) | MS: ✅ Pro, Home brak | safe | MS (Home: hipoteza) |
| Wyłącz personalizowane witryny w Recommended | ...\Explorer\HideRecommendedPersonalizedSites=1 | ✅ Pro (KB5027303+) | safe | MS |
| Wyłącz ID reklamowy | HKLM Policies\Microsoft\Windows\AdvertisingInfo\DisabledByGroupPolicy=1 | MS: ✅ Pro, Home brak | safe | MS |
| Wyłącz „Tailored experiences" | HKU\{SID} ...\CloudContent\DisableTailoredExperiencesWithDiagnosticData=1 | MS: ✅ Pro, Home brak | safe | MS |
| Wymuś Required diagnostic data | HKLM Policies\...\DataCollection\AllowTelemetry=1 | MS: ✅ Pro, Home brak; 0 = 1 poza Ent/Edu | caution (blokuje Optional w UI) | MS |
| Ogranicz logi/zrzuty diagnostyczne | DataCollection (LimitDiagnosticLogCollection, LimitDumpCollection) =1 | ✅ Pro (Win11) | safe | polityka: MS; nazwy wartości: hipoteza |
| Wyłącz powiadomienia o feedbacku | HKLM DataCollection\DoNotShowFeedbackNotifications=1 | ✅ Pro | safe | MS |
| Zablokuj UI opt-in diagnostyki | DataCollection (nazwa wartości niepodana przez MS) | ✅ Pro | caution (blokuje użytkownika) | polityka: MS; wartość: hipoteza |
| Wyłącz usługę DiagTrack | Set-Service / sc config | n/d | caution | **brak dokumentacji MS** – nie dodawać |
| Klasyczne menu PPM | HKU\{SID} Software\Classes\CLSID\{86ca1aa0-...}\InprocServer32 (puste) | n/d | caution | **NIEudokumentowane przez MS** – hipoteza |
| End Task na pasku zadań | HKU\{SID} ...\Explorer\Advanced\TaskbarDeveloperSettings\TaskbarEndTask=1 | n/d | safe | funkcja: MS (Learn+blog); **klucz: hipoteza (nie-MS)** |

### Pytania do autora (max 5)
1. Pozycje, gdzie MS pisze „❌ Pro" (consumer features, Spotlight, Windows Tips, sugestie w Ustawieniach): wpisywać do katalogu na podstawie klucza z ADMX z wymogiem potwierdzenia efektu na VM Home/Pro, czy pomijać do czasu dowodu (zasada „sukces = realny stan")?
2. Klucze bez dokumentacji MS (klasyczne menu PPM, TaskbarEndTask, ContentDeliveryManager): dopuszczasz je z adnotacją „źródło: test na VM 26H2, brak dokumentacji MS" (zasady repo dopuszczają „test na VM 26H2")?
3. AllowTelemetry: akceptujesz uczciwy opis „ustaw Required (1)" bez obietnicy „wyłączenia telemetrii" na Home/Pro, a na Enterprise/Education oferować też 0?
4. DiagTrack i polityki blokujące użytkownika (`ConfigureTelemetryOptInSettingsUx`, `DisableDiagnosticDataViewer`): dodawać, czy zostają poza katalogiem (brak dokumentacji MS dla DiagTrack, ryzyko dla Windows Update)?
5. Zakres per-user vs maszyna: pozycje User (Spotlight, Tailored, End Task, klasyczne menu) działają tylko dla SID zalogowanego użytkownika (`$UserSid`) – czy wystarczy to, czy chcesz też nowe profile (Default User hive – osobna decyzja projektowa)?

## Wyniki VM 26H2 (partia 1)

Eksploracja 2026-10-21, skrypt `tests/vm/explore/Invoke-E4A1Exploration.ps1` (commit e73ad8f i poprawki narzędzia d4a9ce5, ef7aa67, f5420a1).
VM `DSO-26H2-Home` (EditionID `Core`) i `DSO-26H2-Pro` (`Professional`), build 26300.9457, pl-PL, region 191 (Polska), Windows
**nieaktywowany** (LicenseStatus=5), sieć przez `Default Switch`. Każda grupa od checkpointu `czysta-26H2`, na końcu revert i odłączenie karty.
Zmiany mechanizmem przez .NET `RegistryKey` (jak API C# aplikacji) jako Administrator przez PowerShell Direct (K1), obserwacja w sesji `dso`
(K2: UI Automation Ustawień po `AutomationId`, zrzuty, klawisz Win/Win+W/Win+C, prawy klik). Wyniki surowe i zrzuty (poza gitem):
zapis pomiaru w archiwum autora i `shots-<grupa>/`.

**Jak czytać tabelę.** „Eksplorator” = skutek obserwowany po restarcie powłoki `dso` (`Stop-Process`, AutoRestartShell) i zamknięciu
`SearchHost`/`StartMenuExperienceHost`; wariant bez restartu sprawdzono tylko tam, gdzie tak napisano. Wszystkie skutki są **takie same na Home
i Pro**, więc żadna pozycja nie potrzebuje `editions`. Cofnięcie (wartość usunięta, klucze utworzone przez zmianę usunięte) przywracało
obserwowany stan we wszystkich pozycjach, które wchodzą. Wyjątkiem było tło pulpitu po `DisableWindowsSpotlightFeatures`, dlatego ta pozycja wypada (niżej).

### Tabela wyników

| Kandydat | Mechanizm (odczyt z VM) | Skutek Home | Skutek Pro | Restart | Decyzja |
|---|---|---|---|---|---|
| Copilot, polityka | `HKU\<SID>\Software\Policies\Microsoft\Windows\WindowsCopilot` `TurnOffWindowsCopilot` DWORD 1 | brak: aplikacja Copilot (preinstalowana i przypięta na Home) dalej otwiera się przez Win+C, przycisk zostaje | brak (na świeżym Pro nie ma aplikacji Copilot) | Eksplorator | **wypada**: brak skutku; aplikacja Copilot to partia 2 |
| Recall | `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` `AllowRecallEnablement` DWORD 0 | brak: funkcja `Recall` = `DisabledWithPayloadRemoved` przed zmianą i po restarcie VM, brak strony w Ustawieniach | to samo | restart VM | **wypada**: nie dowiedziono (VM bez NPU, funkcja niedostępna) |
| Recall, migawki | `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` i `HKU\<SID>\Software\Policies\Microsoft\Windows\WindowsAI` `DisableAIDataAnalysis` DWORD 1 | brak | brak | – | **wypada**: nie dowiedziono (VM bez NPU, funkcja niedostępna) |
| Click to Do | `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` i `HKU\<SID>\Software\Policies\Microsoft\Windows\WindowsAI` `DisableClickToDo` DWORD 1 | brak punktu wejścia, procesu i strony w Ustawieniach | to samo | Eksplorator | **wypada**: nie dowiedziono (VM bez NPU, funkcja niedostępna) |
| Widżety, polityka | `HKLM\SOFTWARE\Policies\Microsoft\Dsh` `AllowNewsAndInterests` DWORD 0 | od razu: Win+W nie otwiera tablicy, przełącznik „Widżety” w Ustawieniach wyszarzony i wyłączony; przycisk na pasku znika po restarcie Eksploratora | to samo | Eksplorator (przycisk) | **wchodzi** |
| Widżety, `TaskbarDa` | przełącznik Ustawień zapisuje `HKU\<SID>\...\Explorer\Advanced` `TaskbarDa` DWORD 0/1 (domyślnie brak wartości); przełącznik chowa przycisk od razu | zapis z naszego procesu: `UnauthorizedAccessException` (także z osobnego exe o nazwie `DSOptimizer.exe`; sterownik UCPD działa) | to samo | – | **wypada**: system blokuje zapis |
| Wyniki z sieci, przełącznik | przełącznik „Zezwalaj aplikacjom wyszukiwania na wyświetlanie wyników” = `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\SearchSettings` `IsGlobalWebSearchProviderToggleEnabled` DWORD 0 (domyślnie brak = włączone) | po zapisie przez API: znika sekcja „Microsoft Bing sugestie internetowe” i karta „Microsoft Bing” w wynikach (fraza „pogoda”) | to samo | Eksplorator/SearchHost | **wchodzi** |
| Wyniki z sieci, aplikacja Bing | `...\SearchSettings\WebSearchProviders` `Microsoft.BingSearch_8wekyb3d8bbwe!App` DWORD 0 (domyślnie 1) | wyniki Bing znikają | to samo | Eksplorator/SearchHost | działa, węższa alternatywa (tylko dostawca Bing) |
| `DisableSearchBoxSuggestions` | `HKU\<SID>\Software\Policies\Microsoft\Windows\Explorer` DWORD 1 | wyniki Bing znikają | to samo | Eksplorator | działa, alternatywa (MS: wyłącza też historię pola wyszukiwania Eksploratora) |
| `BingSearchEnabled` | `HKU\<SID>\...\CurrentVersion\Search` DWORD 0 (domyślnie brak) | wyniki Bing znikają | to samo | Eksplorator | działa, alternatywa bez dokumentacji MS |
| `ConnectedSearchUseWeb` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search` DWORD 0 | brak: Bing dalej w wynikach | brak | Eksplorator | **wypada** |
| `DisableWebSearch` | j.w., DWORD 1 | brak | brak | Eksplorator | **wypada** |
| Wyróżnienia wyszukiwania | j.w. `EnableDynamicContentInWSB` DWORD 0 | nie do zaobserwowania: strona główna wyszukiwania (Win+S) nie ma wyróżnień już przed zmianą, w Ustawieniach brak przełącznika | to samo | Eksplorator | **wypada**: brak skutku do pokazania |
| Rekomendacje i oferty w Ustawieniach | przełącznik (Prywatność > Rekomendacje i oferty) = `HKU\<SID>\...\ContentDeliveryManager` `SubscribedContent-338393Enabled`, `SubscribedContent-353694Enabled`, `SubscribedContent-353696Enabled` DWORD 0 (domyślnie brak = włączone) | po zapisie trzech wartości przez API przełącznik pokazuje „wyłączone”; zapis 1 = „włączone” | to samo | – | **wchodzi** |
| Porady i sugestie | pole „Uzyskaj porady i sugestie podczas korzystania z systemu Windows” (System > Powiadomienia > Ustawienia dodatkowe) = `...\ContentDeliveryManager` `SubscribedContent-338389Enabled` DWORD 0 (domyślnie 1) | pole odznaczone po zapisie przez API | to samo | – | **wchodzi** |
| „Pokaż środowisko powitalne”, „Zaproponuj sposoby…” | – | w 26H2 w „Ustawieniach dodatkowych” jest tylko pole porad | to samo | – | **wypada**: brak tych ustawień w 26H2 |
| Powiadomienia konta w Ustawieniach | przełącznik „Pokaż powiadomienia w ustawieniach” = `HKU\<SID>\...\SystemSettings\AccountNotifications` `EnableAccountNotifications` DWORD 0 (domyślnie brak = włączone) | przełącznik „wyłączone” po zapisie | to samo | – | **wchodzi** |
| Zalecenia w Start (porady, nowe aplikacje) | przełącznik = `HKU\<SID>\...\Explorer\Advanced` `Start_IrisRecommendations` DWORD 0 (domyślnie brak = włączone) | przełącznik „wyłączone” po zapisie; w Start nie było treści tego typu, więc zmiany w menu nie widać | to samo | Eksplorator | **wchodzi** (dowód: stan w Ustawieniach) |
| Sekcja „Proponowane” w Start | `HKU\<SID>\Software\Policies\Microsoft\Windows\Explorer` `HideRecommendedSection` DWORD 1 (HKLM daje to samo) | sekcja znika z Start po restarcie Eksploratora i po restarcie VM; po cofnięciu wraca | to samo | Eksplorator | **wchodzi** |
| Personalizowane witryny w Polecanych | `HKLM\...\Policies\Microsoft\Windows\Explorer` `HideRecommendedPersonalizedSites` DWORD 1 | nie do zaobserwowania (konto lokalne, brak witryn w sekcji) | to samo | Eksplorator | **wypada** (zawiera się w pozycji wyżej) |
| Identyfikator reklamowy | przełącznik = `HKU\<SID>\...\CurrentVersion\AdvertisingInfo` `Enabled` DWORD 0 oraz `HKU\<SID>\...\CurrentVersion\CPSS\Store\AdvertisingInfo` `Value` DWORD 0; wyłączenie usuwa `AdvertisingInfo\Id` | domyślnie już wyłączony (0/0, bez `Id`); zapis 1/1 przez API włącza przełącznik, zapis 0/0 wyłącza | to samo | – | **wchodzi** |
| ID reklamowy, polityka | `HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo` `DisabledByGroupPolicy` DWORD 1 | baner „Niektórymi z tych ustawień zarządza Twoja organizacja”, przełącznik wyszarzony | to samo | – | działa, alternatywa blokująca przełącznik |
| Spersonalizowane oferty (Tailored experiences) | przełącznik = `HKU\<SID>\...\CurrentVersion\Privacy` `PersonalizedOffersEnabled` DWORD 0 (domyślnie brak = wyłączone; włączenie zapisuje 1) | przełącznik zgodny z wartością zapisaną przez API | to samo | – | **wchodzi** |
| Tailored, polityka | `HKU\<SID>\Software\Policies\Microsoft\Windows\CloudContent` `DisableTailoredExperiencesWithDiagnosticData` DWORD 1 | baner „zarządza organizacja”, przełącznik „Spersonalizowane oferty” wyszarzony | to samo | – | działa, alternatywa blokująca przełącznik |
| Consumer experiences | `HKLM\...\Policies\Microsoft\Windows\CloudContent` `DisableWindowsConsumerFeatures` DWORD 1 | brak dla zalogowanego konta (przypięte WhatsApp, LinkedIn, Solitaire zostają, Ustawienia bez zmian), także po restarcie VM | to samo | restart VM | **wypada**: skutek najwyżej dla nowych profili (poza zakresem) |
| Spotlight, wszystko | `HKU\<SID>\Software\Policies\Microsoft\Windows\CloudContent` `DisableWindowsSpotlightFeatures` DWORD 1 | tło pulpitu przełącza się z „W centrum uwagi Windows” na „Obraz” (znika ikona „Dowiedz się więcej o tym zdjęciu”); **po cofnięciu tło zostaje „Obraz”**; ekran blokady bez zmiany w Ustawieniach | to samo | – | **wypada**: cofnięcie nie przywraca tła Spotlight, czyli łamie obietnicę undo; do decyzji autora: ewentualnie niesugerowana z ostrzeżeniem |
| Spotlight w Ustawieniach | `...\CloudContent` `DisableWindowsSpotlightOnSettings` DWORD 1 (HKU) | brak, także po restarcie VM | brak | restart VM | **wypada** |
| Sugestie zewnętrzne Spotlight | `...\CloudContent` `DisableThirdPartySuggestions` DWORD 1 (HKU) | brak | brak | – | **wypada** |
| Porady Windows (polityka) | `HKLM\...\CloudContent` `DisableSoftLanding` DWORD 1 | brak: pole porad dalej aktywne i zaznaczone | brak | – | **wypada** (zostaje pole porad wyżej) |
| Opcjonalne dane diagnostyczne, przełącznik | przełącznik = `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection` `AllowTelemetry` i `MaxTelemetryAllowed` (1 = tylko wymagane, 3 = opcjonalne); domyślnie 1/1 | zapis 3/3 przez API = przełącznik „włączone”, 1/1 = „wyłączone” | to samo | – | mechanizm użytkownika; do katalogu polityka niżej |
| Wymagane dane diagnostyczne (polityka) | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` `AllowTelemetry` DWORD 1 | baner „Niektórymi z tych ustawień zarządza Twoja organizacja” i „Twoja organizacja zezwala na wysyłanie tylko wymaganych danych diagnostycznych”, przełącznik opcjonalnych danych wyszarzony | to samo | – | **wchodzi** |
| `LimitDiagnosticLogCollection` | `...\DataCollection` DWORD 1 | brak w odczycie „po”; jeden niejednoznaczny odczyt na Home po cofnięciu (wyszarzony przełącznik), na Pro nic | brak | – | **wypada**: brak powtarzalnego skutku |
| `LimitDumpCollection` | `...\DataCollection` DWORD 1 | brak | brak | – | **wypada** |
| Powiadomienia o opiniach | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` `DoNotShowFeedbackNotifications` DWORD 1 | baner „zarządza organizacja”, lista „Częstotliwość opinii” znika ze strony Diagnostyka i opinie | to samo | – | **wchodzi** |
| Usługa DiagTrack | `DiagTrack` typ startu `Disabled` + zatrzymanie (domyślnie `Automatic`, `Running`) | usługa zatrzymana i nie startuje po restarcie VM; wyszukiwanie Windows Update (WUA `Search`) po restarcie: ResultCode=2 (powodzenie), 4 aktualizacje, bez błędu; cofnięcie: `Automatic` + `Running` | to samo | skutek od razu, po restarcie VM trwały | **wchodzi** |
| Klasyczne menu PPM | `HKU\<SID>\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` wartość domyślna REG_SZ pusta | prawy klik na pulpicie otwiera okno klasy `#32768` (menu Win32) zamiast `Microsoft.UI.Content.PopupWindowSiteBridge`; po cofnięciu wraca menu Windows 11 | to samo | Eksplorator | **wchodzi** (`ux`) |
| „Zakończ zadanie” na pasku | przełącznik (System > Zaawansowane) = `HKU\<SID>\...\Explorer\Advanced\TaskbarDeveloperSettings` `TaskbarEndTask` DWORD 1 (domyślnie brak = wyłączone) | menu przycisku na pasku ma pozycję „Zakończ zadanie”; po cofnięciu znika | to samo | Eksplorator | **wchodzi** (`ux`) |
| `ux.game-dvr` | `HKU\<SID>\System\GameConfigStore` `GameDVR_Enabled` DWORD 0 | nie rozstrzygnięte: Granie > Przechwytywanie ma na VM wszystkie opcje wyszarzone (brak sprzętowego kodera), nic się nie zmienia | to samo | – | **wypada** (bez zmian względem C1) |

### Uwagi do decyzji

- **Wyniki z sieci w Start.** Działają cztery mechanizmy, nie działają `ConnectedSearchUseWeb` i `DisableWebSearch`. Proponowana pozycja
  używa przełącznika z Ustawień (`IsGlobalWebSearchProviderToggleEnabled` = 0): to stan, który użytkownik widzi w Prywatność > Wyszukaj
  („Zezwalaj aplikacjom wyszukiwania na wyświetlanie wyników”), wyłącza wszystkich dostawców wyników z sieci, nie blokuje przełącznika
  i nie rusza pola wyszukiwania Eksploratora. Skutek obserwowano po restarcie Eksploratora i SearchHost; bez restartu nie sprawdzano,
  więc pozycja ma ostrzeżenie o restarcie Eksploratora (sama go nie wykonuje).
- **Polityki kontra przełączniki użytkownika** (ID reklamowy, Tailored). Obie drogi działają na Home i Pro. Propozycja: pozycja zapisuje
  wartości przełącznika (nie odbiera właścicielowi wyboru, zgodnie ze spec 5.2), polityki zostają opisane jako alternatywa. Na świeżej
  instalacji z odpowiedzią „nie” w OOBE oba przełączniki są już wyłączone, więc detekcja zwróci `Applied` od razu.
- **Spotlight (`DisableWindowsSpotlightFeatures`)** ma realny skutek (koniec Spotlight na pulpicie), ale cofnięcie polityki nie przywraca
  tła „W centrum uwagi Windows”: użytkownik musi wybrać je ponownie w Personalizacja > Tło. Decyzja autora: nie wchodzi do partii 1,
  bo łamie obietnicę undo. Do decyzji autora: ewentualnie pozycja niesugerowana z ostrzeżeniem „po cofnięciu tło trzeba wybrać ponownie”.
  Ekranu blokady nie dało się sprawdzić, bo nieaktywowany Windows blokuje personalizację.
- **TaskbarDa** jest chroniony: zapis odrzuca system dla każdego naszego procesu (sprawdzone z `powershell.exe` i z osobnego exe).
  Widżety wyłącza polityka `AllowNewsAndInterests`; blokuje ona też przełącznik w Ustawieniach i nie da się tego uniknąć.
- **Menu Start po restarcie VM.** Na Home po pierwszym restarcie z siecią włączyła się nowa wersja menu Start (sekcja „Ostatnie” zamiast
  „Proponowane”, w Ustawieniach `SystemSettings_Start_IrisRecommendations_CheckBox` zamiast `_ToggleSwitch`, nowe opcje
  `Start_LayoutOptions_*`); w drugim przebiegu z restartem już nie. Nie wynikało to z testowanych zmian (to samo przed i po cofnięciu).
  Testy UI nie mogą zakładać jednego `AutomationId` dla tych pozycji; wartości rejestru się nie zmieniają.
- **Restart.** Żadna wchodząca pozycja nie wymaga restartu komputera (`requiresReboot=false`). Restartu Eksploratora wymagają:
  przycisk Widżetów (tablica jest zablokowana od razu), wyniki z sieci w Start, sekcja Proponowane, zalecenia w Start, menu PPM, „Zakończ zadanie”.
  Decyzja autora: żadna pozycja nie restartuje Eksploratora sama. Apply go nie wykonuje, pozycja ma ostrzeżenie, a do restartu
  służy istniejąca akcja `tools.restart-explorer`.
- **Rodzaj dowodu**: pozycje potwierdzone tylko stanem przełącznika w Ustawieniach zostają w katalogu, ale ich docs i specyfikacja
  niżej mówią wprost „stan przełącznika w Ustawieniach, treści nie obserwowano”.

### Domyślne wartości na świeżym 26H2 (`original`), Home = Pro

| Wartość | Stan po instalacji |
|---|---|
| wszystkie wartości `Software\Policies\...` z tabeli (HKLM i HKU) | brak wartości; brak kluczy z wyjątkiem `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection`, który istnieje na świeżej instalacji (po cofnięciu `AllowTelemetry`/`DoNotShowFeedbackNotifications` klucz zostaje) |
| `SearchSettings\IsGlobalWebSearchProviderToggleEnabled` | brak (przełącznik włączony) |
| `SearchSettings\WebSearchProviders\Microsoft.BingSearch_8wekyb3d8bbwe!App` | 1 |
| `ContentDeliveryManager\SubscribedContent-338393Enabled`, `-353694Enabled`, `-353696Enabled` | brak (przełącznik włączony) |
| `ContentDeliveryManager\SubscribedContent-338389Enabled` | 1 |
| `SystemSettings\AccountNotifications\EnableAccountNotifications` | brak (włączone) |
| `Explorer\Advanced\Start_IrisRecommendations` | brak (włączone) |
| `AdvertisingInfo\Enabled`, `CPSS\Store\AdvertisingInfo\Value` | 0 i 0, bez `AdvertisingInfo\Id` (wyłączone po OOBE z unattend) |
| `Privacy\PersonalizedOffersEnabled` | brak (wyłączone) |
| `HKLM\...\CurrentVersion\Policies\DataCollection\AllowTelemetry`, `MaxTelemetryAllowed` | 1 i 1 (opcjonalne dane wyłączone) |
| `Explorer\Advanced\TaskbarDeveloperSettings\TaskbarEndTask` | brak (wyłączone) |
| `Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` | brak klucza |
| usługa `DiagTrack` | `Automatic`, `Running` |
| `Explorer\Advanced\TaskbarDa` | brak (przycisk widoczny) |
| aplikacja `Microsoft.Copilot` | Home: zainstalowana i przypięta; Pro: brak |
| funkcja `Recall` | `DisabledWithPayloadRemoved` |

Przełącznik w Ustawieniach przełączony „tam i z powrotem” zostawia wartość jawną (np. `TaskbarEndTask`=0) zamiast braku wartości.
Cofnięcie w katalogu odtwarza snapshot, więc po cofnięciu stan = stan z instalacji.

### Proponowany podział na pozycje i `suggested`

| Id | Zmiana | Kategoria | `suggested` |
|---|---|---|---|
| `privacy.widgets` | `AllowNewsAndInterests`=0 (HKLM `Policies\Microsoft\Dsh`) | privacy | tak |
| `privacy.search-web-results` | `IsGlobalWebSearchProviderToggleEnabled`=0 (HKU `SearchSettings`) | privacy | tak |
| `privacy.settings-suggestions` | `SubscribedContent-338393/353694/353696Enabled`=0 (HKU `ContentDeliveryManager`) | privacy | tak |
| `privacy.windows-tips` | `SubscribedContent-338389Enabled`=0 | privacy | tak |
| `privacy.settings-account-notifications` | `EnableAccountNotifications`=0 (HKU `SystemSettings\AccountNotifications`) | privacy | tak |
| `privacy.start-recommendations` | `Start_IrisRecommendations`=0 (HKU `Explorer\Advanced`) | privacy | tak |
| `privacy.start-recommended-section` | `HideRecommendedSection`=1 (HKU `Policies\Microsoft\Windows\Explorer`) | privacy | nie (ukrywa też ostatnio otwierane pliki, z których użytkownicy korzystają) |
| `privacy.advertising-id` | `AdvertisingInfo\Enabled`=0 i `CPSS\Store\AdvertisingInfo\Value`=0 (HKU) | privacy | tak |
| `privacy.tailored-experiences` | `Privacy\PersonalizedOffersEnabled`=0 (HKU) | privacy | tak |
| `privacy.diagnostic-data-required` | `AllowTelemetry`=1 (HKLM `Policies\Microsoft\Windows\DataCollection`) | privacy | tak |
| `privacy.feedback-notifications` | `DoNotShowFeedbackNotifications`=1 (HKLM `...\DataCollection`) | privacy | tak |
| `privacy.diagtrack` | usługa `DiagTrack`: `Disabled` i zatrzymanie | privacy | tak, ryzyko `caution` (Windows Update działa bez niej, sprawdzone) |
| `ux.classic-context-menu` | klucz `Software\Classes\CLSID\{86ca1aa0-…}\InprocServer32` z pustą wartością domyślną (HKU) | ux | nie |
| `ux.taskbar-end-task` | `TaskbarEndTask`=1 (HKU `Explorer\Advanced\TaskbarDeveloperSettings`) | ux | nie |

Wypadają (werdykt do `docs/tweaks/<id>.md` i mapy drogowej autora): Copilot (`TurnOffWindowsCopilot`), Recall (`AllowRecallEnablement`,
`DisableAIDataAnalysis`), Click to Do, `TaskbarDa`, `ConnectedSearchUseWeb`, `DisableWebSearch`, `EnableDynamicContentInWSB`,
`DisableWindowsConsumerFeatures`, `DisableWindowsSpotlightFeatures` (cofnięcie nie przywraca tła; autor może poprosić o pozycję
niesugerowaną z ostrzeżeniem), `DisableWindowsSpotlightOnSettings`, `DisableThirdPartySuggestions`, `DisableSoftLanding`,
środowisko powitalne i „Zaproponuj sposoby…” (brak w 26H2), `HideRecommendedPersonalizedSites`, `LimitDiagnosticLogCollection`,
`LimitDumpCollection`, `ux.game-dvr`. Działające alternatywy bez własnej pozycji: `DisableSearchBoxSuggestions`, `BingSearchEnabled`,
`WebSearchProviders\Microsoft.BingSearch_8wekyb3d8bbwe!App`, `DisabledByGroupPolicy`, `DisableTailoredExperiencesWithDiagnosticData`,
`HideRecommendedSection` w HKLM.

### Pozycje do katalogu (specyfikacja dla Tasków 5–8)

Jeden wiersz = jedna pozycja (12 `privacy` + 2 `ux`). Ścieżka „na VM” to klucz odczytany w eksploracji (`<SID>` = SID konta `dso`);
ścieżka „w JSON” to zapis w katalogu (`HKCU\…` silnik mapuje na `HKU\<SID zalogowanego>\…`). „Klucze nieistniejące przed” to kandydaci
do `KeyAbsentAfterUndo` w `tests/vm/cases.psd1`; „brak” znaczy, że klucz istniał już na świeżej instalacji. Restart: żadna pozycja nie
restartuje Eksploratora sama; „Eksplorator” = pozycja dostaje ostrzeżenie i odsyła do akcji `tools.restart-explorer`.
Wartości, stany domyślne i wyniki cofnięcia są takie same na Home i Pro.

#### Task 5 (Widżety, wyniki z sieci)

| Id | Ścieżka na VM | Ścieżka w JSON | Nazwa / typ / dane | Domyślnie (`original`) na VM | Klucze nieistniejące przed | Cofnięcie (obserwacja) | Zakres | Restart | Rodzaj dowodu | `suggested` | `risk` |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `privacy.widgets` | `HKLM\SOFTWARE\Policies\Microsoft\Dsh` | `HKLM\SOFTWARE\Policies\Microsoft\Dsh` | `AllowNewsAndInterests` / DWord / 0 | brak wartości i brak klucza `Dsh` | `HKLM\SOFTWARE\Policies\Microsoft\Dsh` | wartość i klucz usunięte; Win+W znowu otwiera tablicę, przełącznik „Widżety” aktywny, przycisk wraca po restarcie Eksploratora | maszyna | Eksplorator: tylko przycisk na pasku; tablica jest zablokowana od razu | skutek widoczny | tak | safe |
| `privacy.search-web-results` | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\SearchSettings` | `HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings` | `IsGlobalWebSearchProviderToggleEnabled` / DWord / 0 | brak wartości (przełącznik włączony); klucz istnieje | brak | wartość odtworzona do stanu sprzed zapisu; sekcja „Microsoft Bing sugestie internetowe” wraca w wynikach | użytkownik | Eksplorator (z SearchHost) | skutek widoczny | tak | safe |

#### Task 6 (sugestie, Start, reklamy)

| Id | Ścieżka na VM | Ścieżka w JSON | Nazwa / typ / dane | Domyślnie (`original`) na VM | Klucze nieistniejące przed | Cofnięcie (obserwacja) | Zakres | Restart | Rodzaj dowodu | `suggested` | `risk` |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `privacy.settings-suggestions` | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SubscribedContent-338393Enabled` / DWord / 0; `SubscribedContent-353694Enabled` / DWord / 0; `SubscribedContent-353696Enabled` / DWord / 0 | brak wszystkich trzech wartości (przełącznik „Rekomendacje i oferty w ustawieniach” włączony); klucz istnieje | brak | przełącznik znowu „włączony” | użytkownik | brak | stan przełącznika w Ustawieniach, treści nie obserwowano | tak | safe |
| `privacy.windows-tips` | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SubscribedContent-338389Enabled` / DWord / 0 | 1 (pole „Uzyskaj porady i sugestie…” zaznaczone) | brak | pole znowu zaznaczone | użytkownik | brak | stan przełącznika w Ustawieniach, treści nie obserwowano | tak | safe |
| `privacy.settings-account-notifications` | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications` | `HKCU\Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications` | `EnableAccountNotifications` / DWord / 0 | brak wartości (przełącznik „Pokaż powiadomienia w ustawieniach” włączony); klucz istnieje | brak | przełącznik znowu „włączony” | użytkownik | brak | stan przełącznika w Ustawieniach, treści nie obserwowano | tak | safe |
| `privacy.start-recommendations` | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `Start_IrisRecommendations` / DWord / 0 | brak wartości (przełącznik włączony); klucz istnieje | brak | przełącznik znowu „włączony” | użytkownik | Eksplorator (Ustawienia pokazują nowy stan od razu; menu Start mogło nie mieć takich treści) | stan przełącznika w Ustawieniach, treści nie obserwowano | tak | safe |
| `privacy.start-recommended-section` | `HKU\<SID>\Software\Policies\Microsoft\Windows\Explorer` | `HKCU\Software\Policies\Microsoft\Windows\Explorer` | `HideRecommendedSection` / DWord / 1 | brak wartości i brak klucza `Explorer` (klucz `HKU\<SID>\Software\Policies\Microsoft\Windows` istnieje) | `HKU\<SID>\Software\Policies\Microsoft\Windows\Explorer` (w JSON: `HKCU\Software\Policies\Microsoft\Windows\Explorer`) | wartość i klucz usunięte; sekcja „Proponowane” wraca (po restarcie Eksploratora i po restarcie VM) | użytkownik | Eksplorator | skutek widoczny | nie | safe |
| `privacy.advertising-id` | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo` oraz `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo` | `HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo` oraz `HKCU\Software\Microsoft\Windows\CurrentVersion\CPSS\Store\AdvertisingInfo` | `Enabled` / DWord / 0 (pierwszy klucz); `Value` / DWord / 0 (drugi klucz) | `Enabled`=0, `Value`=0, brak wartości `Id` (wyłączony po OOBE z unattend); oba klucze istnieją | brak | po zapisie 1/1 i cofnięciu przełącznik „wyłączony” | użytkownik | brak | stan przełącznika w Ustawieniach, treści nie obserwowano | tak | safe |
| `privacy.tailored-experiences` | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\Privacy` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy` | `PersonalizedOffersEnabled` / DWord / 0 | brak wartości (przełącznik „Spersonalizowane oferty” wyłączony); klucz istnieje | brak | przełącznik wraca do stanu sprzed | użytkownik | brak | stan przełącznika w Ustawieniach, treści nie obserwowano | tak | safe |

Uwaga do `privacy.advertising-id`: Ustawienia przy wyłączaniu usuwają też wartość `Id` (REG_SZ) z
`HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo`. Zapis mechanizmem w teście zawierał to usunięcie, więc nie
sprawdzono, czy same wartości 0/0 bez usunięcia `Id` dają ten sam stan przełącznika.

#### Task 7 (diagnostyka, opinie, DiagTrack)

| Id | Ścieżka na VM | Ścieżka w JSON | Nazwa / typ / dane | Domyślnie (`original`) na VM | Klucze nieistniejące przed | Cofnięcie (obserwacja) | Zakres | Restart | Rodzaj dowodu | `suggested` | `risk` |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `privacy.diagnostic-data-required` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` | `AllowTelemetry` / DWord / 1 | brak wartości; klucz istnieje na świeżej instalacji | brak (klucz zostaje po cofnięciu: kandydat do `KeyPresentAfterUndo`) | wartość usunięta; baner „zarządza organizacja” znika, przełącznik opcjonalnych danych znowu aktywny | maszyna | brak | skutek widoczny (baner, przełącznik opcjonalnych danych zablokowany) | tak | caution |
| `privacy.feedback-notifications` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` | `DoNotShowFeedbackNotifications` / DWord / 1 | brak wartości; klucz istnieje na świeżej instalacji | brak (jak wyżej, `KeyPresentAfterUndo`) | wartość usunięta; lista „Częstotliwość opinii” wraca, baner znika | maszyna | brak | stan w Ustawieniach (baner, lista znika), samych powiadomień nie obserwowano | tak | safe |
| `privacy.diagtrack` | usługa `DiagTrack` (Connected User Experiences and Telemetry) | usługa `DiagTrack` | startType `Disabled`, stop = true | startType `Automatic`, stan `Running` | – | startType `Automatic` i stan `Running` przywrócone (także po restarcie VM) | maszyna | brak (skutek od razu, trwały po restarcie VM) | skutek widoczny: usługa zatrzymana i nie startuje po restarcie VM; Windows Update Agent `Search("IsInstalled=0 and Type='Software'")` po restarcie z wyłączoną usługą: ResultCode=2 (powodzenie), 4 aktualizacje, bez błędu, na Home i Pro | tak | caution |

#### Task 8 (`ux`)

| Id | Ścieżka na VM | Ścieżka w JSON | Nazwa / typ / dane | Domyślnie (`original`) na VM | Klucze nieistniejące przed | Cofnięcie (obserwacja) | Zakres | Restart | Rodzaj dowodu | `suggested` | `risk` |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `ux.classic-context-menu` | `HKU\<SID>\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` | `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` | wartość domyślna (pusta nazwa) / String / pusty ciąg | brak obu kluczy | `HKU\<SID>\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` oraz `HKU\<SID>\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` | oba klucze usunięte; po restarcie Eksploratora wraca menu Windows 11 | użytkownik | Eksplorator | skutek widoczny (okno `#32768` zamiast `Microsoft.UI.Content.PopupWindowSiteBridge`) | nie | caution |
| `ux.taskbar-end-task` | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings` | `TaskbarEndTask` / DWord / 1 | brak wartości i brak klucza `TaskbarDeveloperSettings` (klucz tworzy dopiero przełącznik Ustawień) | `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings` (w teście klucz założył wcześniej przełącznik Ustawień, więc usunięcia klucza przez cofnięcie nie obserwowano) | po cofnięciu i restarcie Eksploratora pozycja „Zakończ zadanie” znika z menu przycisku na pasku | użytkownik | Eksplorator | skutek widoczny | nie | safe |

Wszystkie obserwacje zrobiono na **nieaktywowanym Windows** (LicenseStatus=5, Home i Pro). Personalizacja ekranu blokady i tła była
zablokowana; pozostałe strony Ustawień działały normalnie. Dla pozycji z przełącznikiem Ustawień cofnięcie w eksploracji odtwarzało
stan sprzed zapisu mechanizmem, a nie stan z instalacji: wcześniejszy test przełącznika zostawiał wartość jawną. Brak wartości po
cofnięciu z czystego stanu potwierdzą przypadki VM Tasków 5–8.

## Wyniki VM 26H2 (partia 2)

Eksploracja 2026-10-21, skrypt `tests/vm/explore/Invoke-E4A2Exploration.ps1` (commit 32e29b9). VM `DSO-26H2-Home` (`Core`)
i `DSO-26H2-Pro` (`Professional`), build 26300.9457, pl-PL, **bez sieci**, każda od checkpointu `czysta-26H2`, na końcu revert.
Trzy przebiegi (pierwszy na samym Home, drugi i trzeci na obu VM). Liczby niżej pochodzą z przebiegu 3. Przebieg 1 miał mniej
wariantów usuwania. W przebiegu 2 wylogowanie na Home się nie wykonało, bo na Home nie ma `logoff.exe`, a błąd WinRT zapisał się tylko jako
`AggregateException`. Tam, gdzie warianty się pokrywały, przebiegi 1 i 2 dały te same wyniki. Wyniki surowe i zrzuty (poza gitem):
zapis pomiaru w archiwum autora, `shots/`, `host-start-*.png` (przebiegi 1–2 w `run1-*`, `run2-*`).

Konteksty są takie same jak w partii 1. **K1** to PowerShell Direct jako wbudowany Administrator, czyli proces podniesiony na **innym**
koncie niż zalogowane. **K2** to zadanie Harmonogramu jako `dso` z najwyższymi uprawnieniami, czyli typowy przypadek, w którym właściciel
laptopa podnosi DSOptimizer przez UAC. Czasy dotyczą samego cmdletu w już działającym `powershell.exe`, bez startu procesu
(start `powershell.exe -File` nie był mierzony).

### Odczyt AppX dla zalogowanego konta

| Pomiar | Home | Pro |
|---|---|---|
| `Get-AppxPackage -User <SID dso>` w K1: liczba pakietów | 104 | 103 |
| ten sam zbiór `PackageFullName` co `Get-AppxPackage` (bez `-User`) w K2 | tak | tak |
| czas K1: pierwszy odczyt / drugi | 174 ms / 8 ms | 184 ms / 8 ms |
| `Get-AppxPackage -User <SID>` w K2 (własny SID) | 9 ms, 104 | 11 ms, 103 |
| `Get-AppxPackage` w K2 (bez `-User`) | 164 ms | 223 ms |
| WinRT `PackageManager.FindPackagesForUser(<SID>)` w K1 | 251 ms, 104 | 259 ms, 103 |
| `Get-AppxPackage -AllUsers` w K1 | 104, 54 ms | 104, 57 ms |
| `-PackageTypeFilter Main,Framework,Resource,Bundle,Optional` | 198 | 193 |
| `Get-AppxProvisionedPackage -Online` | 49, 282 ms | 47, 288 ms |
| manifest + nazwy (odczyt `AppxManifest.xml` i `SHLoadIndirectString` dla wszystkich pakietów) | 2,1 s | 2,1 s |

Domyślny `Get-AppxPackage` nie zwraca pakietów zasobów ani pakietów bundle. Flaga `IsResourcePackage` była `False` we wszystkich
104/103 wierszach. Reguła ukrycia zasobów (spec 6.1) zostaje jako zabezpieczenie i w tym odczycie nie ma czego ukrywać. Różnice między
edycjami: tylko Home ma `Microsoft.Copilot`, `MicrosoftCorporationII.MicrosoftFamily` (oba także w obrazie) i `Microsoft.WindowsAppRuntime.1.5`.
Tylko Pro ma `Microsoft.Windows.AssignedAccessLockApp` i `Microsoft.Windows.SecureAssessmentBrowser`, oba z `SignatureKind=System`.

### Nazwy wyświetlane AppX

- Manifest `Properties/DisplayName` to w 72 ze 104 pakietów (Home) `ms-resource:…`. Proces Administratora rozwija go przez
  `SHLoadIndirectString("@{<PackageFullName>?ms-resource://<Name>/Resources/<klucz>}")` (klucz bez `/`; postać `ms-resource://…`
  podajemy bez zmian). Rozwinięcie nie udało się tylko w 5 pakietach z `SignatureKind=System` (`0x80070003`), które i tak są ukryte.
  Surowe `ms-resource:` nie zostało w żadnym widocznym pakiecie.
- WinRT `Package.DisplayName` z `FindPackagesForUser(<SID>)` w K1 daje to samo co `SHLoadIndirectString`. Wypełnione było w 103 ze 104 pakietów,
  a puste tylko w systemowym GUID-pakiecie.
- **Język zależy od konta procesu, nie od konta `dso`.** W K1 (Administrator) część nazw wyszła po angielsku („Windows Calculator”,
  „MSN Weather”, „Snipping Tool”). Te same pakiety w K2 (`dso`, WinRT) mają nazwy polskie („Kalkulator Windows”, „MSN Pogoda”,
  „Narzędzie Wycinanie”). Start (`Get-StartApps` w K2) pokazuje jeszcze krótsze nazwy aplikacji z `VisualElements/@DisplayName`, np. „Kalkulator”,
  „Pogoda”, „Zdjęcia”. Takiej nazwy nie da się odczytać spoza sesji użytkownika (`Get-StartApps` działa tylko w jego kontekście).
- Wniosek: nazwa wiersza = `Package.DisplayName` (WinRT) albo `SHLoadIndirectString` na `Properties/DisplayName`, w razie pustej
  wartości `Name`. W typowym przypadku (proces = konto zalogowane) nazwy są w języku użytkownika. Gdy to inne konto, mogą być w języku
  tego konta. Klasyfikacja dopasowuje po `Name`, nigdy po nazwie wyświetlanej.

### Usuwanie AppX (mechanizm)

| Wariant | Gdzie | Wynik (Home = Pro) | Czas |
|---|---|---|---|
| WinRT `PackageManager.RemovePackageAsync(<pełna>)` | K1 (konto procesu ≠ `dso`) | błąd `0x80073D19` „the current user does not have that package installed”; nic się nie zmienia | 65 ms |
| `Remove-AppxPackage -Package <pełna> -User <SID dso>` | K1 | **bez błędu**, ale pakiet dalej jest dla `dso` w stanie `Installed(pending removal)` i dalej widać go w Start (`Get-StartApps`). Stan bez zmian przez 60 s i przez 90 s po wylogowaniu `dso` (`WTSLogoffSession`, powłoka zamknięta po 10,5 s) | 1–10 ms |
| `Remove-AppxPackage -Package <pełna> -AllUsers` | K1 | pakiet znika u wszystkich kont **i z obrazu** (`Get-AppxProvisionedPackage` pusty, osobny deprovisioning zwraca „nie ma”) | 215–302 ms |
| `Remove-AppxPackage -Package <pełna>` (bez `-User`) | K2 (`dso`) | znika dla `dso` od razu; zostaje `Staged` dla `S-1-5-18` i w obrazie | 442–463 ms |
| `Remove-AppxPackage -Package <pełna> -User <SID dso>` | K2 (`dso`, własny SID) | jak wyżej: znika dla `dso` od razu | 147–151 ms |
| `Remove-AppxProvisionedPackage -Online -PackageName <PackageName z Get-AppxProvisionedPackage, dopasowanie DisplayName = Name>` | K1 | znika z obrazu; po nim `Get-AppxPackage -AllUsers` nie zwraca pakietu wcale (znika też wpis `Staged`) | 326–433 ms |

Start po usunięciu: wyszukiwanie „Pogoda” i „Microsoft News” w Start konta `dso` przed usunięciem pokazuje aplikację, a po usunięciu
„Nie znaleziono wyników” (zrzuty `shots/start-przed-*.png`, `shots/start-po-*.png`, `host-start-*.png`). `Get-StartApps` w K2 ma po
całej fazie 70 wpisów zamiast 73.

**Wniosek:** usunięcie AppX dla konta da się potwierdzić tylko wtedy, gdy proces DSOptimizera działa na koncie zalogowanym
(SID procesu = `IUserContext.Sid`). Z procesu innego konta `-User <SID>` nic nie usuwa, tylko zostawia „pending removal”. Sukces
sprawdzany niezależnym odczytem byłby wtedy nieprawdziwy. `-AllUsers` działa, ale usuwa pakiet wszystkim kontom, a tego spec nie przewiduje.

### Wpisy Uninstall na świeżej VM (Home = Pro, 31 wpisów)

| Gałąź | Klucz | Wartości istotne dla klasyfikacji | Propozycja |
|---|---|---|---|
| HKLM 64 i WOW6432Node | `AddressBook`, `DirectDrawEx`, `DXM_Runtime`, `Fontcore`, `IE40`, `IE4Data`, `IE5BAKEX`, `IEData`, `MobileOptionPack`, `MPlayer2`, `SchedulingAgent` | klucz bez wartości | ukryty (brak `UninstallString`) |
| HKLM 64 i WOW6432Node | `Connection Manager` | `SystemComponent=1` | ukryty |
| HKLM 64 i WOW6432Node | `WIC` | `NoRemove=1`, brak `UninstallString` | ukryty |
| HKLM 64 | `mstsc-4b0a31aa-df6a-4307-9b47-d5cc50009643` | **brak `DisplayName`**, jest `DisplayName_Localized` = `@C:\Windows\System32\mstsc.exe,-4000` (REG_EXPAND_SZ); `Publisher`=Microsoft Corporation; `UninstallString`=`"C:\Windows\System32\mstsc.exe" /uninstall`; `NoModify=1` | chroniony (Podłączanie pulpitu zdalnego). Odczyt nazwy musi rozwijać `DisplayName_Localized` (`SHLoadIndirectString`), gdy brak `DisplayName` |
| WOW6432Node | `Microsoft Edge` | `DisplayName`=Microsoft Edge, `UninstallString`=`"…\Edge\Application\152.0.4191.66\Installer\setup.exe" --uninstall --msedge --system-level --verbose-logging` | chroniony (spec 6.2) |
| WOW6432Node | `Microsoft Edge Update` | `NoRemove=1`, brak `UninstallString` | ukryty |
| WOW6432Node | `Microsoft EdgeWebView` | `DisplayName`=Microsoft Edge WebView2 Runtime, `NoRemove=1`, brak `UninstallString` | ukryty (na liście chronionej spec też jest WebView2) |
| `HKU\<SID dso>` | `OneDriveSetup.exe` | `DisplayName`=Microsoft OneDrive, `UninstallString`=`"C:\Users\dso\AppData\Local\Microsoft\OneDrive\25.087.0506.0001\OneDriveSetup.exe"  /uninstall ` (podwójna spacja, spacja na końcu), `NoModify=1`, `NoRepair=1` | pozostały (spec 6.2: ręcznie); program per użytkownik |

Na świeżej VM nie ma żadnego wpisu z `QuietUninstallString`, `WindowsInstaller=1`, `ParentKeyName` ani `ReleaseType`. Ścieżki MSI,
cichego deinstalatora i aktualizacji sprawdzą dopiero pakiety testowe z Tasku 7.

### Pakiety AppX – propozycja grupy (dla Tasku 9, decyzję podejmuje autor)

„Obraz” = pakiet jest też w `Get-AppxProvisionedPackage -Online`. „Nazwa” = nazwa w Start konta `dso`, a gdy aplikacji nie ma w Start,
`Package.DisplayName` w kontekście `dso`. Wszystkie widoczne pakiety pochodzą od Microsoftu, więc na VM nie ma czego przypisać do
grup `producent` ani `trial`. Te grupy wymagają sprzętu (mapa drogowa autora).

#### AppX widoczne (bez cech ukrycia)

| Pakiet (`Name`) | Nazwa (konto dso / Start) | Wydawca (manifest) | Sygnatura | Obraz | VM | Propozycja | Uwagi |
|---|---|---|---|---|---|---|---|
| `Clipchamp.Clipchamp` | Microsoft Clipchamp | Microsoft Corp. | Store | tak | Home, Pro | sugerowany | microsoft; spec 6.2 |
| `Microsoft.ApplicationCompatibilityEnhancements` | Ulepszenia zgodności aplikacji systemu Windows | Microsoft Corporation | Store | tak | Home, Pro | chroniony | zgodność aplikacji (systemowe) |
| `Microsoft.AV1VideoExtension` | AV1 Video Extension | Microsoft Corporation | Store | tak | Home, Pro | chroniony | kodek, spec 6.2 |
| `Microsoft.AVCEncoderVideoExtension` | Rozszerzenie wideo kodera AVC | Microsoft Corporation | Store | tak | Home, Pro | chroniony | kodek |
| `Microsoft.BingNews` | Microsoft News - Wiadomości | Microsoft Corporation | Store | tak | Home, Pro | sugerowany | microsoft; spec 6.2; usunięty na VM |
| `Microsoft.BingSearch` | Microsoft Bing | Microsoft Corporation | Store | tak | Home, Pro | pozostały | Microsoft Bing, bez wpisu w Start; nie ma w spec – do decyzji autora |
| `Microsoft.BingWeather` | Pogoda | Microsoft Corporation | Store | tak | Home, Pro | sugerowany | microsoft; spec 6.2; usunięty na VM |
| `Microsoft.Copilot` | Copilot | Microsoft Corporation | Store | tak | Home | sugerowany | microsoft; spec 6.2 (tylko Home) |
| `Microsoft.GamingApp` | Xbox | Microsoft Corporation | Store | tak | Home, Pro | sugerowany | microsoft; Xbox, spec 6.2 |
| `Microsoft.GetHelp` | Uzyskaj pomoc | Microsoft Corporation | Store | tak | Home, Pro | sugerowany | microsoft; „Uzyskaj pomoc” – odpowiednik „Porad” ze spec (Getstarted nie ma na 26H2); do potwierdzenia przez autora |
| `Microsoft.HEIFImageExtension` | Rozszerzenie obrazów HEIF | Microsoft Corporation | Store | tak | Home, Pro | chroniony | kodek, spec 6.2 |
| `Microsoft.HEVCVideoExtension` | Rozszerzenia wideo HEVC od producenta urządzenia | Microsoft Corporation | Store | tak | Home, Pro | chroniony | kodek, spec 6.2 |
| `Microsoft.MicrosoftEdge.Stable` | Microsoft Edge | Microsoft Corporation | Developer | tak | Home, Pro | chroniony | Edge, spec 6.2 |
| `Microsoft.MicrosoftSolitaireCollection` | Solitaire & Casual Games | Microsoft Studios | Store | tak | Home, Pro | sugerowany | microsoft; spec 6.2; usunięty na VM |
| `Microsoft.MicrosoftStickyNotes` | Sticky Notes | Microsoft Corporation | Store | tak | Home, Pro | chroniony | użytkowe (jak Notatnik) – propozycja do listy chronionej |
| `Microsoft.MPEG2VideoExtension` | Rozszerzenie wideo MPEG-2 | Microsoft Corporation | Store | tak | Home, Pro | chroniony | kodek |
| `Microsoft.OutlookForWindows` | Outlook | Microsoft Corporation | Developer | tak | Home, Pro | pozostały | nowy Outlook – spec 6.2: ręcznie |
| `Microsoft.Paint` | Paint | Microsoft Corporation | Store | tak | Home, Pro | chroniony | spec 6.2 |
| `Microsoft.PowerAutomateDesktop` | Power Automate | Microsoft Corporation | Store | tak | Home, Pro | sugerowany | microsoft; spec 6.2 |
| `Microsoft.RawImageExtension` | Rozszerzenie obrazów RAW | Microsoft Corporation | Store | tak | Home, Pro | chroniony | kodek |
| `Microsoft.ScreenSketch` | Narzędzie Wycinanie | Microsoft Corporation | Store | tak | Home, Pro | chroniony | Narzędzie Wycinanie, spec 6.2 |
| `Microsoft.StorePurchaseApp` | Host środowiska sklepu Store | Microsoft Corporation | Store | tak | Home, Pro | chroniony | zakupy w Sklepie (zależność Store) |
| `Microsoft.Todos` | Microsoft To Do | Microsoft Corporation | Store | tak | Home, Pro | sugerowany | microsoft; spec 6.2 |
| `Microsoft.VP9VideoExtensions` | Rozszerzenia wideo VP9 | Microsoft Corporation | Store | tak | Home, Pro | chroniony | kodek |
| `Microsoft.WebMediaExtensions` | Rozszerzenia multimediów sieci Web | Microsoft Corporation | Store | tak | Home, Pro | chroniony | kodek |
| `Microsoft.WebpImageExtension` | Rozszerzenie obrazu WebP | Microsoft Corporation | Store | tak | Home, Pro | chroniony | kodek, spec 6.2 |
| `Microsoft.Windows.DevHome` | Dev Home | Microsoft Corporation | Developer | tak | Home, Pro | pozostały | Dev Home; nie ma w spec – do decyzji |
| `Microsoft.Windows.Photos` | Zdjęcia | Microsoft Corporation | Store | tak | Home, Pro | chroniony | spec 6.2 |
| `Microsoft.WindowsAlarms` | Zegar | Microsoft Corporation | Store | tak | Home, Pro | chroniony | Zegar, spec 6.2 |
| `Microsoft.WindowsCalculator` | Kalkulator | Microsoft Corporation | Store | tak | Home, Pro | chroniony | spec 6.2 |
| `Microsoft.WindowsCamera` | Aparat | Microsoft Corporation | Store | tak | Home, Pro | chroniony | spec 6.2 |
| `Microsoft.WindowsFeedbackHub` | Centrum opinii | Microsoft Corporation | Store | tak | Home, Pro | sugerowany | microsoft; Centrum opinii, spec 6.2 |
| `Microsoft.WindowsNotepad` | Notatnik | Microsoft Corporation | Store | tak | Home, Pro | chroniony | spec 6.2 |
| `Microsoft.WindowsSoundRecorder` | Rejestrator dźwięku | Microsoft Corporation | Store | tak | Home, Pro | chroniony | spec 6.2 |
| `Microsoft.WindowsStore` | Microsoft Store | Microsoft Corporation | Store | tak | Home, Pro | chroniony | spec 6.2 |
| `Microsoft.WindowsTerminal` | Terminal | Microsoft Corporation | Store | tak | Home, Pro | chroniony | spec 6.2 |
| `Microsoft.Xbox.TCUI` | Xbox TCUI | Microsoft Corporation | Store | tak | Home, Pro | pozostały | zależność gier/Game Bar (logowanie Xbox); spec: Xbox bez Game Bar – do decyzji |
| `Microsoft.XboxGamingOverlay` | Game Bar | Microsoft Corporation | Store | tak | Home, Pro | pozostały | Game Bar – do decyzji autora w renderze (spec 6.2) |
| `Microsoft.XboxIdentityProvider` | Xbox Identity Provider | Microsoft Corporation | Store | tak | Home, Pro | pozostały | jak wyżej |
| `Microsoft.XboxSpeechToTextOverlay` | Game Speech Window | Microsoft Corporation | Store | tak | Home, Pro | pozostały | nakładka Game Bar |
| `Microsoft.YourPhone` | Aplikacja Łącze z telefonem | Microsoft Corporation | Store | tak | Home, Pro | pozostały | Łącze z telefonem |
| `Microsoft.ZuneMusic` | Odtwarzacz multimedialny | Microsoft Corporation | Store | tak | Home, Pro | chroniony | Odtwarzacz multimedialny, spec 6.2 |
| `MicrosoftCorporationII.MicrosoftFamily` | Family | Microsoft Corp. | Store | tak | Home | pozostały | Family (tylko Home); nie ma w spec – do decyzji |
| `MicrosoftCorporationII.QuickAssist` | Szybka pomoc | Microsoft Corp. | Developer | tak | Home, Pro | pozostały | Szybka pomoc – spec 6.2: ręcznie |
| `MicrosoftWindows.Client.WebExperience` | Windows Web Experience Pack | Microsoft Windows | Store | tak | Home, Pro | pozostały | Widżety (partia 1: polityka Widżetów) |
| `MicrosoftWindows.CrossDevice` | Urządzenia przenośne | Microsoft Windows | Store | tak | Home, Pro | pozostały | Urządzenia przenośne (zależność Łącza z telefonem) |
| `MSTeams` | Microsoft Teams | Microsoft Corporation | Developer | tak | Home, Pro | sugerowany | microsoft; Teams (osobisty), spec 6.2 |

#### AppX ukryte (spec 6.1)

| Pakiet (`Name`) | Powód ukrycia | VM |
|---|---|---|
| `1527c705-839a-4832-9118-54d4Bd6a0c89` | NonRemovable, SignatureKind=System | Home, Pro |
| `c5e2524a-ea46-4f67-841f-6a9465d9d515` | NonRemovable, SignatureKind=System | Home, Pro |
| `E2A4F912-2574-4A75-9BB0-0D023378592B` | NonRemovable, SignatureKind=System | Home, Pro |
| `F46D4000-FD22-4DB4-AC8E-4E1DDDE828FE` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.AAD.BrokerPlugin` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.AccountsControl` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.AIFabric.CBS.1.6` | IsFramework, NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.AsyncTextService` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.BioEnrollment` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.CredDialogHost` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.DesktopAppInstaller` | NonRemovable | Home, Pro |
| `Microsoft.ECApp` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.LockApp` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.MicrosoftEdgeDevToolsClient` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.NET.Native.Framework.2.2` | IsFramework | Home, Pro |
| `Microsoft.NET.Native.Runtime.2.2` | IsFramework | Home, Pro |
| `Microsoft.SecHealthUI` | NonRemovable | Home, Pro |
| `Microsoft.UI.Xaml.2.8` | IsFramework | Home, Pro |
| `Microsoft.UI.Xaml.CBS` | IsFramework, NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.VCLibs.140.00` | IsFramework | Home, Pro |
| `Microsoft.VCLibs.140.00.UWPDesktop` | IsFramework | Home, Pro |
| `Microsoft.Win32WebViewHost` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.Apprep.ChxApp` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.AssignedAccessLockApp` | NonRemovable, SignatureKind=System | Pro |
| `Microsoft.Windows.AugLoop.CBS` | IsFramework, NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.CapturePicker` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.CloudExperienceHost` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.ContentDeliveryManager` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.NarratorQuickStart` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.OOBENetworkCaptivePortal` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.OOBENetworkConnectionFlow` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.ParentalControls` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.PeopleExperienceHost` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.PinningConfirmationDialog` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.PrintQueueActionCenter` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.SecureAssessmentBrowser` | NonRemovable, SignatureKind=System | Pro |
| `Microsoft.Windows.ShellExperienceHost` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.StartMenuExperienceHost` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.Windows.XGpuEjectDialog` | NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.WindowsAppRuntime.1.5` | IsFramework | Home |
| `Microsoft.WindowsAppRuntime.1.8` | IsFramework | Home, Pro |
| `Microsoft.WindowsAppRuntime.2` | IsFramework | Home, Pro |
| `Microsoft.WindowsAppRuntime.CBS.1.6` | IsFramework, NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.WindowsAppRuntime.CBS.2` | IsFramework, NonRemovable, SignatureKind=System | Home, Pro |
| `Microsoft.XboxGameCallableUI` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.61869720.Voiess` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.61869721.Livtop` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.61869722.Speion` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.61869836.InpApp` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.Client.CBS` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.Client.Core` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.Client.CoreAI` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.Client.FileExp` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.Client.OOBE` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.Client.Photon` | NonRemovable, SignatureKind=System | Home, Pro |
| `MicrosoftWindows.UndockedDevKit` | NonRemovable, SignatureKind=System | Home, Pro |
| `Windows.CBSPreview` | NonRemovable, SignatureKind=System | Home, Pro |
| `windows.immersivecontrolpanel` | NonRemovable, SignatureKind=System | Home, Pro |
| `Windows.PrintDialog` | NonRemovable, SignatureKind=System | Home, Pro |

Uwagi do tabeli:
- `Microsoft.DesktopAppInstaller` (Instalator aplikacji / winget) i `Microsoft.SecHealthUI` (Zabezpieczenia Windows) mają `NonRemovable=True`,
  więc są ukryte i nie trafiają na listę chronioną. Edge, Store i Terminal mają `NonRemovable=False` i dlatego potrzebują wpisu na liście chronionej.
- Teams (`MSTeams`), Edge, Outlook, Dev Home i Szybka pomoc mają `SignatureKind=Developer`. Reguła ukrycia `SignatureKind=System`
  ich nie obejmuje i tak ma zostać.
- Z listy „sugerowane” w spec nie ma na świeżym 26H2: Office Hub (Microsoft 365), Porady (`Microsoft.Getstarted`) i LinkedIn.
  „Wprowadzenie” w Start należy do systemowego `MicrosoftWindows.Client.CBS`, więc jest ukryte. Wpisy dla nich można zostawić
  w pliku klasyfikacji, bo na innych obrazach mogą być.

### Ochrona systemu (C:) – odczyt bez zmiany

| Odczyt | Stan wyjściowy (Home i Pro) | Po `Enable-ComputerRestore -Drive C:\` | Po `Disable-ComputerRestore -Drive C:\` |
|---|---|---|---|
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients`, wartość `{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}` (REG_MULTI_SZ) | klucz jest, wartości brak | `\\?\Volume{<GUID C:>}\:Windows (C%3A)` | brak wartości |
| GUID woluminu C: (`Win32_Volume.DeviceID`, `DriveLetter='C:'`) | `\\?\Volume{ee004905-…}\` (Home), `\\?\Volume{2cf430d3-…}\` (Pro) | – | – |
| `root\default:SystemRestoreConfig.RPSessionInterval` | 0 | 1 | 0 |
| `root\default:SystemRestore` (punkty) | 0 | 0 | 0 |
| `Win32_ShadowStorage`, `vssadmin list shadowstorage` | brak / „No items found” | brak | brak |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore` | `RPSessionInterval=0`, `SRInitDone=1` | – | – |
| polityka `HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore` | brak klucza | – | – |
| usługi `VSS`, `swprv` | Running, Manual | bez zmian | bez zmian |

**Ochrona systemu jest wyłączona na obu świeżych VM.** Stan dla C: rozróżnia niezawodnie tylko wartość `SPP\Clients`, w której jest GUID
woluminu C:. `RPSessionInterval` też się zmienia, ale jest globalny, nie per dysk. Liczba punktów, shadow storage i `vssadmin` pokazują
to samo w obu stanach, bo bez pierwszego punktu nie ma przydziału. Cały odczyt (z `vssadmin`) trwa ok. 0,5 s, sam rejestr z `Win32_Volume` – kilkadziesiąt ms.

### csc.exe i Windows Installer (fundament pakietów testowych Tasku 7)

- `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`: jest na Home i Pro (4.8.9221.0). Kompilacja `/target:exe` programu testowego
  i jego uruchomienie (kod wyjścia 7, stdout) działają.
- `%SystemRoot%\System32\msiexec.exe`: jest.
- COM `WindowsInstaller.Installer`: tworzy się (`New-Object -ComObject`). `Version` = 5.0.26100.9444 daje dopiero
  `InvokeMember(… GetProperty …)`, bo zwykły odczyt właściwości w PowerShell zwraca pusty tekst. `OpenDatabase(<ścieżka>, 3)` jako zwykła metoda
  i `Commit()` tworzą pusty plik `.msi`.

### Wybrane mechanizmy (dla Tasków 4, 5 i 7)

- **Odczyt AppX:** skrypt PowerShell (`-File`) z `Get-AppxPackage -User <IUserContext.Sid>`. Wynik jest poprawny także z procesu innego konta
  (ten sam zbiór co w kontekście `dso`) i trwa ok. 0,2 s. Dla każdego pakietu skrypt zwraca `Name`, `PackageFullName`, `PackageFamilyName`,
  `Publisher`, `IsFramework`, `IsResourcePackage`, `NonRemovable`, `SignatureKind`. Nazwę wyświetlaną daje `SHLoadIndirectString`
  na `Properties/DisplayName` z `AppxManifest.xml` (dla tekstu bez `ms-resource:` – sam tekst; przy błędzie – `Name`). Do tego lista
  `Get-AppxProvisionedPackage -Online` (`DisplayName`, `PackageName`). Wybór skryptu zamiast WinRT w C#: `Get-AppxPackage -User` daje
  `NonRemovable`, którego `Windows.ApplicationModel.Package` nie ma, a czas jest podobny (WinRT 0,25 s).
- **Usuwanie AppX:** tylko gdy SID procesu = `IUserContext.Sid`. Wywołanie `Remove-AppxPackage -Package <PackageFullName> -User <SID>`
  (zmierzone z własnym SID: od razu skuteczne, 0,15 s). Dla grupy `microsoft` potem
  `Remove-AppxProvisionedPackage -Online -PackageName <PackageName>`, gdzie `PackageName` pochodzi z `Get-AppxProvisionedPackage`
  i jest dopasowany po `DisplayName = Name` (0,3–0,4 s). Pakietu może nie być w obrazie, wtedy krok jest pomijany. Sukces = `Get-AppxPackage -User <SID> -Name <Name>`
  pusty (i dla `microsoft` brak w `Get-AppxProvisionedPackage`). Gdy SID procesu ≠ zalogowany: aplikacje ze Sklepu bez pola
  wyboru, z tym samym wyjaśnieniem co programy per użytkownik („Uruchom DSOptimizer na koncie <nazwa>, żeby to usunąć.”).
  Powód: `-User <inny SID>` zostawia „pending removal”, które nie znika ani po minucie, ani po wylogowaniu. `-AllUsers` usuwa wszystkim kontom.
  To propozycja zmiany względem obecnego tekstu globalnych ograniczeń (tam ta reguła dotyczy tylko programów per użytkownik), decyzja należy do autora.
- **Ochrona systemu:** odczyt `SPP\Clients` `{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}` przez API rejestru C# (widok 64-bit) i GUID woluminu
  z `Win32_Volume` (`DriveLetter='C:'`, `DeviceID`). Włączona = któryś wpis zawiera GUID woluminu C:. Brak wartości = wyłączona.
- **Pakiety testowe:** `csc.exe` z `Framework64\v4.0.30319` i COM `WindowsInstaller.Installer` (`OpenDatabase`, `Version` przez `InvokeMember`)
  są dostępne na obu VM bez sieci.
