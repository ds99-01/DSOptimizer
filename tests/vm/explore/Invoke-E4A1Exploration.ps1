<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Wszystkie grupy, obie VM (Home, potem Pro), z siecią (Default Switch):
        .\tests\vm\explore\Invoke-E4A1Exploration.ps1 -OutDirectory C:\ścieżka\poza\repo\exploration
    Jedna VM, wybrane grupy:
        .\tests\vm\explore\Invoke-E4A1Exploration.ps1 -VmName DSO-26H2-Pro -Group ai,search -OutDirectory C:\...\exploration
    Wybrane pozycje (filtr po Id z Get-E4A1Plan), np. ponowienie jednej sondy:
        .\tests\vm\explore\Invoke-E4A1Exploration.ps1 -VmName DSO-26H2-Home -Group ads -Candidate softlanding -OutDirectory C:\...\exploration
    Grupa reboot (zmiana -> restart VM -> obserwacja -> cofnięcie -> restart -> obserwacja), pozycje z dowolnej grupy:
        .\tests\vm\explore\Invoke-E4A1Exploration.ps1 -Group reboot -RebootCandidate diagtrack,recall-policy -OutDirectory C:\...\exploration
    -NoNetwork: bez podłączania karty do 'Default Switch' (domyślnie sieć jest podłączana na czas grupy i odłączana po niej).
    -ShowPlan: wypisuje rozstrzygnięte argumenty i listę pozycji (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    Czas: ok. 5-25 minut na grupę i VM (najdłuższa ads), reboot ok. 6 minut na cykl.
Opis działania:
    Eksploracja partii 1 Etapu 4a (prywatność, AI, reklamy, menu PPM, Zakończ zadanie) według zasady dowodu z spec 2: liczy się
    zaobserwowany skutek (Ustawienia, element UI, proces, wynik w Start), nie sam zapis wartości. Wszystko dzieje się WYŁĄCZNIE w VM
    DSO-26H2-Home/Pro; na hoście skrypt tylko podłącza/odłącza kartę sieciową VM (Default Switch, zgoda Dawida 2026-10-21) i zapisuje wyniki.
    Każda grupa to osobny przebieg od checkpointu 'czysta-26H2': revert -> sieć -> start -> gotowość -> sondy -> kopia zrzutów ->
    ZAWSZE (finally) revert, odłączenie sieci i sprawdzenie stanu końcowego (VM Off, karta bez przełącznika) przed zapisem JSON;
    błąd albo naruszenie trafia do pola cleanup i error wyniku oraz kończy grupę błędem (nic nie jest wyciszane).
    Konteksty w VM:
      K1 - PowerShell Direct jako wbudowany Administrator: rejestr HKLM i HKU\<SID dso> (.NET RegistryKey, widok 64-bit), usługi, procesy,
           restart Eksploratora (Stop-Process powłoki dso, AutoRestartShell), migawki rejestru i ich różnice.
      K2 - sesja interaktywna 'dso': zadanie Harmonogramu (principal dso / Interactive / Highest) uruchamia skrypt z operacjami UI:
           strony Ustawień (Start-Process ms-settings:..., UI Automation: AutomationId, stan przełącznika, IsEnabled, wartość listy,
           rozwinięcie sekcji po nazwie, przewijanie), przełączenie przełącznika (TogglePattern), Start i wyszukiwanie (klawisz Win,
           wpisanie frazy, zrzut; wyniki wyszukiwania są w WebView2 niewidocznym dla UIA, więc dowodem jest zrzut), pasek zadań (UIA),
           Win+W (okno WindowsDashboard), Win+C, menu PPM pulpitu (klasa okna: '#32768' = klasyczne), menu przycisku na pasku
           (szukanie 'Zakończ zadanie'), Windows Update ('Sprawdź aktualizacje'). Zrzut ekranu: Graphics.CopyFromScreen w sesji dso,
           PNG w C:\dso\e4a1\shots, kopiowane na host przed revertem.
    Przebieg pozycji (Invoke-E4A1Candidate):
      1. stan przed: wartości rejestru {Exists, Kind, Value}, migawka rejestru (HKLM\SOFTWARE\Policies, HKU\<SID>\Software\Policies,
         HKU\<SID>\Software\Microsoft\Windows\CurrentVersion, ...), odczyty K1 i obserwacje K2;
      2. przełącznik oficjalny (jeśli jest): zmiana przez UIA -> różnica rejestru (to jest mechanizm) -> powrót -> różnica;
      3. zmiana mechanizmem kandydata (zapis rejestru / usługa; dla przełączników: wartości z różnicy z kroku 2), restart Eksploratora
         wg pozycji (none / restart / both = obserwacja przed i po restarcie);
      4. obserwacja skutku (K1 + K2, zrzuty);
      5. cofnięcie (stan sprzed: wartości usunięte, klucze utworzone przez sondę usunięte, typ startu usługi) i obserwacja.
    Ostatni bezpiecznik po stronie VM: Assert-E4A1InsideVm (nazwa komputera DSO-26H2-HOME/PRO i brak DSO_VM_TESTS_MOCK_ONLY) na początku
    każdej funkcji ze skutkiem. Błąd jednej pozycji trafia do wyniku i nie przerywa pozostałych. Pierwszy błąd uwierzytelnienia przerywa
    cały skrypt (blokada konta). Skrypt dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet), nigdy Bruteforce_LTSC.
    Wynik: <OutDirectory>\<VmName>\e4a1-<grupa>.json (UTF-8) i zrzuty <OutDirectory>\<VmName>\shots-<grupa>\*.png.
    Kod wyjścia: 0 gdy wszystkie wybrane grupy na wszystkich VM zebrane bez błędu przebiegu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [string]$OutDirectory,
    [ValidateSet('ai', 'search', 'ads', 'telemetry', 'ux', 'reboot')][string[]]$Group,
    [ValidatePattern('^[a-z0-9-]+\z')][string[]]$Candidate,
    [ValidatePattern('^[a-z0-9-]+\z')][string[]]$RebootCandidate,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$NoNetwork,
    [switch]$ShowPlan
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Plan (dane): pozycje-kandydaci. Wartości dosłownie z planu Tasku 4 i research 2026-10-20-etap4a-zrodla.md.
# {SID} w ścieżce = SID konta 'dso' (wstawiany w VM). Explorer: none | restart | both.
# Observe: operacje K2 (Settings, Search, SearchHome, Start, Taskbar, Widgets, Copilot, ContextMenu, TaskbarMenu, UpdateCheck).
# K1: odczyty K1 (processes:<nazwy>, feature:<nazwa>, service:<nazwa>, appx:<wzorzec>).
# Toggle: przełącznik oficjalny {Uri, Id albo Match (regex nazwy), Expand (nazwy sekcji), Target On|Off}.
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4A1Plan {
    [CmdletBinding()]
    param()
    $Copilot = 'HKU\{SID}\Software\Policies\Microsoft\Windows\WindowsCopilot'
    $WindowsAi = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI'
    $WindowsAiUser = 'HKU\{SID}\Software\Policies\Microsoft\Windows\WindowsAI'
    $WindowsSearch = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search'
    $CloudContent = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent'
    $CloudContentUser = 'HKU\{SID}\Software\Policies\Microsoft\Windows\CloudContent'
    $ExplorerPolicy = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer'
    $ExplorerPolicyUser = 'HKU\{SID}\Software\Policies\Microsoft\Windows\Explorer'
    $DataCollection = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'
    $SettingsTaskbar = @{ Op = 'Settings'; Uri = 'ms-settings:taskbar' }
    $SettingsGeneral = @{ Op = 'Settings'; Uri = 'ms-settings:privacy-general' }
    $SettingsFeedback = @{ Op = 'Settings'; Uri = 'ms-settings:privacy-feedback' }
    $SettingsSearch = @{ Op = 'Settings'; Uri = 'ms-settings:search-permissions' }
    $SettingsStart = @{ Op = 'Settings'; Uri = 'ms-settings:personalization-start' }
    $SettingsNotifications = @{ Op = 'Settings'; Uri = 'ms-settings:notifications'; Expand = @('Ustawienia dodatkowe') }
    $SettingsLock = @{ Op = 'Settings'; Uri = 'ms-settings:lockscreen' }
    $SettingsBackground = @{ Op = 'Settings'; Uri = 'ms-settings:personalization-background' }
    $SettingsPrivacy = @{ Op = 'Settings'; Uri = 'ms-settings:privacy' }
    $Search = @{ Op = 'Search'; Query = 'pogoda' }
    return @(
        # --- ai ---
        @{ Id = 'copilot-policy'; Group = 'ai'; Explorer = 'restart'; K1 = @('appx:Copilot', 'processes:Copilot')
            Values = @(@{ Path = $Copilot; Name = 'TurnOffWindowsCopilot'; Kind = 'DWord'; Value = 1 })
            Observe = @(@{ Op = 'Taskbar' }, @{ Op = 'Copilot' }, $SettingsTaskbar) }
        @{ Id = 'recall-policy'; Group = 'ai'; Explorer = 'none'; K1 = @('feature:Recall', 'appx:CoreAI')
            Values = @(@{ Path = $WindowsAi; Name = 'AllowRecallEnablement'; Kind = 'DWord'; Value = 0 })
            Observe = @($SettingsPrivacy) }
        @{ Id = 'recall-snapshots'; Group = 'ai'; Explorer = 'none'; K1 = @('feature:Recall')
            Values = @(@{ Path = $WindowsAi; Name = 'DisableAIDataAnalysis'; Kind = 'DWord'; Value = 1 }, @{ Path = $WindowsAiUser; Name = 'DisableAIDataAnalysis'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsPrivacy) }
        @{ Id = 'clicktodo'; Group = 'ai'; Explorer = 'restart'; K1 = @('processes:ClickToDo')
            Values = @(@{ Path = $WindowsAi; Name = 'DisableClickToDo'; Kind = 'DWord'; Value = 1 }, @{ Path = $WindowsAiUser; Name = 'DisableClickToDo'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsPrivacy) }
        @{ Id = 'widgets-policy'; Group = 'ai'; Explorer = 'both'; K1 = @('processes:Widgets,WidgetBoard,WidgetService')
            Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Dsh'; Name = 'AllowNewsAndInterests'; Kind = 'DWord'; Value = 0 })
            Observe = @(@{ Op = 'Taskbar' }, @{ Op = 'Widgets' }, $SettingsTaskbar) }
        @{ Id = 'widgets-taskbarda'; Group = 'ai'; Explorer = 'both'; K1 = @('processes:Widgets,WidgetBoard,WidgetService')
            Toggle = @{ Uri = 'ms-settings:taskbar'; Id = 'SystemSettings_DesktopTaskbar_Da_ToggleSwitch'; Target = 'Off' }
            Observe = @(@{ Op = 'Taskbar' }, @{ Op = 'Widgets' }) }
        # --- search ---
        @{ Id = 'search-baseline'; Group = 'search'; Explorer = 'none'; ObserveOnly = $true
            Observe = @($Search, @{ Op = 'SearchHome' }, $SettingsSearch) }
        @{ Id = 'search-web-toggle'; Group = 'search'; Explorer = 'restart'
            Toggle = @{ Uri = 'ms-settings:search-permissions'; Id = 'SystemSettings_Search_GlobalWebSearchToggle_ToggleSwitch'; Target = 'Off' }
            Observe = @($Search) }
        @{ Id = 'search-bing-app-toggle'; Group = 'search'; Explorer = 'restart'
            Toggle = @{ Uri = 'ms-settings:search-permissions'; Id = 'Microsoft.BingSearch_8wekyb3d8bbwe!App_ToggleSwitch'; Target = 'Off' }
            Observe = @($Search) }
        @{ Id = 'search-connectedsearchuseweb'; Group = 'search'; Explorer = 'restart'
            Values = @(@{ Path = $WindowsSearch; Name = 'ConnectedSearchUseWeb'; Kind = 'DWord'; Value = 0 })
            Observe = @($Search, $SettingsSearch) }
        @{ Id = 'search-disablewebsearch'; Group = 'search'; Explorer = 'restart'
            Values = @(@{ Path = $WindowsSearch; Name = 'DisableWebSearch'; Kind = 'DWord'; Value = 1 })
            Observe = @($Search, $SettingsSearch) }
        @{ Id = 'search-box-suggestions'; Group = 'search'; Explorer = 'restart'
            Values = @(@{ Path = $ExplorerPolicyUser; Name = 'DisableSearchBoxSuggestions'; Kind = 'DWord'; Value = 1 })
            Observe = @($Search, $SettingsSearch) }
        @{ Id = 'search-bingsearchenabled'; Group = 'search'; Explorer = 'restart'
            Values = @(@{ Path = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Search'; Name = 'BingSearchEnabled'; Kind = 'DWord'; Value = 0 })
            Observe = @($Search, $SettingsSearch) }
        @{ Id = 'search-highlights'; Group = 'search'; Explorer = 'restart'
            Values = @(@{ Path = $WindowsSearch; Name = 'EnableDynamicContentInWSB'; Kind = 'DWord'; Value = 0 })
            Observe = @(@{ Op = 'SearchHome' }, $SettingsSearch) }
        # --- ads ---
        @{ Id = 'settings-suggestions-toggle'; Group = 'ads'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:privacy-general'; Id = 'SystemSettings_Privacy_EnableSuggestionsInSettings_ToggleSwitch'; Target = 'Off' }
            Observe = @($SettingsGeneral) }
        @{ Id = 'settings-account-notifications-toggle'; Group = 'ads'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:privacy-general'; Id = 'SystemSettings_Privacy_EnableAccountNotificationsInSettings_ToggleSwitch'; Target = 'Off' }
            Observe = @($SettingsGeneral) }
        @{ Id = 'personalized-offers-toggle'; Group = 'ads'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:privacy-general'; Id = 'SystemSettings_Privacy_PersonalizedOffers_ToggleSwitch'; Target = 'Off' }
            Observe = @($SettingsGeneral, $SettingsFeedback) }
        @{ Id = 'advertising-id-toggle'; Group = 'ads'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:privacy-general'; Id = 'SystemSettings_Privacy_AdvertisingIdEnabled_ToggleSwitch'; Target = 'Off' }
            Observe = @($SettingsGeneral) }
        @{ Id = 'tips-toggle'; Group = 'ads'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:notifications'; Match = 'porad'; Expand = @('Ustawienia dodatkowe'); Target = 'Off' }
            Observe = @($SettingsNotifications) }
        @{ Id = 'welcome-toggle'; Group = 'ads'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:notifications'; Match = 'powitaln'; Expand = @('Ustawienia dodatkowe'); Target = 'Off' }
            Observe = @($SettingsNotifications) }
        @{ Id = 'finish-setup-toggle'; Group = 'ads'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:notifications'; Match = 'wykorzysta'; Expand = @('Ustawienia dodatkowe'); Target = 'Off' }
            Observe = @($SettingsNotifications) }
        @{ Id = 'start-iris-toggle'; Group = 'ads'; Explorer = 'restart'
            Toggle = @{ Uri = 'ms-settings:personalization-start'; Id = 'SystemSettings_Start_IrisRecommendations_ToggleSwitch'; Target = 'Off' }
            Observe = @(@{ Op = 'Start' }, $SettingsStart) }
        @{ Id = 'consumer-features'; Group = 'ads'; Explorer = 'restart'
            Values = @(@{ Path = $CloudContent; Name = 'DisableWindowsConsumerFeatures'; Kind = 'DWord'; Value = 1 })
            Observe = @(@{ Op = 'Start' }, $SettingsGeneral, $SettingsNotifications, $SettingsStart) }
        @{ Id = 'spotlight-all'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = $CloudContentUser; Name = 'DisableWindowsSpotlightFeatures'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsLock, $SettingsBackground, $SettingsGeneral, $SettingsNotifications) }
        @{ Id = 'spotlight-settings'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = $CloudContentUser; Name = 'DisableWindowsSpotlightOnSettings'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsGeneral) }
        @{ Id = 'spotlight-thirdparty'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = $CloudContentUser; Name = 'DisableThirdPartySuggestions'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsLock, $SettingsGeneral) }
        @{ Id = 'softlanding'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = $CloudContent; Name = 'DisableSoftLanding'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsNotifications) }
        @{ Id = 'hide-recommended'; Group = 'ads'; Explorer = 'restart'
            Values = @(@{ Path = $ExplorerPolicy; Name = 'HideRecommendedSection'; Kind = 'DWord'; Value = 1 })
            Observe = @(@{ Op = 'Start' }, $SettingsStart) }
        @{ Id = 'hide-recommended-user'; Group = 'ads'; Explorer = 'restart'
            Values = @(@{ Path = $ExplorerPolicyUser; Name = 'HideRecommendedSection'; Kind = 'DWord'; Value = 1 })
            Observe = @(@{ Op = 'Start' }, $SettingsStart) }
        @{ Id = 'hide-personalized-sites'; Group = 'ads'; Explorer = 'restart'
            Values = @(@{ Path = $ExplorerPolicy; Name = 'HideRecommendedPersonalizedSites'; Kind = 'DWord'; Value = 1 })
            Observe = @(@{ Op = 'Start' }, $SettingsStart) }
        @{ Id = 'adid-policy'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo'; Name = 'DisabledByGroupPolicy'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsGeneral) }
        @{ Id = 'tailored-policy'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = $CloudContentUser; Name = 'DisableTailoredExperiencesWithDiagnosticData'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsGeneral, $SettingsFeedback) }
        # --- telemetry ---
        @{ Id = 'optional-diagnostics-toggle'; Group = 'telemetry'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:privacy-feedback'; Id = 'SystemSettings_Privacy_Diagnostic_Data_Toggle_2_ToggleSwitch'; Target = 'Off' }
            Observe = @($SettingsFeedback) }
        @{ Id = 'allowtelemetry'; Group = 'telemetry'; Explorer = 'none'
            Values = @(@{ Path = $DataCollection; Name = 'AllowTelemetry'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsFeedback) }
        @{ Id = 'limit-diagnostic-log'; Group = 'telemetry'; Explorer = 'none'
            Values = @(@{ Path = $DataCollection; Name = 'LimitDiagnosticLogCollection'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsFeedback) }
        @{ Id = 'limit-dump'; Group = 'telemetry'; Explorer = 'none'
            Values = @(@{ Path = $DataCollection; Name = 'LimitDumpCollection'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsFeedback) }
        @{ Id = 'feedback-notifications'; Group = 'telemetry'; Explorer = 'none'
            Values = @(@{ Path = $DataCollection; Name = 'DoNotShowFeedbackNotifications'; Kind = 'DWord'; Value = 1 })
            Observe = @($SettingsFeedback) }
        @{ Id = 'diagtrack'; Group = 'telemetry'; Explorer = 'none'; K1 = @('service:DiagTrack', 'wusearch:')
            Service = @{ Name = 'DiagTrack'; StartType = 'Disabled'; Stop = $true }
            Observe = @(@{ Op = 'UpdateCheck' }, $SettingsFeedback) }
        # --- ux ---
        @{ Id = 'classic-context-menu'; Group = 'ux'; Explorer = 'restart'
            Values = @(@{ Path = 'HKU\{SID}\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32'; Name = ''; Kind = 'String'; Value = '' })
            Observe = @(@{ Op = 'ContextMenu' }) }
        @{ Id = 'taskbar-endtask-toggle'; Group = 'ux'; Explorer = 'restart'
            Toggle = @{ Uri = 'ms-settings:developers'; Id = 'SystemSettings_Taskbar_Endtask_ToggleSwitch'; Target = 'On' }
            Observe = @(@{ Op = 'TaskbarMenu' }) }
        @{ Id = 'game-dvr'; Group = 'ux'; Explorer = 'none'
            Values = @(@{ Path = 'HKU\{SID}\System\GameConfigStore'; Name = 'GameDVR_Enabled'; Kind = 'DWord'; Value = 0 })
            Observe = @(@{ Op = 'Settings'; Uri = 'ms-settings:gaming-gamedvr' }) }
    )
}

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# ---------------------------------------------------------------------------------------------------------------------

function Test-E4A1VmComputerName {
    # Nazwy komputerów VM nadawane przez New-TestVm.ps1 (VmBuild: nazwa VM wielkimi literami).
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$ComputerName)
    return (@('DSO-26H2-HOME', 'DSO-26H2-PRO') -contains ([string]$ComputerName).ToUpperInvariant())
}

function Assert-E4A1InsideVm {
    # Ostatni bezpiecznik: sondy E4A1 zapisują rejestr HKLM/HKU, zmieniają usługi, restartują Eksploratora i klikają w UI, więc działają
    # WYŁĄCZNIE w VM DSO-26H2-Home/Pro i nigdy w testach na mockach (DSO_VM_TESTS_MOCK_ONLY z tests\vm\tests\Support\NoLiveVm.ps1).
    [CmdletBinding()]
    param([string]$ComputerName = $env:COMPUTERNAME)
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: sondy E4A1 nie mogą działać w testach na mockach (nic nie zostało uruchomione).'
    }
    if (-not (Test-E4A1VmComputerName -ComputerName $ComputerName)) {
        throw "Sondy E4A1 działają tylko w VM DSO-26H2-HOME/DSO-26H2-PRO (ten komputer: '$ComputerName'); nic nie zostało uruchomione."
    }
}

function Resolve-E4A1Path {
    # {SID} -> SID konta dso; tylko HKLM\ i HKU\ (jak Open-ExploreRegistryKey).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$UserSid
    )
    $Resolved = $Path.Replace('{SID}', $UserSid)
    if ($Resolved -notmatch '^(HKLM|HKU)\\') { throw "Resolve-E4A1Path: dozwolone tylko 'HKLM\' i 'HKU\' (podano '$Path')." }
    return $Resolved
}

