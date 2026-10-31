BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\explore\ExploreCommon.psm1') -Force

    function script:New-FakeRegKey {
        param([hashtable]$Values = @{}, [hashtable]$Kinds = @{}, [string[]]$SubKeys = @())
        $Key = [pscustomobject]@{ Values = $Values; Kinds = $Kinds; Subs = $SubKeys }
        $Key | Add-Member -MemberType ScriptMethod -Name GetValue -Value { param($Name, $Default, $Options) if ($this.Values.ContainsKey($Name)) { $this.Values[$Name] } else { $null } }
        $Key | Add-Member -MemberType ScriptMethod -Name GetValueKind -Value { param($Name) $this.Kinds[$Name] }
        $Key | Add-Member -MemberType ScriptMethod -Name GetValueNames -Value { [string[]]@($this.Values.Keys) }
        $Key | Add-Member -MemberType ScriptMethod -Name GetSubKeyNames -Value { [string[]]$this.Subs }
        $Key | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
        return $Key
    }
}

Describe 'Open-ExploreRegistryKey (jedyne miejsce otwierania klucza)' {
    It 'brak klucza = $null, bez wyjątku' {
        Open-ExploreRegistryKey -Path 'HKLM\SOFTWARE\DsoNieMaTakiegoKlucza-explore' | Should -BeNullOrEmpty
    }
    It 'inny błąd niż brak klucza jest rzucany' {
        Mock -ModuleName ExploreCommon Get-Item { throw [System.UnauthorizedAccessException]::new('brak dostępu') }
        { Open-ExploreRegistryKey -Path 'HKLM\SOFTWARE\Fake' } | Should -Throw '*brak dostępu*'
    }
    It 'odrzuca HKCU i inne prefiksy' {
        { Open-ExploreRegistryKey -Path 'HKCU\Software\X' } | Should -Throw '*dozwolone tylko*'
    }
    It 'Read-ExploreRegistryValue i Read-ExploreRegistryKey nie mają własnego Get-Item (korzystają z Open-ExploreRegistryKey)' {
        foreach ($Name in 'Read-ExploreRegistryValue', 'Read-ExploreRegistryKey') {
            (Get-Command -Module ExploreCommon -Name $Name).Definition | Should -Not -Match 'Get-Item'
        }
    }
}

Describe 'Read-ExploreRegistryTree' {
    It 'zrzuca klucz i wszystkie podklucze (rodzic przed dziećmi), z typami wartości' {
        Mock -ModuleName ExploreCommon Get-Item {
            switch -Regex ($LiteralPath) {
                'DeviceGuard$' { New-FakeRegKey -Values @{ EnableVirtualizationBasedSecurity = 1 } -Kinds @{ EnableVirtualizationBasedSecurity = 'DWord' } -SubKeys @('Scenarios') }
                'Scenarios$' { New-FakeRegKey -SubKeys @('HypervisorEnforcedCodeIntegrity') }
                'HypervisorEnforcedCodeIntegrity$' { New-FakeRegKey -Values @{ Enabled = 0; WasEnabledBy = 'x' } -Kinds @{ Enabled = 'DWord'; WasEnabledBy = 'String' } }
                default { throw [System.Management.Automation.ItemNotFoundException]::new('brak') }
            }
        }
        $Tree = @(Read-ExploreRegistryTree -Path 'HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard')
        @($Tree | ForEach-Object { $_.Path }) | Should -Be @(
            'HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard',
            'HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios',
            'HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity')
        $Leaf = $Tree[2]
        ($Leaf.Values | Where-Object Name -eq 'Enabled').Kind | Should -Be 'DWord'
        ($Leaf.Values | Where-Object Name -eq 'Enabled').Value | Should -Be 0
    }
    It 'Invoke-ExploreStep -RegistryTrees dokłada drzewo do RegistryKeys' {
        Mock -ModuleName ExploreCommon Read-ExploreRegistryTree { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() }; [pscustomobject]@{ Path = "$Path\Sub"; KeyExists = $true; Values = @(); SubKeys = @() } }
        $Step = Invoke-ExploreStep -Name 'x' -Command 'c' -RegistryTrees @('HKLM\SOFTWARE\Fake')
        @($Step.RegistryKeys).Count | Should -Be 2
    }
}

