<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Jedna VM na proces; najwyżej dwa procesy (dwie VM) naraz.
    Rozpoznanie (identyfikatory przełączników Ustawień, Opcje folderów, Edge, Paint i Notatnik; tylko obserwacja):
        .\tests\vm\explore\Invoke-B5Exploration.ps1 -VmName DSO-EN-Home -Group discover -OutDirectory $env:TEMP\dso-explore\b5
        .\tests\vm\explore\Invoke-B5Exploration.ps1 -VmName DSO-EN-Pro -Group discover -OutDirectory $env:TEMP\dso-explore\b5
    Grupy kandydatów (bez -Group: ads, telemetry, edge, apps, copilot; każda grupa od czystego checkpointu):
        .\tests\vm\explore\Invoke-B5Exploration.ps1 -VmName DSO-EN-Home -OutDirectory $env:TEMP\dso-explore\b5
        .\tests\vm\explore\Invoke-B5Exploration.ps1 -VmName DSO-EN-Pro -OutDirectory $env:TEMP\dso-explore\b5
    Ponowienie jednej sondy (filtr po Id z Get-B5Plan):
        .\tests\vm\explore\Invoke-B5Exploration.ps1 -VmName DSO-EN-Home -Group telemetry -Candidate online-speech -OutDirectory $env:TEMP\dso-explore\b5
    Grupa reboot (zmiana -> restart VM -> obserwacja -> cofnięcie -> restart -> obserwacja), pozycje z dowolnej grupy:
        .\tests\vm\explore\Invoke-B5Exploration.ps1 -VmName DSO-EN-Home -Group reboot -RebootCandidate start-account-notifications,activity-history -OutDirectory $env:TEMP\dso-explore\b5
    Brzmienia polskie (tylko obserwacja):
        .\tests\vm\explore\Invoke-B5Exploration.ps1 -VmName DSO-PL-Pro -Group wording -OutDirectory $env:TEMP\dso-explore\b5
        .\tests\vm\explore\Invoke-B5Exploration.ps1 -VmName DSO-PL-Home -Group wording -Candidate wording-taskbar -OutDirectory $env:TEMP\dso-explore\b5
    -NoNetwork: żadna grupa nie dostaje sieci. Bez niego sieć ('Default Switch') mają tylko grupy discover, ads, edge, apps i wording
    (Get-B5GroupNetwork); telemetry, copilot i reboot działają z odłączoną kartą.
    -ShowPlan: wypisuje rozstrzygnięte argumenty, listę pozycji grup i sieć grup (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
Opis działania:
    Eksploracja nowych pozycji prywatności (reklamy, telemetria, AI) według zasady dowodu: liczy się zaobserwowany skutek (Ustawienia,
    Opcje folderów, Edge, aplikacja, pasek zadań, Start, dyktowanie), nie sam zapis wartości. Wszystko dzieje się WYŁĄCZNIE w VM DSO;
    na hoście skrypt tylko podłącza albo odłącza kartę sieciową VM (Default Switch) i zapisuje wyniki.
    Każda grupa to osobny przebieg od checkpointu 'czysta-26H2': revert -> sieć (podłączona albo odłączona wg grupy) -> start -> gotowość ->
    sondy -> kopia zrzutów -> ZAWSZE (finally) revert, odłączenie sieci i sprawdzenie stanu końcowego (VM Off, karta bez przełącznika)
    przed zapisem JSON; błąd albo naruszenie trafia do pól cleanup i error wyniku oraz kończy grupę błędem (nic nie jest wyciszane).
    Grupy: discover i wording = same obserwacje (identyfikatory, nazwy i stany przełączników stron Ustawień, drzewo Opcji folderów,
    strony Edge, przyciski Painta i Notatnika, pasek zadań); ads, telemetry, edge, apps, copilot = pozycje-kandydaci; reboot = wybrane
    pozycje z restartem VM.
    Konteksty w VM:
      K1 - PowerShell Direct jako wbudowany Administrator: rejestr HKLM i HKU\<SID dso> (.NET RegistryKey, widok 64-bit), restart
           Eksploratora (Stop-Process powłoki dso, AutoRestartShell), migawki rejestru i ich różnice, odczyty pakietów (appx:) i wersji
           plików (file:).
      K2 - sesja interaktywna 'dso': zadanie Harmonogramu (principal dso / Interactive / Highest) uruchamia skrypt z operacjami UI:
           Settings (strona ms-settings:, rozwinięcie każdej sekcji z ExpandCollapsePattern, AutomationId, nazwa i stan przełączników),
           Toggle (przełącznik po AutomationId, TogglePattern), Start, Taskbar (przyciski paska), FolderOptions (control.exe /name
           Microsoft.FolderOptions, zakładka o indeksie 1 przez PSM_SETCURSEL, stan pola z drzewa ustawień zaawansowanych przez MSAA
           tylko do odczytu, zamknięcie bez
           zapisu), Edge (msedge.exe --no-first-run, klik tylko przycisków ekranu powitalnego, monity i banery tylko zapisane - nazwy i zrzut, bez
           kliknięcia, bo są obserwowane; nawigacja klawiaturą, drzewo UIA okna i przyciski ramki, zamknięcie), App (Paint albo Notatnik
           przez alias aplikacji: start, czy żyje po 10 s, przyciski i menu) i Dictation (Win+H, nowe okna). Zrzut ekranu:
           Graphics.CopyFromScreen w sesji dso, PNG w C:\dso\b5\shots, kopiowane na host przed revertem.
    Przebieg pozycji (Invoke-B5Candidate): stan przed (wartości, migawka rejestru, obserwacje) -> przełącznik Ustawień, jeśli pozycja go ma
    (wymaga AutomationId z grupy discover: Target, przeciwny, powrót, różnice rejestru jako dowód mechanizmu; bez wartości pozycji
    różnica jest zapisywana mechanizmem) -> zapis wartości pozycji przez .NET (jak API programu) -> obserwacja po (Explorer=both: przed
    i po restarcie Eksploratora) -> cofnięcie (wartości sprzed, klucze utworzone przez sondę usunięte) -> obserwacja po cofnięciu.
    Rekord pozycji: Before, ToggleDiff, After, AfterRestart, Undone, Errors (i szczegóły: Applied, ApplyDiff, Undo, UndoDiff).
    Ostatni bezpiecznik po stronie VM: Assert-B5InsideVm (nazwa komputera jednej z czterech VM DSO i brak DSO_VM_TESTS_MOCK_ONLY) na
    początku każdej funkcji ze skutkiem. Błąd jednej pozycji trafia do wyniku i nie przerywa pozostałych. Pierwszy błąd uwierzytelnienia
    przerywa cały skrypt (blokada konta). Skrypt dotyka wyłącznie VM DSO z ValidateSet, nigdy obcej VM; polityk spoza sondy nie ustawia.
    Wynik: <OutDirectory>\<VmName>\b5-<grupa>.json (UTF-8) i zrzuty <OutDirectory>\<VmName>\shots-<grupa>\*.png.
    Kod wyjścia: 0 gdy wszystkie wybrane grupy na wszystkich VM zebrane bez błędu przebiegu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro', 'DSO-PL-Home')][string[]]$VmName = @('DSO-EN-Home', 'DSO-EN-Pro'),
    [string]$OutDirectory,
    [ValidateSet('discover', 'ads', 'telemetry', 'edge', 'apps', 'copilot', 'wording', 'reboot')][string[]]$Group,
    [ValidatePattern('^[a-z0-9-]+\z')][string[]]$Candidate,
    [ValidatePattern('^[a-z0-9-]+\z')][string[]]$RebootCandidate,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$NoNetwork,
    [switch]$ShowPlan
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Plan (dane): pozycje-kandydaci i sondy dodatkowe; wartości dosłownie z listy kandydatów paczki, ścieżki HKCU\ jako HKU\{SID}\
# ({SID} = SID konta 'dso', wstawiany w VM). Explorer: none | restart | both.
# Observe: operacje K2 (Settings, Start, Taskbar, FolderOptions, Edge, App, Dictation).
# K1: odczyty K1 (processes:<nazwy>, appx:<wzorzec>, file:<ścieżka>).
# Toggle: przełącznik Ustawień {Uri, Match (angielskie wyrażenie nazwy), Id (AutomationId z grupy discover), Target On|Off}.
# ---------------------------------------------------------------------------------------------------------------------

function Get-B5Plan {
    [CmdletBinding()]
    param()
    $Adv = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    $Cdm = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
    $Toasts = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
    $Edge = 'HKLM\SOFTWARE\Policies\Microsoft\Edge'
    $Paint = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint'
    $EdgeFile = 'file:%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe'
    $Pages = @('ms-settings:lockscreen', 'ms-settings:personalization-start', 'ms-settings:notifications', 'ms-settings:', 'ms-settings:privacy', 'ms-settings:privacy-general',
        'ms-settings:privacy-speech', 'ms-settings:privacy-activityhistory', 'ms-settings:privacy-speechtyping', 'ms-settings:privacy-feedback')
    $Page = @{}
    foreach ($Uri in $Pages) { $Page[$Uri] = @{ Op = 'Settings'; Uri = $Uri; ExpandAll = $true } }
    $AllPages = @($Pages | ForEach-Object { $Page[$_] })
    $FolderOptions = @{ Op = 'FolderOptions'; Match = 'sync provider' }
    $EdgeAll = @{ Op = 'Edge'; Urls = @('edge://settings', 'edge://policy', 'edge://newtab') }
    $Apps = @(@{ Op = 'App'; Exe = 'mspaint.exe' }, @{ Op = 'App'; Exe = 'notepad.exe' })
    $AppsK1 = @('appx:Microsoft.Paint', 'appx:Microsoft.WindowsNotepad')
    $CopilotEdge = @(
        @{ Path = $Edge; Name = 'CopilotPageContext'; Kind = 'DWord'; Value = 0 }
        @{ Path = $Edge; Name = 'EdgeEntraCopilotPageContext'; Kind = 'DWord'; Value = 0 }
        @{ Path = $Edge; Name = 'Microsoft365CopilotChatIconEnabled'; Kind = 'DWord'; Value = 0 }
        @{ Path = $Edge; Name = 'NewTabPageBingChatEnabled'; Kind = 'DWord'; Value = 0 }
        @{ Path = $Edge; Name = 'EdgeHistoryAISearchEnabled'; Kind = 'DWord'; Value = 0 }
        @{ Path = $Edge; Name = 'ComposeInlineEnabled'; Kind = 'DWord'; Value = 0 }
    )
    return @(
        # --- discover: rozpoznanie (identyfikatory i stany, bez zmian) ---
        @{ Id = 'discover-settings'; Group = 'discover'; Explorer = 'none'; ObserveOnly = $true; Observe = $AllPages }
        @{ Id = 'discover-folderoptions'; Group = 'discover'; Explorer = 'none'; ObserveOnly = $true; Observe = @($FolderOptions) }
        @{ Id = 'discover-edge'; Group = 'discover'; Explorer = 'none'; ObserveOnly = $true; K1 = @($EdgeFile); Observe = @($EdgeAll) }
        @{ Id = 'discover-apps'; Group = 'discover'; Explorer = 'none'; ObserveOnly = $true; K1 = $AppsK1; Observe = $Apps }
        # --- ads ---
        @{ Id = 'explorer-sync-provider-notifications'; Group = 'ads'; Explorer = 'both'
            Values = @(@{ Path = $Adv; Name = 'ShowSyncProviderNotifications'; Kind = 'DWord'; Value = 0 })
            Observe = @($FolderOptions) }
        # Bez przełącznika: rozpoznanie na VM nie znalazło pola „fun facts” na stronie ekranu blokady (Windows spotlight, personalizacja
        # zablokowana bez aktywacji), więc sonda zapisuje same wartości i obserwuje stronę.
        @{ Id = 'lockscreen-tips'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = $Cdm; Name = 'RotatingLockScreenOverlayEnabled'; Kind = 'DWord'; Value = 0 }, @{ Path = $Cdm; Name = 'SubscribedContent-338387Enabled'; Kind = 'DWord'; Value = 0 })
            Observe = @($Page['ms-settings:lockscreen']) }
        # Zależność od istniejącej pozycji privacy.spotlight: czy sama polityka Spotlight zmienia pole z pozycji lockscreen-tips.
        @{ Id = 'lockscreen-tips-spotlight'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = 'HKU\{SID}\Software\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsSpotlightFeatures'; Kind = 'DWord'; Value = 1 })
            Observe = @($Page['ms-settings:lockscreen']) }
        # Sondy dodatkowe wiersza 2: która z dwóch wartości przełącza listę „Personalize your lock screen” ze Spotlight na Picture.
        @{ Id = 'lockscreen-tips-overlay-only'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = $Cdm; Name = 'RotatingLockScreenOverlayEnabled'; Kind = 'DWord'; Value = 0 })
            Observe = @($Page['ms-settings:lockscreen']) }
        @{ Id = 'lockscreen-tips-338387-only'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = $Cdm; Name = 'SubscribedContent-338387Enabled'; Kind = 'DWord'; Value = 0 })
            Observe = @($Page['ms-settings:lockscreen']) }
        @{ Id = 'start-account-notifications'; Group = 'ads'; Explorer = 'both'
            Values = @(@{ Path = $Adv; Name = 'Start_AccountNotifications'; Kind = 'DWord'; Value = 0 })
            Toggle = @{ Uri = 'ms-settings:personalization-start'; Match = 'account-related notifications'; Id = 'SystemSettings_Start_AccountNotifications_ToggleSwitch'; Target = 'Off' }
            Observe = @($Page['ms-settings:personalization-start'], @{ Op = 'Start' }) }
        @{ Id = 'toast-suggested'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = "$Toasts\Windows.SystemToast.Suggested"; Name = 'Enabled'; Kind = 'DWord'; Value = 0 })
            Observe = @($Page['ms-settings:notifications']) }
        @{ Id = 'toast-backup-reminder'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = "$Toasts\Windows.SystemToast.BackupReminder"; Name = 'Enabled'; Kind = 'DWord'; Value = 0 })
            Observe = @($Page['ms-settings:notifications']) }
        @{ Id = 'settings-365-ads'; Group = 'ads'; Explorer = 'none'
            Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableConsumerAccountStateContent'; Kind = 'DWord'; Value = 1 })
            Observe = @($Page['ms-settings:']) }
        # Sonda dodatkowa: czy przełącznik sugestii w Ustawieniach zapisuje więcej niż istniejąca pozycja (różnica rejestru).
        @{ Id = 'settings-suggestions-recheck'; Group = 'ads'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:privacy-general'; Match = 'Recommendations and offers in Settings'; Id = 'SystemSettings_Privacy_EnableSuggestionsInSettings_ToggleSwitch'; Target = 'Off' }
            Observe = @($Page['ms-settings:privacy-general']) }
        # --- telemetry ---
        @{ Id = 'start-track-progs'; Group = 'telemetry'; Explorer = 'none'
            Values = @(@{ Path = $Adv; Name = 'Start_TrackProgs'; Kind = 'DWord'; Value = 0 })
            Toggle = @{ Uri = 'ms-settings:privacy-general'; Match = 'Improve Start and search results'; Id = 'SystemSettings_Privacy_StoreAppUsage_ToggleSwitch'; Target = 'Off' }
            Observe = @($Page['ms-settings:privacy-general'], $Page['ms-settings:personalization-start']) }
        @{ Id = 'online-speech'; Group = 'telemetry'; Explorer = 'none'
            Values = @(@{ Path = 'HKU\{SID}\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy'; Name = 'HasAccepted'; Kind = 'DWord'; Value = 0 })
            Toggle = @{ Uri = 'ms-settings:privacy-speech'; Match = 'Online speech'; Id = 'SystemSettings_Privacy_Speech_AllowSpeechServices_ToggleSwitch'; Target = 'Off' }
            Observe = @($Page['ms-settings:privacy-speech'], @{ Op = 'Dictation' }) }
        # Sonda dodatkowa: w obrazie VM przełącznik jest już Off (HasAccepted=0), więc zapis pozycji niczego nie zmienia. Bez wartości, z
        # Target On: mechanizm z różnicy (HasAccepted) zapisany wprost ma przestawić przełącznik (stan po zapisie mechanizmu = On).
        @{ Id = 'online-speech-reverse'; Group = 'telemetry'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:privacy-speech'; Match = 'Online speech'; Id = 'SystemSettings_Privacy_Speech_AllowSpeechServices_ToggleSwitch'; Target = 'On' }
            Observe = @($Page['ms-settings:privacy-speech']) }
        @{ Id = 'activity-history'; Group = 'telemetry'; Explorer = 'none'
            Values = @(
                @{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System'; Name = 'EnableActivityFeed'; Kind = 'DWord'; Value = 0 }
                @{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System'; Name = 'PublishUserActivities'; Kind = 'DWord'; Value = 0 }
                @{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System'; Name = 'UploadUserActivities'; Kind = 'DWord'; Value = 0 })
            Observe = @($Page['ms-settings:privacy-activityhistory'], $Page['ms-settings:privacy']) }
        @{ Id = 'inking-typing-improve'; Group = 'telemetry'; Explorer = 'none'
            Values = @(
                @{ Path = 'HKU\{SID}\Software\Microsoft\Input\TIPC'; Name = 'Enabled'; Kind = 'DWord'; Value = 0 }
                @{ Path = 'HKU\{SID}\Software\Microsoft\InputPersonalization'; Name = 'RestrictImplicitInkCollection'; Kind = 'DWord'; Value = 1 }
                @{ Path = 'HKU\{SID}\Software\Microsoft\InputPersonalization'; Name = 'RestrictImplicitTextCollection'; Kind = 'DWord'; Value = 1 }
                @{ Path = 'HKU\{SID}\Software\Microsoft\InputPersonalization\TrainedDataStore'; Name = 'HarvestContacts'; Kind = 'DWord'; Value = 0 }
                @{ Path = 'HKU\{SID}\Software\Microsoft\Personalization\Settings'; Name = 'AcceptedPrivacyPolicy'; Kind = 'DWord'; Value = 0 })
            Toggle = @{ Uri = 'ms-settings:privacy-speechtyping'; Match = 'inking and typing'; Id = 'SystemSettings_Privacy_PersonalizeInkType_Toggle_ToggleSwitch'; Target = 'Off' }
            Observe = @($Page['ms-settings:privacy-speechtyping'], $Page['ms-settings:privacy-feedback']) }
        # Sonda dodatkowa: czy przy DoNotShowFeedbackNotifications=1 znika lista częstotliwości opinii.
        @{ Id = 'feedback-frequency-recheck'; Group = 'telemetry'; Explorer = 'none'
            Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'DoNotShowFeedbackNotifications'; Kind = 'DWord'; Value = 1 })
            Observe = @($Page['ms-settings:privacy-feedback']) }
        # --- edge (polityki HKLM\SOFTWARE\Policies\Microsoft\Edge) ---
        @{ Id = 'edge-shopping-recommendations'; Group = 'edge'; Explorer = 'none'; K1 = @($EdgeFile)
            Values = @(
                @{ Path = $Edge; Name = 'EdgeShoppingAssistantEnabled'; Kind = 'DWord'; Value = 0 }
                @{ Path = $Edge; Name = 'ShowRecommendationsEnabled'; Kind = 'DWord'; Value = 0 }
                @{ Path = $Edge; Name = 'SpotlightExperiencesAndRecommendationsEnabled'; Kind = 'DWord'; Value = 0 })
            Observe = @(@{ Op = 'Edge'; Urls = @('edge://settings', 'edge://settings/?search=shopping', 'edge://newtab', 'edge://policy') }) }
        @{ Id = 'edge-newtab-content'; Group = 'edge'; Explorer = 'none'; K1 = @($EdgeFile)
            Values = @(@{ Path = $Edge; Name = 'NewTabPageContentEnabled'; Kind = 'DWord'; Value = 0 })
            Observe = @(@{ Op = 'Edge'; Urls = @('edge://newtab', 'edge://policy') }) }
        @{ Id = 'edge-newtab-topsites'; Group = 'edge'; Explorer = 'none'; K1 = @($EdgeFile)
            Values = @(@{ Path = $Edge; Name = 'NewTabPageHideDefaultTopSites'; Kind = 'DWord'; Value = 1 })
            Observe = @(@{ Op = 'Edge'; Urls = @('edge://newtab', 'edge://policy') }) }
        @{ Id = 'edge-prompts-default-browser'; Group = 'edge'; Explorer = 'none'; K1 = @($EdgeFile)
            Values = @(
                @{ Path = $Edge; Name = 'DefaultBrowserSettingEnabled'; Kind = 'DWord'; Value = 0 }
                @{ Path = $Edge; Name = 'DefaultBrowserSettingsCampaignEnabled'; Kind = 'DWord'; Value = 0 })
            Observe = @(@{ Op = 'Edge'; Urls = @('edge://settings/defaultBrowser', 'edge://policy') }) }
        @{ Id = 'edge-diagnostic-data'; Group = 'edge'; Explorer = 'none'; K1 = @($EdgeFile)
            Values = @(
                @{ Path = $Edge; Name = 'DiagnosticData'; Kind = 'DWord'; Value = 0 }
                @{ Path = $Edge; Name = 'PersonalizationReportingEnabled'; Kind = 'DWord'; Value = 0 })
            Observe = @(@{ Op = 'Edge'; Urls = @('edge://settings/privacy', 'edge://settings/?search=diagnostic', 'edge://settings/?search=personalization', 'edge://policy') }) }
        @{ Id = 'edge-user-feedback'; Group = 'edge'; Explorer = 'none'; K1 = @($EdgeFile)
            Values = @(@{ Path = $Edge; Name = 'UserFeedbackAllowed'; Kind = 'DWord'; Value = 0 })
            Observe = @(@{ Op = 'Edge'; Urls = @('edge://settings/?search=feedback', 'edge://policy') }) }
        @{ Id = 'edge-copilot'; Group = 'edge'; Explorer = 'none'; K1 = @($EdgeFile)
            Values = @(@(@{ Path = $Edge; Name = 'HubsSidebarEnabled'; Kind = 'DWord'; Value = 0 }) + $CopilotEdge)
            Observe = @(@{ Op = 'Edge'; Urls = @('edge://newtab', 'edge://policy') }) }
        @{ Id = 'edge-copilot-no-sidebar'; Group = 'edge'; Explorer = 'none'; K1 = @($EdgeFile)
            Values = $CopilotEdge
            Observe = @(@{ Op = 'Edge'; Urls = @('edge://newtab', 'edge://policy') }) }
        # --- apps ---
        @{ Id = 'paint-ai'; Group = 'apps'; Explorer = 'none'; K1 = @('appx:Microsoft.Paint')
            Values = @(
                @{ Path = $Paint; Name = 'DisableCocreator'; Kind = 'DWord'; Value = 1 }
                @{ Path = $Paint; Name = 'DisableGenerativeFill'; Kind = 'DWord'; Value = 1 }
                @{ Path = $Paint; Name = 'DisableImageCreator'; Kind = 'DWord'; Value = 1 })
            Observe = @(@{ Op = 'App'; Exe = 'mspaint.exe' }) }
        @{ Id = 'notepad-ai'; Group = 'apps'; Explorer = 'none'; K1 = @('appx:Microsoft.WindowsNotepad')
            Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\WindowsNotepad'; Name = 'DisableAIFeatures'; Kind = 'DWord'; Value = 1 })
            Observe = @(@{ Op = 'App'; Exe = 'notepad.exe' }) }
        # --- copilot ---
        @{ Id = 'copilot-taskbar-button'; Group = 'copilot'; Explorer = 'both'; K1 = @('appx:Copilot')
            Values = @(@{ Path = $Adv; Name = 'ShowCopilotButton'; Kind = 'DWord'; Value = 0 })
            Observe = @(@{ Op = 'Taskbar' }) }
        # --- wording: brzmienia (tylko obserwacja) ---
        @{ Id = 'wording-settings'; Group = 'wording'; Explorer = 'none'; ObserveOnly = $true; Observe = $AllPages }
        @{ Id = 'wording-folderoptions'; Group = 'wording'; Explorer = 'none'; ObserveOnly = $true; Observe = @($FolderOptions) }
        @{ Id = 'wording-edge'; Group = 'wording'; Explorer = 'none'; ObserveOnly = $true; Observe = @($EdgeAll) }
        @{ Id = 'wording-apps'; Group = 'wording'; Explorer = 'none'; ObserveOnly = $true; Observe = $Apps }
        @{ Id = 'wording-taskbar'; Group = 'wording'; Explorer = 'none'; ObserveOnly = $true; K1 = @('appx:Copilot'); Observe = @(@{ Op = 'Taskbar' }) }
    )
}