function Open-E4A1BaseKey {
    # Gałąź (HKLM albo HKU, widok 64-bit) i ścieżka względna.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if ($Path -match '^HKLM\\(.+)$') { return @([Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64'), $Matches[1]) }
    if ($Path -match '^HKU\\(.+)$') { return @([Microsoft.Win32.RegistryKey]::OpenBaseKey('Users', 'Registry64'), $Matches[1]) }
    throw "Open-E4A1BaseKey: nieobsługiwana ścieżka '$Path'."
}

function ConvertTo-E4A1SnapshotValue {
    # Wartość migawki jako krótki tekst: binaria > 64 B jako długość + SHA1 (porównanie bez puchnięcia JSON).
    [CmdletBinding()]
    [OutputType([string])]
    param($Raw, [string]$Kind)
    if ($null -eq $Raw) { return $null }
    if ($Raw -is [byte[]]) {
        if ($Raw.Length -le 64) { return "${Kind}:" + [BitConverter]::ToString($Raw) }
        $Sha = [System.Security.Cryptography.SHA1]::Create()
        try { $Hash = [BitConverter]::ToString($Sha.ComputeHash($Raw)).Replace('-', '').Substring(0, 16) } finally { $Sha.Dispose() }
        return "${Kind}:len=$($Raw.Length):sha1=$Hash"
    }
    if ($Raw -is [string[]]) { return "${Kind}:" + ($Raw -join '|') }
    return "${Kind}:$Raw"
}

function Get-E4A1Snapshot {
    # Migawka drzew rejestru: słownik 'ścieżka|nazwa' -> 'Kind:wartość' oraz 'ścieżka|' -> '(klucz)' dla każdego klucza.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Roots,
        [int]$MaxDepth = 12
    )
    $Map = @{}
    foreach ($Root in $Roots) {
        $Pair = Open-E4A1BaseKey -Path $Root
        $Base = $Pair[0]
        try {
            $Key = $Base.OpenSubKey($Pair[1], $false)
            if ($null -eq $Key) { continue }
            $Stack = New-Object System.Collections.Stack
            $Stack.Push(@($Key, $Root, 0))
            while ($Stack.Count -gt 0) {
                $Item = $Stack.Pop()
                $Current = $Item[0]; $Name = $Item[1]; $Depth = $Item[2]
                try {
                    $Map["$Name|"] = '(klucz)'
                    foreach ($ValueName in @($Current.GetValueNames())) {
                        try {
                            $Raw = $Current.GetValue($ValueName, $null, 'DoNotExpandEnvironmentNames')
                            $Map["$Name|$ValueName"] = ConvertTo-E4A1SnapshotValue -Raw $Raw -Kind ([string]$Current.GetValueKind($ValueName))
                        }
                        catch { $Map["$Name|$ValueName"] = "(błąd: $($_.Exception.Message))" }
                    }
                    if ($Depth -lt $MaxDepth) {
                        foreach ($Sub in @($Current.GetSubKeyNames())) {
                            try { $Child = $Current.OpenSubKey($Sub, $false); if ($null -ne $Child) { $Stack.Push(@($Child, "$Name\$Sub", ($Depth + 1))) } }
                            catch { $Map["$Name\$Sub|"] = "(brak dostępu: $($_.Exception.Message))" }
                        }
                    }
                }
                finally { $Current.Dispose() }
            }
        }
        finally { $Base.Dispose() }
    }
    return $Map
}

function Compare-E4A1Snapshot {
    # Różnice dwóch migawek: lista {Path, Name, Before, After} (null = brak), posortowana.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$Before,
        [Parameter(Mandatory)][hashtable]$After
    )
    $Keys = New-Object System.Collections.Generic.HashSet[string]
    foreach ($K in $Before.Keys) { $null = $Keys.Add($K) }
    foreach ($K in $After.Keys) { $null = $Keys.Add($K) }
    $Diff = @()
    foreach ($K in ($Keys | Sort-Object)) {
        $B = $Before[$K]; $A = $After[$K]
        if ([string]$B -ne [string]$A -or (($null -eq $B) -ne ($null -eq $A))) {
            $Split = $K.LastIndexOf('|')
            $Diff += [pscustomobject]@{ Path = $K.Substring(0, $Split); Name = $K.Substring($Split + 1); Before = $B; After = $A }
        }
    }
    return $Diff
}

function Read-E4A1Value {
    # Jak Read-ExploreRegistryValue ({Key, KeyExists, Exists, Kind, Value, Hex}), ale dopuszcza pustą nazwę (wartość domyślna klucza).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name
    )
    $Result = [ordered]@{ Key = "$Path\$Name"; KeyExists = $false; Exists = $false; Kind = $null; Value = $null; Hex = $null }
    $Key = Open-ExploreRegistryKey -Path $Path
    if ($null -ne $Key) {
        try {
            $Result.KeyExists = $true
            $Data = Get-ExploreRegistryData -Key $Key -Name $Name
            if ($null -ne $Data) { $Result.Exists = $true; $Result.Kind = $Data.Kind; $Result.Value = $Data.Value; $Result.Hex = $Data.Hex }
        }
        finally { $Key.Dispose() }
    }
    return [pscustomobject]$Result
}

