BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
    $Adv = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    $Cdm = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
    $Toasts = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings'
    $Edge = 'HKLM\SOFTWARE\Policies\Microsoft\Edge'
    $Paint = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint'
    # Wartości kandydatów (tabela „Kandydaci”, ścieżki HKCU\ zapisane jako HKU\{SID}\) i sond dodatkowych, 1:1 z Get-B5Plan.
    $script:ExpectedValues = @(
        @{ Id = 'explorer-sync-provider-notifications'; Path = $Adv; Name = 'ShowSyncProviderNotifications'; Value = 0 }
        @{ Id = 'lockscreen-tips'; Path = $Cdm; Name = 'RotatingLockScreenOverlayEnabled'; Value = 0 }
        @{ Id = 'lockscreen-tips'; Path = $Cdm; Name = 'SubscribedContent-338387Enabled'; Value = 0 }
        @{ Id = 'lockscreen-tips-spotlight'; Path = 'HKU\{SID}\Software\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsSpotlightFeatures'; Value = 1 }
        @{ Id = 'lockscreen-tips-overlay-only'; Path = $Cdm; Name = 'RotatingLockScreenOverlayEnabled'; Value = 0 }
        @{ Id = 'lockscreen-tips-338387-only'; Path = $Cdm; Name = 'SubscribedContent-338387Enabled'; Value = 0 }
        @{ Id = 'start-account-notifications'; Path = $Adv; Name = 'Start_AccountNotifications'; Value = 0 }
        @{ Id = 'toast-suggested'; Path = "$Toasts\Windows.SystemToast.Suggested"; Name = 'Enabled'; Value = 0 }
        @{ Id = 'toast-backup-reminder'; Path = "$Toasts\Windows.SystemToast.BackupReminder"; Name = 'Enabled'; Value = 0 }
        @{ Id = 'settings-365-ads'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableConsumerAccountStateContent'; Value = 1 }
        @{ Id = 'edge-shopping-recommendations'; Path = $Edge; Name = 'EdgeShoppingAssistantEnabled'; Value = 0 }
        @{ Id = 'edge-shopping-recommendations'; Path = $Edge; Name = 'ShowRecommendationsEnabled'; Value = 0 }
        @{ Id = 'edge-shopping-recommendations'; Path = $Edge; Name = 'SpotlightExperiencesAndRecommendationsEnabled'; Value = 0 }
        @{ Id = 'edge-newtab-content'; Path = $Edge; Name = 'NewTabPageContentEnabled'; Value = 0 }
        @{ Id = 'edge-newtab-topsites'; Path = $Edge; Name = 'NewTabPageHideDefaultTopSites'; Value = 1 }
        @{ Id = 'edge-prompts-default-browser'; Path = $Edge; Name = 'DefaultBrowserSettingEnabled'; Value = 0 }
        @{ Id = 'edge-prompts-default-browser'; Path = $Edge; Name = 'DefaultBrowserSettingsCampaignEnabled'; Value = 0 }
        @{ Id = 'start-track-progs'; Path = $Adv; Name = 'Start_TrackProgs'; Value = 0 }
        @{ Id = 'online-speech'; Path = 'HKU\{SID}\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy'; Name = 'HasAccepted'; Value = 0 }
        @{ Id = 'activity-history'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System'; Name = 'EnableActivityFeed'; Value = 0 }
        @{ Id = 'activity-history'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System'; Name = 'PublishUserActivities'; Value = 0 }
        @{ Id = 'activity-history'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System'; Name = 'UploadUserActivities'; Value = 0 }
        @{ Id = 'inking-typing-improve'; Path = 'HKU\{SID}\Software\Microsoft\Input\TIPC'; Name = 'Enabled'; Value = 0 }
        @{ Id = 'inking-typing-improve'; Path = 'HKU\{SID}\Software\Microsoft\InputPersonalization'; Name = 'RestrictImplicitInkCollection'; Value = 1 }
        @{ Id = 'inking-typing-improve'; Path = 'HKU\{SID}\Software\Microsoft\InputPersonalization'; Name = 'RestrictImplicitTextCollection'; Value = 1 }
        @{ Id = 'inking-typing-improve'; Path = 'HKU\{SID}\Software\Microsoft\InputPersonalization\TrainedDataStore'; Name = 'HarvestContacts'; Value = 0 }
        @{ Id = 'inking-typing-improve'; Path = 'HKU\{SID}\Software\Microsoft\Personalization\Settings'; Name = 'AcceptedPrivacyPolicy'; Value = 0 }
        @{ Id = 'edge-diagnostic-data'; Path = $Edge; Name = 'DiagnosticData'; Value = 0 }
        @{ Id = 'edge-diagnostic-data'; Path = $Edge; Name = 'PersonalizationReportingEnabled'; Value = 0 }
        @{ Id = 'edge-user-feedback'; Path = $Edge; Name = 'UserFeedbackAllowed'; Value = 0 }
        @{ Id = 'edge-copilot'; Path = $Edge; Name = 'HubsSidebarEnabled'; Value = 0 }
        @{ Id = 'edge-copilot'; Path = $Edge; Name = 'CopilotPageContext'; Value = 0 }
        @{ Id = 'edge-copilot'; Path = $Edge; Name = 'EdgeEntraCopilotPageContext'; Value = 0 }
        @{ Id = 'edge-copilot'; Path = $Edge; Name = 'Microsoft365CopilotChatIconEnabled'; Value = 0 }
        @{ Id = 'edge-copilot'; Path = $Edge; Name = 'NewTabPageBingChatEnabled'; Value = 0 }
        @{ Id = 'edge-copilot'; Path = $Edge; Name = 'EdgeHistoryAISearchEnabled'; Value = 0 }
        @{ Id = 'edge-copilot'; Path = $Edge; Name = 'ComposeInlineEnabled'; Value = 0 }
        @{ Id = 'edge-copilot-no-sidebar'; Path = $Edge; Name = 'CopilotPageContext'; Value = 0 }
        @{ Id = 'edge-copilot-no-sidebar'; Path = $Edge; Name = 'EdgeEntraCopilotPageContext'; Value = 0 }
        @{ Id = 'edge-copilot-no-sidebar'; Path = $Edge; Name = 'Microsoft365CopilotChatIconEnabled'; Value = 0 }
        @{ Id = 'edge-copilot-no-sidebar'; Path = $Edge; Name = 'NewTabPageBingChatEnabled'; Value = 0 }
        @{ Id = 'edge-copilot-no-sidebar'; Path = $Edge; Name = 'EdgeHistoryAISearchEnabled'; Value = 0 }
        @{ Id = 'edge-copilot-no-sidebar'; Path = $Edge; Name = 'ComposeInlineEnabled'; Value = 0 }
        @{ Id = 'paint-ai'; Path = $Paint; Name = 'DisableCocreator'; Value = 1 }
        @{ Id = 'paint-ai'; Path = $Paint; Name = 'DisableGenerativeFill'; Value = 1 }
        @{ Id = 'paint-ai'; Path = $Paint; Name = 'DisableImageCreator'; Value = 1 }
        @{ Id = 'notepad-ai'; Path = 'HKLM\SOFTWARE\Policies\WindowsNotepad'; Name = 'DisableAIFeatures'; Value = 1 }
        @{ Id = 'copilot-taskbar-button'; Path = $Adv; Name = 'ShowCopilotButton'; Value = 0 }
        @{ Id = 'feedback-frequency-recheck'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'DoNotShowFeedbackNotifications'; Value = 1 }
    )
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-B5Exploration.ps1')
    $script:B5ScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-B5Exploration.ps1'
    $script:Sid = 'S-1-5-21-1-2-3-1000'
    # 18 pozycji tabeli „Kandydaci” -> grupa skryptu.
    $script:TableGroups = [ordered]@{
        'explorer-sync-provider-notifications' = 'ads'; 'lockscreen-tips' = 'ads'; 'start-account-notifications' = 'ads'; 'toast-suggested' = 'ads'
        'toast-backup-reminder' = 'ads'; 'settings-365-ads' = 'ads'; 'edge-shopping-recommendations' = 'edge'; 'edge-newtab-content' = 'edge'
        'edge-prompts-default-browser' = 'edge'; 'start-track-progs' = 'telemetry'; 'online-speech' = 'telemetry'; 'activity-history' = 'telemetry'
        'inking-typing-improve' = 'telemetry'; 'edge-diagnostic-data' = 'edge'; 'edge-copilot' = 'edge'; 'paint-ai' = 'apps'; 'notepad-ai' = 'apps'
        'copilot-taskbar-button' = 'copilot'
    }

    function script:Get-CommandOwner {
        param($Node)
        $Parent = $Node.Parent
        while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
        if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
    }
    function script:Get-SpecValues {
        # Uwaga: $Spec.Values na hashtable bez klucza 'Values' zwraca kolekcję wartości samej hashtable.
        param([hashtable]$Spec)
        if ($Spec.ContainsKey('Values')) { @($Spec.Values) } else { @() }
    }
    function script:Get-PlanSpec {
        param([string]$Id)
        @(Get-B5Plan | Where-Object { $_.Id -eq $Id })[0]
    }
}

Describe 'Plan B5 (wartości dosłownie z tabeli kandydatów)' {
    It 'Plan B5: Id unikalne, grupy z ValidateSet, ścieżki tylko HKLM\ i HKU\{SID}\, Kind = DWord' {
        $Plan = @(Get-B5Plan)
        $Ids = @($Plan | ForEach-Object { $_.Id })
        ($Ids | Sort-Object -Unique).Count | Should -Be $Ids.Count
        $Groups = ((Get-Command $script:B5ScriptPath).Parameters['Group'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues
        foreach ($Spec in $Plan) {
            $Spec.Group | Should -BeIn $Groups
            $Spec.Group | Should -Not -Be 'reboot'
            $Spec.Id | Should -Match '^[a-z0-9-]+$'
            $Spec.Explorer | Should -BeIn @('none', 'restart', 'both')
            foreach ($Value in @(Get-SpecValues $Spec)) {
                $Value.Path | Should -Match '^(HKLM\\SOFTWARE\\|HKU\\\{SID\}\\)'
                $Value.Kind | Should -Be 'DWord'
            }
            if ($Spec.ContainsKey('Toggle')) {
                $Spec.Toggle.Uri | Should -Match '^ms-settings:[a-z0-9-]+$'
                $Spec.Toggle.Target | Should -BeIn @('On', 'Off')
                $Spec.Toggle.Match | Should -Not -BeNullOrEmpty
            }
            foreach ($Op in @($Spec.Observe)) {
                $Op.Op | Should -BeIn @('Settings', 'Start', 'Taskbar', 'FolderOptions', 'Edge', 'App', 'Dictation')
                if ($Op.Op -eq 'Settings') { $Op.Uri | Should -Match '^ms-settings:[a-z0-9-]*$'; $Op.ExpandAll | Should -BeTrue }
                if ($Op.Op -eq 'Edge') { foreach ($Url in $Op.Urls) { $Url | Should -Match '^(edge://|https://)' } }
                if ($Op.Op -eq 'App') { $Op.Exe | Should -BeIn @('mspaint.exe', 'notepad.exe') }
            }
        }
        foreach ($Id in $script:TableGroups.Keys) { (Get-PlanSpec -Id $Id).Group | Should -Be $script:TableGroups[$Id] -Because $Id }
    }

    It '<Id>: <Name> = <Value> w <Path>' -ForEach $script:ExpectedValues {
        $Spec = Get-PlanSpec -Id $Id
        $Spec | Should -Not -BeNullOrEmpty
        $Entry = @($Spec.Values | Where-Object { $_.Name -eq $Name -and $_.Path -eq $Path })
        $Entry.Count | Should -Be 1
        $Entry[0].Kind | Should -Be 'DWord'
        $Entry[0].Value | Should -Be $Value
    }

    It 'wartości planu 1:1 z listą testu (nic ponad tabelę i sondy dodatkowe)' -ForEach @(@{ Expected = $script:ExpectedValues }) {
        $All = @(foreach ($Spec in (Get-B5Plan)) { foreach ($Value in @(Get-SpecValues $Spec)) { "$($Spec.Id)|$($Value.Path)|$($Value.Name)|$($Value.Value)" } })
        $Wanted = @($Expected | ForEach-Object { "$($_.Id)|$($_.Path)|$($_.Name)|$($_.Value)" })
        @($All | Sort-Object) | Should -Be @($Wanted | Sort-Object)
    }

    It 'Edge: tylko klucz HKLM\SOFTWARE\Policies\Microsoft\Edge; żadnej nazwy z listy „nie bierzemy”' {
        $Rejected = @('HideFirstRunExperience', 'TabServicesEnabled', 'AlternateErrorPagesEnabled', 'WalletDonationEnabled', 'ShowAcrobatSubscriptionButton',
            'MetricsReportingEnabled', 'GenAILocalFoundationalModelSettings', 'BuiltInAIAPIsEnabled', 'AIGenThemesEnabled')
        foreach ($Spec in (Get-B5Plan)) {
            foreach ($Value in @(Get-SpecValues $Spec)) {
                $Value.Name | Should -Not -BeIn $Rejected
                if ($Spec.Group -eq 'edge') { $Value.Path | Should -BeExactly 'HKLM\SOFTWARE\Policies\Microsoft\Edge' }
                else { $Value.Path | Should -Not -Match '\\Edge$' }
            }
        }
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:B5ScriptPath), [System.Text.Encoding]::UTF8)
        foreach ($Name in $Rejected) { $Text | Should -Not -Match "'$Name'" }
    }

    It 'discover i wording: ObserveOnly, bez Values i Toggle' {
        $Plan = @(Get-B5Plan)
        $Observe = @($Plan | Where-Object { $_.Group -in @('discover', 'wording') })
        @($Plan | Where-Object { $_.Group -eq 'wording' } | ForEach-Object { $_.Id }) | Should -Be @('wording-settings', 'wording-folderoptions', 'wording-edge', 'wording-apps', 'wording-taskbar')
        foreach ($Spec in $Observe) {
            $Spec.ObserveOnly | Should -BeTrue -Because $Spec.Id
            $Spec.ContainsKey('Values') | Should -BeFalse -Because $Spec.Id
            $Spec.ContainsKey('Toggle') | Should -BeFalse -Because $Spec.Id
        }
        $Ops = @($Plan | Where-Object { $_.Group -eq 'discover' } | ForEach-Object { @($_.Observe) })
        $Pages = @($Ops | Where-Object { $_.Op -eq 'Settings' } | ForEach-Object { $_.Uri })
        $TablePages = @($Plan | Where-Object { $_.Group -notin @('discover', 'wording') } | ForEach-Object { @($_.Observe) + @($_.Toggle | Where-Object { $_ } | ForEach-Object { @{ Op = 'Settings'; Uri = $_.Uri } }) } |
                Where-Object { $_.Op -eq 'Settings' } | ForEach-Object { $_.Uri } | Sort-Object -Unique)
        foreach ($Uri in $TablePages) { $Pages | Should -Contain $Uri }
        @($Ops | Where-Object { $_.Op -eq 'FolderOptions' }).Count | Should -Be 1
        @($Ops | Where-Object { $_.Op -eq 'Edge' } | ForEach-Object { $_.Urls }) | Should -Be @('edge://settings', 'edge://policy', 'edge://newtab')
        @($Ops | Where-Object { $_.Op -eq 'App' } | ForEach-Object { $_.Exe }) | Should -Be @('mspaint.exe', 'notepad.exe')
        $WordingOps = @($Plan | Where-Object { $_.Group -eq 'wording' } | ForEach-Object { @($_.Observe) })
        @($WordingOps | Where-Object { $_.Op -eq 'Settings' } | ForEach-Object { $_.Uri }) | Should -Be $Pages
        @($WordingOps | ForEach-Object { $_.Op } | Sort-Object -Unique) | Should -Be @('App', 'Edge', 'FolderOptions', 'Settings', 'Taskbar')
    }

    It 'przełączniki: Match (angielskie wyrażenie) i Toggle.Id z rozpoznania; tylko pozycje z listy mają przełącznik' {
        $Toggles = @(Get-B5Plan | Where-Object { $_.ContainsKey('Toggle') })
        @($Toggles | ForEach-Object { $_.Id } | Sort-Object) | Should -Be @('inking-typing-improve', 'online-speech', 'online-speech-reverse', 'settings-suggestions-recheck', 'start-account-notifications', 'start-track-progs')
        (Get-PlanSpec -Id 'online-speech').Toggle.Match | Should -Be 'Online speech'
        (Get-PlanSpec -Id 'settings-suggestions-recheck').ContainsKey('Values') | Should -BeFalse
        # Rozpoznanie na VM: na stronie ekranu blokady nie ma pola „fun facts” (personalizacja zablokowana bez aktywacji).
        (Get-PlanSpec -Id 'lockscreen-tips').ContainsKey('Toggle') | Should -BeFalse
    }

    It '<Id>: Toggle.Id = <AutomationId>' -ForEach @(
        @{ Id = 'start-account-notifications'; AutomationId = 'SystemSettings_Start_AccountNotifications_ToggleSwitch' }
        @{ Id = 'settings-suggestions-recheck'; AutomationId = 'SystemSettings_Privacy_EnableSuggestionsInSettings_ToggleSwitch' }
        @{ Id = 'start-track-progs'; AutomationId = 'SystemSettings_Privacy_StoreAppUsage_ToggleSwitch' }
        @{ Id = 'online-speech'; AutomationId = 'SystemSettings_Privacy_Speech_AllowSpeechServices_ToggleSwitch' }
        @{ Id = 'online-speech-reverse'; AutomationId = 'SystemSettings_Privacy_Speech_AllowSpeechServices_ToggleSwitch' }
        @{ Id = 'inking-typing-improve'; AutomationId = 'SystemSettings_Privacy_PersonalizeInkType_Toggle_ToggleSwitch' }
    ) {
        (Get-PlanSpec -Id $Id).Toggle.Id | Should -BeExactly $AutomationId
    }

    It 'online-speech-reverse: sonda samego przełącznika (bez wartości), Target On, grupa telemetry' {
        $Spec = Get-PlanSpec -Id 'online-speech-reverse'
        $Spec.Group | Should -Be 'telemetry'
        $Spec.ContainsKey('Values') | Should -BeFalse
        $Spec.Toggle.Target | Should -Be 'On'
        @($Spec.Observe | ForEach-Object { $_.Uri }) | Should -Be @('ms-settings:privacy-speech')
    }

    It 'Edge: przełączniki polityk obserwowane przez wyszukiwanie w ustawieniach (edge://settings/privacy to w Edge 157 tylko spis podstron)' {
        @((Get-PlanSpec -Id 'edge-shopping-recommendations').Observe[0].Urls) | Should -Contain 'edge://settings/?search=shopping'
        @((Get-PlanSpec -Id 'edge-diagnostic-data').Observe[0].Urls) | Should -Be @('edge://settings/privacy', 'edge://settings/?search=diagnostic', 'edge://settings/?search=personalization', 'edge://policy')
        @((Get-PlanSpec -Id 'edge-user-feedback').Observe[0].Urls) | Should -Be @('edge://settings/?search=feedback', 'edge://policy')
    }

    It 'start-account-notifications: Explorer = both (obserwacja Startu przed i po restarcie Eksploratora)' {
        (Get-PlanSpec -Id 'start-account-notifications').Explorer | Should -Be 'both'
    }

    It 'Get-B5GroupNetwork: discover, ads, edge, apps, wording = $true; telemetry, copilot, reboot = $false' {
        foreach ($Name in 'discover', 'ads', 'edge', 'apps', 'wording') { Get-B5GroupNetwork -Group $Name | Should -BeTrue -Because $Name }
        foreach ($Name in 'telemetry', 'copilot', 'reboot') { Get-B5GroupNetwork -Group $Name | Should -BeFalse -Because $Name }
        { Get-B5GroupNetwork -Group 'search' } | Should -Throw
    }
}

Describe 'Bezpieczniki (VM-only, mock-only)' {
    It 'Test-B5VmComputerName: cztery nazwy DSO, inne odrzucone' {
        foreach ($Name in 'dso-en-home', 'DSO-EN-PRO', 'dso-pl-pro', 'DSO-PL-HOME') { Test-B5VmComputerName -ComputerName $Name | Should -BeTrue -Because $Name }
        foreach ($Name in 'DSO-26H2-PRO', 'BRUTEFORCE_LTSC', 'HOST-PC', '') { Test-B5VmComputerName -ComputerName $Name | Should -BeFalse -Because $Name }
    }
    It 'Assert-B5InsideVm rzuca w trybie mock-only i na hoście' {
        { Assert-B5InsideVm -ComputerName 'DSO-EN-PRO' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        $Saved = $env:DSO_VM_TESTS_MOCK_ONLY
        try {
            [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $null)
            { Assert-B5InsideVm -ComputerName 'HOST-PC' } | Should -Throw '*tylko w VM*'
            { Assert-B5InsideVm -ComputerName 'DSO-EN-HOME' } | Should -Not -Throw
        }
        finally { $env:DSO_VM_TESTS_MOCK_ONLY = $Saved }
    }
    It '<Name> rzuca przed skutkiem' -ForEach @(
        @{ Name = 'Set-B5Value'; Code = { Set-B5Value -Path 'HKLM\SOFTWARE\Policies\X' -Name 'A' -Kind DWord -Value 1 } }
        @{ Name = 'Restore-B5Value'; Code = { Restore-B5Value -Path 'HKLM\SOFTWARE\Policies\X' -Name 'A' -Before ([pscustomobject]@{ Exists = $false }) } }
        @{ Name = 'Restart-B5Explorer'; Code = { Restart-B5Explorer } }
        @{ Name = 'Invoke-B5UserTask'; Code = { Invoke-B5UserTask -Name 'x' -Ops @() } }
        @{ Name = 'Get-B5K2FolderOptions'; Code = { Get-B5K2FolderOptions -Match 'sync provider' -Label 'x' } }
        @{ Name = 'Get-B5K2Edge'; Code = { Get-B5K2Edge -Urls 'edge://policy' -Label 'x' } }
        @{ Name = 'Get-B5K2App'; Code = { Get-B5K2App -Exe 'notepad.exe' -Label 'x' } }
        @{ Name = 'Get-B5K2Dictation'; Code = { Get-B5K2Dictation -Label 'x' } }
        @{ Name = 'Invoke-B5Candidate'; Code = { Invoke-B5Candidate -Spec @{ Id = 'x'; Group = 'ads' } -UserSid $script:Sid } }
        @{ Name = 'Invoke-B5RebootPhase'; Code = { Invoke-B5RebootPhase -Specs @(@{ Id = 'x' }) -UserSid $script:Sid -Phase apply } }
        @{ Name = 'Invoke-B5Collection'; Code = { Invoke-B5Collection -UserSid $script:Sid -PlanJson '[]' -Phase main } }
    ) {
        Mock Stop-Process { throw 'nie wolno' }
        Mock Start-Process { throw 'nie wolno' }
        Mock Register-ScheduledTask { throw 'nie wolno' }
        Mock Get-B5Snapshot { throw 'nie wolno' }
        Mock Open-B5BaseKey { throw 'nie wolno' }
        { & $Code } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        foreach ($Command in 'Stop-Process', 'Start-Process', 'Register-ScheduledTask', 'Get-B5Snapshot', 'Open-B5BaseKey') { Should -Invoke $Command -Times 0 -Exactly }
    }
    It 'Get-B5K2Edge: adres spoza edge:// i https:// rzuca' {
        foreach ($Url in 'http://example.com', 'file:///C:/Windows', 'about:blank', 'javascript:alert(1)') {
            { Get-B5K2Edge -Urls @('edge://policy', $Url) -Label 'x' } | Should -Throw -ExceptionType ([System.Management.Automation.ParameterBindingException]) -Because $Url
        }
    }
    It 'Edge: klikany tylko przycisk ekranu powitalnego; monity (Not now, Got it, No, thanks, Nie teraz, Rozumiem …) nigdy' {
        foreach ($Name in 'Not now', 'Got it', 'No, thanks', 'Nie teraz', 'Rozumiem', 'Skip', 'Pomiń', 'Nie, dziękuję', 'Set as default', 'Ustaw jako domyślną') {
            Select-B5K2EdgeFirstRunButton -Names @($Name) | Should -BeNullOrEmpty -Because $Name
            Test-B5K2EdgePromptButton -Name $Name | Should -BeTrue -Because "$Name trafia do zapisu monitów"
        }
        foreach ($Name in 'Confirm and browse', 'Confirm and continue', 'Continue without signing in', 'Start without your data', 'Potwierdź i przeglądaj', 'Kontynuuj bez logowania') {
            Select-B5K2EdgeFirstRunButton -Names @('Not now', $Name, 'Got it') | Should -Be $Name -Because $Name
            Test-B5K2EdgePromptButton -Name $Name | Should -BeFalse -Because $Name
        }
        # Ekran powitalny rozpoznany tylko po pełnej nazwie przycisku (nie po fragmencie w dłuższym tekście).
        Select-B5K2EdgeFirstRunButton -Names @('Confirm and browse later', 'Learn more about Start without your data') | Should -BeNullOrEmpty
        Select-B5K2EdgeFirstRunButton -Names @() | Should -BeNullOrEmpty
        Test-B5K2EdgePromptButton -Name 'Close' | Should -BeFalse -Because 'ramka okna ma zawsze Close'
        # Get-B5K2Edge klika wyłącznie przycisk wybrany przez Select-B5K2EdgeFirstRunButton; monity tylko zapisuje (nazwa + zrzut).
        $Definition = (Get-Command Get-B5K2Edge).Definition
        $Definition | Should -Match 'Select-B5K2EdgeFirstRunButton'
        $Definition | Should -Match 'Test-B5K2EdgePromptButton'
        $Definition | Should -Not -Match 'Not now|Got it|No, thanks|Nie teraz|Rozumiem'
        @([regex]::Matches($Definition, 'InvokePattern\]::Pattern\)\.Invoke\(\)')).Count | Should -Be 1
    }
    It 'Get-B5K2Settings: zamyka otwarte okno Ustawień przed adresem (samo ms-settings: nie zmienia otwartej strony)' {
        $script:Calls = New-Object System.Collections.Generic.List[string]
        Mock Close-B5K2Settings { $script:Calls.Add('close') }
        Mock Open-B5K2Settings { $script:Calls.Add("open:$Uri"); $null }
        $Result = Get-B5K2Settings -Uri 'ms-settings:' -ExpandAll -Label 'x'
        @($script:Calls) | Should -Be @('close', 'open:ms-settings:')
        $Result.Found | Should -BeFalse
    }
    It 'Close-B5K2Settings: czeka (Wait-B5Until), aż okno zniknie; okno, które zostaje, i błąd Close() wracają jako opis, nie są połykane' {
        Add-Type -AssemblyName UIAutomationClient
        $script:Closed = $false; $script:Disappears = $true; $script:CloseThrows = $false
        $script:FakePattern = [pscustomobject]@{}
        $script:FakePattern | Add-Member -MemberType ScriptMethod -Name Close -Value { if ($script:CloseThrows) { throw 'odmowa' }; $script:Closed = $true }
        $script:FakeWindow = [pscustomobject]@{}
        $script:FakeWindow | Add-Member -MemberType ScriptMethod -Name TryGetCurrentPattern -Value { param($Id, $Ref) $Ref.Value = $script:FakePattern; $true }
        Mock Get-B5K2SettingsWindow { if ($script:Closed -and $script:Disappears) { $null } else { $script:FakeWindow } }
        Mock Wait-B5Until { [bool](& $Probe) }
        Close-B5K2Settings | Should -BeNullOrEmpty
        Should -Invoke Wait-B5Until -Times 1 -Exactly -ParameterFilter { $TimeoutMs -eq 10000 }
        $script:Closed = $false; $script:Disappears = $false
        Close-B5K2Settings | Should -Be 'zamykanie Ustawień: okno nie zamknęło się w 10 s'
        $script:Closed = $false; $script:CloseThrows = $true
        Close-B5K2Settings | Should -Match '^zamykanie Ustawień: Close\(\): .*odmowa.* \| okno nie zamknęło się w 10 s$'
        # Brak okna: nic do zamykania, bez czekania.
        Mock Get-B5K2SettingsWindow { $null }
        Close-B5K2Settings | Should -BeNullOrEmpty
        Should -Invoke Wait-B5Until -Times 3 -Exactly
        $Definition = (Get-Command Close-B5K2Settings).Definition
        $Definition | Should -Not -Match 'Start-Sleep'
        $Definition | Should -Not -Match '\$null = \$_'
    }
    It 'błąd zamknięcia Ustawień trafia do Error operacji (Settings, Start, Dictation) i do wyniku dyspozytora' {
        Mock Close-B5K2Settings { 'zamykanie Ustawień: okno nie zamknęło się w 10 s' }
        Mock Open-B5K2Settings { $null }
        (Get-B5K2Settings -Uri 'ms-settings:' -ExpandAll -Label 'x').Error | Should -Be 'zamykanie Ustawień: okno nie zamknęło się w 10 s'
        foreach ($Name in 'Get-B5K2Start', 'Get-B5K2Dictation') { (Get-Command $Name).Definition | Should -Match '\$Out\.Error = Close-B5K2Settings' -Because $Name }
        Mock Initialize-B5K2Native { }
        Mock Get-B5K2Taskbar { [pscustomobject]@{ Op = 'Taskbar'; Error = $null } }
        $Results = @(Invoke-B5K2Ops -Ops @([pscustomobject]@{ Op = 'Taskbar'; Label = 'a-1' }))
        @($Results | ForEach-Object { $_.Op }) | Should -Be @('Taskbar', 'CloseSettings')
        $Results[1].Error | Should -Be 'zamykanie Ustawień: okno nie zamknęło się w 10 s'
    }
    It 'Wait-B5Until: $true przy pierwszej udanej sondzie, $false po limicie' {
        $script:Count = 0
        Wait-B5Until -TimeoutMs 5000 -IntervalMs 10 -Probe { $script:Count++; $script:Count -ge 3 } | Should -BeTrue
        $script:Count | Should -Be 3
        Wait-B5Until -TimeoutMs 50 -IntervalMs 10 -Probe { $false } | Should -BeFalse
    }
    It 'Opcje folderów: zakładka przez PSM_SETCURSEL, drzewo przez MSAA (do skutku), nazwa okna i diagnostyka w wyniku' {
        $Definition = (Get-Command Get-B5K2FolderOptions).Definition
        $Definition | Should -Match 'Wait-B5Until -TimeoutMs 10000'
        $Definition | Should -Match '\$Out\.Window = '
        $Definition | Should -Match 'SendMessage\(\$Handle, 0x465, \[IntPtr\]1'
        $Definition | Should -Match "ChildByClass\(\`$Handle, 'SysTreeView32'\)"
        $Definition | Should -Match 'AccItems\(\$Tree'
        # Bez TogglePattern i bez klikania: stan tylko z MSAA (STATE_SYSTEM_CHECKED), 'Unknown', gdy MSAA nie raportuje zaznaczeń.
        $Definition | Should -Not -Match 'TogglePattern|InvokePattern|\.Toggle\(\)'
        $Definition | Should -Match '0x10'
        $Definition | Should -Match 'if \(\$Checks -gt 0\)'
        # Bez drzewa: drzewo UIA okna i zrzut zostają w wyniku (diagnostyka), dopiero potem błąd.
        $Definition.IndexOf('Get-B5K2Lines -Element $Window') | Should -BeGreaterThan 0
        $Definition.IndexOf('Get-B5K2Lines -Element $Window') | Should -BeLessThan $Definition.IndexOf('brak drzewa ustawień zaawansowanych')
    }
    It 'Ustawienia: karty konta Microsoft i kopii zapasowej na stronie głównej trafiają do elementów; activity-history obserwuje też ms-settings:privacy' {
        $Definition = (Get-Command Get-B5K2SettingsItems).Definition
        foreach ($Word in 'Microsoft account', 'Back up', 'Activity history') { $Definition | Should -Match ([regex]::Escape($Word)) }
        @((Get-PlanSpec -Id 'activity-history').Observe | ForEach-Object { $_.Uri }) | Should -Be @('ms-settings:privacy-activityhistory', 'ms-settings:privacy')
        @((Get-PlanSpec -Id 'discover-settings').Observe | ForEach-Object { $_.Uri }) | Should -Contain 'ms-settings:privacy'
    }
    It 'Edge: przycisk ekranu powitalnego szukany we wszystkich oknach Edge (Get-B5K2EdgeWindows), lista okien w wyniku jako diagnostyka' {
        $Definition = (Get-Command Get-B5K2Edge).Definition
        $Loop = $Definition.Substring($Definition.IndexOf('for ($Step = 1'))
        $Loop = $Loop.Substring(0, $Loop.IndexOf('Select-B5K2EdgeFirstRunButton'))
        $Loop | Should -Match 'Get-B5K2EdgeWindows'
        $Loop | Should -Not -Match 'Get-B5K2EdgeWindow\b'
        $Definition | Should -Match '\$Out\.Windows = '
        # Diagnostyka: nowe widoczne okna (tytuł | klasa) od startu Edge.
        $Definition | Should -Match '\$Out\.NewWindows = @\(Get-B5K2NewWindows -Before \$VisibleBefore\)'
        # Ekran powitalny poza drzewem UIA: start z przełącznikiem Chromium --no-first-run (nie polityka), okno do 60 s.
        $Definition | Should -Match "Start-Process -FilePath \`$Exe -ArgumentList '--no-first-run'"
        $Definition | Should -Match '\$Poll -lt 120 -and \$null -eq \$Window'
    }
    It 'Initialize-B5K2Native: MSAA i SendMessage dla kontrolek Win32 (bez rejestracji pośredników UIA, która rzuca na VM)' {
        $Definition = (Get-Command Initialize-B5K2Native).Definition
        $Definition | Should -Not -Match 'RegisterClientSideProviderAssembly'
        $Definition | Should -Match "Add-Type -ReferencedAssemblies 'Accessibility'"
        foreach ($Member in 'AccessibleObjectFromWindow', 'ChildByClass', 'AccItems', 'SendMessage') { $Definition | Should -Match $Member }
        # Kod C# kompiluje się (sama kompilacja pod inną nazwą klasy, nic nie jest wywoływane).
        $Source = [regex]::Match($Definition, "(?s)@'\r?\n(.*?)\r?\n'@").Groups[1].Value
        $Source | Should -Match 'public static class B5User32'
        $Name = 'B5User32Compile' + [guid]::NewGuid().ToString('N')
        { Add-Type -ReferencedAssemblies 'Accessibility' -TypeDefinition ($Source -replace 'class B5User32', "class $Name") } | Should -Not -Throw
        ($Name -as [type]) | Should -Not -BeNullOrEmpty
    }
    It 'Get-B5K2App: -Exe spoza ValidateSet rzuca' {
        foreach ($Exe in 'calc.exe', 'C:\Windows\System32\notepad.exe', 'mspaint') {
            { Get-B5K2App -Exe $Exe -Label 'x' } | Should -Throw -ExceptionType ([System.Management.Automation.ParameterBindingException]) -Because $Exe
        }
    }
}

Describe 'Dyspozytor K2 i skrypt dziecka' {
    It 'Invoke-B5K2Ops: nieznana operacja rzuca; obsługiwane = Settings, Start, Taskbar, FolderOptions, Edge, App, Dictation' {
        Mock Initialize-B5K2Native { }
        Mock Close-B5K2Settings { }
        Mock Get-B5K2Settings { [pscustomobject]@{ Op = 'Settings'; Uri = $Uri; ExpandAll = [bool]$ExpandAll } }
        Mock Set-B5K2Toggle { [pscustomobject]@{ Op = 'Toggle'; Id = $Id; Want = $Want } }
        Mock Get-B5K2Start { [pscustomobject]@{ Op = 'Start' } }
        Mock Get-B5K2Taskbar { [pscustomobject]@{ Op = 'Taskbar' } }
        Mock Get-B5K2FolderOptions { [pscustomobject]@{ Op = 'FolderOptions'; Match = $Match } }
        Mock Get-B5K2Edge { [pscustomobject]@{ Op = 'Edge'; Urls = @($Urls) } }
        Mock Get-B5K2App { [pscustomobject]@{ Op = 'App'; Exe = $Exe } }
        Mock Get-B5K2Dictation { [pscustomobject]@{ Op = 'Dictation' } }
        foreach ($Old in 'Search', 'SearchHome', 'Widgets', 'Copilot', 'ContextMenu', 'TaskbarMenu', 'UpdateCheck', 'settings') {
            { Invoke-B5K2Ops -Ops @([pscustomobject]@{ Op = 'Start'; Label = 'a' }, [pscustomobject]@{ Op = $Old; Label = 'b' }) } | Should -Throw '*Nieznana operacja*' -Because $Old
        }
        Should -Invoke Get-B5K2Start -Times 0 -Exactly
        $Ops = @(
            [pscustomobject]@{ Op = 'Settings'; Uri = 'ms-settings:privacy-speech'; ExpandAll = $true; Label = 'a-1' }
            [pscustomobject]@{ Op = 'Start'; Label = 'a-2' }
            [pscustomobject]@{ Op = 'Taskbar'; Label = 'a-3' }
            [pscustomobject]@{ Op = 'FolderOptions'; Match = 'sync provider'; Label = 'a-4' }
            [pscustomobject]@{ Op = 'Edge'; Urls = @('edge://policy', 'edge://newtab'); Label = 'a-5' }
            [pscustomobject]@{ Op = 'App'; Exe = 'mspaint.exe'; Label = 'a-6' }
            [pscustomobject]@{ Op = 'Dictation'; Label = 'a-7' }
        )
        $Results = @(Invoke-B5K2Ops -Ops $Ops)
        @($Results | ForEach-Object { $_.Op }) | Should -Be @('Settings', 'Start', 'Taskbar', 'FolderOptions', 'Edge', 'App', 'Dictation')
        @($Results | Where-Object { $_.PSObject.Properties['Error'] -and $_.Error }).Count | Should -Be 0
        $Results[0].ExpandAll | Should -BeTrue
        $Results[3].Match | Should -Be 'sync provider'
        @($Results[4].Urls) | Should -Be @('edge://policy', 'edge://newtab')
        $Results[5].Exe | Should -Be 'mspaint.exe'
        # Toggle: wewnętrzna operacja przełącznika pozycji (Invoke-B5Candidate), tylko po AutomationId.
        $Toggle = @(Invoke-B5K2Ops -Ops @([pscustomobject]@{ Op = 'Toggle'; Uri = 'ms-settings:privacy-speech'; Id = 'SystemSettings_X_ToggleSwitch'; Want = 'Off'; Label = 't-1' }))
        $Toggle[0].Id | Should -Be 'SystemSettings_X_ToggleSwitch'
        $Toggle[0].Want | Should -Be 'Off'
    }
    It 'New-B5ChildScript: poprawna składnia, funkcje dziecka bez funkcji K1' {
        $Text = New-B5ChildScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Text | Should -Match '^param\(\[Parameter\(Mandatory\)\]\[string\]\$ResultFile, \[Parameter\(Mandatory\)\]\[string\]\$OpsFile\)'
        $Child = @(Get-B5ChildFunctionNames)
        foreach ($Name in $Child) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Text | Should -Match 'Move-Item -LiteralPath \$Temporary -Destination \$ResultFile -Force'
        foreach ($Name in $Child) { (Get-B5RemoteFunctionNames) | Should -Contain $Name }
        foreach ($Name in $Child) {
            $Ast = [System.Management.Automation.Language.Parser]::ParseInput((Get-Command $Name).Definition, [ref]$null, [ref]$null)
            $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-B5*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
            foreach ($Callee in $Called) { $Child | Should -Contain $Callee -Because "$Name woła $Callee" }
        }
        foreach ($NotInChild in 'Set-B5Value', 'Restore-B5Value', 'Restart-B5Explorer', 'Invoke-B5UserTask', 'Invoke-B5Candidate', 'Get-B5Snapshot', 'Get-B5K1Observation') { $Child | Should -Not -Contain $NotInChild }
        foreach ($Name in 'Get-B5K2FolderOptions', 'Get-B5K2Edge', 'Get-B5K2App', 'Get-B5K2Dictation') {
            $Ast = [System.Management.Automation.Language.Parser]::ParseInput((Get-Command $Name).Definition, [ref]$null, [ref]$null)
            $Calls = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() })
            $Calls[0] | Should -Be 'Assert-B5InsideVm' -Because "$Name zaczyna od bezpiecznika"
            $Calls | Should -Contain 'Save-B5K2Shot' -Because "$Name robi zrzut przez Save-B5K2Shot"
        }
    }
    It 'Get-B5RemoteScript: domknięta lista funkcji' {
        $Text = Get-B5RemoteScript
        $Errors = $null
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Sent = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-B5RemoteFunctionNames)
        foreach ($Name in $Sent) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-B5*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        # ConvertTo-B5Hashtable to funkcja zagnieżdżona w Get-B5PlanFromJson (wysyłana razem z nią).
        foreach ($Name in ($Called | Where-Object { $_ -ne 'ConvertTo-B5Hashtable' })) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-B5HostOnlyFunctionNames)) { $Text | Should -Not -Match "function $([regex]::Escape($HostOnly)) \{" }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Start-VM|Connect-VMNetworkAdapter'
    }
    It 'każda funkcja skryptu jest wysyłana albo na liście hosta; polecenia ze skutkiem tylko w swoich funkcjach' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:B5ScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name } | Where-Object { $_ -ne 'ConvertTo-B5Hashtable' })
        foreach ($Name in $Defined) { ((Get-B5RemoteFunctionNames) + (Get-B5HostOnlyFunctionNames)) | Should -Contain $Name }
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Allowed = @{
            'Stop-Process'                = @('Restart-B5Explorer', 'Get-B5K2Edge')
            'Restart-Computer'            = @('Restart-B5Vm')
            'Register-ScheduledTask'      = @('Invoke-B5UserTask')
            'Unregister-ScheduledTask'    = @('Invoke-B5UserTask')
            'Start-Process'               = @('Open-B5K2Settings', 'Get-B5K2FolderOptions', 'Get-B5K2Edge', 'Get-B5K2App')
            'Add-Type'                    = @('Initialize-B5K2Native')
            'Connect-VMNetworkAdapter'    = @('Invoke-B5VmGroup')
            'Disconnect-VMNetworkAdapter' = @('Invoke-B5VmGroup', 'Complete-B5VmCleanup')
            'Start-VM'                    = @('Invoke-B5VmGroup')
            'Restore-VmCleanState'        = @('Invoke-B5VmGroup', 'Complete-B5VmCleanup')
        }
        foreach ($Name in $Allowed.Keys) {
            foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq $Name })) { $Allowed[$Name] | Should -Contain (Get-CommandOwner $Command) -Because $Name }
        }
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Connect-VMNetworkAdapter' })) { $Command.Extent.Text | Should -Match "-SwitchName 'Default Switch'" }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg', 'reg.exe', 'Remove-AppxPackage', 'Disable-WindowsOptionalFeature', 'Stop-Computer', 'Invoke-Expression', 'iex',
            'Invoke-RestMethod', 'Invoke-WebRequest', 'Set-NetConnectionProfile', 'Stop-VM', 'Remove-VM', 'Restore-VMCheckpoint', 'Set-VMNetworkAdapter', 'cmd', 'cmd.exe', 'schtasks', 'schtasks.exe', 'Set-Service', 'Stop-Service') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:B5ScriptPath), [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|EncodedCommand'
    }
    It 'plik: BOM, Instrukcja uruchomienia i Opis działania, ValidateSet = Get-DsoVmNames, domyślnie DSO-EN-Home i DSO-EN-Pro' {
        $Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $script:B5ScriptPath))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:B5ScriptPath), [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
        $Script = Get-Command $script:B5ScriptPath
        ($Script.Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues | Should -Be @(Get-DsoVmNames)
        foreach ($Name in 'Invoke-B5VmGroup', 'Restart-B5Vm', 'Complete-B5VmCleanup') {
            ((Get-Command $Name).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues | Should -Be @(Get-DsoVmNames) -Because $Name
        }
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:B5ScriptPath), [ref]$null, [ref]$null)
        $Parameter = $Ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'VmName' }
        @($Parameter.DefaultValue.SafeGetValue()) | Should -Be @('DSO-EN-Home', 'DSO-EN-Pro')
        ($Script.Parameters['Group'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues |
            Should -Be @('discover', 'ads', 'telemetry', 'edge', 'apps', 'copilot', 'wording', 'reboot')
        foreach ($Name in 'Candidate', 'RebootCandidate', 'OutDirectory', 'CheckpointName', 'ReadyTimeoutSeconds', 'NoNetwork', 'ShowPlan') { $Script.Parameters.Keys | Should -Contain $Name }
    }
}

