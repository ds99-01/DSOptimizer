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
    . (Join-Path $PSScriptRoot '..\explore\Invoke-E4A3Exploration.ps1')
    $script:E4A3ScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-E4A3Exploration.ps1'

    function script:Get-CommandOwner {
        param($Node)
        $Parent = $Node.Parent
        while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
        if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
    }
}

Describe 'Plan pomiaru (dane z planu Tasku 2, dosłownie)' {
    It 'zapytania CIM strony Przygotowanie: klasy, filtry i właściwości z planu' {
        $Queries = @((Get-E4A3Plan).Queries)
        @($Queries | ForEach-Object { $_.Name }) | Should -Be @('VideoController', 'SignedDriverDisplay', 'ProblemDevices', 'Activation', 'ComputerSystemProduct', 'Bios', 'Processor', 'ComputerSystem')
        $ByName = @{}
        foreach ($Query in $Queries) { $ByName[$Query.Name] = $Query }
        $ByName.VideoController.ClassName | Should -Be 'Win32_VideoController'
        @($ByName.VideoController.Properties) | Should -Be @('Name', 'PNPDeviceID', 'DriverVersion', 'DriverDate', 'InfFilename', 'AdapterCompatibility')
        $ByName.SignedDriverDisplay.ClassName | Should -Be 'Win32_PnPSignedDriver'
        $ByName.SignedDriverDisplay.Filter | Should -Be "DeviceClass='DISPLAY'"
        @($ByName.SignedDriverDisplay.Properties) | Should -Be @('DeviceID', 'DriverProviderName', 'DriverDate', 'DriverVersion')
        $ByName.ProblemDevices.ClassName | Should -Be 'Win32_PnPEntity'
        $ByName.ProblemDevices.Filter | Should -Be 'ConfigManagerErrorCode <> 0'
        @($ByName.ProblemDevices.Properties) | Should -Be @('Name', 'ConfigManagerErrorCode', 'HardwareID', 'PNPDeviceID')
        $ByName.Activation.ClassName | Should -Be 'SoftwareLicensingProduct'
        $ByName.Activation.Filter | Should -Be "ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL"
        @($ByName.Activation.Properties) | Should -Be @('LicenseStatus', 'Name')
        @($ByName.ComputerSystemProduct.Properties) | Should -Be @('Version', 'IdentifyingNumber')
        @($ByName.Bios.Properties) | Should -Be @('SerialNumber')
        @($ByName.Processor.Properties) | Should -Be @('Manufacturer')
    }
    It 'oba adresy ms-settings, próg mediany 5 s i lista urządzeń do wyłączenia' {
        $Plan = Get-E4A3Plan
        @($Plan.SettingsUris | ForEach-Object { $_.Uri }) | Should -Be @('ms-settings:windowsupdate', 'ms-settings:windowsupdate-optionalupdates')
        @($Plan.SettingsUris[1].Markers) | Should -Be @('Aktualizacje opcjonalne')
        $Plan.TimeoutThresholdMs | Should -Be 5000
        $Plan.Repeat | Should -BeGreaterOrEqual 3
        @($Plan.DisableTargets).Count | Should -BeGreaterThan 0
        @($Plan.DisableTargets) | Should -Not -Contain 'Microsoft Hyper-V Video'
    }
}