function Get-E4A1SnapshotRoots {
    [CmdletBinding()]
    [OutputType([string[]])]
    param([Parameter(Mandatory)][string]$UserSid)
    return @(
        'HKLM\SOFTWARE\Policies',
        'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies',
        'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo',
        "HKU\$UserSid\Software\Policies",
        "HKU\$UserSid\Software\Microsoft\Windows\CurrentVersion",
        "HKU\$UserSid\Software\Microsoft\Siuf",
        "HKU\$UserSid\Software\Microsoft\InputPersonalization",
        "HKU\$UserSid\System\GameConfigStore"
    )
}

function Test-E4A1NoisyPath {
    # Wpisy, które zmieniają się same (liczniki użycia, historia, bufory powłoki, znaczniki czasu): nie są przenoszone jako „mechanizm”
    # z różnicy przełącznika (lista z przebiegu próbnego 2026-10-21 na Pro: restart Eksploratora i otwarcie Ustawień).
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][string]$Path,
        [AllowEmptyString()][string]$Name = ''
    )
    if ($Path -match '\\(UserAssist|RecentDocs|BagMRU|Bags|SessionInfo|FeatureUsage|CloudStore|TaskManager|MuiCache|ComDlg32|StartupApproved|ActivityDataModel|ShellNoRoam|MountPoints2|WSX|AppListBackup|PushNotifications|Notifications\\Data|ApplicationAssociationToasts)(\\|$)') { return $true }
    # Tło z siecią (przebieg ai 2026-10-21): baza gier GameConfigStore\Children, stan dostarczania treści (podklucze ContentDeliveryManager,
    # nie jego wartości SubscribedContent-*), stan prelaunch Widżetów (CurrentVersion\Dsh).
    if ($Path -match '\\GameConfigStore\\Children(\\|$)|\\ContentDeliveryManager\\(Health|Subscriptions|CreativeEvents|Renderers|Cache|SuggestedApps)(\\|$)|\\CurrentVersion\\Dsh$') { return $true }
    if ($Path -match '\\CurrentVersion\\Start$' -and $Name -eq 'Config') { return $true }
    return [bool]($Name -match '^(LogonCount|GlobalAssocChangedCounter|InstalledPackagedAppsRevision|WebProviderLastNotificationBehaviorTimestamp|LastAppBackupListenerStartTime|packOpenedCount|packLoadedSuccessfullyCount)$')
}

function Set-E4A1Value {
    # Zapis jednej wartości przez .NET (jak API C# aplikacji). Wynik: {Path, Name, Ok, Error, CreatedKeys} - CreatedKeys do cofnięcia.
    # Kind 'Delete' = usunięcie wartości.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name,
        [Parameter(Mandatory)][ValidateSet('DWord', 'String', 'ExpandString', 'QWord', 'Delete')][string]$Kind,
        $Value
    )
    Assert-E4A1InsideVm
    $Result = [ordered]@{ Path = $Path; Name = $Name; Kind = $Kind; Value = $Value; Ok = $false; Error = $null; CreatedKeys = @() }
    $Pair = Open-E4A1BaseKey -Path $Path
    $Base = $Pair[0]
    try {
        # Klucze, których nie było (od najpłytszego), żeby cofnięcie mogło je usunąć.
        $Segments = $Pair[1].Split('\')
        $Prefix = ''
        $Created = @()
        foreach ($Segment in $Segments) {
            $Prefix = if ($Prefix) { "$Prefix\$Segment" } else { $Segment }
            $Probe = $Base.OpenSubKey($Prefix, $false)
            if ($null -eq $Probe) { $Created += $Prefix } else { $Probe.Dispose() }
        }
        if ($Kind -eq 'Delete') {
            $Key = $Base.OpenSubKey($Pair[1], $true)
            if ($null -ne $Key) { try { $Key.DeleteValue($Name, $false) } finally { $Key.Dispose() } }
        }
        else {
            $Key = $Base.CreateSubKey($Pair[1])
            try {
                $Data = switch ($Kind) { 'DWord' { [int]$Value } 'QWord' { [long]$Value } default { [string]$Value } }
                $Key.SetValue($Name, $Data, [Microsoft.Win32.RegistryValueKind]$Kind)
            }
            finally { $Key.Dispose() }
        }
        $Root = if ($Path -like 'HKLM\*') { 'HKLM' } else { 'HKU' }
        $Result.CreatedKeys = @($Created | ForEach-Object { "$Root\$_" })
        $Result.Ok = $true
    }
    catch { $Result.Error = "$($_.Exception.GetType().Name): $($_.Exception.Message)" }
    finally { $Base.Dispose() }
    return [pscustomobject]$Result
}

function Restore-E4A1Value {
    # Cofnięcie do stanu sprzed (rekord Read-ExploreRegistryValue): wartość odtworzona albo usunięta; klucze utworzone przez sondę usunięte
    # (od najgłębszego, tylko puste).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name,
        [Parameter(Mandatory)]$Before,
        [AllowEmptyCollection()][string[]]$CreatedKeys = @()
    )
    Assert-E4A1InsideVm
    $Result = [ordered]@{ Path = $Path; Name = $Name; Ok = $false; Error = $null; RemovedKeys = @() }
    try {
        if ($Before.Exists) {
            $Kind = [string]$Before.Kind
            if ($Kind -notin @('DWord', 'String', 'ExpandString', 'QWord')) { throw "Nieobsługiwany typ do odtworzenia: $Kind" }
            $Write = Set-E4A1Value -Path $Path -Name $Name -Kind $Kind -Value $Before.Value
            if (-not $Write.Ok) { throw $Write.Error }
        }
        else {
            $Delete = Set-E4A1Value -Path $Path -Name $Name -Kind 'Delete'
            if (-not $Delete.Ok) { throw $Delete.Error }
        }
        foreach ($KeyPath in ($CreatedKeys | Sort-Object { $_.Length } -Descending)) {
            $Pair = Open-E4A1BaseKey -Path $KeyPath
            try {
                $Key = $Pair[0].OpenSubKey($Pair[1], $false)
                if ($null -eq $Key) { continue }
                $Empty = ($Key.ValueCount -eq 0 -and $Key.SubKeyCount -eq 0)
                $Key.Dispose()
                if ($Empty) { $Pair[0].DeleteSubKey($Pair[1], $false); $Result.RemovedKeys += $KeyPath }
            }
            finally { $Pair[0].Dispose() }
        }
        $Result.Ok = $true
    }
    catch { $Result.Error = "$($_.Exception.GetType().Name): $($_.Exception.Message)" }
    return [pscustomobject]$Result
}

function Get-E4A1ShellProcess {
    # Procesy powłoki konta dso (explorer.exe) z właścicielem.
    [CmdletBinding()]
    param([ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso')
    $All = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction Stop)
    foreach ($Process in $All) {
        $Owner = Invoke-CimMethod -InputObject $Process -MethodName GetOwner -ErrorAction SilentlyContinue
        if ($null -ne $Owner -and $Owner.User -eq $UserName) { [pscustomobject]@{ ProcessId = [int]$Process.ProcessId; SessionId = [int]$Process.SessionId } }
    }
}

function Restart-E4A1Explorer {
    # Restart powłoki dso: Stop-Process -Force (AutoRestartShell uruchamia nową) + zatrzymanie hostów Start/wyszukiwania (wczytają politykę od nowa).
    [CmdletBinding()]
    param([ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso')
    Assert-E4A1InsideVm
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Old = @(Get-E4A1ShellProcess -UserName $UserName | ForEach-Object { $_.ProcessId })
    $Out = [ordered]@{ OldPids = $Old; NewPids = @(); WaitedMs = $null; Hosts = @(); Error = $null }
    try {
        foreach ($HostName in 'SearchHost', 'StartMenuExperienceHost') {
            foreach ($P in @(Get-Process -Name $HostName -ErrorAction SilentlyContinue)) { try { Stop-Process -Id $P.Id -Force -ErrorAction Stop; $Out.Hosts += "$HostName $($P.Id)" } catch { $Out.Hosts += "$HostName $($P.Id): $($_.Exception.Message)" } }
        }
        foreach ($ProcessId in $Old) { Stop-Process -Id $ProcessId -Force -ErrorAction Stop }
        for ($Poll = 0; $Poll -lt 60; $Poll++) {
            Start-Sleep -Milliseconds 500
            $New = @(Get-E4A1ShellProcess -UserName $UserName | ForEach-Object { $_.ProcessId } | Where-Object { $Old -notcontains $_ })
            if ($New.Count -gt 0) { $Out.NewPids = $New; break }
        }
        if (@($Out.NewPids).Count -eq 0) {
            # AutoRestartShell nie zadziałał w 30 s: tylko zapis błędu (bez awaryjnego startu powłoki). Kolejne obserwacje K2 tej pozycji
            # zwrócą wtedy błędy UI, a wynik pokaże przyczynę w ExplorerRestarts[].Error.
            $Out.Error = 'nowy explorer.exe nie pojawił się w 30 s'
        }
        Start-Sleep -Seconds 8
    }
    catch { $Out.Error = $_.Exception.Message }
    $Out.WaitedMs = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Out
}

function Get-E4A1K1Observation {
    # Odczyty K1: processes:<a,b>, feature:<nazwa>, service:<nazwa>, appx:<fragment nazwy>, wusearch: (wyszukiwanie Windows Update).
    [CmdletBinding()]
    param([AllowEmptyCollection()][string[]]$Spec = @())
    $Out = @()
    foreach ($Item in $Spec) {
        $Kind, $Arg = $Item -split ':', 2
        $Record = [ordered]@{ Spec = $Item; Data = $null; Error = $null }
        try {
            switch ($Kind) {
                'processes' { $Record.Data = @(Get-Process -Name ($Arg -split ',') -ErrorAction SilentlyContinue | ForEach-Object { "$($_.ProcessName):$($_.Id):sesja $($_.SessionId)" }) }
                'feature' { $F = Get-WindowsOptionalFeature -Online -FeatureName $Arg -ErrorAction Stop; $Record.Data = if ($null -ne $F) { [string]$F.State } else { '(brak funkcji)' } }
                'service' { $S = Get-Service -Name $Arg -ErrorAction Stop; $Record.Data = "Status=$($S.Status) StartType=$($S.StartType)" }
                'appx' { $Record.Data = @(Get-AppxPackage -AllUsers -ErrorAction Stop | Where-Object { $_.Name -like "*$Arg*" } | ForEach-Object { "$($_.Name) $($_.Version)" }) }
                'wusearch' {
                    # Prawdziwe wyszukiwanie aktualizacji przez Windows Update Agent API (COM Microsoft.Update.Session, tylko odczyt: Search,
                    # bez pobierania i instalacji). Przycisk w Ustawieniach po pierwszym sprawdzeniu zmienia się w „Pobierz i zainstaluj”,
                    # więc to jest powtarzalny dowód, że wyszukiwanie działa (albo błąd HRESULT, gdy nie działa).
                    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
                    try {
                        $Searcher = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher()
                        $Found = $Searcher.Search("IsInstalled=0 and Type='Software'")
                        $Record.Data = "ResultCode=$($Found.ResultCode) Updates=$($Found.Updates.Count) ms=$($Watch.ElapsedMilliseconds)"
                    }
                    catch { $Record.Data = "BŁĄD $('0x{0:X8}' -f $_.Exception.HResult) $($_.Exception.Message) ms=$($Watch.ElapsedMilliseconds)" }
                }
                default { throw "Nieznany odczyt K1 '$Item'." }
            }
        }
        catch { $Record.Error = $_.Exception.Message }
        $Out += [pscustomobject]$Record
    }
    return $Out
}

function Get-E4A1Inventory {
    # Stan wyjściowy systemu (tylko odczyt): edycja, aktywacja, pakiety AI/Widgets/Copilot/Bing, funkcja Recall, usługi UCPD i DiagTrack,
    # region (GeoId) i sieć.
    [CmdletBinding()]
    param()
    $Out = [ordered]@{}
    try { $Out.Appx = @(Get-AppxPackage -AllUsers -ErrorAction Stop | Where-Object { $_.Name -match 'Copilot|WebExperience|Widgets|CoreAI|AIX|ClickTo|Recall|BingSearch|OutlookForWindows|LinkedIn|WhatsApp|Solitaire|Xbox' } | ForEach-Object { "$($_.Name) $($_.Version)" }) } catch { $Out.Appx = "błąd: $($_.Exception.Message)" }
    try { $Out.Recall = [string](Get-WindowsOptionalFeature -Online -FeatureName Recall -ErrorAction Stop).State } catch { $Out.Recall = "błąd: $($_.Exception.Message)" }
    foreach ($Name in 'UCPD', 'DiagTrack', 'dmwappushservice', 'WSearch') {
        try { $S = Get-Service -Name $Name -ErrorAction Stop; $Out["Service_$Name"] = "Status=$($S.Status) StartType=$($S.StartType)" } catch { $Out["Service_$Name"] = "błąd: $($_.Exception.Message)" }
    }
    try { $Out.Activation = @(Get-CimInstance -ClassName SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'" -ErrorAction Stop | ForEach-Object { "$($_.Name): LicenseStatus=$($_.LicenseStatus)" }) } catch { $Out.Activation = "błąd: $($_.Exception.Message)" }
    try { $Out.GeoId = (Get-WinHomeLocation -ErrorAction Stop).GeoId } catch { $Out.GeoId = "błąd: $($_.Exception.Message)" }
    try { $Out.Network = @(Get-NetConnectionProfile -ErrorAction Stop | ForEach-Object { "$($_.InterfaceAlias): $($_.IPv4Connectivity)" }) } catch { $Out.Network = "błąd: $($_.Exception.Message)" }
    return [pscustomobject]$Out
}

# --- K2: operacje UI w sesji dso (kopiowane do skryptu zadania; Add-Type tylko tam) ---

function Initialize-E4A1K2Native {
    [CmdletBinding()]
    param()
    if ('E4A1User32' -as [type]) { return }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class E4A1User32 {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    public static void Press(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(50); keybd_event(vk, 0, 2, UIntPtr.Zero); }
    public static void Combo(byte a, byte b) {
        keybd_event(a, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(50);
        keybd_event(b, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(50);
        keybd_event(b, 0, 2, UIntPtr.Zero); keybd_event(a, 0, 2, UIntPtr.Zero);
    }
    public static void RightClick(int x, int y) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(150);
        mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
    }
    public static List<string> Visible() {
        var r = new List<string>();
        EnumWindows((h, l) => {
            if (IsWindowVisible(h)) {
                var c = new StringBuilder(256); var t = new StringBuilder(256); uint pid;
                GetClassName(h, c, 256); GetWindowText(h, t, 256); GetWindowThreadProcessId(h, out pid);
                r.Add(c.ToString() + " | " + t.ToString() + " | " + pid);
            }
            return true;
        }, IntPtr.Zero);
        return r;
    }
}
'@
}

function Save-E4A1K2Shot {
    # Zrzut pulpitu sesji dso do C:\dso\e4a1\shots\<Label>.png; zwraca nazwę pliku.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label)
    $Directory = 'C:\dso\e4a1\shots'
    $null = New-Item -ItemType Directory -Path $Directory -Force
    $Bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $Bitmap = New-Object System.Drawing.Bitmap($Bounds.Width, $Bounds.Height)
    try {
        $Graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
        try { $Graphics.CopyFromScreen($Bounds.Left, $Bounds.Top, 0, 0, $Bitmap.Size) } finally { $Graphics.Dispose() }
        $Bitmap.Save("$Directory\$Label.png", [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $Bitmap.Dispose() }
    return "$Label.png"
}

function Get-E4A1K2SettingsWindow {
    [CmdletBinding()]
    param()
    $AE = [System.Windows.Automation.AutomationElement]
    $Condition = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'ApplicationFrameWindow')
    foreach ($Window in $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $Condition)) {
        $Inner = $Window.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Windows.UI.Core.CoreWindow')))
        if ($null -ne $Inner -and $Inner.Current.Name -match 'Ustawienia|Settings') { return $Window }
    }
    return $null
}

function Open-E4A1K2Settings {
    # Otwiera stronę Ustawień (ms-settings:...), maksymalizuje okno, rozwija sekcje o podanych nazwach. Zwraca okno albo $null.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^ms-settings:[a-z0-9-]+\z')][string]$Uri,
        [AllowEmptyCollection()][string[]]$Expand = @()
    )
    Start-Process $Uri
    $Window = $null
    for ($Poll = 0; $Poll -lt 20 -and $null -eq $Window; $Poll++) { Start-Sleep -Milliseconds 500; $Window = Get-E4A1K2SettingsWindow }
    if ($null -eq $Window) { return $null }
    Start-Sleep -Seconds 3
    $Pattern = $null
    if ($Window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$Pattern)) { try { $Pattern.SetWindowVisualState('Maximized') } catch { $null = $_ } }
    Start-Sleep -Milliseconds 800
    # Sekcje do rozwinięcia bywają na dole wirtualizowanej listy (np. „Ustawienia dodatkowe” w Powiadomieniach, przebieg ads 2026-10-21):
    # szukanie po nazwie z przewijaniem w dół, rozwinięcie, powrót na górę.
    $Scroll = $null
    if ($Expand.Count -gt 0) {
        $ScrollCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)
        foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ScrollCondition)) {
            $Candidate = $Element.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
            if ($Candidate.Current.VerticallyScrollable -and $Element.Current.AutomationId -ne 'MenuItemsScrollViewer') { $Scroll = $Candidate; break }
        }
    }
    foreach ($Name in $Expand) {
        $Condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
        $Done = $false
        foreach ($Percent in -1, 25, 50, 75, 100) {
            if ($Percent -ge 0 -and $null -ne $Scroll) { try { $Scroll.SetScrollPercent(-1, $Percent) } catch { $null = $_ }; Start-Sleep -Milliseconds 700 }
            $ExpandableCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsExpandCollapsePatternAvailableProperty, $true)
            foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $Condition)) {
                # Element o tej nazwie albo (gdy sam nie ma wzorca) rozwijalny element w nim lub w jego rodzicu (karta sekcji Ustawień).
                $Targets = @($Element)
                $Targets += @($Element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ExpandableCondition))
                $Parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($Element)
                if ($null -ne $Parent) { $Targets += @($Parent.FindAll([System.Windows.Automation.TreeScope]::Children, $ExpandableCondition)) }
                foreach ($Target in $Targets) {
                    $Expander = $null
                    if ($Target.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox) { continue }
                    if ($Target.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$Expander)) {
                        try { if ([string]$Expander.Current.ExpandCollapseState -eq 'Collapsed') { $Expander.Expand(); Start-Sleep -Milliseconds 800 }; $Done = $true } catch { $null = $_ }
                    }
                    if ($Done) { break }
                }
                if ($Done) { break }
            }
            if ($Done -or $null -eq $Scroll) { break }
        }
    }
    if ($null -ne $Scroll) { try { $Scroll.SetScrollPercent(-1, 0) } catch { $null = $_ }; Start-Sleep -Milliseconds 500 }
    return $Window
}