function Get-B5GroupNetwork {
    # Sieć (Default Switch) w grupie: Edge, nowa karta, ekran blokady, Paint i Notatnik potrzebują sieci; telemetria i przycisk Copilot nie.
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)][ValidateSet('discover', 'ads', 'telemetry', 'edge', 'apps', 'copilot', 'wording', 'reboot')][string]$Group)
    return (@('discover', 'ads', 'edge', 'apps', 'wording') -contains $Group)
}

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# ---------------------------------------------------------------------------------------------------------------------

function Test-B5VmComputerName {
    # Nazwy komputerów VM nadawane przez New-TestVm.ps1 (VmBuild: nazwa VM wielkimi literami).
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$ComputerName)
    return (@('DSO-EN-HOME', 'DSO-EN-PRO', 'DSO-PL-PRO', 'DSO-PL-HOME') -contains ([string]$ComputerName).ToUpperInvariant())
}

function Assert-B5InsideVm {
    # Ostatni bezpiecznik: sondy B5 zapisują rejestr HKLM/HKU, restartują Eksploratora, uruchamiają programy i klikają w UI, więc działają
    # WYŁĄCZNIE w VM DSO i nigdy w testach na mockach (DSO_VM_TESTS_MOCK_ONLY z tests\vm\tests\Support\NoLiveVm.ps1).
    [CmdletBinding()]
    param([string]$ComputerName = $env:COMPUTERNAME)
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: sondy B5 nie mogą działać w testach na mockach (nic nie zostało uruchomione).'
    }
    if (-not (Test-B5VmComputerName -ComputerName $ComputerName)) {
        throw "Sondy B5 działają tylko w VM DSO (DSO-EN-HOME, DSO-EN-PRO, DSO-PL-PRO, DSO-PL-HOME) (ten komputer: '$ComputerName'); nic nie zostało uruchomione."
    }
}

function Resolve-B5Path {
    # {SID} -> SID konta dso; tylko HKLM\ i HKU\ (jak Open-ExploreRegistryKey).
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$UserSid
    )
    $Resolved = $Path.Replace('{SID}', $UserSid)
    if ($Resolved -notmatch '^(HKLM|HKU)\\') { throw "Resolve-B5Path: dozwolone tylko 'HKLM\' i 'HKU\' (podano '$Path')." }
    return $Resolved
}

