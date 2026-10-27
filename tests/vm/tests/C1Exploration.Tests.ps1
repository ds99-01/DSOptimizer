BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')

    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmRun.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-C1Exploration.ps1')

    # Atrapa na hostach bez modułu Dism: Mock wymaga istniejącego polecenia. Bezpieczniki w testach i tak rzucają.
    if (-not (Get-Command Set-WindowsReservedStorageState -ErrorAction SilentlyContinue)) {
        function script:Set-WindowsReservedStorageState { param($State) }
    }

    function script:New-FakeRegKey {
        param([hashtable]$Values, [hashtable]$Kinds)
        $Key = [pscustomobject]@{ Values = $Values; Kinds = $Kinds }
        $Key | Add-Member -MemberType ScriptMethod -Name GetValue -Value { param($Name, $Default, $Options) if ($this.Values.ContainsKey($Name)) { $this.Values[$Name] } else { $null } }
        $Key | Add-Member -MemberType ScriptMethod -Name GetValueKind -Value { param($Name) $this.Kinds[$Name] }
        $Key | Add-Member -MemberType ScriptMethod -Name GetValueNames -Value { [string[]]@($this.Values.Keys) }
        $Key | Add-Member -MemberType ScriptMethod -Name GetSubKeyNames -Value { [string[]]@() }
        $Key | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
        return $Key
    }
}

Describe 'Read-ExploreRegistryValue' {
    It 'rozróżnia brak wartości od wartości 0' {
        Mock -ModuleName ExploreCommon Get-Item { New-FakeRegKey -Values @{ Zero = 0 } -Kinds @{ Zero = 'DWord' } }
        $Zero = Read-ExploreRegistryValue -Path 'HKLM\SOFTWARE\Fake' -Name 'Zero'
        $Missing = Read-ExploreRegistryValue -Path 'HKLM\SOFTWARE\Fake' -Name 'Missing'
        $Zero.Exists | Should -BeTrue
        $Zero.Kind | Should -Be 'DWord'
        $Zero.Value | Should -Be 0
        $Missing.Exists | Should -BeFalse
        $Missing.Kind | Should -BeNullOrEmpty
        $Missing.Value | Should -BeNullOrEmpty
    }
    It 'zapisuje DWORD z flagą 0x80000000 także szesnastkowo' {
        Mock -ModuleName ExploreCommon Get-Item { New-FakeRegKey -Values @{ V = [int]-2147483646 } -Kinds @{ V = 'DWord' } }
        $R = Read-ExploreRegistryValue -Path 'HKLM\SOFTWARE\Fake' -Name 'V'
        $R.Value | Should -Be -2147483646
        $R.Hex | Should -Be '0x80000002'
    }
    It 'zwraca Exists=false, gdy brak klucza, i nie rzuca' {
        $R = Read-ExploreRegistryValue -Path 'HKLM\SOFTWARE\DsoNieMaTakiegoKlucza-c1' -Name 'X'
        $R.Exists | Should -BeFalse
        $R.KeyExists | Should -BeFalse
    }
    It 'czyta prawdziwą wartość tylko-do-odczytu (CurrentBuild jako String)' {
        $R = Read-ExploreRegistryValue -Path 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name 'CurrentBuild'
        $R.Exists | Should -BeTrue
        $R.Kind | Should -Be 'String'
    }
    It 'odrzuca nieznany prefiks ścieżki' {
        { Read-ExploreRegistryValue -Path 'HKCU\Software\X' -Name 'Y' } | Should -Throw
    }
}

Describe 'Read-ExploreRegistryKey' {
    It 'zrzuca wszystkie wartości klucza z typem' {
        Mock -ModuleName ExploreCommon Get-Item { New-FakeRegKey -Values @{ A = 1; B = 'x' } -Kinds @{ A = 'DWord'; B = 'String' } }
        $R = Read-ExploreRegistryKey -Path 'HKLM\SOFTWARE\Fake'
        $R.KeyExists | Should -BeTrue
        @($R.Values).Count | Should -Be 2
        ($R.Values | Where-Object Name -eq 'B').Kind | Should -Be 'String'
    }
    It 'brak klucza = KeyExists false' {
        (Read-ExploreRegistryKey -Path 'HKLM\SOFTWARE\DsoNieMaTakiegoKlucza-c1').KeyExists | Should -BeFalse
    }
}