Describe 'Invoke-ExploreStep: odczyt rejestru przy błędzie' {
    It 'błąd odczytu jednej wartości ląduje w Error i nie przerywa pozostałych' {
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue {
            if ($Name -eq 'Zla') { throw 'odmowa' }
            [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = 1; Hex = $null }
        }
        $Step = Invoke-ExploreStep -Name 'x' -Command 'c' -Registry @(@{ Path = 'HKLM\SOFTWARE\F'; Name = 'Zla' }, @{ Path = 'HKLM\SOFTWARE\F'; Name = 'Dobra' })
        $Step.Error | Should -Match 'odmowa'
        @($Step.Registry).Count | Should -Be 1
    }
}

Describe 'Invoke-ExploreCollection' {
    It 'zwraca JSON: meta, pola dodatkowe, sondy; błąd sondy nie przerywa pozostałych' {
        Mock -ModuleName ExploreCommon Get-ExploreMeta { [ordered]@{ CurrentBuild = '1' } }
        $Json = Invoke-ExploreCollection -Probe ([ordered]@{ dobra = { [pscustomobject]@{ Name = 'a' } }; zla = { throw 'boom' } }) -Extra ([ordered]@{ userSid = 'S-1' })
        $Doc = $Json | ConvertFrom-Json
        @($Doc.PSObject.Properties.Name) | Should -Be @('meta', 'userSid', 'probes')
        $Doc.probes.dobra[0].Name | Should -Be 'a'
        $Doc.probes.zla[0].Name | Should -Be 'BLAD-SONDY'
        $Doc.probes.zla[0].Error | Should -Match 'boom'
    }
}

Describe 'Wysyłka funkcji do VM (część wspólna)' {
    It 'ConvertTo-ExploreRemoteScript daje poprawny skrypt z każdą funkcją' {
        $Names = Get-ExploreCommonRemoteFunctionNames
        $Text = ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function })
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        foreach ($Name in $Names) { $Text | Should -Match "function $Name \{" }
    }
    It 'funkcje wspólne wołają tylko siebie nawzajem i cmdlety systemowe (samowystarczalność)' {
        $Names = Get-ExploreCommonRemoteFunctionNames
        $Text = ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function })
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$null)
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) |
            ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-Explore*' } | Sort-Object -Unique
        foreach ($Name in $Called) { $Names | Should -Contain $Name }
        # nic z hosta (VmHost/VmCommon/Hyper-V) nie może trafić do VM
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Test-VmAuthFailure|Start-VM'
    }
    It 'każda funkcja z listy istnieje w module' {
        foreach ($Name in (Get-ExploreCommonRemoteFunctionNames)) { Get-Command -Module ExploreCommon -Name $Name | Should -Not -BeNullOrEmpty }
    }
}

Describe 'Resolve-ExploreOutDirectory' {
    It 'zwraca bezwzględną ścieżkę liczoną od $PWD i odrzuca dostawców spoza FileSystem bez tworzenia czegokolwiek' {
        $Before = Get-Location
        try {
            Set-Location -LiteralPath $TestDrive
            Resolve-ExploreOutDirectory -Path 'wyniki' | Should -Be ([IO.Path]::GetFullPath((Join-Path $TestDrive 'wyniki')))
            { Resolve-ExploreOutDirectory -Path 'Variable:\X' } | Should -Throw '*FileSystem*'
            { Resolve-ExploreOutDirectory -Path 'Env:\X' } | Should -Throw '*FileSystem*'
        }
        finally { Set-Location -LiteralPath $Before.Path }
        Test-Path -LiteralPath (Join-Path $TestDrive 'wyniki') | Should -BeFalse
    }
}