function Open-B5BaseKey {
    # Gałąź (HKLM albo HKU, widok 64-bit) i ścieżka względna.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if ($Path -match '^HKLM\\(.+)$') { return @([Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64'), $Matches[1]) }
    if ($Path -match '^HKU\\(.+)$') { return @([Microsoft.Win32.RegistryKey]::OpenBaseKey('Users', 'Registry64'), $Matches[1]) }
    throw "Open-B5BaseKey: nieobsługiwana ścieżka '$Path'."
}

function ConvertTo-B5SnapshotValue {
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

function Get-B5Snapshot {
    # Migawka drzew rejestru: słownik 'ścieżka|nazwa' -> 'Kind:wartość' oraz 'ścieżka|' -> '(klucz)' dla każdego klucza.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Roots,
        [int]$MaxDepth = 12
    )
    $Map = @{}
    foreach ($Root in $Roots) {
        $Pair = Open-B5BaseKey -Path $Root
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
                            $Map["$Name|$ValueName"] = ConvertTo-B5SnapshotValue -Raw $Raw -Kind ([string]$Current.GetValueKind($ValueName))
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

function Compare-B5Snapshot {
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

function Read-B5Value {
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

function Get-B5SnapshotRoots {
    # Korzenie migawek: obejmują klucze wszystkich pozycji planu (polityki HKLM i użytkownika, CurrentVersion, mowa, pisanie, personalizacja).
    [CmdletBinding()]
    [OutputType([string[]])]
    param([Parameter(Mandatory)][string]$UserSid)
    return @(
        'HKLM\SOFTWARE\Policies',
        'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies',
        "HKU\$UserSid\Software\Policies",
        "HKU\$UserSid\Software\Microsoft\Windows\CurrentVersion",
        "HKU\$UserSid\Software\Microsoft\Siuf",
        "HKU\$UserSid\Software\Microsoft\Speech_OneCore",
        "HKU\$UserSid\Software\Microsoft\Input",
        "HKU\$UserSid\Software\Microsoft\InputPersonalization",
        "HKU\$UserSid\Software\Microsoft\Personalization"
    )
}

function Test-B5NoisyPath {
    # Wpisy, które zmieniają się same (liczniki użycia, historia, bufory powłoki, znaczniki czasu): nie są przenoszone jako „mechanizm”
    # z różnicy przełącznika.
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][string]$Path,
        [AllowEmptyString()][string]$Name = ''
    )
    if ($Path -match '\\(UserAssist|RecentDocs|BagMRU|Bags|SessionInfo|FeatureUsage|CloudStore|TaskManager|MuiCache|ComDlg32|StartupApproved|ActivityDataModel|ShellNoRoam|MountPoints2|WSX|AppListBackup|PushNotifications|Notifications\\Data|ApplicationAssociationToasts)(\\|$)') { return $true }
    if ($Path -match '\\GameConfigStore\\Children(\\|$)|\\ContentDeliveryManager\\(Health|Subscriptions|CreativeEvents|Renderers|Cache|SuggestedApps)(\\|$)|\\CurrentVersion\\Dsh$') { return $true }
    if ($Path -match '\\CurrentVersion\\Start$' -and $Name -eq 'Config') { return $true }
    return [bool]($Name -match '^(LogonCount|GlobalAssocChangedCounter|InstalledPackagedAppsRevision|WebProviderLastNotificationBehaviorTimestamp|LastAppBackupListenerStartTime|packOpenedCount|packLoadedSuccessfullyCount)$')
}

function Set-B5Value {
    # Zapis jednej wartości przez .NET (jak API C# programu). Wynik: {Path, Name, Ok, Error, CreatedKeys} - CreatedKeys do cofnięcia.
    # Kind 'Delete' = usunięcie wartości.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name,
        [Parameter(Mandatory)][ValidateSet('DWord', 'String', 'ExpandString', 'QWord', 'Delete')][string]$Kind,
        $Value
    )
    Assert-B5InsideVm
    $Result = [ordered]@{ Path = $Path; Name = $Name; Kind = $Kind; Value = $Value; Ok = $false; Error = $null; CreatedKeys = @() }
    $Pair = Open-B5BaseKey -Path $Path
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

function Restore-B5Value {
    # Cofnięcie do stanu sprzed (rekord Read-B5Value): wartość odtworzona albo usunięta; klucze utworzone przez sondę usunięte
    # (od najgłębszego, tylko puste).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name,
        [Parameter(Mandatory)]$Before,
        [AllowEmptyCollection()][string[]]$CreatedKeys = @()
    )
    Assert-B5InsideVm
    $Result = [ordered]@{ Path = $Path; Name = $Name; Ok = $false; Error = $null; RemovedKeys = @() }
    try {
        if ($Before.Exists) {
            $Kind = [string]$Before.Kind
            if ($Kind -notin @('DWord', 'String', 'ExpandString', 'QWord')) { throw "Nieobsługiwany typ do odtworzenia: $Kind" }
            $Write = Set-B5Value -Path $Path -Name $Name -Kind $Kind -Value $Before.Value
            if (-not $Write.Ok) { throw $Write.Error }
        }
        else {
            $Delete = Set-B5Value -Path $Path -Name $Name -Kind 'Delete'
            if (-not $Delete.Ok) { throw $Delete.Error }
        }
        foreach ($KeyPath in ($CreatedKeys | Sort-Object { $_.Length } -Descending)) {
            $Pair = Open-B5BaseKey -Path $KeyPath
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

function Get-B5ShellProcess {
    # Procesy powłoki konta dso (explorer.exe) z właścicielem.
    [CmdletBinding()]
    param([ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso')
    $All = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction Stop)
    foreach ($Process in $All) {
        $Owner = Invoke-CimMethod -InputObject $Process -MethodName GetOwner -ErrorAction SilentlyContinue
        if ($null -ne $Owner -and $Owner.User -eq $UserName) { [pscustomobject]@{ ProcessId = [int]$Process.ProcessId; SessionId = [int]$Process.SessionId } }
    }
}

function Restart-B5Explorer {
    # Restart powłoki dso: Stop-Process -Force (AutoRestartShell uruchamia nową) + zatrzymanie hostów Start/wyszukiwania (wczytają stan od nowa).
    [CmdletBinding()]
    param([ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso')
    Assert-B5InsideVm
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $Old = @(Get-B5ShellProcess -UserName $UserName | ForEach-Object { $_.ProcessId })
    $Out = [ordered]@{ OldPids = $Old; NewPids = @(); WaitedMs = $null; Hosts = @(); Error = $null }
    try {
        foreach ($HostName in 'SearchHost', 'StartMenuExperienceHost') {
            foreach ($P in @(Get-Process -Name $HostName -ErrorAction SilentlyContinue)) { try { Stop-Process -Id $P.Id -Force -ErrorAction Stop; $Out.Hosts += "$HostName $($P.Id)" } catch { $Out.Hosts += "$HostName $($P.Id): $($_.Exception.Message)" } }
        }
        foreach ($ProcessId in $Old) { Stop-Process -Id $ProcessId -Force -ErrorAction Stop }
        for ($Poll = 0; $Poll -lt 60; $Poll++) {
            Start-Sleep -Milliseconds 500
            $New = @(Get-B5ShellProcess -UserName $UserName | ForEach-Object { $_.ProcessId } | Where-Object { $Old -notcontains $_ })
            if ($New.Count -gt 0) { $Out.NewPids = $New; break }
        }
        # AutoRestartShell nie zadziałał w 30 s: tylko zapis błędu (bez awaryjnego startu powłoki).
        if (@($Out.NewPids).Count -eq 0) { $Out.Error = 'nowy explorer.exe nie pojawił się w 30 s' }
        Start-Sleep -Seconds 8
    }
    catch { $Out.Error = $_.Exception.Message }
    $Out.WaitedMs = [int]$Watch.ElapsedMilliseconds
    return [pscustomobject]$Out
}

function Get-B5K1Observation {
    # Odczyty K1: processes:<a,b>, appx:<fragment nazwy pakietu> (nazwa i wersja), file:<ścieżka> (wersja pliku; zmienne %X% rozwijane).
    [CmdletBinding()]
    param([AllowEmptyCollection()][string[]]$Spec = @())
    $Out = @()
    foreach ($Item in $Spec) {
        $Kind, $Arg = $Item -split ':', 2
        $Record = [ordered]@{ Spec = $Item; Data = $null; Error = $null }
        try {
            switch ($Kind) {
                'processes' { $Record.Data = @(Get-Process -Name ($Arg -split ',') -ErrorAction SilentlyContinue | ForEach-Object { "$($_.ProcessName):$($_.Id):sesja $($_.SessionId)" }) }
                'appx' { $Record.Data = @(Get-AppxPackage -AllUsers -ErrorAction Stop | Where-Object { $_.Name -like "*$Arg*" } | ForEach-Object { "$($_.Name) $($_.Version)" }) }
                'file' {
                    $Path = [Environment]::ExpandEnvironmentVariables($Arg)
                    $Record.Data = if (Test-Path -LiteralPath $Path -PathType Leaf) { [string](Get-Item -LiteralPath $Path).VersionInfo.FileVersion } else { '(brak pliku)' }
                }
                default { throw "Nieznany odczyt K1 '$Item'." }
            }
        }
        catch { $Record.Error = $_.Exception.Message }
        $Out += [pscustomobject]$Record
    }
    return $Out
}

function Get-B5Inventory {
    # Stan wyjściowy systemu (tylko odczyt): pakiety AI, Paint, Notatnik, Copilot i Edge, wersja Edge, aktywacja, region (GeoId) i sieć.
    [CmdletBinding()]
    param()
    $Out = [ordered]@{}
    try { $Out.Appx = @(Get-AppxPackage -AllUsers -ErrorAction Stop | Where-Object { $_.Name -match 'Copilot|Paint|WindowsNotepad|MicrosoftEdge|WebExperience|CoreAI|AIX|Photon' } | ForEach-Object { "$($_.Name) $($_.Version)" }) } catch { $Out.Appx = "błąd: $($_.Exception.Message)" }
    $Out.Edge = @(Get-B5K1Observation -Spec @('file:%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe'))[0].Data
    try { $Out.Activation = @(Get-CimInstance -ClassName SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'" -ErrorAction Stop | ForEach-Object { "$($_.Name): LicenseStatus=$($_.LicenseStatus)" }) } catch { $Out.Activation = "błąd: $($_.Exception.Message)" }
    try { $Out.GeoId = (Get-WinHomeLocation -ErrorAction Stop).GeoId } catch { $Out.GeoId = "błąd: $($_.Exception.Message)" }
    try { $Out.Network = @(Get-NetConnectionProfile -ErrorAction Stop | ForEach-Object { "$($_.InterfaceAlias): $($_.IPv4Connectivity)" }) } catch { $Out.Network = "błąd: $($_.Exception.Message)" }
    return [pscustomobject]$Out
}

# --- K2: operacje UI w sesji dso (kopiowane do skryptu zadania; Add-Type tylko tam) ---

function Initialize-B5K2Native {
    [CmdletBinding()]
    param()
    if ('B5User32' -as [type]) { return }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    # Kontrolki Win32 Opcji folderów (zakładki, drzewo ustawień zaawansowanych) są w UIA tego procesu zwykłymi 'Pane' bez TabItem
    # i TogglePattern (rozpoznanie na VM; rejestracja pośredników klienta UIA rzuca NullReferenceException), więc zakładka przez
    # PSM_SETCURSEL, a elementy drzewa przez MSAA (oleacc, IAccessible): nazwa i stan.
    Add-Type -ReferencedAssemblies 'Accessibility' -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class B5User32 {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("oleacc.dll")] public static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    public static IntPtr ChildByClass(IntPtr parent, string cls) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, (h, l) => {
            var c = new StringBuilder(256); GetClassName(h, c, 256);
            if (c.ToString() == cls && IsWindowVisible(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static List<string> AccItems(IntPtr h, int max) {
        var r = new List<string>();
        Guid g = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        object o;
        if (AccessibleObjectFromWindow(h, 0xFFFFFFFC, ref g, out o) != 0 || o == null) { return r; }
        var a = (Accessibility.IAccessible)o;
        int n = a.accChildCount;
        for (int i = 1; i <= n && i <= max; i++) {
            try { r.Add(a.get_accName(i) + "|" + Convert.ToInt32(a.get_accState(i))); } catch { }
        }
        return r;
    }
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    public static void Press(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(50); keybd_event(vk, 0, 2, UIntPtr.Zero); }
    public static void Combo(byte a, byte b) {
        keybd_event(a, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(50);
        keybd_event(b, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(50);
        keybd_event(b, 0, 2, UIntPtr.Zero); keybd_event(a, 0, 2, UIntPtr.Zero);
    }
    public static List<string> Visible() {
        var r = new List<string>();
        EnumWindows((h, l) => {
            if (IsWindowVisible(h)) {
                var c = new StringBuilder(256); var t = new StringBuilder(256);
                GetClassName(h, c, 256); GetWindowText(h, t, 256);
                r.Add(t.ToString() + " | " + c.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return r;
    }
}
'@
}

function Save-B5K2Shot {
    # Zrzut pulpitu sesji dso do C:\dso\b5\shots\<Label>.png; zwraca nazwę pliku.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Label)
    $Directory = 'C:\dso\b5\shots'
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

function Get-B5K2Lines {
    # Linie 'ControlType|Name|IsEnabled|Toggle' elementów z nazwą w drzewie UIA elementu (opcjonalnie tylko wskazane typy), najwyżej Max.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Element,
        [int]$Max = 400,
        [AllowEmptyCollection()][string[]]$Types = @()
    )
    $Lines = New-Object System.Collections.Generic.List[string]
    foreach ($Item in $Element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($Lines.Count -ge $Max) { break }
        try {
            $C = $Item.Current
            if (-not $C.Name) { continue }
            $Type = $C.ControlType.ProgrammaticName -replace '^ControlType\.', ''
            if ($Types.Count -gt 0 -and $Types -notcontains $Type) { continue }
            $Toggle = ''
            $Pattern = $null
            if ($Item.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$Pattern)) { $Toggle = [string]$Pattern.Current.ToggleState }
            $Lines.Add("$Type|$($C.Name)|$($C.IsEnabled)|$Toggle")
        }
        catch { $null = $_ }
    }
    return @($Lines)
}

function Get-B5K2SettingsWindow {
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

function Find-B5K2Scroll {
    # Pionowo przewijalny obszar strony Ustawień (bez listy menu po lewej) albo $null.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Window)
    $Condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)
    foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $Condition)) {
        $Pattern = $Element.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($Pattern.Current.VerticallyScrollable -and $Element.Current.AutomationId -ne 'MenuItemsScrollViewer') { return $Pattern }
    }
    return $null
}

function Open-B5K2Settings {
    # Otwiera stronę Ustawień (ms-settings:..., samo 'ms-settings:' = strona główna), maksymalizuje okno; z -ExpandAll rozwija każdy
    # element z ExpandCollapsePattern na stronie (bez list rozwijanych i menu; tylko widok, bez zmiany ustawień). Zwraca okno albo $null.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^ms-settings:[a-z0-9-]*\z')][string]$Uri,
        [switch]$ExpandAll
    )
    Start-Process $Uri
    $Window = $null
    for ($Poll = 0; $Poll -lt 20 -and $null -eq $Window; $Poll++) { Start-Sleep -Milliseconds 500; $Window = Get-B5K2SettingsWindow }
    if ($null -eq $Window) { return $null }
    Start-Sleep -Seconds 3
    $Pattern = $null
    if ($Window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$Pattern)) { try { $Pattern.SetWindowVisualState('Maximized') } catch { $null = $_ } }
    Start-Sleep -Milliseconds 800
    if (-not $ExpandAll) { return $Window }
    # Lista sekcji jest wirtualizowana: rozwijanie na kolejnych pozycjach przewinięcia, potem powrót na górę.
    $Scroll = Find-B5K2Scroll -Window $Window
    $Expandable = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsExpandCollapsePatternAvailableProperty, $true)
    $Skip = @([System.Windows.Automation.ControlType]::ComboBox, [System.Windows.Automation.ControlType]::MenuItem, [System.Windows.Automation.ControlType]::SplitButton)
    $Expanded = 0
    foreach ($Percent in -1, 25, 50, 75, 100) {
        if ($Percent -ge 0) {
            if ($null -eq $Scroll) { break }
            try { $Scroll.SetScrollPercent(-1, $Percent) } catch { $null = $_ }
            Start-Sleep -Milliseconds 700
        }
        foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $Expandable)) {
            if ($Expanded -ge 60) { break }
            try {
                if ($Skip -contains $Element.Current.ControlType) { continue }
                $Expander = $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
                if ([string]$Expander.Current.ExpandCollapseState -eq 'Collapsed') { $Expander.Expand(); $Expanded++; Start-Sleep -Milliseconds 500 }
            }
            catch { $null = $_ }
        }
    }
    if ($null -ne $Scroll) { try { $Scroll.SetScrollPercent(-1, 0) } catch { $null = $_ }; Start-Sleep -Milliseconds 500 }
    return $Window
}

function Get-B5K2SettingsItems {
    # Elementy strony (z przewijaniem, bo lista jest wirtualizowana): wszystkie przełączniki (AutomationId, nazwa, stan), listy, AutomationId
    # SystemSettings_* i teksty o zarządzaniu albo o Microsoft 365/Copilot.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Window)
    $Map = @{}
    $Collect = {
        foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            try {
                $C = $Element.Current
                $Toggle = $null; $Value = $null; $Pattern = $null
                if ($Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$Pattern)) { $Toggle = [string]$Pattern.Current.ToggleState }
                if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern, [ref]$Pattern)) { $Value = (@($Pattern.Current.GetSelection() | ForEach-Object { $_.Current.Name }) -join '; ') }
                $Interesting = ($C.AutomationId -like 'SystemSettings_*') -or ($null -ne $Toggle) -or ($C.ControlType.ProgrammaticName -eq 'ControlType.ComboBox') -or
                ($C.Name -match '(?i)organiz|managed|administrator|zarz.dza|Aktywuj|Activate|Microsoft 365|Copilot|Microsoft account|Back up|Game Pass|Activity history|Historia aktywno|konto Microsoft|kopi|^Speech$|^Mowa$|Diagnostics|Diagnostyka|Searching Windows')
                if (-not $Interesting) { continue }
                $Key = "$($C.AutomationId)|$($C.Name)|$($C.ControlType.ProgrammaticName)"
                $Map[$Key] = [pscustomobject]@{ Id = $C.AutomationId; Name = $C.Name; Type = ($C.ControlType.ProgrammaticName -replace '^ControlType\.', ''); Enabled = $C.IsEnabled; Toggle = $Toggle; Value = $Value }
            }
            catch { $null = $_ }
        }
    }
    & $Collect
    $Scroll = Find-B5K2Scroll -Window $Window
    if ($null -ne $Scroll) {
        foreach ($Percent in 25, 50, 75, 100) { try { $Scroll.SetScrollPercent(-1, $Percent) } catch { $null = $_ }; Start-Sleep -Milliseconds 600; & $Collect }
        try { $Scroll.SetScrollPercent(-1, 0) } catch { $null = $_ }
    }
    return @($Map.Values | Sort-Object Id, Name)
}

function Find-B5K2Toggle {
    # Przełącznik po AutomationId; przewija, aż element się zmaterializuje.
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Window, [Parameter(Mandatory)][string]$Id)
    $Find = {
        foreach ($Element in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            $Pattern = $null
            if (-not $Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$Pattern)) { continue }
            if ($Element.Current.AutomationId -eq $Id) { return $Element }
        }
        return $null
    }
    $Found = & $Find
    if ($null -ne $Found) { return $Found }
    $Scroll = Find-B5K2Scroll -Window $Window
    if ($null -eq $Scroll) { return $null }
    foreach ($Percent in 20, 40, 60, 80, 100) {
        try { $Scroll.SetScrollPercent(-1, $Percent) } catch { $null = $_ }
        Start-Sleep -Milliseconds 600
        $Found = & $Find
        if ($null -ne $Found) { return $Found }
    }
    return $null
}

function Get-B5K2Settings {
    # Operacja Settings: elementy strony + zrzut.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Uri,
        [switch]$ExpandAll,
        [Parameter(Mandatory)][string]$Label
    )
    $Out = [ordered]@{ Op = 'Settings'; Uri = $Uri; Found = $false; Items = @(); Shot = $null; Error = $null }
    try {
        # Otwarte okno Ustawień nie zmienia strony po samym 'ms-settings:' (strona główna): każde otwarcie od zamkniętego okna.
        $Out.Error = Close-B5K2Settings
        $Window = Open-B5K2Settings -Uri $Uri -ExpandAll:$ExpandAll
        if ($null -ne $Window) {
            $Out.Found = $true
            $Out.Items = @(Get-B5K2SettingsItems -Window $Window)
            $Out.Shot = Save-B5K2Shot -Label $Label
        }
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Set-B5K2Toggle {
    # Operacja Toggle: przełącza przełącznik (po AutomationId) do stanu Want (TogglePattern, jak klik użytkownika); stan przed/po i zrzut.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Uri,
        [AllowEmptyString()][string]$Id,
        [Parameter(Mandatory)][ValidateSet('On', 'Off')][string]$Want,
        [Parameter(Mandatory)][string]$Label
    )
    $Out = [ordered]@{ Op = 'Toggle'; Uri = $Uri; Id = $Id; Want = $Want; Found = $false; Name = $null; Enabled = $null; Before = $null; After = $null; Shot = $null; Error = $null }
    try {
        if (-not $Id) { throw 'brak AutomationId – uruchom discover' }
        $Window = Open-B5K2Settings -Uri $Uri -ExpandAll
        if ($null -eq $Window) { throw "okno Ustawień nie pojawiło się ($Uri)" }
        $Element = Find-B5K2Toggle -Window $Window -Id $Id
        if ($null -eq $Element) { throw "nie znaleziono przełącznika (Id='$Id')" }
        $Out.Found = $true
        $Out.Name = $Element.Current.Name
        $Out.Enabled = $Element.Current.IsEnabled
        $Pattern = $Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $Out.Before = [string]$Pattern.Current.ToggleState
        for ($Attempt = 0; $Attempt -lt 2 -and [string]$Pattern.Current.ToggleState -ne $Want; $Attempt++) { $Pattern.Toggle(); Start-Sleep -Seconds 2 }
        $Out.After = [string]$Pattern.Current.ToggleState
        $Out.Shot = Save-B5K2Shot -Label $Label
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Close-B5K2Settings {
    # Zamyka okno Ustawień (WindowPattern.Close), żeby kolejne otwarcie czytało stan od nowa, i czeka (do 10 s), aż okno zniknie.
    # Zwraca $null albo opis błędu (okno zostało, Close() rzucił, brak WindowPattern): wołający zapisuje go w Error, nic nie jest połykane.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Window = Get-B5K2SettingsWindow
    if ($null -eq $Window) { return $null }
    $Problems = @()
    $Pattern = $null
    if ($Window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$Pattern)) {
        try { $Pattern.Close() } catch { $Problems += "Close(): $($_.Exception.Message)" }
    }
    else { $Problems += 'okno bez WindowPattern' }
    if (-not (Wait-B5Until -TimeoutMs 10000 -IntervalMs 250 -Probe { $null -eq (Get-B5K2SettingsWindow) })) { $Problems += 'okno nie zamknęło się w 10 s' }
    if ($Problems.Count -eq 0) { return $null }
    return "zamykanie Ustawień: $($Problems -join ' | ')"
}

function Get-B5K2NewWindows {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Before)
    return @([B5User32]::Visible() | Where-Object { $Before -notcontains $_ })
}

function Get-B5K2Start {
    # Operacja Start: klawisz Win, 4 s, zrzut (sekcja polecanych, przypięte, konto), Esc.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    $Out = [ordered]@{ Op = 'Start'; Shot = $null; Error = $null }
    try {
        $Out.Error = Close-B5K2Settings
        [B5User32]::Press(0x5B); Start-Sleep -Seconds 4
        $Out.Shot = Save-B5K2Shot -Label $Label
        [B5User32]::Press(0x1B); Start-Sleep -Seconds 1
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-B5K2Taskbar {
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
        $Out.Shot = Save-B5K2Shot -Label $Label
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Get-B5K2FolderOptions {
    # Operacja FolderOptions: control.exe /name Microsoft.FolderOptions, zakładka o indeksie 1 (Widok, PSM_SETCURSEL), stan pola
    # pasującego do Match z drzewa ustawień zaawansowanych (MSAA, tylko odczyt), elementy drzewa 'nazwa|stan' (≤ 200), zrzut po
    # przewinięciu drzewa na dół; zamknięcie przez WindowPattern.Close() (jak „Anuluj”, bez zapisu).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Match,
        [Parameter(Mandatory)][string]$Label
    )
    Assert-B5InsideVm
    $Out = [ordered]@{ Op = 'FolderOptions'; Window = $null; Found = $false; Name = $null; Toggle = 'Unknown'; Names = @(); Shot = $null; Error = $null }
    $Window = $null
    try {
        $AE = [System.Windows.Automation.AutomationElement]
        $Scope = [System.Windows.Automation.TreeScope]
        $Dialog = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, '#32770')
        $Before = @($AE::RootElement.FindAll($Scope::Children, $Dialog) | ForEach-Object { $_.Current.NativeWindowHandle })
        Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\control.exe') -ArgumentList '/name', 'Microsoft.FolderOptions'
        for ($Poll = 0; $Poll -lt 30 -and $null -eq $Window; $Poll++) {
            Start-Sleep -Milliseconds 500
            $Window = @($AE::RootElement.FindAll($Scope::Children, $Dialog) | Where-Object { $Before -notcontains $_.Current.NativeWindowHandle }) | Select-Object -First 1
        }
        if ($null -eq $Window) { throw 'okno Opcji folderów nie pojawiło się w 15 s' }
        $Out.Window = "$($Window.Current.Name)|$($Window.Current.ClassName)"
        # Kontrolki Win32 tego okna są w UIA zwykłymi 'Pane' (rozpoznanie na VM): zakładka o indeksie 1 przez PSM_SETCURSEL (0x465),
        # drzewo SysTreeView32 przez MSAA (nazwa|stan elementu, STATE_SYSTEM_CHECKED = 0x10), przewinięcie na dół (WM_VSCROLL, SB_BOTTOM)
        # przed zrzutem, bo pole 'sync provider' jest pod koniec listy. Okno pojawia się przed stronami: odczyty do skutku (do 10 s).
        $Handle = [IntPtr]$Window.Current.NativeWindowHandle
        $null = Wait-B5Until -TimeoutMs 10000 -Probe { [B5User32]::ChildByClass($Handle, 'SysTabControl32') -ne [IntPtr]::Zero }
        $null = [B5User32]::SendMessage($Handle, 0x465, [IntPtr]1, [IntPtr]::Zero)
        $null = Wait-B5Until -TimeoutMs 10000 -Probe { [B5User32]::ChildByClass($Handle, 'SysTreeView32') -ne [IntPtr]::Zero }
        $Tree = [B5User32]::ChildByClass($Handle, 'SysTreeView32')
        if ($Tree -eq [IntPtr]::Zero) {
            # Diagnostyka: drzewo UIA okna i zrzut zostają w wyniku.
            $Out.Names = @(Get-B5K2Lines -Element $Window -Max 80)
            $Out.Shot = Save-B5K2Shot -Label $Label
            throw "brak drzewa ustawień zaawansowanych na zakładce o indeksie 1 (okno: $($Out.Window))"
        }
        $Items = @([B5User32]::AccItems($Tree, 300))
        $Out.Names = @($Items | Select-Object -First 200)
        $Checks = @($Items | Where-Object { ([int](($_ -split '\|')[-1]) -band 0x10) -ne 0 }).Count
        foreach ($Item in $Items) {
            $Parts = $Item -split '\|'
            $Name = ($Parts[0..($Parts.Count - 2)] -join '|')
            if ($Name -notmatch $Match) { continue }
            $Out.Found = $true
            $Out.Name = $Name
            # Stan tylko, gdy MSAA w ogóle raportuje zaznaczenia (część pól jest domyślnie zaznaczona); inaczej 'Unknown' (zostaje zrzut).
            if ($Checks -gt 0) { $Out.Toggle = if (([int]$Parts[-1] -band 0x10) -ne 0) { 'On' } else { 'Off' } }
            break
        }
        $null = [B5User32]::SendMessage($Tree, 0x115, [IntPtr]7, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 500
        $Out.Shot = Save-B5K2Shot -Label $Label
    }
    catch { $Out.Error = $_.Exception.Message }
    finally {
        if ($null -ne $Window) {
            $Pattern = $null
            if ($Window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$Pattern)) { try { $Pattern.Close() } catch { $null = $_ } }
            Start-Sleep -Seconds 1
        }
    }
    return [pscustomobject]$Out
}

function Wait-B5Until {
    # Powtarza sondę co IntervalMs, aż zwróci $true albo minie TimeoutMs; zwraca wynik ostatniej sondy.
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][scriptblock]$Probe,
        [ValidateRange(0, 600000)][int]$TimeoutMs = 10000,
        [ValidateRange(10, 60000)][int]$IntervalMs = 500
    )
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $Ok = [bool](& $Probe)
        if ($Ok -or $Watch.ElapsedMilliseconds -ge $TimeoutMs) { return $Ok }
        Start-Sleep -Milliseconds $IntervalMs
    }
}

function Get-B5K2EdgeWindows {
    # Wszystkie okna najwyższego poziomu procesów msedge (klasa Chrome_WidgetWin_1), także bez tytułu. Rozpoznanie na VM: ekran powitalny NIE jest
    # osobnym oknem w UIA (jest tylko okno przeglądarki, a przycisków ekranu nie ma w drzewie), dlatego Edge startuje z --no-first-run;
    # lista okien zostaje jako diagnostyka (pole Windows) i jako zapas dla klików ekranu powitalnego.
    [CmdletBinding()]
    param()
    $AE = [System.Windows.Automation.AutomationElement]
    $Ids = @(Get-Process -Name msedge -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $Condition = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Chrome_WidgetWin_1')
    return @($AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $Condition) | Where-Object { $Ids -contains $_.Current.ProcessId })
}

function Get-B5K2EdgeWindow {
    # Okno przeglądarki Edge (klasa Chrome_WidgetWin_1 procesu msedge, z tytułem) albo $null.
    [CmdletBinding()]
    param()
    $AE = [System.Windows.Automation.AutomationElement]
    $Ids = @(Get-Process -Name msedge -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $Condition = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Chrome_WidgetWin_1')
    foreach ($Window in $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $Condition)) {
        if ($Ids -contains $Window.Current.ProcessId -and $Window.Current.Name) { return $Window }
    }
    return $null
}

function Get-B5K2FrameButtons {
    # Nazwy przycisków ramki okna (pasek narzędzi, pasek boczny), bez treści stron (gałęzie ControlType.Document są pomijane).
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Window)
    $Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $Types = @([System.Windows.Automation.ControlType]::Button, [System.Windows.Automation.ControlType]::SplitButton, [System.Windows.Automation.ControlType]::MenuItem)
    $Out = New-Object System.Collections.Generic.List[string]
    $Stack = New-Object System.Collections.Stack
    $Stack.Push(@($Window, 0))
    while ($Stack.Count -gt 0 -and $Out.Count -lt 200) {
        $Pair = $Stack.Pop()
        $Child = $Walker.GetFirstChild($Pair[0])
        while ($null -ne $Child) {
            try {
                $C = $Child.Current
                if ($C.ControlType -ne [System.Windows.Automation.ControlType]::Document) {
                    if ($Types -contains $C.ControlType -and $C.Name) { $Out.Add($C.Name) }
                    if ($Pair[1] -lt 30) { $Stack.Push(@($Child, ($Pair[1] + 1))) }
                }
            }
            catch { $null = $_ }
            $Child = $Walker.GetNextSibling($Child)
        }
    }
    return @($Out)
}