Describe 'Invoke-ExploreNative' {
    It 'przechwytuje stdout, stderr i kod wyjścia osobno' {
        $Out = Invoke-ExploreNative -FilePath 'cmd.exe' -Arguments @('/c', 'echo', 'witaj')
        $Out.ExitCode | Should -Be 0
        $Out.Stdout | Should -Match 'witaj'
        $Out.Stderr | Should -BeNullOrEmpty
        $Err = Invoke-ExploreNative -FilePath 'cmd.exe' -Arguments @('/c', 'dir', 'C:\dso-nie-ma-takiego-katalogu-c1')
        $Err.ExitCode | Should -Not -Be 0
        $Err.Stderr | Should -Not -BeNullOrEmpty
        (Invoke-ExploreNative -FilePath 'cmd.exe' -Arguments @('/c', 'exit', '3')).ExitCode | Should -Be 3
    }
    It 'czyta wyjście w stronie kodowej OEM i zapisuje ją w rekordzie' {
        $R = Invoke-ExploreNative -FilePath 'cmd.exe' -Arguments @('/c', 'echo', 'x')
        $R.OemCodePage | Should -Be ([Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage)
    }
    It 'brak programu = Error, bez wyjątku' {
        $R = Invoke-ExploreNative -FilePath 'dso-nie-ma-takiego-programu.exe' -Arguments @()
        $R.ExitCode | Should -BeNullOrEmpty
        $R.Error | Should -Not -BeNullOrEmpty
    }
}

Describe 'Get-C1HiberfilInfo' {
    It 'zwraca rekord z Exists (tylko odczyt), bez wyjątku także gdy pliku nie ma' {
        $I = Get-C1HiberfilInfo
        $I.Path | Should -Be 'C:\hiberfil.sys'
        $I.Exists | Should -BeOfType [bool]
    }
}

Describe 'Invoke-ExploreStep' {
    It 'buduje rekord: komenda, kod, stdout/stderr, rejestr, czas; nic nie trafia na konsolę' {
        Mock Read-ExploreRegistryValue { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = 0; Hex = '0x00000000' } }
        # ten sam odczyt widziany z wnętrza modułu (Invoke-ExploreStep woła go w zasięgu modułu)
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = 0; Hex = '0x00000000' } }
        $Output = Invoke-ExploreStep -Name 'krok' -Command 'tool /x' -Registry @(@{ Path = 'HKLM\SOFTWARE\Fake'; Name = 'N' }) -Action {
            Write-Host 'szum'
            [pscustomobject]@{ ExitCode = 5; Stdout = 'o'; Stderr = 'e'; Error = $null }
        } 6>&1
        $Step = @($Output | Where-Object { $_ -is [pscustomobject] -or $_ -is [System.Collections.IDictionary] })[-1]
        $Step.Name | Should -Be 'krok'
        $Step.Command | Should -Be 'tool /x'
        $Step.ExitCode | Should -Be 5
        $Step.Stdout | Should -Be 'o'
        $Step.Stderr | Should -Be 'e'
        @($Step.Registry)[0].Exists | Should -BeTrue
        @($Step.Registry)[0].Value | Should -Be 0
        $Step.StartedAt | Should -Not -BeNullOrEmpty
        $Step.DurationMs | Should -BeGreaterOrEqual 0
        ($Output | Where-Object { $_ -is [string] -or $_ -is [System.Management.Automation.InformationRecord] }) | Should -BeNullOrEmpty
    }
    It 'wyjątek akcji ląduje w polu Error, krok nie przerywa zbierania' {
        $Step = Invoke-ExploreStep -Name 'x' -Command 'c' -Action { throw 'boom' }
        $Step.Error | Should -Match 'boom'
    }
    It 'sekcja After trafia do Extra, jej błąd do Error' {
        $Step = Invoke-ExploreStep -Name 'x' -Command 'c' -After { @{ Rozmiar = 42 } }
        $Step.Extra.Rozmiar | Should -Be 42
        $Bad = Invoke-ExploreStep -Name 'x' -Command 'c' -After { throw 'zly odczyt' }
        $Bad.Error | Should -Match 'zly odczyt'
    }
}

