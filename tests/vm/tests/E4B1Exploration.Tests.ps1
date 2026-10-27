BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-E4B1Exploration.ps1')
    $script:E4B1ScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-E4B1Exploration.ps1'

    function script:Get-CommandOwner {
        param($Node)
        $Parent = $Node.Parent
        while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
        if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
    }
}

Describe 'Plan pomiaru (dane z planu Tasku 2)' {
    It 'usługi WU z planu, podstawowa trójka do zatrzymania, UsoSvc i msiserver jako dodatkowe, oba foldery' {
        $Plan = Get-E4B1Plan
        @($Plan.WuServices) | Should -Be @('wuauserv', 'bits', 'cryptsvc', 'UsoSvc', 'msiserver', 'WaaSMedicSvc', 'DoSvc')
        @($Plan.WuStopBasic) | Should -Be @('wuauserv', 'bits', 'cryptsvc')
        @($Plan.WuStopExtra) | Should -Be @('UsoSvc', 'msiserver')
        @($Plan.WuFolders) | Should -Be @('SoftwareDistribution', 'System32\catroot2')
    }
    It 'polityki WU: cztery nazwy z planu w kluczu WindowsUpdate i AU' {
        $Plan = Get-E4B1Plan
        $Names = @($Plan.Policies | ForEach-Object { $_.Name })
        foreach ($Name in 'WUServer', 'NoAutoUpdate', 'DisableWindowsUpdateAccess', 'SetDisableUXWUAccess') { $Names | Should -Contain $Name }
        $Plan.PolicyKey | Should -Be 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'
        (@($Plan.Policies | Where-Object { $_.Name -eq 'NoAutoUpdate' })[0]).Key | Should -Be 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
        foreach ($Policy in $Plan.Policies) { $Policy.Kind | Should -BeIn @('REG_SZ', 'REG_DWORD') }
    }
    It 'Sklep, Instalator aplikacji (rodzina z dokumentacji winget) i adresy Ustawień' {
        $Plan = Get-E4B1Plan
        $Plan.Store.Name | Should -Be 'Microsoft.WindowsStore'
        $Plan.Store.Process | Should -Be 'WinStore.App'
        $Plan.Winget.Family | Should -Be 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe'
        $Plan.WuUri | Should -Be 'ms-settings:windowsupdate'
        $Plan.WuHistoryUri | Should -Be 'ms-settings:windowsupdate-history'
        'Sprawdź dostępność aktualizacji' | Should -Match $Plan.WuCheckButton
        'Sprawdzanie dostępności aktualizacji...' | Should -Match $Plan.WuBusyPattern
    }
    It 'adresy testowe tylko w VM: hosts w domenie .invalid, ręczny IP z sieci prywatnej' {
        $Plan = Get-E4B1Plan
        $Plan.DnsHosts.Name | Should -Match '\.invalid$'
        $Plan.DnsNegativeName | Should -Match '\.invalid$'
        $Plan.NetManual.Address | Should -Match '^192\.168\.'
    }
}

Describe 'Czysta logika (bez VM)' {
    It 'Get-E4B1StartTypeName: Start + DelayedAutostart -> nazwa typu startu' {
        Get-E4B1StartTypeName -Start 2 -Delayed 1 | Should -Be 'AutomaticDelayed'
        Get-E4B1StartTypeName -Start 2 -Delayed 0 | Should -Be 'Automatic'
        Get-E4B1StartTypeName -Start 2 -Delayed $null | Should -Be 'Automatic'
        Get-E4B1StartTypeName -Start 3 -Delayed $null | Should -Be 'Manual'
        Get-E4B1StartTypeName -Start 4 -Delayed $null | Should -Be 'Disabled'
        Get-E4B1StartTypeName -Start 9 -Delayed $null | Should -Match 'nieznany'
        Get-E4B1StartTypeName -Start $null -Delayed $null | Should -BeNullOrEmpty
    }
    It 'Get-E4B1NewBackupName: nazwa.dso-rrrrMMdd-HHmmss-e4b1 niezależnie od kultury' {
        Get-E4B1NewBackupName -FolderName 'SoftwareDistribution' -At ([datetime]'2026-10-22T09:05:07') | Should -Be 'SoftwareDistribution.dso-20261022-090507-e4b1'
        Get-E4B1NewBackupName -FolderName 'catroot2' -At ([datetime]'2026-01-02T23:59:59') | Should -Be 'catroot2.dso-20260102-235959-e4b1'
    }
    It 'Get-E4B1HostsNames: nazwy z linii bez komentarzy, wiele nazw w linii' {
        $Lines = @('# komentarz', '127.0.0.1 localhost', '', '10.1.2.3   a.invalid b.invalid  # dopisek', '   # 1.1.1.1 x')
        @(Get-E4B1HostsNames -Lines $Lines) | Should -Be @('localhost', '1.0.0.127.in-addr.arpa', 'a.invalid', 'b.invalid', '3.2.1.10.in-addr.arpa')
        @(Get-E4B1HostsNames -Lines @('::1 localhost')) | Should -Be @('localhost')
        @(Get-E4B1HostsNames -Lines @()).Count | Should -Be 0
    }
    It 'Test-E4B1DnsOnlyHosts: tylko nazwy z hosts (bez wielkości liter) albo pusto = prawda; inna nazwa = fałsz' {
        Test-E4B1DnsOnlyHosts -Entries @() -HostsNames @('a.invalid') | Should -BeTrue
        Test-E4B1DnsOnlyHosts -Entries @([pscustomobject]@{ Entry = 'A.Invalid' }) -HostsNames @('a.invalid') | Should -BeTrue
        Test-E4B1DnsOnlyHosts -Entries @([pscustomobject]@{ Entry = 'a.invalid' }, [pscustomobject]@{ Entry = 'www.microsoft.com' }) -HostsNames @('a.invalid') | Should -BeFalse
        Test-E4B1DnsOnlyHosts -Entries @([pscustomobject]@{ Entry = 'x' }) -HostsNames @() | Should -BeFalse
        $Names = Get-E4B1HostsNames -Lines @('10.123.45.67 dso-hosts-test.invalid')
        Test-E4B1DnsOnlyHosts -Entries @([pscustomobject]@{ Entry = 'dso-hosts-test.invalid' }, [pscustomobject]@{ Entry = '67.45.123.10.in-addr.arpa' }) -HostsNames $Names | Should -BeTrue
    }
    It 'Get-E4B1PhaseOrder: stała kolejność, baseline zawsze, fazy z siecią przed resetem sieci, netreset-after po netreset, bez sieci bez wuhistory/network' {
        @(Get-E4B1PhaseOrder -Phase @('network', 'dns')) | Should -Be @('baseline', 'dns', 'network')
        @(Get-E4B1PhaseOrder -Phase @('netreset')) | Should -Be @('baseline', 'netreset', 'netreset-after')
        @(Get-E4B1PhaseOrder -Phase @('wureset', 'network', 'wuhistory') -Network $false) | Should -Be @('baseline', 'wureset')
        @(Get-E4B1PhaseOrder -Phase @('baseline', 'dns', 'policies', 'store', 'winget', 'protection', 'wuhistory', 'wureset', 'netreset', 'network')) |
            Should -Be @('baseline', 'dns', 'policies', 'store', 'winget', 'protection', 'wuhistory', 'wureset', 'network', 'netreset', 'netreset-after')
        @(Get-E4B1NetworkPhases) | Should -Be @('wuhistory', 'network')
    }
    It 'Get-E4B1NativeSource: ChangeServiceConfig z SERVICE_NO_CHANGE, ControlService STOP, Restart Manager, BM_CLICK' {
        $Source = Get-E4B1NativeSource
        $Source | Should -Match 'ServiceNoChange = 0xFFFFFFFF'
        $Source | Should -Match 'ChangeServiceConfig\(service, ServiceNoChange, start, ServiceNoChange'
        $Source | Should -Match 'ChangeServiceConfig2\(service, 3, ref info\)'
        $Source | Should -Match 'ControlService\(service, 0x00000001'
        $Source | Should -Match 'RmGetList'
        $Source | Should -Match 'PostMessage\(new IntPtr\(handle\), 0x00F5'
    }
}