function Get-E4A1K2SettingsItems {
    # Elementy strony (z przewijaniem, bo lista jest wirtualizowana): przełączniki, listy, AutomationId SystemSettings_*, teksty o zarządzaniu.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Window)
    $AE = [System.Windows.Automation.AutomationElement]
    $Map = @{}
    $Collect = {
        foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            try {
                $C = $Element.Current
                $Toggle = $null; $Value = $null; $Pattern = $null
                if ($Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$Pattern)) { $Toggle = [string]$Pattern.Current.ToggleState }
                if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern, [ref]$Pattern)) { $Value = (@($Pattern.Current.GetSelection() | ForEach-Object { $_.Current.Name }) -join '; ') }
                $Interesting = ($C.AutomationId -like 'SystemSettings_*') -or ($null -ne $Toggle) -or ($C.ControlType.ProgrammaticName -eq 'ControlType.ComboBox') -or ($C.Name -match 'organizac|zarz.dza|administrator|Aktywuj')
                if (-not $Interesting) { continue }
                $Key = "$($C.AutomationId)|$($C.Name)|$($C.ControlType.ProgrammaticName)"
                $Map[$Key] = [pscustomobject]@{ Id = $C.AutomationId; Name = $C.Name; Type = ($C.ControlType.ProgrammaticName -replace '^ControlType\.', ''); Enabled = $C.IsEnabled; Toggle = $Toggle; Value = $Value }
            }
            catch { $null = $_ }
        }
    }
    & $Collect
    $Scroll = $null
    $ScrollCondition = New-Object System.Windows.Automation.PropertyCondition($AE::IsScrollPatternAvailableProperty, $true)
    foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ScrollCondition)) {
        $Pattern = $Element.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($Pattern.Current.VerticallyScrollable -and $Element.Current.AutomationId -ne 'MenuItemsScrollViewer') { $Scroll = $Pattern; break }
    }
    if ($null -ne $Scroll) {
        foreach ($Percent in 25, 50, 75, 100) { try { $Scroll.SetScrollPercent(-1, $Percent) } catch { $null = $_ }; Start-Sleep -Milliseconds 600; & $Collect }
        try { $Scroll.SetScrollPercent(-1, 0) } catch { $null = $_ }
    }
    return @($Map.Values | Sort-Object Id, Name)
}

function Find-E4A1K2Toggle {
    # Przełącznik po AutomationId albo po fragmencie nazwy (Match); przewija, aż element się zmaterializuje.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Window, [string]$Id, [string]$Match)
    $AE = [System.Windows.Automation.AutomationElement]
    $Find = {
        foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            $Pattern = $null
            if (-not $Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$Pattern)) { continue }
            if ($Id -and $Element.Current.AutomationId -eq $Id) { return $Element }
            if ($Match -and $Element.Current.Name -match $Match) { return $Element }
        }
        return $null
    }
    $Found = & $Find
    if ($null -ne $Found) { return $Found }
    $ScrollCondition = New-Object System.Windows.Automation.PropertyCondition($AE::IsScrollPatternAvailableProperty, $true)
    foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ScrollCondition)) {
        $Scroll = $Element.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if (-not $Scroll.Current.VerticallyScrollable -or $Element.Current.AutomationId -eq 'MenuItemsScrollViewer') { continue }
        foreach ($Percent in 20, 40, 60, 80, 100) {
            try { $Scroll.SetScrollPercent(-1, $Percent) } catch { $null = $_ }
            Start-Sleep -Milliseconds 600
            $Found = & $Find
            if ($null -ne $Found) { return $Found }
        }
    }
    return $null
}

function Get-E4A1K2Settings {
    # Operacja Settings: elementy strony + zrzut (z przewinięciem do Focus, jeśli podano).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Uri,
        [AllowEmptyCollection()][string[]]$Expand = @(),
        [Parameter(Mandatory)][string]$Label
    )
    $Out = [ordered]@{ Op = 'Settings'; Uri = $Uri; Found = $false; Items = @(); Shot = $null; Error = $null }
    try {
        $Window = Open-E4A1K2Settings -Uri $Uri -Expand $Expand
        if ($null -ne $Window) {
            $Out.Found = $true
            $Out.Items = @(Get-E4A1K2SettingsItems -Window $Window)
            $Out.Shot = Save-E4A1K2Shot -Label $Label
        }
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Set-E4A1K2Toggle {
    # Operacja Toggle: przełącza przełącznik do stanu Want (TogglePattern, jak klik użytkownika); zwraca stan przed/po, IsEnabled i zrzut.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Uri,
        [string]$Id,
        [string]$Match,
        [AllowEmptyCollection()][string[]]$Expand = @(),
        [Parameter(Mandatory)][ValidateSet('On', 'Off')][string]$Want,
        [Parameter(Mandatory)][string]$Label
    )
    $Out = [ordered]@{ Op = 'Toggle'; Uri = $Uri; Id = $Id; Match = $Match; Want = $Want; Found = $false; ElementId = $null; Name = $null; Enabled = $null; Before = $null; After = $null; Shot = $null; Error = $null }
    try {
        $Window = Open-E4A1K2Settings -Uri $Uri -Expand $Expand
        if ($null -eq $Window) { throw "okno Ustawień nie pojawiło się ($Uri)" }
        $Element = Find-E4A1K2Toggle -Window $Window -Id $Id -Match $Match
        if ($null -eq $Element) { throw "nie znaleziono przełącznika (Id='$Id', Match='$Match')" }
        $Out.Found = $true
        $Out.ElementId = $Element.Current.AutomationId
        $Out.Name = $Element.Current.Name
        $Out.Enabled = $Element.Current.IsEnabled
        $Pattern = $Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $Out.Before = [string]$Pattern.Current.ToggleState
        for ($Attempt = 0; $Attempt -lt 2 -and [string]$Pattern.Current.ToggleState -ne $Want; $Attempt++) { $Pattern.Toggle(); Start-Sleep -Seconds 2 }
        $Out.After = [string]$Pattern.Current.ToggleState
        $Out.Shot = Save-E4A1K2Shot -Label $Label
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Close-E4A1K2Settings {
    # Zamyka okno Ustawień (WindowPattern.Close), żeby kolejne otwarcie czytało stan od nowa.
    [CmdletBinding()]
    param()
    $Window = Get-E4A1K2SettingsWindow
    if ($null -eq $Window) { return }
    $Pattern = $null
    if ($Window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$Pattern)) { try { $Pattern.Close() } catch { $null = $_ } }
    Start-Sleep -Seconds 1
}

function Get-E4A1K2NewWindows {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Before)
    return @([E4A1User32]::Visible() | Where-Object { $Before -notcontains $_ })
}