Describe 'Sondy (mocki natywnych poleceń)' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-ExploreNative {
            $script:Calls.Add("$FilePath $($Arguments -join ' ')")
            [pscustomobject]@{ ExitCode = 0; Stdout = 'ok'; Stderr = ''; Error = $null }
        }
        Mock Set-WindowsReservedStorageState { throw 'HOST: Set-WindowsReservedStorageState niedozwolone w testach' }
        Mock Get-C1HiberfilInfo { [ordered]@{ Exists = $true; SizeBytes = 123 } }
        Mock Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        # ten sam odczyt widziany z wnętrza modułu (Invoke-ExploreStep woła go w zasięgu modułu)
        Mock -ModuleName ExploreCommon Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock Read-ExploreRegistryValue { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $false; Kind = $null; Value = $null; Hex = $null } }
        # ten sam odczyt widziany z wnętrza modułu (Invoke-ExploreStep woła go w zasięgu modułu)
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $false; Kind = $null; Value = $null; Hex = $null } }
    }
    It 'hibernacja: powercfg /a, /h off, /a, /h on, /a w tej kolejności, rejestr czytany po każdym kroku' {
        $Steps = @(Invoke-C1HibernationProbe)
        $Calls | Should -Be @('powercfg.exe /a', 'powercfg.exe /h off', 'powercfg.exe /a', 'powercfg.exe /h on', 'powercfg.exe /a')
        $Names = @(@($Steps[0].Registry) | ForEach-Object { $_.Key })
        ($Names -join '|') | Should -Match 'Control\\Power\\HibernateEnabled\b'
        ($Names -join '|') | Should -Match 'Control\\Power\\HibernateEnabledDefault'
        ($Names -join '|') | Should -Match 'Session Manager\\Power\\HiberbootEnabled'
        ($Names -join '|') | Should -Match 'Session Manager\\Power\\HibernateEnabled'
        foreach ($Step in $Steps) { @($Step.Registry).Count | Should -BeGreaterThan 3 }
        $Steps[1].Extra.Hiberfil.SizeBytes | Should -Be 123
    }
    It 'NTFS: przywraca pierwotną wartość disablelastaccess (0x80000002 -> 2), nie zgaduje' {
        Mock Read-ExploreRegistryValue {
            if ($Name -eq 'NtfsDisableLastAccessUpdate') { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = [int]-2147483646; Hex = '0x80000002' } }
            elseif ($Name -eq 'NtfsDisable8dot3NameCreation') { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = 2; Hex = '0x00000002' } }
            else { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $false; Kind = $null; Value = $null; Hex = $null } }
        }
        # ten sam odczyt widziany z wnętrza modułu (Invoke-ExploreStep woła go w zasięgu modułu)
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue {
            if ($Name -eq 'NtfsDisableLastAccessUpdate') { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = [int]-2147483646; Hex = '0x80000002' } }
            elseif ($Name -eq 'NtfsDisable8dot3NameCreation') { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = 2; Hex = '0x00000002' } }
            else { [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $false; Kind = $null; Value = $null; Hex = $null } }
        }
        $Steps = @(Invoke-C1NtfsProbe)
        $Calls | Should -Contain 'fsutil.exe behavior set disablelastaccess 1'
        $Calls | Should -Contain 'fsutil.exe behavior set disablelastaccess 0'
        $Calls | Should -Contain 'fsutil.exe behavior set disablelastaccess 2'
        $Calls | Should -Contain 'fsutil.exe 8dot3name set 1'
        $Calls | Should -Contain 'fsutil.exe 8dot3name set 2'
        $Calls | Should -Contain 'fsutil.exe behavior query disablelastaccess'
        $Calls | Should -Contain 'fsutil.exe 8dot3name query C:'
        ($Steps | Where-Object Name -like '*przywroc*disablelastaccess*').Extra.RestoreArgument | Should -Be 2
    }
    It 'NTFS: gdy wartość nie istniała na starcie, nie wymyśla przywrócenia' {
        $Steps = @(Invoke-C1NtfsProbe)
        $Calls | Should -Not -Contain 'fsutil.exe behavior set disablelastaccess 2'
        $Skipped = $Steps | Where-Object { $_.Extra -and $_.Extra.RestoreSkipped }
        @($Skipped).Count | Should -Be 2
    }
    It 'reserved storage: brak cmdletów = Available false i żadnego Set' {
        Mock Get-Command { $null }
        $Steps = @(Invoke-C1ReservedStorageProbe)
        ($Steps | Where-Object Name -eq 'dostepnosc-cmdletow').Extra.Get.Available | Should -BeFalse
        @($Steps | Where-Object Name -like 'set-*').Count | Should -Be 0
        @($Steps | Where-Object { $_.RegistryKeys }).Count | Should -BeGreaterThan 0
    }
    It 'ux: bez załadowanego hive użytkownika zapisuje HiveLoaded=false i nie czyta HKU' {
        $Steps = @(Invoke-C1UxProbe -UserSid 'S-1-5-21-1' -HiveLoaded $false)
        $Steps[0].Extra.HiveLoaded | Should -BeFalse
        $Keys = @($Steps.Registry | ForEach-Object { $_.Key })
        ($Keys -join '|') | Should -Match 'AllowGameDVR'
        ($Keys -join '|') | Should -Not -Match 'HKU'
    }
    It 'ux: z hive czyta MenuShowDelay, GameDVR_Enabled, AppCaptureEnabled pod HKU\<SID>' {
        $Steps = @(Invoke-C1UxProbe -UserSid 'S-1-5-21-1' -HiveLoaded $true)
        $Keys = (@($Steps.Registry | ForEach-Object { $_.Key })) -join '|'
        $Keys | Should -Match 'HKU\\S-1-5-21-1\\Control Panel\\Desktop\\MenuShowDelay'
        $Keys | Should -Match 'HKU\\S-1-5-21-1\\System\\GameConfigStore\\GameDVR_Enabled'
        $Keys | Should -Match 'HKU\\S-1-5-21-1\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR\\AppCaptureEnabled'
    }
}