Describe 'Czysta logika (bez VM)' {
    It 'Get-E4A3Median: nieparzysta i parzysta liczba, $null pomijane, pusta = $null' {
        Get-E4A3Median -Values @(5, 1, 3) | Should -Be 3
        Get-E4A3Median -Values @(4, 1, 3, 2) | Should -Be 2.5
        Get-E4A3Median -Values @(7, $null) | Should -Be 7
        Get-E4A3Median -Values @() | Should -BeNullOrEmpty
    }
    It 'Get-E4A3IntegrityName: RID -> nazwa (Medium = nie podniesiony, High = podniesiony)' {
        Get-E4A3IntegrityName -Rid 8192 | Should -Be 'Medium'
        Get-E4A3IntegrityName -Rid 12288 | Should -Be 'High'
        Get-E4A3IntegrityName -Rid 4096 | Should -Be 'Low'
        Get-E4A3IntegrityName -Rid 1234 | Should -Be 'RID 1234'
        Get-E4A3IntegrityName -Rid -1 | Should -BeNullOrEmpty
        Get-E4A3IntegrityName -Rid $null | Should -BeNullOrEmpty
    }
    It 'Test-E4A3Markers: każdy znacznik musi być początkiem którejś nazwy; „…, aktualizacje opcjonalne” w opisie nie wystarcza' {
        $Main = @('Windows Update', 'Historia aktualizacji', 'Opcje zaawansowane', 'Optymalizacja dostarczania, aktualizacje opcjonalne, godziny aktywnego użytkowania')
        Test-E4A3Markers -Names $Main -Markers @('Windows Update', 'Historia aktualizacji', 'Opcje zaawansowane') | Should -BeTrue
        Test-E4A3Markers -Names $Main -Markers @('Aktualizacje opcjonalne') | Should -BeFalse
        Test-E4A3Markers -Names @('Windows Update', 'Aktualizacje opcjonalne') -Markers @('aktualizacje OPCJONALNE') | Should -BeTrue
        Test-E4A3Markers -Names @('x') -Markers @() | Should -BeFalse
        Test-E4A3Markers -Names @() -Markers @('x') | Should -BeFalse
    }
    It 'Get-E4A3TimeoutDecision: mediana > 5 s = 20 s, inaczej 10 s; brak median = 20 s' {
        (Get-E4A3TimeoutDecision -MediansMs @(7, 241, 124)).TimeoutSeconds | Should -Be 10
        (Get-E4A3TimeoutDecision -MediansMs @(5000)).TimeoutSeconds | Should -Be 10
        (Get-E4A3TimeoutDecision -MediansMs @(10, 5001)).TimeoutSeconds | Should -Be 20
        (Get-E4A3TimeoutDecision -MediansMs @(10, 5001)).MaxMedianMs | Should -Be 5001
        (Get-E4A3TimeoutDecision -MediansMs @()).TimeoutSeconds | Should -Be 20
    }
    It 'ConvertTo-E4A3Row: wybrane właściwości, tablice zostają tablicami, data jako ISO' {
        $Instance = New-CimInstance -ClassName Win32_PnPEntity -ClientOnly -Property @{ Name = 'Karta'; ConfigManagerErrorCode = [uint32]28; HardwareID = [string[]]@('PCI\VEN_8086&DEV_A0E8', 'PCI\VEN_8086'); Extra = 'x' }
        $Row = ConvertTo-E4A3Row -Instance $Instance -Properties @('Name', 'ConfigManagerErrorCode', 'HardwareID', 'PNPDeviceID')
        @($Row.PSObject.Properties.Name) | Should -Be @('Name', 'ConfigManagerErrorCode', 'HardwareID', 'PNPDeviceID')
        $Row.ConfigManagerErrorCode | Should -Be 28
        @($Row.HardwareID) | Should -Be @('PCI\VEN_8086&DEV_A0E8', 'PCI\VEN_8086')
        $Row.PNPDeviceID | Should -BeNullOrEmpty
        $Dated = New-CimInstance -ClassName Win32_VideoController -ClientOnly -Property @{ DriverDate = [datetime]'2006-06-21' }
        (ConvertTo-E4A3Row -Instance $Dated -Properties @('DriverDate')).DriverDate | Should -Match '^2006-06-21T'
    }
    It 'Measure-E4A3Query: Repeat odczytów, filtr tylko gdy niepusty, mediana z udanych, błąd jest daną' {
        $script:Seen = [System.Collections.Generic.List[string]]::new()
        Mock Get-CimInstance { $script:Seen.Add("$ClassName|$Filter|$($Property -join ',')"); New-CimInstance -ClassName Win32_BIOS -ClientOnly -Property @{ SerialNumber = '7491-1319' } }
        $Result = Measure-E4A3Query -Query @{ Name = 'Bios'; ClassName = 'Win32_BIOS'; Filter = ''; Properties = @('SerialNumber') } -Repeat 3
        $script:Seen.Count | Should -Be 3
        $script:Seen[0] | Should -Be 'Win32_BIOS||SerialNumber'
        @($Result.Ms).Count | Should -Be 3
        $Result.MedianMs | Should -Not -BeNullOrEmpty
        $Result.Count | Should -Be 1
        $Result.Rows[0].SerialNumber | Should -Be '7491-1319'
        Mock Get-CimInstance { throw [Microsoft.Management.Infrastructure.CimException]::new('Odmowa dostępu') }
        $Failed = Measure-E4A3Query -Query @{ Name = 'P'; ClassName = 'Win32_PnPEntity'; Filter = 'ConfigManagerErrorCode <> 0'; Properties = @('Name') } -Repeat 2
        @($Failed.Errors).Count | Should -Be 2
        $Failed.MedianMs | Should -BeNullOrEmpty
        @($Failed.Ms).Count | Should -Be 0
    }
}