function Get-E4A1K2Search {
    # Operacja Search: klawisz Win, wpisanie frazy, 8 s na wyniki (z sieci), zrzut, Esc. Wyniki są w WebView2 (niewidoczne dla UIA) - dowodem jest zrzut.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9 ]+\z')][string]$Query, [Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'Search'; Query = $Query; Shot = $null; Error = $null }
    try {
        Close-E4A1K2Settings
        [E4A1User32]::Press(0x5B); Start-Sleep -Seconds 3
        [System.Windows.Forms.SendKeys]::SendWait($Query); Start-Sleep -Seconds 8
        $Out.Shot = Save-E4A1K2Shot -Label $Label
        [E4A1User32]::Press(0x1B); Start-Sleep -Seconds 1
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-E4A1K2SearchHome {
    # Operacja SearchHome: Win+S (strona główna wyszukiwania: wyróżnienia), 8 s, zrzut, Esc.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'SearchHome'; Shot = $null; Error = $null }
    try {
        Close-E4A1K2Settings
        [E4A1User32]::Combo(0x5B, 0x53); Start-Sleep -Seconds 8
        $Out.Shot = Save-E4A1K2Shot -Label $Label
        [E4A1User32]::Press(0x1B); Start-Sleep -Seconds 1
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-E4A1K2Start {
    # Operacja Start: klawisz Win, 4 s, zrzut (sekcja „Proponowane”/przypięte), Esc.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'Start'; Shot = $null; Error = $null }
    try {
        Close-E4A1K2Settings
        [E4A1User32]::Press(0x5B); Start-Sleep -Seconds 4
        $Out.Shot = Save-E4A1K2Shot -Label $Label
        [E4A1User32]::Press(0x1B); Start-Sleep -Seconds 1
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-E4A1K2Taskbar {
    # Operacja Taskbar: przyciski paska (UIA Shell_TrayWnd: AutomationId i nazwa) + zrzut.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'Taskbar'; Buttons = @(); Shot = $null; Error = $null }
    try {
        $AE = [System.Windows.Automation.AutomationElement]
        $Tray = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
        if ($null -eq $Tray) { throw 'brak Shell_TrayWnd' }
        $Condition = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $Out.Buttons = @($Tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, $Condition) | ForEach-Object { "$($_.Current.AutomationId) | $($_.Current.Name)" })
        $Out.Shot = Save-E4A1K2Shot -Label $Label
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-E4A1K2Widgets {
    # Operacja Widgets: Win+W, 6 s, nowe okna (WindowsDashboard = tablica widżetów) + procesy Widget*, zrzut, Esc.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'Widgets'; NewWindows = @(); Processes = @(); BoardOpened = $false; Shot = $null; Error = $null }
    try {
        Close-E4A1K2Settings
        $Before = @([E4A1User32]::Visible())
        [E4A1User32]::Combo(0x5B, 0x57); Start-Sleep -Seconds 6
        $Out.NewWindows = @(Get-E4A1K2NewWindows -Before $Before)
        $Out.BoardOpened = [bool](@($Out.NewWindows | Where-Object { $_ -like 'WindowsDashboard*' }).Count -gt 0)
        $Out.Processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'Widget' } | ForEach-Object { "$($_.ProcessName):$($_.Id)" })
        $Out.Shot = Save-E4A1K2Shot -Label $Label
        [E4A1User32]::Press(0x1B); Start-Sleep -Seconds 1
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-E4A1K2Copilot {
    # Operacja Copilot: Win+C, 6 s, nowe okna i procesy z „Copilot” w nazwie, zrzut, Esc.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'Copilot'; NewWindows = @(); Processes = @(); Shot = $null; Error = $null }
    try {
        Close-E4A1K2Settings
        $Before = @([E4A1User32]::Visible())
        [E4A1User32]::Combo(0x5B, 0x43); Start-Sleep -Seconds 6
        $Out.NewWindows = @(Get-E4A1K2NewWindows -Before $Before)
        $Out.Processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'Copilot' } | ForEach-Object { "$($_.ProcessName):$($_.Id)" })
        $Out.Shot = Save-E4A1K2Shot -Label $Label
        [E4A1User32]::Press(0x1B); Start-Sleep -Seconds 1
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-E4A1K2ContextMenu {
    # Operacja ContextMenu: zminimalizowane okna, prawy klik na pulpicie (500,300), nowe okna: '#32768' = klasyczne menu Win32,
    # 'Microsoft.UI.Content.PopupWindowSiteBridge'/'XamlExplorerHostIslandWindow*' = menu Windows 11; zrzut, Esc, przywrócenie okien.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'ContextMenu'; NewWindows = @(); Classic = $false; Modern = $false; Shot = $null; Error = $null }
    $Shell = $null
    try {
        Close-E4A1K2Settings
        $Shell = New-Object -ComObject Shell.Application
        $Shell.MinimizeAll(); Start-Sleep -Seconds 2
        $Before = @([E4A1User32]::Visible())
        [E4A1User32]::RightClick(500, 300); Start-Sleep -Seconds 2
        $Out.NewWindows = @(Get-E4A1K2NewWindows -Before $Before)
        $Out.Classic = [bool](@($Out.NewWindows | Where-Object { $_ -like '#32768 |*' }).Count -gt 0)
        $Out.Modern = [bool](@($Out.NewWindows | Where-Object { $_ -like 'Microsoft.UI.Content.PopupWindowSiteBridge*' -or $_ -like 'XamlExplorerHostIslandWindow*' }).Count -gt 0)
        $Out.Shot = Save-E4A1K2Shot -Label $Label
        [E4A1User32]::Press(0x1B); Start-Sleep -Seconds 1
    }
    catch { $Out.Error = $_.Exception.Message }
    finally { if ($null -ne $Shell) { try { $Shell.UndoMinimizeALL() } catch { $null = $_ } } }
    return [pscustomobject]$Out
}

function Get-E4A1K2TaskbarMenu {
    # Operacja TaskbarMenu: otwarte Ustawienia, prawy klik w ich przycisk na pasku, nowe okna i elementy UIA z nazwą 'Zako*czenie|Zako*cz zadanie|End task'
    # (szukane w drzewie elementu z fokusem), zrzut, Esc.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'TaskbarMenu'; Button = $null; NewWindows = @(); MenuItems = @(); EndTaskFound = $false; Shot = $null; Error = $null }
    try {
        $AE = [System.Windows.Automation.AutomationElement]
        $Window = Open-E4A1K2Settings -Uri 'ms-settings:about'
        if ($null -eq $Window) { throw 'okno Ustawień nie pojawiło się' }
        $Tray = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
        $Condition = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $Button = @($Tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, $Condition) | Where-Object { $_.Current.AutomationId -like '*immersivecontrolpanel*' }) | Select-Object -First 1
        if ($null -eq $Button) { throw 'brak przycisku Ustawień na pasku' }
        $Rect = $Button.Current.BoundingRectangle
        $Out.Button = "$($Button.Current.Name) @ $Rect"
        $Before = @([E4A1User32]::Visible())
        [E4A1User32]::RightClick([int]($Rect.X + $Rect.Width / 2), [int]($Rect.Y + $Rect.Height / 2)); Start-Sleep -Seconds 3
        $Out.NewWindows = @(Get-E4A1K2NewWindows -Before $Before)
        $Focused = $AE::FocusedElement
        $Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $Top = $Focused
        while ($null -ne $Top) { $Parent = $Walker.GetParent($Top); if ($null -eq $Parent -or $Parent -eq $AE::RootElement) { break }; $Top = $Parent }
        if ($null -ne $Top) {
            $Out.MenuItems = @($Top.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
                    Where-Object { $_.Current.Name } | ForEach-Object { "$($_.Current.ControlType.ProgrammaticName -replace '^ControlType\.', '') | $($_.Current.Name)" } | Select-Object -First 60)
        }
        $Out.EndTaskFound = [bool](@($Out.MenuItems | Where-Object { $_ -match 'Zako.cz zadanie|End task' }).Count -gt 0)
        $Out.Shot = Save-E4A1K2Shot -Label $Label
        [E4A1User32]::Press(0x1B); Start-Sleep -Seconds 1
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Invoke-E4A1K2UpdateCheck {
    # Operacja UpdateCheck: Ustawienia > Windows Update, przycisk sprawdzania (AutomationId *CheckForUpdates* albo nazwa 'Sprawd*'),
    # do 120 s odczyt tekstów strony (stan, błędy 0x...), zrzut.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'UpdateCheck'; Clicked = $null; Texts = @(); Shot = $null; Error = $null }
    try {
        $AE = [System.Windows.Automation.AutomationElement]
        $Window = Open-E4A1K2Settings -Uri 'ms-settings:windowsupdate'
        if ($null -eq $Window) { throw 'okno Ustawień nie pojawiło się' }
        $Condition = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $Button = @($Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $Condition) | Where-Object { $_.Current.AutomationId -match 'CheckForUpdates' -or $_.Current.Name -match '^Sprawd|^Check for|^Pon.w' }) | Select-Object -First 1
        if ($null -ne $Button) {
            $Out.Clicked = "$($Button.Current.AutomationId) | $($Button.Current.Name)"
            $Invoke = $Button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $Invoke.Invoke()
        }
        $TextCondition = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
        for ($Poll = 0; $Poll -lt 24; $Poll++) {
            Start-Sleep -Seconds 5
            $Texts = @($Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $TextCondition) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -and $_ -match 'aktualn|Sprawdzanie|Pobieranie|Instalowanie|b..d|0x|Error|ponow|Ostatnio' })
            $Out.Texts = $Texts
            if (-not ($Texts -match 'Sprawdzanie')) { if ($Poll -ge 3) { break } }
        }
        $Out.Shot = Save-E4A1K2Shot -Label $Label
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Invoke-E4A1K2Ops {
    # Dyspozytor operacji K2 (lista {Op, ...argumenty, Label}); błąd operacji jest daną.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops)
    Initialize-E4A1K2Native
    $Results = @()
    foreach ($Op in $Ops) {
        try {
            $Label = [string]$Op.Label
            $Expand = @(); if ($Op.PSObject.Properties['Expand'] -and $null -ne $Op.Expand) { $Expand = @($Op.Expand) }
            switch ([string]$Op.Op) {
                'Settings' { $Results += Get-E4A1K2Settings -Uri $Op.Uri -Expand $Expand -Label $Label }
                'Toggle' {
                    $Id = if ($Op.PSObject.Properties['Id']) { [string]$Op.Id } else { '' }
                    $Match = if ($Op.PSObject.Properties['Match']) { [string]$Op.Match } else { '' }
                    $Results += Set-E4A1K2Toggle -Uri $Op.Uri -Id $Id -Match $Match -Expand $Expand -Want $Op.Want -Label $Label
                }
                'Search' { $Results += Get-E4A1K2Search -Query $Op.Query -Label $Label }
                'SearchHome' { $Results += Get-E4A1K2SearchHome -Label $Label }
                'Start' { $Results += Get-E4A1K2Start -Label $Label }
                'Taskbar' { $Results += Get-E4A1K2Taskbar -Label $Label }
                'Widgets' { $Results += Get-E4A1K2Widgets -Label $Label }
                'Copilot' { $Results += Get-E4A1K2Copilot -Label $Label }
                'ContextMenu' { $Results += Get-E4A1K2ContextMenu -Label $Label }
                'TaskbarMenu' { $Results += Get-E4A1K2TaskbarMenu -Label $Label }
                'UpdateCheck' { $Results += Invoke-E4A1K2UpdateCheck -Label $Label }
                default { throw "Nieznana operacja K2 '$($Op.Op)'." }
            }
        }
        catch { $Results += [pscustomobject]@{ Op = [string]$Op.Op; Error = $_.Exception.Message } }
    }
    Close-E4A1K2Settings
    return $Results
}

function Get-E4A1ChildFunctionNames {
    # Funkcje kopiowane do skryptu zadania K2 (domknięcie wywołań pilnuje test).
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @(
        'Initialize-E4A1K2Native', 'Save-E4A1K2Shot', 'Get-E4A1K2SettingsWindow', 'Open-E4A1K2Settings', 'Get-E4A1K2SettingsItems', 'Find-E4A1K2Toggle',
        'Get-E4A1K2Settings', 'Set-E4A1K2Toggle', 'Close-E4A1K2Settings', 'Get-E4A1K2NewWindows', 'Get-E4A1K2Search', 'Get-E4A1K2SearchHome', 'Get-E4A1K2Start',
        'Get-E4A1K2Taskbar', 'Get-E4A1K2Widgets', 'Get-E4A1K2Copilot', 'Get-E4A1K2ContextMenu', 'Get-E4A1K2TaskbarMenu', 'Invoke-E4A1K2UpdateCheck', 'Invoke-E4A1K2Ops'
    )
}

function New-E4A1ChildScript {
    # Skrypt zadania K2: param(ResultFile, OpsFile), definicje funkcji, Invoke-E4A1K2Ops na liście z pliku JSON, wynik atomowo przez plik tymczasowy.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Header = @'
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][string]$OpsFile)
$ErrorActionPreference = 'Stop'
'@
    $Definitions = foreach ($Name in Get-E4A1ChildFunctionNames) { "function $Name {`n$((Get-Command -Name $Name -CommandType Function -ErrorAction Stop).Definition)`n}" }
    $Main = @'
$Document = [ordered]@{ Ok = $false; Error = $null; ProcessId = $PID; Results = @() }
try {
    $Parsed = Get-Content -LiteralPath $OpsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $Ops = @(foreach ($Item in $Parsed) { $Item })
    $Document.Results = @(Invoke-E4A1K2Ops -Ops $Ops)
    $Document.Ok = $true
}
catch { $Document.Error = $_.Exception.Message }
$Temporary = $ResultFile + '.tmp'
$Document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Temporary -Encoding UTF8
Move-Item -LiteralPath $Temporary -Destination $ResultFile -Force
'@
    return ($Header + "`n`n" + ($Definitions -join "`n`n") + "`n`n" + $Main)
}

function Invoke-E4A1UserTask {
    # K2: zadanie Harmonogramu (dso / Interactive / Highest) z listą operacji; wynik z pliku; zadanie ZAWSZE wyrejestrowane.
    # Niedostępność K2 to dana (Available=false, Reason), nie wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops,
        [ValidateRange(10, 1800)][int]$TimeoutSeconds = 240,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-E4A1InsideVm
    $Directory = 'C:\dso\e4a1'
    $TaskName = "DSO-E4A1-$Name"
    $ScriptPath = "$Directory\k2.ps1"
    $OpsPath = "$Directory\$Name.ops.json"
    $ResultPath = "$Directory\$Name.json"
    $Out = [ordered]@{ Name = $Name; Available = $false; Reason = $null; WaitedMs = $null; ChildError = $null; UnregisterError = $null; Results = @() }
    $Registered = $false
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $null = New-Item -ItemType Directory -Path $Directory -Force
        Set-Content -LiteralPath $ScriptPath -Value (New-E4A1ChildScript) -Encoding UTF8
        Set-Content -LiteralPath $OpsPath -Value (ConvertTo-Json -InputObject @($Ops) -Depth 6) -Encoding UTF8
        if (Test-Path -LiteralPath $ResultPath) { Remove-Item -LiteralPath $ResultPath -Force }
        $PowerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $Action = New-ScheduledTaskAction -Execute $PowerShellPath -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File $ScriptPath -ResultFile $ResultPath -OpsFile $OpsPath"
        $Principal = New-ScheduledTaskPrincipal -UserId $UserName -LogonType Interactive -RunLevel Highest
        $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds ($TimeoutSeconds + 60))
        $Registered = $true
        $null = Register-ScheduledTask -TaskName $TaskName -Action $Action -Principal $Principal -Settings $Settings -Force -ErrorAction Stop
        Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        $Found = $false
        for ($Poll = 1; $Poll -le $TimeoutSeconds * 2; $Poll++) {
            if (Test-Path -LiteralPath $ResultPath) { $Found = $true; break }
            Start-Sleep -Milliseconds 500
        }
        $Out.WaitedMs = [int]$Watch.ElapsedMilliseconds
        if ($Found) {
            Start-Sleep -Milliseconds 300
            $Child = Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $Out.Available = $true
            if (-not $Child.Ok) { $Out.ChildError = $Child.Error }
            $Out.Results = @($Child.Results)
        }
        else {
            $Out.Reason = "K2 niedostępne: brak wyniku w $TimeoutSeconds s"
            try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction Stop } catch { $Out.Reason += " | Stop-ScheduledTask: $($_.Exception.Message)" }
        }
    }
    catch { $Out.Reason = "K2 niedostępne: $($_.Exception.Message)" }
    finally {
        if ($Registered) {
            try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop } catch { $Out.UnregisterError = $_.Exception.Message }
        }
    }
    return [pscustomobject]$Out
}

function ConvertTo-E4A1Ops {
    # Operacje obserwacji pozycji z etykietami zrzutów '<pozycja>-<faza>-<nr>-<op>'.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$CandidateId,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Phase,
        [AllowEmptyCollection()][object[]]$Observe = @()
    )
    $Index = 0
    foreach ($Item in $Observe) {
        $Index++
        $Op = [ordered]@{}
        foreach ($Key in $Item.Keys) { $Op[$Key] = $Item[$Key] }
        $Op.Label = ("$CandidateId-$Phase-$Index-$($Item.Op)").ToLowerInvariant()
        [pscustomobject]$Op
    }
}

