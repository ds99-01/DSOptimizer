BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-C2Exploration.ps1')

    function script:New-RegValue {
        param([string]$Path, [string]$Name, $Value, [string]$Kind = 'DWord')
        $Exists = $null -ne $Value
        [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $Exists; Kind = $(if ($Exists) { $Kind } else { $null }); Value = $Value; Hex = $null }
    }
}

Describe 'Get-C2StartTypeRestore (typ startu do przywrócenia pochodzi z odczytu startowego)' {
    It 'Start=2 + DelayedAutoStart=1 -> AutomaticDelayed' {
        $R = Get-C2StartTypeRestore -Start (New-RegValue 'p' 'Start' 2) -DelayedAutoStart (New-RegValue 'p' 'DelayedAutoStart' 1)
        $R.Kind | Should -Be 'AutomaticDelayed'
    }
    It 'Start=2 bez DelayedAutoStart albo z 0 -> Automatic' {
        (Get-C2StartTypeRestore -Start (New-RegValue 'p' 'Start' 2) -DelayedAutoStart (New-RegValue 'p' 'DelayedAutoStart' $null)).Kind | Should -Be 'Automatic'
        (Get-C2StartTypeRestore -Start (New-RegValue 'p' 'Start' 2) -DelayedAutoStart (New-RegValue 'p' 'DelayedAutoStart' 0)).Kind | Should -Be 'Automatic'
    }
    It 'Start=3 -> Manual, Start=4 -> Disabled' {
        (Get-C2StartTypeRestore -Start (New-RegValue 'p' 'Start' 3) -DelayedAutoStart (New-RegValue 'p' 'D' $null)).Kind | Should -Be 'Manual'
        (Get-C2StartTypeRestore -Start (New-RegValue 'p' 'Start' 4) -DelayedAutoStart (New-RegValue 'p' 'D' $null)).Kind | Should -Be 'Disabled'
    }
    It 'brak wartości Start, nieoczekiwany typ albo liczba -> $null Kind z powodem (bez zgadywania)' {
        $Missing = Get-C2StartTypeRestore -Start (New-RegValue 'p' 'Start' $null) -DelayedAutoStart (New-RegValue 'p' 'D' $null)
        $Missing.Kind | Should -BeNullOrEmpty
        $Missing.Reason | Should -Not -BeNullOrEmpty
        (Get-C2StartTypeRestore -Start (New-RegValue 'p' 'Start' 0) -DelayedAutoStart (New-RegValue 'p' 'D' $null)).Kind | Should -BeNullOrEmpty
        (Get-C2StartTypeRestore -Start (New-RegValue 'p' 'Start' '2' 'String') -DelayedAutoStart (New-RegValue 'p' 'D' $null)).Kind | Should -BeNullOrEmpty
    }
}