function Select-B5K2EdgeFirstRunButton {
    # Przycisk ekranu powitalnego (pierwsze uruchomienie Edge) do kliknięcia albo $null. Ekran jest rozpoznany tylko wtedy, gdy okno ma
    # przycisk o pełnej nazwie z tej listy (przejście dalej bez logowania i bez danych, EN i PL); takie przyciski są tylko na ekranie
    # powitalnym. Ogólne „Not now”, „Got it”, „No, thanks”, „Skip” i ich polskie odpowiedniki NIE są na liście: mają je też monity
    # przeglądarki domyślnej, zakupów i Copilota, które sonda obserwuje, a odrzucenie monitu zostaje w profilu (fałszywy dowód).
    [CmdletBinding()]
    [OutputType([string])]
    param([AllowEmptyCollection()][string[]]$Names = @())
    $Welcome = '^(Confirm and browse|Confirm and continue|Continue without signing in|Continue without this data|Start without your data|Potwierdź i przeglądaj|Potwierdź i kontynuuj|Kontynuuj bez logowania|Kontynuuj bez tych danych|Rozpocznij bez danych)$'
    foreach ($Name in $Names) { if ([string]$Name -match $Welcome) { return [string]$Name } }
    return $null
}

function Test-B5K2EdgePromptButton {
    # Przycisk monitu albo banera (odrzucenie, potwierdzenie, przeglądarka domyślna): tylko zapis nazwy i zrzut, nigdy klik. Bez „Close”
    # i „Zamknij”: ma je zawsze ramka okna, więc zrzut monitu powstawałby przy każdym starcie.
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][string]$Name)
    return [bool]($Name -match '^(Not now|Got it|No, thanks|No thanks|Skip|Maybe later|Dismiss|Set as default|Make default|Nie teraz|Rozumiem|Nie, dziękuję|Pomiń|Może później|Odrzuć|Ustaw jako domyślną|Ustaw jako domyślne)$')
}