function Invoke-E4A1Observe {
    # Faza obserwacji: odczyty K1 + jedno zadanie K2 z operacjami pozycji.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$Spec,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Phase
    )
    $K1 = @()
    if ($Spec.ContainsKey('K1')) { $K1 = @(Get-E4A1K1Observation -Spec @($Spec.K1)) }
    $Ops = @()
    if ($Spec.ContainsKey('Observe')) { $Ops = @(ConvertTo-E4A1Ops -CandidateId $Spec.Id -Phase $Phase -Observe @($Spec.Observe)) }
    $K2 = $null
    if ($Ops.Count -gt 0) { $K2 = Invoke-E4A1UserTask -Name "$($Spec.Id)-$Phase" -Ops $Ops -TimeoutSeconds (60 + 60 * $Ops.Count) }
    return [pscustomobject]@{ Phase = $Phase; At = (Get-Date).ToString('o'); K1 = $K1; K2 = $K2 }
}

function Invoke-E4A1Explorer {
    # Restart Eksploratora wg pozycji: 'restart'/'both' -> restart; 'none' -> nic.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('none', 'restart', 'both')][string]$Mode)
    if ($Mode -eq 'none') { return $null }
    return (Restart-E4A1Explorer)
}

function Invoke-E4A1ApplyValues {
    # Zapis listy wartości (z {SID}); zwraca rekordy zapisu ze stanem sprzed (do cofnięcia).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Values,
        [Parameter(Mandatory)][string]$UserSid
    )
    foreach ($Value in $Values) {
        $Path = Resolve-E4A1Path -Path $Value.Path -UserSid $UserSid
        $Before = Read-E4A1Value -Path $Path -Name $Value.Name
        $Write = Set-E4A1Value -Path $Path -Name $Value.Name -Kind $Value.Kind -Value $Value.Value
        $After = Read-E4A1Value -Path $Path -Name $Value.Name
        [pscustomobject]@{ Path = $Path; Name = $Value.Name; Before = $Before; Write = $Write; After = $After }
    }
}

function Invoke-E4A1UndoValues {
    # Cofnięcie rekordów Invoke-E4A1ApplyValues w odwrotnej kolejności; odczyt po cofnięciu (wartość i istnienie klucza).
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Applied)
    $List = @($Applied)
    [array]::Reverse($List)
    foreach ($Record in $List) {
        $Created = @(); if ($null -ne $Record.Write -and $null -ne $Record.Write.CreatedKeys) { $Created = @($Record.Write.CreatedKeys) }
        $Restore = Restore-E4A1Value -Path $Record.Path -Name $Record.Name -Before $Record.Before -CreatedKeys $Created
        $After = Read-E4A1Value -Path $Record.Path -Name $Record.Name
        [pscustomobject]@{ Path = $Record.Path; Name = $Record.Name; Restore = $Restore; After = $After }
    }
}

function ConvertFrom-E4A1Diff {
    # Różnica przełącznika -> lista wartości do zapisu mechanizmem (stan „po”): pomija szum (Test-E4A1NoisyPath), klucze i binaria;
    # brak wartości po = usunięcie.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff)
    foreach ($Entry in $Diff) {
        if ($Entry.Name -eq '' -and ($Entry.After -eq '(klucz)' -or $Entry.Before -eq '(klucz)')) { continue }
        if (Test-E4A1NoisyPath -Path $Entry.Path -Name $Entry.Name) { continue }
        if ($null -eq $Entry.After) { [pscustomobject]@{ Path = $Entry.Path; Name = $Entry.Name; Kind = 'Delete'; Value = $null }; continue }
        if ([string]$Entry.After -match '^(DWord|String|ExpandString|QWord):(.*)$') {
            $Kind = $Matches[1]; $Raw = $Matches[2]
            $Value = switch ($Kind) { 'DWord' { [int]::Parse($Raw) } 'QWord' { [long]::Parse($Raw) } default { $Raw } }
            [pscustomobject]@{ Path = $Entry.Path; Name = $Entry.Name; Kind = $Kind; Value = $Value }
        }
    }
}

