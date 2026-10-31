BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-C3Exploration.ps1')

    # Typ zastępujący enum NetworkCategory z CIM (tylko definicja typu w pamięci procesu testów).
    if (-not ('DsoTestNetCategory' -as [type])) {
        Add-Type -TypeDefinition 'public enum DsoTestNetCategory { Public = 0, Private = 1, DomainAuthenticated = 2 }'
    }
    if (-not ('DsoTestConnectivity' -as [type])) {
        Add-Type -TypeDefinition 'public enum DsoTestConnectivity { Disconnected = 0, NoTraffic = 1, Subnet = 2, LocalNetwork = 3, Internet = 4 }'
    }
}

Describe 'Sondy C3 (mocki; sieć, SMB i rejestr hosta nietknięte)' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        $script:Smb = @{ Guest = $false; Sign = $false; FailGet = $false; FailSetCall = 0; SetCount = 0; Audit = $false; EnableSign = $true }
        # Bezpieczniki: każdy cmdlet zmieniający stan lub czytający sieć rzuca, dopóki test nie nadpisze go zachowaniem.
        Mock Set-SmbClientConfiguration { throw 'HOST: Set-SmbClientConfiguration niedozwolone w testach' }
        Mock Get-SmbClientConfiguration { throw 'HOST: Get-SmbClientConfiguration niedozwolone w testach' }
        Mock Set-NetConnectionProfile { throw 'HOST: Set-NetConnectionProfile niedozwolone w testach' }
        Mock Disable-NetAdapterBinding { throw 'HOST: Disable-NetAdapterBinding niedozwolone w testach' }
        Mock Enable-NetAdapterBinding { throw 'HOST: Enable-NetAdapterBinding niedozwolone w testach' }
        Mock Get-NetConnectionProfile { throw 'HOST: Get-NetConnectionProfile niedozwolone w testach' }
        Mock Get-NetAdapter { throw 'HOST: Get-NetAdapter niedozwolone w testach' }
        Mock Get-NetAdapterBinding { throw 'HOST: Get-NetAdapterBinding niedozwolone w testach' }
        Mock Get-NetPrefixPolicy { throw 'HOST: Get-NetPrefixPolicy niedozwolone w testach' }
        Mock Get-CimInstance { throw "HOST: Get-CimInstance niedozwolone w testach ($ClassName)" }
        Mock Invoke-ExploreNative { throw 'HOST: Invoke-ExploreNative niedozwolone w testach' }
        $script:RegBlock = {
            [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $false; Kind = $null; Value = $null; Hex = $null }
        }
        Mock Read-ExploreRegistryValue $script:RegBlock
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue $script:RegBlock
        Mock Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock -ModuleName ExploreCommon Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock Read-ExploreRegistryTree { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock -ModuleName ExploreCommon Read-ExploreRegistryTree { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
    }

    Context 'SMB: sekwencja odczyt -> guest -> odczyt -> signing -> odczyt -> przywrócenie -> odczyt' {
        BeforeEach {
            Mock Get-SmbClientConfiguration {
                $script:Calls.Add('Get')
                if ($script:Smb.FailGet) { throw 'Get-SmbClientConfiguration: brak' }
                [pscustomobject]@{
                    EnableInsecureGuestLogons = $script:Smb.Guest; RequireSecuritySignature = $script:Smb.Sign
                    EnableSecuritySignature = $script:Smb.EnableSign; AuditInsecureGuestLogon = $script:Smb.Audit
                }
            }
            Mock Set-SmbClientConfiguration {
                $script:Smb.SetCount++
                # Bool niepodany ma wartość $false, więc do logu tylko parametry faktycznie podane.
                $GuestText = if ($PesterBoundParameters.ContainsKey('EnableInsecureGuestLogons')) { $EnableInsecureGuestLogons } else { '' }
                $SignText = if ($PesterBoundParameters.ContainsKey('RequireSecuritySignature')) { $RequireSecuritySignature } else { '' }
                $script:Calls.Add("Set guest=$GuestText sign=$SignText force=$($Force.IsPresent)")
                if ($script:Smb.FailSetCall -eq $script:Smb.SetCount) { throw 'Set-SmbClientConfiguration: odmowa' }
                if ($PesterBoundParameters.ContainsKey('EnableInsecureGuestLogons')) { $script:Smb.Guest = [bool]$EnableInsecureGuestLogons }
                if ($PesterBoundParameters.ContainsKey('RequireSecuritySignature')) { $script:Smb.Sign = [bool]$RequireSecuritySignature }
            }
        }
        It 'kolejność wywołań i nazwy kroków; przywrócenie używa wartości z odczytu STARTOWEGO (nie z pośrednich)' {
            $script:Smb.Guest = $false; $script:Smb.Sign = $true
            $Steps = @(Invoke-C3SmbProbe)
            $Calls | Should -Be @('Get', 'Set guest=True sign= force=True', 'Get', 'Set guest= sign=False force=True', 'Get', 'Set guest=False sign=True force=True', 'Get')
            ($Steps | ForEach-Object { $_.Name }) | Should -Be @('baseline', 'set-guest', 'set-signing', 'przywroc-z-odczytu-startowego')
            $Steps[-1].Extra.MatchesInitial | Should -BeTrue
            @($Steps[-1].Extra.Differences).Count | Should -Be 0
            $script:Smb.Guest | Should -BeFalse
            $script:Smb.Sign | Should -BeTrue
        }
        It 'start = wartości docelowe z planu (Guest=True, Sign=False): zmiana jest na PRZECIWNE, restore wraca do startowych' {
            $script:Smb.Guest = $true; $script:Smb.Sign = $false
            $Steps = @(Invoke-C3SmbProbe)
            $Calls | Should -Be @('Get', 'Set guest=False sign= force=True', 'Get', 'Set guest= sign=True force=True', 'Get', 'Set guest=True sign=False force=True', 'Get')
            $Steps[1].Extra.Smb.Values.EnableInsecureGuestLogons | Should -BeFalse
            $Steps[2].Extra.Smb.Values.RequireSecuritySignature | Should -BeTrue
            $Steps[-1].Extra.MatchesInitial | Should -BeTrue
            $script:Smb.Guest | Should -BeTrue
            $script:Smb.Sign | Should -BeFalse
        }
        It 'start różny od docelowych (Guest=False, Sign=False): obie zmiany na True, restore do False/False' {
            $Steps = @(Invoke-C3SmbProbe)
            $Calls | Should -Be @('Get', 'Set guest=True sign= force=True', 'Get', 'Set guest= sign=True force=True', 'Get', 'Set guest=False sign=False force=True', 'Get')
            $Steps[-1].Extra.MatchesInitial | Should -BeTrue
        }
        It 'start = docelowe: nieudane przywrócenie nadal zapisane (Error, MatchesInitial=false)' {
            $script:Smb.Guest = $true; $script:Smb.Sign = $false
            $script:Smb.FailSetCall = 3
            $Steps = @(Invoke-C3SmbProbe)
            $Restore = $Steps | Where-Object Name -eq 'przywroc-z-odczytu-startowego'
            $Restore.Error | Should -Match 'odmowa'
            $Restore.Extra.MatchesInitial | Should -BeFalse
            ($Restore.Extra.Differences -join '|') | Should -Match 'RequireSecuritySignature'
        }
        It 'każdy krok niesie odczyt (Smb) z czterema właściwościami: wartościami surowymi' {
            $script:Smb.Guest = $true; $script:Smb.Sign = $true; $script:Smb.Audit = $true
            $Steps = @(Invoke-C3SmbProbe)
            foreach ($Step in $Steps) {
                $Step.Extra.Smb.Ok | Should -BeTrue
                @($Step.Extra.Smb.Values.PSObject.Properties.Name) | Should -Be @('EnableInsecureGuestLogons', 'RequireSecuritySignature', 'EnableSecuritySignature', 'AuditInsecureGuestLogon')
            }
            $Steps[0].Extra.Smb.Values.EnableInsecureGuestLogons | Should -BeTrue
            $Steps[0].Extra.Smb.Values.AuditInsecureGuestLogon | Should -BeTrue
            $Steps[1].Extra.Smb.Values.EnableInsecureGuestLogons | Should -BeFalse # przeciwna do startowej (True)
            $Steps[2].Extra.Smb.Values.RequireSecuritySignature | Should -BeFalse # przeciwna do startowej (True)
        }
        It 'każdy krok zrzuca oba klucze rejestru SMB klienta i jawnie AllowInsecureGuestAuth oraz RequireSecuritySignature (Exists osobno od Value)' {
            $Steps = @(Invoke-C3SmbProbe)
            foreach ($Step in $Steps) {
                $Keys = @($Step.RegistryKeys | ForEach-Object { $_.Path })
                $Keys | Should -Contain 'HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters'
                $Keys | Should -Contain 'HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation'
                $Names = @($Step.Registry | ForEach-Object { $_.Key }) -join '|'
                $Names | Should -Match 'LanmanWorkstation\\Parameters\\AllowInsecureGuestAuth'
                $Names | Should -Match 'Windows\\LanmanWorkstation\\AllowInsecureGuestAuth'
                $Names | Should -Match 'LanmanWorkstation\\Parameters\\RequireSecuritySignature'
            }
        }
        It 'nieudane przywrócenie jest ZAPISANE (Error kroku, MatchesInitial=false, różnice), nie połknięte' {
            $script:Smb.Guest = $false; $script:Smb.Sign = $true
            $script:Smb.FailSetCall = 3
            $Steps = @(Invoke-C3SmbProbe)
            $Restore = $Steps | Where-Object Name -eq 'przywroc-z-odczytu-startowego'
            $Restore.Error | Should -Match 'odmowa'
            $Restore.Extra.MatchesInitial | Should -BeFalse
            ($Restore.Extra.Differences -join '|') | Should -Match 'RequireSecuritySignature'
            $Calls[-1] | Should -Be 'Get'
        }
        It 'błąd kroku set-guest jest zapisany, ale podpisywanie i przywrócenie nadal się odbywają' {
            $script:Smb.Guest = $false; $script:Smb.Sign = $true
            $script:Smb.FailSetCall = 1
            $Steps = @(Invoke-C3SmbProbe)
            ($Steps | Where-Object Name -eq 'set-guest').Error | Should -Match 'odmowa'
            @($Calls | Where-Object { $_ -like 'Set*' }).Count | Should -Be 3
            $Steps[-1].Extra.MatchesInitial | Should -BeTrue
        }
        It 'brak odczytu startowego (Get rzuca): zero zmian, tylko krok baseline z Ok=false i błędem' {
            $script:Smb.FailGet = $true
            $Steps = @(Invoke-C3SmbProbe)
            @($Steps).Count | Should -Be 1
            $Steps[0].Name | Should -Be 'baseline'
            $Steps[0].Extra.Smb.Ok | Should -BeFalse
            $Steps[0].Extra.Smb.Error | Should -Match 'brak'
            Should -Invoke Set-SmbClientConfiguration -Times 0 -Exactly
        }
        It 'wartość startowa nie jest logiczna (np. brak właściwości): nie zgaduje, zero zmian, powód w kroku' {
            Mock Get-SmbClientConfiguration { $script:Calls.Add('Get'); [pscustomobject]@{ EnableInsecureGuestLogons = $null; RequireSecuritySignature = $false } }
            $Steps = @(Invoke-C3SmbProbe)
            Should -Invoke Set-SmbClientConfiguration -Times 0 -Exactly
            $Steps[-1].Extra.RestoreSkipped | Should -BeTrue
            $Steps[-1].Extra.Reason | Should -Not -BeNullOrEmpty
        }
    }

    Context 'Rejestr: SystemProfile, IPv6 (tylko odczyt)' {
        It 'SystemProfile: zrzut klucza i jawnie NetworkThrottlingIndex oraz SystemResponsiveness; zero zapisów' {
            $Steps = @(Invoke-C3SystemProfileProbe)
            $Keys = @($Steps | ForEach-Object { $_.RegistryKeys } | ForEach-Object { $_.Path })
            $Keys | Should -Contain 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
            $Names = @($Steps | ForEach-Object { $_.Registry } | ForEach-Object { $_.Key }) -join '|'
            $Names | Should -Match 'SystemProfile\\NetworkThrottlingIndex'
            $Names | Should -Match 'SystemProfile\\SystemResponsiveness'
            Should -Invoke Invoke-ExploreNative -Times 0 -Exactly
        }
        It 'IPv6: DisabledComponents + zrzut klucza; Get-NetPrefixPolicy i Get-NetAdapterBinding -ComponentID ms_tcpip6 jako dane; błąd cmdletu zapisany' {
            Mock Get-NetPrefixPolicy { @([pscustomobject]@{ Prefix = '::1/128'; Precedence = 50; Label = 0; PolicyStore = 'ActiveStore' }) }
            Mock Get-NetAdapterBinding { if ($ComponentID -ne 'ms_tcpip6') { throw "zły ComponentID $ComponentID" }; throw 'No MSFT_NetAdapterBindingSettingData objects found' }
            $Steps = @(Invoke-C3Ipv6Probe)
            $Keys = @($Steps | ForEach-Object { $_.RegistryKeys } | ForEach-Object { $_.Path })
            $Keys | Should -Contain 'HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'
            (@($Steps | ForEach-Object { $_.Registry } | ForEach-Object { $_.Key }) -join '|') | Should -Match 'Tcpip6\\Parameters\\DisabledComponents'
            $Policy = ($Steps | Where-Object Name -eq 'get-netprefixpolicy').Extra.NetPrefixPolicy
            $Policy.Ok | Should -BeTrue
            @($Policy.Items).Count | Should -Be 1
            $Policy.Items[0].Prefix | Should -Be '::1/128'
            $Policy.Items[0].Precedence | Should -Be 50
            $Binding = ($Steps | Where-Object Name -eq 'get-netadapterbinding-ms-tcpip6').Extra.NetAdapterBinding
            $Binding.Ok | Should -BeFalse
            $Binding.Error | Should -Match 'No MSFT_NetAdapterBindingSettingData'
            Should -Invoke Disable-NetAdapterBinding -Times 0 -Exactly
            Should -Invoke Enable-NetAdapterBinding -Times 0 -Exactly
        }
    }

    Context 'Usługi (tylko odczyt)' {
        It 'Win32_Service dla siedmiu usług: StartMode, State, DelayedAutoStart; brak usługi = Found=false' {
            Mock Get-CimInstance {
                if ($ClassName -ne 'Win32_Service') { throw "nieoczekiwane $ClassName" }
                if ($Filter -match "'lmhosts'") { return $null }
                $Name = [regex]::Match($Filter, "Name='([^']+)'").Groups[1].Value
                [pscustomobject]@{ Name = $Name; StartMode = 'Manual'; State = 'Stopped'; DelayedAutoStart = $false }
            }
            $Steps = @(Invoke-C3ServicesProbe)
            $Services = @($Steps[0].Extra.Services)
            @($Services | ForEach-Object { $_.Name }) | Should -Be @('fdPHost', 'FDResPub', 'SSDPSRV', 'upnphost', 'lmhosts', 'LanmanWorkstation', 'LanmanServer')
            ($Services | Where-Object Name -eq 'fdPHost').StartMode | Should -Be 'Manual'
            ($Services | Where-Object Name -eq 'fdPHost').Found | Should -BeTrue
            ($Services | Where-Object Name -eq 'lmhosts').Found | Should -BeFalse
        }
        It 'błąd CIM jednej usługi jest zapisany przy niej, pozostałe czytane dalej' {
            Mock Get-CimInstance {
                if ($Filter -match "'SSDPSRV'") { throw 'RPC niedostępny' }
                [pscustomobject]@{ StartMode = 'Auto'; State = 'Running'; DelayedAutoStart = $true }
            }
            $Services = @((@(Invoke-C3ServicesProbe))[0].Extra.Services)
            ($Services | Where-Object Name -eq 'SSDPSRV').Error | Should -Match 'RPC'
            ($Services | Where-Object Name -eq 'upnphost').DelayedAutoStart | Should -BeTrue
        }
    }

    Context 'Profil sieciowy i karty (tylko odczyt)' {
        It 'NetworkCategory zapisane jako [string] i [int], IPv4Connectivity też; liczba obiektów' {
            Mock Get-NetConnectionProfile {
                @([pscustomobject]@{ Name = 'Network'; InterfaceAlias = 'Ethernet'; InterfaceIndex = 5; NetworkCategory = [DsoTestNetCategory]::Private
                        IPv4Connectivity = [DsoTestConnectivity]::Internet; IPv6Connectivity = [DsoTestConnectivity]::Disconnected })
            }
            Mock Get-NetAdapter { @([pscustomobject]@{ Name = 'Ethernet'; Status = 'Up'; InterfaceDescription = 'Hyper-V Network Adapter'; ifIndex = 5 }) }
            Mock Get-CimInstance {
                if ($Namespace -ne 'root\StandardCimv2' -or $ClassName -ne 'MSFT_NetConnectionProfile') { throw "nieoczekiwane $Namespace $ClassName" }
                @([pscustomobject]@{ Name = 'Network'; InterfaceAlias = 'Ethernet'; NetworkCategory = [uint32]1; IPv4Connectivity = [uint32]4 })
            }
            $Steps = @(Invoke-C3NetworkProfileProbe)
            $Profile = ($Steps | Where-Object Name -eq 'get-netconnectionprofile').Extra.NetConnectionProfile
            $Profile.Ok | Should -BeTrue
            $Profile.Count | Should -Be 1
            $Profile.Items[0].NetworkCategory.String | Should -Be 'Private'
            $Profile.Items[0].NetworkCategory.Int | Should -Be 1
            $Profile.Items[0].IPv4Connectivity.String | Should -Be 'Internet'
            $Profile.Items[0].IPv4Connectivity.Int | Should -Be 4
            $Adapter = ($Steps | Where-Object Name -eq 'get-netadapter').Extra.NetAdapter
            $Adapter.Count | Should -Be 1
            $Adapter.Items[0].Status | Should -Be 'Up'
            $Cim = ($Steps | Where-Object Name -eq 'cim-msft-netconnectionprofile').Extra.CimProfile
            $Cim.Count | Should -Be 1
            $Cim.Items[0].NetworkCategory.Int | Should -Be 1
        }
        It 'pusty wynik: Ok=true, Count=0 (to dane, nie błąd)' {
            Mock Get-NetConnectionProfile { @() }
            Mock Get-NetAdapter { @() }
            Mock Get-CimInstance { @() }
            $Steps = @(Invoke-C3NetworkProfileProbe)
            $Profile = ($Steps | Where-Object Name -eq 'get-netconnectionprofile').Extra.NetConnectionProfile
            $Profile.Ok | Should -BeTrue
            $Profile.Count | Should -Be 0
            @($Profile.Items).Count | Should -Be 0
        }
        It 'błędy wszystkich trzech odczytów są zapisane jako dane i nie przerywają sondy' {
            Mock Get-NetConnectionProfile { throw 'Brak profilu: sieć niedostępna' }
            Mock Get-NetAdapter { throw 'No MSFT_NetAdapter objects found' }
            Mock Get-CimInstance { throw 'Invalid namespace' }
            $Steps = @(Invoke-C3NetworkProfileProbe)
            @($Steps).Count | Should -Be 3
            ($Steps | Where-Object Name -eq 'get-netconnectionprofile').Extra.NetConnectionProfile.Error | Should -Match 'sieć niedostępna'
            ($Steps | Where-Object Name -eq 'get-netadapter').Extra.NetAdapter.Ok | Should -BeFalse
            ($Steps | Where-Object Name -eq 'cim-msft-netconnectionprofile').Extra.CimProfile.Error | Should -Match 'Invalid namespace'
            Should -Invoke Set-NetConnectionProfile -Times 0 -Exactly
        }
    }

    Context 'Kolekcja' {
        It 'Invoke-C3Collection składa sondy w ustalonej kolejności i meta' {
            Mock Get-SmbClientConfiguration { throw 'brak' }
            Mock Get-CimInstance { throw 'brak' }
            Mock Get-NetConnectionProfile { throw 'brak' }
            Mock Get-NetAdapter { throw 'brak' }
            Mock Get-NetAdapterBinding { throw 'brak' }
            Mock Get-NetPrefixPolicy { throw 'brak' }
            Mock -ModuleName ExploreCommon Get-ExploreMeta { [ordered]@{ CurrentBuild = '26300'; UBR = 1; EditionID = 'Pro'; DisplayVersion = '26H2'; Culture = 'pl-PL'; UICulture = 'pl-PL' } }
            $Doc = Invoke-C3Collection | ConvertFrom-Json
            @($Doc.probes.PSObject.Properties.Name) | Should -Be @('systemProfile', 'ipv6', 'smbClient', 'services', 'networkProfile')
            $Doc.meta.DisplayVersion | Should -Be '26H2'
            Should -Invoke Set-SmbClientConfiguration -Times 0 -Exactly
        }
    }
}