Describe 'Sondy C2 (mocki; usługi i rejestr hosta nietknięte)' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        $script:Svc = @{ Exists = $true; Start = 2; DelayedReg = 1; State = 'Running'; HasDependents = $false; DelayedCim = $true }
        # Bezpieczniki: każdy cmdlet zmieniający stan rzuca, dopóki test nie nadpisze go zachowaniem (modelem usługi).
        Mock Set-Service { throw 'HOST: Set-Service niedozwolone w testach' }
        Mock Stop-Service { throw 'HOST: Stop-Service niedozwolone w testach' }
        Mock Start-Service { throw 'HOST: Start-Service niedozwolone w testach' }
        Mock Invoke-ExploreNative {
            if ($FilePath -eq 'sc.exe') { throw 'HOST: sc.exe niedozwolone w testach' }
            $script:Calls.Add("$FilePath $($Arguments -join ' ')")
            [pscustomobject]@{ ExitCode = 0; Stdout = 'ok'; Stderr = ''; Error = $null; OemCodePage = 852 }
        }
        Mock Get-CimInstance {
            if ($ClassName -eq 'Win32_Service') {
                if (-not $script:Svc.Exists) { return $null }
                $Mode = switch ($script:Svc.Start) { 2 { 'Auto' } 3 { 'Manual' } 4 { 'Disabled' } default { 'Unknown' } }
                [pscustomobject]@{ Name = 'SysMain'; StartMode = $Mode; State = $script:Svc.State; DelayedAutoStart = $script:Svc.DelayedCim; ExitCode = 0; ProcessId = $(if ($script:Svc.State -eq 'Running') { 4242 } else { 0 }) }
            }
            else { throw "HOST: nieoczekiwany Get-CimInstance $ClassName" }
        }
        Mock Get-Service {
            if (-not $script:Svc.Exists) { throw [Microsoft.PowerShell.Commands.ServiceCommandException]::new('Cannot find any service with service name SysMain.') }
            $Deps = if ($script:Svc.HasDependents) { @([pscustomobject]@{ Name = 'Zalezna'; Status = 'Running' }) } else { @() }
            [pscustomobject]@{ Name = 'SysMain'; DependentServices = $Deps; ServicesDependedOn = @([pscustomobject]@{ Name = 'RpcSs'; Status = 'Running' }, [pscustomobject]@{ Name = 'FltMgr'; Status = 'Running' }) }
        }
        $script:RegBlock = {
            $Value = switch ($Name) {
                'Start' { $script:Svc.Start }
                'DelayedAutoStart' { $script:Svc.DelayedReg }
                default { $null }
            }
            [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = ($null -ne $Value); Kind = $(if ($null -ne $Value) { 'DWord' } else { $null }); Value = $Value; Hex = $null }
        }
        Mock Read-ExploreRegistryValue $script:RegBlock
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue $script:RegBlock
        Mock Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock -ModuleName ExploreCommon Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
    }
    Context 'SysMain: pełna sekwencja' {
        BeforeEach {
            # Model usługi: zachowania cmdletów zmieniają $script:Svc, a odczyty (CIM, rejestr) go odzwierciedlają.
            Mock Set-Service {
                $script:Calls.Add("Set-Service $Name $StartupType")
                switch ($StartupType) { 'Disabled' { $script:Svc.Start = 4 } 'Manual' { $script:Svc.Start = 3 } 'Automatic' { $script:Svc.Start = 2 } default { throw "nieoczekiwany typ $StartupType" } }
            }
            Mock Stop-Service {
                $script:Calls.Add("Stop-Service $Name Force=$($Force.IsPresent)")
                if ($script:Svc.HasDependents -and -not $Force.IsPresent) { throw 'Cannot stop service because it has dependent services.' }
                $script:Svc.State = 'Stopped'
            }
            Mock Start-Service { $script:Calls.Add("Start-Service $Name"); $script:Svc.State = 'Running' }
            Mock Invoke-ExploreNative {
                $script:Calls.Add("$FilePath $($Arguments -join ' ')")
                if ($FilePath -eq 'sc.exe' -and ($Arguments -join ' ') -eq 'config SysMain start= delayed-auto') { $script:Svc.Start = 2; $script:Svc.DelayedReg = 1; $script:Svc.DelayedCim = $true }
                elseif ($FilePath -eq 'sc.exe') { throw "HOST: nieoczekiwane sc.exe $($Arguments -join ' ')" }
                [pscustomobject]@{ ExitCode = 0; Stdout = '[SC] ChangeServiceConfig SUCCESS'; Stderr = ''; Error = $null; OemCodePage = 852 }
            }
        }
        It 'delayed-auto: Set-Service Disabled -> Stop-Service bez Force -> sc.exe delayed-auto -> Start-Service, dokładnie w tej kolejności' {
            $Steps = @(Invoke-C2SysMainProbe)
            $Calls | Should -Be @('Set-Service SysMain Disabled', 'Stop-Service SysMain Force=False', 'sc.exe config SysMain start= delayed-auto', 'Start-Service SysMain')
            ($Steps | ForEach-Object { $_.Name }) | Should -Be @('baseline', 'set-disabled', 'stop-service', 'przywroc-typ-startu', 'start-service-jesli-dzialala', 'po-przywroceniu', 'porownanie-z-baseline')
            $Steps[-1].Extra.MatchesBaseline | Should -BeTrue
            @($Steps[-1].Extra.Differences).Count | Should -Be 0
        }
        It 'rekord przywrócenia przez sc.exe niesie kod wyjścia i surowy stdout' {
            $Steps = @(Invoke-C2SysMainProbe)
            $Restore = $Steps | Where-Object Name -eq 'przywroc-typ-startu'
            $Restore.Command | Should -Match 'sc.exe config SysMain start= delayed-auto'
            $Restore.ExitCode | Should -Be 0
            $Restore.Stdout | Should -Match 'SUCCESS'
        }
        It 'Automatic bez delayed: przywraca Set-Service -StartupType Automatic, nie sc.exe' {
            $script:Svc.DelayedReg = $null; $script:Svc.DelayedCim = $false
            $Steps = @(Invoke-C2SysMainProbe)
            $Calls | Should -Be @('Set-Service SysMain Disabled', 'Stop-Service SysMain Force=False', 'Set-Service SysMain Automatic', 'Start-Service SysMain')
            $Steps[-1].Extra.MatchesBaseline | Should -BeTrue
        }
        It 'startowo Manual i zatrzymana: przywraca Manual i NIE startuje usługi' {
            $script:Svc.Start = 3; $script:Svc.DelayedReg = $null; $script:Svc.DelayedCim = $false; $script:Svc.State = 'Stopped'
            $Steps = @(Invoke-C2SysMainProbe)
            $Calls | Should -Be @('Set-Service SysMain Disabled', 'Stop-Service SysMain Force=False', 'Set-Service SysMain Manual')
            $Steps[-1].Extra.MatchesBaseline | Should -BeTrue
        }
        It 'usługi zależne blokują Stop-Service bez Force: błąd w kroku, typ startu przywrócony, bez zbędnego Start-Service' {
            $script:Svc.HasDependents = $true
            $Steps = @(Invoke-C2SysMainProbe)
            ($Steps | Where-Object Name -eq 'stop-service').Error | Should -Match 'dependent'
            $Calls | Should -Be @('Set-Service SysMain Disabled', 'Stop-Service SysMain Force=False', 'sc.exe config SysMain start= delayed-auto')
            $Steps[-1].Extra.MatchesBaseline | Should -BeTrue
        }
        It 'startowo Disabled: nie przywraca niczego (brak sc.exe i Set-Service Automatic)' {
            $script:Svc.Start = 4; $script:Svc.DelayedReg = $null; $script:Svc.DelayedCim = $false; $script:Svc.State = 'Stopped'
            $Steps = @(Invoke-C2SysMainProbe)
            $Calls | Should -Not -Contain 'Set-Service SysMain Automatic'
            ($Calls | Where-Object { $_ -like 'sc.exe*' }) | Should -BeNullOrEmpty
            $Steps[-1].Extra.MatchesBaseline | Should -BeTrue
        }
        It 'brak wartości Start w rejestrze: nie zgaduje przywrócenia, zapisuje powód, MatchesBaseline=false' {
            $script:Svc.Start = $null
            $Mode = $null
            Mock Set-Service { $script:Calls.Add("Set-Service $Name $StartupType"); $script:Svc.Start = 4 }
            $Steps = @(Invoke-C2SysMainProbe)
            ($Calls | Where-Object { $_ -like 'sc.exe*' -or $_ -like 'Set-Service SysMain Automatic' -or $_ -like 'Set-Service SysMain Manual' }) | Should -BeNullOrEmpty
            $Restore = $Steps | Where-Object Name -eq 'przywroc-typ-startu'
            $Restore.Extra.RestoreSkipped | Should -BeTrue
            $Steps[-1].Extra.MatchesBaseline | Should -BeFalse
            @($Steps[-1].Extra.Differences).Count | Should -BeGreaterThan 0
        }
        It 'przywrócenie, które nie zadziałało, daje MatchesBaseline=false i listę różnic' {
            Mock Invoke-ExploreNative { $script:Calls.Add("$FilePath $($Arguments -join ' ')"); [pscustomobject]@{ ExitCode = 5; Stdout = ''; Stderr = 'Access denied'; Error = $null; OemCodePage = 852 } }
            $Steps = @(Invoke-C2SysMainProbe)
            $Steps[-1].Extra.MatchesBaseline | Should -BeFalse
            ($Steps[-1].Extra.Differences -join '|') | Should -Match 'Start'
            ($Steps | Where-Object Name -eq 'przywroc-typ-startu').ExitCode | Should -Be 5
        }
        It 'MatchesBaseline nie zależy od liczby odczytów (odczyt rejestru liczony, wynik ten sam)' {
            $script:Reads = 0
            $Counting = { $script:Reads++; & $script:RegBlock }
            Mock Read-ExploreRegistryValue { $script:Reads++; $Value = switch ($Name) { 'Start' { $script:Svc.Start } 'DelayedAutoStart' { $script:Svc.DelayedReg } }; [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = ($null -ne $Value); Kind = 'DWord'; Value = $Value; Hex = $null } }
            Mock -ModuleName ExploreCommon Read-ExploreRegistryValue { $script:Reads++; $Value = switch ($Name) { 'Start' { $script:Svc.Start } 'DelayedAutoStart' { $script:Svc.DelayedReg } }; [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = ($null -ne $Value); Kind = 'DWord'; Value = $Value; Hex = $null } }
            $Steps = @(Invoke-C2SysMainProbe)
            $script:Reads | Should -BeGreaterThan 10
            $Steps[-1].Extra.MatchesBaseline | Should -BeTrue
        }
        It 'baseline zapisuje Win32_Service (StartMode, State, DelayedAutoStart, ExitCode, ProcessId), wartości Start/DelayedAutoStart i zależności z nazwami i Status' {
            $Steps = @(Invoke-C2SysMainProbe)
            $State = $Steps[0].Extra.Service
            $State.Found | Should -BeTrue
            $State.Cim.StartMode | Should -Be 'Auto'
            $State.Cim.State | Should -Be 'Running'
            $State.Cim.DelayedAutoStart | Should -BeTrue
            $State.Cim.ExitCode | Should -Be 0
            $State.Cim.ProcessId | Should -Be 4242
            @($State.DependsOn | ForEach-Object { $_.Name }) | Should -Be @('RpcSs', 'FltMgr')
            $State.DependsOn[0].Status | Should -Be 'Running'
            @($State.Dependents).Count | Should -Be 0
            $Keys = @($Steps[0].Registry | ForEach-Object { $_.Key }) -join '|'
            $Keys | Should -Match 'Services\\SysMain\\Start'
            $Keys | Should -Match 'Services\\SysMain\\DelayedAutoStart'
        }
        It 'brak usługi: tylko krok baseline z Found=false, żadna zmiana' {
            $script:Svc.Exists = $false
            $Steps = @(Invoke-C2SysMainProbe)
            @($Steps).Count | Should -Be 1
            $Steps[0].Extra.Service.Found | Should -BeFalse
            $Calls | Should -BeNullOrEmpty
        }
    }
    Context 'Memory Management (tylko odczyt)' {
        It 'zrzuca cały klucz i jawnie DisablePagingExecutive, FeatureSettingsOverride, FeatureSettingsOverrideMask; nie zmienia niczego' {
            $Steps = @(Invoke-C2MemoryManagementProbe)
            $Keys = @($Steps | ForEach-Object { $_.RegistryKeys } | ForEach-Object { $_.Path })
            $Keys | Should -Contain 'HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'
            $Names = @($Steps | ForEach-Object { $_.Registry } | ForEach-Object { $_.Key }) -join '|'
            foreach ($N in 'DisablePagingExecutive', 'FeatureSettingsOverride', 'FeatureSettingsOverrideMask') { $Names | Should -Match "Memory Management\\$N" }
            Should -Invoke Set-Service -Times 0 -Exactly
            Should -Invoke Stop-Service -Times 0 -Exactly
            Should -Invoke Start-Service -Times 0 -Exactly
            $Calls | Should -BeNullOrEmpty
        }
    }
    Context 'VBS/HVCI (tylko odczyt)' {
        BeforeEach {
            Mock Get-CimInstance {
                if ($ClassName -eq 'Win32_DeviceGuard' -and $Namespace -eq 'root\Microsoft\Windows\DeviceGuard') {
                    $Props = @(
                        [pscustomobject]@{ Name = 'VirtualizationBasedSecurityStatus'; CimType = 'UInt32'; Value = [uint32]2 }
                        [pscustomobject]@{ Name = 'SecurityServicesRunning'; CimType = 'UInt32Array'; Value = [uint32[]]@(1, 2) }
                        [pscustomobject]@{ Name = 'SecurityServicesConfigured'; CimType = 'UInt32Array'; Value = [uint32[]]@() }
                    )
                    [pscustomobject]@{ CimInstanceProperties = $Props }
                }
                else { throw "HOST: nieoczekiwany Get-CimInstance $Namespace $ClassName" }
            }
        }
        It 'Win32_DeviceGuard: wszystkie właściwości z typami, tablice jako tablice' {
            $Steps = @(Invoke-C2VbsProbe)
            $Guard = ($Steps | Where-Object Name -eq 'win32-deviceguard').Extra.DeviceGuard
            $Guard.Ok | Should -BeTrue
            $Props = @($Guard.Instances[0].Properties)
            @($Props | ForEach-Object { $_.Name }) | Should -Be @('VirtualizationBasedSecurityStatus', 'SecurityServicesRunning', 'SecurityServicesConfigured')
            $Props[0].Value | Should -Be 2
            $Props[0].CimType | Should -Be 'UInt32'
            $Props[1].IsArray | Should -BeTrue
            @($Props[1].Value) | Should -Be @(1, 2)
            $Props[2].IsArray | Should -BeTrue
            @($Props[2].Value).Count | Should -Be 0
            $Json = $Guard | ConvertTo-Json -Depth 8
            $Json | Should -Match '"Value":\s*\[\s*1,\s*2\s*\]'
        }
        It 'brak klasy Win32_DeviceGuard: Ok=false z błędem, bez wyjątku' {
            Mock Get-CimInstance { throw 'Invalid namespace' }
            $Steps = @(Invoke-C2VbsProbe)
            $Guard = ($Steps | Where-Object Name -eq 'win32-deviceguard').Extra.DeviceGuard
            $Guard.Ok | Should -BeFalse
            $Guard.Error | Should -Match 'Invalid namespace'
        }
        It 'czyta drzewo DeviceGuard, klucz CI\State i LsaCfgFlags; bcdedit /enum {current} surowo' {
            Mock Read-ExploreRegistryTree { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
            Mock -ModuleName ExploreCommon Read-ExploreRegistryTree { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
            $Steps = @(Invoke-C2VbsProbe)
            $Keys = @($Steps | ForEach-Object { $_.RegistryKeys } | ForEach-Object { $_.Path })
            $Keys | Should -Contain 'HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard'
            $Keys | Should -Contain 'HKLM\SYSTEM\CurrentControlSet\Control\CI\State'
            (@($Steps | ForEach-Object { $_.Registry } | ForEach-Object { $_.Key }) -join '|') | Should -Match 'Control\\Lsa\\LsaCfgFlags'
            $Bcd = $Steps | Where-Object Name -eq 'bcdedit-enum-current'
            $Bcd.Command | Should -Be 'bcdedit.exe /enum {current}'
            $Bcd.ExitCode | Should -Be 0
            $Bcd.Stdout | Should -Be 'ok'
            $Calls | Should -Be @('bcdedit.exe /enum {current}')
        }
        It 'nie zmienia niczego (żaden cmdlet zmieniający stan, żadne sc.exe)' {
            $null = @(Invoke-C2VbsProbe)
            Should -Invoke Set-Service -Times 0 -Exactly
            Should -Invoke Stop-Service -Times 0 -Exactly
            Should -Invoke Start-Service -Times 0 -Exactly
        }
    }
    Context 'Kolekcja' {
        It 'Invoke-C2Collection składa sondy sysmain, memoryManagement, vbsHvci i meta' {
            Mock Get-CimInstance { if ($ClassName -eq 'Win32_Service') { $null } else { throw 'brak' } }
            Mock Get-Service { throw 'brak usługi' }
            # Odczyty rejestru hosta (HKLM, tylko do odczytu) też na mockach: wynik nie zależy od maszyny, na której biegną testy.
            foreach ($Scope in @{}, @{ ModuleName = 'ExploreCommon' }) {
                Mock @Scope Read-ExploreRegistryValue { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $false; Exists = $false; Kind = $null; Value = $null; Hex = $null } }
                Mock @Scope Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $false; Values = @(); SubKeys = @() } }
                Mock @Scope Read-ExploreRegistryTree { [pscustomobject]@{ Path = $Path; KeyExists = $false; Values = @(); SubKeys = @() } }
            }
            Mock -ModuleName ExploreCommon Get-ExploreMeta { [ordered]@{ CurrentBuild = '26300'; UBR = 1; EditionID = 'Pro'; DisplayVersion = '26H2'; Culture = 'pl-PL'; UICulture = 'pl-PL' } }
            $Doc = Invoke-C2Collection | ConvertFrom-Json
            @($Doc.probes.PSObject.Properties.Name) | Should -Be @('sysmain', 'memoryManagement', 'vbsHvci')
            $Doc.meta.DisplayVersion | Should -Be '26H2'
            Should -Invoke -ModuleName ExploreCommon Read-ExploreRegistryTree -Times 1
        }
    }
}