Describe 'Bezpieczniki (VM-only, mock-only)' {
    It 'Test-E4B1VmComputerName: tylko DSO-26H2-HOME/PRO' {
        Test-E4B1VmComputerName -ComputerName 'dso-26h2-pro' | Should -BeTrue
        Test-E4B1VmComputerName -ComputerName 'DSO-26H2-HOME' | Should -BeTrue
        Test-E4B1VmComputerName -ComputerName 'BRUTEFORCE_LTSC' | Should -BeFalse
        Test-E4B1VmComputerName -ComputerName '' | Should -BeFalse
    }
    It 'Assert-E4B1InsideVm rzuca w trybie mock-only, nawet z nazwą VM; poza trybem rzuca na hoście' {
        { Assert-E4B1InsideVm -ComputerName 'DSO-26H2-PRO' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        $Saved = $env:DSO_VM_TESTS_MOCK_ONLY
        try {
            [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $null)
            { Assert-E4B1InsideVm -ComputerName 'HOST-PC' } | Should -Throw '*tylko w VM*'
            { Assert-E4B1InsideVm -ComputerName 'DSO-26H2-HOME' } | Should -Not -Throw
        }
        finally { $env:DSO_VM_TESTS_MOCK_ONLY = $Saved }
    }
    It '<Name> rzuca przed jakimkolwiek skutkiem (netsh, reg, usługi, foldery, AppX, zadania, ochrona systemu)' -ForEach @(
        @{ Name = 'Invoke-E4B1NetResetProbe'; Code = { Invoke-E4B1NetResetProbe -Manual @{ Address = '192.168.77.10'; PrefixLength = 24; Gateway = '192.168.77.1'; Dns = @('192.168.77.53') } } }
        @{ Name = 'Invoke-E4B1NetResetAfter'; Code = { Invoke-E4B1NetResetAfter -Manual @{ Address = '192.168.77.10' } } }
        @{ Name = 'Invoke-E4B1DnsProbe'; Code = { Invoke-E4B1DnsProbe -HostsEntry @{ Address = '10.0.0.1'; Name = 'x.invalid' } -NegativeName 'y.invalid' } }
        @{ Name = 'Invoke-E4B1PolicyProbe'; Code = { Invoke-E4B1PolicyProbe -PolicyKey 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' -Policies @(@{ Key = 'HKLM\X'; Name = 'A'; Kind = 'REG_SZ'; Value = '1' }) -WuUri 'ms-settings:windowsupdate' -CheckPattern 'x' } }
        @{ Name = 'Invoke-E4B1Reg'; Code = { Invoke-E4B1Reg -Arguments @('delete', 'HKLM\SOFTWARE\X', '/f') } }
        @{ Name = 'Invoke-E4B1ServiceConfigProbe'; Code = { Invoke-E4B1ServiceConfigProbe -Names @('wuauserv') } }
        @{ Name = 'Invoke-E4B1CryptForceProbe'; Code = { Invoke-E4B1CryptForceProbe -Paths @('C:\Windows\System32\catroot2') -At (Get-Date) } }
        @{ Name = 'Invoke-E4B1WuHistoryProbe'; Code = { Invoke-E4B1WuHistoryProbe -WuUri 'u' -HistoryUri 'h' -CheckPattern 'c' -BusyPattern 'b' -ScanSeconds 1 } }
        @{ Name = 'Invoke-E4B1WuResetProbe'; Code = { Invoke-E4B1WuResetProbe -AllServices @('wuauserv') -StopBasic @('wuauserv') -StopExtra @('UsoSvc') -Folders @('SoftwareDistribution') -RecreateChecks @(0) -HistoryUri 'ms-settings:windowsupdate-history' -WuUri 'ms-settings:windowsupdate' } }
        @{ Name = 'Stop-E4B1Service'; Code = { Stop-E4B1Service -Name 'wuauserv' } }
        @{ Name = 'Move-E4B1Folder'; Code = { Move-E4B1Folder -Path 'C:\Windows\SoftwareDistribution' -NewName 'x' } }
        @{ Name = 'Invoke-E4B1StoreProbe'; Code = { Invoke-E4B1StoreProbe -UserSid 'S-1-5-21-1' -Store @{ Name = 'Microsoft.WindowsStore' } } }
        @{ Name = 'Invoke-E4B1WingetProbe'; Code = { Invoke-E4B1WingetProbe -UserSid 'S-1-5-21-1' -Winget @{ Name = 'Microsoft.DesktopAppInstaller' } } }
        @{ Name = 'Invoke-E4B1ProtectionProbe'; Code = { Invoke-E4B1ProtectionProbe -TweakId 'vmtest.hklm-value' -Description 'x' } }
        @{ Name = 'Get-E4B1Protection'; Code = { Get-E4B1Protection } }
        @{ Name = 'Invoke-E4B1Appx'; Code = { Invoke-E4B1Appx -Action Remove -Target 'Microsoft.WindowsStore' } }
        @{ Name = 'Invoke-E4B1Winget'; Code = { Invoke-E4B1Winget -Command version } }
        @{ Name = 'Invoke-E4B1ProtectionUi'; Code = { Invoke-E4B1ProtectionUi } }
        @{ Name = 'Invoke-E4B1SettingsPage'; Code = { Invoke-E4B1SettingsPage -Uri 'ms-settings:windowsupdate' -Label 'x' } }
        @{ Name = 'Invoke-E4B1StoreStart'; Code = { Invoke-E4B1StoreStart -Aumid 'a!App' -ProcessName 'WinStore.App' -WindowTitle 'Microsoft Store' -Label 'x' } }
        @{ Name = 'Start-E4B1Explorer'; Code = { Start-E4B1Explorer -Argument 'ms-settings:windowsupdate' } }
        @{ Name = 'Stop-E4B1ProcessByName'; Code = { Stop-E4B1ProcessByName -Name 'SystemSettings' } }
        @{ Name = 'Save-E4B1Shot'; Code = { Save-E4B1Shot -Label 'x' } }
        @{ Name = 'Invoke-E4B1UserTask'; Code = { Invoke-E4B1UserTask -Name 'x' -Ops @() } }
        @{ Name = 'Invoke-E4B1K2Ops'; Code = { Invoke-E4B1K2Ops -Ops @(@{ Op = 'Winget'; Command = 'version' }) } }
        @{ Name = 'Invoke-E4B1Baseline'; Code = { Invoke-E4B1Baseline -UserSid 'S-1-5-21-1' -Plan (Get-E4B1Plan) } }
        @{ Name = 'Invoke-E4B1NetworkProbe'; Code = { Invoke-E4B1NetworkProbe -DnsNames @('a') -HostsEntry @{} -NegativeName 'b' -WuUri 'u' -HistoryUri 'h' -CheckPattern 'c' -BusyPattern 'b' -ScanSeconds 1 -Folders @('f') -Services @('s') } }
        @{ Name = 'Invoke-E4B1Collection'; Code = { Invoke-E4B1Collection -PlanJson '{}' -Phase netreset -UserSid 'S-1-5-21-1' } }
    ) {
        Mock Invoke-ExploreNative { throw 'nie wolno' }
        Mock New-NetIPAddress { throw 'nie wolno' }
        Mock Set-DnsClientServerAddress { throw 'nie wolno' }
        Mock Clear-DnsClientCache { throw 'nie wolno' }
        Mock Register-ScheduledTask { throw 'nie wolno' }
        Mock Remove-AppxPackage { throw 'nie wolno' }
        Mock Add-AppxPackage { throw 'nie wolno' }
        Mock Enable-ComputerRestore { throw 'nie wolno' }
        Mock Disable-ComputerRestore { throw 'nie wolno' }
        Mock New-Item { throw 'nie wolno' }
        Mock Get-Process { throw 'nie wolno' }
        Mock Initialize-E4B1Native { throw 'nie wolno' }
        { & $Code } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        foreach ($Command in 'Invoke-ExploreNative', 'New-NetIPAddress', 'Set-DnsClientServerAddress', 'Clear-DnsClientCache', 'Register-ScheduledTask', 'Remove-AppxPackage', 'Add-AppxPackage',
            'Enable-ComputerRestore', 'Disable-ComputerRestore', 'New-Item', 'Get-Process', 'Initialize-E4B1Native') {
            Should -Invoke $Command -Times 0 -Exactly
        }
    }
    It 'każda funkcja wysyłana do VM ze skutkiem (wprost albo przez inną taką funkcję) zaczyna się od Assert-E4B1InsideVm' {
        $EffectCommands = @('Register-ScheduledTask', 'Start-ScheduledTask', 'Stop-ScheduledTask', 'Unregister-ScheduledTask', 'Set-Content', 'New-Item', 'Remove-Item', 'Move-Item', 'Copy-Item',
            'Invoke-ExploreNative', 'Stop-Process', 'New-NetIPAddress', 'Remove-NetIPAddress', 'Remove-NetRoute', 'Set-NetIPInterface', 'Set-DnsClientServerAddress', 'Clear-DnsClientCache',
            'Remove-AppxPackage', 'Add-AppxPackage', 'Enable-ComputerRestore', 'Disable-ComputerRestore', 'Invoke-CimMethod')
        $EffectMembers = @('Kill', 'Start', 'Stop', 'CopyFromScreen', 'Save', 'Move', 'AppendAllText', 'WriteAllBytes', 'StopService', 'SetStartType', 'Click', 'Invoke', 'Select')
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4B1ScriptPath), [ref]$null, [ref]$null)
        $Remote = @(Get-E4B1RemoteFunctionNames)
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
                if (-not $Effect[$Name] -and @($Calls[$Name] | Where-Object { $Effect[$_] -and $_ -ne 'Assert-E4B1InsideVm' }).Count -gt 0) { $Effect[$Name] = $true; $Changed = $true }
            }
        } while ($Changed)
        # Wyjątki odczytowe: Restart Manager i odczyt usług tylko czytają (sc qtriggerinfo przez Invoke-ExploreNative), Initialize ładuje typy.
        $ReadOnly = @('Initialize-E4B1Native', 'Get-E4B1FolderLockers', 'Get-E4B1ServiceInfo', 'Get-E4B1Elements', 'Find-E4B1Element', 'Find-E4B1Window', 'Wait-E4B1Window', 'New-E4B1ChildScript')
        $Guarded = @($Functions.Keys | Where-Object { $Effect[$_] -and $ReadOnly -notcontains $_ } | Sort-Object)
        foreach ($Name in 'Invoke-E4B1NetResetProbe', 'Invoke-E4B1WuResetProbe', 'Invoke-E4B1CryptForceProbe', 'Invoke-E4B1WuHistoryProbe', 'Invoke-E4B1PolicyProbe', 'Invoke-E4B1StoreProbe', 'Invoke-E4B1ProtectionProbe', 'Invoke-E4B1UserTask', 'Invoke-E4B1Collection') { $Guarded | Should -Contain $Name }
        foreach ($Name in $Guarded) {
            $First = @($Functions[$Name].Body.EndBlock.Statements)[0]
            $First.Extent.Text | Should -Match '^Assert-E4B1InsideVm\b' -Because "$Name ma skutek, więc pierwsza instrukcja to bezpiecznik"
        }
    }
}