function Get-B5K2Edge {
    # Operacja Edge: msedge.exe z Program Files (x86), ekran powitalny przez przyciski UIA wybrane przez Select-B5K2EdgeFirstRunButton (każdy
    # krok ze zrzutem), monity i banery tylko zapisane (Prompts: przyciski + zrzut, bez kliknięcia), każdy adres przez Ctrl+L + tekst +
    # Enter i 6 s, drzewo UIA okna (≤ 400 linii) i zrzut na stronę, przyciski ramki; na końcu CloseMainWindow, do 15 s czekania
    # i Stop-Process pozostałych procesów msedge konta. Polityk nie ustawia.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^(edge://|https://)')][string[]]$Urls,
        [Parameter(Mandatory)][string]$Label,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-B5InsideVm
    $Out = [ordered]@{ Op = 'Edge'; Version = $null; Windows = @(); NewWindows = @(); FirstRun = @(); Prompts = @(); Toolbar = @(); Pages = @(); Error = $null }
    $Exe = Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'
    $Started = $false
    try {
        if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "brak pliku $Exe" }
        $Out.Version = [string](Get-Item -LiteralPath $Exe).VersionInfo.FileVersion
        $VisibleBefore = @([B5User32]::Visible())
        # --no-first-run (przełącznik Chromium, nie polityka): ekran powitalny jest poza drzewem UIA (rozpoznanie na VM) i zasłania strony.
        Start-Process -FilePath $Exe -ArgumentList '--no-first-run'
        $Started = $true
        $Window = $null
        # Do 60 s: z siecią Edge potrafi się zaktualizować zaraz po starcie VM (rozpoznanie: 152 -> 157, okna brak po 20 s).
        for ($Poll = 0; $Poll -lt 120 -and $null -eq $Window; $Poll++) { Start-Sleep -Milliseconds 500; $Window = Get-B5K2EdgeWindow }
        if ($null -eq $Window) { throw 'okno Edge nie pojawiło się w 60 s' }
        Start-Sleep -Seconds 3
        # Ekran powitalny: klik tylko przycisku wybranego przez Select-B5K2EdgeFirstRunButton (każdy krok: nazwa i zrzut w FirstRun).
        # Monity i banery (przeglądarka domyślna, zakupy, Copilot) są obserwowane, więc nigdy nie są klikane: tylko Prompts (nazwy i zrzut).
        $ButtonCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        # Przyciski ekranu powitalnego szukane we wszystkich oknach Edge (Get-B5K2EdgeWindows). Na VM ekran powitalny był poza drzewem UIA i nie był
        # osobnym oknem (rozpoznanie), więc przy --no-first-run pętla zwykle nic nie znajduje; zostaje na wypadek innego ekranu.
        $Out.Windows = @(Get-B5K2EdgeWindows | ForEach-Object { "$($_.Current.Name)|$($_.Current.ClassName)|$($_.Current.BoundingRectangle)" })
        $Out.NewWindows = @(Get-B5K2NewWindows -Before $VisibleBefore)
        for ($Step = 1; $Step -le 6; $Step++) {
            $All = @(Get-B5K2EdgeWindows)
            if ($All.Count -eq 0) { break }
            $Buttons = @(foreach ($Each in $All) { @($Each.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ButtonCondition) | Where-Object { $_.Current.IsEnabled }) })
            $Choice = Select-B5K2EdgeFirstRunButton -Names @($Buttons | ForEach-Object { [string]$_.Current.Name })
            if ($null -eq $Choice) { break }
            $Button = @($Buttons | Where-Object { [string]$_.Current.Name -eq $Choice }) | Select-Object -First 1
            $Shot = Save-B5K2Shot -Label "$Label-fre-$Step"
            $Out.FirstRun += "$Choice|$Shot"
            $Button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Seconds 3
        }
        $Prompt = @(foreach ($Each in @(Get-B5K2EdgeWindows)) { @($Each.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ButtonCondition) | ForEach-Object { [string]$_.Current.Name } | Where-Object { Test-B5K2EdgePromptButton -Name $_ }) })
        if ($Prompt.Count -gt 0) { $Out.Prompts += [pscustomobject]@{ At = 'start'; Buttons = $Prompt; Shot = (Save-B5K2Shot -Label "$Label-monit") } }
        $Index = 0
        foreach ($Url in $Urls) {
            $Index++
            $Window = Get-B5K2EdgeWindow
            if ($null -eq $Window) { throw "okno Edge zniknęło przed adresem $Url" }
            try { $Window.SetFocus() } catch { $null = $_ }
            [B5User32]::Combo(0x11, 0x4C); Start-Sleep -Milliseconds 600
            [System.Windows.Forms.SendKeys]::SendWait(($Url -replace '([+^%~(){}\[\]])', '{$1}')); Start-Sleep -Milliseconds 300
            [B5User32]::Press(0x0D); Start-Sleep -Seconds 6
            $Window = Get-B5K2EdgeWindow
            $Names = if ($null -ne $Window) { @(Get-B5K2Lines -Element $Window -Max 400) } else { @() }
            $Shot = Save-B5K2Shot -Label "$Label-$Index"
            $Out.Pages += [pscustomobject]@{ Url = $Url; Names = $Names; Shot = $Shot }
            $Prompt = @($Names | Where-Object { $_ -like 'Button|*' } | ForEach-Object { ($_ -split '\|', 3)[1] } | Where-Object { Test-B5K2EdgePromptButton -Name $_ })
            if ($Prompt.Count -gt 0) { $Out.Prompts += [pscustomobject]@{ At = $Url; Buttons = $Prompt; Shot = $Shot } }
        }
        $Window = Get-B5K2EdgeWindow
        if ($null -ne $Window) { $Out.Toolbar = @(Get-B5K2FrameButtons -Window $Window) }
    }
    catch { $Out.Error = $_.Exception.Message }
    finally {
        if ($Started) {
            try {
                $Mine = @(Get-Process -Name msedge -IncludeUserName -ErrorAction SilentlyContinue | Where-Object { $_.UserName -like "*\$UserName" })
                foreach ($P in $Mine) { try { if ($P.MainWindowHandle -ne [IntPtr]::Zero) { $null = $P.CloseMainWindow() } } catch { $null = $_ } }
                for ($Poll = 0; $Poll -lt 30; $Poll++) {
                    if (@(Get-Process -Name msedge -IncludeUserName -ErrorAction SilentlyContinue | Where-Object { $_.UserName -like "*\$UserName" }).Count -eq 0) { break }
                    Start-Sleep -Milliseconds 500
                }
                foreach ($P in @(Get-Process -Name msedge -IncludeUserName -ErrorAction SilentlyContinue | Where-Object { $_.UserName -like "*\$UserName" })) {
                    try { Stop-Process -Id $P.Id -Force -ErrorAction Stop } catch { $null = $_ }
                }
            }
            catch { $Out.Error = (@($Out.Error, "zamykanie Edge: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
        }
    }
    return [pscustomobject]$Out
}

function Get-B5K2App {
    # Operacja App: Paint albo Notatnik przez alias aplikacji (%LOCALAPPDATA%\Microsoft\WindowsApps; Notatnik bez aliasu = System32),
    # do 20 s na okno, po 10 s od startu: czy proces żyje; przyciski i pozycje menu (≤ 300), zrzut, zamknięcie przez CloseMainWindow.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('mspaint.exe', 'notepad.exe')][string]$Exe,
        [Parameter(Mandatory)][string]$Label
    )
    Assert-B5InsideVm
    $Out = [ordered]@{ Op = 'App'; Exe = $Exe; Path = $null; Started = $false; AliveAfter10s = $false; Names = @(); Shot = $null; Error = $null }
    $Process = $null
    try {
        $Path = Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\$Exe"
        if (-not (Test-Path -LiteralPath $Path) -and $Exe -eq 'notepad.exe') { $Path = Join-Path $env:SystemRoot 'System32\notepad.exe' }
        if (-not (Test-Path -LiteralPath $Path)) { throw "brak pliku $Path" }
        $Out.Path = $Path
        $Name = [System.IO.Path]::GetFileNameWithoutExtension($Exe)
        $Before = @(Get-Process -Name $Name -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
        $Watch = [System.Diagnostics.Stopwatch]::StartNew()
        Start-Process -FilePath $Path
        $Out.Started = $true
        for ($Poll = 0; $Poll -lt 40 -and $null -eq $Process; $Poll++) {
            Start-Sleep -Milliseconds 500
            $Process = @(Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $Before -notcontains $_.Id -and $_.MainWindowHandle -ne [IntPtr]::Zero }) | Select-Object -First 1
        }
        if ($null -eq $Process) { throw "okno $Exe nie pojawiło się w 20 s" }
        $Remaining = 10000 - [int]$Watch.ElapsedMilliseconds
        if ($Remaining -gt 0) { Start-Sleep -Milliseconds $Remaining }
        $Out.AliveAfter10s = ($null -ne (Get-Process -Id $Process.Id -ErrorAction SilentlyContinue))
        if ($Out.AliveAfter10s) {
            $Process.Refresh()
            $Window = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
            $Out.Names = @(Get-B5K2Lines -Element $Window -Max 300 -Types @('Button', 'SplitButton', 'MenuItem'))
        }
        $Out.Shot = Save-B5K2Shot -Label $Label
    }
    catch { $Out.Error = $_.Exception.Message }
    finally {
        if ($null -ne $Process) {
            try { if (-not $Process.HasExited) { $null = $Process.CloseMainWindow() } } catch { $null = $_ }
            Start-Sleep -Seconds 2
        }
    }
    return [pscustomobject]$Out
}

function Get-B5K2Dictation {
    # Operacja Dictation: Win+H, 4 s, nowe okna najwyższego poziomu (tytuł | klasa) względem stanu przed, zrzut, Esc.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Label)
    Assert-B5InsideVm
    $Out = [ordered]@{ Op = 'Dictation'; NewWindows = @(); Shot = $null; Error = $null }
    try {
        $Out.Error = Close-B5K2Settings
        $Before = @([B5User32]::Visible())
        [B5User32]::Combo(0x5B, 0x48); Start-Sleep -Seconds 4
        $Out.NewWindows = @(Get-B5K2NewWindows -Before $Before)
        $Out.Shot = Save-B5K2Shot -Label $Label
        [B5User32]::Press(0x1B); Start-Sleep -Seconds 1
    }
    catch { $Out.Error = $_.Exception.Message }
    return [pscustomobject]$Out
}

function Invoke-B5K2Ops {
    # Dyspozytor operacji K2 (lista {Op, ...argumenty, Label}). Nieznana operacja = wyjątek przed pierwszą operacją; błąd znanej jest daną.
    # Toggle to operacja przełącznika pozycji (Invoke-B5Candidate), nie obserwacja.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops)
    $Known = @('Settings', 'Toggle', 'Start', 'Taskbar', 'FolderOptions', 'Edge', 'App', 'Dictation')
    foreach ($Op in $Ops) { if ($Known -cnotcontains [string]$Op.Op) { throw "Nieznana operacja K2 '$($Op.Op)'." } }
    Initialize-B5K2Native
    $Results = @()
    foreach ($Op in $Ops) {
        try {
            $Label = [string]$Op.Label
            switch ([string]$Op.Op) {
                'Settings' {
                    $ExpandAll = [bool]($Op.PSObject.Properties['ExpandAll'] -and $Op.ExpandAll)
                    $Results += Get-B5K2Settings -Uri $Op.Uri -ExpandAll:$ExpandAll -Label $Label
                }
                'Toggle' {
                    $Id = if ($Op.PSObject.Properties['Id']) { [string]$Op.Id } else { '' }
                    $Results += Set-B5K2Toggle -Uri $Op.Uri -Id $Id -Want $Op.Want -Label $Label
                }
                'Start' { $Results += Get-B5K2Start -Label $Label }
                'Taskbar' { $Results += Get-B5K2Taskbar -Label $Label }
                'FolderOptions' { $Results += Get-B5K2FolderOptions -Match ([string]$Op.Match) -Label $Label }
                'Edge' { $Results += Get-B5K2Edge -Urls @($Op.Urls) -Label $Label }
                'App' { $Results += Get-B5K2App -Exe ([string]$Op.Exe) -Label $Label }
                'Dictation' { $Results += Get-B5K2Dictation -Label $Label }
            }
        }
        catch { $Results += [pscustomobject]@{ Op = [string]$Op.Op; Error = $_.Exception.Message } }
    }
    $CloseError = Close-B5K2Settings
    if ($CloseError) { $Results += [pscustomobject]@{ Op = 'CloseSettings'; Error = $CloseError } }
    return $Results
}

function Get-B5ChildFunctionNames {
    # Funkcje kopiowane do skryptu zadania K2 (domknięcie wywołań pilnuje test).
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    return @(
        'Test-B5VmComputerName', 'Assert-B5InsideVm', 'Initialize-B5K2Native', 'Save-B5K2Shot', 'Get-B5K2Lines', 'Get-B5K2SettingsWindow', 'Find-B5K2Scroll',
        'Open-B5K2Settings', 'Get-B5K2SettingsItems', 'Find-B5K2Toggle', 'Get-B5K2Settings', 'Set-B5K2Toggle', 'Close-B5K2Settings', 'Get-B5K2NewWindows',
        'Get-B5K2Start', 'Get-B5K2Taskbar', 'Wait-B5Until', 'Get-B5K2FolderOptions', 'Get-B5K2EdgeWindows', 'Get-B5K2EdgeWindow', 'Get-B5K2FrameButtons', 'Select-B5K2EdgeFirstRunButton',
        'Test-B5K2EdgePromptButton', 'Get-B5K2Edge', 'Get-B5K2App', 'Get-B5K2Dictation', 'Invoke-B5K2Ops'
    )
}

function New-B5ChildScript {
    # Skrypt zadania K2: param(ResultFile, OpsFile), definicje funkcji, Invoke-B5K2Ops na liście z pliku JSON, wynik atomowo przez plik tymczasowy.
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Header = @'
param([Parameter(Mandatory)][string]$ResultFile, [Parameter(Mandatory)][string]$OpsFile)
$ErrorActionPreference = 'Stop'
'@
    $Definitions = foreach ($Name in Get-B5ChildFunctionNames) { "function $Name {`n$((Get-Command -Name $Name -CommandType Function -ErrorAction Stop).Definition)`n}" }
    $Main = @'
$Document = [ordered]@{ Ok = $false; Error = $null; ProcessId = $PID; Results = @() }
try {
    $Parsed = Get-Content -LiteralPath $OpsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $Ops = @(foreach ($Item in $Parsed) { $Item })
    $Document.Results = @(Invoke-B5K2Ops -Ops $Ops)
    $Document.Ok = $true
}
catch { $Document.Error = $_.Exception.Message }
$Temporary = $ResultFile + '.tmp'
$Document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Temporary -Encoding UTF8
Move-Item -LiteralPath $Temporary -Destination $ResultFile -Force
'@
    return ($Header + "`n`n" + ($Definitions -join "`n`n") + "`n`n" + $Main)
}

function Invoke-B5UserTask {
    # K2: zadanie Harmonogramu (dso / Interactive / Highest) z listą operacji; wynik z pliku; zadanie ZAWSZE wyrejestrowane.
    # Niedostępność K2 to dana (Available=false, Reason), nie wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Name,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Ops,
        [ValidateRange(10, 1800)][int]$TimeoutSeconds = 240,
        [ValidatePattern('^[A-Za-z0-9_-]+\z')][string]$UserName = 'dso'
    )
    Assert-B5InsideVm
    $Directory = 'C:\dso\b5'
    $TaskName = "DSO-B5-$Name"
    $ScriptPath = "$Directory\k2.ps1"
    $OpsPath = "$Directory\$Name.ops.json"
    $ResultPath = "$Directory\$Name.json"
    $Out = [ordered]@{ Name = $Name; Available = $false; Reason = $null; WaitedMs = $null; ChildError = $null; UnregisterError = $null; Results = @() }
    $Registered = $false
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $null = New-Item -ItemType Directory -Path $Directory -Force
        Set-Content -LiteralPath $ScriptPath -Value (New-B5ChildScript) -Encoding UTF8
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

function ConvertTo-B5Ops {
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

function Invoke-B5Observe {
    # Faza obserwacji: odczyty K1 + jedno zadanie K2 z operacjami pozycji (limit 60 s + 90 s na operację + 60 s na każdy adres Edge:
    # start, ekran pierwszego uruchomienia, nawigacja, drzewo UIA strony i zamknięcie nie mieszczą się w 90 s przy trzech adresach).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$Spec,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\z')][string]$Phase
    )
    $K1 = @()
    if ($Spec.ContainsKey('K1')) { $K1 = @(Get-B5K1Observation -Spec @($Spec.K1)) }
    $Ops = @()
    if ($Spec.ContainsKey('Observe')) { $Ops = @(ConvertTo-B5Ops -CandidateId $Spec.Id -Phase $Phase -Observe @($Spec.Observe)) }
    $K2 = $null
    if ($Ops.Count -gt 0) {
        $Budget = 60 + 90 * $Ops.Count
        foreach ($Op in $Ops) { if ($Op.Op -eq 'Edge') { $Budget += 60 * @($Op.Urls).Count } }
        $K2 = Invoke-B5UserTask -Name "$($Spec.Id)-$Phase" -Ops $Ops -TimeoutSeconds ([math]::Min(1800, $Budget))
    }
    return [pscustomobject]@{ Phase = $Phase; At = (Get-Date).ToString('o'); K1 = $K1; K2 = $K2 }
}

function Invoke-B5Explorer {
    # Restart Eksploratora wg pozycji: 'restart'/'both' -> restart; 'none' -> nic.
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('none', 'restart', 'both')][string]$Mode)
    if ($Mode -eq 'none') { return $null }
    return (Restart-B5Explorer)
}

