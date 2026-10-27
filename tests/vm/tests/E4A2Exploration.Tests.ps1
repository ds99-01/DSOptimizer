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
    . (Join-Path $PSScriptRoot '..\explore\Invoke-E4A2Exploration.ps1')
    $script:E4A2ScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-E4A2Exploration.ps1'
    $script:Sid = 'S-1-5-21-1-2-3-1000'

    function script:Get-CommandOwner {
        param($Node)
        $Parent = $Node.Parent
        while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
        if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
    }
}

Describe 'Plan pomiaru (dane z planu Tasku 2, dosłownie)' {
    It 'cele usuwania: Weather (WinRT procesu, -User (SID), odpytywanie 60 s, zapasowo -AllUsers), News i Solitaire z konta dso, To Do do próby wylogowania' {
        $Plan = Get-E4A2Plan
        @($Plan.Targets | ForEach-Object { $_.Name }) | Should -Be @('Microsoft.BingWeather', 'Microsoft.BingNews', 'Microsoft.MicrosoftSolitaireCollection', 'Microsoft.Todos')
        @($Plan.Targets[0].Methods) | Should -Be @('WinRtProcess', 'UserSid')
        $Plan.Targets[0].PollSeconds | Should -Be 60
        $Plan.Targets[0].Fallback | Should -Be 'AllUsers'
        @($Plan.Targets[1].Methods) | Should -Be @('OwnUser')
        @($Plan.Targets[2].Methods) | Should -Be @('OwnUserSid')
        @($Plan.Targets[3].Methods) | Should -Be @('UserSid')
        @($Plan.Targets | ForEach-Object { [bool]$_.Deprovision }) | Should -Be @($true, $true, $true, $false)
        @($Plan.Targets | Where-Object { $_.Search } | ForEach-Object { $_.Name }) | Should -Be @('Microsoft.BingWeather', 'Microsoft.BingNews')
        $Plan.LogoffTarget | Should -Be 'Microsoft.Todos'
    }
    It 'gałęzie Uninstall: HKLM 64, WOW6432Node i HKU\{SID}' {
        @((Get-E4A2Plan).UninstallRoots) | Should -Be @(
            'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
            'HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
            'HKU\{SID}\Software\Microsoft\Windows\CurrentVersion\Uninstall'
        )
    }
    It 'csc.exe z Framework64\v4.0.30319 i COM WindowsInstaller.Installer' {
        $Plan = Get-E4A2Plan
        $Plan.CscRelativePath | Should -Be 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        $Plan.InstallerProgId | Should -Be 'WindowsInstaller.Installer'
    }
}