Describe 'Wysyłka funkcji do VM (C3)' {
    It 'Get-C3RemoteScript jest poprawnym skryptem i definiuje wszystkie funkcje zdalne' {
        $Text = Get-C3RemoteScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        foreach ($Name in ((Get-ExploreCommonRemoteFunctionNames) + (Get-C3RemoteFunctionNames))) { $Text | Should -Match "function $Name \{" }
        (Get-C3RemoteFunctionNames) | Should -Contain 'Invoke-C3Collection'
    }
    It 'funkcje zdalne nie wołają niczego spoza listy wysyłanej (samowystarczalność AST)' {
        $Text = Get-C3RemoteScript
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$null)
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) |
            ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-C3*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        $Sent = (Get-ExploreCommonRemoteFunctionNames) + (Get-C3RemoteFunctionNames)
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Test-VmAuthFailure|Start-VM|Invoke-ExploreVmRun|Invoke-ExploreAll'
    }
    It 'jedyną zmianą stanu jest Set-SmbClientConfiguration, i tylko wewnątrz Invoke-C3SmbProbe (z -Force)' {
        $Path = Join-Path $PSScriptRoot '..\explore\Invoke-C3Exploration.ps1'
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $Path), [ref]$null, [ref]$null)
        $Commands = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        $Mutating = $Names | Where-Object { $_ -match '^(Set|New|Remove|Add|Enable|Disable|Restart|Stop|Start|Clear|Rename|Move|Copy|Write|Out|Register|Unregister|Reset|Install|Uninstall)-' } |
            Where-Object { $_ -notin 'Write-Host', 'Start-VM', 'Set-StrictMode', 'Import-Module', 'New-Object', 'New-Item', 'Out-Null' }
        # Poza hostowym przebiegiem (New-Item katalog wyników, Write-Host) nic nie zmienia stanu; Start-VM/Stop-VM tylko w module.
        @($Mutating | Sort-Object -Unique) | Should -Be @('Set-SmbClientConfiguration')
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Set-SmbClientConfiguration' })) {
            $Parent = $Command.Parent
            while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
            $Parent.Name | Should -Be 'Invoke-C3SmbProbe'
            $Command.Extent.Text | Should -Match '-Force'
        }
        foreach ($Forbidden in 'Set-NetConnectionProfile', 'Disable-NetAdapterBinding', 'Enable-NetAdapterBinding', 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg.exe', 'netsh', 'Restart-Computer', 'Set-NetIPv6Protocol') {
            $Names | Should -Not -Contain $Forbidden
        }
    }
}