Describe 'Save-ExploreResult' {
    It 'zapisuje UTF-8 do <Out>\<Vm>\<plik> i zachowuje Exists=false oraz 0' {
        $Result = [ordered]@{ vmName = 'DSO-26H2-Home'; probes = @([ordered]@{ Name = 'a'; Registry = @([ordered]@{ Key = 'k'; Exists = $false; Kind = $null; Value = $null }, [ordered]@{ Key = 'z'; Exists = $true; Kind = 'DWord'; Value = 0 }) }); note = 'zażółć' }
        $Path = Save-ExploreResult -Result $Result -OutDirectory $TestDrive -VmName 'DSO-26H2-Home' -FileName 'c2-exploration.json'
        $Path | Should -Be (Join-Path (Join-Path $TestDrive 'DSO-26H2-Home') 'c2-exploration.json')
        $Json = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        $Json.note | Should -Be 'zażółć'
        $Json.probes[0].Registry[0].Exists | Should -BeFalse
        $Json.probes[0].Registry[1].Exists | Should -BeTrue
        $Json.probes[0].Registry[1].Value | Should -Be 0
    }
    It 'względny OutDirectory jest liczony od bieżącej lokalizacji PowerShell, nie od katalogu procesu .NET' {
        $Before = Get-Location
        try {
            Set-Location -LiteralPath $TestDrive
            $Path = Save-ExploreResult -Result @{ a = 1 } -OutDirectory 'rel-out' -VmName 'DSO-26H2-Pro' -FileName 'rel.json'
        }
        finally { Set-Location -LiteralPath $Before.Path }
        $Expected = [IO.Path]::GetFullPath((Join-Path $TestDrive 'rel-out\DSO-26H2-Pro\rel.json'))
        [IO.Path]::IsPathRooted($Path) | Should -BeTrue
        $Path | Should -Be $Expected
        Test-Path -LiteralPath $Expected | Should -BeTrue
    }
    It 'OutDirectory spoza dostawcy FileSystem (np. Variable:) jest odrzucony przed utworzeniem czegokolwiek' {
        $Before = Get-Location
        $Work = Join-Path $TestDrive 'm4-pusty'
        $null = New-Item -ItemType Directory -Path $Work -Force
        try {
            Set-Location -LiteralPath $Work
            { Save-ExploreResult -Result @{ a = 1 } -OutDirectory 'Variable:\DsoNieFilesystem' -VmName 'DSO-26H2-Pro' -FileName 'x.json' } | Should -Throw '*FileSystem*'
        }
        finally { Set-Location -LiteralPath $Before.Path }
        @(Get-ChildItem -LiteralPath $Work -Force).Count | Should -Be 0
    }
    It 'odrzuca obcą VM i nazwę pliku ze ścieżką' {
        { Save-ExploreResult -Result @{} -OutDirectory $TestDrive -VmName 'ObcaVM' -FileName 'x.json' } | Should -Throw
        { Save-ExploreResult -Result @{} -OutDirectory $TestDrive -VmName 'DSO-26H2-Pro' -FileName '..\x.json' } | Should -Throw
    }
}

Describe 'Assert-ExploreOutDirectory (wspólna reguła katalogu wyników)' {
    It 'wymagany, bezwzględny (dysk albo UNC), poza repo; w repo tylko ignorowane przez git; przykład w komunikacie' {
        $Repo = 'C:\Repo\DSOptimizer'
        { Assert-ExploreOutDirectory -Path '' -RepoRoot $Repo -Example '...\dso-explore\x' } | Should -Throw '*wymagany*dso-explore\x*'
        { Assert-ExploreOutDirectory -Path 'wyniki' -RepoRoot $Repo } | Should -Throw '*bezwzględn*'
        { Assert-ExploreOutDirectory -Path 'C:\Repo\DSOptimizer\tests\vm\out\x' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-ExploreOutDirectory -Path 'C:\Repo\DSOptimizer' -RepoRoot $Repo } | Should -Throw '*w repo*'
        { Assert-ExploreOutDirectory -Path 'C:\Repo\DSOptimizer\wyniki\pomiar\pro' -RepoRoot $Repo } | Should -Throw '*w repo*'
        $Real = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).ProviderPath   # prawdziwe repo: git check-ignore
        Assert-ExploreOutDirectory -Path "$Real\tests\vm\out\x" -RepoRoot $Real | Should -Be "$Real\tests\vm\out\x"   # ignorowany przez git
        { Assert-ExploreOutDirectory -Path "$Real\src\x" -RepoRoot $Real } | Should -Throw '*ignorowanym przez git*'   # nieignorowany
        Assert-ExploreOutDirectory -Path (Join-Path $env:TEMP 'dso-explore') -RepoRoot $Real | Should -Be (Join-Path $env:TEMP 'dso-explore')   # poza repo
        Assert-ExploreOutDirectory -Path 'D:\pomiary\e5' -RepoRoot $Repo | Should -Be 'D:\pomiary\e5'
        Assert-ExploreOutDirectory -Path '\\nas\udzial\e5' -RepoRoot $Repo | Should -Be '\\nas\udzial\e5'
    }
}