Describe 'Czysta logika (bez VM)' {
    It 'ConvertTo-E4A2ResourceReference: ms-resource w trzech postaciach -> @{pełna nazwa?ms-resource://...}; zwykły tekst bez zamiany' {
        $Full = 'Microsoft.BingWeather_4.54.1.0_x64__8wekyb3d8bbwe'
        $Name = 'Microsoft.BingWeather'
        @(ConvertTo-E4A2ResourceReference -Raw 'ms-resource:AppName' -PackageFullName $Full -PackageName $Name) | Should -Be @(
            "@{${Full}?ms-resource://$Name/Resources/AppName}"
            "@{${Full}?ms-resource://$Name/AppName}"
        )
        @(ConvertTo-E4A2ResourceReference -Raw 'ms-resource://Microsoft.BingWeather/Resources/Title' -PackageFullName $Full -PackageName $Name) | Should -Be @("@{${Full}?ms-resource://Microsoft.BingWeather/Resources/Title}")
        @(ConvertTo-E4A2ResourceReference -Raw 'ms-resource:/Resources/Title' -PackageFullName $Full -PackageName $Name) | Should -Be @("@{${Full}?ms-resource://$Name/Resources/Title}")
        @(ConvertTo-E4A2ResourceReference -Raw 'Kalkulator' -PackageFullName $Full -PackageName $Name).Count | Should -Be 0
        @(ConvertTo-E4A2ResourceReference -Raw '' -PackageFullName $Full -PackageName $Name).Count | Should -Be 0
    }
    It 'ConvertTo-E4A2SearchPhrase: początek nazwy z liter ASCII, cyfr i spacji (SendKeys); za krótki = fraza zapasowa' {
        ConvertTo-E4A2SearchPhrase -Name 'MSN Pogoda' -Fallback 'Pogoda' | Should -Be 'MSN Pogoda'
        ConvertTo-E4A2SearchPhrase -Name 'Wiadomości' -Fallback 'News' | Should -Be 'Wiadomo'
        ConvertTo-E4A2SearchPhrase -Name 'Ał (x)' -Fallback 'News' | Should -Be 'News'
        ConvertTo-E4A2SearchPhrase -Name '' -Fallback 'News' | Should -Be 'News'
    }
    It 'Test-E4A2SppVolume: wpis SPP\Clients z GUID woluminu C: = ochrona włączona; inny wolumin albo brak = wyłączona' {
        $Volume = '\\?\Volume{11111111-2222-3333-4444-555555555555}\'
        Test-E4A2SppVolume -Entries @('\\?\Volume{11111111-2222-3333-4444-555555555555}\:(C%3A)') -VolumeId $Volume | Should -BeTrue
        Test-E4A2SppVolume -Entries @('\\?\Volume{99999999-2222-3333-4444-555555555555}\:(D%3A)') -VolumeId $Volume | Should -BeFalse
        Test-E4A2SppVolume -Entries @() -VolumeId $Volume | Should -BeFalse
        Test-E4A2SppVolume -Entries @('x') -VolumeId '' | Should -BeFalse
    }
    It 'Resolve-E4A2Path: {SID} -> SID; inne gałęzie odrzucone' {
        Resolve-E4A2Path -Path 'HKU\{SID}\Software\X' -UserSid $script:Sid | Should -Be "HKU\$($script:Sid)\Software\X"
        { Resolve-E4A2Path -Path 'HKCU\Software\X' -UserSid $script:Sid } | Should -Throw '*HKLM*'
    }
    It 'Select-E4A2Present: pierwsza wersja pakietu o danej nazwie albo $null' {
        $List = @([pscustomobject]@{ Name = 'Microsoft.BingNews'; PackageFullName = 'N_1' }, [pscustomobject]@{ Name = 'Microsoft.BingWeather'; PackageFullName = 'W_1' })
        (Select-E4A2Present -Packages $List -Name 'Microsoft.BingWeather').PackageFullName | Should -Be 'W_1'
        Select-E4A2Present -Packages $List -Name 'Microsoft.Nie' | Should -BeNullOrEmpty
        Select-E4A2Present -Packages @() -Name 'Microsoft.BingNews' | Should -BeNullOrEmpty
    }
}