function Invoke-E4A1Candidate {
    # Jedna pozycja: stan przed -> przełącznik oficjalny (diff) -> zmiana mechanizmem -> obserwacja -> cofnięcie -> obserwacja.
    # Błąd kroku trafia do Errors; cofnięcie jest zawsze próbowane.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$Spec,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-E4A1InsideVm
    $Roots = Get-E4A1SnapshotRoots -UserSid $UserSid
    $Explorer = if ($Spec.ContainsKey('Explorer')) { [string]$Spec.Explorer } else { 'none' }
    $Out = [ordered]@{ Id = $Spec.Id; Group = $Spec.Group; StartedAt = (Get-Date).ToString('o'); ValuesBefore = @(); Observations = @(); Toggle = $null
        Applied = @(); ApplyDiff = @(); Service = $null; ExplorerRestarts = @(); Undo = @(); UndoDiff = @(); Errors = @(); DurationMs = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $ObserveOnly = [bool]($Spec.ContainsKey('ObserveOnly') -and $Spec.ObserveOnly)
    $Base = Get-E4A1Snapshot -Roots $Roots
    try {
        if ($Spec.ContainsKey('Values')) { $Out.ValuesBefore = @($Spec.Values | ForEach-Object { Read-E4A1Value -Path (Resolve-E4A1Path -Path $_.Path -UserSid $UserSid) -Name $_.Name }) }
        $Out.Observations += Invoke-E4A1Observe -Spec $Spec -Phase 'przed'
        if ($ObserveOnly) { throw [System.OperationCanceledException]::new('tylko obserwacja') }
        $Values = @()
        if ($Spec.ContainsKey('Values')) { $Values = @($Spec.Values) }
        if ($Spec.ContainsKey('Toggle')) {
            # Przełącznik oficjalny: najpierw na stan przeciwny do Target (jeśli już jest Target) albo na Target; różnice w obie strony.
            $T = $Spec.Toggle
            $Op = [ordered]@{ Op = 'Toggle'; Uri = $T.Uri; Want = $T.Target }
            if ($T.ContainsKey('Id')) { $Op.Id = $T.Id }
            if ($T.ContainsKey('Match')) { $Op.Match = $T.Match }
            if ($T.ContainsKey('Expand')) { $Op.Expand = @($T.Expand) }
            $Opposite = if ($T.Target -eq 'On') { 'Off' } else { 'On' }
            $Record = [ordered]@{ ToTarget = $null; ToTargetDiff = @(); ToOpposite = $null; ToOppositeDiff = @(); Restore = $null; RestoreDiff = @(); Mechanism = @(); OppositeMechanism = @() }
            $Op.Label = "$($Spec.Id)-toggle-1-target"
            $First = Invoke-E4A1UserTask -Name "$($Spec.Id)-toggle1" -Ops @([pscustomobject]$Op) -TimeoutSeconds 180
            $Record.ToTarget = $First
            $S1 = Get-E4A1Snapshot -Roots $Roots
            $Record.ToTargetDiff = @(Compare-E4A1Snapshot -Before $Base -After $S1)
            $Op.Want = $Opposite; $Op.Label = "$($Spec.Id)-toggle-2-opposite"
            $Second = Invoke-E4A1UserTask -Name "$($Spec.Id)-toggle2" -Ops @([pscustomobject]$Op) -TimeoutSeconds 180
            $Record.ToOpposite = $Second
            $S2 = Get-E4A1Snapshot -Roots $Roots
            $Record.ToOppositeDiff = @(Compare-E4A1Snapshot -Before $S1 -After $S2)
            # Powrót do stanu wyjściowego przełącznika (przed).
            $Initial = $null
            if ($null -ne $First -and @($First.Results).Count -gt 0) { $Initial = [string]$First.Results[0].Before }
            if ($Initial -in @('On', 'Off')) {
                $Op.Want = $Initial; $Op.Label = "$($Spec.Id)-toggle-3-initial"
                $Record.Restore = Invoke-E4A1UserTask -Name "$($Spec.Id)-toggle3" -Ops @([pscustomobject]$Op) -TimeoutSeconds 180
            }
            $S3 = Get-E4A1Snapshot -Roots $Roots
            $Record.RestoreDiff = @(Compare-E4A1Snapshot -Before $Base -After $S3)
            # Mechanizm = wartości stanu Target. Gdy przełącznik już był w Target, różnica „do Target” jest pusta - wtedy stan Target bierzemy
            # z drugiego kroku odwrotnie (Before z różnicy do stanu przeciwnego).
            $Mechanism = @(ConvertFrom-E4A1Diff -Diff $Record.ToTargetDiff)
            $OppositeMechanism = @(ConvertFrom-E4A1Diff -Diff $Record.ToOppositeDiff)
            if ($Mechanism.Count -eq 0 -and $OppositeMechanism.Count -gt 0) {
                $Reverse = @($Record.ToOppositeDiff | ForEach-Object { [pscustomobject]@{ Path = $_.Path; Name = $_.Name; Before = $_.After; After = $_.Before } })
                $Mechanism = @(ConvertFrom-E4A1Diff -Diff $Reverse)
            }
            $Record.Mechanism = $Mechanism
            $Record.OppositeMechanism = $OppositeMechanism
            $Out.Toggle = [pscustomobject]$Record
            # Zapis mechanizmem: najpierw stan przeciwny (żeby było widać przełączenie w UI), obserwacja, potem stan Target.
            if ($OppositeMechanism.Count -gt 0) {
                $OppositeApplied = @(Invoke-E4A1ApplyValues -Values $OppositeMechanism -UserSid $UserSid)
                $Out.Applied += $OppositeApplied
                $Out.ExplorerRestarts += Invoke-E4A1Explorer -Mode $Explorer
                $Out.Observations += Invoke-E4A1Observe -Spec $Spec -Phase 'mech-przeciwny'
            }
            $Values = $Mechanism
        }
        if ($Values.Count -gt 0) {
            $Applied = @(Invoke-E4A1ApplyValues -Values $Values -UserSid $UserSid)
            $Out.Applied += $Applied
        }
        if ($Spec.ContainsKey('Service')) {
            $Svc = $Spec.Service
            $Before = Get-Service -Name $Svc.Name -ErrorAction Stop
            $ServiceRecord = [ordered]@{ Name = $Svc.Name; BeforeStatus = [string]$Before.Status; BeforeStartType = [string]$Before.StartType; SetError = $null; AfterStatus = $null; AfterStartType = $null; UndoError = $null; UndoStatus = $null; UndoStartType = $null }
            try {
                Set-Service -Name $Svc.Name -StartupType $Svc.StartType -ErrorAction Stop
                if ($Svc.Stop) { Stop-Service -Name $Svc.Name -Force -ErrorAction Stop }
            }
            catch { $ServiceRecord.SetError = $_.Exception.Message }
            $After = Get-Service -Name $Svc.Name
            $ServiceRecord.AfterStatus = [string]$After.Status; $ServiceRecord.AfterStartType = [string]$After.StartType
            $Out.Service = $ServiceRecord
        }
        if ($Explorer -eq 'both') { $Out.Observations += Invoke-E4A1Observe -Spec $Spec -Phase 'po-bez-restartu' }
        $Out.ExplorerRestarts += Invoke-E4A1Explorer -Mode $Explorer
        $Out.Observations += Invoke-E4A1Observe -Spec $Spec -Phase 'po'
        $Out.ApplyDiff = @(Compare-E4A1Snapshot -Before $Base -After (Get-E4A1Snapshot -Roots $Roots))
    }
    catch { if ($_.Exception -isnot [System.OperationCanceledException]) { $Out.Errors += "zmiana/obserwacja: $($_.Exception.Message)" } }
    finally {
        if (-not $ObserveOnly) {
            try {
                $Out.Undo = @(Invoke-E4A1UndoValues -Applied @($Out.Applied))
                if ($null -ne $Out.Service) {
                    try {
                        Set-Service -Name $Out.Service.Name -StartupType $Out.Service.BeforeStartType -ErrorAction Stop
                        if ($Out.Service.BeforeStatus -eq 'Running') { Start-Service -Name $Out.Service.Name -ErrorAction Stop }
                    }
                    catch { $Out.Service.UndoError = $_.Exception.Message }
                    $Now = Get-Service -Name $Out.Service.Name
                    $Out.Service.UndoStatus = [string]$Now.Status; $Out.Service.UndoStartType = [string]$Now.StartType
                }
                $Out.ExplorerRestarts += Invoke-E4A1Explorer -Mode $Explorer
                $Out.Observations += Invoke-E4A1Observe -Spec $Spec -Phase 'cofniete'
                $Out.UndoDiff = @(Compare-E4A1Snapshot -Before $Base -After (Get-E4A1Snapshot -Roots $Roots))
            }
            catch { $Out.Errors += "cofnięcie: $($_.Exception.Message)" }
        }
        $Out.DurationMs = [int]$Watch.ElapsedMilliseconds
    }
    return [pscustomobject]$Out
}

function Invoke-E4A1RebootPhase {
    # Grupa reboot, po stronie VM: 'apply' = stan przed + obserwacja + zmiana (stan do pliku C:\dso\e4a1\reboot-state.json, przetrwa restart);
    # 'after' = obserwacja po restarcie + cofnięcie (bez restartu Eksploratora); 'undone' = obserwacja po drugim restarcie + różnica.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable[]]$Specs,
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][ValidateSet('apply', 'after', 'undone')][string]$Phase
    )
    Assert-E4A1InsideVm
    $StatePath = 'C:\dso\e4a1\reboot-state.json'
    $Records = @()
    if ($Phase -eq 'apply') {
        $null = New-Item -ItemType Directory -Path 'C:\dso\e4a1' -Force
        $State = @()
        foreach ($Spec in $Specs) {
            $Record = [ordered]@{ Id = $Spec.Id; Phase = $Phase; Observation = $null; Applied = @(); Service = $null; Error = $null }
            try {
                $Record.Observation = Invoke-E4A1Observe -Spec $Spec -Phase 'r-przed'
                if ($Spec.ContainsKey('Values')) { $Record.Applied = @(Invoke-E4A1ApplyValues -Values @($Spec.Values) -UserSid $UserSid) }
                if ($Spec.ContainsKey('Service')) {
                    $Before = Get-Service -Name $Spec.Service.Name -ErrorAction Stop
                    $Record.Service = [ordered]@{ Name = $Spec.Service.Name; BeforeStatus = [string]$Before.Status; BeforeStartType = [string]$Before.StartType }
                    Set-Service -Name $Spec.Service.Name -StartupType $Spec.Service.StartType -ErrorAction Stop
                    if ($Spec.Service.Stop) { Stop-Service -Name $Spec.Service.Name -Force -ErrorAction Stop }
                }
            }
            catch { $Record.Error = $_.Exception.Message }
            $State += [pscustomobject]@{ Id = $Spec.Id; Applied = $Record.Applied; Service = $Record.Service }
            $Records += [pscustomobject]$Record
        }
        Set-Content -LiteralPath $StatePath -Value (ConvertTo-Json -InputObject @($State) -Depth 8) -Encoding UTF8
        return $Records
    }
    $Parsed = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $Saved = @(foreach ($Item in $Parsed) { $Item })
    foreach ($Spec in $Specs) {
        $Record = [ordered]@{ Id = $Spec.Id; Phase = $Phase; Observation = $null; Undo = @(); Service = $null; Values = @(); Error = $null }
        try {
            $Label = if ($Phase -eq 'after') { 'r-po-restarcie' } else { 'r-cofniete-po-restarcie' }
            $Record.Observation = Invoke-E4A1Observe -Spec $Spec -Phase $Label
            if ($Spec.ContainsKey('Values')) { $Record.Values = @($Spec.Values | ForEach-Object { Read-E4A1Value -Path (Resolve-E4A1Path -Path $_.Path -UserSid $UserSid) -Name $_.Name }) }
            $Entry = @($Saved | Where-Object { $_.Id -eq $Spec.Id }) | Select-Object -First 1
            if ($Phase -eq 'after' -and $null -ne $Entry) {
                $Record.Undo = @(Invoke-E4A1UndoValues -Applied @($Entry.Applied))
                if ($null -ne $Entry.Service) {
                    Set-Service -Name $Entry.Service.Name -StartupType $Entry.Service.BeforeStartType -ErrorAction Stop
                    if ($Entry.Service.BeforeStatus -eq 'Running') { Start-Service -Name $Entry.Service.Name -ErrorAction Stop }
                }
            }
            if ($Spec.ContainsKey('Service')) { $S = Get-Service -Name $Spec.Service.Name; $Record.Service = "Status=$($S.Status) StartType=$($S.StartType)" }
        }
        catch { $Record.Error = $_.Exception.Message }
        $Records += [pscustomobject]$Record
    }
    return $Records
}

function Get-E4A1PlanFromJson {
    # Plan przekazany z hosta jako JSON -> lista hashtable (rekurencyjnie; tablice zostają tablicami).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Json)
    function ConvertTo-E4A1Hashtable($Node) {
        if ($null -eq $Node) { return $null }
        if ($Node -is [System.Management.Automation.PSCustomObject]) {
            $Table = @{}
            foreach ($Property in $Node.PSObject.Properties) { $Table[$Property.Name] = ConvertTo-E4A1Hashtable $Property.Value }
            return $Table
        }
        if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) { return , @($Node | ForEach-Object { ConvertTo-E4A1Hashtable $_ }) }
        return $Node
    }
    # ConvertFrom-Json w PowerShell 5.1 oddaje tablicę JSON jako JEDEN obiekt: przypisanie i foreach rozwija ją na elementy.
    $Parsed = $Json | ConvertFrom-Json
    $List = @()
    foreach ($Item in $Parsed) { $List += , (ConvertTo-E4A1Hashtable $Item) }
    return $List
}

function Invoke-E4A1Collection {
    # Punkt wejścia po stronie VM (jedna faza): zwraca JEDEN string JSON. Phase: main (pozycje po kolei) albo apply/after/undone (reboot).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][ValidateSet('main', 'apply', 'after', 'undone')][string]$Phase
    )
    Assert-E4A1InsideVm
    $Specs = @(Get-E4A1PlanFromJson -Json $PlanJson)
    $Document = [ordered]@{ meta = $null; userSid = $UserSid; phase = $Phase; inventory = $null; candidates = @() }
    try { $Document.meta = Get-ExploreMeta } catch { $Document.meta = [ordered]@{ Error = $_.Exception.Message } }
    try { $Document.inventory = Get-E4A1Inventory } catch { $Document.inventory = [ordered]@{ Error = $_.Exception.Message } }
    if ($Phase -eq 'main') {
        foreach ($Spec in $Specs) {
            try { $Document.candidates += Invoke-E4A1Candidate -Spec $Spec -UserSid $UserSid }
            catch { $Document.candidates += [pscustomobject]@{ Id = $Spec.Id; Errors = @("BŁĄD-SONDY: $($_.Exception.Message)") } }
        }
    }
    else { $Document.candidates = @(Invoke-E4A1RebootPhase -Specs $Specs -UserSid $UserSid -Phase $Phase) }
    return ($Document | ConvertTo-Json -Depth 14)
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-E4A1RemoteFunctionNames {
    return @(
        'Test-E4A1VmComputerName', 'Assert-E4A1InsideVm', 'Resolve-E4A1Path', 'Open-E4A1BaseKey', 'ConvertTo-E4A1SnapshotValue', 'Get-E4A1Snapshot',
        'Compare-E4A1Snapshot', 'Read-E4A1Value', 'Get-E4A1SnapshotRoots', 'Test-E4A1NoisyPath', 'Set-E4A1Value', 'Restore-E4A1Value', 'Get-E4A1ShellProcess', 'Restart-E4A1Explorer',
        'Get-E4A1K1Observation', 'Get-E4A1Inventory', 'Initialize-E4A1K2Native', 'Save-E4A1K2Shot', 'Get-E4A1K2SettingsWindow', 'Open-E4A1K2Settings',
        'Get-E4A1K2SettingsItems', 'Find-E4A1K2Toggle', 'Get-E4A1K2Settings', 'Set-E4A1K2Toggle', 'Close-E4A1K2Settings', 'Get-E4A1K2NewWindows',
        'Get-E4A1K2Search', 'Get-E4A1K2SearchHome', 'Get-E4A1K2Start', 'Get-E4A1K2Taskbar', 'Get-E4A1K2Widgets', 'Get-E4A1K2Copilot', 'Get-E4A1K2ContextMenu',
        'Get-E4A1K2TaskbarMenu', 'Invoke-E4A1K2UpdateCheck', 'Invoke-E4A1K2Ops', 'Get-E4A1ChildFunctionNames', 'New-E4A1ChildScript', 'Invoke-E4A1UserTask',
        'ConvertTo-E4A1Ops', 'Invoke-E4A1Observe', 'Invoke-E4A1Explorer', 'Invoke-E4A1ApplyValues', 'Invoke-E4A1UndoValues', 'ConvertFrom-E4A1Diff',
        'Invoke-E4A1Candidate', 'Invoke-E4A1RebootPhase', 'Get-E4A1PlanFromJson', 'Invoke-E4A1Collection'
    )
}

function Get-E4A1HostOnlyFunctionNames {
    return @('Get-E4A1Plan', 'Get-E4A1RemoteFunctionNames', 'Get-E4A1HostOnlyFunctionNames', 'Get-E4A1RemoteScript', 'Select-E4A1Specs', 'Assert-E4A1OutDirectory',
        'Get-E4A1RunArguments', 'Invoke-E4A1VmPhase', 'Restart-E4A1Vm', 'Complete-E4A1VmCleanup', 'Invoke-E4A1VmGroup', 'Invoke-E4A1Exploration')
}

function Get-E4A1RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje E4A1 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4A1RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Select-E4A1Specs {
    # Pozycje grupy (albo, dla reboot, pozycje z -RebootCandidate), z filtrem -Candidate. Nieznane Id = wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('ai', 'search', 'ads', 'telemetry', 'ux', 'reboot')][string]$Group,
        [AllowNull()][AllowEmptyCollection()][string[]]$Candidate,
        [AllowNull()][AllowEmptyCollection()][string[]]$RebootCandidate
    )
    $Plan = @(Get-E4A1Plan)
    $Known = @($Plan | ForEach-Object { $_.Id })
    foreach ($Id in @($Candidate) + @($RebootCandidate)) { if ($Id -and $Known -notcontains $Id) { throw "Nieznana pozycja '$Id' (znane: $($Known -join ', '))." } }
    if ($Group -eq 'reboot') {
        $Ids = @($RebootCandidate | Where-Object { $_ })
        if ($Ids.Count -eq 0) { $Ids = @('diagtrack', 'recall-policy', 'hide-recommended') }
        return @($Plan | Where-Object { $Ids -contains $_.Id })
    }
    $Selected = @($Plan | Where-Object { $_.Group -eq $Group })
    $Filter = @($Candidate | Where-Object { $_ })
    if ($Filter.Count -gt 0) { $Selected = @($Selected | Where-Object { $Filter -contains $_.Id }) }
    return $Selected
}