Describe 'Bezpieczniki (VM-only, mock-only)' {
    It 'Test-E4A3VmComputerName: tylko DSO-26H2-HOME/PRO' {
        Test-E4A3VmComputerName -ComputerName 'dso-26h2-pro' | Should -BeTrue
        Test-E4A3VmComputerName -ComputerName 'DSO-26H2-HOME' | Should -BeTrue
        Test-E4A3VmComputerName -ComputerName 'BRUTEFORCE_LTSC' | Should -BeFalse
        Test-E4A3VmComputerName -ComputerName '' | Should -BeFalse
    }
    It 'Assert-E4A3InsideVm rzuca w trybie mock-only, nawet z nazwą VM; poza trybem rzuca na hoście' {
        { Assert-E4A3InsideVm -ComputerName 'DSO-26H2-PRO' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        $Saved = $env:DSO_VM_TESTS_MOCK_ONLY
        try {
            [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $null)
            { Assert-E4A3InsideVm -ComputerName 'HOST-PC' } | Should -Throw '*tylko w VM*'
            { Assert-E4A3InsideVm -ComputerName 'DSO-26H2-HOME' } | Should -Not -Throw
        }
        finally { $env:DSO_VM_TESTS_MOCK_ONLY = $Saved }
    }
    It '<Name> rzuca przed jakimkolwiek skutkiem (urządzenia, zadania, Ustawienia, zrzuty)' -ForEach @(
        @{ Name = 'Invoke-E4A3DeviceProbe'; Code = { Invoke-E4A3DeviceProbe -Queries @(@{ Name = 'VideoController' }) -DisableTargets @('Composite Bus Enumerator') } }
        @{ Name = 'Invoke-E4A3UserTask'; Code = { Invoke-E4A3UserTask -Name 'x' -Ops @() } }
        @{ Name = 'Invoke-E4A3OpenSettings'; Code = { Invoke-E4A3OpenSettings -Uri 'ms-settings:windowsupdate' -Label 'x' } }
        @{ Name = 'Stop-E4A3Settings'; Code = { Stop-E4A3Settings } }
        @{ Name = 'Save-E4A3K2Shot'; Code = { Save-E4A3K2Shot -Label 'x' } }
        @{ Name = 'Invoke-E4A3K2Ops'; Code = { Invoke-E4A3K2Ops -Ops @(@{ Op = 'Settings'; Uri = 'ms-settings:windowsupdate'; Label = 'x' }) } }
        @{ Name = 'Invoke-E4A3Collection'; Code = { Invoke-E4A3Collection -PlanJson '{}' -Phase devices } }
    ) {
        Mock Disable-PnpDevice { throw 'nie wolno' }
        Mock Enable-PnpDevice { throw 'nie wolno' }
        Mock Get-PnpDevice { throw 'nie wolno' }
        Mock Register-ScheduledTask { throw 'nie wolno' }
        Mock Invoke-ExploreNative { throw 'nie wolno' }
        Mock New-Item { throw 'nie wolno' }
        Mock Get-Process { throw 'nie wolno' }
        Mock Initialize-E4A3K2Native { throw 'nie wolno' }
        { & $Code } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        Should -Invoke Disable-PnpDevice -Times 0 -Exactly
        Should -Invoke Enable-PnpDevice -Times 0 -Exactly
        Should -Invoke Get-PnpDevice -Times 0 -Exactly
        Should -Invoke Register-ScheduledTask -Times 0 -Exactly
        Should -Invoke Invoke-ExploreNative -Times 0 -Exactly
        Should -Invoke New-Item -Times 0 -Exactly
        Should -Invoke Get-Process -Times 0 -Exactly
        Should -Invoke Initialize-E4A3K2Native -Times 0 -Exactly
    }
    It 'każda funkcja wysyłana do VM ze skutkiem (wprost albo przez inną taką funkcję) zaczyna się od Assert-E4A3InsideVm' {
        $EffectCommands = @('Disable-PnpDevice', 'Enable-PnpDevice', 'Register-ScheduledTask', 'Start-ScheduledTask', 'Stop-ScheduledTask', 'Unregister-ScheduledTask',
            'Set-Content', 'New-Item', 'Remove-Item', 'Move-Item', 'Copy-Item', 'Invoke-ExploreNative', 'Stop-Process')
        $EffectMembers = @('Kill', 'Start', 'CopyFromScreen', 'Save')
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4A3ScriptPath), [ref]$null, [ref]$null)
        $Remote = @(Get-E4A3RemoteFunctionNames)
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
                if (-not $Effect[$Name] -and @($Calls[$Name] | Where-Object { $Effect[$_] -and $_ -ne 'Assert-E4A3InsideVm' }).Count -gt 0) { $Effect[$Name] = $true; $Changed = $true }
            }
        } while ($Changed)
        $Guarded = @($Functions.Keys | Where-Object { $Effect[$_] } | Sort-Object)
        foreach ($Name in 'Invoke-E4A3DeviceProbe', 'Save-E4A3K2Shot', 'Stop-E4A3Settings', 'Invoke-E4A3OpenSettings', 'Invoke-E4A3K2Ops', 'Invoke-E4A3UserTask', 'Invoke-E4A3Collection') { $Guarded | Should -Contain $Name }
        foreach ($Name in $Guarded) {
            $First = @($Functions[$Name].Body.EndBlock.Statements)[0]
            $First.Extent.Text | Should -Match '^Assert-E4A3InsideVm\b' -Because "$Name ma skutek, więc pierwsza instrukcja to bezpiecznik"
        }
    }
}