Describe 'Bezpieczniki (VM-only, mock-only)' {
    It 'Test-E4A2VmComputerName: tylko DSO-26H2-HOME/PRO' {
        Test-E4A2VmComputerName -ComputerName 'dso-26h2-pro' | Should -BeTrue
        Test-E4A2VmComputerName -ComputerName 'DSO-26H2-HOME' | Should -BeTrue
        Test-E4A2VmComputerName -ComputerName 'BRUTEFORCE_LTSC' | Should -BeFalse
        Test-E4A2VmComputerName -ComputerName '' | Should -BeFalse
    }
    It 'Assert-E4A2InsideVm rzuca w trybie mock-only, nawet z nazwą VM; poza trybem rzuca na hoście' {
        { Assert-E4A2InsideVm -ComputerName 'DSO-26H2-PRO' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        $Saved = $env:DSO_VM_TESTS_MOCK_ONLY
        try {
            [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $null)
            { Assert-E4A2InsideVm -ComputerName 'HOST-PC' } | Should -Throw '*tylko w VM*'
            { Assert-E4A2InsideVm -ComputerName 'DSO-26H2-HOME' } | Should -Not -Throw
        }
        finally { $env:DSO_VM_TESTS_MOCK_ONLY = $Saved }
    }
    It '<Name> rzuca przed jakimkolwiek skutkiem (usuwanie AppX, ochrona systemu, kompilacja, zadania, klawisze i zrzuty K2)' -ForEach @(
        @{ Name = 'Invoke-E4A2Removal'; Code = { Invoke-E4A2Removal -UserSid $script:Sid -Name 'Microsoft.BingWeather' -Method UserSid } }
        @{ Name = 'Invoke-E4A2RemovalPhase'; Code = { Invoke-E4A2RemovalPhase -UserSid $script:Sid -Targets @(@{ Name = 'Microsoft.BingWeather'; Method = 'UserSid'; Deprovision = $true }) } }
        @{ Name = 'Set-E4A2SystemProtection'; Code = { Set-E4A2SystemProtection -Enable $true } }
        @{ Name = 'Get-E4A2ToolAvailability'; Code = { Get-E4A2ToolAvailability -CscRelativePath 'x\csc.exe' -InstallerProgId 'WindowsInstaller.Installer' } }
        @{ Name = 'Invoke-E4A2UserTask'; Code = { Invoke-E4A2UserTask -Name 'x' -Ops @() } }
        @{ Name = 'Invoke-E4A2LogoffProbe'; Code = { Invoke-E4A2LogoffProbe -UserSid $script:Sid -Name 'Microsoft.Todos' } }
        @{ Name = 'Invoke-E4A2Collection'; Code = { Invoke-E4A2Collection -UserSid $script:Sid -PlanJson '{}' -Phase inventory } }
        @{ Name = 'Invoke-E4A2WinRtRemove'; Code = { Invoke-E4A2WinRtRemove -PackageFullName 'Microsoft.BingWeather_1_x64__8wekyb3d8bbwe' } }
        @{ Name = 'Get-E4A2SystemProtection'; Code = { Get-E4A2SystemProtection } }
        @{ Name = 'Save-E4A2K2Shot'; Code = { Save-E4A2K2Shot -Label 'x' } }
        @{ Name = 'Get-E4A2K2Search'; Code = { Get-E4A2K2Search -Name 'Microsoft.BingWeather' -Fallback 'Pogoda' -Label 'x' } }
        @{ Name = 'Invoke-E4A2K2Remove'; Code = { Invoke-E4A2K2Remove -Name 'Microsoft.BingWeather' } }
        @{ Name = 'Invoke-E4A2K2Ops'; Code = { Invoke-E4A2K2Ops -Ops @(@{ Op = 'Remove'; Name = 'Microsoft.BingWeather' }) } }
    ) {
        Mock Remove-AppxPackage { throw 'nie wolno' }
        Mock Remove-AppxProvisionedPackage { throw 'nie wolno' }
        Mock Enable-ComputerRestore { throw 'nie wolno' }
        Mock Disable-ComputerRestore { throw 'nie wolno' }
        Mock Register-ScheduledTask { throw 'nie wolno' }
        Mock Invoke-ExploreNative { throw 'nie wolno' }
        Mock New-Item { throw 'nie wolno' }
        Mock Get-AppxPackage { throw 'nie wolno' }
        Mock Initialize-E4A2K2Native { throw 'nie wolno' }
        Mock Initialize-E4A2Native { throw 'nie wolno' }
        { & $Code } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke New-Item -Times 0 -Exactly
        Should -Invoke Get-AppxPackage -Times 0 -Exactly
        Should -Invoke Initialize-E4A2K2Native -Times 0 -Exactly
        Should -Invoke Initialize-E4A2Native -Times 0 -Exactly
        Should -Invoke Remove-AppxPackage -Times 0 -Exactly
        Should -Invoke Remove-AppxProvisionedPackage -Times 0 -Exactly
        Should -Invoke Enable-ComputerRestore -Times 0 -Exactly
        Should -Invoke Disable-ComputerRestore -Times 0 -Exactly
        Should -Invoke Register-ScheduledTask -Times 0 -Exactly
        Should -Invoke Invoke-ExploreNative -Times 0 -Exactly
    }
    It 'każda funkcja wysyłana do VM ze skutkiem (wprost albo przez inną taką funkcję) zaczyna się od Assert-E4A2InsideVm' {
        # Skutek: polecenia zmieniające system lub pliki, uruchamianie programów, klawisze, WinRT/COM ze zmianą, wylogowanie.
        $EffectCommands = @('Remove-AppxPackage', 'Remove-AppxProvisionedPackage', 'Enable-ComputerRestore', 'Disable-ComputerRestore', 'Register-ScheduledTask',
            'Start-ScheduledTask', 'Stop-ScheduledTask', 'Unregister-ScheduledTask', 'Set-Content', 'New-Item', 'Remove-Item', 'Move-Item', 'Copy-Item', 'Invoke-ExploreNative')
        $EffectMembers = @('Press', 'SendWait', 'RemovePackageAsync', 'WTSLogoffSession', 'OpenDatabase', 'Commit', 'CopyFromScreen')
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4A2ScriptPath), [ref]$null, [ref]$null)
        $Remote = @(Get-E4A2RemoteFunctionNames)
        $Functions = @{}
        foreach ($Fn in $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) { if ($Remote -contains $Fn.Name) { $Functions[$Fn.Name] = $Fn } }
        $Calls = @{}
        $Effect = @{}
        foreach ($Name in $Functions.Keys) {
            $Body = $Functions[$Name].Body
            $Commands = @($Body.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
            $Members = @($Body.FindAll({ param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true) | ForEach-Object { $_.Member.Extent.Text })
            $Calls[$Name] = @($Commands | Where-Object { $Functions.ContainsKey($_) -and $_ -ne $Name })
            $Effect[$Name] = (@($Commands | Where-Object { $EffectCommands -contains $_ }).Count + @($Members | Where-Object { $EffectMembers -contains $_ }).Count) -gt 0
        }
        do {
            $Changed = $false
            foreach ($Name in $Functions.Keys) {
                if (-not $Effect[$Name] -and @($Calls[$Name] | Where-Object { $Effect[$_] -and $_ -ne 'Assert-E4A2InsideVm' }).Count -gt 0) { $Effect[$Name] = $true; $Changed = $true }
            }
        } while ($Changed)
        $Guarded = @($Functions.Keys | Where-Object { $Effect[$_] } | Sort-Object)
        foreach ($Name in 'Invoke-E4A2WinRtRemove', 'Save-E4A2K2Shot', 'Get-E4A2K2Search', 'Invoke-E4A2K2Remove', 'Invoke-E4A2K2Ops', 'Invoke-E4A2Removal', 'Invoke-E4A2UserTask') { $Guarded | Should -Contain $Name }
        foreach ($Name in $Guarded) {
            $First = @($Functions[$Name].Body.EndBlock.Statements)[0]
            $First.Extent.Text | Should -Match '^Assert-E4A2InsideVm\b' -Because "$Name ma skutek, więc pierwsza instrukcja to bezpiecznik"
        }
    }
}

Describe 'Usuwanie w VM (mocki: AppX nietknięte)' {
    BeforeEach {
        Mock Assert-E4A2InsideVm { }
        $script:Removed = [System.Collections.Generic.List[string]]::new()
        $script:Weather = [pscustomobject]@{ Name = 'Microsoft.BingWeather'; PackageFullName = 'Microsoft.BingWeather_4.1_x64__8wekyb3d8bbwe' }
        Mock Remove-AppxPackage { $script:Removed.Add("remove $Package user=$User all=$([bool]$AllUsers)") }
        Mock Remove-AppxProvisionedPackage { $script:Removed.Add("deprovision $PackageName online=$([bool]$Online)") }
        Mock Get-E4A2PackageState { [pscustomobject]@{ Name = $Name; ForUser = $null; Provisioned = $null; Users = @(); Error = $null } }
        Mock Get-AppxPackage { $script:Weather }
        Mock Get-AppxProvisionedPackage { [pscustomobject]@{ DisplayName = 'Microsoft.BingWeather'; PackageName = 'Microsoft.BingWeather_4.1_neutral_~_8wekyb3d8bbwe' } }
    }
    It 'UserSid: Remove-AppxPackage -Package (pełna nazwa) -User (SID), czas i stan po' {
        $Result = Invoke-E4A2Removal -UserSid $script:Sid -Name 'Microsoft.BingWeather' -Method UserSid
        @($script:Removed) | Should -Be @("remove Microsoft.BingWeather_4.1_x64__8wekyb3d8bbwe user=$($script:Sid) all=False")
        $Result.Method | Should -Be 'UserSid'
        $Result.PackageFullName | Should -Be 'Microsoft.BingWeather_4.1_x64__8wekyb3d8bbwe'
        $Result.Command | Should -Match '-User S-1-5-21-1-2-3-1000'
        $Result.DurationMs | Should -BeGreaterOrEqual 0
        $Result.Error | Should -BeNullOrEmpty
        $Result.After.Name | Should -Be 'Microsoft.BingWeather'
    }
    It 'AllUsers: Remove-AppxPackage -Package (pełna nazwa) -AllUsers' {
        $null = Invoke-E4A2Removal -UserSid $script:Sid -Name 'Microsoft.BingWeather' -Method AllUsers
        @($script:Removed) | Should -Be @('remove Microsoft.BingWeather_4.1_x64__8wekyb3d8bbwe user= all=True')
    }
    It 'Deprovision: Remove-AppxProvisionedPackage -Online -PackageName (pełna nazwa z obrazu)' {
        $null = Invoke-E4A2Removal -UserSid $script:Sid -Name 'Microsoft.BingWeather' -Method Deprovision
        @($script:Removed) | Should -Be @('deprovision Microsoft.BingWeather_4.1_neutral_~_8wekyb3d8bbwe online=True')
    }
    It 'WinRtProcess: PackageManager.RemovePackageAsync(pełna nazwa pakietu dso) w procesie Administratora; wynik operacji zapisany' {
        Mock Invoke-E4A2WinRtRemove { "ExtendedErrorCode=0x80073CF1 dla $PackageFullName" }
        $Result = Invoke-E4A2Removal -UserSid $script:Sid -Name 'Microsoft.BingWeather' -Method WinRtProcess
        $Result.Outcome | Should -Be 'ExtendedErrorCode=0x80073CF1 dla Microsoft.BingWeather_4.1_x64__8wekyb3d8bbwe'
        $Result.Command | Should -Match 'RemovePackageAsync'
        $script:Removed.Count | Should -Be 0
    }
    It 'WinRtProcess: błąd opakowany w AggregateException zapisany jako błąd bazowy (HRESULT i komunikat)' {
        Mock Invoke-E4A2WinRtRemove {
            $Inner = [System.Runtime.InteropServices.COMException]::new('Nie znaleziono pakietu.', -2147009295)
            throw [System.AggregateException]::new($Inner)
        }
        (Invoke-E4A2Removal -UserSid $script:Sid -Name 'Microsoft.BingWeather' -Method WinRtProcess).Error | Should -Be '0x80073CF1 Nie znaleziono pakietu.'
    }
    It 'brak pakietu: nic nie jest usuwane, wynik NotPresent' {
        Mock Get-AppxPackage { }
        Mock Get-AppxProvisionedPackage { }
        (Invoke-E4A2Removal -UserSid $script:Sid -Name 'Microsoft.BingWeather' -Method UserSid).NotPresent | Should -BeTrue
        (Invoke-E4A2Removal -UserSid $script:Sid -Name 'Microsoft.BingWeather' -Method Deprovision).NotPresent | Should -BeTrue
        $script:Removed.Count | Should -Be 0
    }
    It 'błąd cmdletu jest daną (Error), nie przerywa' {
        Mock Remove-AppxPackage { throw 'HRESULT 0x80073CFA' }
        (Invoke-E4A2Removal -UserSid $script:Sid -Name 'Microsoft.BingWeather' -Method UserSid).Error | Should -Match '0x80073CFA'
    }
    It 'faza usuwania: metody po kolei, odpytywanie i Start w trakcie, gdy dalej jest dla dso -> Fallback, potem deprovisioning; metody dso przez zadanie' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E4A2Removal { $script:Order.Add("$Name $Method"); [pscustomobject]@{ Name = $Name; Method = $Method; After = [pscustomobject]@{ ForUser = 'jest' } } }
        Mock Invoke-E4A2UserTask { $script:Order.Add("task $Name $($Ops[0].Op) $($Ops[0].Name) $($Ops[0].WithSid)"); [pscustomobject]@{ Available = $true; Results = @() } }
        Mock Get-E4A2PackageState { $script:Order.Add("state $Name"); [pscustomobject]@{ Name = $Name; ForUser = 'jest'; Users = @() } }
        Mock Start-Sleep { }
        $Plan = Get-E4A2Plan
        $Out = Invoke-E4A2RemovalPhase -UserSid $script:Sid -Targets $Plan.Targets -PollIntervalSeconds 30
        @($script:Order | Where-Object { $_ -notlike 'state *' }) | Should -Be @(
            'Microsoft.BingWeather WinRtProcess', 'Microsoft.BingWeather UserSid', 'task start-microsoft-bingweather Appx  ', 'Microsoft.BingWeather AllUsers', 'Microsoft.BingWeather Deprovision',
            'task remove-microsoft-bingnews Remove Microsoft.BingNews False', 'Microsoft.BingNews Deprovision',
            'task remove-microsoft-microsoftsolitairecollection Remove Microsoft.MicrosoftSolitaireCollection True', 'Microsoft.MicrosoftSolitaireCollection Deprovision',
            'Microsoft.Todos UserSid'
        )
        @($Out).Count | Should -Be 4
        @($Out[0].Poll).Count | Should -Be 2
    }
    It 'faza usuwania: po skutecznym -User nie ma odpytywania ani Fallback' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E4A2Removal { $script:Order.Add("$Name $Method"); [pscustomobject]@{ Name = $Name; Method = $Method; After = [pscustomobject]@{ ForUser = $null } } }
        Mock Get-E4A2PackageState { [pscustomobject]@{ Name = $Name; ForUser = $null } }
        Mock Invoke-E4A2UserTask { throw 'nie powinno' }
        $null = Invoke-E4A2RemovalPhase -UserSid $script:Sid -Targets @(@{ Name = 'Microsoft.BingWeather'; Methods = @('UserSid'); PollSeconds = 60; Fallback = 'AllUsers'; Deprovision = $true; Search = $true })
        @($script:Order) | Should -Be @('Microsoft.BingWeather UserSid', 'Microsoft.BingWeather Deprovision')
    }
}