function Invoke-B5ApplyValues {
    # Zapis listy wartości (z {SID}); zwraca rekordy zapisu ze stanem sprzed (do cofnięcia).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Values,
        [Parameter(Mandatory)][string]$UserSid
    )
    foreach ($Value in $Values) {
        $Path = Resolve-B5Path -Path $Value.Path -UserSid $UserSid
        $Before = Read-B5Value -Path $Path -Name $Value.Name
        $Write = Set-B5Value -Path $Path -Name $Value.Name -Kind $Value.Kind -Value $Value.Value
        $After = Read-B5Value -Path $Path -Name $Value.Name
        [pscustomobject]@{ Path = $Path; Name = $Value.Name; Before = $Before; Write = $Write; After = $After }
    }
}

function Invoke-B5UndoValues {
    # Cofnięcie rekordów Invoke-B5ApplyValues w odwrotnej kolejności; odczyt po cofnięciu.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Applied)
    $List = @($Applied)
    [array]::Reverse($List)
    foreach ($Record in $List) {
        $Created = @(); if ($null -ne $Record.Write -and $null -ne $Record.Write.CreatedKeys) { $Created = @($Record.Write.CreatedKeys) }
        $Restore = Restore-B5Value -Path $Record.Path -Name $Record.Name -Before $Record.Before -CreatedKeys $Created
        $After = Read-B5Value -Path $Record.Path -Name $Record.Name
        [pscustomobject]@{ Path = $Record.Path; Name = $Record.Name; Restore = $Restore; After = $After }
    }
}