Describe 'Complete-ExploreVmCleanup (wspólny kontrakt stanu końcowego VM)' {
    BeforeEach {
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        $script:Order = [System.Collections.Generic.List[string]]::new()
        $script:Attached = [System.Collections.Generic.List[string]]::new()
        $script:Attached.Add('C:\VM\DSO-26H2-Pro.vhdx')
        $script:Attached.Add('C:\w\DSO-26H2-Pro\usb-SingleExe.vhdx')
        Mock -ModuleName ExploreCommon Restore-VmCleanState { $script:Order.Add("revert $VmName $CheckpointName") }
        Mock -ModuleName ExploreCommon Get-VMHardDiskDrive { @($script:Attached | ForEach-Object { [pscustomobject]@{ VMName = $VMName; Path = $_; ControllerType = 'SCSI'; ControllerNumber = 0; ControllerLocation = 1 } }) }
        Mock -ModuleName ExploreCommon Remove-VMHardDiskDrive -RemoveParameterType 'VMHardDiskDrive' { $script:Order.Add("detach $(@($VMHardDiskDrive)[0].Path)"); $null = $script:Attached.Remove(@($VMHardDiskDrive)[0].Path) }
        Mock -ModuleName ExploreCommon Disconnect-VMNetworkAdapter { $script:Order.Add("disconnect $VMName") }
        Mock -ModuleName ExploreCommon Get-VM { $script:Order.Add("state $Name"); [pscustomobject]@{ Name = $Name; State = 'Off' } }
        Mock -ModuleName ExploreCommon Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; SwitchName = $null } }
    }
    It 'revert -> odłączenie tylko wskazanego dysku -> odłączenie karty -> odczyt stanu; Ok przy VM Off, karcie bez przełącznika i bez dysku' {
        $R = Complete-ExploreVmCleanup -VmName 'DSO-26H2-Pro' -CheckpointName 'czysta-26H2' -DetachDiskPath @('C:\w\DSO-26H2-Pro\usb-SingleExe.vhdx')
        @($script:Order) | Should -Be @('revert DSO-26H2-Pro czysta-26H2', 'detach C:\w\DSO-26H2-Pro\usb-SingleExe.vhdx', 'disconnect DSO-26H2-Pro', 'state DSO-26H2-Pro')
        $R.Ok | Should -BeTrue
        @($R.AttachedDisks).Count | Should -Be 0
        @($script:Attached) | Should -Be @('C:\VM\DSO-26H2-Pro.vhdx')
    }
    It 'bez dysków do odłączenia: jak dotąd (revert, karta, stan), Remove-VMHardDiskDrive nie jest wołany' {
        $R = Complete-ExploreVmCleanup -VmName 'DSO-26H2-Home' -CheckpointName 'czysta-26H2'
        $R.Ok | Should -BeTrue
        Should -Invoke -ModuleName ExploreCommon Remove-VMHardDiskDrive -Times 0 -Exactly
        @($script:Order) | Should -Be @('revert DSO-26H2-Home czysta-26H2', 'disconnect DSO-26H2-Home', 'state DSO-26H2-Home')
    }
    It 'dysk, którego nie da się odłączyć, i VM nie Off z kartą na przełączniku = naruszenia kontraktu; błędy kroków nie przerywają kolejnych' {
        Mock -ModuleName ExploreCommon Restore-VmCleanState { throw 'checkpoint zablokowany' }
        Mock -ModuleName ExploreCommon Remove-VMHardDiskDrive -RemoveParameterType 'VMHardDiskDrive' { throw 'dysk w użyciu' }
        Mock -ModuleName ExploreCommon Get-VM { [pscustomobject]@{ Name = $Name; State = 'Running' } }
        Mock -ModuleName ExploreCommon Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; SwitchName = 'Default Switch' } }
        $R = Complete-ExploreVmCleanup -VmName 'DSO-26H2-Pro' -CheckpointName 'czysta-26H2' -DetachDiskPath @('C:\w\DSO-26H2-Pro\usb-SingleExe.vhdx')
        $R.Ok | Should -BeFalse
        $R.RevertError | Should -Match 'zablokowany'
        $R.DetachError | Should -Match 'w użyciu'
        @($R.AttachedDisks) | Should -Be @('C:\w\DSO-26H2-Pro\usb-SingleExe.vhdx')
        ($R.Violations -join ' | ') | Should -Match 'Running'
        ($R.Violations -join ' | ') | Should -Match 'Default Switch'
        ($R.Violations -join ' | ') | Should -Match 'usb-SingleExe\.vhdx'
        Should -Invoke -ModuleName ExploreCommon Disconnect-VMNetworkAdapter -Times 1 -Exactly
    }
    It 'VmName tylko DSO-26H2-Home/Pro; obca VM odrzucona przed jakimkolwiek krokiem' {
        { Complete-ExploreVmCleanup -VmName 'ObcaVM' -CheckpointName 'czysta-26H2' } | Should -Throw
        $Attr = (Get-Command -Module ExploreCommon -Name 'Complete-ExploreVmCleanup').Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        @($script:Order).Count | Should -Be 0
    }
}