Describe 'Wysyłka funkcji do VM (C2)' {
    It 'Get-C2RemoteScript jest poprawnym skryptem i definiuje wszystkie funkcje zdalne' {
        $Text = Get-C2RemoteScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        foreach ($Name in ((Get-ExploreCommonRemoteFunctionNames) + (Get-C2RemoteFunctionNames))) { $Text | Should -Match "function $Name \{" }
        (Get-C2RemoteFunctionNames) | Should -Contain 'Invoke-C2Collection'
    }
    It 'funkcje zdalne nie wołają niczego spoza listy wysyłanej (samowystarczalność AST)' {
        $Text = Get-C2RemoteScript
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$null)
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) |
            ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-C2*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        $Sent = (Get-ExploreCommonRemoteFunctionNames) + (Get-C2RemoteFunctionNames)
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Test-VmAuthFailure|Start-VM|Invoke-ExploreVmRun|Invoke-ExploreAll'
    }
    It 'Stop-Service jest wołany jawnie bez Force (-Force:$false), a zmieniają stan tylko: Set-Service, Stop-Service, Start-Service, sc.exe' {
        $Text = Get-C2RemoteScript
        $Text | Should -Match 'Stop-Service -Name \$ServiceName -Force:\$false'
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$null)
        $Mutating = 'Set-Item', 'New-Item', 'Remove-Item', 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'Restart-Service', 'Restart-Computer', 'reg.exe', 'bcdedit.exe-set'
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() }
        foreach ($Name in $Mutating) { $Called | Should -Not -Contain $Name }
    }
}