Describe 'Sondy w VM (mocki: nic nie jest zmieniane naprawdę)' {
    BeforeEach {
        Mock Assert-E4B1InsideVm { }
        Mock Start-Sleep { }
        Mock Initialize-E4B1Native { }
    }
    It 'reset WU: zatrzymanie w kolejności planu, przeniesienie obu folderów, dodatkowe usługi tylko przy porażce, przywrócenie tylko działających' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Get-E4B1ServiceInfo { [pscustomobject]@{ Name = $Name; Status = if ($Name -in @('bits', 'cryptsvc', 'UsoSvc')) { 'Running' } else { 'Stopped' } } }
        Mock Get-E4B1FolderInfo { [pscustomobject]@{ Path = $Path; Exists = $true } }
        Mock Get-E4B1FolderLockers { $script:Order.Add("lockers $(Split-Path -Leaf $Path)"); [pscustomobject]@{ Path = $Path; Lockers = @() } }
        Mock Stop-E4B1Service { $script:Order.Add("stop $Name"); [pscustomobject]@{ Name = $Name; Code = 0 } }
        Mock Get-E4B1ServiceStates { @() }
        Mock Move-E4B1Folder { $script:Order.Add("move $(Split-Path -Leaf $Path) -> $NewName"); [pscustomobject]@{ Path = $Path; Moved = ((Split-Path -Leaf $Path) -ne 'catroot2' -or $NewName -notmatch '-\d{6}-e4b1$' -or $script:Retry) } }
        Mock Invoke-E4B1UserTask { $script:Order.Add("k2 $Name"); [pscustomobject]@{ Name = $Name } }
        Mock Get-E4B1WuHistoryCount { [pscustomobject]@{ Count = 0 } }
        Mock Invoke-E4B1CryptForceProbe { $script:Order.Add('cryptforce'); [pscustomobject]@{} }
        Mock New-Object -ParameterFilter { $TypeName -eq 'System.ServiceProcess.ServiceController' } { $script:Order.Add("start $($ArgumentList[0])"); [pscustomobject]@{ Status = 'Running' } | Add-Member -MemberType ScriptMethod -Name Refresh -Value { } -PassThru }
        $script:Retry = $false
        $Out = Invoke-E4B1WuResetProbe -AllServices @('wuauserv', 'bits', 'cryptsvc', 'UsoSvc', 'msiserver') -StopBasic @('wuauserv', 'bits', 'cryptsvc') -StopExtra @('UsoSvc', 'msiserver') `
            -Folders @('SoftwareDistribution', 'System32\catroot2') -RecreateChecks @(0, 30) -HistoryUri 'ms-settings:windowsupdate-history' -WuUri 'ms-settings:windowsupdate'
        $Out.Error | Should -BeNullOrEmpty
        $Stops = @($script:Order | Where-Object { $_ -like 'stop *' })
        $Stops | Should -Be @('stop wuauserv', 'stop bits', 'stop cryptsvc', 'stop UsoSvc', 'stop msiserver')
        @($script:Order | Where-Object { $_ -like 'move *' }).Count | Should -Be 3
        $script:Order.IndexOf('stop cryptsvc') | Should -BeLessThan ($script:Order.IndexOf(@($script:Order | Where-Object { $_ -like 'move SoftwareDistribution*' })[0]))
        $script:Order.IndexOf('stop UsoSvc') | Should -BeGreaterThan ($script:Order.IndexOf(@($script:Order | Where-Object { $_ -like 'move catroot2*' })[0]))
        $script:Order.IndexOf('cryptforce') | Should -BeGreaterThan ($script:Order.LastIndexOf(@($script:Order | Where-Object { $_ -like 'move catroot2*' })[-1]))
        @($script:Order | Where-Object { $_ -like 'start *' }) | Should -Be @('start cryptsvc', 'start bits', 'start UsoSvc')
        @($Out.Recreate).Count | Should -Be 3
        $script:Order[-1] | Should -Be 'k2 wureset-view'
    }
    It 'reset WU: oba foldery przeniesione od razu = bez zatrzymania dodatkowych usług' {
        $script:Stops = [System.Collections.Generic.List[string]]::new()
        Mock Get-E4B1ServiceInfo { [pscustomobject]@{ Name = $Name; Status = 'Stopped' } }
        Mock Get-E4B1FolderInfo { [pscustomobject]@{ Path = $Path } }
        Mock Get-E4B1FolderLockers { [pscustomobject]@{ Path = $Path } }
        Mock Stop-E4B1Service { $script:Stops.Add($Name); [pscustomobject]@{ Name = $Name } }
        Mock Get-E4B1ServiceStates { @() }
        Mock Move-E4B1Folder { [pscustomobject]@{ Path = $Path; Moved = $true } }
        Mock Invoke-E4B1UserTask { [pscustomobject]@{ Name = $Name } }
        Mock Get-E4B1WuHistoryCount { [pscustomobject]@{ Count = 0 } }
        Mock Invoke-E4B1CryptForceProbe { throw 'nie wolno' }
        $Out = Invoke-E4B1WuResetProbe -AllServices @('wuauserv') -StopBasic @('wuauserv', 'bits', 'cryptsvc') -StopExtra @('UsoSvc', 'msiserver') -Folders @('SoftwareDistribution', 'System32\catroot2') `
            -RecreateChecks @(0) -HistoryUri 'h' -WuUri 'u'
        @($script:Stops) | Should -Be @('wuauserv', 'bits', 'cryptsvc')
        @($Out.Retries).Count | Should -Be 0
        @($Out.Restores).Count | Should -Be 0
    }
    It 'polityki: dodanie -> widok -> export -> delete -> widok -> import -> widok, zawsze sprzątanie na końcu (także po błędzie)' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock New-Item { }
        Mock Test-Path { $false }
        Mock Get-E4B1PolicyState { @([pscustomobject]@{ Path = $Key }) }
        Mock Invoke-E4B1Reg { $script:Order.Add("reg $($Arguments[0])"); [pscustomobject]@{ ExitCode = 0 } }
        Mock Invoke-E4B1UserTask { $script:Order.Add("k2 $Name"); [pscustomobject]@{ Name = $Name } }
        $Policies = @((Get-E4B1Plan).Policies)
        $Out = Invoke-E4B1PolicyProbe -PolicyKey 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' -Policies $Policies -WuUri 'ms-settings:windowsupdate' -CheckPattern 'x'
        $Out.Error | Should -BeNullOrEmpty
        $Single = @(foreach ($Policy in $Policies) { 'reg delete'; 'reg add'; "k2 policy-$(([string]$Policy.Name).ToLowerInvariant())" })
        $Expected = @(@($Policies | ForEach-Object { 'reg add' }) + @('k2 policies-set', 'reg export', 'reg delete', 'k2 policies-deleted', 'reg import', 'k2 policies-imported') + $Single + @('reg delete'))
        @($script:Order) | Should -Be $Expected
        @($Out.Single).Count | Should -Be $Policies.Count
        $script:Order.Clear()
        Mock Invoke-E4B1UserTask { throw 'K2 padło' }
        $Failed = Invoke-E4B1PolicyProbe -PolicyKey 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' -Policies $Policies -WuUri 'ms-settings:windowsupdate' -CheckPattern 'x'
        $Failed.Error | Should -Match 'K2 padło'
        $script:Order[-1] | Should -Be 'reg delete'
    }
    It 'Sklep: start przed, usunięcie jako dso, start, rejestracja manifestu z InstallLocation, start' {
        $script:Ops = [System.Collections.Generic.List[string]]::new()
        Mock Get-E4B1PackageState { [pscustomobject]@{ Packages = @([pscustomobject]@{ InstallLocation = 'C:\Program Files\WindowsApps\Microsoft.WindowsStore_1_x64' }) } }
        Mock Invoke-E4B1UserTask { foreach ($Op in $Ops) { $script:Ops.Add("$Name $($Op.Op) $(if ($Op.ContainsKey('Action')) { "$($Op.Action) $($Op.Target)" })".Trim()) }; [pscustomobject]@{ Name = $Name } }
        $Out = Invoke-E4B1StoreProbe -UserSid 'S-1-5-21-1' -Store (Get-E4B1Plan).Store
        $Out.Error | Should -BeNullOrEmpty
        $Out.Manifest | Should -Be 'C:\Program Files\WindowsApps\Microsoft.WindowsStore_1_x64\AppxManifest.xml'
        @($script:Ops) | Should -Be @('store-start-before StoreStart', 'store-remove Appx Remove Microsoft.WindowsStore', 'store-start-removed StoreStart',
            'store-register Appx RegisterManifest C:\Program Files\WindowsApps\Microsoft.WindowsStore_1_x64\AppxManifest.xml', 'store-start-registered StoreStart')
    }
    It 'Sklep: brak pakietu na dysku = błąd jako dana, bez zadań dso' {
        Mock Get-E4B1PackageState { [pscustomobject]@{ Packages = @() } }
        Mock Invoke-E4B1UserTask { throw 'nie wolno' }
        $Out = Invoke-E4B1StoreProbe -UserSid 'S-1-5-21-1' -Store (Get-E4B1Plan).Store
        $Out.Error | Should -Match 'brak pakietu'
        Should -Invoke Invoke-E4B1UserTask -Times 0 -Exactly
    }
    It 'winget: wersja -> usunięcie -> wersja -> RegisterByFamilyName z rodziną z planu -> wersja' {
        $script:Ops = [System.Collections.Generic.List[string]]::new()
        Mock Get-E4B1PackageState { [pscustomobject]@{ Packages = @() } }
        Mock Get-E4B1WingetAlias { [pscustomobject]@{ Exists = $true } }
        Mock Invoke-E4B1UserTask { foreach ($Op in $Ops) { $script:Ops.Add("$Name $($Op.Op) $(if ($Op.ContainsKey('Command')) { $Op.Command } else { "$($Op.Action) $($Op.Target)" })") }; [pscustomobject]@{ Name = $Name } }
        $Out = Invoke-E4B1WingetProbe -UserSid 'S-1-5-21-1' -Winget (Get-E4B1Plan).Winget
        $Out.Error | Should -BeNullOrEmpty
        @($script:Ops) | Should -Be @('winget-before Winget version', 'winget-before Winget info', 'winget-remove Appx Remove Microsoft.DesktopAppInstaller', 'winget-remove Appx RemoveAlias winget.exe', 'winget-remove Winget version',
            'winget-removed Winget version', 'winget-register Appx RegisterFamily Microsoft.DesktopAppInstaller_8wekyb3d8bbwe', 'winget-registered Winget version', 'winget-registered Winget info')
    }
    It 'ochrona systemu: okno na świeżym -> Enable -> punkt przez DSOptimizer.exe --restore-point required -> Disable -> 60 s -> punkt WMI z częstotliwością 0 -> ponowne Enable -> Disable' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Get-E4B1Protection { $script:Order.Add('read'); [pscustomobject]@{} }
        Mock Enable-ComputerRestore { $script:Order.Add("enable $Drive") }
        Mock Disable-ComputerRestore { $script:Order.Add("disable $Drive") }
        Mock Invoke-E4B1Reg { $script:Order.Add("reg $($Arguments -join ' ')") }
        Mock Invoke-ExploreNative { $script:Order.Add("app $($Arguments -join ' ')"); [pscustomobject]@{ ExitCode = 0 } }
        Mock Test-Path { $false }
        Mock Invoke-E4B1UserTask { $script:Order.Add("k2 $Name"); [pscustomobject]@{} }
        Mock Invoke-CimMethod { $script:Order.Add("wmi $MethodName"); [pscustomobject]@{ ReturnValue = 0 } }
        $Out = Invoke-E4B1ProtectionProbe -TweakId 'vmtest.hklm-value' -Description 'x'
        $Out.Error | Should -BeNullOrEmpty
        @($script:Order) | Should -Be @('read', 'k2 protection-ui', 'read', 'enable C:\', 'read', 'reg add HKLM\SOFTWARE\DSOptimizer.VmTests /f',
            'app --apply vmtest.hklm-value --catalog C:\dso\vm-test-catalog.json --out C:\dso\out\e4b1-restore-point.json --restore-point required', 'read', 'disable C:\', 'read', 'read',
            'reg add HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore /v SystemRestorePointCreationFrequency /t REG_DWORD /d 0 /f', 'wmi CreateRestorePoint', 'read',
            'enable C:\', 'read', 'disable C:\', 'read')
    }
    It 'reset sieci: ręczny IP przez netsh (static) i DNS z planu, potem netsh winsock reset i netsh int ip reset z logiem, z pełnej ścieżki System32, potem to samo jako dso' {
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Select-E4B1Adapter { [pscustomobject]@{ Name = 'Ethernet'; ifIndex = 7; Status = 'Disconnected'; InterfaceGuid = '{G}' } }
        Mock Get-E4B1IpConfig { @() }
        Mock Set-NetIPInterface { $script:Order.Add("dhcp $InterfaceIndex $Dhcp") }
        Mock Invoke-E4B1UserTask { $script:Order.Add("k2 $Name $($Ops[0].Op)"); [pscustomobject]@{} }
        Mock New-NetIPAddress { $script:Order.Add("ip $InterfaceIndex $IPAddress/$PrefixLength gw $DefaultGateway") }
        Mock Set-DnsClientServerAddress { $script:Order.Add("dns $InterfaceIndex $($ServerAddresses -join ',')") }
        Mock Invoke-ExploreNative { $script:Order.Add("$FilePath $($Arguments -join ' ')"); [pscustomobject]@{ ExitCode = 0; Stdout = 'ok' } }
        Mock Get-CimInstance { [pscustomobject]@{ LastBootUpTime = [datetime]'2026-10-22T10:00:00' } }
        $Out = Invoke-E4B1NetResetProbe -Manual (Get-E4B1Plan).NetManual
        $Out.Error | Should -BeNullOrEmpty
        $Netsh = Join-Path $env:SystemRoot 'System32\netsh.exe'
        $Log = Join-Path $env:TEMP 'dso-e4b1-netsh-int-ip-reset.log'
        @($script:Order) | Should -Be @("$Netsh interface ipv4 set address name=7 source=static address=192.168.77.10 mask=255.255.255.0 gateway=192.168.77.1", 'dns 7 192.168.77.53,192.168.77.54',
            "$Netsh winsock reset", "$Netsh int ip reset $Log", 'k2 netsh-ip-reset NetshIpReset')
        Should -Invoke Set-NetIPInterface -Times 0 -Exactly
        Should -Invoke New-NetIPAddress -Times 0 -Exactly
    }
    It 'DNS: hosts dopisany i ZAWSZE przywrócony (także po błędzie), kolejność: fill -> ipconfig /flushdns -> fill -> Clear-DnsClientCache' {
        $Hosts = Join-Path $TestDrive 'hosts'
        [System.IO.File]::WriteAllText($Hosts, "127.0.0.1 localhost`r`n")
        $script:Order = [System.Collections.Generic.List[string]]::new()
        $env:DSO_E4B1_TEST_ROOT = $TestDrive
        Mock Join-Path { [System.IO.Path]::Combine([string]@($Path)[0], $ChildPath) }
        Mock Join-Path -ParameterFilter { $ChildPath -eq 'System32\drivers\etc\hosts' } { [System.IO.Path]::Combine($env:DSO_E4B1_TEST_ROOT, 'hosts') }
        Mock Get-E4B1DnsCache { $script:Order.Add('cache'); [pscustomobject]@{ Entries = @() } }
        Mock Resolve-DnsName { $script:Order.Add("resolve $Name"); @() }
        Mock Invoke-ExploreNative { $script:Order.Add("native $($Arguments -join ' ')"); [pscustomobject]@{ ExitCode = 0 } }
        Mock Clear-DnsClientCache { $script:Order.Add('clear') }
        try {
            $Out = Invoke-E4B1DnsProbe -HostsEntry @{ Address = '10.123.45.67'; Name = 'dso-hosts-test.invalid' } -NegativeName 'dso-neg-test.invalid'
            $Out.Error | Should -BeNullOrEmpty
            $Out.HostsRestored | Should -BeTrue
            @($Out.HostsNamesWithEntry) | Should -Contain 'dso-hosts-test.invalid'
            [System.IO.File]::ReadAllText($Hosts) | Should -Be "127.0.0.1 localhost`r`n"
            $First = $script:Order.IndexOf('native /flushdns')
            $First | Should -BeGreaterThan $script:Order.IndexOf('resolve dso-hosts-test.invalid')
            $script:Order.IndexOf('clear') | Should -BeGreaterThan $script:Order.LastIndexOf('resolve dso-hosts-test.invalid')
            Mock Invoke-ExploreNative { throw 'ipconfig padł' }
            $Failed = Invoke-E4B1DnsProbe -HostsEntry @{ Address = '10.123.45.67'; Name = 'dso-hosts-test.invalid' } -NegativeName 'dso-neg-test.invalid'
            $Failed.Error | Should -Match 'ipconfig padł'
            [System.IO.File]::ReadAllText($Hosts) | Should -Be "127.0.0.1 localhost`r`n"
        }
        finally { Remove-Item Env:DSO_E4B1_TEST_ROOT -ErrorAction SilentlyContinue }
    }
}