Describe 'Przywracanie stanu startowego (mocki)' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        $script:Rs = 'Enabled'
        $script:Reg0 = 1
        $script:Tick = 0
        Mock Invoke-ExploreNative {
            $script:Calls.Add("$FilePath $($Arguments -join ' ')")
            [pscustomobject]@{ ExitCode = 0; Stdout = 'ok'; Stderr = ''; Error = $null }
        }
        Mock Set-WindowsReservedStorageState { $script:Calls.Add("set $State"); $script:Rs = $State }
        Mock Get-C1HiberfilInfo { [ordered]@{ Exists = $true; SizeBytes = 123 } }
        Mock Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        # ten sam odczyt widziany z wnętrza modułu (Invoke-ExploreStep woła go w zasięgu modułu)
        Mock -ModuleName ExploreCommon Read-ExploreRegistryKey { [pscustomobject]@{ Path = $Path; KeyExists = $true; Values = @(); SubKeys = @() } }
        Mock Read-ExploreRegistryValue {
            $Value = if ($Name -eq 'HibernateEnabled' -and $Path -like '*Control\Power') { $script:Reg0 } else { 5 }
            [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = $Value; Hex = $null }
        }
        # ten sam odczyt widziany z wnętrza modułu (Invoke-ExploreStep woła go w zasięgu modułu)
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue {
            $Value = if ($Name -eq 'HibernateEnabled' -and $Path -like '*Control\Power') { $script:Reg0 } else { 5 }
            [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = $Value; Hex = $null }
        }
        Mock Get-C1ReservedStorageObject {
            [ordered]@{ Ok = $true; Error = $null; ObjectType = 'x'; IsEnum = $false; EnumNames = @(); Text = $script:Rs
                Properties = @([pscustomobject]@{ Name = 'State'; TypeName = 'E'; Value = $script:Rs; IsEnum = $true; EnumNames = @('Enabled', 'Disabled') }) }
        }
        Mock Get-Command { [pscustomobject]@{ CommandType = 'Cmdlet'; Source = 'Dism'; Version = '3.0'; Parameters = @{ State = 1 } } }
    }
    It 'hibernacja startowo włączona: bez dodatkowego /h off, MatchesBaseline = true' {
        $Steps = @(Invoke-C1HibernationProbe)
        $Calls | Should -Be @('powercfg.exe /a', 'powercfg.exe /h off', 'powercfg.exe /a', 'powercfg.exe /h on', 'powercfg.exe /a')
        $Steps[-1].Name | Should -Be 'porownanie-z-baseline'
        $Steps[-1].Extra.MatchesBaseline | Should -BeTrue
    }
    It 'hibernacja startowo wyłączona: po /h on dodatkowy krok /h off (przywrócenie)' {
        $script:Reg0 = 0
        $Steps = @(Invoke-C1HibernationProbe)
        $Calls | Should -Be @('powercfg.exe /a', 'powercfg.exe /h off', 'powercfg.exe /a', 'powercfg.exe /h on', 'powercfg.exe /a', 'powercfg.exe /h off')
        ($Steps | Where-Object Name -eq 'przywroc-powercfg-h-off') | Should -Not -BeNullOrEmpty
    }
    It 'hibernacja: różnica względem baseline -> MatchesBaseline=false i lista różnic' {
        Mock Read-ExploreRegistryValue {
            $script:Tick++
            [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = $script:Tick; Hex = $null }
        }
        # ten sam odczyt widziany z wnętrza modułu (Invoke-ExploreStep woła go w zasięgu modułu)
        Mock -ModuleName ExploreCommon Read-ExploreRegistryValue {
            $script:Tick++
            [pscustomobject]@{ Key = "$Path\$Name"; KeyExists = $true; Exists = $true; Kind = 'DWord'; Value = $script:Tick; Hex = $null }
        }
        $Steps = @(Invoke-C1HibernationProbe)
        $Steps[-1].Extra.MatchesBaseline | Should -BeFalse
        @($Steps[-1].Extra.Differences).Count | Should -BeGreaterThan 0
    }
    It 'hibernacja zrzuca całe klucze Control\Power i Session Manager\Power' {
        $Steps = @(Invoke-C1HibernationProbe)
        $Paths = @($Steps[0].RegistryKeys | ForEach-Object { $_.Path }) -join '|'
        $Paths | Should -Match 'Control\\Power'
        $Paths | Should -Match 'Session Manager\\Power'
    }
    It 'reserved storage startowo Enabled: Disabled -> Enabled, bez dodatkowego przywracania' {
        $Steps = @(Invoke-C1ReservedStorageProbe)
        $Calls | Where-Object { $_ -like 'set *' } | Should -Be @('set Disabled', 'set Enabled')
        $Restore = $Steps | Where-Object Name -eq 'przywroc-stan-startowy'
        $Restore.Extra.MatchesBaseline | Should -BeTrue
    }
    It 'reserved storage startowo Disabled: po Enabled przywraca -State Disabled z odczytu baseline' {
        $script:Rs = 'Disabled'
        $Steps = @(Invoke-C1ReservedStorageProbe)
        $Calls | Where-Object { $_ -like 'set *' } | Should -Be @('set Disabled', 'set Enabled', 'set Disabled')
        $Restore = $Steps | Where-Object Name -eq 'przywroc-stan-startowy'
        $Restore.Extra.RestoreValue | Should -Be 'Disabled'
        $Restore.Extra.MatchesBaseline | Should -BeTrue
    }
    It 'reserved storage: brak parametru State -> Error z listą parametrów, Set nigdy nie wywołany' {
        Mock Get-Command { [pscustomobject]@{ CommandType = 'Cmdlet'; Source = 'Dism'; Version = '3.0'; Parameters = @{ Inny = 1 } } }
        $Steps = @(Invoke-C1ReservedStorageProbe)
        ($Steps | Where-Object Name -eq 'set-Disabled').Error | Should -Match 'nie ma parametru State.*Inny'
        $Calls | Where-Object { $_ -like 'set *' } | Should -BeNullOrEmpty
    }
    It 'reserved storage: kroki DISM /Online /Get-ReservedStorageState przed i po zmianach' {
        $Steps = @(Invoke-C1ReservedStorageProbe)
        $Names = @($Steps | ForEach-Object { $_.Name })
        $Names.IndexOf('dism-get-przed') | Should -BeLessThan $Names.IndexOf('set-Disabled')
        $Names.IndexOf('dism-get-po') | Should -BeGreaterThan $Names.IndexOf('set-Enabled')
        ($Calls | Where-Object { $_ -eq 'DISM.exe /Online /Get-ReservedStorageState' }).Count | Should -Be 2
    }
}