function ConvertFrom-B5Diff {
    # Różnica przełącznika -> lista wartości do zapisu mechanizmem (stan „po”): pomija szum (Test-B5NoisyPath), klucze i binaria;
    # brak wartości po = usunięcie.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff)
    foreach ($Entry in $Diff) {
        if ($Entry.Name -eq '' -and ($Entry.After -eq '(klucz)' -or $Entry.Before -eq '(klucz)')) { continue }
        if (Test-B5NoisyPath -Path $Entry.Path -Name $Entry.Name) { continue }
        if ($null -eq $Entry.After) { [pscustomobject]@{ Path = $Entry.Path; Name = $Entry.Name; Kind = 'Delete'; Value = $null }; continue }
        if ([string]$Entry.After -match '^(DWord|String|ExpandString|QWord):(.*)$') {
            $Kind = $Matches[1]; $Raw = $Matches[2]
            $Value = switch ($Kind) { 'DWord' { [int]::Parse($Raw) } 'QWord' { [long]::Parse($Raw) } default { $Raw } }
            [pscustomobject]@{ Path = $Entry.Path; Name = $Entry.Name; Kind = $Kind; Value = $Value }
        }
    }
}

function Invoke-B5Candidate {
    # Jedna pozycja: stan przed -> przełącznik Ustawień (różnice) -> zapis wartości -> obserwacja po (i po restarcie Eksploratora) -> cofnięcie
    # -> obserwacja. Błąd kroku trafia do Errors; cofnięcie jest zawsze próbowane.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$Spec,
        [Parameter(Mandatory)][string]$UserSid
    )
    Assert-B5InsideVm
    $Roots = Get-B5SnapshotRoots -UserSid $UserSid
    $Explorer = if ($Spec.ContainsKey('Explorer')) { [string]$Spec.Explorer } else { 'none' }
    $Out = [ordered]@{ Id = $Spec.Id; Group = $Spec.Group; StartedAt = (Get-Date).ToString('o'); ValuesBefore = @(); Before = $null; ToggleDiff = $null; MechanismOpposite = $null
        After = $null; AfterRestart = $null; Undone = $null; Applied = @(); ApplyDiff = @(); ExplorerRestarts = @(); Undo = @(); UndoDiff = @(); Errors = @(); DurationMs = $null }
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $ObserveOnly = [bool]($Spec.ContainsKey('ObserveOnly') -and $Spec.ObserveOnly)
    $Base = Get-B5Snapshot -Roots $Roots
    try {
        if ($Spec.ContainsKey('Values')) { $Out.ValuesBefore = @($Spec.Values | ForEach-Object { Read-B5Value -Path (Resolve-B5Path -Path $_.Path -UserSid $UserSid) -Name $_.Name }) }
        $Out.Before = Invoke-B5Observe -Spec $Spec -Phase 'przed'
        if ($ObserveOnly) { throw [System.OperationCanceledException]::new('tylko obserwacja') }
        $Values = @()
        if ($Spec.ContainsKey('Values')) { $Values = @($Spec.Values) }
        if ($Spec.ContainsKey('Toggle')) {
            # Przełącznik Ustawień: na Target, na stan przeciwny i z powrotem; różnice rejestru = dowód mechanizmu przełącznika.
            $T = $Spec.Toggle
            if (-not ($T.ContainsKey('Id') -and $T.Id)) { throw "brak AutomationId – uruchom discover (przełącznik '$($T.Match)' na $($T.Uri))" }
            $Op = [ordered]@{ Op = 'Toggle'; Uri = $T.Uri; Id = $T.Id; Want = $T.Target }
            $Opposite = if ($T.Target -eq 'On') { 'Off' } else { 'On' }
            $Record = [ordered]@{ ToTarget = $null; ToTargetDiff = @(); ToOpposite = $null; ToOppositeDiff = @(); Restore = $null; RestoreDiff = @(); Mechanism = @(); OppositeMechanism = @() }
            $Op.Label = "$($Spec.Id)-toggle-1-target"
            $First = Invoke-B5UserTask -Name "$($Spec.Id)-toggle1" -Ops @([pscustomobject]$Op) -TimeoutSeconds 180
            $Record.ToTarget = $First
            $S1 = Get-B5Snapshot -Roots $Roots
            $Record.ToTargetDiff = @(Compare-B5Snapshot -Before $Base -After $S1)
            $Op.Want = $Opposite; $Op.Label = "$($Spec.Id)-toggle-2-opposite"
            $Record.ToOpposite = Invoke-B5UserTask -Name "$($Spec.Id)-toggle2" -Ops @([pscustomobject]$Op) -TimeoutSeconds 180
            $S2 = Get-B5Snapshot -Roots $Roots
            $Record.ToOppositeDiff = @(Compare-B5Snapshot -Before $S1 -After $S2)
            $Initial = $null
            if ($null -ne $First -and @($First.Results).Count -gt 0) { $Initial = [string]$First.Results[0].Before }
            if ($Initial -in @('On', 'Off')) {
                $Op.Want = $Initial; $Op.Label = "$($Spec.Id)-toggle-3-initial"
                $Record.Restore = Invoke-B5UserTask -Name "$($Spec.Id)-toggle3" -Ops @([pscustomobject]$Op) -TimeoutSeconds 180
            }
            $S3 = Get-B5Snapshot -Roots $Roots
            $Record.RestoreDiff = @(Compare-B5Snapshot -Before $Base -After $S3)
            # Mechanizm = wartości stanu Target; gdy przełącznik już był w Target, stan Target z drugiego kroku odwrotnie.
            $Mechanism = @(ConvertFrom-B5Diff -Diff $Record.ToTargetDiff)
            $OppositeMechanism = @(ConvertFrom-B5Diff -Diff $Record.ToOppositeDiff)
            if ($Mechanism.Count -eq 0 -and $OppositeMechanism.Count -gt 0) {
                $Reverse = @($Record.ToOppositeDiff | ForEach-Object { [pscustomobject]@{ Path = $_.Path; Name = $_.Name; Before = $_.After; After = $_.Before } })
                $Mechanism = @(ConvertFrom-B5Diff -Diff $Reverse)
            }
            $Record.Mechanism = $Mechanism
            $Record.OppositeMechanism = $OppositeMechanism
            $Out.ToggleDiff = [pscustomobject]$Record
            # Bez wartości pozycji (sonda samego przełącznika) zapis mechanizmem z różnicy: najpierw stan przeciwny i obserwacja, potem Target.
            # Z wartościami pozycji różnica zostaje dowodem, a zapisywane są wartości pozycji.
            if ($Values.Count -eq 0) {
                if ($OppositeMechanism.Count -gt 0) {
                    $Out.Applied += @(Invoke-B5ApplyValues -Values $OppositeMechanism -UserSid $UserSid)
                    $Out.ExplorerRestarts += Invoke-B5Explorer -Mode $Explorer
                    $Out.MechanismOpposite = Invoke-B5Observe -Spec $Spec -Phase 'mech-przeciwny'
                }
                $Values = $Mechanism
            }
        }
        if ($Values.Count -gt 0) { $Out.Applied += @(Invoke-B5ApplyValues -Values $Values -UserSid $UserSid) }
        if ($Explorer -ne 'restart') { $Out.After = Invoke-B5Observe -Spec $Spec -Phase 'po' }
        if ($Explorer -ne 'none') {
            $Out.ExplorerRestarts += Invoke-B5Explorer -Mode $Explorer
            $Out.AfterRestart = Invoke-B5Observe -Spec $Spec -Phase 'po-restarcie'
        }
        $Out.ApplyDiff = @(Compare-B5Snapshot -Before $Base -After (Get-B5Snapshot -Roots $Roots))
    }
    catch { if ($_.Exception -isnot [System.OperationCanceledException]) { $Out.Errors += "zmiana/obserwacja: $($_.Exception.Message)" } }
    finally {
        if (-not $ObserveOnly) {
            try {
                $Out.Undo = @(Invoke-B5UndoValues -Applied @($Out.Applied))
                $Out.ExplorerRestarts += Invoke-B5Explorer -Mode $Explorer
                $Out.Undone = Invoke-B5Observe -Spec $Spec -Phase 'cofniete'
                $Out.UndoDiff = @(Compare-B5Snapshot -Before $Base -After (Get-B5Snapshot -Roots $Roots))
            }
            catch { $Out.Errors += "cofnięcie: $($_.Exception.Message)" }
        }
        $Out.DurationMs = [int]$Watch.ElapsedMilliseconds
    }
    return [pscustomobject]$Out
}

function Invoke-B5RebootPhase {
    # Grupa reboot, po stronie VM: 'apply' = stan przed + obserwacja + zapis (stan do pliku C:\dso\b5\reboot-state.json, przetrwa restart);
    # 'after' = obserwacja po restarcie + cofnięcie; 'undone' = obserwacja po drugim restarcie i odczyt wartości.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable[]]$Specs,
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][ValidateSet('apply', 'after', 'undone')][string]$Phase
    )
    Assert-B5InsideVm
    $StatePath = 'C:\dso\b5\reboot-state.json'
    $Records = @()
    if ($Phase -eq 'apply') {
        $null = New-Item -ItemType Directory -Path 'C:\dso\b5' -Force
        $State = @()
        foreach ($Spec in $Specs) {
            $Record = [ordered]@{ Id = $Spec.Id; Phase = $Phase; Observation = $null; Applied = @(); Error = $null }
            try {
                $Record.Observation = Invoke-B5Observe -Spec $Spec -Phase 'r-przed'
                if ($Spec.ContainsKey('Values')) { $Record.Applied = @(Invoke-B5ApplyValues -Values @($Spec.Values) -UserSid $UserSid) }
            }
            catch { $Record.Error = $_.Exception.Message }
            $State += [pscustomobject]@{ Id = $Spec.Id; Applied = $Record.Applied }
            $Records += [pscustomobject]$Record
        }
        Set-Content -LiteralPath $StatePath -Value (ConvertTo-Json -InputObject @($State) -Depth 8) -Encoding UTF8
        return $Records
    }
    $Parsed = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $Saved = @(foreach ($Item in $Parsed) { $Item })
    foreach ($Spec in $Specs) {
        $Record = [ordered]@{ Id = $Spec.Id; Phase = $Phase; Observation = $null; Undo = @(); Values = @(); Error = $null }
        try {
            $Label = if ($Phase -eq 'after') { 'r-po-restarcie' } else { 'r-cofniete-po-restarcie' }
            $Record.Observation = Invoke-B5Observe -Spec $Spec -Phase $Label
            if ($Spec.ContainsKey('Values')) { $Record.Values = @($Spec.Values | ForEach-Object { Read-B5Value -Path (Resolve-B5Path -Path $_.Path -UserSid $UserSid) -Name $_.Name }) }
            $Entry = @($Saved | Where-Object { $_.Id -eq $Spec.Id }) | Select-Object -First 1
            if ($Phase -eq 'after' -and $null -ne $Entry) { $Record.Undo = @(Invoke-B5UndoValues -Applied @($Entry.Applied)) }
        }
        catch { $Record.Error = $_.Exception.Message }
        $Records += [pscustomobject]$Record
    }
    return $Records
}

function Get-B5PlanFromJson {
    # Plan przekazany z hosta jako JSON -> lista hashtable (rekurencyjnie; tablice zostają tablicami).
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Json)
    function ConvertTo-B5Hashtable($Node) {
        if ($null -eq $Node) { return $null }
        if ($Node -is [System.Management.Automation.PSCustomObject]) {
            $Table = @{}
            foreach ($Property in $Node.PSObject.Properties) { $Table[$Property.Name] = ConvertTo-B5Hashtable $Property.Value }
            return $Table
        }
        if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) { return , @($Node | ForEach-Object { ConvertTo-B5Hashtable $_ }) }
        return $Node
    }
    # ConvertFrom-Json w PowerShell 5.1 oddaje tablicę JSON jako JEDEN obiekt: przypisanie i foreach rozwija ją na elementy.
    $Parsed = $Json | ConvertFrom-Json
    $List = @()
    foreach ($Item in $Parsed) { $List += , (ConvertTo-B5Hashtable $Item) }
    return $List
}