Describe 'Copy-ExploreRawFiles (surowe pliki z VM kawałkami Base64)' {
    It 'kopiuje każdy plik katalogu VM do katalogu hosta (zawartość bajt w bajt)' {
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        Mock -ModuleName ExploreCommon Assert-DsoVmLiveAllowed { }
        $script:Files = @{ 'a.json' = [System.Text.Encoding]::UTF8.GetBytes('{"zażółć":1}'); 'b.log' = [byte[]](0, 1, 2, 255) }
        Mock -ModuleName ExploreCommon Invoke-Command -RemoveParameterValidation 'Session' {
            if (@($ArgumentList).Count -eq 1) { return @($script:Files.Keys | Sort-Object | ForEach-Object { [pscustomobject]@{ Name = $_; Length = $script:Files[$_].Length } }) }
            return [Convert]::ToBase64String($script:Files[[string]$ArgumentList[1]])
        }
        $Destination = Join-Path $TestDrive 'raw'
        $Copied = @(Copy-ExploreRawFiles -Session $null -Destination $Destination -Source 'C:\dso\e5\raw')
        $Copied.Count | Should -Be 2
        [System.IO.File]::ReadAllBytes((Join-Path $Destination 'b.log')) | Should -Be ([byte[]](0, 1, 2, 255))
        [System.IO.File]::ReadAllText((Join-Path $Destination 'a.json'), [System.Text.Encoding]::UTF8) | Should -Be '{"zażółć":1}'
    }
}

Describe 'Invoke-ExploreNative: surowa linia argumentów' {
    It 'RawArguments trafia do procesu bez zmian (cudzysłowy i spacje), a Arguments jest wtedy ignorowane' {
        $Raw = Invoke-ExploreNative -FilePath 'cmd.exe' -RawArguments '/c echo "a  b" ^& c' -Arguments @('ignorowane')
        $Raw.ExitCode | Should -Be 0
        $Raw.Stdout.Trim() | Should -Be '"a  b" & c'
    }
}