Describe 'Przebieg hosta C3' {
    BeforeEach {
        # Bezpieczniki: wyłącznik NoLiveVm pozostaje aktywny (tu go nie mockujemy), a Hyper-V rzuca w obu zasięgach.
        Set-NoLiveVmDefaultMock -ModuleName VmHost
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        Mock Set-SmbClientConfiguration { throw 'HOST: Set-SmbClientConfiguration niedozwolone w testach' }
        $script:Seen = $null
        Mock -ModuleName ExploreCommon Invoke-ExploreAll { $script:Seen = @{ FileName = $FileName; EntryCommand = $EntryCommand; RemoteScript = $RemoteScript; PassUserSid = $PassUserSid.IsPresent; VmName = $VmName; CheckpointName = $CheckpointName }; [pscustomobject]@{ Paths = @('x'); Errors = @() } }
        Mock Invoke-ExploreAll { $script:Seen = @{ FileName = $FileName; EntryCommand = $EntryCommand; RemoteScript = $RemoteScript; PassUserSid = $PassUserSid.IsPresent; VmName = $VmName; CheckpointName = $CheckpointName }; [pscustomobject]@{ Paths = @('x'); Errors = @() } }
    }
    It 'Invoke-C3Exploration deleguje do wspólnego modułu z plikiem c3-exploration.json i punktem wejścia Invoke-C3Collection, bez SID, checkpoint czysta-26H2' {
        $null = Invoke-C3Exploration -VmName @('DSO-26H2-Home') -OutDirectory $TestDrive
        $Seen.FileName | Should -Be 'c3-exploration.json'
        $Seen.EntryCommand | Should -Be 'Invoke-C3Collection'
        $Seen.PassUserSid | Should -BeFalse
        $Seen.CheckpointName | Should -Be 'czysta-26H2'
        $Seen.RemoteScript | Should -Match 'function Invoke-C3Collection \{'
        Should -Invoke Set-SmbClientConfiguration -Times 0 -Exactly
    }
    It 'parametr VmName przyjmuje tylko DSO-26H2-Home i DSO-26H2-Pro (nigdy obcej VM)' {
        $Attr = (Get-Command (Join-Path $PSScriptRoot '..\explore\Invoke-C3Exploration.ps1')).Parameters['VmName'].Attributes |
            Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania' {
        $Path = Join-Path $PSScriptRoot '..\explore\Invoke-C3Exploration.ps1'
        $Bytes = [System.IO.File]::ReadAllBytes($Path)
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
    }
    It 'błąd uwierzytelnienia zatrzymuje całość: logika we wspólnym module (Invoke-ExploreAll), skrypt jej nie dubluje' {
        $Text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\explore\Invoke-C3Exploration.ps1'), [System.Text.Encoding]::UTF8)
        $Text | Should -Match 'Invoke-ExploreAll'
        $Text | Should -Not -Match 'Restore-VMCheckpoint|Start-VM|New-PSSession'
    }
}

AfterAll { Clear-NoLiveVm }