Describe 'Skrypt dziecka K2 i wysyłka funkcji do VM' {
    It 'New-E4A2ChildScript: poprawna składnia, param(ResultFile, OpsFile), definicje funkcji dziecka, zapis atomowy' {
        $Text = New-E4A2ChildScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Text | Should -Match '^param\(\[Parameter\(Mandatory\)\]\[string\]\$ResultFile, \[Parameter\(Mandatory\)\]\[string\]\$OpsFile\)'
        foreach ($Name in Get-E4A2ChildFunctionNames) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Text | Should -Match 'Move-Item -LiteralPath \$Temporary -Destination \$ResultFile -Force'
    }
    It 'lista funkcji dziecka jest domknięta i wysyłana do VM; dziecko nie zawiera funkcji K1 (usuwanie -User, ochrona, zadania)' {
        $Child = @(Get-E4A2ChildFunctionNames)
        foreach ($Name in $Child) { (Get-E4A2RemoteFunctionNames) | Should -Contain $Name }
        foreach ($Name in $Child) {
            $Ast = [System.Management.Automation.Language.Parser]::ParseInput((Get-Command $Name).Definition, [ref]$null, [ref]$null)
            $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4A2*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
            foreach ($Callee in $Called) { $Child | Should -Contain $Callee -Because "$Name woła $Callee" }
        }
        foreach ($NotInChild in 'Invoke-E4A2Removal', 'Set-E4A2SystemProtection', 'Invoke-E4A2UserTask', 'Get-E4A2ToolAvailability') { $Child | Should -Not -Contain $NotInChild }
    }
    It 'skrypt dziecka K2 ma bezpiecznik: definicje Assert-E4A2InsideVm i Test-E4A2VmComputerName oraz wywołanie przed operacjami' {
        @(Get-E4A2ChildFunctionNames) | Should -Contain 'Assert-E4A2InsideVm'
        @(Get-E4A2ChildFunctionNames) | Should -Contain 'Test-E4A2VmComputerName'
        $Text = New-E4A2ChildScript
        $Main = $Text.Substring($Text.IndexOf('$Document = [ordered]@{'))
        $Main | Should -Match '(?s)try \{\s*Assert-E4A2InsideVm.*Invoke-E4A2K2Ops -Ops \$Ops'
    }
    It 'Get-E4A2RemoteScript: poprawny, definiuje wszystkie funkcje zdalne; nie woła niczego spoza listy; funkcje hosta nie są wysyłane' {
        $Text = Get-E4A2RemoteScript
        $Errors = $null
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Sent = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4A2RemoteFunctionNames)
        foreach ($Name in $Sent) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4A2*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-E4A2HostOnlyFunctionNames)) { $Text | Should -Not -Match "function $([regex]::Escape($HostOnly)) \{" }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Start-VM|Connect-VMNetworkAdapter'
    }
    It 'każda funkcja skryptu jest wysyłana albo na liście hosta; polecenia ze skutkiem tylko w swoich funkcjach' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4A2ScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
        foreach ($Name in $Defined) { ((Get-E4A2RemoteFunctionNames) + (Get-E4A2HostOnlyFunctionNames)) | Should -Contain $Name }
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Allowed = @{
            'Remove-AppxPackage'            = @('Invoke-E4A2Removal', 'Invoke-E4A2K2Remove')
            'Remove-AppxProvisionedPackage' = @('Invoke-E4A2Removal')
            'Enable-ComputerRestore'        = @('Set-E4A2SystemProtection')
            'Disable-ComputerRestore'       = @('Set-E4A2SystemProtection')
            'Register-ScheduledTask'        = @('Invoke-E4A2UserTask')
            'Unregister-ScheduledTask'      = @('Invoke-E4A2UserTask')
            'Add-Type'                      = @('Initialize-E4A2Native', 'Initialize-E4A2K2Native')
            'Connect-VMNetworkAdapter'      = @('Invoke-E4A2VmRun')
            'Disconnect-VMNetworkAdapter'   = @('Complete-E4A2VmCleanup')
            'Start-VM'                      = @('Invoke-E4A2VmRun')
            'Restore-VmCleanState'          = @('Invoke-E4A2VmRun', 'Complete-E4A2VmCleanup')
            'Save-DsoVmThumbnail'           = @('Invoke-E4A2VmRun')
        }
        foreach ($Name in $Allowed.Keys) {
            foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq $Name })) { $Allowed[$Name] | Should -Contain (Get-CommandOwner $Command) -Because $Name }
        }
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Connect-VMNetworkAdapter' })) { $Command.Extent.Text | Should -Match "-SwitchName 'Default Switch'" }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg', 'reg.exe', 'msiexec', 'msiexec.exe', 'Stop-Computer', 'Restart-Computer', 'Invoke-Expression', 'iex',
            'Invoke-RestMethod', 'Invoke-WebRequest', 'Stop-VM', 'Remove-VM', 'Restore-VMCheckpoint', 'Set-VMNetworkAdapter', 'cmd', 'cmd.exe', 'schtasks', 'schtasks.exe', 'Checkpoint-Computer') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4A2ScriptPath), [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|EncodedCommand|Bruteforce_LTSC\s*[''"]'
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania; VmName tylko Home/Pro' {
        $Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $script:E4A2ScriptPath))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4A2ScriptPath), [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
        $Attr = (Get-Command $script:E4A2ScriptPath).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        $Attr2 = (Get-Command Invoke-E4A2VmRun).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr2.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
    }
}