Describe 'Sonda urządzeń w VM (mocki: nic nie jest wyłączane naprawdę)' {
    BeforeEach {
        Mock Assert-E4A3InsideVm { }
        Mock Start-Sleep { }
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Get-PnpDevice { if ($FriendlyName -eq 'Brak') { return }; [pscustomobject]@{ FriendlyName = $FriendlyName; InstanceId = "ROOT\$($FriendlyName -replace ' ', '')\0000" } }
        Mock Disable-PnpDevice { $script:Order.Add("disable $InstanceId"); if ($InstanceId -like '*NieDa*') { throw 'Not supported' } }
        Mock Enable-PnpDevice { $script:Order.Add("enable $InstanceId") }
        Mock Get-E4A3DeviceReads { $script:Order.Add('read'); @() }
        Mock Get-CimInstance { [pscustomobject]@{ PNPDeviceID = 'VMBUS\{DA0A7802}\{5620E0C7}' } }
        Mock Invoke-ExploreNative { $script:Order.Add("pnputil $($Arguments -join ' ')"); [pscustomobject]@{ ExitCode = 0; Stdout = 'ok' } }
        Mock Get-E4A3InfEvidence { @() }
    }
    It 'kod 22: pierwsze urządzenie, które da się wyłączyć (brak i „Not supported” pomijane), odczyt, włączenie, odczyt; potem karta graficzna' {
        $Out = Invoke-E4A3DeviceProbe -Queries @(@{ Name = 'VideoController' }) -DisableTargets @('Brak', 'Nie Da', 'Composite Bus')
        @($Out.Code22.Attempts | ForEach-Object { $_.Target }) | Should -Be @('Brak', 'Nie Da', 'Composite Bus')
        $Out.Code22.Attempts[0].DisableError | Should -Be 'brak urządzenia'
        $Out.Code22.Attempts[1].DisableError | Should -Match 'Not supported'
        $Out.Code22.Target | Should -Be 'Composite Bus'
        @($script:Order) | Should -Be @(
            'disable ROOT\NieDa\0000', 'disable ROOT\CompositeBus\0000', 'read', 'enable ROOT\CompositeBus\0000', 'read',
            'read', 'disable VMBUS\{DA0A7802}\{5620E0C7}', 'read', 'enable VMBUS\{DA0A7802}\{5620E0C7}', 'read',
            'pnputil /remove-device VMBUS\{DA0A7802}\{5620E0C7}', 'read', 'pnputil /scan-devices', 'read'
        )
    }
    It 'żadnego urządzenia nie da się wyłączyć: brak odczytów kodu 22, karta graficzna dalej mierzona' {
        $Out = Invoke-E4A3DeviceProbe -Queries @(@{ Name = 'VideoController' }) -DisableTargets @('Brak')
        $Out.Code22.Target | Should -BeNullOrEmpty
        @($script:Order | Where-Object { $_ -like 'enable ROOT*' }).Count | Should -Be 0
        @($script:Order | Where-Object { $_ -like 'pnputil*' }).Count | Should -Be 2
    }
}