Describe 'Orkiestracja (mocki; VM i Hyper-V nietknięte)' {
    BeforeEach {
        # Bezpieczniki: domyślnie wszystko, co dotyka Hyper-V / PowerShell Direct, rzuca (w obu zasięgach); wyłącznik mockujemy tylko tu,
        # bo orkiestracja woła go na mockach. Test nadpisuje tylko to, czego potrzebuje.
        Set-NoLiveVmDefaultMock -ModuleName VmHost
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        Mock -ModuleName ExploreCommon Assert-DsoVmLiveAllowed {}
        $script:Order = [System.Collections.Generic.List[string]]::new()
        $script:Calls = [System.Collections.Generic.List[object]]::new()
        Mock -ModuleName ExploreCommon Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock -ModuleName ExploreCommon Restore-VmCleanState { $script:Order.Add("restore:$VmName") }
        Mock -ModuleName ExploreCommon Start-VM { $script:Order.Add("start:$Name") }
        Mock -ModuleName ExploreCommon Wait-VmReady { $script:Order.Add("ready:$VmName"); [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession]) }
        Mock -ModuleName ExploreCommon Remove-PSSession { $script:Order.Add('removesession') }
        Mock -ModuleName ExploreCommon Stop-VM { throw 'HOST: Stop-VM niedozwolone w testach' }
        Mock -ModuleName ExploreCommon Invoke-Command {
            if ($ArgumentList) { $script:Order.Add('collect'); $script:Calls.Add($ArgumentList); '{"meta":{"CurrentBuild":"1"},"probes":{}}' }
            else { 'S-1-5-21-1' }
        }
    }
    It 'revert -> start -> ready -> zbieranie -> usunięcie sesji -> revert; JSON w <Out>\<Vm>\<plik>' {
        $Path = Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'c2-exploration.json' -RemoteScript 'function X {}' -EntryCommand 'Invoke-C2Collection' -ReadyTimeoutSeconds 60
        ($Order -join ',') | Should -Be 'restore:DSO-26H2-Pro,start:DSO-26H2-Pro,ready:DSO-26H2-Pro,collect,removesession,restore:DSO-26H2-Pro'
        $Path | Should -Be (Join-Path $TestDrive 'DSO-26H2-Pro\c2-exploration.json')
        $Doc = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        $Doc.vmName | Should -Be 'DSO-26H2-Pro'
        $Doc.checkpoint | Should -Be 'czysta-26H2'
        $Doc.meta.CurrentBuild | Should -Be '1'
    }
    It 'bez -PassUserSid nie pyta o SID i woła punkt wejścia bez -UserSid; z nim przekazuje SID' {
        Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'Invoke-C2Collection' | Out-Null
        $Calls[0][1] | Should -Be 'Invoke-C2Collection'
        $Calls[0][2] | Should -BeNullOrEmpty
        Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'b.json' -RemoteScript 'x' -EntryCommand 'Invoke-C1Collection' -PassUserSid | Out-Null
        $Calls[1][2] | Should -Be 'S-1-5-21-1'
    }
    It 'Mode Prepare: VM zostaje uruchomiona po sukcesie (bez końcowego revertu), po błędzie revert' {
        $Path = Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'p.json' -RemoteScript 'x' -EntryCommand 'E' -Mode Prepare
        ($Order -join ',') | Should -Be 'restore:DSO-26H2-Pro,start:DSO-26H2-Pro,ready:DSO-26H2-Pro,collect,removesession'
        $Path | Should -Be (Join-Path $TestDrive 'DSO-26H2-Pro\p.json')
        $Order.Clear()
        Mock -ModuleName ExploreCommon Invoke-Command { throw 'zerwana sesja' }
        { Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'p.json' -RemoteScript 'x' -EntryCommand 'E' -Mode Prepare } | Should -Throw '*zerwana sesja*'
        $Order[-1] | Should -Be 'restore:DSO-26H2-Pro'
    }
    It 'Mode Finish: bez startu i revertu na początku, zbieranie, zawsze revert (także po błędzie)' {
        $null = Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'f.json' -RemoteScript 'x' -EntryCommand 'E' -Mode Finish
        ($Order -join ',') | Should -Be 'ready:DSO-26H2-Pro,collect,removesession,restore:DSO-26H2-Pro'
        $Order.Clear()
        Mock -ModuleName ExploreCommon Invoke-Command { throw 'zerwana sesja' }
        { Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'f.json' -RemoteScript 'x' -EntryCommand 'E' -Mode Finish } | Should -Throw
        $Order[-1] | Should -Be 'restore:DSO-26H2-Pro'
    }
    It 'błąd zbierania: nadal revert w finally, wyjątek propagowany' {
        Mock -ModuleName ExploreCommon Invoke-Command { if ($ArgumentList) { throw 'zerwana sesja' } else { 'S-1' } }
        { Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'E' } | Should -Throw '*zerwana sesja*'
        $Order[-1] | Should -Be 'restore:DSO-26H2-Pro'
    }
    It 'błąd revertu po błędzie zbierania: oba komunikaty w wyjątku' {
        Mock -ModuleName ExploreCommon Invoke-Command { if ($ArgumentList) { throw 'zerwana sesja' } else { 'S-1' } }
        $script:RestoreCalls = 0
        Mock -ModuleName ExploreCommon Restore-VmCleanState { $script:RestoreCalls++; if ($script:RestoreCalls -ge 2) { throw 'revert padł' } }
        { Invoke-ExploreVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'E' } | Should -Throw '*zerwana sesja*revert padł*'
    }
    It 'wyczerpane próby logowania + nieudany revert: znacznik w komunikacie i flaga auth zachowana (Invoke-ExploreAll nadal staje)' {
        Mock -ModuleName ExploreCommon Wait-VmReady {
            $Failure = [System.InvalidOperationException]::new('New-PSSession nie powiodło się po 8 próbach')
            $Failure.Data['VmAuthFailure'] = $true
            throw $Failure
        }
        $script:RestoreCalls = 0
        Mock -ModuleName ExploreCommon Restore-VmCleanState { $script:RestoreCalls++; if ($script:RestoreCalls -ge 2) { throw 'revert padł' } }
        { Invoke-ExploreVmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'E' } | Should -Throw '*po wyczerpaniu prób logowania*revert padł*'
        $script:RestoreCalls = 0
        $Caught = $null
        try { Invoke-ExploreAll -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'E' }
        catch { $Caught = $_.Exception }
        $Caught | Should -Not -BeNullOrEmpty
        Test-VmAuthFailure -Exception $Caught | Should -BeTrue
        ($Order | Where-Object { $_ -like '*Pro*' }) | Should -BeNullOrEmpty
    }
    It 'typowany błąd uwierzytelnienia z VmHost zatrzymuje całość: druga VM nietknięta, revert pierwszej jest' {
        Mock -ModuleName ExploreCommon Wait-VmReady {
            $Failure = [System.InvalidOperationException]::new('inny tekst niż znacznik')
            $Failure.Data['VmAuthFailure'] = $true
            throw $Failure
        }
        { Invoke-ExploreAll -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'E' } | Should -Throw '*inny tekst*'
        ($Order | Where-Object { $_ -like '*Pro*' }) | Should -BeNullOrEmpty
        $Order[-1] | Should -Be 'restore:DSO-26H2-Home'
    }
    It 'znacznik z VmHost w komunikacie też zatrzymuje całość (wyjątek spłaszczony do tekstu)' {
        $script:Text = "VM x: $(Get-VmAuthFailureMarker), przerywam"
        Mock -ModuleName ExploreCommon Wait-VmReady { throw $script:Text }
        { Invoke-ExploreAll -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'E' } | Should -Throw
        ($Order | Where-Object { $_ -like '*Pro*' }) | Should -BeNullOrEmpty
    }
    It 'inny błąd jednej VM nie przerywa drugiej, błędy są zebrane' {
        $script:Fail = $true
        Mock -ModuleName ExploreCommon Invoke-Command {
            if ($ArgumentList) { if ($script:Fail) { $script:Fail = $false; throw 'padło raz' } else { $script:Order.Add('collect'); '{"meta":{},"probes":{}}' } }
            else { 'S-1' }
        }
        $Outcome = Invoke-ExploreAll -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'E'
        @($Outcome.Errors).Count | Should -Be 1
        @($Outcome.Paths).Count | Should -Be 1
        ($Order | Where-Object { $_ -eq 'start:DSO-26H2-Pro' }) | Should -Not -BeNullOrEmpty
    }
    It 'VmName przyjmuje tylko DSO-26H2-Home i DSO-26H2-Pro (obca VM odrzucona)' {
        foreach ($Command in 'Invoke-ExploreVmRun', 'Invoke-ExploreAll') {
            $Attr = (Get-Command -Module ExploreCommon -Name $Command).Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
            $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        }
        { Invoke-ExploreVmRun -VmName 'ObcaVM' -OutDirectory $TestDrive -FileName 'a.json' -RemoteScript 'x' -EntryCommand 'E' } | Should -Throw
        $Order | Should -BeNullOrEmpty
    }
}

AfterAll { Clear-NoLiveVm }