function Invoke-B5Collection {
    # Punkt wejścia po stronie VM (jedna faza): zwraca JEDEN string JSON. Phase: main (pozycje po kolei) albo apply/after/undone (reboot).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$UserSid,
        [Parameter(Mandatory)][string]$PlanJson,
        [Parameter(Mandatory)][ValidateSet('main', 'apply', 'after', 'undone')][string]$Phase
    )
    Assert-B5InsideVm
    $Specs = @(Get-B5PlanFromJson -Json $PlanJson)
    $Document = [ordered]@{ meta = $null; userSid = $UserSid; phase = $Phase; inventory = $null; candidates = @() }
    try { $Document.meta = Get-ExploreMeta } catch { $Document.meta = [ordered]@{ Error = $_.Exception.Message } }
    try { $Document.inventory = Get-B5Inventory } catch { $Document.inventory = [ordered]@{ Error = $_.Exception.Message } }
    if ($Phase -eq 'main') {
        foreach ($Spec in $Specs) {
            try { $Document.candidates += Invoke-B5Candidate -Spec $Spec -UserSid $UserSid }
            catch { $Document.candidates += [pscustomobject]@{ Id = $Spec.Id; Errors = @("BŁĄD-SONDY: $($_.Exception.Message)") } }
        }
    }
    else { $Document.candidates = @(Invoke-B5RebootPhase -Specs $Specs -UserSid $UserSid -Phase $Phase) }
    return ($Document | ConvertTo-Json -Depth 14)
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta (nic z tego nie jest wysyłane do VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-B5RemoteFunctionNames {
    return @(
        'Test-B5VmComputerName', 'Assert-B5InsideVm', 'Resolve-B5Path', 'Open-B5BaseKey', 'ConvertTo-B5SnapshotValue', 'Get-B5Snapshot', 'Compare-B5Snapshot',
        'Read-B5Value', 'Get-B5SnapshotRoots', 'Test-B5NoisyPath', 'Set-B5Value', 'Restore-B5Value', 'Get-B5ShellProcess', 'Restart-B5Explorer',
        'Get-B5K1Observation', 'Get-B5Inventory', 'Initialize-B5K2Native', 'Save-B5K2Shot', 'Get-B5K2Lines', 'Get-B5K2SettingsWindow', 'Find-B5K2Scroll',
        'Open-B5K2Settings', 'Get-B5K2SettingsItems', 'Find-B5K2Toggle', 'Get-B5K2Settings', 'Set-B5K2Toggle', 'Close-B5K2Settings', 'Get-B5K2NewWindows',
        'Get-B5K2Start', 'Get-B5K2Taskbar', 'Wait-B5Until', 'Get-B5K2FolderOptions', 'Get-B5K2EdgeWindows', 'Get-B5K2EdgeWindow', 'Get-B5K2FrameButtons', 'Select-B5K2EdgeFirstRunButton',
        'Test-B5K2EdgePromptButton', 'Get-B5K2Edge', 'Get-B5K2App', 'Get-B5K2Dictation', 'Invoke-B5K2Ops', 'Get-B5ChildFunctionNames', 'New-B5ChildScript', 'Invoke-B5UserTask', 'ConvertTo-B5Ops', 'Invoke-B5Observe',
        'Invoke-B5Explorer', 'Invoke-B5ApplyValues', 'Invoke-B5UndoValues', 'ConvertFrom-B5Diff', 'Invoke-B5Candidate', 'Invoke-B5RebootPhase',
        'Get-B5PlanFromJson', 'Invoke-B5Collection'
    )
}

function Get-B5HostOnlyFunctionNames {
    return @('Get-B5Plan', 'Get-B5GroupNetwork', 'Get-B5RemoteFunctionNames', 'Get-B5HostOnlyFunctionNames', 'Get-B5RemoteScript', 'Select-B5Specs',
        'Get-B5RunArguments', 'Invoke-B5VmPhase', 'Restart-B5Vm', 'Complete-B5VmCleanup', 'Invoke-B5VmGroup', 'Invoke-B5Exploration')
}

function Get-B5RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje B5 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-B5RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Select-B5Specs {
    # Pozycje grupy (albo, dla reboot, pozycje z -RebootCandidate - wymagane), z filtrem -Candidate. Nieznane Id = wyjątek.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('discover', 'ads', 'telemetry', 'edge', 'apps', 'copilot', 'wording', 'reboot')][string]$Group,
        [AllowNull()][AllowEmptyCollection()][string[]]$Candidate,
        [AllowNull()][AllowEmptyCollection()][string[]]$RebootCandidate
    )
    $Plan = @(Get-B5Plan)
    $Known = @($Plan | ForEach-Object { $_.Id })
    foreach ($Id in @($Candidate) + @($RebootCandidate)) { if ($Id -and $Known -notcontains $Id) { throw "Nieznana pozycja '$Id' (znane: $($Known -join ', '))." } }
    if ($Group -eq 'reboot') {
        $Ids = @($RebootCandidate | Where-Object { $_ })
        if ($Ids.Count -eq 0) { throw 'Grupa reboot wymaga -RebootCandidate (Id pozycji z Get-B5Plan).' }
        return @($Plan | Where-Object { $Ids -contains $_.Id })
    }
    $Selected = @($Plan | Where-Object { $_.Group -eq $Group })
    $Filter = @($Candidate | Where-Object { $_ })
    if ($Filter.Count -gt 0) { $Selected = @($Selected | Where-Object { $Filter -contains $_.Id }) }
    return $Selected
}

function Get-B5RunArguments {
    # Argumenty przebiegu po walidacji: brak -Group = ads, telemetry, edge, apps, copilot (discover, wording i reboot tylko jawnie);
    # -Candidate musi należeć do wybranych grup; Networks = sieć grupy (Get-B5GroupNetwork, -Network $false wyłącza zawsze).
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
    $Order = @('discover', 'ads', 'telemetry', 'edge', 'apps', 'copilot', 'wording', 'reboot')
    $Default = @('ads', 'telemetry', 'edge', 'apps', 'copilot')
    $Selected = if (@($Group | Where-Object { $_ }).Count -gt 0) { @($Order | Where-Object { $Group -contains $_ }) } else { $Default }
    $Plan = [ordered]@{}
    $Networks = [ordered]@{}
    foreach ($Name in $Selected) {
        $Plan[$Name] = @(Select-B5Specs -Group $Name -Candidate $Candidate -RebootCandidate $RebootCandidate | ForEach-Object { $_.Id })
        $Networks[$Name] = ($Network -and (Get-B5GroupNetwork -Group $Name))
    }
    foreach ($Id in @($Candidate | Where-Object { $_ })) {
        $Hit = $false
        foreach ($Name in $Selected) { if ($Name -ne 'reboot' -and @($Plan[$Name]) -contains $Id) { $Hit = $true } }
        if (-not $Hit) { throw "-Candidate '$Id' nie należy do wybranych grup ($($Selected -join ', '))." }
    }
    return [ordered]@{ VmName = @($VmName); OutDirectory = $OutDirectory; Groups = @($Selected); Plan = $Plan; Networks = $Networks; CheckpointName = $CheckpointName
        ReadyTimeoutSeconds = $ReadyTimeoutSeconds; Network = $Network }
}

function Invoke-B5VmPhase {
    # Jedna faza w VM przez otwartą sesję: definicje + Invoke-B5Collection; zwraca sparsowany dokument.
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
        Invoke-B5Collection -UserSid $Sid -PlanJson $Json -Phase $Which
    }
    $Raw = @(Invoke-Command -Session $Session -ScriptBlock $Remote -ArgumentList @($RemoteScript, $UserSid, $PlanJson, $Phase))
    $Text = [string]($Raw | Where-Object { $_ -is [string] } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($Text)) { throw "Faza '$Phase' nie zwróciła JSON." }
    return ($Text | ConvertFrom-Json)
}

function Restart-B5Vm {
    # Restart gościa (Restart-Computer w VM), czekanie na spadek heartbeat i nową sesję z zalogowanym dso. Zwraca nową sesję.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro', 'DSO-PL-Home')][string]$VmName,
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

function Complete-B5VmCleanup {
    # Koniec grupy: revert do checkpointu, odłączenie karty (zawsze, także w grupie bez sieci) i sprawdzenie kontraktu stanu końcowego:
    # VM w stanie Off, karta bez przełącznika. Każdy krok osobno w try/catch; nic nie jest wyciszane - błędy i naruszenia wracają
    # w Problems (Ok=false), a wołający zapisuje je w wyniku i zgłasza.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro', 'DSO-PL-Home')][string]$VmName,
        [Parameter(Mandatory)][string]$CheckpointName
    )
    $Out = [ordered]@{ Ok = $false; RevertError = $null; DisconnectError = $null; CheckError = $null; FinalState = $null; FinalSwitch = $null; Violations = @(); Problems = @() }
    try { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName }
    catch { $Out.RevertError = $_.Exception.Message; $Out.Problems += "revert do '$CheckpointName': $($_.Exception.Message)" }
    try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop }
    catch { $Out.DisconnectError = $_.Exception.Message; $Out.Problems += "odłączenie sieci: $($_.Exception.Message)" }
    try {
        $Out.FinalState = [string](Get-VM -Name $VmName -ErrorAction Stop).State
        $Out.FinalSwitch = (@(Get-VMNetworkAdapter -VMName $VmName -ErrorAction Stop | ForEach-Object { $_.SwitchName } | Where-Object { $_ }) -join ', ')
        if ($Out.FinalState -ne 'Off') { $Out.Violations += "VM w stanie '$($Out.FinalState)' zamiast 'Off'" }
        if ($Out.FinalSwitch) { $Out.Violations += "karta dalej podłączona do '$($Out.FinalSwitch)'" }
    }
    catch { $Out.CheckError = $_.Exception.Message; $Out.Problems += "odczyt stanu końcowego: $($_.Exception.Message)" }
    if (@($Out.Violations).Count -gt 0) { $Out.Problems += "stan końcowy: $(@($Out.Violations) -join '; ')" }
    $Out.Ok = (@($Out.Problems).Count -eq 0)
    return [pscustomobject]$Out
}

function Invoke-B5VmGroup {
    # Jedna grupa na jednej VM: revert -> sieć (Network: podłączenie do Default Switch; bez sieci: odłączenie karty przed startem) -> start ->
    # gotowość -> faza main albo apply/restart/after/restart/undone -> kopia zrzutów -> ZAWSZE revert, odłączenie sieci i kontrola stanu
    # końcowego -> zapis JSON. Zwraca ścieżkę JSON.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro', 'DSO-PL-Home')][string]$VmName,
        [Parameter(Mandatory)][ValidateSet('discover', 'ads', 'telemetry', 'edge', 'apps', 'copilot', 'wording', 'reboot')][string]$Group,
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
        else { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop }
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        $UserSid = [string](Invoke-Command -Session $Session -ScriptBlock { (Get-LocalUser -Name 'dso').SID.Value })
        if ([string]::IsNullOrWhiteSpace($UserSid)) { throw "Nie udało się odczytać SID użytkownika 'dso' w VM '$VmName'." }
        # Chwila na dokończenie logowania dso (pasek zadań, Start, Edge w tle) przed pierwszą obserwacją.
        Start-Sleep -Seconds 45
        $RemoteScript = Get-B5RemoteScript
        $PlanJson = ConvertTo-Json -InputObject @($Specs) -Depth 8
        if ($Group -ne 'reboot') {
            $Phases['main'] = Invoke-B5VmPhase -Session $Session -RemoteScript $RemoteScript -UserSid $UserSid -PlanJson $PlanJson -Phase 'main'
        }
        else {
            $Phases['apply'] = Invoke-B5VmPhase -Session $Session -RemoteScript $RemoteScript -UserSid $UserSid -PlanJson $PlanJson -Phase 'apply'
            $Session = Restart-B5Vm -VmName $VmName -Session $Session -Credential $Credential -ReadyTimeoutSeconds $ReadyTimeoutSeconds
            Start-Sleep -Seconds 45
            $Phases['after'] = Invoke-B5VmPhase -Session $Session -RemoteScript $RemoteScript -UserSid $UserSid -PlanJson $PlanJson -Phase 'after'
            $Session = Restart-B5Vm -VmName $VmName -Session $Session -Credential $Credential -ReadyTimeoutSeconds $ReadyTimeoutSeconds
            Start-Sleep -Seconds 45
            $Phases['undone'] = Invoke-B5VmPhase -Session $Session -RemoteScript $RemoteScript -UserSid $UserSid -PlanJson $PlanJson -Phase 'undone'
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
                Copy-Item -FromSession $Session -Path 'C:\dso\b5\shots\*.png' -Destination $Shots -ErrorAction Stop
            }
            catch { $PrimaryError = (@($PrimaryError, "kopia zrzutów: $($_.Exception.Message)") | Where-Object { $_ }) -join ' | ' }
            Remove-PSSession -Session $Session -ErrorAction SilentlyContinue
        }
        # Sprzątanie PRZED zapisem, żeby jego wynik (także błędy) trafił do JSON; błąd sprzątania nigdy nie jest ukrywany.
        $Cleanup = Complete-B5VmCleanup -VmName $VmName -CheckpointName $CheckpointName
        if (-not $Cleanup.Ok) {
            $CleanupMessage = "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            Write-Warning "VM '$VmName', grupa '$Group': $CleanupMessage"
            $PrimaryError = (@($PrimaryError, $CleanupMessage) | Where-Object { $_ }) -join ' | '
        }
        $Result = [ordered]@{ schema = 1; vmName = $VmName; group = $Group; checkpoint = $CheckpointName; network = $Network; startedAt = $StartedAt.ToString('o')
            finishedAt = (Get-Date).ToString('o'); error = $PrimaryError; cleanup = $Cleanup; candidates = @($Specs | ForEach-Object { $_.Id }); shots = $Shots; phases = $Phases }
        $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName "b5-$Group.json"
    }
    if ($PrimaryError) { throw "VM '$VmName', grupa '$Group': $PrimaryError (wynik częściowy: $Saved)" }
    return $Saved
}

function Invoke-B5Exploration {
    # Wszystkie wybrane grupy na wszystkich VM, sieć wg Arguments.Networks. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej.
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
                $Specs = @(Select-B5Specs -Group $Name -Candidate $Candidate -RebootCandidate $RebootCandidate)
                if ($Specs.Count -eq 0) { continue }
                $GroupNetwork = [bool]$Arguments.Networks[$Name]
                Write-Host "$(Get-Date -Format 'HH:mm:ss') $Vm / $Name (sieć: $GroupNetwork; $(@($Specs | ForEach-Object { $_.Id }) -join ', '))"
                $Paths.Add((Invoke-B5VmGroup -VmName $Vm -Group $Name -Specs $Specs -OutDirectory $Arguments.OutDirectory -CheckpointName $Arguments.CheckpointName `
                            -ReadyTimeoutSeconds $Arguments.ReadyTimeoutSeconds -Network $GroupNetwork))
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
        $OutDirectory = Assert-ExploreOutDirectory -Path $OutDirectory -RepoRoot (Get-DsoRepoRoot) -Example '$env:TEMP\dso-explore\b5'
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        $RunArguments = Get-B5RunArguments -VmName $VmName -OutDirectory $OutDirectory -Group $Group -Candidate $Candidate -RebootCandidate $RebootCandidate `
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
        $Outcome = Invoke-B5Exploration -Arguments $RunArguments -Candidate $Candidate -RebootCandidate $RebootCandidate
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