Describe 'Skrypt dziecka K2 i wysyłka funkcji do VM' {
    It 'New-E4A3ChildScript: poprawna składnia, param(ResultFile, OpsFile), definicje funkcji dziecka, bezpiecznik przed operacjami, zapis atomowy' {
        $Text = New-E4A3ChildScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Text | Should -Match '^param\(\[Parameter\(Mandatory\)\]\[string\]\$ResultFile, \[Parameter\(Mandatory\)\]\[string\]\$OpsFile\)'
        foreach ($Name in Get-E4A3ChildFunctionNames) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Main = $Text.Substring($Text.IndexOf('$Document = [ordered]@{'))
        $Main | Should -Match '(?s)try \{\s*Assert-E4A3InsideVm.*Invoke-E4A3K2Ops -Ops \$Ops'
        $Text | Should -Match 'Move-Item -LiteralPath \$Temporary -Destination \$ResultFile -Force'
    }
    It 'lista funkcji dziecka jest domknięta i wysyłana do VM; dziecko nie zawiera funkcji K1 (urządzenia, zadania)' {
        $Child = @(Get-E4A3ChildFunctionNames)
        foreach ($Name in $Child) { (Get-E4A3RemoteFunctionNames) | Should -Contain $Name }
        foreach ($Name in $Child) {
            $Ast = [System.Management.Automation.Language.Parser]::ParseInput((Get-Command $Name).Definition, [ref]$null, [ref]$null)
            $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4A3*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
            foreach ($Callee in $Called) { $Child | Should -Contain $Callee -Because "$Name woła $Callee" }
        }
        foreach ($NotInChild in 'Invoke-E4A3DeviceProbe', 'Invoke-E4A3UserTask', 'Invoke-E4A3Collection') { $Child | Should -Not -Contain $NotInChild }
    }
    It 'Get-E4A3NativeSource: token przez TokenElevation (20), TokenElevationType (18) i TokenIntegrityLevel (25)' {
        $Source = Get-E4A3NativeSource
        $Source | Should -Match 'GetTokenInformation\(token, 20,'
        $Source | Should -Match 'GetTokenInformation\(token, 18,'
        $Source | Should -Match 'GetTokenInformation\(token, 25, buffer'
        $Source | Should -Match 'OpenProcess\(0x1000,'
    }
    It 'Invoke-E4A3OpenSettings: explorer.exe ze stałej ścieżki %SystemRoot%, adres w cudzysłowie, UseShellExecute=false' {
        $Body = (Get-Command Invoke-E4A3OpenSettings).Definition
        $Body | Should -Match "Join-Path \`$env:SystemRoot 'explorer\.exe'"
        $Body | Should -Match '\$Psi\.UseShellExecute = \$false'
        $Body | Should -Match "\`$Psi\.Arguments = '`"' \+ \`$Uri \+ '`"'"
        $Body | Should -Not -Match 'Start-Process'
    }
    It 'Get-E4A3RemoteScript: poprawny, definiuje wszystkie funkcje zdalne; nie woła niczego spoza listy; funkcje hosta nie są wysyłane' {
        $Text = Get-E4A3RemoteScript
        $Errors = $null
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Sent = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4A3RemoteFunctionNames)
        foreach ($Name in $Sent) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4A3*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-E4A3HostOnlyFunctionNames)) { $Text | Should -Not -Match "function $([regex]::Escape($HostOnly)) \{" }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Start-VM'
    }
    It 'każda funkcja skryptu jest wysyłana albo na liście hosta; polecenia ze skutkiem tylko w swoich funkcjach' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4A3ScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
        foreach ($Name in $Defined) { ((Get-E4A3RemoteFunctionNames) + (Get-E4A3HostOnlyFunctionNames)) | Should -Contain $Name }
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Allowed = @{
            'Disable-PnpDevice'        = @('Invoke-E4A3DeviceProbe')
            'Enable-PnpDevice'         = @('Invoke-E4A3DeviceProbe')
            'Invoke-ExploreNative'     = @('Invoke-E4A3DeviceProbe')
            'Register-ScheduledTask'   = @('Invoke-E4A3UserTask')
            'Unregister-ScheduledTask' = @('Invoke-E4A3UserTask')
            'Add-Type'                 = @('Initialize-E4A3K2Native')
            'Start-VM'                 = @('Invoke-E4A3VmRun')
            'Restore-VmCleanState'     = @('Invoke-E4A3VmRun', 'Complete-E4A3VmCleanup')
        }
        foreach ($Name in $Allowed.Keys) {
            foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq $Name })) { $Allowed[$Name] | Should -Contain (Get-CommandOwner $Command) -Because $Name }
        }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg', 'reg.exe', 'msiexec', 'msiexec.exe', 'Stop-Computer', 'Restart-Computer', 'Invoke-Expression', 'iex',
            'Invoke-RestMethod', 'Invoke-WebRequest', 'Stop-VM', 'Remove-VM', 'Restore-VMCheckpoint', 'Connect-VMNetworkAdapter', 'Set-VMNetworkAdapter', 'cmd', 'cmd.exe', 'schtasks', 'schtasks.exe',
            'Start-Process', 'Remove-PnpDevice') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4A3ScriptPath), [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|EncodedCommand'
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania; VmName tylko Home/Pro' {
        $Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $script:E4A3ScriptPath))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4A3ScriptPath), [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
        $Attr = (Get-Command $script:E4A3ScriptPath).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        $Attr2 = (Get-Command Invoke-E4A3VmRun).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr2.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
    }
}

Describe 'Argumenty przebiegu' {
    It 'Assert-E4A3OutDirectory: wymagany, bezwzględny, poza repo; w repo tylko ignorowane przez git' {
        $Repo = 'C:\Repo\DSOptimizer'
        { Assert-E4A3OutDirectory -Path '' -RepoRoot $Repo } | Should -Throw '*wymagany*'
        { Assert-E4A3OutDirectory -Path 'wyniki' -RepoRoot $Repo } | Should -Throw '*bezwzględn*'
        { Assert-E4A3OutDirectory -Path 'C:\Repo\DSOptimizer\tests\vm\out\x' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-E4A3OutDirectory -Path 'C:\Repo\DSOptimizer\wyniki\pomiar\exploration' -RepoRoot $Repo } | Should -Throw '*w repo*'
        $Real = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).ProviderPath   # prawdziwe repo: git check-ignore
        Assert-E4A3OutDirectory -Path "$Real\tests\vm\out\x" -RepoRoot $Real | Should -Be "$Real\tests\vm\out\x"   # ignorowany przez git
        { Assert-E4A3OutDirectory -Path "$Real\src\x" -RepoRoot $Real } | Should -Throw '*ignorowanym przez git*'   # nieignorowany
        Assert-E4A3OutDirectory -Path (Join-Path $env:TEMP 'dso-explore') -RepoRoot $Real | Should -Be (Join-Path $env:TEMP 'dso-explore')   # poza repo
    }
    It 'Get-E4A3RunArguments: VM i plan' {
        $Arguments = Get-E4A3RunArguments -VmName 'DSO-26H2-Pro' -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        @($Arguments.VmName) | Should -Be @('DSO-26H2-Pro')
        @($Arguments.Plan.Queries).Count | Should -Be 8
    }
}

Describe 'Orkiestracja hosta (mocki Hyper-V)' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        Mock Assert-DsoVmLiveAllowed { }
        Mock Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock Restore-VmCleanState { $script:Calls.Add("revert $VmName") }
        Mock Start-VM { $script:Calls.Add("start $Name") }
        Mock Wait-VmReady { $script:Calls.Add('ready'); $null }
        Mock Start-Sleep { }
        Mock Get-E4A3RemoteScript { 'function X {}' }
        Mock Invoke-E4A3VmPhase { $script:Calls.Add("phase $Phase"); [pscustomobject]@{ phase = $Phase } }
        Mock Copy-Item -RemoveParameterValidation 'FromSession' { }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Save-ExploreResult { $script:Calls.Add("save $FileName"); $script:SavedResult = $Result; "C:\w\$VmName\$FileName" }
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Off' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = $null } }
        $script:SavedResult = $null
    }
    It 'przebieg: revert -> start -> gotowość -> inventory -> settings -> devices -> revert -> stan końcowy -> zapis' {
        $Path = Invoke-E4A3VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e4a3.json'
        @($script:Calls) | Should -Be @('revert DSO-26H2-Pro', 'start DSO-26H2-Pro', 'ready', 'phase inventory', 'phase settings', 'phase devices', 'revert DSO-26H2-Pro', 'save e4a3.json')
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        $script:SavedResult.cleanup.FinalState | Should -Be 'Off'
        $script:SavedResult.error | Should -BeNullOrEmpty
    }
    It 'stan końcowy niezgodny z kontraktem (VM nie Off, karta na przełączniku): zapisany i zgłoszony' {
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Running' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Default Switch' } }
        { Invoke-E4A3VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive 3>$null } | Should -Throw '*stan końcowy*'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Running'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Default Switch'
    }
    It 'błąd fazy: wynik częściowy zapisany, revert wykonany, wyjątek z nazwą VM' {
        Mock Invoke-E4A3VmPhase { if ($Phase -eq 'settings') { throw 'faza padła' }; [pscustomobject]@{ phase = $Phase } }
        { Invoke-E4A3VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive } | Should -Throw '*DSO-26H2-Pro*faza padła*'
        $script:Calls[-2] | Should -Be 'revert DSO-26H2-Pro'
        $script:Calls[-1] | Should -Be 'save e4a3.json'
        $script:SavedResult.error | Should -Match 'faza padła'
        $script:SavedResult.phases.inventory.phase | Should -Be 'inventory'
    }
    It 'błąd uwierzytelnienia: przekazany dalej, revert wykonany; Invoke-E4A3Exploration zatrzymuje całość' {
        Mock Wait-VmReady { $E = [System.InvalidOperationException]::new('błąd uwierzytelnienia PowerShell Direct'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4A3VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive } | Should -Throw '*uwierzytelnienia*'
        $script:Calls | Should -Contain 'revert DSO-26H2-Pro'
        $Arguments = Get-E4A3RunArguments -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        Mock Invoke-E4A3VmRun { $E = [System.InvalidOperationException]::new('x'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4A3Exploration -Arguments $Arguments 6>$null } | Should -Throw
        Should -Invoke Invoke-E4A3VmRun -Times 1 -Exactly -Scope It
    }
    It 'Invoke-E4A3Exploration: zwykły błąd jednej VM zapisany, druga idzie dalej' {
        Mock Invoke-E4A3VmRun { if ($VmName -eq 'DSO-26H2-Home') { throw 'zwykły błąd' }; "C:\w\$VmName\e4a3.json" }
        $Arguments = Get-E4A3RunArguments -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory 'C:\w' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        $Outcome = Invoke-E4A3Exploration -Arguments $Arguments 6>$null
        @($Outcome.Errors).Count | Should -Be 1
        @($Outcome.Paths) | Should -Be @('C:\w\DSO-26H2-Pro\e4a3.json')
    }
}

Describe 'Blok główny SKRYPTU (-ShowPlan; bez VM)' {
    It '-ShowPlan: kod 0, plan; bez -OutDirectory kod 1' {
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Be '1'
        $Output = & $script:E4A3ScriptPath -ShowPlan -VmName DSO-26H2-Pro -OutDirectory (Join-Path $TestDrive 'w') 6>$null
        $LASTEXITCODE | Should -Be 0
        $Plan = ($Output | Out-String) | ConvertFrom-Json
        @($Plan.VmName) | Should -Be @('DSO-26H2-Pro')
        @($Plan.Plan.SettingsUris | ForEach-Object { $_.Uri }) | Should -Contain 'ms-settings:windowsupdate'
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        & $script:E4A3ScriptPath -ShowPlan 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
}

AfterAll { Clear-NoLiveVm }