Describe 'Wysyłka funkcji do VM' {
    It 'Get-C1RemoteScript jest poprawnym skryptem i definiuje wszystkie funkcje zdalne' {
        $Text = Get-C1RemoteScript
        $Errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        foreach ($Name in ((Get-ExploreCommonRemoteFunctionNames) + (Get-C1RemoteFunctionNames))) { $Text | Should -Match "function $Name \{" }
        (Get-C1RemoteFunctionNames) | Should -Contain 'Invoke-C1Collection'
    }
    It 'funkcje zdalne nie wołają niczego spoza listy wysyłanej (samowystarczalność)' {
        $Text = Get-C1RemoteScript
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$null)
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) |
            ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-C1*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        $Sent = (Get-ExploreCommonRemoteFunctionNames) + (Get-C1RemoteFunctionNames)
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
    }
}

Describe 'Invoke-C1VmExploration (orkiestracja na mockach)' {
    BeforeEach {
        # Bezpieczniki: domyślnie wszystko, co dotyka Hyper-V / PowerShell Direct, rzuca (w obu zasięgach); wyłącznik mockujemy tylko tu,
        # bo orkiestracja woła go na mockach. Test nadpisuje tylko to, czego potrzebuje.
        Set-NoLiveVmDefaultMock -ModuleName VmHost
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        Mock -ModuleName ExploreCommon Assert-DsoVmLiveAllowed {}
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName ExploreCommon Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock -ModuleName ExploreCommon Restore-VmCleanState { $script:Order.Add("restore:$VmName") }
        Mock -ModuleName ExploreCommon Start-VM { $script:Order.Add("start:$Name") }
        Mock -ModuleName ExploreCommon Wait-VmReady { $script:Order.Add("ready:$VmName"); [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession]) }
        Mock -ModuleName ExploreCommon Remove-PSSession { $script:Order.Add('removesession') }
        Mock -ModuleName ExploreCommon Invoke-Command {
            if ($ArgumentList) { $script:Order.Add('collect'); '{"meta":{"CurrentBuild":"1"},"probes":[]}' }
            else { 'S-1-5-21-1' }
        }
    }
    It 'revert -> start -> ready -> zbieranie -> revert, plik JSON zapisany' {
        $Paths = @(Invoke-C1VmExploration -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60)
        ($Order -join ',') | Should -Be 'restore:DSO-26H2-Pro,start:DSO-26H2-Pro,ready:DSO-26H2-Pro,collect,removesession,restore:DSO-26H2-Pro'
        Test-Path -LiteralPath (Join-Path $TestDrive 'DSO-26H2-Pro\c1-exploration.json') | Should -BeTrue
    }
    It 'błąd zbierania: nadal revert w finally, wyjątek propagowany' {
        Mock -ModuleName ExploreCommon Invoke-Command { if ($ArgumentList) { throw 'zerwana sesja' } else { 'S-1' } }
        { Invoke-C1VmExploration -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60 } | Should -Throw '*zerwana sesja*'
        $Order[-1] | Should -Be 'restore:DSO-26H2-Pro'
    }
    It 'błąd uwierzytelnienia zatrzymuje cały skrypt: druga VM nietknięta, revert pierwszej jest' {
        Mock -ModuleName ExploreCommon Wait-VmReady { throw "VM 'x': błąd uwierzytelnienia PowerShell Direct, przerywam bez ponawiania: coś" }
        { Invoke-C1Exploration -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60 } | Should -Throw '*uwierzytelnienia*'
        ($Order | Where-Object { $_ -like '*Pro*' }) | Should -BeNullOrEmpty
        $Order[-1] | Should -Be 'restore:DSO-26H2-Home'
    }
    It 'inny błąd jednej VM nie przerywa drugiej, błędy są zebrane' {
        $script:Fail = $true
        Mock -ModuleName ExploreCommon Invoke-Command {
            if ($ArgumentList) { if ($script:Fail) { $script:Fail = $false; throw 'padło raz' } else { $script:Order.Add('collect'); '{"meta":{},"probes":[]}' } }
            else { 'S-1' }
        }
        $Outcome = Invoke-C1Exploration -VmName @('DSO-26H2-Home', 'DSO-26H2-Pro') -OutDirectory $TestDrive -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60
        @($Outcome.Errors).Count | Should -Be 1
        @($Outcome.Paths).Count | Should -Be 1
        ($Order | Where-Object { $_ -eq 'start:DSO-26H2-Pro' }) | Should -Not -BeNullOrEmpty
    }
    It 'parametr VmName przyjmuje tylko DSO-26H2-Home i DSO-26H2-Pro' {
        $Attr = (Get-Command (Join-Path $PSScriptRoot '..\explore\Invoke-C1Exploration.ps1')).Parameters['VmName'].Attributes |
            Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
    }
}

AfterAll { Clear-NoLiveVm }