Describe 'Argumenty przebiegu' {
    It 'Assert-E4A2OutDirectory: wymagany, bezwzględny, poza repo; w repo tylko ignorowane przez git' {
        $Repo = 'C:\Repo\DSOptimizer'
        { Assert-E4A2OutDirectory -Path '' -RepoRoot $Repo } | Should -Throw '*wymagany*'
        { Assert-E4A2OutDirectory -Path 'wyniki' -RepoRoot $Repo } | Should -Throw '*bezwzględn*'
        { Assert-E4A2OutDirectory -Path 'C:\Repo\DSOptimizer\tests\vm\out\x' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-E4A2OutDirectory -Path 'C:\Repo\DSOptimizer\wyniki\pomiar\exploration' -RepoRoot $Repo } | Should -Throw '*w repo*'
        $Real = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).ProviderPath   # prawdziwe repo: git check-ignore
        Assert-E4A2OutDirectory -Path "$Real\tests\vm\out\x" -RepoRoot $Real | Should -Be "$Real\tests\vm\out\x"   # ignorowany przez git
        { Assert-E4A2OutDirectory -Path "$Real\src\x" -RepoRoot $Real } | Should -Throw '*ignorowanym przez git*'   # nieignorowany
        Assert-E4A2OutDirectory -Path (Join-Path $env:TEMP 'dso-explore') -RepoRoot $Real | Should -Be (Join-Path $env:TEMP 'dso-explore')   # poza repo
    }
    It 'Get-E4A2RunArguments: domyślnie bez sieci; -Network $true na życzenie' {
        $Arguments = Get-E4A2RunArguments -VmName 'DSO-26H2-Pro' -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        $Arguments.Network | Should -BeFalse
        @($Arguments.VmName) | Should -Be @('DSO-26H2-Pro')
        (Get-E4A2RunArguments -VmName 'DSO-26H2-Pro' -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60 -Network $true).Network | Should -BeTrue
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
        Mock Get-E4A2RemoteScript { 'function X {}' }
        Mock Invoke-E4A2VmPhase { $script:Calls.Add("phase $Phase $Label"); [pscustomobject]@{ phase = $Phase } }
        Mock Save-DsoVmThumbnail { $script:Calls.Add("thumb $(Split-Path -Leaf $Path)"); $Path }
        Mock Copy-Item -RemoveParameterValidation 'FromSession' { }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Save-ExploreResult { $script:Calls.Add("save $FileName"); $script:SavedResult = $Result; "C:\w\$VmName\$FileName" }
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Off' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = $null } }
        $script:SavedResult = $null
    }
    It 'przebieg: revert -> start -> spis -> Start przed (+zrzut hosta) -> usuwanie -> Start po (+zrzut) -> ochrona -> revert -> stan końcowy -> zapis' {
        $Path = Invoke-E4A2VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e4a2.json'
        @($script:Calls) | Should -Be @(
            'revert DSO-26H2-Pro', 'start DSO-26H2-Pro', 'ready', 'phase inventory ', 'phase search przed', 'thumb host-start-przed.png',
            'phase removal ', 'phase search po', 'thumb host-start-po.png', 'phase protection ', 'phase logoff ', 'revert DSO-26H2-Pro', 'save e4a2.json'
        )
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        $script:SavedResult.cleanup.FinalState | Should -Be 'Off'
        [string]$script:SavedResult.cleanup.FinalSwitch | Should -BeNullOrEmpty
        $script:SavedResult.error | Should -BeNullOrEmpty
        $script:SavedResult.userSid | Should -Be 'S-1-5-21-9-9-9-1000'
    }
    It '-Network $true: karta do Default Switch przed startem, odłączona po revercie' {
        $null = Invoke-E4A2VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Network $true
        $script:Calls[1] | Should -Be 'connect DSO-26H2-Home Default Switch'
        $script:Calls[-2] | Should -Be 'disconnect DSO-26H2-Home'
    }
    It 'stan końcowy niezgodny z kontraktem (VM nie Off, karta dalej na przełączniku): zapisany i zgłoszony, także bez -Network' {
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Running' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Default Switch' } }
        { Invoke-E4A2VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive 3>$null } | Should -Throw '*stan końcowy*'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Running'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Default Switch'
    }
    It 'błąd zrzutu hosta jest daną, nie przerywa przebiegu' {
        Mock Save-DsoVmThumbnail { throw 'WMI zajęte' }
        $null = Invoke-E4A2VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive
        @($script:Calls | Where-Object { $_ -like 'phase *' }).Count | Should -Be 6
        ($script:SavedResult.thumbnails | ForEach-Object { $_.Error }) -join ' ' | Should -Match 'WMI zajęte'
    }
    It 'błąd fazy: wynik częściowy zapisany, revert wykonany, wyjątek z nazwą VM' {
        Mock Invoke-E4A2VmPhase { if ($Phase -eq 'removal') { throw 'faza padła' }; [pscustomobject]@{ phase = $Phase } }
        { Invoke-E4A2VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive } | Should -Throw "*DSO-26H2-Pro*faza padła*"
        $script:Calls[-2] | Should -Be 'revert DSO-26H2-Pro'
        $script:Calls[-1] | Should -Be 'save e4a2.json'
        $script:SavedResult.error | Should -Match 'faza padła'
        $script:SavedResult.phases.inventory.phase | Should -Be 'inventory'
    }
    It 'błąd uwierzytelnienia: przekazany dalej, revert wykonany; Invoke-E4A2Exploration zatrzymuje całość' {
        Mock Wait-VmReady { $E = [System.InvalidOperationException]::new('błąd uwierzytelnienia PowerShell Direct'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4A2VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive } | Should -Throw '*uwierzytelnienia*'
        $script:Calls | Should -Contain 'revert DSO-26H2-Pro'
        $Arguments = Get-E4A2RunArguments -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        Mock Invoke-E4A2VmRun { $E = [System.InvalidOperationException]::new('x'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4A2Exploration -Arguments $Arguments 6>$null } | Should -Throw
        Should -Invoke Invoke-E4A2VmRun -Times 1 -Exactly -Scope It
    }
    It 'Invoke-E4A2Exploration: zwykły błąd jednej VM zapisany, druga idzie dalej' {
        Mock Invoke-E4A2VmRun { if ($VmName -eq 'DSO-26H2-Home') { throw 'zwykły błąd' }; "C:\w\$VmName\e4a2.json" }
        $Arguments = Get-E4A2RunArguments -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        $Outcome = Invoke-E4A2Exploration -Arguments $Arguments 6>$null
        @($Outcome.Errors).Count | Should -Be 1
        @($Outcome.Paths) | Should -Be @('C:\w\DSO-26H2-Pro\e4a2.json')
    }
}

Describe 'Blok główny SKRYPTU (-ShowPlan; bez VM)' {
    It '-ShowPlan: kod 0, plan; bez -OutDirectory kod 1' {
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Be '1'
        $Output = & $script:E4A2ScriptPath -ShowPlan -VmName DSO-26H2-Pro -OutDirectory (Join-Path $TestDrive 'w') 6>$null
        $LASTEXITCODE | Should -Be 0
        $Plan = ($Output | Out-String) | ConvertFrom-Json
        @($Plan.VmName) | Should -Be @('DSO-26H2-Pro')
        @($Plan.Targets | ForEach-Object { $_.Name }) | Should -Contain 'Microsoft.BingWeather'
        $Plan.Network | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        & $script:E4A2ScriptPath -ShowPlan 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
}

AfterAll { Clear-NoLiveVm }