Describe 'Skrypt dziecka K2 i wysyłka funkcji do VM' {
    It 'New-E4B1ChildScript: poprawna składnia, param(ResultFile, OpsFile), definicje funkcji dziecka, bezpiecznik przed operacjami, zapis atomowy' {
        $Text = New-E4B1ChildScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Text | Should -Match '^param\(\[Parameter\(Mandatory\)\]\[string\]\$ResultFile, \[Parameter\(Mandatory\)\]\[string\]\$OpsFile\)'
        foreach ($Name in Get-E4B1ChildFunctionNames) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Main = $Text.Substring($Text.IndexOf('$Document = [ordered]@{'))
        $Main | Should -Match '(?s)try \{\s*Assert-E4B1InsideVm.*Invoke-E4B1K2Ops -Ops \$Ops'
        $Text | Should -Match 'Move-Item -LiteralPath \$Temporary -Destination \$ResultFile -Force'
    }
    It 'lista funkcji dziecka jest domknięta i wysyłana do VM; dziecko nie zawiera sond K1 (sieć, WU, polityki, zadania)' {
        $Child = @(Get-E4B1ChildFunctionNames)
        $Sent = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4B1RemoteFunctionNames)
        foreach ($Name in $Child) { $Sent | Should -Contain $Name }
        foreach ($Name in $Child) {
            $Ast = [System.Management.Automation.Language.Parser]::ParseInput((Get-Command $Name).Definition, [ref]$null, [ref]$null)
            $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4B1*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
            foreach ($Callee in $Called) { $Child | Should -Contain $Callee -Because "$Name woła $Callee" }
        }
        foreach ($NotInChild in 'Invoke-E4B1NetResetProbe', 'Invoke-E4B1WuResetProbe', 'Invoke-E4B1PolicyProbe', 'Invoke-E4B1UserTask', 'Invoke-E4B1Collection', 'Invoke-E4B1Reg') { $Child | Should -Not -Contain $NotInChild }
    }
    It 'Start-E4B1Explorer: explorer.exe ze stałej ścieżki %SystemRoot%, argument w cudzysłowie, UseShellExecute=false, tylko ms-settings: i shell:AppsFolder' {
        $Body = (Get-Command Start-E4B1Explorer).Definition
        $Body | Should -Match "Join-Path \`$env:SystemRoot 'explorer\.exe'"
        $Body | Should -Match '\$Psi\.UseShellExecute = \$false'
        $Attr = (Get-Command Start-E4B1Explorer).Parameters['Argument'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidatePatternAttribute] }
        'ms-settings:windowsupdate-history' | Should -Match $Attr.RegexPattern
        'shell:AppsFolder\Microsoft.WindowsStore_8wekyb3d8bbwe!App' | Should -Match $Attr.RegexPattern
        'https://example.com' | Should -Not -Match $Attr.RegexPattern
    }
    It 'Get-E4B1RemoteScript: poprawny, definiuje wszystkie funkcje zdalne; nie woła niczego spoza listy; funkcje hosta nie są wysyłane' {
        $Text = Get-E4B1RemoteScript
        $Errors = $null
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Sent = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E4B1RemoteFunctionNames)
        foreach ($Name in $Sent) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E4B1*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-E4B1HostOnlyFunctionNames)) { $Text | Should -Not -Match "function $([regex]::Escape($HostOnly)) \{" }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Start-VM|Connect-VMNetworkAdapter'
    }
    It 'każda funkcja skryptu jest wysyłana albo na liście hosta; polecenia ze skutkiem tylko w swoich funkcjach' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E4B1ScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
        foreach ($Name in $Defined) { ((Get-E4B1RemoteFunctionNames) + (Get-E4B1HostOnlyFunctionNames)) | Should -Contain $Name }
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Allowed = @{
            'New-NetIPAddress'            = @('Invoke-E4B1NetResetProbe')
            'Remove-NetIPAddress'         = @('Invoke-E4B1NetResetAfter')
            'Remove-NetRoute'             = @('Invoke-E4B1NetResetAfter')
            'Set-NetIPInterface'          = @('Invoke-E4B1NetResetProbe', 'Invoke-E4B1NetResetAfter')
            'Set-DnsClientServerAddress'  = @('Invoke-E4B1NetResetProbe', 'Invoke-E4B1NetResetAfter')
            'Clear-DnsClientCache'        = @('Invoke-E4B1DnsProbe')
            'Remove-AppxPackage'          = @('Invoke-E4B1Appx')
            'Add-AppxPackage'             = @('Invoke-E4B1Appx')
            'Enable-ComputerRestore'      = @('Invoke-E4B1ProtectionProbe')
            'Disable-ComputerRestore'     = @('Invoke-E4B1ProtectionProbe')
            'Invoke-CimMethod'            = @('Invoke-E4B1ProtectionProbe')
            'Register-ScheduledTask'      = @('Invoke-E4B1UserTask')
            'Unregister-ScheduledTask'    = @('Invoke-E4B1UserTask')
            'Add-Type'                    = @('Initialize-E4B1Native')
            'Start-VM'                    = @('Invoke-E4B1VmRun')
            'Restore-VmCleanState'        = @('Invoke-E4B1VmRun', 'Complete-E4B1VmCleanup')
            'Connect-VMNetworkAdapter'    = @('Invoke-E4B1VmRun')
            'Disconnect-VMNetworkAdapter' = @('Invoke-E4B1VmRun', 'Complete-E4B1VmCleanup')
            'Copy-DsoBuildToVm'           = @('Invoke-E4B1VmRun')
        }
        foreach ($Name in $Allowed.Keys) {
            foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq $Name })) { $Allowed[$Name] | Should -Contain (Get-CommandOwner $Command) -Because $Name }
        }
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Connect-VMNetworkAdapter' })) { $Command.Extent.Text | Should -Match "-SwitchName 'Default Switch'" }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg', 'netsh', 'ipconfig', 'msiexec', 'Stop-Computer', 'Invoke-Expression', 'iex', 'Invoke-RestMethod', 'Invoke-WebRequest',
            'Stop-VM', 'Remove-VM', 'Restore-VMCheckpoint', 'Set-VMNetworkAdapter', 'cmd', 'cmd.exe', 'schtasks', 'schtasks.exe', 'Start-Process', 'Remove-AppxProvisionedPackage', 'Set-Service', 'Stop-Service') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4B1ScriptPath), [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|EncodedCommand|Bruteforce_LTSC\s*[''"]|-AllUsers -Package|Remove-AppxPackage[^\r\n]*-AllUsers'
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania; VmName tylko Home/Pro' {
        $Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $script:E4B1ScriptPath))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E4B1ScriptPath), [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
        foreach ($Command in (Get-Command $script:E4B1ScriptPath), (Get-Command Invoke-E4B1VmRun), (Get-Command Restart-E4B1Vm), (Get-Command Complete-E4B1VmCleanup)) {
            $Attr = $Command.Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
            $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        }
    }
}