Describe 'Przebieg hosta C2' {
    BeforeEach {
        # Bezpieczniki: wyłącznik NoLiveVm pozostaje aktywny (tu go nie mockujemy), a Hyper-V rzuca w obu zasięgach.
        Set-NoLiveVmDefaultMock -ModuleName VmHost
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        $script:Seen = $null
        Mock -ModuleName ExploreCommon Invoke-ExploreAll { $script:Seen = @{ FileName = $FileName; EntryCommand = $EntryCommand; RemoteScript = $RemoteScript; PassUserSid = $PassUserSid.IsPresent }; [pscustomobject]@{ Paths = @('x'); Errors = @() } }
        Mock Invoke-ExploreAll { $script:Seen = @{ FileName = $FileName; EntryCommand = $EntryCommand; RemoteScript = $RemoteScript; PassUserSid = $PassUserSid.IsPresent }; [pscustomobject]@{ Paths = @('x'); Errors = @() } }
    }
    It 'Invoke-C2Exploration deleguje do wspólnego modułu z plikiem c2-exploration.json i punktem wejścia Invoke-C2Collection, bez SID' {
        $null = Invoke-C2Exploration -VmName @('DSO-26H2-Home') -OutDirectory $TestDrive
        $Seen.FileName | Should -Be 'c2-exploration.json'
        $Seen.EntryCommand | Should -Be 'Invoke-C2Collection'
        $Seen.PassUserSid | Should -BeFalse
        $Seen.RemoteScript | Should -Match 'function Invoke-C2Collection \{'
    }
    It 'parametr VmName przyjmuje tylko DSO-26H2-Home i DSO-26H2-Pro' {
        $Attr = (Get-Command (Join-Path $PSScriptRoot '..\explore\Invoke-C2Exploration.ps1')).Parameters['VmName'].Attributes |
            Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania' {
        $Path = Join-Path $PSScriptRoot '..\explore\Invoke-C2Exploration.ps1'
        $Bytes = [System.IO.File]::ReadAllBytes($Path)
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Head = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8).Substring(0, 400)
        $Head | Should -Match 'Instrukcja uruchomienia'
        [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | Should -Match 'Opis działania'
    }
}

AfterAll { Clear-NoLiveVm }
