BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-E4A1Exploration.ps1')
    $script:E4A1ScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-E4A1Exploration.ps1'
    $script:Sid = 'S-1-5-21-1-2-3-1000'

    function script:Get-CommandOwner {
        param($Node)
        $Parent = $Node.Parent
        while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
        if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
    }
    function script:Get-PlanValue {
        param([string]$Id)
        @(Get-E4A1Plan | Where-Object { $_.Id -eq $Id })[0]
    }
}

Describe 'Plan pozycji (dane z planu Tasku 4, dosłownie)' {
    It 'Id unikalne, grupy znane, ścieżki tylko HKLM\ i HKU\{SID}\, typy DWord/String' {
        $Plan = @(Get-E4A1Plan)
        $Ids = @($Plan | ForEach-Object { $_.Id })
        ($Ids | Sort-Object -Unique).Count | Should -Be $Ids.Count
        foreach ($Spec in $Plan) {
            $Spec.Group | Should -BeIn @('ai', 'search', 'ads', 'telemetry', 'ux')
            $Spec.Id | Should -Match '^[a-z0-9-]+$'
            if ($Spec.ContainsKey('Values')) {
                foreach ($Value in $Spec.Values) {
                    $Value.Path | Should -Match '^(HKLM\\SOFTWARE\\|HKU\\\{SID\}\\)'
                    $Value.Kind | Should -BeIn @('DWord', 'String')
                }
            }
            if ($Spec.ContainsKey('Explorer')) { $Spec.Explorer | Should -BeIn @('none', 'restart', 'both') }
            if ($Spec.ContainsKey('Toggle')) { $Spec.Toggle.Target | Should -BeIn @('On', 'Off'); $Spec.Toggle.Uri | Should -Match '^ms-settings:[a-z0-9-]+$' }
            foreach ($Op in @($Spec.Observe)) { $Op.Op | Should -BeIn @('Settings', 'Search', 'SearchHome', 'Start', 'Taskbar', 'Widgets', 'Copilot', 'ContextMenu', 'TaskbarMenu', 'UpdateCheck') }
        }
    }
    It '<Id>: <Name> = <Value> w <Path>' -ForEach @(
        @{ Id = 'copilot-policy'; Path = 'HKU\{SID}\Software\Policies\Microsoft\Windows\WindowsCopilot'; Name = 'TurnOffWindowsCopilot'; Value = 1 }
        @{ Id = 'recall-policy'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI'; Name = 'AllowRecallEnablement'; Value = 0 }
        @{ Id = 'recall-snapshots'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI'; Name = 'DisableAIDataAnalysis'; Value = 1 }
        @{ Id = 'clicktodo'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI'; Name = 'DisableClickToDo'; Value = 1 }
        @{ Id = 'widgets-policy'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Dsh'; Name = 'AllowNewsAndInterests'; Value = 0 }
        @{ Id = 'search-connectedsearchuseweb'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name = 'ConnectedSearchUseWeb'; Value = 0 }
        @{ Id = 'search-disablewebsearch'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name = 'DisableWebSearch'; Value = 1 }
        @{ Id = 'search-box-suggestions'; Path = 'HKU\{SID}\Software\Policies\Microsoft\Windows\Explorer'; Name = 'DisableSearchBoxSuggestions'; Value = 1 }
        @{ Id = 'search-bingsearchenabled'; Path = 'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Search'; Name = 'BingSearchEnabled'; Value = 0 }
        @{ Id = 'search-highlights'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name = 'EnableDynamicContentInWSB'; Value = 0 }
        @{ Id = 'consumer-features'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsConsumerFeatures'; Value = 1 }
        @{ Id = 'spotlight-all'; Path = 'HKU\{SID}\Software\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsSpotlightFeatures'; Value = 1 }
        @{ Id = 'spotlight-settings'; Path = 'HKU\{SID}\Software\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableWindowsSpotlightOnSettings'; Value = 1 }
        @{ Id = 'spotlight-thirdparty'; Path = 'HKU\{SID}\Software\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableThirdPartySuggestions'; Value = 1 }
        @{ Id = 'softlanding'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableSoftLanding'; Value = 1 }
        @{ Id = 'hide-recommended'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer'; Name = 'HideRecommendedSection'; Value = 1 }
        @{ Id = 'hide-recommended-user'; Path = 'HKU\{SID}\Software\Policies\Microsoft\Windows\Explorer'; Name = 'HideRecommendedSection'; Value = 1 }
        @{ Id = 'hide-personalized-sites'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer'; Name = 'HideRecommendedPersonalizedSites'; Value = 1 }
        @{ Id = 'adid-policy'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo'; Name = 'DisabledByGroupPolicy'; Value = 1 }
        @{ Id = 'tailored-policy'; Path = 'HKU\{SID}\Software\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableTailoredExperiencesWithDiagnosticData'; Value = 1 }
        @{ Id = 'allowtelemetry'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'AllowTelemetry'; Value = 1 }
        @{ Id = 'limit-diagnostic-log'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'LimitDiagnosticLogCollection'; Value = 1 }
        @{ Id = 'limit-dump'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'LimitDumpCollection'; Value = 1 }
        @{ Id = 'feedback-notifications'; Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name = 'DoNotShowFeedbackNotifications'; Value = 1 }
        @{ Id = 'game-dvr'; Path = 'HKU\{SID}\System\GameConfigStore'; Name = 'GameDVR_Enabled'; Value = 0 }
    ) {
        $Spec = Get-PlanValue -Id $Id
        $Spec | Should -Not -BeNullOrEmpty
        $Entry = @($Spec.Values | Where-Object { $_.Name -eq $Name -and $_.Path -eq $Path })
        $Entry.Count | Should -Be 1
        $Entry[0].Kind | Should -Be 'DWord'
        $Entry[0].Value | Should -Be $Value
    }
    It 'klasyczne menu PPM: pusty ciąg (wartość domyślna) w HKU\{SID}\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32; restart Eksploratora' {
        $Spec = Get-PlanValue -Id 'classic-context-menu'
        $Spec.Values[0].Path | Should -BeExactly 'HKU\{SID}\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32'
        $Spec.Values[0].Name | Should -BeExactly ''
        $Spec.Values[0].Kind | Should -Be 'String'
        $Spec.Values[0].Value | Should -BeExactly ''
        $Spec.Explorer | Should -Be 'restart'
        $Spec.Group | Should -Be 'ux'
    }
    It 'przełączniki oficjalne (AutomationId z Ustawień 26H2): TaskbarDa, wyniki z sieci, Bing, sugestie, reklamy, diagnostyka, Zakończ zadanie' {
        (Get-PlanValue -Id 'widgets-taskbarda').Toggle.Id | Should -Be 'SystemSettings_DesktopTaskbar_Da_ToggleSwitch'
        (Get-PlanValue -Id 'search-web-toggle').Toggle.Id | Should -Be 'SystemSettings_Search_GlobalWebSearchToggle_ToggleSwitch'
        (Get-PlanValue -Id 'advertising-id-toggle').Toggle.Id | Should -Be 'SystemSettings_Privacy_AdvertisingIdEnabled_ToggleSwitch'
        (Get-PlanValue -Id 'optional-diagnostics-toggle').Toggle.Id | Should -Be 'SystemSettings_Privacy_Diagnostic_Data_Toggle_2_ToggleSwitch'
        $EndTask = Get-PlanValue -Id 'taskbar-endtask-toggle'
        $EndTask.Toggle.Id | Should -Be 'SystemSettings_Taskbar_Endtask_ToggleSwitch'
        $EndTask.Toggle.Target | Should -Be 'On'
        $EndTask.Group | Should -Be 'ux'
        foreach ($Id in 'tips-toggle', 'welcome-toggle', 'finish-setup-toggle') { (Get-PlanValue -Id $Id).Toggle.Expand | Should -Contain 'Ustawienia dodatkowe' }
    }
    It 'DiagTrack: typ startu Disabled + zatrzymanie, obserwacja Windows Update; brak AllowTelemetry=0 i polityk blokujących Ustawienia' {
        $Spec = Get-PlanValue -Id 'diagtrack'
        $Spec.Service.Name | Should -Be 'DiagTrack'
        $Spec.Service.StartType | Should -Be 'Disabled'
        $Spec.Service.Stop | Should -BeTrue
        $Spec.K1 | Should -Contain 'wusearch:'
        @($Spec.Observe | ForEach-Object { $_.Op }) | Should -Contain 'UpdateCheck'
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4A1ScriptPath), [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'ConfigureTelemetryOptInSettingsUx|DisableDiagnosticDataViewer'
        foreach ($Spec in (Get-E4A1Plan)) { foreach ($Value in @($Spec.Values)) { if ($Value.Name -eq 'AllowTelemetry') { $Value.Value | Should -Be 1 } } }
    }
}

Describe 'Bezpieczniki (VM-only, mock-only)' {
    It 'Test-E4A1VmComputerName: tylko DSO-26H2-HOME/PRO' {
        Test-E4A1VmComputerName -ComputerName 'dso-26h2-pro' | Should -BeTrue
        Test-E4A1VmComputerName -ComputerName 'DSO-26H2-HOME' | Should -BeTrue
        Test-E4A1VmComputerName -ComputerName 'BRUTEFORCE_LTSC' | Should -BeFalse
        Test-E4A1VmComputerName -ComputerName '' | Should -BeFalse
    }
    It 'Assert-E4A1InsideVm rzuca w trybie mock-only, nawet z nazwą VM; poza trybem rzuca na hoście' {
        { Assert-E4A1InsideVm -ComputerName 'DSO-26H2-PRO' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        $Saved = $env:DSO_VM_TESTS_MOCK_ONLY
        try {
            [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $null)
            { Assert-E4A1InsideVm -ComputerName 'HOST-PC' } | Should -Throw '*tylko w VM*'
            { Assert-E4A1InsideVm -ComputerName 'DSO-26H2-HOME' } | Should -Not -Throw
        }
        finally { $env:DSO_VM_TESTS_MOCK_ONLY = $Saved }
    }
    It '<Name> rzuca przed jakimkolwiek skutkiem (rejestr, procesy, zadania, usługi)' -ForEach @(
        @{ Name = 'Set-E4A1Value'; Code = { Set-E4A1Value -Path 'HKLM\SOFTWARE\Policies\X' -Name 'A' -Kind DWord -Value 1 } }
        @{ Name = 'Restore-E4A1Value'; Code = { Restore-E4A1Value -Path 'HKLM\SOFTWARE\Policies\X' -Name 'A' -Before ([pscustomobject]@{ Exists = $false }) } }
        @{ Name = 'Restart-E4A1Explorer'; Code = { Restart-E4A1Explorer } }
        @{ Name = 'Invoke-E4A1UserTask'; Code = { Invoke-E4A1UserTask -Name 'x' -Ops @() } }
        @{ Name = 'Invoke-E4A1Candidate'; Code = { Invoke-E4A1Candidate -Spec @{ Id = 'x'; Group = 'ai' } -UserSid $script:Sid } }
        @{ Name = 'Invoke-E4A1RebootPhase'; Code = { Invoke-E4A1RebootPhase -Specs @(@{ Id = 'x' }) -UserSid $script:Sid -Phase apply } }
        @{ Name = 'Invoke-E4A1Collection'; Code = { Invoke-E4A1Collection -UserSid $script:Sid -PlanJson '[]' -Phase main } }
    ) {
        Mock Stop-Process { throw 'nie wolno' }
        Mock Register-ScheduledTask { throw 'nie wolno' }
        Mock Set-Service { throw 'nie wolno' }
        Mock Get-E4A1Snapshot { throw 'nie wolno' }
        Mock Open-E4A1BaseKey { throw 'nie wolno' }
        { & $Code } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke Stop-Process -Times 0 -Exactly
        Should -Invoke Register-ScheduledTask -Times 0 -Exactly
        Should -Invoke Set-Service -Times 0 -Exactly
        Should -Invoke Get-E4A1Snapshot -Times 0 -Exactly
        Should -Invoke Open-E4A1BaseKey -Times 0 -Exactly
    }
}

Describe 'Migawki rejestru i mechanizm z różnicy przełącznika (czysta logika)' {
    It 'ConvertTo-E4A1SnapshotValue: typ i wartość; binaria > 64 B jako długość + SHA1' {
        ConvertTo-E4A1SnapshotValue -Raw 1 -Kind 'DWord' | Should -Be 'DWord:1'
        ConvertTo-E4A1SnapshotValue -Raw 'abc' -Kind 'String' | Should -Be 'String:abc'
        ConvertTo-E4A1SnapshotValue -Raw ([byte[]](1, 2)) -Kind 'Binary' | Should -Be 'Binary:01-02'
        ConvertTo-E4A1SnapshotValue -Raw ([byte[]](1..100)) -Kind 'Binary' | Should -Match '^Binary:len=100:sha1=[0-9A-F]{16}$'
        ConvertTo-E4A1SnapshotValue -Raw $null -Kind 'DWord' | Should -BeNullOrEmpty
    }
    It 'Compare-E4A1Snapshot: dodane, zmienione i usunięte wpisy; identyczne pominięte' {
        $Before = @{ 'HKU\S\A|x' = 'DWord:1'; 'HKU\S\A|y' = 'DWord:2'; 'HKU\S\A|' = '(klucz)'; 'HKU\S\B|z' = 'String:q' }
        $After = @{ 'HKU\S\A|x' = 'DWord:0'; 'HKU\S\A|y' = 'DWord:2'; 'HKU\S\A|' = '(klucz)'; 'HKU\S\C|n' = 'DWord:5' }
        $Diff = @(Compare-E4A1Snapshot -Before $Before -After $After)
        $Diff.Count | Should -Be 3
        ($Diff | Where-Object { $_.Path -eq 'HKU\S\A' -and $_.Name -eq 'x' }).After | Should -Be 'DWord:0'
        ($Diff | Where-Object { $_.Path -eq 'HKU\S\B' }).After | Should -BeNullOrEmpty
        ($Diff | Where-Object { $_.Path -eq 'HKU\S\C' }).Before | Should -BeNullOrEmpty
    }
    It 'Test-E4A1NoisyPath: liczniki i bufory powłoki to szum; ContentDeliveryManager, Explorer\Advanced i Policies nie' {
        Test-E4A1NoisyPath -Path 'HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{X}\Count' -Name 'a' | Should -BeTrue
        Test-E4A1NoisyPath -Path 'HKU\S\Software\Microsoft\Windows\CurrentVersion\WSX\WSXPacks\P\1' -Name 'packOpenedCount' | Should -BeTrue
        Test-E4A1NoisyPath -Path 'HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer' -Name 'LogonCount' | Should -BeTrue
        Test-E4A1NoisyPath -Path 'HKU\S\Software\Microsoft\Windows\CurrentVersion\Start' -Name 'Config' | Should -BeTrue
        Test-E4A1NoisyPath -Path 'HKU\S\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' -Name 'SubscribedContent-338389Enabled' | Should -BeFalse
        Test-E4A1NoisyPath -Path 'HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name 'TaskbarDa' | Should -BeFalse
        Test-E4A1NoisyPath -Path 'HKLM\SOFTWARE\Policies\Microsoft\Dsh' -Name 'AllowNewsAndInterests' | Should -BeFalse
    }
    It 'ConvertFrom-E4A1Diff: zapis DWord/String, usunięcie przy braku „po”, bez kluczy, binariów i szumu' {
        $Diff = @(
            [pscustomobject]@{ Path = 'HKU\S\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'; Name = 'SubscribedContent-338389Enabled'; Before = 'DWord:1'; After = 'DWord:0' }
            [pscustomobject]@{ Path = 'HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'TaskbarDa'; Before = $null; After = 'DWord:0' }
            [pscustomobject]@{ Path = 'HKU\S\Software\X'; Name = 'Neg'; Before = $null; After = 'DWord:-1' }
            [pscustomobject]@{ Path = 'HKU\S\Software\X'; Name = 'Text'; Before = 'String:a'; After = 'String:' }
            [pscustomobject]@{ Path = 'HKU\S\Software\X'; Name = 'Gone'; Before = 'DWord:1'; After = $null }
            [pscustomobject]@{ Path = 'HKU\S\Software\X'; Name = ''; Before = $null; After = '(klucz)' }
            [pscustomobject]@{ Path = 'HKU\S\Software\X'; Name = 'Blob'; Before = 'Binary:01'; After = 'Binary:02' }
            [pscustomobject]@{ Path = 'HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer'; Name = 'LogonCount'; Before = 'QWord:3'; After = 'QWord:4' }
        )
        $Out = @(ConvertFrom-E4A1Diff -Diff $Diff)
        $Out.Count | Should -Be 5
        ($Out | Where-Object Name -eq 'SubscribedContent-338389Enabled').Value | Should -Be 0
        ($Out | Where-Object Name -eq 'TaskbarDa').Kind | Should -Be 'DWord'
        ($Out | Where-Object Name -eq 'Neg').Value | Should -Be -1
        ($Out | Where-Object Name -eq 'Text').Value | Should -BeExactly ''
        ($Out | Where-Object Name -eq 'Gone').Kind | Should -Be 'Delete'
    }
    It 'Resolve-E4A1Path: {SID} -> SID; inne gałęzie odrzucone' {
        Resolve-E4A1Path -Path 'HKU\{SID}\Software\Policies\A' -UserSid $script:Sid | Should -Be "HKU\$($script:Sid)\Software\Policies\A"
        { Resolve-E4A1Path -Path 'HKCU\Software\A' -UserSid $script:Sid } | Should -Throw
    }
    It 'Get-E4A1SnapshotRoots: Policies (HKLM i HKU), CurrentVersion użytkownika i Siuf, bez HKCU' {
        $Roots = @(Get-E4A1SnapshotRoots -UserSid $script:Sid)
        $Roots | Should -Contain 'HKLM\SOFTWARE\Policies'
        $Roots | Should -Contain "HKU\$($script:Sid)\Software\Policies"
        $Roots | Should -Contain "HKU\$($script:Sid)\Software\Microsoft\Windows\CurrentVersion"
        $Roots | Should -Contain "HKU\$($script:Sid)\Software\Microsoft\Siuf"
        foreach ($Root in $Roots) { $Root | Should -Match '^(HKLM|HKU)\\' }
    }
    It 'Get-E4A1PlanFromJson: tablica JSON -> lista hashtable (także jednoelementowa), tablice zagnieżdżone zostają tablicami' {
        $One = @(Get-E4A1PlanFromJson -Json (ConvertTo-Json -InputObject @(@{ Id = 'a'; Observe = @(@{ Op = 'Start' }); Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\A'; Name = 'x'; Kind = 'DWord'; Value = 1 }) }) -Depth 6))
        $One.Count | Should -Be 1
        $One[0] | Should -BeOfType [hashtable]
        $One[0].Id | Should -Be 'a'
        @($One[0].Observe).Count | Should -Be 1
        $One[0].Observe[0].Op | Should -Be 'Start'
        $One[0].Values[0].Value | Should -Be 1
        $Two = @(Get-E4A1PlanFromJson -Json (ConvertTo-Json -InputObject @(@{ Id = 'a' }, @{ Id = 'b' }) -Depth 4))
        @($Two | ForEach-Object { $_.Id }) | Should -Be @('a', 'b')
    }
    It 'ConvertTo-E4A1Ops: etykiety zrzutów <pozycja>-<faza>-<nr>-<op> małymi literami, argumenty przeniesione' {
        $Ops = @(ConvertTo-E4A1Ops -CandidateId 'widgets-policy' -Phase 'po' -Observe @(@{ Op = 'Taskbar' }, @{ Op = 'Settings'; Uri = 'ms-settings:taskbar' }))
        $Ops[0].Label | Should -Be 'widgets-policy-po-1-taskbar'
        $Ops[1].Label | Should -Be 'widgets-policy-po-2-settings'
        $Ops[1].Uri | Should -Be 'ms-settings:taskbar'
    }
}

Describe 'Invoke-E4A1Candidate (mocki: rejestr, UI i Eksplorator nietknięte)' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        Mock Assert-E4A1InsideVm { }
        Mock Get-E4A1Snapshot { @{} }
        Mock Compare-E4A1Snapshot { @() }
        Mock Read-E4A1Value { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $false; Exists = $false; Kind = $null; Value = $null; Hex = $null } }
        Mock Set-E4A1Value { $script:Calls.Add("set $Path|$Name=$Value"); [pscustomobject]@{ Path = $Path; Name = $Name; Ok = $true; Error = $null; CreatedKeys = @($Path) } }
        Mock Restore-E4A1Value { $script:Calls.Add("restore $Path|$Name keys=$($CreatedKeys -join ',')"); [pscustomobject]@{ Ok = $true; Error = $null; RemovedKeys = @($CreatedKeys) } }
        Mock Restart-E4A1Explorer { $script:Calls.Add('explorer'); [pscustomobject]@{ OldPids = @(1); NewPids = @(2) } }
        Mock Invoke-E4A1Observe { $script:Calls.Add("observe $Phase"); [pscustomobject]@{ Phase = $Phase } }
        Mock Invoke-E4A1UserTask { $script:Calls.Add("task $Name"); [pscustomobject]@{ Available = $true; Results = @([pscustomobject]@{ Before = 'On'; After = 'Off' }) } }
        Mock Set-Service { $script:Calls.Add("set-service $Name $StartupType") }
        Mock Stop-Service { $script:Calls.Add("stop-service $Name") }
        Mock Start-Service { $script:Calls.Add("start-service $Name") }
        Mock Get-Service { [pscustomobject]@{ Name = $Name; Status = 'Running'; StartType = 'Automatic' } }
    }
    It 'wartości: przed -> zapis -> restart Eksploratora -> po -> cofnięcie (klucze utworzone przekazane) -> restart -> cofnięte' {
        $Spec = @{ Id = 'hide-recommended'; Group = 'ads'; Explorer = 'restart'; Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer'; Name = 'HideRecommendedSection'; Kind = 'DWord'; Value = 1 }); Observe = @(@{ Op = 'Start' }) }
        $Result = Invoke-E4A1Candidate -Spec $Spec -UserSid $script:Sid
        @($Script:Calls) | Should -Be @(
            'observe przed', 'set HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer|HideRecommendedSection=1', 'explorer', 'observe po',
            'restore HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer|HideRecommendedSection keys=HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer', 'explorer', 'observe cofniete')
        @($Result.Errors).Count | Should -Be 0
        @($Result.Applied).Count | Should -Be 1
    }
    It 'Explorer=both: obserwacja przed restartem i po restarcie' {
        $Spec = @{ Id = 'widgets-policy'; Group = 'ai'; Explorer = 'both'; Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Dsh'; Name = 'AllowNewsAndInterests'; Kind = 'DWord'; Value = 0 }); Observe = @(@{ Op = 'Taskbar' }) }
        $null = Invoke-E4A1Candidate -Spec $Spec -UserSid $script:Sid
        $Index = $script:Calls.IndexOf('observe po-bez-restartu')
        $Index | Should -BeGreaterThan 0
        $script:Calls[$Index + 1] | Should -Be 'explorer'
        $script:Calls[$Index + 2] | Should -Be 'observe po'
    }
    It 'błąd obserwacji po zmianie: cofnięcie i tak wykonane, błąd w Errors' {
        Mock Invoke-E4A1Observe { $script:Calls.Add("observe $Phase"); if ($Phase -eq 'po') { throw 'UIA padło' }; [pscustomobject]@{ Phase = $Phase } }
        $Spec = @{ Id = 'softlanding'; Group = 'ads'; Explorer = 'none'; Values = @(@{ Path = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name = 'DisableSoftLanding'; Kind = 'DWord'; Value = 1 }); Observe = @(@{ Op = 'Settings'; Uri = 'ms-settings:notifications' }) }
        $Result = Invoke-E4A1Candidate -Spec $Spec -UserSid $script:Sid
        $script:Calls | Should -Contain 'restore HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent|DisableSoftLanding keys=HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent'
        $script:Calls | Should -Contain 'observe cofniete'
        ($Result.Errors -join ' ') | Should -Match 'UIA padło'
    }
    It 'ObserveOnly: tylko obserwacja przed, bez zapisu, cofnięcia i Eksploratora' {
        $Result = Invoke-E4A1Candidate -Spec @{ Id = 'search-baseline'; Group = 'search'; Explorer = 'restart'; ObserveOnly = $true; Observe = @(@{ Op = 'Search'; Query = 'pogoda' }) } -UserSid $script:Sid
        @($script:Calls) | Should -Be @('observe przed')
        @($Result.Errors).Count | Should -Be 0
        $Result.Id | Should -Be 'search-baseline'
    }
    It 'usługa: Disabled + Stop, cofnięcie do typu i stanu sprzed (Automatic, Running)' {
        $Spec = @{ Id = 'diagtrack'; Group = 'telemetry'; Explorer = 'none'; Service = @{ Name = 'DiagTrack'; StartType = 'Disabled'; Stop = $true }; Observe = @(@{ Op = 'UpdateCheck' }) }
        $Result = Invoke-E4A1Candidate -Spec $Spec -UserSid $script:Sid
        $script:Calls | Should -Contain 'set-service DiagTrack Disabled'
        $script:Calls | Should -Contain 'stop-service DiagTrack'
        $script:Calls | Should -Contain 'set-service DiagTrack Automatic'
        $script:Calls | Should -Contain 'start-service DiagTrack'
        $script:Calls.IndexOf('set-service DiagTrack Automatic') | Should -BeGreaterThan $script:Calls.IndexOf('observe po')
        $Result.Service.BeforeStartType | Should -Be 'Automatic'
    }
    It 'przełącznik: Target, przeciwny, powrót do stanu wyjściowego; mechanizm z różnicy zapisany przez API i cofnięty' {
        $script:Snap = 0
        Mock Get-E4A1Snapshot { $script:Snap++; @{ n = $script:Snap } }
        Mock Compare-E4A1Snapshot {
            # Krok 1 (Base -> S1, do Target=Off): TaskbarDa 1 -> 0; krok 2 (S1 -> S2, do On): 0 -> 1.
            if ($Before.n -eq 1 -and $After.n -eq 2) { return @([pscustomobject]@{ Path = 'HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'TaskbarDa'; Before = $null; After = 'DWord:0' }) }
            if ($Before.n -eq 2 -and $After.n -eq 3) { return @([pscustomobject]@{ Path = 'HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name = 'TaskbarDa'; Before = 'DWord:0'; After = 'DWord:1' }) }
            @()
        }
        $Spec = @{ Id = 'widgets-taskbarda'; Group = 'ai'; Explorer = 'none'; Toggle = @{ Uri = 'ms-settings:taskbar'; Id = 'SystemSettings_DesktopTaskbar_Da_ToggleSwitch'; Target = 'Off' }; Observe = @(@{ Op = 'Taskbar' }) }
        $Result = Invoke-E4A1Candidate -Spec $Spec -UserSid $script:Sid
        @($script:Calls | Where-Object { $_ -like 'task *' }) | Should -Be @('task widgets-taskbarda-toggle1', 'task widgets-taskbarda-toggle2', 'task widgets-taskbarda-toggle3')
        Should -Invoke Invoke-E4A1UserTask -ParameterFilter { $Name -eq 'widgets-taskbarda-toggle1' -and $Ops[0].Want -eq 'Off' -and $Ops[0].Id -eq 'SystemSettings_DesktopTaskbar_Da_ToggleSwitch' }
        Should -Invoke Invoke-E4A1UserTask -ParameterFilter { $Name -eq 'widgets-taskbarda-toggle2' -and $Ops[0].Want -eq 'On' }
        Should -Invoke Invoke-E4A1UserTask -ParameterFilter { $Name -eq 'widgets-taskbarda-toggle3' -and $Ops[0].Want -eq 'On' }
        @($Result.Toggle.Mechanism).Count | Should -Be 1
        $Result.Toggle.Mechanism[0].Value | Should -Be 0
        $script:Calls | Should -Contain 'set HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced|TaskbarDa=1'
        $script:Calls | Should -Contain 'set HKU\S\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced|TaskbarDa=0'
        $script:Calls | Should -Contain 'observe mech-przeciwny'
        @($script:Calls | Where-Object { $_ -like 'restore *' }).Count | Should -Be 2
    }
}

Describe 'Skrypt dziecka K2 i wysyłka funkcji do VM' {
    It 'New-E4A1ChildScript: poprawna składnia, param(ResultFile, OpsFile), definicje funkcji dziecka, zapis atomowy' {
        $Text = New-E4A1ChildScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Text | Should -Match '^param\(\[Parameter\(Mandatory\)\]\[string\]\$ResultFile, \[Parameter\(Mandatory\)\]\[string\]\$OpsFile\)'
        foreach ($Name in Get-E4A1ChildFunctionNames) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Text | Should -Match 'Move-Item -LiteralPath \$Temporary -Destination \$ResultFile -Force'
    }
    It 'lista funkcji dziecka jest domknięta i wysyłana do VM; dziecko nie zawiera funkcji K1 (rejestr, Eksplorator, zadania)' {
        $Child = @(Get-E4A1ChildFunctionNames)
        foreach ($Name in $Child) { (Get-E4A1RemoteFunctionNames) | Should -Contain $Name }
        foreach ($Name in $Child) {
            $Ast = [System.Management.Automation.Language.Parser]::ParseInput((Get-Command $Name).Definition, [ref]$null, [ref]$null)
            $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4A1*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
            foreach ($Callee in $Called) { $Child | Should -Contain $Callee -Because "$Name woła $Callee" }
        }
        foreach ($NotInChild in 'Set-E4A1Value', 'Restore-E4A1Value', 'Restart-E4A1Explorer', 'Invoke-E4A1UserTask', 'Invoke-E4A1Candidate', 'Get-E4A1Snapshot') { $Child | Should -Not -Contain $NotInChild }
    }
    It 'Get-E4A1RemoteScript: poprawny, definiuje wszystkie funkcje zdalne; nie woła niczego spoza listy; funkcje hosta nie są wysyłane' {
        $Text = Get-E4A1RemoteScript
        $Errors = $null
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Sent = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4A1RemoteFunctionNames)
        foreach ($Name in $Sent) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4A1*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        # ConvertTo-E4A1Hashtable to funkcja zagnieżdżona w Get-E4A1PlanFromJson (wysyłana razem z nią).
        foreach ($Name in ($Called | Where-Object { $_ -ne 'ConvertTo-E4A1Hashtable' })) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-E4A1HostOnlyFunctionNames)) { $Text | Should -Not -Match "function $([regex]::Escape($HostOnly)) \{" }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Start-VM|Connect-VMNetworkAdapter'
    }
    It 'każda funkcja skryptu jest wysyłana albo na liście hosta; polecenia ze skutkiem tylko w swoich funkcjach' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4A1ScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name } | Where-Object { $_ -ne 'ConvertTo-E4A1Hashtable' })
        foreach ($Name in $Defined) { ((Get-E4A1RemoteFunctionNames) + (Get-E4A1HostOnlyFunctionNames)) | Should -Contain $Name }
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Allowed = @{
            'Stop-Process'               = @('Restart-E4A1Explorer')
            'Set-Service'                = @('Invoke-E4A1Candidate', 'Invoke-E4A1RebootPhase')
            'Stop-Service'               = @('Invoke-E4A1Candidate', 'Invoke-E4A1RebootPhase')
            'Start-Service'              = @('Invoke-E4A1Candidate', 'Invoke-E4A1RebootPhase')
            'Restart-Computer'           = @('Restart-E4A1Vm')
            'Register-ScheduledTask'     = @('Invoke-E4A1UserTask')
            'Unregister-ScheduledTask'   = @('Invoke-E4A1UserTask')
            'Start-Process'              = @('Open-E4A1K2Settings')
            'Add-Type'                   = @('Initialize-E4A1K2Native')
            'Connect-VMNetworkAdapter'   = @('Invoke-E4A1VmGroup')
            'Disconnect-VMNetworkAdapter' = @('Complete-E4A1VmCleanup')
            'Start-VM'                   = @('Invoke-E4A1VmGroup')
            'Restore-VmCleanState'       = @('Invoke-E4A1VmGroup', 'Complete-E4A1VmCleanup')
        }
        foreach ($Name in $Allowed.Keys) {
            foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq $Name })) { $Allowed[$Name] | Should -Contain (Get-CommandOwner $Command) -Because $Name }
        }
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Connect-VMNetworkAdapter' })) { $Command.Extent.Text | Should -Match "-SwitchName 'Default Switch'" }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg', 'reg.exe', 'Remove-AppxPackage', 'Disable-WindowsOptionalFeature', 'Stop-Computer', 'Invoke-Expression', 'iex',
            'Invoke-RestMethod', 'Invoke-WebRequest', 'Set-NetConnectionProfile', 'Stop-VM', 'Remove-VM', 'Restore-VMCheckpoint', 'Set-VMNetworkAdapter', 'cmd', 'cmd.exe', 'schtasks', 'schtasks.exe') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4A1ScriptPath), [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|EncodedCommand|Bruteforce_LTSC\s*[''"]'
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania; VmName tylko Home/Pro' {
        $Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $script:E4A1ScriptPath))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4A1ScriptPath), [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
        $Attr = (Get-Command $script:E4A1ScriptPath).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        $Attr2 = (Get-Command Invoke-E4A1VmGroup).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr2.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
    }
}

Describe 'Wybór pozycji i argumenty przebiegu' {
    It 'Select-E4A1Specs: grupa, filtr -Candidate; reboot = pozycje z -RebootCandidate (domyślnie diagtrack, recall-policy, hide-recommended); nieznane Id rzuca' {
        @(Select-E4A1Specs -Group ai | ForEach-Object { $_.Id }) | Should -Contain 'widgets-policy'
        @(Select-E4A1Specs -Group ads -Candidate softlanding | ForEach-Object { $_.Id }) | Should -Be @('softlanding')
        @(Select-E4A1Specs -Group reboot | ForEach-Object { $_.Id }) | Should -Be @('recall-policy', 'hide-recommended', 'diagtrack')
        @(Select-E4A1Specs -Group reboot -RebootCandidate 'diagtrack' | ForEach-Object { $_.Id }) | Should -Be @('diagtrack')
        { Select-E4A1Specs -Group ai -Candidate 'nie-ma' } | Should -Throw '*Nieznana pozycja*'
    }
    It 'Get-E4A1RunArguments: bez -Group wszystkie grupy bez reboot; -Candidate spoza wybranych grup rzuca' {
        $Arguments = Get-E4A1RunArguments -VmName 'DSO-26H2-Pro' -OutDirectory 'C:\w' -Group $null -Candidate $null -RebootCandidate $null -CheckpointName 'c' -ReadyTimeoutSeconds 60
        @($Arguments.Groups) | Should -Be @('ai', 'search', 'ads', 'telemetry', 'ux')
        $Arguments.Network | Should -BeTrue
        { Get-E4A1RunArguments -VmName 'DSO-26H2-Pro' -OutDirectory 'C:\w' -Group @('ai') -Candidate @('softlanding') -RebootCandidate $null -CheckpointName 'c' -ReadyTimeoutSeconds 60 } | Should -Throw '*nie należy*'
        (Get-E4A1RunArguments -VmName 'DSO-26H2-Pro' -OutDirectory 'C:\w' -Group @('reboot', 'ai') -Candidate $null -RebootCandidate @('diagtrack') -CheckpointName 'c' -ReadyTimeoutSeconds 60 -Network $false).Groups | Should -Be @('ai', 'reboot')
    }
    It 'Assert-E4A1OutDirectory: wymagany, bezwzględny, poza repo; w repo tylko ignorowane przez git' {
        $Repo = 'C:\Repo\DSOptimizer'
        { Assert-E4A1OutDirectory -Path '' -RepoRoot $Repo } | Should -Throw '*wymagany*'
        { Assert-E4A1OutDirectory -Path 'wyniki' -RepoRoot $Repo } | Should -Throw '*bezwzględn*'
        { Assert-E4A1OutDirectory -Path 'C:\Repo\DSOptimizer\tests\vm\out\x' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-E4A1OutDirectory -Path 'C:\Repo\DSOptimizer\wyniki\pomiar\exploration' -RepoRoot $Repo } | Should -Throw '*w repo*'
        $Real = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).ProviderPath   # prawdziwe repo: git check-ignore
        Assert-E4A1OutDirectory -Path "$Real\tests\vm\out\x" -RepoRoot $Real | Should -Be "$Real\tests\vm\out\x"   # ignorowany przez git
        { Assert-E4A1OutDirectory -Path "$Real\src\x" -RepoRoot $Real } | Should -Throw '*ignorowanym przez git*'   # nieignorowany
        Assert-E4A1OutDirectory -Path (Join-Path $env:TEMP 'dso-explore') -RepoRoot $Real | Should -Be (Join-Path $env:TEMP 'dso-explore')   # poza repo
        Assert-E4A1OutDirectory -Path 'D:\wyniki e4a1\' -RepoRoot $Repo | Should -Be 'D:\wyniki e4a1'
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
        Mock Get-E4A1RemoteScript { 'function X {}' }
        Mock Invoke-E4A1VmPhase { $script:Calls.Add("phase $Phase"); [pscustomobject]@{ phase = $Phase } }
        Mock Restart-E4A1Vm { $script:Calls.Add('reboot'); $null }
        Mock Copy-Item -RemoveParameterValidation 'FromSession' { }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Save-ExploreResult { $script:Calls.Add("save $FileName"); $script:SavedResult = $Result; "C:\w\$VmName\$FileName" }
        # Stan końcowy po revercie i odłączeniu (kontrakt: VM Off, karta bez przełącznika).
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Off' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = $null } }
        $script:SavedResult = $null
    }
    It 'grupa zwykła: revert -> sieć -> start -> faza main -> revert -> odłączenie -> sprawdzenie stanu końcowego -> zapis' {
        $Path = Invoke-E4A1VmGroup -VmName 'DSO-26H2-Pro' -Group 'ai' -Specs @(@{ Id = 'widgets-policy' }) -OutDirectory $TestDrive
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e4a1-ai.json'
        @($script:Calls) | Should -Be @('revert DSO-26H2-Pro', 'connect DSO-26H2-Pro Default Switch', 'start DSO-26H2-Pro', 'ready', 'phase main', 'revert DSO-26H2-Pro', 'disconnect DSO-26H2-Pro', 'save e4a1-ai.json')
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        $script:SavedResult.cleanup.FinalState | Should -Be 'Off'
        [string]$script:SavedResult.cleanup.FinalSwitch | Should -BeNullOrEmpty
        $script:SavedResult.error | Should -BeNullOrEmpty
    }
    It 'nieudane odłączenie sieci: zapisane w wyniku (cleanup.DisconnectError, error) i zgłoszone wyjątkiem, nie ukryte' {
        Mock Disconnect-VMNetworkAdapter { $script:Calls.Add("disconnect $VMName"); throw 'karta zajęta' }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Default Switch' } }
        { Invoke-E4A1VmGroup -VmName 'DSO-26H2-Pro' -Group 'ai' -Specs @(@{ Id = 'widgets-policy' }) -OutDirectory $TestDrive 3>$null } | Should -Throw '*odłączenie sieci*karta zajęta*'
        $script:SavedResult.cleanup.Ok | Should -BeFalse
        $script:SavedResult.cleanup.DisconnectError | Should -Match 'karta zajęta'
        $script:SavedResult.cleanup.FinalSwitch | Should -Be 'Default Switch'
        $script:SavedResult.error | Should -Match 'odłączenie sieci'
        $script:SavedResult.error | Should -Match 'stan końcowy'
    }
    It 'stan końcowy niezgodny z kontraktem (VM nie Off, karta dalej na przełączniku): zapisany i zgłoszony' {
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Running' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Default Switch' } }
        { Invoke-E4A1VmGroup -VmName 'DSO-26H2-Home' -Group 'ux' -Specs @(@{ Id = 'game-dvr' }) -OutDirectory $TestDrive 3>$null } | Should -Throw '*stan końcowy*'
        $script:SavedResult.cleanup.Ok | Should -BeFalse
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Running'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Default Switch'
    }
    It 'nieudany revert: zapisany w wyniku i zgłoszony; odłączenie sieci i tak próbowane' {
        $script:RevertCount = 0
        Mock Restore-VmCleanState { $script:Calls.Add("revert $VmName"); $script:RevertCount++; if ($script:RevertCount -gt 1) { throw 'checkpoint zablokowany' } }
        { Invoke-E4A1VmGroup -VmName 'DSO-26H2-Pro' -Group 'ai' -Specs @(@{ Id = 'widgets-policy' }) -OutDirectory $TestDrive 3>$null } | Should -Throw '*revert*checkpoint zablokowany*'
        $script:Calls | Should -Contain 'disconnect DSO-26H2-Pro'
        $script:SavedResult.cleanup.RevertError | Should -Match 'checkpoint zablokowany'
    }
    It 'grupa reboot: apply -> restart -> after -> restart -> undone' {
        $null = Invoke-E4A1VmGroup -VmName 'DSO-26H2-Home' -Group 'reboot' -Specs @(@{ Id = 'diagtrack' }) -OutDirectory $TestDrive
        @($script:Calls | Where-Object { $_ -like 'phase *' -or $_ -eq 'reboot' }) | Should -Be @('phase apply', 'reboot', 'phase after', 'reboot', 'phase undone')
        $script:Calls[-2] | Should -Be 'disconnect DSO-26H2-Home'
        $script:Calls[-1] | Should -Be 'save e4a1-reboot.json'
    }
    It '-Network $false: bez podłączania i odłączania karty; przełącznik karty nie jest częścią kontraktu' {
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Inny' } }
        $null = Invoke-E4A1VmGroup -VmName 'DSO-26H2-Pro' -Group 'ux' -Specs @(@{ Id = 'game-dvr' }) -OutDirectory $TestDrive -Network $false
        @($script:Calls | Where-Object { $_ -like 'connect*' -or $_ -like 'disconnect*' }).Count | Should -Be 0
        $script:SavedResult.cleanup.Ok | Should -BeTrue
    }
    It 'błąd fazy: wynik częściowy zapisany, revert i odłączenie wykonane, wyjątek z nazwą grupy' {
        Mock Invoke-E4A1VmPhase { throw 'faza padła' }
        { Invoke-E4A1VmGroup -VmName 'DSO-26H2-Pro' -Group 'ads' -Specs @(@{ Id = 'softlanding' }) -OutDirectory $TestDrive } | Should -Throw "*grupa 'ads'*faza padła*"
        $script:Calls[-3] | Should -Be 'revert DSO-26H2-Pro'
        $script:Calls[-2] | Should -Be 'disconnect DSO-26H2-Pro'
        $script:Calls[-1] | Should -Be 'save e4a1-ads.json'
        $script:SavedResult.error | Should -Match 'faza padła'
    }
    It 'błąd uwierzytelnienia: przekazany dalej (stop całości), revert i odłączenie wykonane' {
        Mock Wait-VmReady { $E = [System.InvalidOperationException]::new('błąd uwierzytelnienia PowerShell Direct'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4A1VmGroup -VmName 'DSO-26H2-Pro' -Group 'ai' -Specs @(@{ Id = 'copilot-policy' }) -OutDirectory $TestDrive } | Should -Throw '*uwierzytelnienia*'
        $script:Calls | Should -Contain 'revert DSO-26H2-Pro'
        $script:Calls | Should -Contain 'disconnect DSO-26H2-Pro'
        $script:Calls[-1] | Should -Be 'save e4a1-ai.json'
    }
    It 'Invoke-E4A1Exploration: błąd uwierzytelnienia zatrzymuje całość; inny błąd zapisany i kolejne grupy idą dalej' {
        Mock Invoke-E4A1VmGroup { if ($Group -eq 'ai') { throw 'zwykły błąd' }; "C:\w\$VmName\e4a1-$Group.json" }
        $Arguments = Get-E4A1RunArguments -VmName 'DSO-26H2-Pro' -OutDirectory 'C:\w' -Group @('ai', 'ux') -Candidate $null -RebootCandidate $null -CheckpointName 'c' -ReadyTimeoutSeconds 60
        $Outcome = Invoke-E4A1Exploration -Arguments $Arguments -Candidate $null -RebootCandidate $null 6>$null
        @($Outcome.Errors).Count | Should -Be 1
        @($Outcome.Paths) | Should -Be @('C:\w\DSO-26H2-Pro\e4a1-ux.json')
        Mock Invoke-E4A1VmGroup { $E = [System.InvalidOperationException]::new('x'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4A1Exploration -Arguments $Arguments -Candidate $null -RebootCandidate $null 6>$null } | Should -Throw
        Should -Invoke Invoke-E4A1VmGroup -Times 2 -Exactly -ParameterFilter { $Group -eq 'ai' } -Scope It
        Should -Invoke Invoke-E4A1VmGroup -Times 1 -Exactly -ParameterFilter { $Group -eq 'ux' } -Scope It
    }
}

Describe 'Blok główny SKRYPTU (-ShowPlan; bez VM)' {
    It '-ShowPlan: kod 0, plan grup i pozycji; bez -OutDirectory kod 1' {
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Be '1'
        $Output = & $script:E4A1ScriptPath -ShowPlan -VmName DSO-26H2-Pro -Group ux, reboot -RebootCandidate diagtrack -OutDirectory (Join-Path $TestDrive 'w') 6>$null
        $LASTEXITCODE | Should -Be 0
        $Plan = ($Output | Out-String) | ConvertFrom-Json
        @($Plan.Groups) | Should -Be @('ux', 'reboot')
        @($Plan.Plan.reboot) | Should -Be @('diagtrack')
        @($Plan.Plan.ux) | Should -Contain 'classic-context-menu'
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        & $script:E4A1ScriptPath -ShowPlan 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
}

AfterAll { Clear-NoLiveVm }