Describe 'Argumenty przebiegu' {
    It 'Assert-E4B1OutDirectory: wymagany, bezwzględny, poza repo; w repo tylko ignorowane przez git' {
        $Repo = 'C:\Repo\DSOptimizer'
        { Assert-E4B1OutDirectory -Path '' -RepoRoot $Repo } | Should -Throw '*wymagany*'
        { Assert-E4B1OutDirectory -Path 'wyniki' -RepoRoot $Repo } | Should -Throw '*bezwzględn*'
        { Assert-E4B1OutDirectory -Path 'C:\Repo\DSOptimizer\tests\vm\out\x' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-E4B1OutDirectory -Path 'C:\Repo\DSOptimizer\wyniki\pomiar\exploration' -RepoRoot $Repo } | Should -Throw '*w repo*'
        $Real = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).ProviderPath   # prawdziwe repo: git check-ignore
        Assert-E4B1OutDirectory -Path "$Real\tests\vm\out\x" -RepoRoot $Real | Should -Be "$Real\tests\vm\out\x"   # ignorowany przez git
        { Assert-E4B1OutDirectory -Path "$Real\src\x" -RepoRoot $Real } | Should -Throw '*ignorowanym przez git*'   # nieignorowany
        Assert-E4B1OutDirectory -Path (Join-Path $env:TEMP 'dso-explore') -RepoRoot $Real | Should -Be (Join-Path $env:TEMP 'dso-explore')   # poza repo
    }
    It 'Get-E4B1RunArguments: VM, fazy w kolejności i plan' {
        $Arguments = Get-E4B1RunArguments -VmName 'DSO-26H2-Pro' -Phase @('network', 'netreset') -OutDirectory 'C:\w' -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        @($Arguments.VmName) | Should -Be @('DSO-26H2-Pro')
        @($Arguments.Phases) | Should -Be @('baseline', 'network', 'netreset', 'netreset-after')
        @($Arguments.Plan.WuServices).Count | Should -Be 7
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
        Mock Invoke-Command -RemoveParameterValidation 'Session' { if ($ScriptBlock.ToString() -match 'shots') { @() } else { 'S-1-5-21-1-1001' } }
        Mock Start-Sleep { }
        Mock Copy-DsoBuildToVm -RemoveParameterValidation 'Session' { $script:Calls.Add('copy build') }
        Mock Get-E4B1RemoteScript { 'function X {}' }
        Mock Invoke-E4B1VmPhase { $script:Calls.Add("phase $Phase"); [pscustomobject]@{ phase = $Phase } }
        Mock Restart-E4B1Vm { $script:Calls.Add("restart $VmName"); $null }
        Mock Connect-VMNetworkAdapter { $script:Calls.Add("connect $VMName $SwitchName") }
        Mock Disconnect-VMNetworkAdapter { $script:Calls.Add("disconnect $VMName") }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Save-ExploreResult { $script:Calls.Add("save $FileName"); $script:SavedResult = $Result; "C:\w\$VmName\$FileName" }
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Off' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = $null } }
        $script:SavedResult = $null
    }
    It 'przebieg: revert -> start -> gotowość -> build -> fazy, sieć od wuhistory do network, restart przed netreset-after -> revert -> odłączenie -> zapis' {
        $Phases = Get-E4B1PhaseOrder -Phase @('wuhistory', 'wureset', 'netreset', 'network')
        $Path = Invoke-E4B1VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases $Phases -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' 6>$null
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e4b1.json'
        @($script:Calls) | Should -Be @('revert DSO-26H2-Pro', 'start DSO-26H2-Pro', 'ready', 'copy build', 'phase baseline', 'connect DSO-26H2-Pro Default Switch', 'phase wuhistory', 'phase wureset',
            'phase network', 'disconnect DSO-26H2-Pro', 'phase netreset', 'restart DSO-26H2-Pro', 'phase netreset-after', 'revert DSO-26H2-Pro', 'disconnect DSO-26H2-Pro', 'save e4b1.json')
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        $script:SavedResult.userSid | Should -Be 'S-1-5-21-1-1001'
        $script:SavedResult.error | Should -BeNullOrEmpty
    }
    It 'błąd fazy network: karta odłączona od razu, wynik częściowy zapisany, revert, wyjątek z nazwą VM' {
        Mock Invoke-E4B1VmPhase { if ($Phase -eq 'network') { throw 'skan padł' }; [pscustomobject]@{ phase = $Phase } }
        { Invoke-E4B1VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline', 'network') -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' 6>$null } | Should -Throw '*DSO-26H2-Home*skan padł*'
        $script:Calls.IndexOf('disconnect DSO-26H2-Home') | Should -BeLessThan $script:Calls.LastIndexOf('revert DSO-26H2-Home')
        $script:Calls[-1] | Should -Be 'save e4b1.json'
        $script:SavedResult.phases.baseline.phase | Should -Be 'baseline'
    }
    It 'stan końcowy niezgodny z kontraktem (VM nie Off, karta na przełączniku): zapisany i zgłoszony' {
        Mock Get-VM { [pscustomobject]@{ Name = $Name; State = 'Running' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = 'Default Switch' } }
        { Invoke-E4B1VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('baseline') -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' 3>$null 6>$null } | Should -Throw '*stan końcowy*'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Running'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Default Switch'
    }
    It 'błąd uwierzytelnienia: przekazany dalej, revert wykonany; Invoke-E4B1Exploration zatrzymuje całość' {
        Mock Wait-VmReady { $E = [System.InvalidOperationException]::new('błąd uwierzytelnienia PowerShell Direct'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4B1VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('baseline') -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' } | Should -Throw '*uwierzytelnienia*'
        $script:Calls | Should -Contain 'revert DSO-26H2-Pro'
        $Arguments = Get-E4B1RunArguments -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -Phase @('baseline') -OutDirectory 'C:\w' -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        Mock Invoke-E4B1VmRun { $E = [System.InvalidOperationException]::new('x'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E4B1Exploration -Arguments $Arguments 6>$null } | Should -Throw
        Should -Invoke Invoke-E4B1VmRun -Times 1 -Exactly -Scope It
    }
    It 'Invoke-E4B1Exploration: zwykły błąd jednej VM zapisany, druga idzie dalej' {
        Mock Invoke-E4B1VmRun { if ($VmName -eq 'DSO-26H2-Home') { throw 'zwykły błąd' }; "C:\w\$VmName\e4b1.json" }
        $Arguments = Get-E4B1RunArguments -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -Phase @('baseline') -OutDirectory 'C:\w' -BuildDirectory 'C:\b' -CatalogPath 'C:\c.json' -CheckpointName 'c' -ReadyTimeoutSeconds 60
        $Outcome = Invoke-E4B1Exploration -Arguments $Arguments 6>$null
        @($Outcome.Errors).Count | Should -Be 1
        @($Outcome.Paths) | Should -Be @('C:\w\DSO-26H2-Pro\e4b1.json')
    }
}

Describe 'Blok główny SKRYPTU (-ShowPlan; bez VM)' {
    It '-ShowPlan: kod 0, plan i fazy; bez -OutDirectory kod 1' {
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Be '1'
        $Output = & $script:E4B1ScriptPath -ShowPlan -VmName DSO-26H2-Pro -Phase dns -NoNetwork -OutDirectory (Join-Path $TestDrive 'w') 6>$null
        $LASTEXITCODE | Should -Be 0
        $Plan = ($Output | Out-String) | ConvertFrom-Json
        @($Plan.VmName) | Should -Be @('DSO-26H2-Pro')
        @($Plan.Phases) | Should -Be @('baseline', 'dns')
        $Plan.Network | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        & $script:E4B1ScriptPath -ShowPlan 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
}

AfterAll { Clear-NoLiveVm }