function Assert-E4A1OutDirectory {
    # -OutDirectory: wymagany, bezwzględny (C:\... albo \\serwer\udział\...), poza repo; w repo tylko katalog ignorowany przez git.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowEmptyString()][string]$Path,
        [Parameter(Mandatory)][string]$RepoRoot
    )
    if ([string]::IsNullOrWhiteSpace($Path)) { throw '-OutDirectory jest wymagany (ścieżka bezwzględna poza repo, np. $env:TEMP\dso-explore\exploration).' }
    if ($Path -notmatch '^[A-Za-z]:\\' -and $Path -notmatch '^\\\\[^\\]+\\[^\\]+') { throw "-OutDirectory musi być ścieżką bezwzględną (C:\... albo \\serwer\udział\...): '$Path'." }
    $Full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $Root = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
    $InRepo = ($Full -ieq $Root) -or $Full.StartsWith("$Root\", [System.StringComparison]::OrdinalIgnoreCase)
    if ($InRepo -and -not (Test-ExploreGitIgnored -Path $Full -RepoRoot $Root)) { throw "-OutDirectory '$Full' leży w repo ('$Root') poza katalogiem ignorowanym przez git; wyniki eksploracji trzymaj poza repo albo w katalogu ignorowanym przez git." }
    return $Full
}

function Get-E4A1RunArguments {
    # Argumenty przebiegu po walidacji: brak -Group = wszystkie grupy bez reboot (reboot tylko jawnie); -Candidate musi należeć do wybranych grup.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [AllowNull()][AllowEmptyCollection()][string[]]$Group,
        [AllowNull()][AllowEmptyCollection()][string[]]$Candidate,
        [AllowNull()][AllowEmptyCollection()][string[]]$RebootCandidate,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds,
        [bool]$Network = $true
    )
    $Order = @('ai', 'search', 'ads', 'telemetry', 'ux', 'reboot')
    $Selected = if (@($Group | Where-Object { $_ }).Count -gt 0) { @($Order | Where-Object { $Group -contains $_ }) } else { @($Order | Where-Object { $_ -ne 'reboot' }) }
    $Plan = [ordered]@{}
    foreach ($Name in $Selected) { $Plan[$Name] = @(Select-E4A1Specs -Group $Name -Candidate $Candidate -RebootCandidate $RebootCandidate | ForEach-Object { $_.Id }) }
    $Filter = @($Candidate | Where-Object { $_ })
    foreach ($Id in $Filter) {
        $Hit = $false
        foreach ($Name in $Selected) { if ($Name -ne 'reboot' -and @($Plan[$Name]) -contains $Id) { $Hit = $true } }
        if (-not $Hit) { throw "-Candidate '$Id' nie należy do wybranych grup ($($Selected -join ', '))." }
    }
    return [ordered]@{ VmName = @($VmName); OutDirectory = $OutDirectory; Groups = @($Selected); Plan = $Plan; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds; Network = $Network }
}

function Invoke-E4A1VmPhase {
    # Jedna faza w VM przez otwartą sesję: definicje + Invoke-E4A1Collection; zwraca sparsowany dokument.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$RemoteScript,
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][ValidateSet('main', 'apply', 'after', 'undone')][string]$Phase
    )
    Assert-DsoVmLiveAllowed
    $Remote = {
        param([string]$Definitions, [string]$Sid, [string]$Json, [string]$Which)
        . ([scriptblock]::Create($Definitions))
        Invoke-E4A1Collection -UserSid $Sid -PlanJson $Json -Phase $Which
    }
    $Raw = @(Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript, $UserSid, $PlanJson, $Phase))
    $Text = [string]($Raw | Where-Object { $_ -is [string] } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw "Faza '$Phase' nie zwróciła JSON." }
    return ($Text | ConvertFrom-Json)
}

function Restart-E4A1Vm {
    # Restart gościa (Restart-Computer w VM), czekanie na spadek heartbeat i nową sesję z zalogowanym dso. Zwraca nową sesję.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][pscredential]$Credential,
        [int]$ReadyTimeoutSeconds = 1200
    )
    Assert-DsoVmLiveAllowed
    Invoke-Command -Session $Session -ScriptBlock { Restart-Computer -Force } -ErrorAction SilentlyContinue
    Remove-PSSession -Session $Session -ErrorAction SilentlyContinue
    $null = Wait-VmHeartbeatDrop -VmName $VmName
    return (Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds)
}

function Complete-E4A1VmCleanup {
    # Koniec grupy: revert do checkpointu, odłączenie karty (gdy grupa ją podłączała) i sprawdzenie kontraktu stanu końcowego:
    # VM w stanie Off, a przy grupie z siecią karta bez przełącznika (SwitchName pusty). Każdy krok osobno w try/catch; nic nie jest
    # wyciszane - błędy i naruszenia wracają w Problems (Ok=false), a wołający zapisuje je w wyniku i zgłasza.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][bool]$Network
    )
    $Out = [ordered]@{ Ok = $false; RevertError = $null; DisconnectError = $null; CheckError = $null; FinalState = $null; FinalSwitch = $null; Violations = @(); Problems = @() }
    try { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName }
    catch { $Out.RevertError = $_.Exception.Message; $Out.Problems += "revert do '$CheckpointName': $($_.Exception.Message)" }
    if ($Network) {
        try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop }
        catch { $Out.DisconnectError = $_.Exception.Message; $Out.Problems += "odłączenie sieci: $($_.Exception.Message)" }
    }
    try {
        $Out.FinalState = [string](Get-VM -Name $VmName -ErrorAction Stop).State
        $Out.FinalSwitch = (@(Get-VMNetworkAdapter -VMName $VmName -ErrorAction Stop | ForEach-Object { $_.SwitchName } | Where-Object { $_ }) -join ', ')
        if ($Out.FinalState -ne 'Off') { $Out.Violations += "VM w stanie '$($Out.FinalState)' zamiast 'Off'" }
        if ($Network -and $Out.FinalSwitch) { $Out.Violations += "karta dalej podłączona do '$($Out.FinalSwitch)'" }
    }
    catch { $Out.CheckError = $_.Exception.Message; $Out.Problems += "odczyt stanu końcowego: $($_.Exception.Message)" }
    if (@($Out.Violations).Count -gt 0) { $Out.Problems += "stan końcowy: $(@($Out.Violations) -join '; ')" }
    $Out.Ok = (@($Out.Problems).Count -eq 0)
    return [pscustomobject]$Out
}

function Invoke-E4A1VmGroup {
    # Jedna grupa na jednej VM: revert -> (sieć) -> start -> gotowość -> faza main albo apply/restart/after/restart/undone -> kopia zrzutów ->
    # zapis JSON -> ZAWSZE revert i odłączenie sieci. Zwraca ścieżkę JSON.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][ValidateSet('ai', 'search', 'ads', 'telemetry', 'ux', 'reboot')][string]$Group,
        [Parameter(Mandatory)][hashtable[]]$Specs,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200,
        [bool]$Network = $true
    )
    Assert-DsoVmLiveAllowed
    $Credential = Get-DsoVmCredential -VmName $VmName
    $Session = $null
    $StartedAt = Get-Date
    $Phases = [ordered]@{}
    $PrimaryError = $null
    try {
        Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
        if ($Network) { Connect-VMNetworkAdapter -VMName $VmName -SwitchName 'Default Switch' -ErrorAction Stop }
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        $UserSid = [string](Invoke-Command -Session $Session -ScriptBlock { (Get-LocalUser -Name 'dso').SID.Value })
        if ([string]::IsNullOrWhiteSpace($UserSid)) { throw "Nie udało się odczytać SID użytkownika 'dso' w VM '$VmName'." }
        # Chwila na dokończenie logowania dso (pasek zadań, widżety, wyszukiwanie) przed pierwszą obserwacją.
        Start-Sleep -Seconds 45
        $RemoteScript = Get-E4A1RemoteScript
        $PlanJson = ConvertTo-Json -InputObject @($Specs) -Depth 8
        if ($Group -ne 'reboot') {
            $Phases['main'] = Invoke-E4A1VmPhase -Session $Session -RemoteScript $RemoteScript -UserSid $UserSid -PlanJson $PlanJson -Phase 'main'
        }
        else {
            $Phases['apply'] = Invoke-E4A1VmPhase -Session $Session -RemoteScript $RemoteScript -UserSid $UserSid -PlanJson $PlanJson -Phase 'apply'
            $Session = Restart-E4A1Vm -VmName $VmName -Session $Session -Credential $Credential -ReadyTimeoutSeconds $ReadyTimeoutSeconds
            Start-Sleep -Seconds 45
            $Phases['after'] = Invoke-E4A1VmPhase -Session $Session -RemoteScript $RemoteScript -UserSid $UserSid -PlanJson $PlanJson -Phase 'after'
            $Session = Restart-E4A1Vm -VmName $VmName -Session $Session -Credential $Credential -ReadyTimeoutSeconds $ReadyTimeoutSeconds
            Start-Sleep -Seconds 45
            $Phases['undone'] = Invoke-E4A1VmPhase -Session $Session -RemoteScript $RemoteScript -UserSid $UserSid -PlanJson $PlanJson -Phase 'undone'
        }
    }
    catch {
        $PrimaryError = $_.Exception.Message
        if (Test-VmAuthFailure -Exception $_.Exception) { throw }
    }
    finally {
        $Shots = Join-Path (Join-Path $OutDirectory $VmName) "shots-$Group"
        if ($null -ne $Session) {
            try {
                $null = New-Item -ItemType Directory -Path $Shots -Force
                Copy-Item -FromSession $Session -Path 'C:\dso\e4a1\shots\*.png' -Destination $Shots -ErrorAction Stop
            }
            catch { $PrimaryError = (@($PrimaryError, "kopia zrzutów: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
            Remove-PSSession -Session $Session -ErrorAction SilentlyContinue
        }
        # Sprzątanie PRZED zapisem, żeby jego wynik (także błędy) trafił do JSON; błąd sprzątania nigdy nie jest ukrywany.
        $Cleanup = Complete-E4A1VmCleanup -VmName $VmName -CheckpointName $CheckpointName -Network $Network
        if (-not $Cleanup.Ok) {
            $CleanupMessage = "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            Write-Warning "VM '$VmName', grupa '$Group': $CleanupMessage"
            $PrimaryError = (@($PrimaryError, $CleanupMessage) | Where-Object { $_ }) -join ' | '
        }
        $Result = [ordered]@{ schema = 1; vmName = $VmName; group = $Group; checkpoint = $CheckpointName; network = $Network; startedAt = $StartedAt.ToString('o')
            finishedAt = (Get-Date).ToString('o'); error = $PrimaryError; cleanup = $Cleanup; candidates = @($Specs | ForEach-Object { $_.Id }); shots = $Shots; phases = $Phases }
        $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName "e4a1-$Group.json"
    }
    if ($PrimaryError) { throw "VM '$VmName', grupa '$Group': $PrimaryError (wynik częściowy: $Saved)" }
    return $Saved
}

function Invoke-E4A1Exploration {
    # Wszystkie wybrane grupy na wszystkich VM. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Arguments,
        [AllowNull()][AllowEmptyCollection()][string[]]$Candidate,
        [AllowNull()][AllowEmptyCollection()][string[]]$RebootCandidate
    )
    $Paths = New-Object System.Collections.Generic.List[string]
    $Errors = New-Object System.Collections.Generic.List[string]
    foreach ($Vm in $Arguments.VmName) {
        foreach ($Name in $Arguments.Groups) {
            try {
                $Specs = @(Select-E4A1Specs -Group $Name -Candidate $Candidate -RebootCandidate $RebootCandidate)
                if ($Specs.Count -eq 0) { continue }
                Write-Host "$(Get-Date -Format 'HH:mm:ss') $Vm / $Name ($(@($Specs | ForEach-Object { $_.Id }) -join ', '))"
                $Paths.Add((Invoke-E4A1VmGroup -VmName $Vm -Group $Name -Specs $Specs -OutDirectory $Arguments.OutDirectory -CheckpointName $Arguments.CheckpointName `
                            -ReadyTimeoutSeconds $Arguments.ReadyTimeoutSeconds -Network $Arguments.Network))
            }
            catch {
                if (Test-VmAuthFailure -Exception $_.Exception) { throw }
                $Errors.Add("VM '$Vm', grupa '$Name': $($_.Exception.Message)")
            }
        }
    }
    return [pscustomobject]@{ Paths = $Paths.ToArray(); Errors = $Errors.ToArray() }
}

# Przy dot-sourcingu (testy) tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    $ExitCode = 1
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1')
        $OutDirectory = Assert-E4A1OutDirectory -Path $OutDirectory -RepoRoot (Get-DsoRepoRoot)
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        $RunArguments = Get-E4A1RunArguments -VmName $VmName -OutDirectory $OutDirectory -Group $Group -Candidate $Candidate -RebootCandidate $RebootCandidate `
            -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -Network (-not $NoNetwork)
        if ($ShowPlan) {
            [pscustomobject]$RunArguments | ConvertTo-Json -Depth 4
            exit 0
        }
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon (zaimportowany wyżej) trzyma referencję do starej instancji VmHost; ponowny import po nich wiąże go z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        Write-Host "Wyniki: $OutDirectory"
        Write-Host "Grupy: $($RunArguments.Groups -join ', '); VM: $($RunArguments.VmName -join ', '); sieć: $($RunArguments.Network)"
        $Outcome = Invoke-E4A1Exploration -Arguments $RunArguments -Candidate $Candidate -RebootCandidate $RebootCandidate
        foreach ($Path in $Outcome.Paths) { Write-Host "Zapisano: $Path" }
        foreach ($Message in $Outcome.Errors) { Write-Host "BŁĄD $Message" -ForegroundColor Red }
        $ExitCode = if (@($Outcome.Errors).Count -eq 0) { 0 } else { 1 }
    }
    catch {
        Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
        $ExitCode = 1
    }
    exit $ExitCode
}