Describe 'Migawki, odczyty K1 i mechanizm z różnicy (czysta logika)' {
    It 'Compare-B5Snapshot: dodane, zmienione i usunięte wpisy; identyczne pominięte' {
        $Before = @{ 'HKU\S\A|x' = 'DWord:1'; 'HKU\S\A|y' = 'DWord:2'; 'HKU\S\B|z' = 'String:q' }
        $After = @{ 'HKU\S\A|x' = 'DWord:0'; 'HKU\S\A|y' = 'DWord:2'; 'HKU\S\C|n' = 'DWord:5' }
        $Diff = @(Compare-B5Snapshot -Before $Before -After $After)
        $Diff.Count | Should -Be 3
        ($Diff | Where-Object { $_.Path -eq 'HKU\S\A' -and $_.Name -eq 'x' }).After | Should -Be 'DWord:0'
    }
    It 'ConvertFrom-B5Diff: zapis DWord, usunięcie przy braku „po”, bez kluczy i szumu' {
        $Diff = @(
            [pscustomobject]@{ Path = 'HKU\S\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-353698Enabled'; Before = 'DWord:1'; After = 'DWord:0' }
            [pscustomobject]@{ Path = 'HKU\S\Software\X'; Name = 'Gone'; Before = 'DWord:1'; After = $null }
            [pscustomobject]@{ Path = 'HKU\S\Software\X'; Name = ''; Before = $null; After = '(klucz)' }
            [pscustomobject]@{ Path = 'HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer'; Name = 'LogonCount'; Before = 'QWord:3'; After = 'QWord:4' }
        )
        $Out = @(ConvertFrom-B5Diff -Diff $Diff)
        $Out.Count | Should -Be 2
        ($Out | Where-Object Name -eq 'SubscribedContent-353698Enabled').Value | Should -Be 0
        ($Out | Where-Object Name -eq 'Gone').Kind | Should -Be 'Delete'
    }
    It 'Resolve-B5Path i Get-B5SnapshotRoots: tylko HKLM\ i HKU\<SID>\; korzenie obejmują klucze kandydatów' {
        Resolve-B5Path -Path 'HKU\{SID}\Software\Policies\A' -UserSid $script:Sid | Should -Be "HKU\$($script:Sid)\Software\Policies\A"
        { Resolve-B5Path -Path 'HKCU\Software\A' -UserSid $script:Sid } | Should -Throw
        $Roots = @(Get-B5SnapshotRoots -UserSid $script:Sid)
        foreach ($Root in $Roots) { $Root | Should -Match '^(HKLM|HKU)\\' }
        foreach ($Spec in (Get-B5Plan)) {
            foreach ($Value in @(Get-SpecValues $Spec)) {
                $Path = Resolve-B5Path -Path $Value.Path -UserSid $script:Sid
                @($Roots | Where-Object { $Path -eq $_ -or $Path.StartsWith("$_\", [System.StringComparison]::OrdinalIgnoreCase) }).Count | Should -BeGreaterThan 0 -Because $Path
            }
        }
    }
    It 'Get-B5PlanFromJson i ConvertTo-B5Ops: hashtable z JSON, etykiety <pozycja>-<faza>-<nr>-<op>, argumenty przeniesione' {
        $One = @(Get-B5PlanFromJson -Json (ConvertTo-Json -InputObject @(@{ Id = 'a'; Observe = @(@{ Op = 'Edge'; Urls = @('edge://policy') }) }) -Depth 6))
        $One.Count | Should -Be 1
        $One[0] | Should -BeOfType [hashtable]
        @($One[0].Observe[0].Urls) | Should -Be @('edge://policy')
        $Ops = @(ConvertTo-B5Ops -CandidateId 'paint-ai' -Phase 'po' -Observe @(@{ Op = 'App'; Exe = 'mspaint.exe' }, @{ Op = 'Settings'; Uri = 'ms-settings:'; ExpandAll = $true }))
        $Ops[0].Label | Should -Be 'paint-ai-po-1-app'
        $Ops[0].Exe | Should -Be 'mspaint.exe'
        $Ops[1].Uri | Should -Be 'ms-settings:'
        $Ops[1].ExpandAll | Should -BeTrue
    }
    It 'Invoke-B5Observe: limit zadania K2 = 60 s + 90 s na operację + 60 s na każdy adres Edge' {
        Mock Invoke-B5UserTask { [pscustomobject]@{ Name = $Name; TimeoutSeconds = $TimeoutSeconds } }
        Mock Get-B5K1Observation { @() }
        $Edge = @{ Id = 'discover-edge'; Observe = @(@{ Op = 'Edge'; Urls = @('edge://settings', 'edge://policy', 'edge://newtab') }) }
        (Invoke-B5Observe -Spec $Edge -Phase 'przed').K2.TimeoutSeconds | Should -Be 330
        $Pages = @{ Id = 'x'; Observe = @(@{ Op = 'Settings'; Uri = 'ms-settings:'; ExpandAll = $true }, @{ Op = 'Taskbar' }) }
        (Invoke-B5Observe -Spec $Pages -Phase 'po').K2.TimeoutSeconds | Should -Be 240
        (Invoke-B5Observe -Spec @{ Id = 'y'; K1 = @('appx:Copilot') } -Phase 'po').K2 | Should -BeNullOrEmpty
        Should -Invoke Invoke-B5UserTask -Times 2 -Exactly
    }
    It 'Get-B5K1Observation: file: = wersja pliku (zmienne środowiskowe rozwinięte), brak pliku i nieznany odczyt jako dane' {
        $Out = @(Get-B5K1Observation -Spec @('file:%SystemRoot%\System32\notepad.exe', 'file:C:\nie-ma-takiego\msedge.exe', 'wusearch:'))
        $Out[0].Data | Should -Match '^\d+\.\d+'
        $Out[0].Error | Should -BeNullOrEmpty
        $Out[1].Data | Should -Be '(brak pliku)'
        $Out[2].Error | Should -Match 'Nieznany odczyt'
    }
}

Describe 'Invoke-B5Candidate (mocki: rejestr, UI i Eksplorator nietknięte)' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        Mock Assert-B5InsideVm { }
        Mock Get-B5Snapshot { @{} }
        Mock Compare-B5Snapshot { @() }
        Mock Read-B5Value { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $false; Exists = $false; Kind = $null; Value = $null; Hex = $null } }
        Mock Set-B5Value { $script:Calls.Add("set $Path|$Name=$Value"); [pscustomobject]@{ Path = $Path; Name = $Name; Ok = $true; Error = $null; CreatedKeys = @($Path) } }
        Mock Restore-B5Value { $script:Calls.Add("restore $Path|$Name keys=$($CreatedKeys -join ',')"); [pscustomobject]@{ Ok = $true; Error = $null; RemovedKeys = @($CreatedKeys) } }
        Mock Restart-B5Explorer { $script:Calls.Add('explorer'); [pscustomobject]@{ OldPids = @(1); NewPids = @(2) } }
        Mock Invoke-B5Observe { $script:Calls.Add("observe $Phase"); [pscustomobject]@{ Phase = $Phase } }
        Mock Invoke-B5UserTask { $script:Calls.Add("task $Name"); [pscustomobject]@{ Available = $true; Results = @([pscustomobject]@{ Before = 'On'; After = 'Off' }) } }
    }
    It 'wartości, Explorer=restart: przed -> zapis -> restart -> po restarcie -> cofnięcie (klucze utworzone przekazane) -> restart -> cofnięte' {
        $Path = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
        $Resolved = "HKU\$($script:Sid)\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
        $Spec = @{ Id = 'start-account-notifications'; Group = 'ads'; Explorer = 'restart'; Values = @(@{ Path = $Path; Name = 'Start_AccountNotifications'; Kind = 'DWord'; Value = 0 }); Observe = @(@{ Op = 'Start' }) }
        $Result = Invoke-B5Candidate -Spec $Spec -UserSid $script:Sid
        @($script:Calls) | Should -Be @('observe przed', "set $Resolved|Start_AccountNotifications=0", 'explorer', 'observe po-restarcie', "restore $Resolved|Start_AccountNotifications keys=$Resolved", 'explorer', 'observe cofniete')
        @($Result.Errors).Count | Should -Be 0
        $Result.Before.Phase | Should -Be 'przed'
        $Result.After | Should -BeNullOrEmpty
        $Result.AfterRestart.Phase | Should -Be 'po-restarcie'
        $Result.Undone.Phase | Should -Be 'cofniete'
        $Result.ToggleDiff | Should -BeNullOrEmpty
    }
    It 'Explorer=both: obserwacja przed restartem Eksploratora i po nim' {
        $Spec = @{ Id = 'copilot-taskbar-button'; Group = 'copilot'; Explorer = 'both'; Values = @(@{ Path = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'ShowCopilotButton'; Kind = 'DWord'; Value = 0 }); Observe = @(@{ Op = 'Taskbar' }) }
        $Result = Invoke-B5Candidate -Spec $Spec -UserSid $script:Sid
        $Index = $script:Calls.IndexOf('observe po')
        $Index | Should -BeGreaterThan 0
        $script:Calls[$Index + 1] | Should -Be 'explorer'
        $script:Calls[$Index + 2] | Should -Be 'observe po-restarcie'
        $Result.After.Phase | Should -Be 'po'
        $Result.AfterRestart.Phase | Should -Be 'po-restarcie'
    }
    It 'błąd obserwacji po zmianie: cofnięcie i tak wykonane, błąd w Errors' {
        Mock Invoke-B5Observe { $script:Calls.Add("observe $Phase"); if ($Phase -eq 'po') { throw 'UIA padło' }; [pscustomobject]@{ Phase = $Phase } }
        $Spec = @{ Id = 'notepad-ai'; Group = 'apps'; Explorer = 'none'; Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\WindowsNotepad'; Name = 'DisableAIFeatures'; Kind = 'DWord'; Value = 1 }); Observe = @(@{ Op = 'App'; Exe = 'notepad.exe' }) }
        $Result = Invoke-B5Candidate -Spec $Spec -UserSid $script:Sid
        $script:Calls | Should -Contain 'restore HKLM\SOFTWARE\Policies\WindowsNotepad|DisableAIFeatures keys=HKLM\SOFTWARE\Policies\WindowsNotepad'
        $script:Calls | Should -Contain 'observe cofniete'
        $script:Calls | Should -Not -Contain 'explorer'
        ($Result.Errors -join ' ') | Should -Match 'UIA padło'
    }
    It 'ObserveOnly: tylko obserwacja przed, bez zapisu, cofnięcia i Eksploratora' {
        $Result = Invoke-B5Candidate -Spec @{ Id = 'discover-edge'; Group = 'discover'; Explorer = 'none'; ObserveOnly = $true; Observe = @(@{ Op = 'Edge'; Urls = @('edge://policy') }) } -UserSid $script:Sid
        @($script:Calls) | Should -Be @('observe przed')
        @($Result.Errors).Count | Should -Be 0
        $Result.Id | Should -Be 'discover-edge'
        $Result.Before.Phase | Should -Be 'przed'
    }
    It 'przełącznik bez Toggle.Id: błąd „brak AutomationId – uruchom discover”, bez zadania K2 i bez zapisu' {
        $Spec = @{ Id = 'online-speech'; Group = 'telemetry'; Explorer = 'none'; Values = @(@{ Path = 'HKU\{SID}\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy'; Name = 'HasAccepted'; Kind = 'DWord'; Value = 0 })
            Toggle = @{ Uri = 'ms-settings:privacy-speech'; Match = 'Online speech'; Target = 'Off' }; Observe = @(@{ Op = 'Settings'; Uri = 'ms-settings:privacy-speech'; ExpandAll = $true }) }
        $Result = Invoke-B5Candidate -Spec $Spec -UserSid $script:Sid
        ($Result.Errors -join ' ') | Should -Match 'brak AutomationId – uruchom discover'
        @($script:Calls | Where-Object { $_ -like 'task *' -or $_ -like 'set *' }).Count | Should -Be 0
    }
    It 'przełącznik z różnicą (bez wartości z tabeli): Target, przeciwny, powrót; mechanizm z różnicy zapisany i cofnięty' {
        $script:Snap = 0
        Mock Get-B5Snapshot { $script:Snap++; @{ n = $script:Snap } }
        Mock Compare-B5Snapshot {
            $Cdm = 'HKU\S\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
            if ($Before.n -eq 1 -and $After.n -eq 2) { return @([pscustomobject]@{ Path = $Cdm; Name = 'SubscribedContent-338393Enabled'; Before = 'DWord:1'; After = 'DWord:0' }) }
            if ($Before.n -eq 2 -and $After.n -eq 3) { return @([pscustomobject]@{ Path = $Cdm; Name = 'SubscribedContent-338393Enabled'; Before = 'DWord:0'; After = 'DWord:1' }) }
            @()
        }
        $Spec = @{ Id = 'settings-suggestions-recheck'; Group = 'ads'; Explorer = 'none'
            Toggle = @{ Uri = 'ms-settings:privacy-general'; Id = 'SystemSettings_Privacy_EnableSuggestionsInSettings_ToggleSwitch'; Match = 'suggested content'; Target = 'Off' }
            Observe = @(@{ Op = 'Settings'; Uri = 'ms-settings:privacy-general'; ExpandAll = $true }) }
        $Result = Invoke-B5Candidate -Spec $Spec -UserSid $script:Sid
        @($script:Calls | Where-Object { $_ -like 'task *' }) | Should -Be @('task settings-suggestions-recheck-toggle1', 'task settings-suggestions-recheck-toggle2', 'task settings-suggestions-recheck-toggle3')
        Should -Invoke Invoke-B5UserTask -ParameterFilter { $Name -eq 'settings-suggestions-recheck-toggle1' -and $Ops[0].Want -eq 'Off' -and $Ops[0].Id -eq 'SystemSettings_Privacy_EnableSuggestionsInSettings_ToggleSwitch' }
        Should -Invoke Invoke-B5UserTask -ParameterFilter { $Name -eq 'settings-suggestions-recheck-toggle3' -and $Ops[0].Want -eq 'On' }
        @($Result.ToggleDiff.Mechanism).Count | Should -Be 1
        $script:Calls | Should -Contain 'set HKU\S\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager|SubscribedContent-338393Enabled=1'
        $script:Calls | Should -Contain 'set HKU\S\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager|SubscribedContent-338393Enabled=0'
        $script:Calls | Should -Contain 'observe mech-przeciwny'
        @($script:Calls | Where-Object { $_ -like 'restore *' }).Count | Should -Be 2
    }
    It 'przełącznik z różnicą i wartości z tabeli: różnica tylko jako dowód, zapisane są wartości z tabeli' {
        $script:Snap = 0
        Mock Get-B5Snapshot { $script:Snap++; @{ n = $script:Snap } }
        Mock Compare-B5Snapshot {
            if ($Before.n -eq 1 -and $After.n -eq 2) { return @([pscustomobject]@{ Path = 'HKU\S\Software\Other'; Name = 'X'; Before = 'DWord:1'; After = 'DWord:0' }) }
            @()
        }
        $Spec = @{ Id = 'online-speech'; Group = 'telemetry'; Explorer = 'none'; Values = @(@{ Path = 'HKU\{SID}\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy'; Name = 'HasAccepted'; Kind = 'DWord'; Value = 0 })
            Toggle = @{ Uri = 'ms-settings:privacy-speech'; Id = 'SystemSettings_X_ToggleSwitch'; Match = 'Online speech'; Target = 'Off' }; Observe = @(@{ Op = 'Dictation' }) }
        $Result = Invoke-B5Candidate -Spec $Spec -UserSid $script:Sid
        @($script:Calls | Where-Object { $_ -like 'task *' }).Count | Should -Be 3
        @($script:Calls | Where-Object { $_ -like 'set *' }) | Should -Be @("set HKU\$($script:Sid)\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy|HasAccepted=0")
        $script:Calls | Should -Not -Contain 'observe mech-przeciwny'
        @($Result.ToggleDiff.ToTargetDiff).Count | Should -Be 1
        @($Result.Errors).Count | Should -Be 0
    }
}

Describe 'Wybór pozycji i argumenty przebiegu' {
    It 'Select-B5Specs: grupa i filtr -Candidate; reboot = pozycje z -RebootCandidate; nieznane Id rzuca' {
        @(Select-B5Specs -Group ads | ForEach-Object { $_.Id }) | Should -Contain 'explorer-sync-provider-notifications'
        @(Select-B5Specs -Group telemetry -Candidate online-speech | ForEach-Object { $_.Id }) | Should -Be @('online-speech')
        @(Select-B5Specs -Group reboot -RebootCandidate 'activity-history', 'start-account-notifications' | ForEach-Object { $_.Id }) | Should -Be @('start-account-notifications', 'activity-history')
        { Select-B5Specs -Group reboot } | Should -Throw '*-RebootCandidate*'
        { Select-B5Specs -Group ads -Candidate 'nie-ma' } | Should -Throw '*Nieznana pozycja*'
        { Select-B5Specs -Group reboot -RebootCandidate 'nie-ma' } | Should -Throw '*Nieznana pozycja*'
    }
    It 'Get-B5RunArguments: bez -Group = ads, telemetry, edge, apps, copilot (bez discover, wording, reboot); -Candidate spoza wybranych grup rzuca' {
        $Arguments = Get-B5RunArguments -VmName 'DSO-EN-Pro' -OutDirectory 'C:\w' -Group $null -Candidate $null -RebootCandidate $null -CheckpointName 'c' -ReadyTimeoutSeconds 60
        @($Arguments.Groups) | Should -Be @('ads', 'telemetry', 'edge', 'apps', 'copilot')
        $Arguments.Network | Should -BeTrue
        $Arguments.Networks['ads'] | Should -BeTrue
        $Arguments.Networks['telemetry'] | Should -BeFalse
        $Arguments.Networks['copilot'] | Should -BeFalse
        $All = @($Arguments.Groups | ForEach-Object { @($Arguments.Plan[$_]) })
        foreach ($Id in $script:TableGroups.Keys) { $All | Should -Contain $Id }
        { Get-B5RunArguments -VmName 'DSO-EN-Pro' -OutDirectory 'C:\w' -Group @('ads') -Candidate @('online-speech') -RebootCandidate $null -CheckpointName 'c' -ReadyTimeoutSeconds 60 } | Should -Throw '*nie należy*'
        $Ordered = Get-B5RunArguments -VmName 'DSO-PL-Pro' -OutDirectory 'C:\w' -Group @('wording', 'reboot', 'discover') -Candidate $null -RebootCandidate @('activity-history') -CheckpointName 'c' -ReadyTimeoutSeconds 60 -Network $false
        @($Ordered.Groups) | Should -Be @('discover', 'wording', 'reboot')
        foreach ($Name in $Ordered.Groups) { $Ordered.Networks[$Name] | Should -BeFalse -Because "-NoNetwork wyłącza sieć zawsze ($Name)" }
    }
}

Describe 'Orkiestracja hosta (mocki Hyper-V)' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        Mock Assert-DsoVmLiveAllowed { }
        Mock Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock Restore-VmCleanState { $script:Calls.Add("revert $VmName") }
        Mock Connect-VMNetworkAdapter { $script:Calls.Add("connect $VMName $SwitchName") }
        Mock Disconnect-VMNetworkAdapter { $script:Calls.Add("disconnect $VMName") }
        Mock Start-VM { $script:Calls.Add("start $Name") }
        Mock Wait-VmReady { $script:Calls.Add('ready'); $null }
        Mock Invoke-Command -RemoveParameterValidation 'Session' { 'S-1-5-21-9-9-9-1000' }
        Mock Start-Sleep { }
        Mock Get-B5RemoteScript { 'function X {}' }
        Mock Invoke-B5VmPhase { $script:Calls.Add("phase $Phase"); [pscustomobject]@{ phase = $Phase } }
        Mock Restart-B5Vm { $script:Calls.Add('reboot'); $null }
        Mock Copy-Item -RemoveParameterValidation 'FromSession' { }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Save-ExploreResult { $script:Calls.Add("save $FileName"); $script:SavedResult = $Result; "C:\w\$VmName\$FileName" }
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Off' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = $null } }
        $script:SavedResult = $null
    }
    It 'grupa z siecią: revert -> sieć -> start -> faza main -> revert -> odłączenie -> sprawdzenie stanu końcowego -> zapis b5-<grupa>.json' {
        $Path = Invoke-B5VmGroup -VmName 'DSO-EN-Pro' -Group 'edge' -Specs @(@{ Id = 'edge-copilot' }) -OutDirectory $TestDrive
        $Path | Should -Be 'C:\w\DSO-EN-Pro\b5-edge.json'
        @($script:Calls) | Should -Be @('revert DSO-EN-Pro', 'connect DSO-EN-Pro Default Switch', 'start DSO-EN-Pro', 'ready', 'phase main', 'revert DSO-EN-Pro', 'disconnect DSO-EN-Pro', 'save b5-edge.json')
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        $script:SavedResult.network | Should -BeTrue
        $script:SavedResult.error | Should -BeNullOrEmpty
    }
    It 'grupa bez sieci: karta nie jest podłączana (odłączona przed startem), po pracy sprawdzona bez przełącznika' {
        $null = Invoke-B5VmGroup -VmName 'DSO-EN-Home' -Group 'telemetry' -Specs @(@{ Id = 'online-speech' }) -OutDirectory $TestDrive -Network $false
        @($script:Calls | Where-Object { $_ -like 'connect*' }).Count | Should -Be 0
        $script:Calls.IndexOf('disconnect DSO-EN-Home') | Should -BeLessThan $script:Calls.IndexOf('start DSO-EN-Home')
        $script:Calls[-1] | Should -Be 'save b5-telemetry.json'
        $script:SavedResult.network | Should -BeFalse
        $script:SavedResult.cleanup.Ok | Should -BeTrue
    }
    It 'nieudane odłączenie sieci: zapisane w wyniku i zgłoszone wyjątkiem, nie ukryte' {
        Mock Disconnect-VMNetworkAdapter { $script:Calls.Add("disconnect $VMName"); throw 'karta zajęta' }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Default Switch' } }
        { Invoke-B5VmGroup -VmName 'DSO-EN-Pro' -Group 'ads' -Specs @(@{ Id = 'lockscreen-tips' }) -OutDirectory $TestDrive 3>$null } | Should -Throw '*odłączenie sieci*karta zajęta*'
        $script:SavedResult.cleanup.Ok | Should -BeFalse
        $script:SavedResult.cleanup.DisconnectError | Should -Match 'karta zajęta'
        $script:SavedResult.error | Should -Match 'stan końcowy'
    }
    It 'stan końcowy niezgodny z kontraktem (VM nie Off, karta dalej na przełączniku): zapisany i zgłoszony' {
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Running' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Default Switch' } }
        { Invoke-B5VmGroup -VmName 'DSO-EN-Home' -Group 'copilot' -Specs @(@{ Id = 'copilot-taskbar-button' }) -OutDirectory $TestDrive -Network $false 3>$null } | Should -Throw '*stan końcowy*'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Running'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Default Switch'
    }
    It 'grupa reboot: apply -> restart -> after -> restart -> undone' {
        $null = Invoke-B5VmGroup -VmName 'DSO-EN-Home' -Group 'reboot' -Specs @(@{ Id = 'activity-history' }) -OutDirectory $TestDrive -Network $false
        @($script:Calls | Where-Object { $_ -like 'phase *' -or $_ -eq 'reboot' }) | Should -Be @('phase apply', 'reboot', 'phase after', 'reboot', 'phase undone')
        $script:Calls[-1] | Should -Be 'save b5-reboot.json'
    }
    It 'błąd fazy: wynik częściowy zapisany, revert i odłączenie wykonane, wyjątek z nazwą grupy' {
        Mock Invoke-B5VmPhase { throw 'faza padła' }
        { Invoke-B5VmGroup -VmName 'DSO-EN-Pro' -Group 'apps' -Specs @(@{ Id = 'paint-ai' }) -OutDirectory $TestDrive } | Should -Throw "*grupa 'apps'*faza padła*"
        $script:Calls[-3] | Should -Be 'revert DSO-EN-Pro'
        $script:Calls[-2] | Should -Be 'disconnect DSO-EN-Pro'
        $script:Calls[-1] | Should -Be 'save b5-apps.json'
        $script:SavedResult.error | Should -Match 'faza padła'
    }
    It 'błąd uwierzytelnienia: przekazany dalej (stop całości), revert i odłączenie wykonane' {
        Mock Wait-VmReady { $E = [System.InvalidOperationException]::new('błąd uwierzytelnienia PowerShell Direct'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-B5VmGroup -VmName 'DSO-EN-Pro' -Group 'ads' -Specs @(@{ Id = 'lockscreen-tips' }) -OutDirectory $TestDrive } | Should -Throw '*uwierzytelnienia*'
        $script:Calls | Should -Contain 'revert DSO-EN-Pro'
        $script:Calls | Should -Contain 'disconnect DSO-EN-Pro'
        $script:Calls[-1] | Should -Be 'save b5-ads.json'
    }
    It 'Invoke-B5Exploration: sieć wg Get-B5GroupNetwork (bez sieci = karta nie jest podłączana); błąd uwierzytelnienia zatrzymuje całość' {
        $script:Network = @{}
        Mock Invoke-B5VmGroup { $script:Network[$Group] = $Network; if ($Group -eq 'ads') { throw 'zwykły błąd' }; "C:\w\$VmName\b5-$Group.json" }
        $Arguments = Get-B5RunArguments -VmName 'DSO-EN-Pro' -OutDirectory 'C:\w' -Group $null -Candidate $null -RebootCandidate $null -CheckpointName 'c' -ReadyTimeoutSeconds 60
        $Outcome = Invoke-B5Exploration -Arguments $Arguments -Candidate $null -RebootCandidate $null 6>$null
        @($Outcome.Errors).Count | Should -Be 1
        @($Outcome.Paths).Count | Should -Be 4
        $script:Network['ads'] | Should -BeTrue
        $script:Network['edge'] | Should -BeTrue
        $script:Network['apps'] | Should -BeTrue
        $script:Network['telemetry'] | Should -BeFalse
        $script:Network['copilot'] | Should -BeFalse
        $NoNetwork = Get-B5RunArguments -VmName 'DSO-EN-Pro' -OutDirectory 'C:\w' -Group @('edge') -Candidate $null -RebootCandidate $null -CheckpointName 'c' -ReadyTimeoutSeconds 60 -Network $false
        $null = Invoke-B5Exploration -Arguments $NoNetwork -Candidate $null -RebootCandidate $null 6>$null
        $script:Network['edge'] | Should -BeFalse
        Mock Invoke-B5VmGroup { $E = [System.InvalidOperationException]::new('x'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-B5Exploration -Arguments $Arguments -Candidate $null -RebootCandidate $null 6>$null } | Should -Throw
        Should -Invoke Invoke-B5VmGroup -Times 1 -Exactly -Scope It -ParameterFilter { $Group -eq 'telemetry' }
    }
}

Describe 'Blok główny SKRYPTU (-ShowPlan; bez VM)' {
    It '-ShowPlan: kod 0; bez -OutDirectory kod 1' {
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Be '1'
        $Output = & $script:B5ScriptPath -ShowPlan -VmName DSO-EN-Pro -Group telemetry, reboot -RebootCandidate activity-history -OutDirectory (Join-Path $TestDrive 'w') 6>$null
        $LASTEXITCODE | Should -Be 0
        $Plan = ($Output | Out-String) | ConvertFrom-Json
        @($Plan.Groups) | Should -Be @('telemetry', 'reboot')
        @($Plan.Plan.reboot) | Should -Be @('activity-history')
        @($Plan.Plan.telemetry) | Should -Contain 'online-speech'
        $Plan.Networks.telemetry | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        & $script:B5ScriptPath -ShowPlan 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
}

AfterAll { Clear-NoLiveVm }
