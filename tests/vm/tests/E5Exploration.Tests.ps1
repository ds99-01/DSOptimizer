BeforeDiscovery {
    $script:HasHyperV = [bool](Get-Module -ListAvailable -Name Hyper-V)
}

BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-E5Exploration.ps1')
    $script:E5ScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-E5Exploration.ps1'

    function script:Get-CommandOwner {
        param($Node)
        $Parent = $Node.Parent
        while ($null -ne $Parent -and $Parent -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $Parent = $Parent.Parent }
        if ($null -ne $Parent) { $Parent.Name } else { '(poza funkcją)' }
    }

    function script:New-TestPlan {
        # Plan z katalogami w TestDrive (zapis sond, pobrań i surowych plików bez dotykania C:\dso i C:\ProgramData); z -Variant także
        # artefakt (jak w Invoke-E5VmRun).
        param([ValidateSet('', 'SingleExe', 'Folder')][string]$Variant = '')
        $Plan = Get-E5Plan
        $Plan.WorkDirectory = Join-Path $TestDrive 'e5'
        $Plan.DownloadsDirectory = Join-Path $TestDrive 'downloads'
        $Plan.RawDirectory = Join-Path $TestDrive 'e5raw'
        $Plan.FixturesDirectory = Join-Path $TestDrive 'fixtures'
        if ($Variant -eq 'SingleExe') { $Plan.Artifact = [ordered]@{ Variant = 'SingleExe'; Name = 'DSOptimizer-4.0.0.exe'; ExeRelativePath = 'DSOptimizer-4.0.0.exe' } }
        if ($Variant -eq 'Folder') { $Plan.Artifact = [ordered]@{ Variant = 'Folder'; Name = 'DSOptimizer-4.0.0.zip'; ExeRelativePath = 'DSOptimizer\DSOptimizer.exe' } }
        return ($Plan | ConvertTo-Json -Depth 6 | ConvertFrom-Json)
    }

    function script:New-FakeRelease {
        # Udawane wydanie z Build-Release.ps1 w TestDrive: DSOptimizer-<wersja>.exe albo .zip (katalog DSOptimizer\ z exe i DLL) i SHA256SUMS.txt.
        param(
            [Parameter(Mandatory)][ValidateSet('SingleExe', 'Folder')][string]$Variant,
            [string]$Version = '4.0.0',
            [switch]$BadSum,
            [switch]$NoSums,
            [switch]$NoExeInZip
        )
        $Dir = Join-Path $TestDrive ("wydania\v$Version-$Variant-" + [guid]::NewGuid().ToString('N').Substring(0, 6))
        $null = New-Item -ItemType Directory -Path $Dir -Force
        if ($Variant -eq 'SingleExe') {
            $Path = Join-Path $Dir "DSOptimizer-$Version.exe"
            [System.IO.File]::WriteAllBytes($Path, [byte[]](77, 90, 1, 2, 3))
        }
        else {
            $Source = Join-Path $Dir 'src\DSOptimizer'
            $null = New-Item -ItemType Directory -Path $Source -Force
            $ExeName = if ($NoExeInZip) { 'Inny.exe' } else { 'DSOptimizer.exe' }
            [System.IO.File]::WriteAllBytes((Join-Path $Source $ExeName), [byte[]](77, 90, 9))
            [System.IO.File]::WriteAllBytes((Join-Path $Source 'Wpf.Ui.dll'), [byte[]](1, 2))
            $Path = Join-Path $Dir "DSOptimizer-$Version.zip"
            Compress-Archive -Path $Source -DestinationPath $Path
            Remove-Item -LiteralPath (Join-Path $Dir 'src') -Recurse -Force
        }
        if (-not $NoSums) {
            $Sha = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($BadSum) { $Sha = '0' * 64 }
            [System.IO.File]::WriteAllText((Join-Path $Dir 'SHA256SUMS.txt'), "$Sha  $(Split-Path -Leaf $Path)`n")
        }
        return $Path
    }

    $script:UsbExe = 'E:\Narzędzia DSO\DSOptimizer-4.0.0.exe'

    function script:New-VmPlan {
        # Plan ze ścieżkami VM (C:\ProgramData\DSOptimizer\downloads) – tylko dla testów, w których wszystkie skutki są zamockowane.
        return (Get-E5Plan | ConvertTo-Json -Depth 6 | ConvertFrom-Json)
    }

    function script:New-ReleaseJson {
        param([string]$Digest, [string]$Tag = '26.10.07', [bool]$Prerelease = $false, [bool]$Draft = $false, [string[]]$AssetNames = @('winutil.ps1'))
        $Assets = @($AssetNames | ForEach-Object { [ordered]@{ name = $_; size = 927798; digest = $Digest; browser_download_url = "https://github.com/ChrisTitusTech/winutil/releases/download/$Tag/$_" } })
        return ([ordered]@{ tag_name = $Tag; prerelease = $Prerelease; draft = $Draft; assets = $Assets } | ConvertTo-Json -Depth 5)
    }

    $script:Guid = '0123456789abcdef0123456789abcdef'
    $script:VmFile = "C:\ProgramData\DSOptimizer\downloads\$script:Guid.ps1"
}

Describe 'Plan i fazy (plan Tasku 1 Etapu 5)' {
    It 'plan: wariant A = dzisiejsze polecenie katalogu, źródło WinUtil z GitHuba, folder pobrań programu, zdarzenia Defendera, 60 s' {
        $Plan = Get-E5Plan
        $Plan.WinUtilCommand | Should -BeExactly '-NoExit -Command "irm https://christitus.com/win | iex"'
        $Plan.WinUtilApiUrl | Should -BeExactly 'https://api.github.com/repos/ChrisTitusTech/winutil/releases/latest'
        $Plan.WinUtilAssetName | Should -BeExactly 'winutil.ps1'
        $Plan.DownloadsDirectory | Should -BeExactly 'C:\ProgramData\DSOptimizer\downloads'
        $Plan.DefenderEventLog | Should -BeExactly 'Microsoft-Windows-Windows Defender/Operational'
        @($Plan.DefenderEventIds) | Should -Be @(1006, 1007, 1015, 1116, 1117, 1118, 1119, 1121, 1122, 1126)
        $Plan.VariantWaitSeconds | Should -Be 60
        @($Plan.Variants) | Should -Be @('A', 'B', 'C')
        $Plan.UserName | Should -Be 'dso'
        $Plan.WorkDirectory | Should -Be 'C:\dso\e5'
    }
    It 'Get-E5PhaseOrder: defender zawsze pierwsza, gdy w przebiegu jest inna faza (K1)' {
        @(Get-E5PhaseOrder -Phase @('winutil')) | Should -Be @('defender', 'winutil')
        @(Get-E5PhaseOrder -Phase @('winutil', 'defender')) | Should -Be @('defender', 'winutil')
        @(Get-E5PhaseOrder -Phase @('defender')) | Should -Be @('defender')
        $Param = (Get-Command $script:E5ScriptPath).ScriptBlock.Ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Phase' }
        @($Param.Attributes | Where-Object { $_.TypeName.Name -eq 'ValidateSet' } | ForEach-Object { $_.PositionalArguments.Value }) | Should -Be @('defender', 'winutil', 'startup', 'av', 'motw', 'dpi')
    }
    It 'Get-E5VariantArguments: A = polecenie katalogu, B = RemoteSigned, C = Bypass, oba -File "<plik>" z folderu pobrań' {
        $Plan = New-VmPlan
        Get-E5VariantArguments -Plan $Plan -Variant 'A' | Should -BeExactly '-NoExit -Command "irm https://christitus.com/win | iex"'
        Get-E5VariantArguments -Plan $Plan -Variant 'B' -Path $script:VmFile | Should -BeExactly "-NoExit -NoProfile -ExecutionPolicy RemoteSigned -File `"$script:VmFile`""
        Get-E5VariantArguments -Plan $Plan -Variant 'C' -Path $script:VmFile | Should -BeExactly "-NoExit -NoProfile -ExecutionPolicy Bypass -File `"$script:VmFile`""
    }
    It 'Get-E5VariantArguments: plik spoza folderu pobrań, nazwa inna niż <guid>.ps1, cudzysłów albo brak pliku = wyjątek' {
        $Plan = New-VmPlan
        foreach ($Bad in 'C:\Temp\0123456789abcdef0123456789abcdef.ps1', 'C:\ProgramData\DSOptimizer\downloads\winutil.ps1', 'C:\ProgramData\DSOptimizer\downloads\0123456789abcdef0123456789abcdef.ps1" -x "', '', 'C:\ProgramData\DSOptimizer\downloads\sub\0123456789abcdef0123456789abcdef.ps1') {
            { Get-E5VariantArguments -Plan $Plan -Variant 'B' -Path $Bad } | Should -Throw -Because "'$Bad'"
        }
        { Get-E5VariantArguments -Plan $Plan -Variant 'C' } | Should -Throw
        { Get-E5VariantArguments -Plan $Plan -Variant 'D' } | Should -Throw
    }
}

Describe 'Wydanie WinUtil (czysta logika)' {
    It 'Select-E5WinUtilAsset: jedno winutil.ps1 z digest sha256 -> tag, adres i SHA256 małymi literami' {
        $Sha = ('AB' * 32)
        $R = Select-E5WinUtilAsset -Release (New-ReleaseJson -Digest "sha256:$Sha" | ConvertFrom-Json) -AssetName 'winutil.ps1'
        $R.Tag | Should -Be '26.10.07'
        $R.Name | Should -BeExactly 'winutil.ps1'
        $R.Sha256 | Should -BeExactly ('ab' * 32)
        $R.Url | Should -BeExactly 'https://github.com/ChrisTitusTech/winutil/releases/download/26.10.07/winutil.ps1'
        $R.Size | Should -Be 927798
    }
    It 'Select-E5WinUtilAsset: prerelease, draft, brak assetu, dwa assety, inna wielkość liter, brak albo zły digest = wyjątek' {
        $Good = "sha256:$('ab' * 32)"
        { Select-E5WinUtilAsset -Release (New-ReleaseJson -Digest $Good -Prerelease $true | ConvertFrom-Json) -AssetName 'winutil.ps1' } | Should -Throw '*prerelease*'
        { Select-E5WinUtilAsset -Release (New-ReleaseJson -Digest $Good -Draft $true | ConvertFrom-Json) -AssetName 'winutil.ps1' } | Should -Throw '*draft*'
        { Select-E5WinUtilAsset -Release (New-ReleaseJson -Digest $Good -AssetNames @('winutil.zip') | ConvertFrom-Json) -AssetName 'winutil.ps1' } | Should -Throw
        { Select-E5WinUtilAsset -Release (New-ReleaseJson -Digest $Good -AssetNames @('winutil.ps1', 'winutil.ps1') | ConvertFrom-Json) -AssetName 'winutil.ps1' } | Should -Throw
        { Select-E5WinUtilAsset -Release (New-ReleaseJson -Digest $Good -AssetNames @('WinUtil.ps1') | ConvertFrom-Json) -AssetName 'winutil.ps1' } | Should -Throw
        foreach ($Bad in '', 'sha1:abcd', "sha256:$('ab' * 31)", "md5:$('ab' * 32)") {
            { Select-E5WinUtilAsset -Release (New-ReleaseJson -Digest $Bad | ConvertFrom-Json) -AssetName 'winutil.ps1' } | Should -Throw -Because "'$Bad'"
        }
    }
}

Describe 'Bezpieczniki (VM-only, mock-only, Defender tylko do odczytu)' {
    It 'Assert-E5InsideVm rzuca w trybie mock-only, nawet z nazwą VM; poza trybem rzuca na hoście' {
        { Assert-E5InsideVm -ComputerName 'DSO-26H2-PRO' } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        $Saved = $env:DSO_VM_TESTS_MOCK_ONLY
        try {
            [Environment]::SetEnvironmentVariable('DSO_VM_TESTS_MOCK_ONLY', $null)
            { Assert-E5InsideVm -ComputerName 'HOST-PC' } | Should -Throw '*tylko w VM*'
            { Assert-E5InsideVm -ComputerName 'BRUTEFORCE_LTSC' } | Should -Throw '*tylko w VM*'
            { Assert-E5InsideVm -ComputerName 'DSO-26H2-HOME' } | Should -Not -Throw
        }
        finally { $env:DSO_VM_TESTS_MOCK_ONLY = $Saved }
    }
    It '<Name> rzuca przed jakimkolwiek skutkiem (aktualizacja sygnatur, sieć, zadanie Harmonogramu, zamykanie procesów, pliki)' -ForEach @(
        @{ Name = 'Update-E5Signatures'; Code = { Update-E5Signatures } }
        @{ Name = 'Invoke-E5DefenderPhase'; Code = { Invoke-E5DefenderPhase -Plan (New-TestPlan) } }
        @{ Name = 'Invoke-E5HttpText'; Code = { Invoke-E5HttpText -Url 'https://api.github.com/x' -TimeoutSeconds 5 -MaxBytes 1024 } }
        @{ Name = 'Save-E5HttpFile'; Code = { Save-E5HttpFile -Url 'https://github.com/x' -Path (Join-Path $TestDrive 'downloads\x.ps1') -TimeoutSeconds 5 -MaxBytes 1024 } }
        @{ Name = 'Save-E5WinUtilDownload'; Code = { Save-E5WinUtilDownload -Plan (New-TestPlan) } }
        @{ Name = 'Start-E5UserProcess'; Code = { Start-E5UserProcess -Plan (New-TestPlan) -TaskName 'DSO-E5-WinUtil-A' -Arguments '-NoExit' } }
        @{ Name = 'Invoke-E5UserProbe'; Code = { Invoke-E5UserProbe -Plan (New-TestPlan) -Name 'windows-A' -ScriptText 'x' -TimeoutSeconds 5 } }
        @{ Name = 'Start-E5Variant'; Code = { Start-E5Variant -Plan (New-TestPlan) -Variant 'A' -UserSid 'S-1-5-21-1' } }
        @{ Name = 'Stop-E5Variant'; Code = { Stop-E5Variant -Plan (New-TestPlan) -State ([pscustomobject]@{ Variant = 'A'; RootPid = 4242; TaskName = 'DSO-E5-WinUtil-A' }) } }
        @{ Name = 'Invoke-E5Collection'; Code = { Invoke-E5Collection -PlanJson '{}' -Phase 'defender' -UserSid 'S-1-5-21-1' } }
    ) {
        Mock Update-MpSignature { throw 'nie wolno' }
        Mock Register-ScheduledTask { throw 'nie wolno' }
        Mock Start-ScheduledTask { throw 'nie wolno' }
        Mock Unregister-ScheduledTask { throw 'nie wolno' }
        Mock Stop-Process { throw 'nie wolno' }
        { & $Code } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
        foreach ($Command in 'Update-MpSignature', 'Register-ScheduledTask', 'Start-ScheduledTask', 'Unregister-ScheduledTask', 'Stop-Process') { Should -Invoke $Command -Times 0 -Exactly }
        Test-Path -LiteralPath (Join-Path $TestDrive 'e5') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $TestDrive 'downloads') | Should -BeFalse
    }
    It 'każda funkcja wysyłana do VM ze skutkiem (wprost albo przez inną taką funkcję) zaczyna się od Assert-E5InsideVm' {
        $EffectCommands = @('Set-Content', 'New-Item', 'Remove-Item', 'Move-Item', 'Copy-Item', 'Register-ScheduledTask', 'Start-ScheduledTask', 'Unregister-ScheduledTask', 'Stop-Process', 'Update-MpSignature',
            'Set-Disk', 'Add-PartitionAccessPath', 'Restart-Computer', 'Invoke-ExploreNative')
        $EffectMembers = @('Kill', 'Start', 'Delete', 'WriteAllBytes', 'WriteAllText', 'Create', 'CreateDirectory', 'Open', 'GetResponse', 'Write', 'CopyTo', 'SetValue', 'CreateSubKey')
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E5ScriptPath), [ref]$null, [ref]$null)
        $Remote = @(Get-E5RemoteFunctionNames)
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
                if (-not $Effect[$Name] -and @($Calls[$Name] | Where-Object { $Effect[$_] -and $_ -ne 'Assert-E5InsideVm' }).Count -gt 0) { $Effect[$Name] = $true; $Changed = $true }
            }
        } while ($Changed)
        $Guarded = @($Functions.Keys | Where-Object { $Effect[$_] } | Sort-Object)
        foreach ($Name in 'Update-E5Signatures', 'Invoke-E5DefenderPhase', 'Invoke-E5HttpText', 'Save-E5HttpFile', 'Save-E5WinUtilDownload', 'Start-E5UserProcess', 'Invoke-E5UserProbe', 'Start-E5Variant', 'Stop-E5Variant', 'Invoke-E5Collection',
            'Register-E5Task', 'Unregister-E5Task', 'Stop-E5Processes', 'Invoke-E5Cli', 'Resolve-E5UsbVolume', 'Install-E5Fixture', 'Copy-E5DsoLogs', 'Start-E5Gui', 'Stop-E5Gui',
            'Invoke-E5WinUtilCliStep', 'Invoke-E5AvPhase', 'Invoke-E5StartupPhase', 'Start-E5Motw', 'Stop-E5Motw', 'Set-E5DpiScale', 'Restart-E5Vm', 'Start-E5DpiProbe', 'Stop-E5DpiProbe') {
            $Guarded | Should -Contain $Name
        }
        foreach ($Name in $Guarded) {
            $First = @($Functions[$Name].Body.EndBlock.Statements)[0]
            $First.Extent.Text | Should -Match '^Assert-E5InsideVm\b' -Because "$Name ma skutek, więc pierwsza instrukcja to bezpiecznik"
        }
    }
    It 'Defender tylko do odczytu: w treści skryptu nie ma Set-MpPreference, Add-MpPreference, Remove-MpPreference ani DisableRealtimeMonitoring' {
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E5ScriptPath), [System.Text.Encoding]::UTF8)
        foreach ($Forbidden in 'Set-MpPreference', 'Add-MpPreference', 'Remove-MpPreference', 'DisableRealtimeMonitoring') {
            $Text | Should -Not -Match ([regex]::Escape($Forbidden)) -Because "$Forbidden zmieniałby ustawienia Defendera (spec 5, K1)"
        }
    }
    It 'Defender: z poleceń *-Mp* tylko odczyt (Get-MpComputerStatus, Get-MpPreference, Get-MpThreat, Get-MpThreatDetection) i Update-MpSignature wyłącznie w Update-E5Signatures' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E5ScriptPath), [ref]$null, [ref]$null)
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Mp = @($Commands | Where-Object { [string]$_.GetCommandName() -match '-Mp[A-Z]' })
        foreach ($Command in $Mp) { @('Get-MpComputerStatus', 'Get-MpPreference', 'Get-MpThreat', 'Get-MpThreatDetection', 'Update-MpSignature') | Should -Contain $Command.GetCommandName() }
        @($Mp | Where-Object { $_.GetCommandName() -eq 'Update-MpSignature' }).Count | Should -Be 1
        foreach ($Command in ($Mp | Where-Object { $_.GetCommandName() -eq 'Update-MpSignature' })) { Get-CommandOwner $Command | Should -Be 'Update-E5Signatures' }
    }
    It 'pobieranie bez MotW (HttpWebRequest): brak Invoke-WebRequest, Invoke-RestMethod, Start-BitsTransfer i Invoke-Expression jako poleceń' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E5ScriptPath), [ref]$null, [ref]$null)
        $Names = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'Invoke-WebRequest', 'iwr', 'Invoke-RestMethod', 'irm', 'Start-BitsTransfer', 'Invoke-Expression', 'iex', 'curl', 'wget') { $Names | Should -Not -Contain $Forbidden }
        (Get-Command Invoke-E5HttpText).Definition | Should -Match 'HttpWebRequest'
        (Get-Command Save-E5HttpFile).Definition | Should -Match 'HttpWebRequest'
    }
    It '-VmName spoza ValidateSet odrzucony: skrypt, Invoke-E5VmRun i Restart-E5VmAndWait; VM nietknięta (sprzątanie: Complete-ExploreVmCleanup z ExploreCommon)' {
        Mock Restore-VmCleanState { throw 'nie wolno' }
        Mock Get-DsoVmCredential { throw 'nie wolno' }
        { & $script:E5ScriptPath -VmName 'ObcaVM' -ShowPlan -OutDirectory (Join-Path $TestDrive 'w') 6>$null } | Should -Throw
        { Invoke-E5VmRun -VmName 'DSO-26H2-Test' -OutDirectory $TestDrive -Phases @('defender') } | Should -Throw
        Should -Invoke Restore-VmCleanState -Times 0 -Exactly
        Should -Invoke Get-DsoVmCredential -Times 0 -Exactly
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        Get-Command -Name 'Complete-E5VmCleanup', 'Assert-E5OutDirectory' -CommandType Function -ErrorAction SilentlyContinue | Should -BeNullOrEmpty
        foreach ($Command in (Get-Command $script:E5ScriptPath), (Get-Command Invoke-E5VmRun), (Get-Command Restart-E5VmAndWait)) {
            $Attr = $Command.Parameters['VmName'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
            $Attr.ValidValues | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        }
    }
    It 'sonda okien: poza VM (i w trybie mock-only) odmawia przed UI Automation i zapisuje wynik z błędem' {
        $Dir = Join-Path $TestDrive 'probe'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Probe = Join-Path $Dir 'windows.ps1'
        [System.IO.File]::WriteAllText($Probe, (Get-E5WindowProbeScript), (New-Object System.Text.UTF8Encoding($true)))
        $ResultFile = Join-Path $Dir 'windows.json'
        & $Probe -ResultFile $ResultFile
        $Result = Get-Content -LiteralPath $ResultFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $Result.Error | Should -Match 'tylko w VM|DSO_VM_TESTS_MOCK_ONLY'
        @($Result.Windows).Count | Should -Be 0
    }
}

Describe 'Sondy w VM (mocki: nic nie jest uruchamiane naprawdę)' {
    BeforeEach {
        Mock Assert-E5InsideVm { }
        Mock Start-Sleep { }
        # Domyślne RZUCAJĄCE mocki skutków: test nadpisuje tylko to, czego potrzebuje.
        Mock Update-MpSignature { throw 'nie wolno: prawdziwa aktualizacja sygnatur w teście' }
        # -RemoveParameterType już tutaj: nadpisujące mocki w testach przekazują obiekty zastępcze zamiast CimInstance.
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { throw 'nie wolno: prawdziwe zadanie Harmonogramu w teście' }
        Mock Start-ScheduledTask { throw 'nie wolno: prawdziwe zadanie Harmonogramu w teście' }
        Mock Stop-Process { throw 'nie wolno: prawdziwe zamykanie procesu w teście' }
        Mock Invoke-E5HttpText { throw 'nie wolno: sieć w teście' }
        Mock Save-E5HttpFile { throw 'nie wolno: sieć w teście' }
        Mock Test-E5Network { [pscustomobject]@{ Name = $Name; Resolved = $true; Addresses = @('1.2.3.4'); Seconds = 0 } }
    }
    It 'Get-E5DefenderState: tylko odczyt Get-MpComputerStatus i Get-MpPreference, pola z planu (wersja i data sygnatur, ochrona, chmura)' {
        Mock Get-MpComputerStatus { [pscustomobject]@{ AMServiceEnabled = $true; RealTimeProtectionEnabled = $true; IsTamperProtected = $true; AntivirusEnabled = $true; AMProductVersion = '4.18.25090.3009'; AMEngineVersion = '1.1.25090.3001'; AntivirusSignatureVersion = '1.439.1.0'; AntivirusSignatureLastUpdated = [datetime]'2026-10-26T08:00:00'; NISSignatureVersion = '1.439.1.0' } }
        Mock Get-MpPreference { [pscustomobject]@{ MAPSReporting = 2; SubmitSamplesConsent = 1 } }
        $S = Get-E5DefenderState
        $S.AMServiceEnabled | Should -BeTrue
        $S.RealTimeProtectionEnabled | Should -BeTrue
        $S.IsTamperProtected | Should -BeTrue
        $S.AntivirusSignatureVersion | Should -Be '1.439.1.0'
        $S.AntivirusSignatureLastUpdated | Should -Match '^2026-10-26T08:00:00'
        $S.MAPSReporting | Should -Be 2
        $S.SubmitSamplesConsent | Should -Be 1
        @($S.Errors).Count | Should -Be 0
        Mock Get-MpComputerStatus { throw 'usługa niedostępna' }
        $S2 = Get-E5DefenderState
        ($S2.Errors -join ' ') | Should -Match 'Get-MpComputerStatus.*usługa niedostępna'
        $S2.MAPSReporting | Should -Be 2
    }
    It 'Invoke-E5DefenderPhase: nieudane Update-MpSignature -> SignaturesUpdated = false, błąd zapisany, detekcje i tak odczytane' {
        Mock Get-E5DefenderState { [pscustomobject]@{ AntivirusSignatureVersion = '1.400.0.0'; Errors = @() } }
        Mock Update-MpSignature { throw 'Nie można połączyć się z serwerem aktualizacji' }
        Mock Get-E5Detections { [pscustomobject]@{ Since = $Since.ToString('o'); Threats = @(); Events = @(); Count = 0; Errors = @() } }
        $R = Invoke-E5DefenderPhase -Plan (New-TestPlan)
        $R.SignaturesUpdated | Should -BeFalse
        $R.Update.Succeeded | Should -BeFalse
        $R.Update.Error | Should -Match 'serwerem aktualizacji'
        $R.Error | Should -Match 'Update-MpSignature'
        Should -Invoke Update-MpSignature -Times 1 -Exactly
        Should -Invoke Get-E5Detections -Times 1 -Exactly -ParameterFilter { $LogName -eq 'Microsoft-Windows-Windows Defender/Operational' -and @($EventIds).Count -eq 10 }
    }
    It 'Invoke-E5DefenderPhase: udane Update-MpSignature -> SignaturesUpdated = true, stan przed i po (zmiana wersji sygnatur)' {
        $script:Reads = 0
        Mock Get-E5DefenderState { $script:Reads++; [pscustomobject]@{ AntivirusSignatureVersion = $(if ($script:Reads -eq 1) { '1.400.0.0' } else { '1.439.1.0' }); Errors = @() } }
        Mock Update-MpSignature { }
        Mock Get-E5Detections { [pscustomobject]@{ Count = 0 } }
        $R = Invoke-E5DefenderPhase -Plan (New-TestPlan)
        $R.SignaturesUpdated | Should -BeTrue
        $R.Error | Should -BeNullOrEmpty
        $R.Update.Before.AntivirusSignatureVersion | Should -Be '1.400.0.0'
        $R.Update.After.AntivirusSignatureVersion | Should -Be '1.439.1.0'
        $R.Update.SignatureChanged | Should -BeTrue
        Should -Invoke Test-E5Network -Times 1 -Exactly -ParameterFilter { $Name -eq 'go.microsoft.com' }
    }
    It 'Invoke-E5DefenderPhase: brak sieci -> Update-MpSignature nie startuje, SignaturesUpdated = false' {
        Mock Test-E5Network { [pscustomobject]@{ Name = $Name; Resolved = $false; Addresses = @(); Seconds = 300 } }
        Mock Get-E5DefenderState { [pscustomobject]@{ Errors = @() } }
        Mock Get-E5Detections { [pscustomobject]@{ Count = 0 } }
        $R = Invoke-E5DefenderPhase -Plan (New-TestPlan)
        $R.SignaturesUpdated | Should -BeFalse
        $R.Error | Should -Match 'sieci'
        Should -Invoke Update-MpSignature -Times 0 -Exactly
    }
    It 'Get-E5Detections: wykrycia od znacznika czasu z nazwą zagrożenia, zdarzenia Operational o Id z planu od tego samego znacznika' {
        $Since = [datetime]'2026-10-26T10:00:00'
        Mock Get-MpThreat { @([pscustomobject]@{ ThreatID = 2147735505; ThreatName = 'Trojan:PowerShell/Test.A' }) }
        Mock Get-MpThreatDetection {
            @([pscustomobject]@{ ThreatID = 2147735505; InitialDetectionTime = [datetime]'2026-10-26T09:00:00'; ProcessName = 'C:\old.exe'; Resources = @('file:_C:\old.ps1'); ActionSuccess = $true; ThreatStatusID = 3; DetectionSourceTypeID = 3; DomainUser = 'VM\dso' }
                [pscustomobject]@{ ThreatID = 2147735505; InitialDetectionTime = [datetime]'2026-10-26T10:00:30'; ProcessName = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'; Resources = @('amsi:_powershell.exe'); ActionSuccess = $true; ThreatStatusID = 3; DetectionSourceTypeID = 10; DomainUser = 'VM\dso' })
        }
        Mock Get-WinEvent { $script:Filter = $FilterHashtable[0]; @([pscustomobject]@{ Id = 1116; TimeCreated = [datetime]'2026-10-26T10:00:31'; LevelDisplayName = 'Ostrzeżenie'; Message = 'Program ochrony przed złośliwym kodem wykrył złośliwe oprogramowanie.' }) }
        $R = Get-E5Detections -Since $Since -LogName 'Microsoft-Windows-Windows Defender/Operational' -EventIds @(1006, 1116)
        @($R.Threats).Count | Should -Be 1
        $R.Threats[0].ThreatName | Should -Be 'Trojan:PowerShell/Test.A'
        $R.Threats[0].Resources | Should -Be @('amsi:_powershell.exe')
        @($R.Events).Count | Should -Be 1
        $R.Events[0].Id | Should -Be 1116
        $R.ThreatCount | Should -Be 1
        $R.EventCount | Should -Be 1
        $R.Any | Should -BeTrue
        $R.Threats[0].Reused | Should -BeFalse
        $script:Filter.LogName | Should -Be 'Microsoft-Windows-Windows Defender/Operational'
        @($script:Filter.Id) | Should -Be @(1006, 1116)
        $script:Filter.StartTime | Should -Be $Since
        @($R.Errors).Count | Should -Be 0
    }
    It 'Get-E5Detections: brak zdarzeń (NoMatchingEventsFound) i brak wykryć = pusta lista bez błędu; inny błąd dziennika zapisany' {
        Mock Get-MpThreat { }
        Mock Get-MpThreatDetection { }
        Mock Get-WinEvent { throw [System.Management.Automation.ErrorRecord]::new([System.Exception]::new('Nie znaleziono zdarzeń.'), 'NoMatchingEventsFound,Microsoft.PowerShell.Commands.GetWinEventCommand', 'ObjectNotFound', $null) }
        $R = Get-E5Detections -Since (Get-Date) -LogName 'Microsoft-Windows-Windows Defender/Operational' -EventIds @(1116)
        $R.ThreatCount | Should -Be 0
        $R.EventCount | Should -Be 0
        $R.Any | Should -BeFalse
        @($R.Errors).Count | Should -Be 0
        Mock Get-WinEvent { throw 'Dziennik niedostępny' }
        $R2 = Get-E5Detections -Since (Get-Date) -LogName 'Microsoft-Windows-Windows Defender/Operational' -EventIds @(1116)
        ($R2.Errors -join ' ') | Should -Match 'Dziennik niedostępny'
    }
    It 'Save-E5WinUtilDownload: folder pobrań tworzony, plik <guid>.ps1, SHA256 zgodny z digest z API, bez Zone.Identifier, podpis odczytany' {
        $Plan = New-TestPlan
        $script:Content = [System.Text.Encoding]::UTF8.GetBytes("Write-Output 'winutil'`r`n")
        $Sha = ([System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash($script:Content)) -replace '-', '').ToLowerInvariant()
        $script:Release = New-ReleaseJson -Digest "sha256:$Sha"
        Mock Invoke-E5HttpText { $script:Release }
        Mock Save-E5HttpFile { [System.IO.File]::WriteAllBytes($Path, $script:Content); [pscustomobject]@{ Bytes = $script:Content.Length; FinalUrl = 'https://release-assets.githubusercontent.com/x' } }
        Mock Get-AuthenticodeSignature { [pscustomobject]@{ Status = 'NotSigned'; SignerCertificate = $null } }
        $R = Save-E5WinUtilDownload -Plan $Plan
        $R.Error | Should -BeNullOrEmpty
        $R.DirectoryCreated | Should -BeTrue
        Test-Path -LiteralPath $Plan.DownloadsDirectory -PathType Container | Should -BeTrue
        $R.Path | Should -Match '\\[0-9a-f]{32}\.ps1$'
        Split-Path -Parent $R.Path | Should -Be $Plan.DownloadsDirectory
        $R.Sha256 | Should -BeExactly $Sha
        $R.HashMatches | Should -BeTrue
        $R.ZoneIdentifier | Should -BeFalse
        $R.Signature | Should -Be 'NotSigned'
        $R.Release.Tag | Should -Be '26.10.07'
        Should -Invoke Invoke-E5HttpText -Times 1 -Exactly -ParameterFilter { $Url -eq 'https://api.github.com/repos/ChrisTitusTech/winutil/releases/latest' }
        Should -Invoke Save-E5HttpFile -Times 1 -Exactly -ParameterFilter { $Url -eq 'https://github.com/ChrisTitusTech/winutil/releases/download/26.10.07/winutil.ps1' -and $MaxBytes -eq $Plan.WinUtilMaxBytes }
    }
    It 'Save-E5WinUtilDownload: SHA256 pliku niezgodny z digest -> HashMatches = false; zła odpowiedź API -> błąd bez pobierania' {
        $Plan = New-TestPlan
        $script:Release = New-ReleaseJson -Digest "sha256:$('00' * 32)"
        Mock Invoke-E5HttpText { $script:Release }
        Mock Save-E5HttpFile { [System.IO.File]::WriteAllBytes($Path, [byte[]](1, 2, 3)); [pscustomobject]@{ Bytes = 3; FinalUrl = $Url } }
        Mock Get-AuthenticodeSignature { [pscustomobject]@{ Status = 'NotSigned'; SignerCertificate = $null } }
        $R = Save-E5WinUtilDownload -Plan $Plan
        $R.HashMatches | Should -BeFalse
        $R.ExpectedSha256 | Should -Be ('00' * 32)
        $script:Release = New-ReleaseJson -Digest "sha256:$('00' * 32)" -Prerelease $true
        $R2 = Save-E5WinUtilDownload -Plan $Plan
        $R2.Error | Should -Match 'prerelease'
        Should -Invoke Save-E5HttpFile -Times 1 -Exactly
    }
    It 'Save-E5WinUtilDownload: Defender blokuje zapis pliku (0x800700E1) albo plik znika po zapisie (kwarantanna) -> BlockedByDefender = true z etapem; błąd API -> false' {
        $Plan = New-TestPlan
        $script:Release = New-ReleaseJson -Digest "sha256:$('00' * 32)"
        Mock Invoke-E5HttpText { $script:Release }
        Mock Get-AuthenticodeSignature { [pscustomobject]@{ Status = 'NotSigned'; SignerCertificate = $null } }
        Mock Save-E5HttpFile { throw [System.IO.IOException]::new('Operacja nie została ukończona pomyślnie, ponieważ plik zawiera wirusa.', -2147024671) }
        $R = Save-E5WinUtilDownload -Plan $Plan
        $R.BlockedByDefender | Should -BeTrue
        $R.Stage | Should -Be 'download'
        $R.Error | Should -Match '0x800700E1'
        # Plik zapisany i od razu usunięty przez antywirusa: odczyt SHA256 nie znajduje pliku.
        Mock Save-E5HttpFile { [pscustomobject]@{ Bytes = 927798; FinalUrl = $Url } }
        $R2 = Save-E5WinUtilDownload -Plan $Plan
        $R2.BlockedByDefender | Should -BeTrue
        $R2.Stage | Should -Be 'hash'
        Mock Invoke-E5HttpText { throw [System.Net.WebException]::new('Nie można rozpoznać nazwy zdalnej: api.github.com') }
        $R3 = Save-E5WinUtilDownload -Plan $Plan
        $R3.BlockedByDefender | Should -BeFalse
        $R3.Stage | Should -Be 'api'
        $R3.Error | Should -Match 'api.github.com'
    }
    It 'Test-E5DefenderBlock: kody ERROR_VIRUS_INFECTED/ERROR_VIRUS_DELETED od etapu zapisu; ten sam kod na etapie API to nie blokada pliku' {
        $Virus = [System.IO.IOException]::new('wirus', -2147024671)
        $Deleted = [System.IO.IOException]::new('usunięty', -2147024670)
        Test-E5DefenderBlock -ErrorRecord $Virus -Stage 'download' -Path '' | Should -BeTrue
        Test-E5DefenderBlock -ErrorRecord $Deleted -Stage 'signature' -Path '' | Should -BeTrue
        Test-E5DefenderBlock -ErrorRecord $Virus -Stage 'api' -Path '' | Should -BeFalse
        Test-E5DefenderBlock -ErrorRecord ([System.IO.IOException]::new('dysk pełny', -2147024784)) -Stage 'download' -Path (Join-Path $TestDrive 'brak.ps1') | Should -BeFalse
    }
    It 'Start-E5UserProcess: zadanie jako dso (Interactive, Highest), powershell.exe z System32, argumenty dokładnie, widoczne okno, bez limitu czasu' {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        Mock New-ScheduledTaskAction { $script:Action = [pscustomobject]@{ Execute = $Execute; Argument = $Argument }; $script:Action }
        Mock New-ScheduledTaskPrincipal { $script:Principal = [pscustomobject]@{ UserId = $UserId; LogonType = [string]$LogonType; RunLevel = [string]$RunLevel }; $script:Principal }
        Mock New-ScheduledTaskSettingsSet { $script:Limit = $ExecutionTimeLimit; [pscustomobject]@{} }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { $script:Calls.Add("register $TaskName") }
        Mock Start-ScheduledTask { $script:Calls.Add("start $TaskName") }
        $Plan = New-VmPlan
        Start-E5UserProcess -Plan $Plan -TaskName 'DSO-E5-WinUtil-A' -Arguments $Plan.WinUtilCommand
        $script:Action.Execute | Should -Be (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe')
        $script:Action.Argument | Should -BeExactly '-NoExit -Command "irm https://christitus.com/win | iex"'
        $script:Principal.UserId | Should -Be 'dso'
        $script:Principal.LogonType | Should -Be 'Interactive'
        $script:Principal.RunLevel | Should -Be 'Highest'
        $script:Limit | Should -Be ([TimeSpan]::Zero)
        @($script:Calls) | Should -Be @('register DSO-E5-WinUtil-A', 'start DSO-E5-WinUtil-A')
        { Start-E5UserProcess -Plan $Plan -TaskName 'Inne zadanie' -Arguments '-NoExit' } | Should -Throw
    }
    It 'Invoke-E5UserProbe: skrypt w pliku (BOM), ukryte zadanie jako dso (Interactive, Highest), wynik JSON, zadanie wyrejestrowane' {
        $Plan = New-TestPlan
        Mock New-ScheduledTaskAction { $script:Action = [pscustomobject]@{ Execute = $Execute; Argument = $Argument }; $script:Action }
        Mock New-ScheduledTaskPrincipal { $script:Principal = [pscustomobject]@{ UserId = $UserId; LogonType = [string]$LogonType; RunLevel = [string]$RunLevel }; $script:Principal }
        Mock New-ScheduledTaskSettingsSet { [pscustomobject]@{} }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { }
        Mock Start-ScheduledTask { Set-Content -LiteralPath (Join-Path $Plan.WorkDirectory 'windows-A.json') -Value '{ "Sid": "S-1-5-21-1", "Windows": [ { "ProcessId": 1, "Name": "Winutil", "ClassName": "Window" } ], "Error": null }' -Encoding UTF8 }
        Mock Unregister-ScheduledTask { }
        $R = Invoke-E5UserProbe -Plan $Plan -Name 'windows-A' -ScriptText 'param($ResultFile)' -TimeoutSeconds 10
        $R.Sid | Should -Be 'S-1-5-21-1'
        $R.Windows[0].Name | Should -Be 'Winutil'
        $ScriptPath = Join-Path $Plan.WorkDirectory 'windows-A.ps1'
        ([System.IO.File]::ReadAllBytes($ScriptPath)[0..2] -join ',') | Should -Be '239,187,191'
        $script:Action.Argument | Should -Match ('-WindowStyle Hidden -File ' + [regex]::Escape($ScriptPath) + ' -ResultFile ')
        $script:Principal.UserId | Should -Be 'dso'
        $script:Principal.LogonType | Should -Be 'Interactive'
        $script:Principal.RunLevel | Should -Be 'Highest'
        Should -Invoke Unregister-ScheduledTask -Times 1 -Exactly -ParameterFilter { $TaskName -eq 'DSO-E5-windows-A' }
    }
    It 'Invoke-E5UserProbe: brak wyniku w limicie = wyjątek, zadanie i tak wyrejestrowane' {
        Mock New-ScheduledTaskAction { [pscustomobject]@{} }
        Mock New-ScheduledTaskPrincipal { [pscustomobject]@{} }
        Mock New-ScheduledTaskSettingsSet { [pscustomobject]@{} }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { }
        Mock Start-ScheduledTask { }
        Mock Unregister-ScheduledTask { }
        { Invoke-E5UserProbe -Plan (New-TestPlan) -Name 'windows-B' -ScriptText 'x' -TimeoutSeconds 1 } | Should -Throw '*windows-B*'
        Should -Invoke Unregister-ScheduledTask -Times 1 -Exactly
    }
    It 'Get-E5WindowProbeScript: poprawna składnia, UI Automation, wynik do pliku z parametru -ResultFile, bez zamykania procesów' {
        $Text = Get-E5WindowProbeScript
        $Errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Text | Should -Match 'System\.Windows\.Automation'
        $Text | Should -Match '\$ResultFile'
        $Text | Should -Not -Match 'Stop-Process|\.Kill\(|taskkill|Start-Process'
    }
    It 'Find-E5RootProcess: powershell.exe ze znacznikiem w linii poleceń, uruchomiony po starcie wariantu; starszy proces pominięty' {
        $Since = [datetime]'2026-10-26T10:00:00'
        Mock Get-CimInstance {
            @([pscustomobject]@{ ProcessId = 10; Name = 'powershell.exe'; CommandLine = 'powershell.exe -NoExit -Command "irm https://christitus.com/win | iex"'; CreationDate = [datetime]'2026-10-26T09:00:00' }
                [pscustomobject]@{ ProcessId = 20; Name = 'powershell.exe'; CommandLine = 'powershell.exe -NoExit -Command "irm https://christitus.com/win | iex"'; CreationDate = [datetime]'2026-10-26T10:00:02' }
                [pscustomobject]@{ ProcessId = 30; Name = 'powershell.exe'; CommandLine = 'powershell.exe -File C:\dso\e5\windows-A.ps1'; CreationDate = [datetime]'2026-10-26T10:00:03' })
        }
        Find-E5RootProcess -Marker 'christitus.com/win' -Since $Since -TimeoutSeconds 0 | Should -Be 20
        Find-E5RootProcess -Marker 'C:\ProgramData\DSOptimizer\downloads\x.ps1' -Since $Since -TimeoutSeconds 0 | Should -BeNullOrEmpty
    }
    It 'Get-E5ProcessTree: korzeń i potomkowie (BFS), proces o ponownie użytym PID (starszy od rodzica) pominięty; brak korzenia = pusta lista' {
        Mock Get-CimInstance {
            @([pscustomobject]@{ ProcessId = 20; ParentProcessId = 5; Name = 'powershell.exe'; CommandLine = 'a'; CreationDate = [datetime]'2026-10-26T10:00:02' }
                [pscustomobject]@{ ProcessId = 21; ParentProcessId = 20; Name = 'conhost.exe'; CommandLine = 'b'; CreationDate = [datetime]'2026-10-26T10:00:03' }
                [pscustomobject]@{ ProcessId = 22; ParentProcessId = 21; Name = 'powershell.exe'; CommandLine = 'c'; CreationDate = [datetime]'2026-10-26T10:00:04' }
                [pscustomobject]@{ ProcessId = 23; ParentProcessId = 20; Name = 'stary.exe'; CommandLine = 'd'; CreationDate = [datetime]'2026-10-26T08:00:00' })
        }
        @(Get-E5ProcessTree -RootPid 20 | ForEach-Object { $_.ProcessId }) | Should -Be @(20, 21, 22)
        @(Get-E5ProcessTree -RootPid 99).Count | Should -Be 0
    }
    Context 'Start-E5Variant (start wariantu jako dso, 60 s, okna)' {
        BeforeEach {
            Mock Start-E5UserProcess { $script:Started = [pscustomobject]@{ TaskName = $TaskName; Arguments = $Arguments } }
            Mock Find-E5RootProcess { $script:Marker = $Marker; 4242 }
            Mock Get-E5ProcessTree { @([pscustomobject]@{ ProcessId = 4242; ParentProcessId = 1000; Name = 'powershell.exe' }, [pscustomobject]@{ ProcessId = 4243; ParentProcessId = 4242; Name = 'conhost.exe' }) }
            Mock Get-ScheduledTask { [pscustomobject]@{ State = 'Running' } }
            Mock Get-ScheduledTaskInfo { [pscustomobject]@{ LastTaskResult = 267009 } }
            Mock Invoke-E5UserProbe {
                [pscustomobject]@{ Sid = 'S-1-5-21-1'; Error = $null; Windows = @(
                        [pscustomobject]@{ ProcessId = 4242; Name = "Chris Titus Tech's Windows Utility (Winutil)"; ClassName = 'HwndWrapper' }
                        [pscustomobject]@{ ProcessId = 777; Name = 'Windows PowerShell'; ClassName = 'CASCADIA_HOSTING_WINDOW_CLASS' }) }
            }
            Mock Save-E5WinUtilDownload { [pscustomobject]@{ Path = $script:VmFile; HashMatches = $true; Sha256 = ('ab' * 32); ExpectedSha256 = ('ab' * 32); Error = $null } }
        }
        It 'wariant A: dokładne polecenie katalogu, bez pobierania, start jako dso, proces żyje po 60 s, okno WinUtil w drzewie z dokładnym tytułem' {
            $R = Start-E5Variant -Plan (New-VmPlan) -Variant 'A' -UserSid 'S-1-5-21-1'
            $R.Error | Should -BeNullOrEmpty
            $R.Command | Should -BeExactly '-NoExit -Command "irm https://christitus.com/win | iex"'
            $script:Started.Arguments | Should -BeExactly $R.Command
            $script:Started.TaskName | Should -Be 'DSO-E5-WinUtil-A'
            $script:Marker | Should -Be 'christitus.com/win'
            $R.Started | Should -BeTrue
            $R.RootPid | Should -Be 4242
            $R.AliveAfterWait | Should -BeTrue
            $R.WinUtilVisible | Should -BeTrue
            $R.WinUtilWindowTitle | Should -BeExactly "Chris Titus Tech's Windows Utility (Winutil)"
            @($R.TreeWindows).Count | Should -Be 1
            @($R.Windows).Count | Should -Be 2
            $R.ProbeAsDso | Should -BeTrue
            $R.TaskState | Should -Be 'Running'
            $R.Since | Should -Not -BeNullOrEmpty
            Should -Invoke Save-E5WinUtilDownload -Times 0 -Exactly
            Should -Invoke Start-Sleep -ParameterFilter { $Seconds -ge 59 -and $Seconds -le 60 }
            Should -Invoke Test-E5Network -ParameterFilter { $Name -eq 'api.github.com' }
        }
        It 'wariant B: pobranie przed startem, -ExecutionPolicy RemoteSigned -File "<plik>", znacznik procesu = ścieżka pliku' {
            $R = Start-E5Variant -Plan (New-VmPlan) -Variant 'B' -UserSid 'S-1-5-21-1'
            $R.Command | Should -BeExactly "-NoExit -NoProfile -ExecutionPolicy RemoteSigned -File `"$script:VmFile`""
            $script:Marker | Should -Be $script:VmFile
            $R.Download.HashMatches | Should -BeTrue
            $R.Started | Should -BeTrue
            Should -Invoke Save-E5WinUtilDownload -Times 1 -Exactly
        }
        It 'wariant C: -ExecutionPolicy Bypass' {
            $R = Start-E5Variant -Plan (New-VmPlan) -Variant 'C' -UserSid 'S-1-5-21-1'
            $R.Command | Should -BeExactly "-NoExit -NoProfile -ExecutionPolicy Bypass -File `"$script:VmFile`""
        }
        It 'wariant B: SHA256 niezgodny z digest -> okno nie startuje, zapisane jako blokada (wynik pomiaru), nie błąd' {
            Mock Save-E5WinUtilDownload { [pscustomobject]@{ Path = $script:VmFile; HashMatches = $false; Sha256 = ('cd' * 32); ExpectedSha256 = ('ab' * 32); Error = $null } }
            $R = Start-E5Variant -Plan (New-VmPlan) -Variant 'B' -UserSid 'S-1-5-21-1'
            $R.Started | Should -BeFalse
            $R.Blocked | Should -Match 'SHA256'
            $R.Error | Should -BeNullOrEmpty
            Should -Invoke Start-E5UserProcess -Times 0 -Exactly
        }
        It 'proces nie żyje po 60 s -> AliveAfterWait = false, kod zakończenia zadania zapisany; okno spoza drzewa nie liczy się jako WinUtil' {
            Mock Get-E5ProcessTree { @() }
            Mock Get-ScheduledTask { [pscustomobject]@{ State = 'Ready' } }
            Mock Get-ScheduledTaskInfo { [pscustomobject]@{ LastTaskResult = 1 } }
            $R = Start-E5Variant -Plan (New-VmPlan) -Variant 'A' -UserSid 'S-1-5-21-1'
            $R.AliveAfterWait | Should -BeFalse
            $R.TaskLastResult | Should -Be '0x00000001'
            $R.WinUtilVisible | Should -BeFalse
            @($R.MatchingWindows).Count | Should -Be 1
        }
        It 'wariant B: Defender blokuje pobrany plik (symulacja) -> blokada zapisana jako wynik, okno i sonda nie startują, bez błędu' {
            Mock Save-E5WinUtilDownload { [pscustomobject]@{ Path = $script:VmFile; HashMatches = $false; Sha256 = $null; ExpectedSha256 = ('ab' * 32); Stage = 'download'; BlockedByDefender = $true; Error = '0x800700E1 plik zawiera wirusa' } }
            $R = Start-E5Variant -Plan (New-VmPlan) -Variant 'B' -UserSid 'S-1-5-21-1'
            $R.Started | Should -BeFalse
            $R.Blocked | Should -Match 'Defender zablokował'
            $R.Blocked | Should -Match '0x800700E1'
            $R.Error | Should -BeNullOrEmpty
            Should -Invoke Start-E5UserProcess -Times 0 -Exactly
            Should -Invoke Invoke-E5UserProbe -Times 0 -Exactly
        }
        It 'wariant B: pobranie padło z innego powodu (sieć, API) -> błąd (awaria), nie blokada' {
            Mock Save-E5WinUtilDownload { [pscustomobject]@{ Path = $null; HashMatches = $false; Stage = 'api'; BlockedByDefender = $false; Error = '0x80131509 Nie można rozpoznać nazwy zdalnej' } }
            $R = Start-E5Variant -Plan (New-VmPlan) -Variant 'B' -UserSid 'S-1-5-21-1'
            $R.Blocked | Should -BeNullOrEmpty
            $R.Error | Should -Match 'Pobranie.*etap api'
            Should -Invoke Start-E5UserProcess -Times 0 -Exactly
        }
        It 'sonda okien przekroczyła limit -> ProbeError zapisany, reszta pomiaru zostaje' {
            Mock Invoke-E5UserProbe { throw 'Sonda windows-A w sesji dso nie zapisała wyniku w 90 s' }
            $R = Start-E5Variant -Plan (New-VmPlan) -Variant 'A' -UserSid 'S-1-5-21-1'
            $R.ProbeError | Should -Match 'nie zapisała wyniku'
            $R.AliveAfterWait | Should -BeTrue
            $R.Error | Should -BeNullOrEmpty
        }
        It 'brak sieci -> nic nie startuje, błąd zapisany' {
            Mock Test-E5Network { [pscustomobject]@{ Name = $Name; Resolved = $false; Addresses = @(); Seconds = 300 } }
            $R = Start-E5Variant -Plan (New-VmPlan) -Variant 'A' -UserSid 'S-1-5-21-1'
            $R.Started | Should -BeFalse
            $R.Error | Should -Match 'sieci'
            Should -Invoke Start-E5UserProcess -Times 0 -Exactly
        }
    }
    Context 'Stop-E5Variant (detekcje i zamknięcie drzewa)' {
        It 'najpierw detekcje od startu wariantu, potem zamknięcie drzewa (dzieci przed rodzicem), na końcu wyrejestrowanie zadania' {
            $script:Order = [System.Collections.Generic.List[string]]::new()
            Mock Get-E5Detections { $script:Order.Add('detections'); $script:Since = $Since; [pscustomobject]@{ ThreatCount = 0; EventCount = 1; Any = $true; Threats = @(); Events = @([pscustomobject]@{ Id = 1116 }); Errors = @() } }
            Mock Get-E5ProcessTree { $script:Order.Add('tree'); @([pscustomobject]@{ ProcessId = 4242; ParentProcessId = 1 }, [pscustomobject]@{ ProcessId = 4243; ParentProcessId = 4242 }) }
            Mock Stop-Process { $script:Order.Add("stop $Id") }
            Mock Get-Process { $null }
            Mock Unregister-ScheduledTask { $script:Order.Add("unregister $TaskName") }
            $State = [pscustomobject]@{ Variant = 'A'; Since = '2026-10-26T10:00:00.0000000+02:00'; RootPid = 4242; TaskName = 'DSO-E5-WinUtil-A' }
            $R = Stop-E5Variant -Plan (New-VmPlan) -State $State
            @($script:Order) | Should -Be @('detections', 'tree', 'stop 4243', 'stop 4242', 'unregister DSO-E5-WinUtil-A')
            $script:Since | Should -Be ([datetimeoffset]::Parse('2026-10-26T10:00:00+02:00').LocalDateTime)
            $R.Detections.EventCount | Should -Be 1
            $R.Detections.Any | Should -BeTrue
            @($R.Closed) | Should -Be @(4243, 4242)
            @($R.StillRunning).Count | Should -Be 0
            $R.TaskUnregistered | Should -BeTrue
            $R.Error | Should -BeNullOrEmpty
        }
        It 'wariant, który nie wystartował (bez RootPid i zadania): tylko detekcje, nic nie jest zamykane ani wyrejestrowywane' {
            Mock Get-E5Detections { [pscustomobject]@{ ThreatCount = 0; EventCount = 0; Any = $false; Errors = @() } }
            Mock Get-E5ProcessTree { throw 'nie wolno' }
            Mock Unregister-ScheduledTask { throw 'nie wolno' }
            $R = Stop-E5Variant -Plan (New-VmPlan) -State ([pscustomobject]@{ Variant = 'B'; Since = (Get-Date).ToString('o') })
            $R.Detections.Any | Should -BeFalse
            Should -Invoke Stop-Process -Times 0 -Exactly
            Should -Invoke Unregister-ScheduledTask -Times 0 -Exactly
        }
        It 'zadanie o obcej nazwie nie jest wyrejestrowywane' {
            Mock Get-E5Detections { [pscustomobject]@{ Count = 0 } }
            Mock Unregister-ScheduledTask { }
            $R = Stop-E5Variant -Plan (New-VmPlan) -State ([pscustomobject]@{ Variant = 'A'; Since = (Get-Date).ToString('o'); TaskName = 'Microsoft\Windows\Defrag' })
            Should -Invoke Unregister-ScheduledTask -Times 0 -Exactly
            $R.Error | Should -Match 'zadani'
        }
    }
    It 'Invoke-E5Collection: defender, winutil-start z wariantem, winutil-stop ze stanem; nieznana faza = błąd w dokumencie' {
        Mock Invoke-E5DefenderPhase { [pscustomobject]@{ SignaturesUpdated = $true } }
        Mock Start-E5Variant { [pscustomobject]@{ Variant = $Variant; Sid = $UserSid } }
        Mock Stop-E5Variant { [pscustomobject]@{ RootPid = $State.RootPid } }
        $PlanJson = Get-E5Plan | ConvertTo-Json -Depth 6
        (Invoke-E5Collection -PlanJson $PlanJson -Phase 'defender' -UserSid 'S' | ConvertFrom-Json).data.SignaturesUpdated | Should -BeTrue
        $Start = Invoke-E5Collection -PlanJson $PlanJson -Phase 'winutil-start' -UserSid 'S-1' -ArgumentJson '{"Variant":"B"}' | ConvertFrom-Json
        $Start.data.Variant | Should -Be 'B'
        $Start.data.Sid | Should -Be 'S-1'
        (Invoke-E5Collection -PlanJson $PlanJson -Phase 'winutil-stop' -UserSid 'S' -ArgumentJson '{"Variant":"B","RootPid":5}' | ConvertFrom-Json).data.RootPid | Should -Be 5
        (Invoke-E5Collection -PlanJson $PlanJson -Phase 'x' -UserSid 'S' | ConvertFrom-Json).error | Should -Match 'Nieznana faza'
    }
}

Describe 'Wysyłka funkcji do VM i treść skryptu' {
    It 'Get-E5RemoteScript: poprawny, definiuje wszystkie funkcje zdalne; nie woła niczego spoza listy; funkcje hosta nie są wysyłane' {
        $Text = Get-E5RemoteScript
        $Errors = $null
        $Ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
        @($Errors).Count | Should -Be 0
        $Sent = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-E5RemoteFunctionNames)
        foreach ($Name in $Sent) { $Text | Should -Match "function $([regex]::Escape($Name)) \{" }
        $Called = $Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -like '*-E5*' -or $_ -like '*-Explore*' } | Sort-Object -Unique
        foreach ($Name in $Called) { $Sent | Should -Contain $Name }
        foreach ($HostOnly in (Get-E5HostOnlyFunctionNames)) { $Text | Should -Not -Match "function $([regex]::Escape($HostOnly)) \{" }
        $Text | Should -Not -Match 'Restore-VmCleanState|Wait-VmReady|Get-DsoVmCredential|Start-VM|Connect-VMNetworkAdapter|Save-DsoVmThumbnail|New-VHD|Mount-VHD|Initialize-Disk|Format-Volume|Add-VMHardDiskDrive|Complete-ExploreVmCleanup'
    }
    It 'każda funkcja skryptu jest wysyłana albo na liście hosta; polecenia ze skutkiem tylko w swoich funkcjach' {
        $Ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script:E5ScriptPath), [ref]$null, [ref]$null)
        $Defined = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
        foreach ($Name in $Defined) { ((Get-E5RemoteFunctionNames) + (Get-E5HostOnlyFunctionNames)) | Should -Contain $Name }
        $Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
        $Allowed = @{
            'Start-VM'                    = @('Invoke-E5VmRun')
            'Restore-VmCleanState'        = @('Invoke-E5VmRun')
            'Connect-VMNetworkAdapter'    = @('Invoke-E5VmRun')
            'Disconnect-VMNetworkAdapter' = @('Invoke-E5VmRun')
            'Add-VMHardDiskDrive'         = @('Invoke-E5VmRun')
            'Remove-VMHardDiskDrive'      = @()
            'Wait-VmReady'                = @('Invoke-E5VmRun', 'Restart-E5VmAndWait')
            'Wait-VmHeartbeatDrop'        = @('Restart-E5VmAndWait')
            'Save-DsoVmThumbnail'         = @('Invoke-E5WinUtilPhase', 'Save-E5Screenshot')
            'New-VHD'                     = @('New-E5UsbDisk')
            'Mount-VHD'                   = @('New-E5UsbDisk')
            'Dismount-VHD'                = @('New-E5UsbDisk')
            'Initialize-Disk'             = @('New-E5UsbDisk')
            'New-Partition'               = @('New-E5UsbDisk')
            'Format-Volume'               = @('New-E5UsbDisk')
            'Expand-Archive'              = @('Copy-E5ArtifactToUsb')
            'Set-Disk'                    = @('Resolve-E5UsbVolume')
            'Add-PartitionAccessPath'     = @('Resolve-E5UsbVolume')
            'Register-ScheduledTask'      = @('Start-E5UserProcess', 'Invoke-E5UserProbe', 'Register-E5Task')
            'Start-ScheduledTask'         = @('Start-E5UserProcess', 'Invoke-E5UserProbe', 'Register-E5Task')
            'Unregister-ScheduledTask'    = @('Invoke-E5UserProbe', 'Stop-E5Variant', 'Unregister-E5Task')
            'Stop-Process'                = @('Stop-E5Variant', 'Stop-E5Processes')
            'Restart-Computer'            = @('Restart-E5Vm')
            'Update-MpSignature'          = @('Update-E5Signatures')
        }
        foreach ($Name in $Allowed.Keys) {
            foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq $Name })) { $Allowed[$Name] | Should -Contain (Get-CommandOwner $Command) -Because $Name }
        }
        foreach ($Command in ($Commands | Where-Object { $_.GetCommandName() -eq 'Connect-VMNetworkAdapter' })) { $Command.Extent.Text | Should -Match "-SwitchName 'Default Switch'" }
        $Names = @($Commands | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ })
        foreach ($Forbidden in 'winget', 'winget.exe', 'Set-ItemProperty', 'New-ItemProperty', 'Remove-ItemProperty', 'reg', 'reg.exe', 'taskkill', 'taskkill.exe', 'Stop-ScheduledTask',
            'Stop-VM', 'Remove-VM', 'Restore-VMCheckpoint', 'Set-VMNetworkAdapter', 'cmd', 'cmd.exe', 'Start-Process', 'Set-Acl', 'Set-ExecutionPolicy', 'Clear-Disk', 'Remove-Partition', 'Set-Partition') {
            $Names | Should -Not -Contain $Forbidden
        }
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E5ScriptPath), [System.Text.Encoding]::UTF8)
        $Text | Should -Not -Match 'HKCU:|HKEY_CURRENT_USER|EncodedCommand|\.Kill\('
    }
    It 'plik ma BOM UTF-8 oraz sekcje Instrukcja uruchomienia i Opis działania' {
        $Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $script:E5ScriptPath))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
        $Text = [System.IO.File]::ReadAllText((Resolve-Path $script:E5ScriptPath), [System.Text.Encoding]::UTF8)
        $Text.Substring(0, 400) | Should -Match 'Instrukcja uruchomienia'
        $Text | Should -Match 'Opis działania'
    }
    It 'plik testów też ma BOM UTF-8' {
        $Bytes = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'E5Exploration.Tests.ps1'))
        ($Bytes[0..2] -join ',') | Should -Be '239,187,191'
    }
}

Describe 'Argumenty przebiegu i podsumowania (czysta logika)' {
    It 'Get-E5RunArguments: VM w kolejności, fazy per VM (defender pierwsza), katalog builda, plan' {
        $Arguments = Get-E5RunArguments -VmName @('DSO-26H2-Pro', 'DSO-26H2-Home') -Phase @('winutil') -OutDirectory 'C:\w' -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60 -BuildDirectory 'C:\b'
        @($Arguments.VmName) | Should -Be @('DSO-26H2-Pro', 'DSO-26H2-Home')
        @($Arguments.Phases['DSO-26H2-Pro']) | Should -Be @('defender', 'winutil')
        @($Arguments.Phases['DSO-26H2-Home']) | Should -Be @('defender', 'winutil')
        $Arguments.BuildDirectory | Should -Be 'C:\b'
        $Arguments.Plan.WinUtilAssetName | Should -Be 'winutil.ps1'
    }
    It 'Test-E5SignaturesUpdated: true tylko przy data.SignaturesUpdated = true i braku błędu fazy' {
        Test-E5SignaturesUpdated -Record ([ordered]@{ phase = 'defender'; error = $null; data = [pscustomobject]@{ SignaturesUpdated = $true } }) | Should -BeTrue
        Test-E5SignaturesUpdated -Record ([ordered]@{ phase = 'defender'; error = $null; data = [pscustomobject]@{ SignaturesUpdated = $false } }) | Should -BeFalse
        Test-E5SignaturesUpdated -Record ([ordered]@{ phase = 'defender'; error = 'sesja padła'; data = [pscustomobject]@{ SignaturesUpdated = $true } }) | Should -BeFalse
        Test-E5SignaturesUpdated -Record ([ordered]@{ phase = 'defender'; error = $null }) | Should -BeFalse
        Test-E5SignaturesUpdated -Record $null | Should -BeFalse
    }
    It 'Get-E5VariantSummary: start, okno, detekcje, zrzut i błędy jednego wariantu' {
        $Entry = [ordered]@{
            start = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Started = $true; AliveAfterWait = $true; WinUtilVisible = $true; WinUtilWindowTitle = 'Winutil'; ProbeSid = 'S-1'; ProbeAsDso = $true; ProbeError = $null; Error = $null } }
            screenshot = 'C:\w\DSO-26H2-Pro\winutil-A.png'; screenshotError = $null
            stop = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Detections = [pscustomobject]@{ ThreatCount = 1; EventCount = 2; Any = $true; Errors = @() }; Error = $null } }
        }
        $S = Get-E5VariantSummary -Variant 'A' -Entry $Entry
        $S.Variant | Should -Be 'A'
        $S.Valid | Should -BeTrue
        $S.Started | Should -BeTrue
        $S.AliveAfterWait | Should -BeTrue
        $S.WinUtilVisible | Should -BeTrue
        $S.WinUtilWindowTitle | Should -Be 'Winutil'
        $S.ThreatCount | Should -Be 1
        $S.EventCount | Should -Be 2
        $S.Detected | Should -BeTrue
        $S.Screenshot | Should -Be 'C:\w\DSO-26H2-Pro\winutil-A.png'
        @($S.Errors).Count | Should -Be 0
        $Broken = [ordered]@{ start = [pscustomobject]@{ error = 'zbieranie padło'; data = $null }; screenshot = $null; screenshotError = 'brak WMI'; stop = $null }
        $S2 = Get-E5VariantSummary -Variant 'B' -Entry $Broken
        $S2.Valid | Should -BeFalse
        $S2.Started | Should -BeFalse
        $S2.ThreatCount | Should -BeNullOrEmpty
        ($S2.Errors -join ' | ') | Should -Match 'zbieranie padło.*brak WMI'
    }
}

Describe 'Ważność wariantu (Get-E5VariantSummary: wynik pomiaru a awaria odczytu)' {
    BeforeAll {
        function script:New-Entry {
            param($StartData, $Detections = [pscustomobject]@{ ThreatCount = 0; EventCount = 0; Any = $false; Errors = @() }, [string]$ScreenshotError)
            return [ordered]@{
                start = [pscustomobject]@{ error = $null; data = $StartData }
                screenshot = 'C:\w\x.png'; screenshotError = $(if ($ScreenshotError) { $ScreenshotError } else { $null })
                stop = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Detections = $Detections; Error = $null } }
            }
        }
        function script:New-StartedData {
            param($ProbeAsDso = $true, $ProbeError = $null)
            return [pscustomobject]@{ Started = $true; Blocked = $null; AliveAfterWait = $true; WinUtilVisible = $false; WinUtilWindowTitle = $null; ProbeSid = 'S-1'; ProbeAsDso = $ProbeAsDso; ProbeError = $ProbeError; Error = $null }
        }
    }
    It 'detekcje odczytane z błędami (Get-MpThreatDetection i Get-WinEvent padły) -> Valid = false, a nie „0 detekcji”' {
        $S = Get-E5VariantSummary -Variant 'B' -Entry (New-Entry -StartData (New-StartedData) -Detections ([pscustomobject]@{ ThreatCount = 0; EventCount = 0; Any = $false; Errors = @('Get-MpThreatDetection: 0x80070005 Odmowa dostępu', 'Get-WinEvent: dziennik niedostępny') }))
        $S.Valid | Should -BeFalse
        $S.ThreatCount | Should -Be 0
        ($S.Errors -join ' | ') | Should -Match 'odczyt detekcji: Get-MpThreatDetection.*odczyt detekcji: Get-WinEvent'
    }
    It 'brak odczytu detekcji (stop bez Detections) -> Valid = false' {
        $S = Get-E5VariantSummary -Variant 'A' -Entry (New-Entry -StartData (New-StartedData) -Detections $null)
        $S.Valid | Should -BeFalse
        ($S.Errors -join ' ') | Should -Match 'brak odczytu detekcji'
    }
    It 'sonda okien nie jako dso (ProbeAsDso = false) albo z błędem -> Valid = false; „brak okna” nie udaje wyniku' {
        $S = Get-E5VariantSummary -Variant 'A' -Entry (New-Entry -StartData (New-StartedData -ProbeAsDso $false))
        $S.Valid | Should -BeFalse
        $S.WinUtilVisible | Should -BeFalse
        ($S.Errors -join ' ') | Should -Match 'nie działała jako dso'
        $S2 = Get-E5VariantSummary -Variant 'A' -Entry (New-Entry -StartData (New-StartedData -ProbeAsDso $null -ProbeError 'limit 90 s'))
        $S2.Valid | Should -BeFalse
        ($S2.Errors -join ' ') | Should -Match 'sonda okien: limit 90 s'
    }
    It 'okno bez WinUtil i 0 detekcji przy poprawnych odczytach -> Valid = true (prawdziwy wynik)' {
        $S = Get-E5VariantSummary -Variant 'B' -Entry (New-Entry -StartData (New-StartedData))
        $S.Valid | Should -BeTrue
        $S.WinUtilVisible | Should -BeFalse
        $S.ThreatCount | Should -Be 0
        $S.Detected | Should -BeFalse
        @($S.Errors).Count | Should -Be 0
    }
    It 'pobrany plik zablokowany przez Defendera (symulacja) -> Valid = true, blokada i detekcja zapisane, sonda nie wymagana' {
        $Blocked = [pscustomobject]@{ Started = $false; Blocked = 'Defender zablokował plik winutil.ps1 (etap download): 0x800700E1'; AliveAfterWait = $false; WinUtilVisible = $false; ProbeAsDso = $null; ProbeError = $null; Error = $null }
        $S = Get-E5VariantSummary -Variant 'B' -Entry (New-Entry -StartData $Blocked -Detections ([pscustomobject]@{ ThreatCount = 1; EventCount = 1; Any = $true; Errors = @() }))
        $S.Valid | Should -BeTrue
        $S.Started | Should -BeFalse
        $S.Blocked | Should -Match 'Defender zablokował'
        $S.ThreatCount | Should -Be 1
    }
    It 'okno nie wystartowało bez blokady i bez błędu (niespójne dane) -> Valid = false' {
        $S = Get-E5VariantSummary -Variant 'C' -Entry (New-Entry -StartData ([pscustomobject]@{ Started = $false; Blocked = $null; Error = $null }))
        $S.Valid | Should -BeFalse
    }
}

Describe 'Task 6: fazy, plan av i artefakt wydania (czysta logika, bez VM)' {
    It 'fazy: sieć dla defender/winutil/startup/av/motw (dpi bez sieci), artefakt dla startup/av/motw/dpi; każda inna faza dokłada defender na początek' {
        @(Get-E5NetworkPhases) | Should -Be @('defender', 'winutil', 'startup', 'av', 'motw')
        @(Get-E5ArtifactPhases) | Should -Be @('startup', 'av', 'motw', 'dpi')
        @(Get-E5PhaseOrder -Phase @('dpi')) | Should -Be @('defender', 'dpi')
        @(Get-E5PhaseOrder -Phase @('av')) | Should -Be @('defender', 'av')
        @(Get-E5PhaseOrder -Phase @('motw', 'startup')) | Should -Be @('defender', 'startup', 'motw')
        # Kolejność z planu (defender,av,motw,startup,dpi) -> startup przed av: pierwszy start = pierwsze uruchomienie po starcie VM.
        @(Get-E5PhaseOrder -Phase @('defender', 'av', 'motw', 'startup', 'dpi')) | Should -Be @('defender', 'startup', 'av', 'motw', 'dpi')
    }
    It 'plan: nośnik „Narzędzia DSO” (spacja i polski znak), exFAT, 3 starty, ZoneId=3, LogPixels 144; kroki av z planu dosłownie i w kolejności' {
        $Plan = Get-E5Plan
        $Plan.UsbFolderName | Should -BeExactly 'Narzędzia DSO'
        $Plan.UsbFileSystem | Should -Be 'exFAT'
        $Plan.StartupRuns | Should -Be 3
        $Plan.MotwZoneId | Should -Be 3
        $Plan.MotwWaitSeconds | Should -Be 5
        $Plan.DpiLogPixels | Should -Be 144
        $Plan.GuiWindowSeconds | Should -Be 60
        $Plan.GuiLanguage | Should -BeExactly 'pl'
        $Plan.MainWindowTitlePrefix | Should -BeExactly 'DSOptimizer'
        $Plan.Contains('MainWindowTitle') | Should -BeFalse
        @($Plan.SmartScreenTitles) | Should -Be @('System Windows ochronił ten komputer', 'Windows protected your PC')
        @($Plan.AvSteps | ForEach-Object { if ($_.Contains('Arguments')) { $_.Arguments } else { $_.Kind } }) | Should -Be @(
            '--apply privacy.advertising-id', '--undo privacy.advertising-id', '--apply privacy.diagtrack', '--undo privacy.diagtrack',
            '--apply repair.system-protection', '--execute repair.restore-point', '--undo repair.system-protection', '--execute panels.computer-management',
            '--execute repair.dns-flush', '--execute tools.restart-explorer', '--execute repair.wu-reset', '--execute repair.disk-cleanup', '--list-apps',
            'remove-appx', 'remove-win32', '--install-apps install.7zip,install.rustdesk', '--execute tools.winutil', 'gui')
        $Appx = @($Plan.AvSteps | Where-Object { $_.Kind -eq 'remove-appx' })[0]
        $Appx.RestorePoint | Should -Be 'skip'
        $Appx.ListFrom | Should -Be 'list-apps'
        $Win32 = @($Plan.AvSteps | Where-Object { $_.Kind -eq 'remove-win32' })[0]
        $Win32.AppId | Should -BeExactly 'win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestQuiet'
        $Win32.RestorePoint | Should -Be 'ifavailable'
        $Plan.FixtureKeyName | Should -BeExactly 'DsoTestQuiet'
    }
    It 'Resolve-E5Artifact: SingleExe -> plik wydania pod własną nazwą, wersja, SHA256 zgodny z SHA256SUMS.txt' {
        $Path = New-FakeRelease -Variant SingleExe
        $A = Resolve-E5Artifact -Variant 'SingleExe' -ArtifactPath $Path
        $A.Variant | Should -Be 'SingleExe'
        $A.Name | Should -BeExactly 'DSOptimizer-4.0.0.exe'
        $A.Version | Should -Be '4.0.0'
        $A.ExeRelativePath | Should -BeExactly 'DSOptimizer-4.0.0.exe'
        $A.Sha256 | Should -BeExactly (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        $A.Path | Should -Be $Path
    }
    It 'Resolve-E5Artifact: Folder -> zip z DSOptimizer\DSOptimizer.exe; zip bez exe = wyjątek' {
        $A = Resolve-E5Artifact -Variant 'Folder' -ArtifactPath (New-FakeRelease -Variant Folder)
        $A.ExeRelativePath | Should -BeExactly 'DSOptimizer\DSOptimizer.exe'
        $A.Name | Should -BeExactly 'DSOptimizer-4.0.0.zip'
        { Resolve-E5Artifact -Variant 'Folder' -ArtifactPath (New-FakeRelease -Variant Folder -NoExeInZip) } | Should -Throw '*DSOptimizer\DSOptimizer.exe*'
    }
    It 'Resolve-E5Artifact: oba puste = brak artefaktu; -Variant bez artefaktu, artefakt bez -Variant, brak pliku, zły wariant/nazwa, ścieżka względna, brak albo inna suma = wyjątek' {
        Resolve-E5Artifact -Variant '' -ArtifactPath '' | Should -BeNullOrEmpty
        $Exe = New-FakeRelease -Variant SingleExe
        { Resolve-E5Artifact -Variant 'SingleExe' -ArtifactPath '' } | Should -Throw '*-ArtifactPath*'
        { Resolve-E5Artifact -Variant '' -ArtifactPath $Exe } | Should -Throw '*-Variant*'
        { Resolve-E5Artifact -Variant 'SingleExe' -ArtifactPath (Join-Path $TestDrive 'brak\DSOptimizer-4.0.0.exe') } | Should -Throw '*Brak pliku*'
        { Resolve-E5Artifact -Variant 'Folder' -ArtifactPath $Exe } | Should -Throw '*nie jest wydaniem Folder*'
        { Resolve-E5Artifact -Variant 'Inny' -ArtifactPath $Exe } | Should -Throw
        { Resolve-E5Artifact -Variant 'SingleExe' -ArtifactPath 'wydania\DSOptimizer-4.0.0.exe' } | Should -Throw '*bezwzględn*'
        { Resolve-E5Artifact -Variant 'SingleExe' -ArtifactPath (New-FakeRelease -Variant SingleExe -NoSums) } | Should -Throw '*SHA256SUMS.txt*'
        { Resolve-E5Artifact -Variant 'SingleExe' -ArtifactPath (New-FakeRelease -Variant SingleExe -BadSum) } | Should -Throw '*różni się*'
        $Renamed = Join-Path (Split-Path -Parent $Exe) 'DSOptimizer.exe'
        Copy-Item -LiteralPath $Exe -Destination $Renamed
        { Resolve-E5Artifact -Variant 'SingleExe' -ArtifactPath $Renamed } | Should -Throw '*nie jest wydaniem SingleExe*'
    }
    It 'Get-E5RunArguments: faza z nośnikiem bez -Variant/-ArtifactPath = wyjątek; z artefaktem – opis artefaktu w argumentach przebiegu' {
        { Get-E5RunArguments -VmName @('DSO-26H2-Pro') -Phase @('av') -OutDirectory 'C:\w' -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60 } | Should -Throw '*-Variant*-ArtifactPath*'
        { Get-E5RunArguments -VmName @('DSO-26H2-Pro') -Phase @('dpi') -OutDirectory 'C:\w' -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60 -Variant 'SingleExe' } | Should -Throw '*-ArtifactPath*'
        $Arguments = Get-E5RunArguments -VmName @('DSO-26H2-Pro') -Phase @('defender', 'av', 'motw', 'startup', 'dpi') -OutDirectory 'C:\w' -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60 `
            -Variant 'Folder' -ArtifactPath (New-FakeRelease -Variant Folder)
        $Arguments.Artifact.ExeRelativePath | Should -Be 'DSOptimizer\DSOptimizer.exe'
        @($Arguments.Phases['DSO-26H2-Pro']) | Should -Be @('defender', 'startup', 'av', 'motw', 'dpi')
        (Get-E5RunArguments -VmName @('DSO-26H2-Pro') -Phase @('winutil') -OutDirectory 'C:\w' -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60).Artifact | Should -BeNullOrEmpty
    }
}

Describe 'Task 6: sondy w VM (mocki: nic nie jest uruchamiane naprawdę)' {
    BeforeEach {
        Set-NoLiveVmDefaultMock
        Mock Assert-E5InsideVm { }
        Mock Start-Sleep { }
        Mock Update-MpSignature { throw 'nie wolno: prawdziwa aktualizacja sygnatur w teście' }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { throw 'nie wolno: prawdziwe zadanie Harmonogramu w teście' }
        Mock Start-ScheduledTask { throw 'nie wolno: prawdziwe zadanie Harmonogramu w teście' }
        Mock Unregister-ScheduledTask { throw 'nie wolno: prawdziwe zadanie Harmonogramu w teście' }
        Mock Stop-Process { throw 'nie wolno: prawdziwe zamykanie procesu w teście' }
        Mock Set-Disk { throw 'nie wolno: prawdziwa zmiana dysku w teście' }
        Mock Add-PartitionAccessPath -RemoveParameterType 'InputObject' { throw 'nie wolno: prawdziwa litera dysku w teście' }
        Mock Restart-Computer { throw 'nie wolno: prawdziwy restart w teście' }
        Mock Invoke-ExploreNative { throw 'nie wolno: prawdziwy proces w teście' }
        Mock Invoke-E5HttpText { throw 'nie wolno: sieć w teście' }
        Mock Save-E5HttpFile { throw 'nie wolno: sieć w teście' }
        Mock Get-E5Detections { [pscustomobject]@{ Since = $Since.ToString('o'); Threats = @(); Events = @(); ThreatCount = 0; EventCount = 0; Any = $false; Errors = @() } }
    }
    It 'Get-E5TaskExecute: ścieżka ze spacją i polskim znakiem w cudzysłowie; cudzysłów w ścieżce, ścieżka względna albo pusta = wyjątek' {
        Get-E5TaskExecute -Path $script:UsbExe | Should -BeExactly '"E:\Narzędzia DSO\DSOptimizer-4.0.0.exe"'
        Get-E5TaskExecute -Path 'F:\Narzędzia DSO\DSOptimizer\DSOptimizer.exe' | Should -BeExactly '"F:\Narzędzia DSO\DSOptimizer\DSOptimizer.exe"'
        foreach ($Bad in 'Narzędzia DSO\DSOptimizer.exe', 'E:\Narzędzia DSO\a".exe', '', '\\serwer\DSOptimizer.exe') { { Get-E5TaskExecute -Path $Bad } | Should -Throw -Because "'$Bad'" }
    }
    It 'Invoke-E5Cli: akcja zadania = exe z „Narzędzia DSO” w cudzysłowie + argumenty dosłownie (nie Start-Process), dso Interactive Highest, kod z LastTaskResult, raport --out, zadanie wyrejestrowane' {
        $Plan = New-TestPlan -Variant SingleExe
        $script:OutFile = Join-Path $TestDrive 'av-01-apply.json'
        $script:Polls = 0
        Mock New-ScheduledTaskAction { $script:Action = [pscustomobject]@{ Execute = $Execute; Argument = $Argument }; $script:Action }
        Mock New-ScheduledTaskPrincipal { $script:Principal = [pscustomobject]@{ UserId = $UserId; LogonType = [string]$LogonType; RunLevel = [string]$RunLevel }; $script:Principal }
        Mock New-ScheduledTaskSettingsSet { [pscustomobject]@{} }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { $script:Registered = $TaskName }
        Mock Start-ScheduledTask { [System.IO.File]::WriteAllText($script:OutFile, '{"exitCode":1,"outcome":"Refused","result":"OperationFailed"}', (New-Object System.Text.UTF8Encoding($false))) }
        Mock Get-ScheduledTask { $script:Polls++; [pscustomobject]@{ State = $(if ($script:Polls -lt 2) { 'Running' } else { 'Ready' }) } }
        Mock Get-ScheduledTaskInfo { [pscustomobject]@{ LastTaskResult = $(if ($script:Polls -lt 2) { 267009 } else { 1 }) } }
        Mock Unregister-ScheduledTask { $script:Unregistered = $TaskName }
        Mock Start-Process { throw 'nie wolno: Start-Process nie cytuje ścieżek' }
        $R = Invoke-E5Cli -Plan $Plan -Exe $script:UsbExe -Name '01-apply' -Arguments "--apply privacy.advertising-id --out $script:OutFile" -OutFile $script:OutFile -TimeoutSeconds 60
        $script:Action.Execute | Should -BeExactly '"E:\Narzędzia DSO\DSOptimizer-4.0.0.exe"'
        $script:Action.Argument | Should -BeExactly "--apply privacy.advertising-id --out $script:OutFile"
        $script:Principal.UserId | Should -Be 'dso'
        $script:Principal.LogonType | Should -Be 'Interactive'
        $script:Principal.RunLevel | Should -Be 'Highest'
        $script:Registered | Should -Be 'DSO-E5-cli-01-apply'
        $R.Finished | Should -BeTrue
        $R.ExitCode | Should -Be 1
        $R.Hex | Should -Be '0x00000001'
        $R.Report.outcome | Should -Be 'Refused'
        $R.Error | Should -BeNullOrEmpty
        $script:Unregistered | Should -Be 'DSO-E5-cli-01-apply'
        Should -Invoke Start-Process -Times 0 -Exactly
    }
    It 'Invoke-E5Cli: limit czasu -> TimedOut, zadanie NIE wyrejestrowane (program pracuje dalej), brak raportu nie udaje wyniku' {
        Mock Register-E5Task { }
        Mock Wait-E5Task { [pscustomobject]@{ Finished = $false; TimedOut = $true; ExitCode = $null; Hex = $null; State = 'Running'; Seconds = 600 } }
        Mock Unregister-E5Task { 'nie wolno' }
        $R = Invoke-E5Cli -Plan (New-TestPlan -Variant SingleExe) -Exe $script:UsbExe -Name '12-disk-cleanup' -Arguments '--execute repair.disk-cleanup' -OutFile (Join-Path $TestDrive 'brak.json') -TimeoutSeconds 600
        $R.TimedOut | Should -BeTrue
        $R.ExitCode | Should -BeNullOrEmpty
        $R.Report | Should -BeNullOrEmpty
        Should -Invoke Unregister-E5Task -Times 0 -Exactly
    }
    It 'Wait-E5Task: 0x41303 (jeszcze nie działało) i 0x41301 (działa) to nie koniec; kod 0xFFFFFFFF -> -1' {
        $script:Polls = 0
        Mock Get-ScheduledTask { $script:Polls++; [pscustomobject]@{ State = 'Ready' } }
        Mock Get-ScheduledTaskInfo { [pscustomobject]@{ LastTaskResult = @(267011, 267009, 4294967295)[[math]::Min($script:Polls - 1, 2)] } }
        $W = Wait-E5Task -TaskName 'DSO-E5-cli-x' -TimeoutSeconds 60 -PollSeconds 1
        $W.Finished | Should -BeTrue
        $W.ExitCode | Should -Be -1
        $W.Hex | Should -Be '0xFFFFFFFF'
        $script:Polls | Should -Be 3
    }
    It 'Start-E5Gui: zadanie bez argumentów z exe w cudzysłowie (Highest); ścieżka exe trafia do sondy w żądaniu JSON, nie w linii zadania' {
        Mock Register-E5Task { $script:Task = [pscustomobject]@{ TaskName = $TaskName; Execute = $Execute; Argument = $Argument; RunLevel = $RunLevel } }
        Mock Invoke-E5UserProbe { $script:Request = $RequestJson | ConvertFrom-Json; $script:ProbeName = $Name; [pscustomobject]@{ Sid = 'S-1-5-21-1'; Found = $true; Seconds = 3.2; ProcessId = 77; Title = 'DSOptimizer 4.0.0'; ClassName = 'HwndWrapper[DSOptimizer;;x]'; Source = 'uia'; Dpi = 96; Error = $null } }
        $R = Start-E5Gui -Plan (New-TestPlan -Variant SingleExe) -UserSid 'S-1-5-21-1' -Exe $script:UsbExe -Name '18-gui' -WaitSeconds 60
        $script:Task.Execute | Should -BeExactly '"E:\Narzędzia DSO\DSOptimizer-4.0.0.exe"'
        $script:Task.Argument | Should -BeNullOrEmpty
        $script:Task.RunLevel | Should -Be 'Highest'
        $script:Task.TaskName | Should -Be 'DSO-E5-gui-18-gui'
        $script:Request.Exe | Should -BeExactly $script:UsbExe
        $script:Request.TitlePrefix | Should -BeExactly 'DSOptimizer'
        $null -eq $script:Request.PSObject.Properties['Title'] | Should -BeTrue
        $script:Request.TimeoutSeconds | Should -Be 60
        $script:Request.Language | Should -BeExactly 'pl'
        $R.Found | Should -BeTrue
        # Faktyczny tytuł z wersją (program ustawia go w trakcie startu) trafia do wyniku.
        $R.Title | Should -BeExactly 'DSOptimizer 4.0.0'
        $R.Source | Should -Be 'uia'
        $R.Seconds | Should -Be 3.2
        $R.ProbeAsDso | Should -BeTrue
    }
    It 'Invoke-E5UserProbe -RequestJson: plik żądania (UTF-8) obok sondy i argument -Request w akcji zadania' {
        $Plan = New-TestPlan
        Mock New-ScheduledTaskAction { $script:Action = [pscustomobject]@{ Execute = $Execute; Argument = $Argument }; $script:Action }
        Mock New-ScheduledTaskPrincipal { [pscustomobject]@{} }
        Mock New-ScheduledTaskSettingsSet { [pscustomobject]@{} }
        Mock Register-ScheduledTask -RemoveParameterType 'Action', 'Principal', 'Settings' { }
        Mock Start-ScheduledTask { Set-Content -LiteralPath (Join-Path $Plan.WorkDirectory 'startup.json') -Value '{ "Sid": "S-1", "Runs": [], "Error": null }' -Encoding UTF8 }
        Mock Unregister-ScheduledTask { }
        $null = Invoke-E5UserProbe -Plan $Plan -Name 'startup' -ScriptText 'param($ResultFile, $Request)' -RequestJson '{"Exe":"E:\\Narzędzia DSO\\DSOptimizer-4.0.0.exe"}' -TimeoutSeconds 10
        $RequestPath = Join-Path $Plan.WorkDirectory 'startup.req.json'
        ([System.IO.File]::ReadAllText($RequestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json).Exe | Should -BeExactly $script:UsbExe
        $script:Action.Argument | Should -Match ([regex]::Escape("-Request $RequestPath") + '$')
    }
    It 'sondy okna, startu i wypakowania: poprawna składnia, parametry -ResultFile i -Request, poza VM (mock-only) odmawiają przed skutkiem' {
        foreach ($Text in (Get-E5WaitWindowProbeScript), (Get-E5StartupProbeScript), (Get-E5ExtractProbeScript)) {
            $Errors = $null
            $null = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$null, [ref]$Errors)
            @($Errors).Count | Should -Be 0
            $Text | Should -Match '\$ResultFile'
            $Text | Should -Match '\$Request'
            $Text | Should -Match 'DSO_VM_TESTS_MOCK_ONLY'
            $Text | Should -Not -Match 'HKCU:|Set-ItemProperty|Start-Process'
        }
        Get-E5StartupProbeScript | Should -Match 'System\.Windows\.Automation'
        Get-E5ExtractProbeScript | Should -Match 'CopyHere'
        $Dir = Join-Path $TestDrive 'probe6'
        $null = New-Item -ItemType Directory -Path $Dir -Force
        $Request = Join-Path $Dir 'req.json'
        [System.IO.File]::WriteAllText($Request, '{"Exe":"C:\\Windows\\System32\\notepad.exe","Title":"x","Runs":1,"WindowTimeoutSeconds":1,"CloseTimeoutSeconds":1,"PauseSeconds":0,"TimeoutSeconds":1}')
        foreach ($Pair in @(@{ N = 'startup'; T = (Get-E5StartupProbeScript) }, @{ N = 'window'; T = (Get-E5WaitWindowProbeScript) }, @{ N = 'extract'; T = (Get-E5ExtractProbeScript) })) {
            $Probe = Join-Path $Dir "$($Pair.N).ps1"
            [System.IO.File]::WriteAllText($Probe, $Pair.T, (New-Object System.Text.UTF8Encoding($true)))
            $ResultFile = Join-Path $Dir "$($Pair.N).json"
            & $Probe -ResultFile $ResultFile -Request $Request
            (Get-Content -LiteralPath $ResultFile -Raw -Encoding UTF8 | ConvertFrom-Json).Error | Should -Match 'DSO_VM_TESTS_MOCK_ONLY'
        }
        Get-Process -Name 'notepad' -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-1) } | Should -BeNullOrEmpty
    }
    It 'Resolve-E5UsbVolume: dysk spoza systemowego offline -> online; wolumin z etykietą DSO-E5 nie pojawił się w limicie = wyjątek' {
        $Plan = New-TestPlan -Variant SingleExe
        $Plan.UsbWaitSeconds = 0
        Mock Get-Disk { @([pscustomobject]@{ Number = 0; IsBoot = $true; IsSystem = $true; IsOffline = $false; IsReadOnly = $false }, [pscustomobject]@{ Number = 1; IsBoot = $false; IsSystem = $false; IsOffline = $true; IsReadOnly = $false }) }
        Mock Set-Disk { $script:Onlined = $Number }
        Mock Get-Volume { @([pscustomobject]@{ FileSystemLabel = 'Windows'; DriveLetter = 'C' }) }
        { Resolve-E5UsbVolume -Plan $Plan } | Should -Throw '*DSO-E5*'
        $script:Onlined | Should -Be 1
        Should -Invoke Set-Disk -Times 1 -Exactly -ParameterFilter { $Number -eq 1 -and $IsOffline -eq $false }
        Should -Invoke Set-Disk -Times 0 -Exactly -ParameterFilter { $Number -eq 0 }
    }
    Context 'faza motw (kopia z MotW, start przez powłokę)' {
        BeforeEach {
            $script:Profile = Join-Path $TestDrive ('profil-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
            $null = New-Item -ItemType Directory -Path (Join-Path $script:Profile 'Desktop') -Force
            Mock Get-E5UserProfile { $script:Profile }
            Mock Register-E5Task { $script:Task = [pscustomobject]@{ TaskName = $TaskName; Execute = $Execute; Argument = $Argument; RunLevel = $RunLevel } }
        }
        It 'SingleExe: kopia wydania na pulpit dso z ZoneId=3, start: explorer.exe "<plik>" jako dso z RunLevel Limited (jak dwuklik)' {
            $script:Release = New-FakeRelease -Variant SingleExe
            Mock Resolve-E5UsbVolume { [pscustomobject]@{ ExePath = $script:UsbExe; ReleasePath = $script:Release; ReleaseExists = $true } }
            $R = Start-E5Motw -Plan (New-TestPlan -Variant SingleExe) -UserSid 'S-1-5-21-1'
            $R.Error | Should -BeNullOrEmpty
            $R.Copied | Should -Be (Join-Path $script:Profile 'Desktop\DSOptimizer-4.0.0.exe')
            $R.CopiedZone.ZoneId | Should -Be 3
            (Test-E5ZoneIdentifier -Path $R.Copied).ZoneId | Should -Be 3
            $R.LaunchPath | Should -Be $R.Copied
            $R.Launched | Should -BeTrue
            $script:Task.Execute | Should -Be (Join-Path $env:SystemRoot 'explorer.exe')
            $script:Task.Argument | Should -BeExactly ('"{0}"' -f $R.Copied)
            $script:Task.RunLevel | Should -Be 'Limited'
            $script:Task.TaskName | Should -Be 'DSO-E5-motw'
        }
        It 'Folder: zip z ZoneId=3 wypakowany przez powłokę dso (sonda), start exe z wypakowanego folderu' {
            $script:Release = New-FakeRelease -Variant Folder
            Mock Resolve-E5UsbVolume { [pscustomobject]@{ ExePath = 'E:\Narzędzia DSO\DSOptimizer\DSOptimizer.exe'; ReleasePath = $script:Release; ReleaseExists = $true } }
            Mock Invoke-E5UserProbe -ParameterFilter { $Name -eq 'motw-extract' } {
                $Req = $RequestJson | ConvertFrom-Json
                $script:ExtractRequest = $Req
                $null = New-Item -ItemType Directory -Path (Split-Path -Parent $Req.ExpectedExe) -Force
                [System.IO.File]::WriteAllBytes($Req.ExpectedExe, [byte[]](77, 90))
                [pscustomobject]@{ Sid = 'S-1-5-21-1'; Extracted = $true; Error = $null }
            }
            $R = Start-E5Motw -Plan (New-TestPlan -Variant Folder) -UserSid 'S-1-5-21-1'
            $R.Error | Should -BeNullOrEmpty
            $script:ExtractRequest.Zip | Should -Be (Join-Path $script:Profile 'Desktop\DSOptimizer-4.0.0.zip')
            $script:ExtractRequest.Destination | Should -Be (Join-Path $script:Profile 'Desktop\DSOptimizer-4.0.0')
            $R.LaunchPath | Should -Be (Join-Path $script:Profile 'Desktop\DSOptimizer-4.0.0\DSOptimizer\DSOptimizer.exe')
            $script:Task.Argument | Should -BeExactly ('"{0}"' -f $R.LaunchPath)
            $R.LaunchZone.Exists | Should -BeFalse
        }
        It 'brak pliku wydania na nośniku -> błąd, nic nie uruchomione' {
            Mock Resolve-E5UsbVolume { [pscustomobject]@{ ExePath = $script:UsbExe; ReleasePath = 'E:\wydanie\DSOptimizer-4.0.0.exe'; ReleaseExists = $false } }
            $R = Start-E5Motw -Plan (New-TestPlan -Variant SingleExe) -UserSid 'S-1-5-21-1'
            $R.Error | Should -Match 'wydania'
            $R.Launched | Should -BeFalse
            Should -Invoke Register-E5Task -Times 0 -Exactly
        }
    }
    It 'Get-E5MotwShown: okno procesu smartscreen.exe = SmartScreen, consent.exe = UAC, proces programu = Aplikacja, nic = nic' {
        @(Get-E5MotwShown -Windows @([pscustomobject]@{ ProcessId = 5 }) -Processes @([pscustomobject]@{ Name = 'smartscreen.exe'; ProcessId = 5 })) | Should -Be @('SmartScreen')
        @(Get-E5MotwShown -Windows @() -Processes @([pscustomobject]@{ Name = 'smartscreen.exe'; ProcessId = 5 })) | Should -Be @('nic')
        @(Get-E5MotwShown -Windows @() -Processes @([pscustomobject]@{ Name = 'consent.exe'; ProcessId = 6 })) | Should -Be @('UAC')
        @(Get-E5MotwShown -Windows @() -Processes @([pscustomobject]@{ Name = 'DSOptimizer-4.0.0.exe'; ProcessId = 7 })) | Should -Be @('Aplikacja')
        @(Get-E5MotwShown) | Should -Be @('nic')
    }
    It 'Stop-E5Motw: smartscreen.exe sprzed startu fazy (bez filtra czasu) z widocznym oknem Win32 niewidocznym dla UIA = SmartScreen; consent.exe = UAC; zamykany tylko dialog SmartScreen; detekcje od startu fazy' {
        Mock Invoke-E5UserProbe { [pscustomobject]@{ Sid = 'S-1-5-21-1'; Error = $null; Win32Error = $null
                Windows = @([pscustomobject]@{ ProcessId = 10; Name = ''; ClassName = 'ApplicationFrameWindow' }, [pscustomobject]@{ ProcessId = 10; Name = ''; ClassName = 'Shell_TrayWnd' })
                Win32Windows = @([pscustomobject]@{ Source = 'win32'; ProcessId = 50; ProcessName = 'smartscreen.exe'; Name = 'Windows SmartScreen'; ClassName = 'Windows.UI.Core.CoreWindow' }) } }
        # smartscreen.exe żyje od logowania – utworzony długo przed znacznikiem fazy.
        Mock Get-E5NamedProcesses { @([pscustomobject]@{ ProcessId = 50; Name = 'smartscreen.exe'; SessionId = 1; CreationDate = '2026-10-26T08:00:00.0000000+01:00' }, [pscustomobject]@{ ProcessId = 60; Name = 'smartscreen.exe'; SessionId = 1; CreationDate = '2026-10-26T08:00:00.0000000+01:00' }) }
        Mock Get-E5NewProcesses { @([pscustomobject]@{ ProcessId = 51; Name = 'consent.exe' }) }
        Mock Get-E5ExeProcesses { @() }
        Mock Stop-E5Processes { $script:Closed = @($Ids); [pscustomobject]@{ Closed = @($Ids); StillRunning = @(); Errors = @() } }
        Mock Unregister-E5Task { $script:Unregistered = $TaskName; $null }
        $State = [pscustomobject]@{ Since = '2026-10-26T12:00:00.0000000+01:00'; LaunchPath = 'C:\Users\dso\Desktop\DSOptimizer-4.0.0.exe'; TaskName = 'DSO-E5-motw' }
        $R = Stop-E5Motw -Plan (New-TestPlan -Variant SingleExe) -UserSid 'S-1-5-21-1' -State $State
        @($R.Shown) | Should -Be @('SmartScreen', 'UAC')
        ($R.SmartScreenEvidence -join ' ') | Should -Match 'okno Win32 procesu smartscreen.exe \(PID 50'
        @($R.SmartScreenProcesses | ForEach-Object { $_.ProcessId }) | Should -Be @(50, 60)
        @($R.Win32Windows).Count | Should -Be 1
        # consent.exe (UAC, proces SYSTEM) zostaje; smartscreen.exe bez okna (PID 60) też.
        @($script:Closed) | Should -Be @(50)
        $script:Unregistered | Should -Be 'DSO-E5-motw'
        $R.ProbeAsDso | Should -BeTrue
        Should -Invoke Get-E5NamedProcesses -Times 1 -Exactly -ParameterFilter { @($Names) -contains 'smartscreen.exe' }
        Should -Invoke Get-E5NewProcesses -Times 1 -Exactly -ParameterFilter { @($Names) -contains 'consent.exe' -and @($Names) -notcontains 'smartscreen.exe' }
        Should -Invoke Get-E5Detections -Times 1 -Exactly -ParameterFilter { $Since -eq ([datetimeoffset]::Parse('2026-10-26T12:00:00+01:00').LocalDateTime) }
        Should -Invoke Get-E5ExeProcesses -Times 1 -Exactly -ParameterFilter { $Path -eq 'C:\Users\dso\Desktop\DSOptimizer-4.0.0.exe' }
    }
    It 'Get-E5SmartScreenEvidence / Get-E5MotwShown: każda ścieżka wykrycia SmartScreen osobno; bez żadnego dowodu = nic' {
        $Titles = @((Get-E5Plan).SmartScreenTitles)
        # 1) okno Win32 procesu o nazwie smartscreen.exe (proces spoza listy procesów)
        @(Get-E5MotwShown -Win32Windows @([pscustomobject]@{ ProcessId = 70; ProcessName = 'smartscreen.exe'; Name = ''; ClassName = 'x' }) -Titles $Titles) | Should -Be @('SmartScreen')
        # 2) okno Win32 procesu z listy smartscreen.exe (nazwa procesu w oknie nieodczytana)
        @(Get-E5MotwShown -Win32Windows @([pscustomobject]@{ ProcessId = 71; ProcessName = $null; Name = 'x'; ClassName = 'x' }) -Processes @([pscustomobject]@{ ProcessId = 71; Name = 'smartscreen.exe' }) -Titles $Titles) | Should -Be @('SmartScreen')
        # 3) tytuł PL i 4) tytuł EN – okno dowolnego procesu
        @(Get-E5MotwShown -Win32Windows @([pscustomobject]@{ ProcessId = 72; ProcessName = 'explorer.exe'; Name = 'System Windows ochronił ten komputer'; ClassName = 'x' }) -Titles $Titles) | Should -Be @('SmartScreen')
        @(Get-E5MotwShown -Windows @([pscustomobject]@{ ProcessId = 73; Name = 'Windows protected your PC'; ClassName = 'x' }) -Titles $Titles) | Should -Be @('SmartScreen')
        # 5) okno UIA procesu smartscreen.exe
        @(Get-E5MotwShown -Windows @([pscustomobject]@{ ProcessId = 74; Name = ''; ClassName = 'x' }) -Processes @([pscustomobject]@{ ProcessId = 74; Name = 'smartscreen.exe' }) -Titles $Titles) | Should -Be @('SmartScreen')
        # brak dowodu: proces smartscreen.exe bez okna, okna innych procesów bez tytułu SmartScreen
        @(Get-E5MotwShown -Windows @([pscustomobject]@{ ProcessId = 10; Name = ''; ClassName = 'Shell_TrayWnd' }) -Win32Windows @([pscustomobject]@{ ProcessId = 10; ProcessName = 'explorer.exe'; Name = ''; ClassName = 'Progman' }) -Processes @([pscustomobject]@{ ProcessId = 75; Name = 'smartscreen.exe' }) -Titles $Titles) | Should -Be @('nic')
        @(Get-E5SmartScreenEvidence -Win32Windows @([pscustomobject]@{ ProcessId = 72; ProcessName = 'explorer.exe'; Name = 'System Windows ochronił ten komputer'; ClassName = 'x' }) -Titles $Titles)[0] | Should -Match 'o tytule SmartScreen'
    }
    It 'Select-E5MainWindow: okno procesu z tytułem „DSOptimizer” + wersja (tytuł ustawiany w trakcie startu) – wybrane; inny PID, okno dialogowe #32770, pusty tytuł albo inny przedrostek – nie' {
        $Main = [pscustomobject]@{ Source = 'uia'; ProcessId = 77; Name = 'DSOptimizer 4.0.0'; ClassName = 'HwndWrapper[DSOptimizer;;6d1f]' }
        (Select-E5MainWindow -Windows @($Main) -ProcessIds @(77) -TitlePrefix 'DSOptimizer').Name | Should -BeExactly 'DSOptimizer 4.0.0'
        (Select-E5MainWindow -Windows @([pscustomobject]@{ ProcessId = 77; Name = 'DSOptimizer 4.1.2'; ClassName = 'HwndWrapper[x]' }) -ProcessIds @(77) -TitlePrefix 'DSOptimizer').Name | Should -BeExactly 'DSOptimizer 4.1.2'
        (Select-E5MainWindow -Windows @([pscustomobject]@{ ProcessId = 77; Name = 'DSOptimizer'; ClassName = 'HwndWrapper[x]' }) -ProcessIds @(77) -TitlePrefix 'DSOptimizer').Name | Should -BeExactly 'DSOptimizer'
        Select-E5MainWindow -Windows @($Main) -ProcessIds @(78) -TitlePrefix 'DSOptimizer' | Should -BeNullOrEmpty
        Select-E5MainWindow -Windows @([pscustomobject]@{ ProcessId = 77; Name = 'DSOptimizer – błąd'; ClassName = '#32770' }) -ProcessIds @(77) -TitlePrefix 'DSOptimizer' | Should -BeNullOrEmpty
        Select-E5MainWindow -Windows @([pscustomobject]@{ ProcessId = 77; Name = ''; ClassName = 'HwndWrapper[x]' }) -ProcessIds @(77) -TitlePrefix 'DSOptimizer' | Should -BeNullOrEmpty
        Select-E5MainWindow -Windows @([pscustomobject]@{ ProcessId = 77; Name = 'Default IME'; ClassName = 'IME' }) -ProcessIds @(77) -TitlePrefix 'DSOptimizer' | Should -BeNullOrEmpty
        Select-E5MainWindow -Windows @() -ProcessIds @(77) -TitlePrefix 'DSOptimizer' | Should -BeNullOrEmpty
        # Kolejność listy wygrywa (UIA przed Win32) – pierwsze pasujące okno.
        (Select-E5MainWindow -Windows @($Main, [pscustomobject]@{ Source = 'win32'; ProcessId = 77; Name = 'DSOptimizer 4.0.0'; ClassName = 'HwndWrapper[x]' }) -ProcessIds @(77) -TitlePrefix 'DSOptimizer').Source | Should -Be 'uia'
    }
    It 'Select-E5MainWindow: okno wyboru języka (AutomationId LanguageChoiceWindow, tytuł „DSOptimizer”) nie jest oknem głównym – ani samo, ani jako kopia Win32 o tym samym uchwycie' {
        $Chooser = [pscustomobject]@{ Source = 'uia'; ProcessId = 77; Name = 'DSOptimizer'; ClassName = 'HwndWrapper[x]'; AutomationId = 'LanguageChoiceWindow'; Handle = 500 }
        $ChooserWin32 = [pscustomobject]@{ Source = 'win32'; ProcessId = 77; Name = 'DSOptimizer'; ClassName = 'HwndWrapper[x]'; Handle = 500 }
        $Main = [pscustomobject]@{ Source = 'uia'; ProcessId = 77; Name = 'DSOptimizer 4.0.1'; ClassName = 'HwndWrapper[x]'; AutomationId = ''; Handle = 600 }
        Select-E5MainWindow -Windows @($Chooser) -ProcessIds @(77) -TitlePrefix 'DSOptimizer' | Should -BeNullOrEmpty
        Select-E5MainWindow -Windows @($ChooserWin32, $Chooser) -ProcessIds @(77) -TitlePrefix 'DSOptimizer' | Should -BeNullOrEmpty
        (Select-E5MainWindow -Windows @($ChooserWin32, $Chooser, $Main) -ProcessIds @(77) -TitlePrefix 'DSOptimizer').Handle | Should -Be 600
        # bez AutomationId i uchwytu (starsze dane) okno nadal działa jak dotąd
        (Select-E5MainWindow -Windows @([pscustomobject]@{ ProcessId = 77; Name = 'DSOptimizer'; ClassName = 'HwndWrapper[x]' }) -ProcessIds @(77) -TitlePrefix 'DSOptimizer').Name | Should -BeExactly 'DSOptimizer'
    }
    It 'sondy okna i startu: sprawdzają obecność okna wyboru po AutomationId i czekają na jego zniknięcie; czas startu bez czasu, gdy okno wyboru było widoczne' {
        foreach ($Text in (Get-E5WaitWindowProbeScript), (Get-E5StartupProbeScript)) {
            $Text | Should -Match 'function Test-ProbeLanguageChoiceOpen \{'
            $Text | Should -Match 'AutomationIdProperty|AutomationId'
            $Text | Should -Match 'NativeWindowHandle'
        }
        $Startup = Get-E5StartupProbeScript
        $Startup | Should -Match 'ChooserDetectedMs'
        $Startup | Should -Match 'ChooserGoneMs'
        $Startup | Should -Match 'ChooserVisibleMs'
        $Startup | Should -Match 'WindowMs = \[int\]\$Watch\.ElapsedMilliseconds - '
    }
    It 'sondy okna i startu: wybierają język w oknie wyboru (LanguageChoiceWindow, kafelek przez InvokePattern) przed oknem głównym' {
        foreach ($Text in (Get-E5WaitWindowProbeScript), (Get-E5StartupProbeScript)) {
            $Text | Should -Match 'function Invoke-ProbeLanguageChoice \{'
            $Text | Should -Match 'LanguageChoiceWindow'
            $Text | Should -Match 'LanguageChoicePolish'
            $Text | Should -Match 'LanguageChoiceEnglish'
            $Text | Should -Match 'InvokePattern'
            $Text | Should -Match '\$Req\.Language'
            $Text.IndexOf('Invoke-ProbeLanguageChoice -ProcessIds') | Should -BeLessThan $Text.IndexOf('Select-E5MainWindow -Windows')
        }
    }
    It 'sondy okna, startu i okien: zawierają Select-E5MainWindow i Win32 EnumWindows, wybór po PID (ProcessIdProperty), bez dokładnego warunku tytułu' {
        foreach ($Text in (Get-E5WaitWindowProbeScript), (Get-E5StartupProbeScript)) {
            $Text | Should -Match 'function Select-E5MainWindow \{'
            $Text | Should -Match 'function Get-ProbeWin32Windows \{'
            $Text | Should -Match 'ProcessIdProperty'
            $Text | Should -Match 'Select-E5MainWindow -Windows'
            $Text | Should -Match 'TitlePrefix'
            $Text | Should -Not -Match 'NameProperty'
            $Text | Should -Not -Match '\$Req\.Title\b'
        }
        $Window = Get-E5WindowProbeScript
        $Window | Should -Match 'EnumWindows'
        $Window | Should -Match 'Win32Windows = @\(Get-ProbeWin32Windows\)'
        $Window | Should -Match '^param\(\[Parameter\(Mandatory\)\]\[string\]\$ResultFile\)'
    }
    It 'Get-ProbeWin32Windows (tekst sond): kompiluje się w PowerShell 5.1 i wylicza widoczne okna najwyższego poziomu (tylko odczyt, host)' {
        $Helpers = Get-E5ProbeHelpersText
        $Result = & {
            . ([scriptblock]::Create($Helpers))
            @(Get-ProbeWin32Windows)
        }
        foreach ($Item in @($Result | Select-Object -First 5)) {
            $Item.Source | Should -Be 'win32'
            $Item.ProcessId | Should -BeOfType [int]
            $null -ne $Item.PSObject.Properties['ClassName'] | Should -BeTrue
            $null -ne $Item.Bounds | Should -BeTrue
        }
        $Own = & { . ([scriptblock]::Create($Helpers)); @(Get-ProbeWin32Windows -ProcessIds @(-1)) }
        @($Own).Count | Should -Be 0
    }
    It 'Invoke-E5AvPhase: kroki w kolejności planu z --out w katalogu surowym, AppX = pierwsza Suggested/Removable z --list-apps, fixture przed --remove-app Win32, panel zamknięty, powłoka poczekana, WinUtil i GUI, detekcje od startu fazy, kopie logów obok exe' {
        $Plan = New-TestPlan -Variant SingleExe
        $Raw = $Plan.RawDirectory
        $script:Order = [System.Collections.Generic.List[string]]::new()
        Mock Resolve-E5UsbVolume { [pscustomobject]@{ ExePath = $script:UsbExe; ReleasePath = 'E:\wydanie\DSOptimizer-4.0.0.exe'; ReleaseExists = $true } }
        Mock Invoke-E5Cli {
            $script:Order.Add("cli $Name | $Arguments")
            $Report = $null
            if ($Name -like '*-list-apps') {
                $Report = [pscustomobject]@{ apps = @(
                        [pscustomobject]@{ id = 'appx:Microsoft.Inne_8wekyb3d8bbwe'; group = 'Other'; removability = 'Removable'; name = 'Inne' }
                        [pscustomobject]@{ id = 'win32:HKLM\SOFTWARE\X'; group = 'Suggested'; removability = 'Removable'; name = 'Program' }
                        [pscustomobject]@{ id = 'appx:Microsoft.Chroniona_8wekyb3d8bbwe'; group = 'Suggested'; removability = 'Protected'; name = 'Chroniona' }
                        [pscustomobject]@{ id = 'appx:Clipchamp.Clipchamp_yxz26nhyzhsrt'; group = 'Suggested'; removability = 'Removable'; name = 'Clipchamp' }
                        [pscustomobject]@{ id = 'appx:Microsoft.BingNews_8wekyb3d8bbwe'; group = 'Suggested'; removability = 'Removable'; name = 'Wiadomości' }) }
            }
            [pscustomobject]@{ Name = $Name; ExitCode = 0; Finished = $true; TimedOut = $false; Report = $Report; ReportError = $null; Error = $null }
        }
        Mock Install-E5Fixture { $script:Order.Add('fixture'); [pscustomobject]@{ Present = $true; ExitCode = 0 } }
        Mock Get-E5NewProcesses { $script:Order.Add("procs $($Names -join ',')"); @([pscustomobject]@{ ProcessId = 900; Name = 'mmc.exe' }) }
        Mock Stop-E5Processes { $script:Order.Add("close $(@($Ids) -join ',')"); [pscustomobject]@{ Closed = @($Ids); StillRunning = @(); Errors = @() } }
        Mock Wait-E5UserShell { $script:Order.Add('shell'); [pscustomobject]@{ Ready = $true; Seconds = 4 } }
        Mock Invoke-E5WinUtilCliStep { $script:Order.Add("winutil $Name | $Arguments"); [pscustomobject]@{ Cli = [pscustomobject]@{ ExitCode = 0 }; AliveAfterWait = $true; WinUtilVisible = $true } }
        Mock Start-E5Gui { $script:Order.Add("gui $Name $Exe"); [pscustomobject]@{ Exe = $Exe; TaskName = "DSO-E5-gui-$Name"; Found = $true } }
        Mock Stop-E5Gui { $script:Order.Add('gui-stop'); [pscustomobject]@{ Error = $null } }
        Mock Copy-E5DsoLogs { $script:Order.Add("logs $ExeDirectory"); [pscustomobject]@{ Copied = @('journal.json'); UsbLogExists = $true } }
        $R = Invoke-E5AvPhase -Plan $Plan -UserSid 'S-1-5-21-1'
        $R.Error | Should -BeNullOrEmpty
        @($script:Order) | Should -Be @(
            "cli 01-apply-advertising-id | --apply privacy.advertising-id --out $Raw\av-01-apply-advertising-id.json"
            "cli 02-undo-advertising-id | --undo privacy.advertising-id --out $Raw\av-02-undo-advertising-id.json"
            "cli 03-apply-diagtrack | --apply privacy.diagtrack --out $Raw\av-03-apply-diagtrack.json"
            "cli 04-undo-diagtrack | --undo privacy.diagtrack --out $Raw\av-04-undo-diagtrack.json"
            "cli 05-apply-system-protection | --apply repair.system-protection --out $Raw\av-05-apply-system-protection.json"
            "cli 06-restore-point | --execute repair.restore-point --out $Raw\av-06-restore-point.json"
            "cli 07-undo-system-protection | --undo repair.system-protection --out $Raw\av-07-undo-system-protection.json"
            "cli 08-computer-management | --execute panels.computer-management --out $Raw\av-08-computer-management.json"
            'procs mmc.exe'
            'close 900'
            "cli 09-dns-flush | --execute repair.dns-flush --out $Raw\av-09-dns-flush.json"
            "cli 10-restart-explorer | --execute tools.restart-explorer --out $Raw\av-10-restart-explorer.json"
            'shell'
            "cli 11-wu-reset | --execute repair.wu-reset --out $Raw\av-11-wu-reset.json"
            "cli 12-disk-cleanup | --execute repair.disk-cleanup --out $Raw\av-12-disk-cleanup.json"
            "cli 13-list-apps | --list-apps --out $Raw\av-13-list-apps.json"
            "cli 14-remove-appx | --remove-app appx:Clipchamp.Clipchamp_yxz26nhyzhsrt --restore-point skip --out $Raw\av-14-remove-appx.json"
            'fixture'
            "cli 15-remove-win32 | --remove-app win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DsoTestQuiet --restore-point ifavailable --out $Raw\av-15-remove-win32.json"
            "cli 16-install-apps | --install-apps install.7zip,install.rustdesk --out $Raw\av-16-install-apps.json"
            "winutil 17-winutil | --execute tools.winutil --out $Raw\av-17-winutil.json"
            "gui 18-gui $script:UsbExe"
            'gui-stop'
            'logs E:\Narzędzia DSO')
        $R.Candidate.Id | Should -Be 'appx:Clipchamp.Clipchamp_yxz26nhyzhsrt'
        @($R.Steps).Count | Should -Be 18
        @($R.Steps | Where-Object { $_.Error }).Count | Should -Be 0
        Should -Invoke Invoke-E5Cli -Times 1 -Exactly -ParameterFilter { $Name -eq '12-disk-cleanup' -and $TimeoutSeconds -eq 3600 }
        Should -Invoke Invoke-E5Cli -Times 1 -Exactly -ParameterFilter { $Name -eq '01-apply-advertising-id' -and $TimeoutSeconds -eq 600 -and $Exe -eq $script:UsbExe }
        Should -Invoke Get-E5Detections -Times 1 -Exactly -ParameterFilter { $Since -eq [datetime]$R.Since }
    }
    It 'Invoke-E5AvPhase: brak AppX Suggested/Removable -> błąd kroku (nic nie usunięto), kolejne kroki idą dalej' {
        Mock Resolve-E5UsbVolume { [pscustomobject]@{ ExePath = $script:UsbExe } }
        Mock Invoke-E5Cli { [pscustomobject]@{ Name = $Name; ExitCode = 0; Report = $(if ($Name -like '*-list-apps') { [pscustomobject]@{ apps = @() } } else { $null }) } }
        Mock Install-E5Fixture { [pscustomobject]@{ Present = $true } }
        Mock Get-E5NewProcesses { @() }
        Mock Stop-E5Processes { [pscustomobject]@{ Closed = @(); StillRunning = @(); Errors = @() } }
        Mock Wait-E5UserShell { [pscustomobject]@{ Ready = $true } }
        Mock Invoke-E5WinUtilCliStep { [pscustomobject]@{ Cli = $null } }
        Mock Start-E5Gui { [pscustomobject]@{ Exe = $Exe } }
        Mock Stop-E5Gui { [pscustomobject]@{ Error = $null } }
        Mock Copy-E5DsoLogs { [pscustomobject]@{ Copied = @() } }
        $R = Invoke-E5AvPhase -Plan (New-TestPlan -Variant SingleExe) -UserSid 'S-1-5-21-1'
        $Step = @($R.Steps | Where-Object { $_.Kind -eq 'remove-appx' })[0]
        $Step.Error | Should -Match 'Brak pozycji AppX'
        Should -Invoke Invoke-E5Cli -Times 0 -Exactly -ParameterFilter { $Name -like '*-remove-appx' }
        Should -Invoke Invoke-E5Cli -Times 1 -Exactly -ParameterFilter { $Name -like '*-install-apps' }
        Should -Invoke Start-E5Gui -Times 1 -Exactly
    }
    It 'Invoke-E5WinUtilCliStep: korzeń = plik z folderu pobrań, Zone.Identifier pobranego winutil.ps1 zapisany, okno WinUtil w drzewie, zamknięcie przez Stop-E5Variant' {
        $Plan = New-TestPlan -Variant SingleExe
        $Downloaded = Join-Path $Plan.DownloadsDirectory "$script:Guid.ps1"
        $null = New-Item -ItemType Directory -Path $Plan.DownloadsDirectory -Force
        [System.IO.File]::WriteAllText($Downloaded, "Write-Output 'winutil'")
        Mock Invoke-E5Cli { $script:CliArguments = $Arguments; [pscustomobject]@{ ExitCode = 0; Finished = $true } }
        Mock Find-E5RootProcess { $script:Markers += , $Marker; if ($Marker -eq $Plan.DownloadsDirectory) { 4242 } else { $null } }
        $script:Markers = @()
        Mock Get-E5ProcessTree { @([pscustomobject]@{ ProcessId = 4242; ParentProcessId = 1; Name = 'powershell.exe'; CommandLine = "powershell.exe -NoExit -NoProfile -ExecutionPolicy RemoteSigned -File `"$Downloaded`"" }) }
        Mock Invoke-E5UserProbe { [pscustomobject]@{ Sid = 'S-1-5-21-1'; Error = $null; Windows = @([pscustomobject]@{ ProcessId = 4242; Name = "Chris Titus Tech's Windows Utility (Winutil)"; ClassName = 'HwndWrapper' }) } }
        Mock Stop-E5Variant { $script:StopState = $State; [pscustomobject]@{ Closed = @(4242); Detections = [pscustomobject]@{ Errors = @() }; Error = $null } }
        $R = Invoke-E5WinUtilCliStep -Plan $Plan -UserSid 'S-1-5-21-1' -Exe $script:UsbExe -Name '17-winutil' -Arguments '--execute tools.winutil --out x.json'
        $script:CliArguments | Should -BeExactly '--execute tools.winutil --out x.json'
        $R.RootPid | Should -Be 4242
        $R.RootMarker | Should -Be $Plan.DownloadsDirectory
        $R.RootFromDownloads | Should -BeTrue
        $R.DownloadedZoneError | Should -BeNullOrEmpty
        $R.AliveAfterWait | Should -BeTrue
        $R.DownloadedFile | Should -Be $Downloaded
        $R.DownloadedZone.Exists | Should -BeFalse
        @($R.DownloadsFiles).Count | Should -Be 1
        $R.DownloadsFiles[0].ZoneIdentifier | Should -BeFalse
        $R.WinUtilVisible | Should -BeTrue
        $R.ProbeAsDso | Should -BeTrue
        $script:StopState.RootPid | Should -Be 4242
        $script:StopState.Variant | Should -Be '17-winutil'
        Should -Invoke Start-Sleep -ParameterFilter { $Seconds -ge 59 -and $Seconds -le 60 }
    }
    It 'Invoke-E5WinUtilCliStep: korzeń tylko po znaczniku awaryjnym (irm) -> RootFromDownloads = false; błąd odczytu strumieni pobranego pliku -> DownloadedZoneError, nie „brak MotW”' {
        $Plan = New-TestPlan -Variant SingleExe
        $Downloaded = Join-Path $Plan.DownloadsDirectory "$script:Guid.ps1"
        $null = New-Item -ItemType Directory -Path $Plan.DownloadsDirectory -Force
        [System.IO.File]::WriteAllText($Downloaded, "Write-Output 'winutil'")
        Mock Invoke-E5Cli { [pscustomobject]@{ ExitCode = 0; Finished = $true } }
        Mock Find-E5RootProcess { if ($Marker -eq 'christitus.com/win') { 4242 } else { $null } }
        Mock Get-E5ProcessTree { @([pscustomobject]@{ ProcessId = 4242; ParentProcessId = 1; Name = 'powershell.exe'; CommandLine = "powershell.exe -NoExit -File `"$Downloaded`"" }) }
        Mock Test-E5ZoneIdentifier { throw [System.UnauthorizedAccessException]::new('Odmowa dostępu') }
        Mock Invoke-E5UserProbe { [pscustomobject]@{ Sid = 'S-1-5-21-1'; Error = $null; Windows = @() } }
        Mock Stop-E5Variant { [pscustomobject]@{ Closed = @(); Detections = [pscustomobject]@{ Errors = @() }; Error = $null } }
        $R = Invoke-E5WinUtilCliStep -Plan $Plan -UserSid 'S-1-5-21-1' -Exe $script:UsbExe -Name '17-winutil' -Arguments '--execute tools.winutil --out x.json'
        $R.RootMarker | Should -Be 'christitus.com/win'
        $R.RootFromDownloads | Should -BeFalse
        $R.DownloadedZone | Should -BeNullOrEmpty
        $R.DownloadedZoneError | Should -Match 'Odmowa dostępu'
        $R.DownloadsFiles[0].ZoneIdentifier | Should -BeNullOrEmpty
        $R.DownloadsFiles[0].ZoneError | Should -Match 'Odmowa dostępu'
        $R.Error | Should -BeNullOrEmpty
    }
    It 'Test-E5ZoneIdentifier: brak strumienia = Exists false; strumień ZoneId=3 odczytany; brak pliku albo błąd odczytu listy strumieni = wyjątek (nie „brak MotW”)' {
        $File = Join-Path $TestDrive 'mark.ps1'
        [System.IO.File]::WriteAllText($File, 'x')
        (Test-E5ZoneIdentifier -Path $File).Exists | Should -BeFalse
        Set-Content -LiteralPath $File -Stream 'Zone.Identifier' -Value "[ZoneTransfer]`r`nZoneId=3"
        $Zone = Test-E5ZoneIdentifier -Path $File
        $Zone.Exists | Should -BeTrue
        $Zone.ZoneId | Should -Be 3
        { Test-E5ZoneIdentifier -Path (Join-Path $TestDrive 'brak.ps1') } | Should -Throw '*Brak pliku*'
        Mock Get-Item -ParameterFilter { $Stream -eq '*' } { throw [System.UnauthorizedAccessException]::new('Odmowa dostępu') }
        { Test-E5ZoneIdentifier -Path $File } | Should -Throw '*Odmowa dostępu*'
    }
    It 'Invoke-E5StartupPhase: sonda dostaje exe z nośnika, tytuł okna i 3 przebiegi w żądaniu; przebiegi i SID zapisane; pozostałe procesy zamknięte; detekcje od startu fazy' {
        Mock Resolve-E5UsbVolume { [pscustomobject]@{ ExePath = $script:UsbExe } }
        Mock Invoke-E5UserProbe { $script:Request = $RequestJson | ConvertFrom-Json; [pscustomobject]@{ Sid = 'S-1-5-21-1'; Elevated = $true; Error = $null; Runs = @(
                    [pscustomobject]@{ Index = 1; WindowMs = 9100 }, [pscustomobject]@{ Index = 2; WindowMs = 4100 }, [pscustomobject]@{ Index = 3; WindowMs = 3900 }) } }
        Mock Get-E5ExeProcesses { @() }
        Mock Stop-E5Processes { [pscustomobject]@{ Closed = @(); StillRunning = @(); Errors = @() } }
        $R = Invoke-E5StartupPhase -Plan (New-TestPlan -Variant SingleExe) -UserSid 'S-1-5-21-1'
        $script:Request.Exe | Should -BeExactly $script:UsbExe
        $script:Request.TitlePrefix | Should -BeExactly 'DSOptimizer'
        $script:Request.Runs | Should -Be 3
        $script:Request.Language | Should -BeExactly 'pl'
        @($R.Runs).Count | Should -Be 3
        $R.ProbeAsDso | Should -BeTrue
        $R.Error | Should -BeNullOrEmpty
        Should -Invoke Get-E5Detections -Times 1 -Exactly
        Should -Invoke Get-E5ExeProcesses -Times 1 -Exactly -ParameterFilter { $Path -eq $script:UsbExe }
    }
}

Describe 'Task 6: Get-E5Detections (ponownie użyte rekordy wykryć)' {
    It 'Get-E5Detections: rekord wykrycia sprzed znacznika, ale ze zmianą stanu po nim (ponownie użyty) liczy się jako Reused; starszy bez zmiany nie' {
        $Since = [datetime]'2026-10-26T10:00:00'
        Mock Get-MpThreat { @([pscustomobject]@{ ThreatID = 7; ThreatName = 'Trojan:Win32/Wacatac' }) }
        Mock Get-MpThreatDetection {
            @([pscustomobject]@{ ThreatID = 7; InitialDetectionTime = [datetime]'2026-10-26T09:00:00'; LastThreatStatusChangeTime = [datetime]'2026-10-26T10:05:00'; ProcessName = 'x'; Resources = @('file:_E:\Narzędzia DSO\DSOptimizer-4.0.0.exe') }
                [pscustomobject]@{ ThreatID = 7; InitialDetectionTime = [datetime]'2026-10-26T08:00:00'; LastThreatStatusChangeTime = [datetime]'2026-10-26T08:01:00'; ProcessName = 'y'; Resources = @('file:_C:\stary.exe') })
        }
        Mock Get-WinEvent { throw [System.Management.Automation.ErrorRecord]::new([System.Exception]::new('brak'), 'NoMatchingEventsFound,Microsoft.PowerShell.Commands.GetWinEventCommand', 'ObjectNotFound', $null) }
        $R = Get-E5Detections -Since $Since -LogName 'Microsoft-Windows-Windows Defender/Operational' -EventIds @(1116)
        $R.ThreatCount | Should -Be 1
        $R.EventCount | Should -Be 0
        $R.Any | Should -BeTrue
        $R.Threats[0].Reused | Should -BeTrue
        $R.Threats[0].LastThreatStatusChangeTime | Should -Match '^2026-10-26T10:05:00'
        $R.Threats[0].ThreatName | Should -Be 'Trojan:Win32/Wacatac'
    }
}

Describe 'Task 6: podsumowania faz (czysta logika: wynik pomiaru a awaria)' {
    BeforeAll {
        $script:OkDetections = [pscustomobject]@{ ThreatCount = 0; EventCount = 0; Any = $false; Errors = @() }
        function script:New-AvStep {
            param([string]$Name, [string]$Kind = 'cli', $Exit = 0, [string]$Outcome = 'Applied', $Extra = $null, [string]$Error = $null, [switch]$TimedOut, [string]$ReportError)
            $Result = if ($Kind -eq 'gui') { $null } else { [pscustomobject]@{ ExitCode = $Exit; TimedOut = [bool]$TimedOut; Finished = -not $TimedOut; Report = [pscustomobject]@{ outcome = $Outcome }; ReportError = $(if ($ReportError) { $ReportError } else { $null }); Error = $null; Seconds = 3 } }
            return [pscustomobject]@{ Name = $Name; Kind = $Kind; ExpectedExit = 0; Result = $Result; Extra = $Extra; Error = $(if ($Error) { $Error } else { $null }) }
        }
        function script:New-AvData {
            param([object[]]$Steps, $Detections = $script:OkDetections)
            return [pscustomobject]@{ Steps = $Steps; Candidate = [pscustomobject]@{ Id = 'appx:Clipchamp' }; Detections = $Detections; Logs = [pscustomobject]@{ UsbLogExists = $true }; Error = $null }
        }
        $script:GoodWinUtil = [pscustomobject]@{ AliveAfterWait = $true; WinUtilVisible = $true; WaitSeconds = 60; ProbeAsDso = $true; ProbeError = $null; Error = $null; RootPid = 4242; RootMarker = 'C:\ProgramData\DSOptimizer\downloads'
            RootFromDownloads = $true; DownloadedZone = [pscustomobject]@{ Exists = $false; ZoneId = $null }; DownloadedZoneError = $null; DownloadsFiles = @([pscustomobject]@{ Name = 'a.ps1'; ZoneIdentifier = $false; ZoneError = $null })
            Stop = [pscustomobject]@{ Error = $null; Detections = $script:OkDetections } }
        $script:GoodGui = [pscustomobject]@{ Start = [pscustomobject]@{ Found = $true; Title = 'DSOptimizer 4.0.0'; ProbeAsDso = $true; ProbeError = $null; Error = $null }; Stop = [pscustomobject]@{ Error = $null } }
    }
    It 'Get-E5AvSummary: wszystkie kroki z kodem 0, raportami, oknem WinUtil i GUI, detekcje odczytane -> Valid, 0 wykryć to wynik' {
        $S = Get-E5AvSummary -Data (New-AvData -Steps @((New-AvStep '01-apply'), (New-AvStep '17-winutil' -Kind 'winutil' -Outcome 'Executed' -Extra $script:GoodWinUtil), (New-AvStep '18-gui' -Kind 'gui' -Extra $script:GoodGui)))
        $S.Valid | Should -BeTrue
        @($S.Problems).Count | Should -Be 0
        $S.ThreatCount | Should -Be 0
        $S.Detected | Should -BeFalse
        $S.UsbLogExists | Should -BeTrue
        @($S.Steps | ForEach-Object { $_.Outcome }) | Should -Be @('Applied', 'Executed', $null)
        @($S.Steps)[2].WindowTitle | Should -BeExactly 'DSOptimizer 4.0.0'
    }
    It 'Get-E5AvSummary: kod inny niż oczekiwany, limit czasu, brak raportu, błąd kroku, WinUtil nieżywy, GUI bez okna, błąd odczytu detekcji -> problemy (kod 1)' {
        $DeadWinUtil = [pscustomobject]@{ AliveAfterWait = $false; WinUtilVisible = $false; WaitSeconds = 60; ProbeAsDso = $true; Stop = [pscustomobject]@{ Detections = $script:OkDetections } }
        $NoWindow = [pscustomobject]@{ Start = [pscustomobject]@{ Found = $false; ProbeAsDso = $true }; Stop = [pscustomobject]@{} }
        $Steps = @((New-AvStep '05-apply-system-protection' -Exit 1 -Outcome 'Failed'), (New-AvStep '10-restart-explorer' -Outcome 'Executed' -Extra ([pscustomobject]@{ Shell = [pscustomobject]@{ Ready = $false; Seconds = 90 } })), (New-AvStep '12-disk-cleanup' -TimedOut -Exit $null), (New-AvStep '13-list-apps' -ReportError 'brak raportu --out'),
            (New-AvStep '14-remove-appx' -Error 'Brak pozycji AppX'), (New-AvStep '17-winutil' -Kind 'winutil' -Extra $DeadWinUtil), (New-AvStep '18-gui' -Kind 'gui' -Extra $NoWindow))
        $S = Get-E5AvSummary -Data (New-AvData -Steps $Steps -Detections ([pscustomobject]@{ ThreatCount = 0; EventCount = 0; Any = $false; Errors = @('Get-WinEvent: dziennik niedostępny') }))
        $S.Valid | Should -BeFalse
        $Text = $S.Problems -join ' | '
        $Text | Should -Match '05-apply-system-protection: kod 1 zamiast 0 \(wynik: Failed\)'
        $Text | Should -Match '12-disk-cleanup: limit czasu'
        $Text | Should -Match '10-restart-explorer: powłoka dso nie wróciła po 90 s'
        $Text | Should -Match '13-list-apps: brak raportu'
        $Text | Should -Match '14-remove-appx: Brak pozycji AppX'
        $Text | Should -Match '17-winutil: okno WinUtil nie żyje po 60 s'
        $Text | Should -Match '18-gui: okno główne nie pojawiło się'
        $Text | Should -Match 'av: odczyt detekcji: Get-WinEvent'
        (Get-E5AvSummary -Data $null).Valid | Should -BeFalse
    }
    It 'Get-E5AvSummary: WinUtil znaleziony po znaczniku awaryjnym, nieustalony plik albo błąd odczytu Zone.Identifier -> problemy (pomiar MotW niepełny)' {
        $Fallback = [pscustomobject]@{ AliveAfterWait = $true; WinUtilVisible = $true; WaitSeconds = 60; ProbeAsDso = $true; RootPid = 4242; RootMarker = 'christitus.com/win'; RootFromDownloads = $false
            DownloadedZone = $null; DownloadedZoneError = $null; DownloadsFiles = @(); Stop = [pscustomobject]@{ Detections = $script:OkDetections } }
        $S = Get-E5AvSummary -Data (New-AvData -Steps @((New-AvStep '17-winutil' -Kind 'winutil' -Extra $Fallback)))
        $S.Valid | Should -BeFalse
        ($S.Problems -join ' | ') | Should -Match "znaczniku awaryjnym 'christitus.com/win'"
        ($S.Problems -join ' | ') | Should -Match 'nie ustalono pobranego winutil.ps1'
        $ReadError = [pscustomobject]@{ AliveAfterWait = $true; WinUtilVisible = $true; WaitSeconds = 60; ProbeAsDso = $true; RootPid = 4242; RootFromDownloads = $true
            DownloadedZone = $null; DownloadedZoneError = '0x80070005 Odmowa dostępu'; DownloadsFiles = @([pscustomobject]@{ Name = 'a.ps1'; ZoneIdentifier = $null; ZoneError = '0x80070005 Odmowa dostępu' }); Stop = [pscustomobject]@{ Detections = $script:OkDetections } }
        $S2 = Get-E5AvSummary -Data (New-AvData -Steps @((New-AvStep '17-winutil' -Kind 'winutil' -Extra $ReadError)))
        $S2.Valid | Should -BeFalse
        ($S2.Problems -join ' | ') | Should -Match 'odczyt Zone.Identifier pobranego winutil.ps1: 0x80070005'
        ($S2.Problems -join ' | ') | Should -Match 'odczyt Zone.Identifier pliku pobrań a.ps1'
    }
    It 'Get-E5StartupSummary: pierwszy start (zimny) i mediana z 3 przebiegów; Valid przy oknach w limicie' {
        $Data = [pscustomobject]@{ ProbeAsDso = $true; ProbeError = $null; Error = $null; Runs = @([pscustomobject]@{ Index = 1; WindowMs = 9100; Title = 'DSOptimizer 4.0.0' }, [pscustomobject]@{ Index = 2; WindowMs = 4100; Title = 'DSOptimizer 4.0.0' }, [pscustomobject]@{ Index = 3; WindowMs = 3900; Title = 'DSOptimizer 4.0.0' })
            Leftover = [pscustomobject]@{ Errors = @() }; Detections = $script:OkDetections }
        $S = Get-E5StartupSummary -Data $Data -ExpectedRuns 3
        $S.Valid | Should -BeTrue
        $S.FirstMs | Should -Be 9100
        $S.MedianMs | Should -Be 4100
        $S.FirstSeconds | Should -Be 9.1
        $S.MedianSeconds | Should -Be 4.1
        @($S.WindowMs) | Should -Be @(9100, 4100, 3900)
        @($S.WindowTitles) | Should -Be @('DSOptimizer 4.0.0')
    }
    It 'Get-E5StartupSummary: przebieg bez okna w limicie, mniej przebiegów, sonda nie jako dso albo z błędem -> problemy' {
        $Data = [pscustomobject]@{ ProbeAsDso = $false; ProbeError = $null; Error = $null; Runs = @([pscustomobject]@{ Index = 1; WindowMs = $null; TimedOut = $true }, [pscustomobject]@{ Index = 2; WindowMs = 4000 }); Detections = $script:OkDetections }
        $S = Get-E5StartupSummary -Data $Data -ExpectedRuns 3
        $S.Valid | Should -BeFalse
        ($S.Problems -join ' | ') | Should -Match 'start 1: okno główne nie pojawiło się'
        ($S.Problems -join ' | ') | Should -Match 'przebiegów 2 zamiast 3'
        ($S.Problems -join ' | ') | Should -Match 'nie działała jako dso'
        $S.MedianMs | Should -Be 4000
        (Get-E5StartupSummary -Data ([pscustomobject]@{ ProbeError = 'Sonda startup w sesji dso nie zapisała wyniku'; Runs = @(); Detections = $script:OkDetections }) -ExpectedRuns 3).Problems -join ' ' | Should -Match 'nie zapisała wyniku'
    }
    It 'Get-E5MotwSummary: co się pokazało, MotW pliku uruchomionego, zrzut; błąd zrzutu albo ZoneId inne niż 3 = problem' {
        $Entry = [ordered]@{
            start = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Variant = 'SingleExe'; Launched = $true; LaunchPath = 'C:\Users\dso\Desktop\DSOptimizer-4.0.0.exe'; CopiedZone = [pscustomobject]@{ ZoneId = 3 }; LaunchZone = [pscustomobject]@{ Exists = $true; ZoneId = 3 }; Error = $null } }
            screenshot = 'C:\w\motw.png'; screenshotError = $null
            stop = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Shown = @('SmartScreen'); ProbeAsDso = $true; ProbeError = $null; Detections = $script:OkDetections; Error = $null } }
        }
        $S = Get-E5MotwSummary -Entry $Entry -ZoneId 3
        $S.Valid | Should -BeTrue
        @($S.Shown) | Should -Be @('SmartScreen')
        $S.LaunchZoneId | Should -Be 3
        $S.Screenshot | Should -Be 'C:\w\motw.png'
        $Entry.screenshotError = 'WMI niedostępne'
        $Entry.start.data.CopiedZone.ZoneId = 2
        $S2 = Get-E5MotwSummary -Entry $Entry -ZoneId 3
        $S2.Valid | Should -BeFalse
        ($S2.Problems -join ' | ') | Should -Match 'WMI niedostępne'
        ($S2.Problems -join ' | ') | Should -Match 'ZoneId=2 zamiast 3'
    }
    It 'Get-E5MotwSummary: „nic” (ani SmartScreen, ani UAC, ani programu) przy programie z requireAdministrator = problem; brak odczytu MotW pliku uruchomionego = problem' {
        $Entry = [ordered]@{
            start = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Variant = 'Folder'; Launched = $true; LaunchPath = 'C:\Users\dso\Desktop\x\DSOptimizer\DSOptimizer.exe'; CopiedZone = [pscustomobject]@{ ZoneId = 3 }; LaunchZone = $null; Error = $null } }
            screenshot = 'C:\w\motw.png'; screenshotError = $null
            stop = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Shown = @('nic'); ProbeAsDso = $true; ProbeError = $null; Detections = $script:OkDetections; Error = $null } }
        }
        $S = Get-E5MotwSummary -Entry $Entry -ZoneId 3
        $S.Valid | Should -BeFalse
        @($S.Shown) | Should -Be @('nic')
        ($S.Problems -join ' | ') | Should -Match 'nie pokazał ani SmartScreen, ani UAC, ani programu'
        ($S.Problems -join ' | ') | Should -Match 'brak odczytu Zone.Identifier pliku uruchomionego'
    }
    It 'Get-E5DpiSummary: okno przy DPI 144 = zweryfikowane; DPI 96 (skala się nie przyjęła) = niezweryfikowane z powodem, ale bez problemu (K7)' {
        $Record = [ordered]@{ phase = 'dpi'; set = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Error = $null } }; restartError = $null
            start = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Error = $null; Gui = [pscustomobject]@{ Found = $true; Title = 'DSOptimizer 4.0.0'; Dpi = 144; LogPixels = 144; ProbeAsDso = $true; ProbeError = $null; Error = $null; ScreenWidth = 1024; ScreenHeight = 768; Bounds = [pscustomobject]@{ Width = 1024; Height = 728 } } } }
            screenshot = 'C:\w\dpi-150.png'; screenshotError = $null; stop = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Error = $null; Detections = $script:OkDetections } } }
        $S = Get-E5DpiSummary -Record $Record -LogPixels 144
        $S.Status | Should -Be 'zweryfikowane'
        $S.Valid | Should -BeTrue
        $S.Reason | Should -BeNullOrEmpty
        $S.Screen | Should -Be '1024x768'
        $S.WindowTitle | Should -BeExactly 'DSOptimizer 4.0.0'
        $Record.start.data.Gui.Dpi = 96
        $S2 = Get-E5DpiSummary -Record $Record -LogPixels 144
        $S2.Status | Should -Be 'niezweryfikowane'
        $S2.Reason | Should -Match 'DPI ekranu 96 zamiast 144'
        $S2.Valid | Should -BeTrue
    }
    It 'Get-E5DpiSummary: nieudany restart albo brak okna = niezweryfikowane z powodem i problem (kod 1)' {
        $Record = [ordered]@{ phase = 'dpi'; set = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Error = $null } }; restartError = 'restart VM: brak heartbeatu'; start = $null; screenshot = $null; screenshotError = $null; stop = $null }
        $S = Get-E5DpiSummary -Record $Record -LogPixels 144
        $S.Status | Should -Be 'niezweryfikowane'
        $S.Reason | Should -Match 'brak heartbeatu'
        $S.Valid | Should -BeFalse
        $Record2 = [ordered]@{ phase = 'dpi'; set = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Error = $null } }; restartError = $null
            start = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Gui = [pscustomobject]@{ Found = $false; Dpi = 144; ProbeAsDso = $true } } }; screenshot = 'x.png'; screenshotError = $null
            stop = [pscustomobject]@{ error = $null; data = [pscustomobject]@{ Detections = $script:OkDetections } } }
        $S2 = Get-E5DpiSummary -Record $Record2 -LogPixels 144
        $S2.Valid | Should -BeFalse
        $S2.Status | Should -Be 'niezweryfikowane'
        ($S2.Problems -join ' ') | Should -Match 'okno główne nie pojawiło się przy 150%'
    }
}

Describe 'Task 6: nośnik VHDX na hoście (mocki dysku; nic nie jest formatowane naprawdę)' {
    BeforeEach {
        Set-NoLiveVmDefaultMock
        Mock Assert-DsoVmLiveAllowed { }
        $script:Order = [System.Collections.Generic.List[string]]::new()
        $script:MountedNumber = 7
        $script:Vhd = Join-Path $TestDrive ('DSO-26H2-Pro\usb-SingleExe-' + [guid]::NewGuid().ToString('N').Substring(0, 6) + '.vhdx')
        $script:DiskLocation = $script:Vhd
        Mock New-VHD { $script:Order.Add('new-vhd'); [pscustomobject]@{ Path = $Path } }
        Mock Mount-VHD { $script:Order.Add('mount'); [pscustomobject]@{ Path = $Path; DiskNumber = $script:MountedNumber } }
        # Get-Disk odpowiada tylko na pytanie o numer z Mount-VHD (inne wywołanie = brak pasującego mocka = wyjątek).
        Mock Get-Disk -ParameterFilter { $Number -eq 7 } {
            $Disk = [pscustomobject]@{ Number = 7; BusType = 'File Backed Virtual'; Location = $script:DiskLocation; PartitionStyle = 'RAW'; IsBoot = $false; IsSystem = $false; Size = 1073741824; Tag = 'zweryfikowany' }
            # Typ jak MSFT_Disk z CIM (PSTypeName parametru InputObject w Initialize-Disk/New-Partition) – potok przyjmuje tylko taki obiekt.
            $Disk.PSObject.TypeNames.Insert(0, 'Microsoft.Management.Infrastructure.CimInstance#MSFT_Disk')
            $Disk
        }
        Mock Initialize-Disk -RemoveParameterType 'InputObject' { $script:Order.Add("init $(@($InputObject)[0].Number) $(@($InputObject)[0].Tag) $PartitionStyle $($PSBoundParameters.ContainsKey('Number'))") }
        Mock New-Partition -RemoveParameterType 'InputObject' { $script:Order.Add("partition $(@($InputObject)[0].Number) $(@($InputObject)[0].Tag) $($PSBoundParameters.ContainsKey('DiskNumber'))"); [pscustomobject]@{ DiskNumber = 7; PartitionNumber = 1 } }
        Mock Format-Volume -RemoveParameterType 'Partition' { $script:Order.Add("format $FileSystem $NewFileSystemLabel") }
        Mock Get-Partition { [pscustomobject]@{ DriveLetter = 'X' } }
        Mock Copy-E5ArtifactToUsb { $script:Order.Add("copy $Root"); @([pscustomobject]@{ Path = 'Narzędzia DSO\DSOptimizer-4.0.0.exe'; Length = 5 }) }
        Mock Dismount-VHD { $script:Order.Add('dismount') }
    }
    It 'New-E5UsbDisk: New-VHD -> Mount-VHD -> bezpiecznik dysku -> MBR, partycja, exFAT z etykietą DSO-E5 -> kopia wydania -> Dismount-VHD' {
        $Artifact = [ordered]@{ Variant = 'SingleExe'; Name = 'DSOptimizer-4.0.0.exe'; ExeRelativePath = 'DSOptimizer-4.0.0.exe'; Path = 'C:\w\DSOptimizer-4.0.0.exe' }
        $R = New-E5UsbDisk -VhdPath $script:Vhd -Artifact $Artifact -Plan (Get-E5Plan)
        @($script:Order) | Should -Be @('new-vhd', 'mount', 'init 7 zweryfikowany MBR False', 'partition 7 zweryfikowany False', 'format exFAT DSO-E5', 'copy X:\', 'dismount')
        Should -Invoke Get-Disk -Times 1 -Exactly -ParameterFilter { $Number -eq 7 }
        $R.Path | Should -Be $script:Vhd
        $R.Folder | Should -BeExactly 'Narzędzia DSO'
        @($R.Files)[0].Path | Should -BeExactly 'Narzędzia DSO\DSOptimizer-4.0.0.exe'
        Should -Invoke New-VHD -Times 1 -Exactly -ParameterFilter { $Dynamic -and $SizeBytes -eq 1073741824 }
    }
    It 'New-E5UsbDisk: podłączony dysk to nie ten VHDX (inna lokalizacja) -> Initialize-Disk i Format-Volume NIE są wołane, Dismount-VHD i tak' {
        $script:DiskLocation = 'PCIROOT(0)#PCI(0100)#NVME(P00T00L00)'
        $Artifact = [ordered]@{ Variant = 'SingleExe'; Name = 'DSOptimizer-4.0.0.exe'; ExeRelativePath = 'DSOptimizer-4.0.0.exe'; Path = 'C:\w\DSOptimizer-4.0.0.exe' }
        { New-E5UsbDisk -VhdPath $script:Vhd -Artifact $Artifact -Plan (Get-E5Plan) } | Should -Throw '*nic nie zostało sformatowane*'
        Should -Invoke Initialize-Disk -Times 0 -Exactly
        Should -Invoke Format-Volume -Times 0 -Exactly
        @($script:Order)[-1] | Should -Be 'dismount'
    }
    It 'New-E5UsbDisk: Mount-VHD bez numeru dysku -> wyjątek przed Get-Disk; Initialize-Disk i Format-Volume NIE są wołane, Dismount-VHD i tak' {
        $script:MountedNumber = $null
        $Artifact = [ordered]@{ Variant = 'SingleExe'; Name = 'DSOptimizer-4.0.0.exe'; ExeRelativePath = 'DSOptimizer-4.0.0.exe'; Path = 'C:\w\DSOptimizer-4.0.0.exe' }
        { New-E5UsbDisk -VhdPath $script:Vhd -Artifact $Artifact -Plan (Get-E5Plan) } | Should -Throw '*numeru dysku*nic nie zostało sformatowane*'
        Should -Invoke Get-Disk -Times 0 -Exactly
        Should -Invoke Initialize-Disk -Times 0 -Exactly
        Should -Invoke New-Partition -Times 0 -Exactly
        Should -Invoke Format-Volume -Times 0 -Exactly
        @($script:Order) | Should -Be @('new-vhd', 'mount', 'dismount')
    }
    It 'New-E5UsbDisk: błąd Dismount-VHD po błędzie bezpiecznika nie przykrywa pierwotnego wyjątku (oba w komunikacie)' {
        $script:DiskLocation = 'PCIROOT(0)#PCI(0100)#NVME(P00T00L00)'
        Mock Dismount-VHD { $script:Order.Add('dismount'); throw 'odłączenie padło (atrapa)' }
        $Artifact = [ordered]@{ Variant = 'SingleExe'; Name = 'DSOptimizer-4.0.0.exe'; ExeRelativePath = 'DSOptimizer-4.0.0.exe'; Path = 'C:\w\DSOptimizer-4.0.0.exe' }
        { New-E5UsbDisk -VhdPath $script:Vhd -Artifact $Artifact -Plan (Get-E5Plan) } | Should -Throw '*nic nie zostało sformatowane*Dismount-VHD*odłączenie padło (atrapa)*'
        Should -Invoke Format-Volume -Times 0 -Exactly
        @($script:Order)[-1] | Should -Be 'dismount'
    }
    It 'New-E5UsbDisk: błąd samego Dismount-VHD po udanej kopii = wyjątek z nazwą VHDX (nie cichy sukces)' {
        Mock Dismount-VHD { $script:Order.Add('dismount'); throw 'odłączenie padło (atrapa)' }
        $Artifact = [ordered]@{ Variant = 'SingleExe'; Name = 'DSOptimizer-4.0.0.exe'; ExeRelativePath = 'DSOptimizer-4.0.0.exe'; Path = 'C:\w\DSOptimizer-4.0.0.exe' }
        { New-E5UsbDisk -VhdPath $script:Vhd -Artifact $Artifact -Plan (Get-E5Plan) } | Should -Throw '*Dismount-VHD*usb-SingleExe-*odłączenie padło (atrapa)*'
        @($script:Order)[-2..-1] | Should -Be @('copy X:\', 'dismount')
    }
    It 'New-E5UsbDisk: istniejący plik VHDX nie jest nadpisywany (nic nie tworzone ani podłączane)' {
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $script:Vhd) -Force
        [System.IO.File]::WriteAllText($script:Vhd, 'x')
        { New-E5UsbDisk -VhdPath $script:Vhd -Artifact ([ordered]@{ Variant = 'SingleExe' }) -Plan (Get-E5Plan) } | Should -Throw '*już istnieje*'
        Should -Invoke New-VHD -Times 0 -Exactly
        Should -Invoke Mount-VHD -Times 0 -Exactly
    }
    It 'Assert-E5UsbHostDisk: BusType, lokalizacja, partycje, dysk rozruchowy/systemowy albo rozmiar inne niż świeży VHDX = wyjątek' {
        $Good = @{ Number = 7; BusType = 'File Backed Virtual'; Location = 'C:\w\a.vhdx'; PartitionStyle = 'RAW'; IsBoot = $false; IsSystem = $false; Size = 1073741824 }
        { Assert-E5UsbHostDisk -Disk ([pscustomobject]$Good) -VhdPath 'C:\W\A.vhdx' -SizeBytes 1073741824 } | Should -Not -Throw
        foreach ($Change in @(@{ BusType = 'NVMe' }, @{ Location = 'C:\w\b.vhdx' }, @{ PartitionStyle = 'GPT' }, @{ IsBoot = $true }, @{ IsSystem = $true }, @{ Size = 512 })) {
            $Disk = $Good.Clone()
            foreach ($Key in $Change.Keys) { $Disk[$Key] = $Change[$Key] }
            { Assert-E5UsbHostDisk -Disk ([pscustomobject]$Disk) -VhdPath 'C:\w\a.vhdx' -SizeBytes 1073741824 } | Should -Throw -Because ($Change.Keys -join ',')
        }
    }
}

Describe 'Task 6: zawartość nośnika (pliki w TestDrive)' {
    It 'Copy-E5ArtifactToUsb: SingleExe -> Narzędzia DSO\plik wydania i wydanie\plik; Folder -> Narzędzia DSO\DSOptimizer\DSOptimizer.exe z DLL (zip wypakowany)' {
        $Root = Join-Path $TestDrive 'usb-single'
        $null = New-Item -ItemType Directory -Path $Root -Force
        $Single = Resolve-E5Artifact -Variant 'SingleExe' -ArtifactPath (New-FakeRelease -Variant SingleExe)
        $Files = @(Copy-E5ArtifactToUsb -Root $Root -Artifact $Single -Plan (Get-E5Plan))
        @($Files | ForEach-Object { $_.Path } | Sort-Object) | Should -Be @('Narzędzia DSO\DSOptimizer-4.0.0.exe', 'wydanie\DSOptimizer-4.0.0.exe')
        $RootFolder = Join-Path $TestDrive 'usb-folder'
        $null = New-Item -ItemType Directory -Path $RootFolder -Force
        $Folder = Resolve-E5Artifact -Variant 'Folder' -ArtifactPath (New-FakeRelease -Variant Folder)
        $Files2 = @(Copy-E5ArtifactToUsb -Root $RootFolder -Artifact $Folder -Plan (Get-E5Plan))
        @($Files2 | ForEach-Object { $_.Path } | Sort-Object) | Should -Be @('Narzędzia DSO\DSOptimizer\DSOptimizer.exe', 'Narzędzia DSO\DSOptimizer\Wpf.Ui.dll', 'wydanie\DSOptimizer-4.0.0.zip')
    }
}

Describe 'Orkiestracja hosta (mocki Hyper-V)' -Skip:(-not $script:HasHyperV) {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        $script:StopStates = [System.Collections.Generic.List[object]]::new()
        $script:SignaturesOk = $true
        $script:FailOn = $null
        $script:DetectionErrors = @{}
        $script:StartBlocked = @{}
        Set-NoLiveVmDefaultMock
        Mock Assert-DsoVmLiveAllowed { }
        Mock Get-DsoVmCredential { [pscredential]::new('Administrator', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock Restore-VmCleanState { $script:Calls.Add("revert $VmName $CheckpointName") }
        Mock Start-VM { $script:Calls.Add("start $Name") }
        Mock Wait-VmReady { $script:Calls.Add('ready'); $null }
        Mock Invoke-Command -RemoveParameterValidation 'Session' { 'S-1-5-21-1-1001' }
        Mock Start-Sleep { }
        Mock Get-E5RemoteScript { 'function X {}' }
        Mock Invoke-E5VmPhase -RemoveParameterValidation 'Session' {
            $Arg = $ArgumentJson | ConvertFrom-Json
            $Variant = if ($null -ne $Arg -and $null -ne $Arg.PSObject.Properties['Variant']) { " $($Arg.Variant)" } else { '' }
            $script:Calls.Add("phase $Phase$Variant")
            if ($script:FailOn -eq "$Phase$Variant") { throw 'sesja padła' }
            switch ($Phase) {
                'defender' { [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ SignaturesUpdated = $script:SignaturesOk; Error = $(if ($script:SignaturesOk) { $null } else { 'Update-MpSignature: brak połączenia' }) } } }
                'winutil-start' { [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ Variant = $Arg.Variant; Since = '2026-10-26T10:00:00.0000000+02:00'; RootPid = 100; TaskName = "DSO-E5-WinUtil-$($Arg.Variant)"; Started = ($null -eq $script:StartBlocked[$Arg.Variant]); Blocked = $script:StartBlocked[$Arg.Variant]; AliveAfterWait = $true; WinUtilVisible = $true; WinUtilWindowTitle = 'Winutil'; ProbeSid = 'S-1-5-21-1-1001'; ProbeAsDso = $true; ProbeError = $null; Tree = @([pscustomobject]@{ ProcessId = 100 }) } } }
                'winutil-stop' { $script:StopStates.Add($Arg); [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ Variant = $Arg.Variant; Detections = [pscustomobject]@{ ThreatCount = 0; EventCount = 0; Any = $false; Errors = @($script:DetectionErrors[$Arg.Variant] | Where-Object { $_ }) } } } }
                'startup' { [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ ProbeAsDso = $true; ProbeError = $null; Error = $null; Runs = @([pscustomobject]@{ Index = 1; WindowMs = 9000 }, [pscustomobject]@{ Index = 2; WindowMs = 4000 }, [pscustomobject]@{ Index = 3; WindowMs = 4200 }); Leftover = [pscustomobject]@{ Errors = @() }; Detections = $script:Ok } } }
                'av' { [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ Error = $null; Steps = @([pscustomobject]@{ Name = '01-apply-advertising-id'; Kind = 'cli'; ExpectedExit = 0; Error = $null; Result = [pscustomobject]@{ ExitCode = 0; Finished = $true; TimedOut = $false; Report = [pscustomobject]@{ outcome = 'Applied' } } }); Detections = $script:Ok; Logs = [pscustomobject]@{ UsbLogExists = $true } } } }
                'motw-start' { [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ Since = '2026-10-26T12:00:00.0000000+01:00'; Variant = 'SingleExe'; Launched = $true; LaunchPath = 'C:\Users\dso\Desktop\DSOptimizer-4.0.0.exe'; TaskName = 'DSO-E5-motw'; CopiedZone = [pscustomobject]@{ ZoneId = 3 }; LaunchZone = [pscustomobject]@{ Exists = $true; ZoneId = 3 }; Error = $null } } }
                'motw-stop' { $script:MotwState = $Arg; [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ Shown = @('SmartScreen'); ProbeAsDso = $true; ProbeError = $null; Detections = $script:Ok; Error = $null } } }
                'dpi-set' { [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ Error = $null } } }
                'dpi-start' { $script:DpiSession = $Session; [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ Since = '2026-10-26T12:10:00.0000000+01:00'; Error = $null; Gui = [pscustomobject]@{ Exe = 'E:\Narzędzia DSO\DSOptimizer-4.0.0.exe'; TaskName = 'DSO-E5-gui-dpi'; Found = $true; Dpi = 144; LogPixels = 144; ProbeAsDso = $true; ProbeError = $null; Error = $null } } } }
                'dpi-stop' { $script:DpiState = $Arg; [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ Error = $null; Detections = $script:Ok } } }
            }
        }
        Mock Save-DsoVmThumbnail { $script:Calls.Add("shot $(Split-Path -Leaf $Path)"); $Path }
        $script:Ok = [pscustomobject]@{ ThreatCount = 0; EventCount = 0; Any = $false; Errors = @() }
        $script:NewSession = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject([System.Management.Automation.Runspaces.PSSession])
        Mock New-E5UsbDisk { $script:Calls.Add("usb $($Artifact.Variant)"); $script:VhdPath = $VhdPath; [pscustomobject]@{ Path = $VhdPath; Variant = $Artifact.Variant } }
        Mock Add-VMHardDiskDrive { $script:Calls.Add("attach $VMName"); $script:AttachedDisks.Add($Path) }
        Mock Copy-E5FixturesToVm -RemoveParameterValidation 'Session' { $script:Calls.Add('fixtures') }
        Mock Copy-ExploreRawFiles -RemoveParameterValidation 'Session' { $script:Calls.Add('raw'); @('C:\w\raw\av-01-apply-advertising-id.json') }
        Mock Restart-E5VmAndWait -RemoveParameterValidation 'Session' { $script:Calls.Add('restart'); $script:NewSession }
        $script:Artifact = [ordered]@{ Variant = 'SingleExe'; Path = 'C:\w\wydania\DSOptimizer-4.0.0.exe'; Name = 'DSOptimizer-4.0.0.exe'; Version = '4.0.0'; Sha256 = ('ab' * 32); ExeRelativePath = 'DSOptimizer-4.0.0.exe' }
        Mock Connect-VMNetworkAdapter { $script:Calls.Add("connect $VMName $SwitchName") }
        Mock Disconnect-VMNetworkAdapter { $script:Calls.Add("disconnect $VMName") }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Save-ExploreResult { $script:Calls.Add("save $FileName"); $script:SavedResult = $Result; "C:\w\$VmName\$FileName" }
        Mock Get-VM { $script:Calls.Add("state $Name"); [pscustomobject]@{ Name = $Name; State = 'Off' } }
        Mock Get-VMNetworkAdapter { [pscustomobject]@{ VMName = $VMName; Name = 'Network Adapter'; SwitchName = $null } }
        # Sprzątanie (Complete-ExploreVmCleanup) działa w module ExploreCommon: te same mocki w jego zasięgu.
        Set-NoLiveVmDefaultMock -ModuleName ExploreCommon
        Mock -ModuleName ExploreCommon Restore-VmCleanState { $script:Calls.Add("revert $VmName $CheckpointName") }
        Mock -ModuleName ExploreCommon Disconnect-VMNetworkAdapter { $script:Calls.Add("disconnect $VMName") }
        Mock -ModuleName ExploreCommon Get-VM { $script:Calls.Add("state $Name"); $script:FinalVm }
        Mock -ModuleName ExploreCommon Get-VMNetworkAdapter { $script:FinalAdapter }
        Mock -ModuleName ExploreCommon Get-VMHardDiskDrive { @($script:AttachedDisks | ForEach-Object { [pscustomobject]@{ VMName = $VMName; Path = $_ } }) }
        Mock -ModuleName ExploreCommon Remove-VMHardDiskDrive -RemoveParameterType 'VMHardDiskDrive' { $script:Calls.Add("detach $(Split-Path -Leaf @($VMHardDiskDrive)[0].Path)"); $null = $script:AttachedDisks.Remove(@($VMHardDiskDrive)[0].Path) }
        $script:FinalVm = [pscustomobject]@{ Name = 'x'; State = 'Off' }
        $script:FinalAdapter = [pscustomobject]@{ Name = 'Network Adapter'; SwitchName = $null }
        $script:AttachedDisks = [System.Collections.Generic.List[string]]::new()
        $script:SavedResult = $null
    }
    It 'przebieg Pro: revert -> start -> gotowość -> defender z siecią -> winutil A/B/C (start, zrzut, stop) z siecią -> revert -> odłączenie -> stan końcowy -> zapis e5.json' {
        $Path = Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender', 'winutil') 6>$null
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e5.json'
        @($script:Calls) | Should -Be @('revert DSO-26H2-Pro czysta-26H2', 'disconnect DSO-26H2-Pro', 'start DSO-26H2-Pro', 'ready',
            'connect DSO-26H2-Pro Default Switch', 'phase defender', 'disconnect DSO-26H2-Pro',
            'connect DSO-26H2-Pro Default Switch',
            'phase winutil-start A', 'shot winutil-A.png', 'phase winutil-stop A',
            'phase winutil-start B', 'shot winutil-B.png', 'phase winutil-stop B',
            'phase winutil-start C', 'shot winutil-C.png', 'phase winutil-stop C',
            'disconnect DSO-26H2-Pro',
            'revert DSO-26H2-Pro czysta-26H2', 'disconnect DSO-26H2-Pro', 'state DSO-26H2-Pro', 'save e5.json')
        $script:SavedResult.signaturesUpdated | Should -BeTrue
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        $script:SavedResult.error | Should -BeNullOrEmpty
        $script:SavedResult.userSid | Should -Be 'S-1-5-21-1-1001'
        @($script:SavedResult.phases.winutil.summary | ForEach-Object { $_.Variant }) | Should -Be @('A', 'B', 'C')
        $script:SavedResult.phases.winutil.variants['B'].screenshot | Should -Match 'winutil-B\.png$'
        # Stop dostaje ze startu znacznik czasu, PID korzenia i nazwę zadania (bez reszty dokumentu).
        $script:StopStates[0].RootPid | Should -Be 100
        $script:StopStates[0].TaskName | Should -Be 'DSO-E5-WinUtil-A'
        $script:StopStates[0].Since | Should -Be '2026-10-26T10:00:00.0000000+02:00'
        $null -eq $script:StopStates[0].PSObject.Properties['Tree'] | Should -BeTrue
    }
    It 'nieudana aktualizacja sygnatur: winutil nie startuje (i nie dostaje sieci), rekord signaturesUpdated = false, przebieg zgłasza błąd K1, revert i odłączenie w finally' {
        $script:SignaturesOk = $false
        { Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender', 'winutil') 6>$null } | Should -Throw '*sygnatur*'
        @($script:Calls | Where-Object { $_ -like 'phase winutil*' }).Count | Should -Be 0
        @($script:Calls | Where-Object { $_ -like 'connect *' }).Count | Should -Be 1
        $script:SavedResult.signaturesUpdated | Should -BeFalse
        $script:SavedResult.phases.winutil.skipped | Should -Match 'sygnatur'
        $script:SavedResult.error | Should -Match 'K1'
        $script:SavedResult.error | Should -Match 'brak połączenia'
        @($script:Calls)[-4..-1] | Should -Be @('revert DSO-26H2-Pro czysta-26H2', 'disconnect DSO-26H2-Pro', 'state DSO-26H2-Pro', 'save e5.json')
    }
    It 'faza defender rzuca (zerwana sesja): winutil nie startuje, signaturesUpdated = false, sprzątanie w finally' {
        $script:FailOn = 'defender'
        { Invoke-E5VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('defender', 'winutil') 6>$null } | Should -Throw '*sesja padła*'
        @($script:Calls | Where-Object { $_ -like 'phase winutil*' }).Count | Should -Be 0
        $script:Calls[$script:Calls.IndexOf('phase defender') + 1] | Should -Be 'disconnect DSO-26H2-Home'
        $script:SavedResult.signaturesUpdated | Should -BeFalse
        @($script:Calls)[-4..-1] | Should -Be @('revert DSO-26H2-Home czysta-26H2', 'disconnect DSO-26H2-Home', 'state DSO-26H2-Home', 'save e5.json')
    }
    It 'błąd w środku fazy winutil (wariant B): karta odłączona od razu, revert do czysta-26H2, odłączenie, sprawdzenie stanu końcowego, wynik częściowy zapisany' {
        $script:FailOn = 'winutil-start B'
        { Invoke-E5VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('defender', 'winutil') 6>$null } | Should -Throw '*DSO-26H2-Home*sesja padła*'
        $script:Calls.IndexOf('phase winutil-start B') | Should -BeGreaterThan 0
        $script:Calls[$script:Calls.IndexOf('phase winutil-start B') + 1] | Should -Be 'disconnect DSO-26H2-Home'
        $script:Calls | Should -Not -Contain 'phase winutil-start C'
        @($script:Calls)[-4..-1] | Should -Be @('revert DSO-26H2-Home czysta-26H2', 'disconnect DSO-26H2-Home', 'state DSO-26H2-Home', 'save e5.json')
        # Revert na starcie (skrypt) i w sprzątaniu (Complete-ExploreVmCleanup w ExploreCommon).
        Should -Invoke Restore-VmCleanState -Times 1 -Exactly -ParameterFilter { $CheckpointName -eq 'czysta-26H2' }
        Should -Invoke -ModuleName ExploreCommon Restore-VmCleanState -Times 1 -Exactly -ParameterFilter { $CheckpointName -eq 'czysta-26H2' }
        $script:SavedResult.phases.winutil.variants['A'].start.data.Variant | Should -Be 'A'
        $script:SavedResult.phases.winutil.variants['A'].stop.data.Variant | Should -Be 'A'
        $script:SavedResult.error | Should -Match 'faza winutil'
        $script:SavedResult.signaturesUpdated | Should -BeTrue
        $script:SavedResult.cleanup.Ok | Should -BeTrue
    }
    It 'błąd zrzutu ekranu nie przerywa wariantu (stop i tak wołany), ale wariant jest nieważny, więc przebieg kończy się błędem' {
        Mock Save-DsoVmThumbnail { throw 'WMI niedostępne' }
        { Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender', 'winutil') 6>$null } | Should -Throw '*wariant A nieważny*WMI*'
        $script:SavedResult.phases.winutil.summary[0].Valid | Should -BeFalse
        $script:SavedResult.phases.winutil.variants['A'].screenshotError | Should -Match 'WMI'
        @($script:Calls | Where-Object { $_ -like 'phase winutil-stop *' }).Count | Should -Be 3
    }
    It 'błędy odczytu detekcji w wariancie A: pozostałe warianty zmierzone, wynik zapisany, ale przebieg kończy się błędem z nazwą wariantu (kod 1)' {
        $script:DetectionErrors = @{ 'A' = 'Get-WinEvent: dziennik niedostępny' }
        { Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender', 'winutil') 6>$null } | Should -Throw '*wariant A nieważny*odczyt detekcji*'
        @($script:Calls | Where-Object { $_ -like 'phase winutil-stop *' }).Count | Should -Be 3
        $Summary = @($script:SavedResult.phases.winutil.summary)
        @($Summary | ForEach-Object { $_.Valid }) | Should -Be @($false, $true, $true)
        @($script:SavedResult.phases.winutil.invalidVariants) | Should -Be @('A')
        ($script:SavedResult.phaseErrors -join ' ') | Should -Match 'winutil: wariant A nieważny'
        $script:SavedResult.signaturesUpdated | Should -BeTrue
        @($script:Calls)[-4..-1] | Should -Be @('revert DSO-26H2-Pro czysta-26H2', 'disconnect DSO-26H2-Pro', 'state DSO-26H2-Pro', 'save e5.json')
        # Ten sam wyjątek w Invoke-E5Exploration = błąd VM, czyli kod wyjścia 1 skryptu.
        $Arguments = Get-E5RunArguments -VmName @('DSO-26H2-Pro') -Phase @('winutil') -OutDirectory $TestDrive -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60
        $Outcome = Invoke-E5Exploration -Arguments $Arguments 6>$null
        @($Outcome.Paths).Count | Should -Be 0
        ($Outcome.Errors -join ' ') | Should -Match 'wariant A nieważny'
    }
    It 'błędy odczytu detekcji w fazie defender: sygnatury zaktualizowane (winutil idzie), ale przebieg kończy się błędem fazy' {
        Mock Invoke-E5VmPhase -RemoveParameterValidation 'Session' -ParameterFilter { $Phase -eq 'defender' } {
            $script:Calls.Add('phase defender')
            [pscustomobject]@{ phase = $Phase; error = $null; data = [pscustomobject]@{ SignaturesUpdated = $true; Error = $null; Detections = [pscustomobject]@{ Count = 0; Errors = @('Get-MpThreat: 0x80070005') } } }
        }
        { Invoke-E5VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('defender', 'winutil') 6>$null } | Should -Throw '*defender: odczyt detekcji*'
        $script:SavedResult.signaturesUpdated | Should -BeTrue
        $script:Calls | Should -Contain 'phase winutil-start A'
    }
    It 'wariant B zablokowany przez Defendera przy pobraniu (symulacja): wariant ważny, blokada zapisana, przebieg bez błędu' {
        $script:StartBlocked = @{ 'B' = 'Defender zablokował plik winutil.ps1 (etap download): 0x800700E1' }
        $Path = Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender', 'winutil') 6>$null
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e5.json'
        $B = @($script:SavedResult.phases.winutil.summary)[1]
        $B.Valid | Should -BeTrue
        $B.Blocked | Should -Match 'Defender zablokował'
        $script:SavedResult.error | Should -BeNullOrEmpty
        @($script:SavedResult.phaseErrors).Count | Should -Be 0
    }
    It 'stan końcowy niezgodny z kontraktem (VM nie Off, karta na przełączniku): zapisany i zgłoszony' {
        $script:FinalVm = [pscustomobject]@{ Name = 'DSO-26H2-Home'; State = 'Running' }
        $script:FinalAdapter = [pscustomobject]@{ Name = 'Network Adapter'; SwitchName = 'Default Switch' }
        { Invoke-E5VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('defender') 3>$null 6>$null } | Should -Throw '*stan końcowy*'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Running'
        ($script:SavedResult.cleanup.Violations -join ' ') | Should -Match 'Default Switch'
    }
    It 'błąd uwierzytelnienia: przekazany dalej, revert wykonany; Invoke-E5Exploration zatrzymuje całość po pierwszej VM' {
        Mock Wait-VmReady { $E = [System.InvalidOperationException]::new('błąd uwierzytelnienia PowerShell Direct'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender') } | Should -Throw '*uwierzytelnienia*'
        $script:Calls | Should -Contain 'revert DSO-26H2-Pro czysta-26H2'
        $Arguments = Get-E5RunArguments -VmName @('DSO-26H2-Pro', 'DSO-26H2-Home') -Phase @('defender') -OutDirectory 'C:\w' -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60
        Mock Invoke-E5VmRun { $E = [System.InvalidOperationException]::new('x'); $E.Data['VmAuthFailure'] = $true; throw $E }
        { Invoke-E5Exploration -Arguments $Arguments 6>$null } | Should -Throw
        Should -Invoke Invoke-E5VmRun -Times 1 -Exactly -Scope It
    }
    It 'Task 6 – pełny przebieg Pro SingleExe: VHDX -> revert -> karta odłączona -> VHDX podłączony -> start -> fixtures -> fazy z siecią (startup, av, motw) i dpi z restartem -> surowe pliki -> revert, odłączenie VHDX i karty, stan końcowy -> zapis' {
        $Path = Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender', 'startup', 'av', 'motw', 'dpi') -Artifact $script:Artifact 6>$null
        $Path | Should -Be 'C:\w\DSO-26H2-Pro\e5.json'
        @($script:Calls) | Should -Be @('usb SingleExe', 'revert DSO-26H2-Pro czysta-26H2', 'disconnect DSO-26H2-Pro', 'attach DSO-26H2-Pro', 'start DSO-26H2-Pro', 'ready', 'fixtures',
            'connect DSO-26H2-Pro Default Switch', 'phase defender', 'disconnect DSO-26H2-Pro',
            'connect DSO-26H2-Pro Default Switch', 'phase startup', 'disconnect DSO-26H2-Pro',
            'connect DSO-26H2-Pro Default Switch', 'phase av', 'disconnect DSO-26H2-Pro',
            'connect DSO-26H2-Pro Default Switch', 'phase motw-start', 'shot motw.png', 'phase motw-stop', 'disconnect DSO-26H2-Pro',
            'phase dpi-set', 'restart', 'phase dpi-start', 'shot dpi-150.png', 'phase dpi-stop',
            'raw', 'revert DSO-26H2-Pro czysta-26H2', "detach $(Split-Path -Leaf $script:VhdPath)", 'disconnect DSO-26H2-Pro', 'state DSO-26H2-Pro', 'save e5.json')
        $script:VhdPath | Should -Match ([regex]::Escape((Join-Path $TestDrive 'DSO-26H2-Pro')) + '\\usb-SingleExe-\d{8}-\d{6}\.vhdx$')
        Should -Invoke Add-VMHardDiskDrive -Times 1 -Exactly -ParameterFilter { $Path -eq $script:VhdPath }
        $script:SavedResult.error | Should -BeNullOrEmpty
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        @($script:SavedResult.cleanup.AttachedDisks).Count | Should -Be 0
        $script:SavedResult.artifact.Sha256 | Should -Be ('ab' * 32)
        $script:SavedResult.phases.startup.summary.MedianMs | Should -Be 4200
        $script:SavedResult.phases.av.summary.Valid | Should -BeTrue
        @($script:SavedResult.phases.motw.summary.Shown) | Should -Be @('SmartScreen')
        $script:SavedResult.phases.dpi.summary.Status | Should -Be 'zweryfikowane'
        @($script:SavedResult.rawFiles) | Should -Be @('av-01-apply-advertising-id.json')
        # Stop motw dostaje znacznik czasu, ścieżkę uruchomioną i zadanie; po restarcie (dpi) dalsze kroki idą nową sesją.
        $script:MotwState.LaunchPath | Should -Be 'C:\Users\dso\Desktop\DSOptimizer-4.0.0.exe'
        $script:MotwState.TaskName | Should -Be 'DSO-E5-motw'
        [object]::ReferenceEquals($script:DpiSession, $script:NewSession) | Should -BeTrue
        $script:DpiState.Gui.TaskName | Should -Be 'DSO-E5-gui-dpi'
        Should -Invoke Copy-ExploreRawFiles -Times 1 -Exactly -ParameterFilter { [object]::ReferenceEquals($Session, $script:NewSession) -and $Source -eq 'C:\dso\e5\raw' }
        # Plan w VM zna artefakt (nazwa i ścieżka exe na nośniku).
        Should -Invoke Invoke-E5VmPhase -ParameterFilter { $Phase -eq 'av' -and ($PlanJson | ConvertFrom-Json).Artifact.ExeRelativePath -eq 'DSOptimizer-4.0.0.exe' }
    }
    It 'Task 6 – błąd w fazie av (zerwana sesja): karta odłączona od razu, kolejne fazy próbowane jak dotąd; finally: surowe pliki, revert, VHDX odłączony od VM, karta, stan końcowy, wynik zapisany' {
        $script:FailOn = 'av'
        { Invoke-E5VmRun -VmName 'DSO-26H2-Home' -OutDirectory $TestDrive -Phases @('defender', 'startup', 'av', 'motw', 'dpi') -Artifact $script:Artifact 3>$null 6>$null } | Should -Throw '*DSO-26H2-Home*faza av*sesja padła*'
        $script:Calls[$script:Calls.IndexOf('phase av') + 1] | Should -Be 'disconnect DSO-26H2-Home'
        $script:Calls | Should -Contain 'phase motw-start'
        @($script:Calls)[-6..-1] | Should -Be @('raw', 'revert DSO-26H2-Home czysta-26H2', "detach $(Split-Path -Leaf $script:VhdPath)", 'disconnect DSO-26H2-Home', 'state DSO-26H2-Home', 'save e5.json')
        Should -Invoke -ModuleName ExploreCommon Remove-VMHardDiskDrive -Times 1 -Exactly
        $script:SavedResult.cleanup.Ok | Should -BeTrue
        @($script:SavedResult.cleanup.AttachedDisks).Count | Should -Be 0
        $script:SavedResult.error | Should -Match 'faza av'
        $script:SavedResult.phases.startup.summary.Valid | Should -BeTrue
    }
    It 'Task 6 – VHDX, którego nie da się odłączyć od VM: naruszenie kontraktu stanu końcowego zapisane i zgłoszone (kod 1)' {
        Mock -ModuleName ExploreCommon Remove-VMHardDiskDrive -RemoveParameterType 'VMHardDiskDrive' { throw 'dysk w użyciu' }
        { Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender', 'startup') -Artifact $script:Artifact 3>$null 6>$null } | Should -Throw '*nadal podłączony*'
        $script:SavedResult.cleanup.Ok | Should -BeFalse
        @($script:SavedResult.cleanup.AttachedDisks) | Should -Be @($script:VhdPath)
        $script:SavedResult.cleanup.DetachError | Should -Match 'w użyciu'
    }
    It 'Task 6 – fazy z nośnikiem bez artefaktu: wyjątek, zanim cokolwiek dotknie VM albo hosta (bez VHDX, revertu i poświadczeń)' {
        { Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender', 'av') 6>$null } | Should -Throw '*artefaktu*nie została ruszona*'
        @($script:Calls).Count | Should -Be 0
        Should -Invoke Get-DsoVmCredential -Times 0 -Exactly
        Should -Invoke New-E5UsbDisk -Times 0 -Exactly
        Should -Invoke Restore-VmCleanState -Times 0 -Exactly
    }
    It 'Task 6 – nieudana aktualizacja sygnatur: startup, av, motw i dpi pominięte (K1), VHDX i tak odłączony w sprzątaniu' {
        $script:SignaturesOk = $false
        { Invoke-E5VmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Phases @('defender', 'startup', 'av', 'motw', 'dpi') -Artifact $script:Artifact 6>$null } | Should -Throw '*K1*'
        @($script:Calls | Where-Object { $_ -like 'phase *' }) | Should -Be @('phase defender')
        foreach ($Phase in 'startup', 'av', 'motw', 'dpi') { $script:SavedResult.phases[$Phase].skipped | Should -Match 'K1' }
        $script:Calls | Should -Contain "detach $(Split-Path -Leaf $script:VhdPath)"
    }
    It 'Invoke-E5Exploration: inny błąd jednej VM zapisany, następna VM idzie' {
        $script:Runs = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-E5VmRun { $script:Runs.Add("$VmName|$(@($Phases) -join ',')"); if ($VmName -eq 'DSO-26H2-Pro') { throw 'Pro padła' }; "C:\w\$VmName\e5.json" }
        $Arguments = Get-E5RunArguments -VmName @('DSO-26H2-Pro', 'DSO-26H2-Home') -Phase @('winutil') -OutDirectory 'C:\w' -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 60
        $Outcome = Invoke-E5Exploration -Arguments $Arguments 6>$null
        @($script:Runs) | Should -Be @('DSO-26H2-Pro|defender,winutil', 'DSO-26H2-Home|defender,winutil')
        @($Outcome.Paths) | Should -Be @('C:\w\DSO-26H2-Home\e5.json')
        ($Outcome.Errors -join ' ') | Should -Match 'Pro padła'
    }
}

Describe 'Blok główny SKRYPTU (-ShowPlan; bez VM)' {
    It '-ShowPlan: kod 0, defender pierwsza, nic nie dotyka VM ani katalogu wyników; bez -OutDirectory kod 1' {
        $env:DSO_VM_TESTS_MOCK_ONLY | Should -Be '1'
        Mock Restore-VmCleanState { throw 'nie wolno' }
        Mock Get-DsoVmCredential { throw 'nie wolno' }
        Mock Wait-VmReady { throw 'nie wolno' }
        $Output = & $script:E5ScriptPath -ShowPlan -VmName DSO-26H2-Pro -Phase winutil -OutDirectory (Join-Path $TestDrive 'w') 6>$null
        $LASTEXITCODE | Should -Be 0
        $Plan = ($Output | Out-String) | ConvertFrom-Json
        @($Plan.VmName) | Should -Be @('DSO-26H2-Pro')
        @($Plan.Phases.'DSO-26H2-Pro') | Should -Be @('defender', 'winutil')
        $Plan.Plan.WinUtilCommand | Should -BeExactly '-NoExit -Command "irm https://christitus.com/win | iex"'
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        Should -Invoke Restore-VmCleanState -Times 0 -Exactly
        Should -Invoke Get-DsoVmCredential -Times 0 -Exactly
        Should -Invoke Wait-VmReady -Times 0 -Exactly
        & $script:E5ScriptPath -ShowPlan 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
    It '-ShowPlan: faza z nośnikiem bez -Variant/-ArtifactPath albo -Variant bez -ArtifactPath = kod 1, nic nie dotknięte' {
        Mock Restore-VmCleanState { throw 'nie wolno' }
        Mock Get-DsoVmCredential { throw 'nie wolno' }
        & $script:E5ScriptPath -ShowPlan -VmName DSO-26H2-Pro -Phase av -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
        & $script:E5ScriptPath -ShowPlan -VmName DSO-26H2-Pro -Phase defender -Variant SingleExe -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
        & $script:E5ScriptPath -ShowPlan -VmName DSO-26H2-Pro -Phase dpi -Variant Folder -ArtifactPath (Join-Path $TestDrive 'brak\DSOptimizer-4.0.0.zip') -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-Null
        $LASTEXITCODE | Should -Be 1
        Test-Path -LiteralPath (Join-Path $TestDrive 'w') | Should -BeFalse
        Should -Invoke Restore-VmCleanState -Times 0 -Exactly
        Should -Invoke Get-DsoVmCredential -Times 0 -Exactly
    }
    It '-ShowPlan z artefaktem: kod 0, kolejność faz defender, startup, av, motw, dpi; artefakt (wariant, ścieżka exe na nośniku, SHA256) w planie' {
        $Release = New-FakeRelease -Variant Folder
        $Plan = (& $script:E5ScriptPath -ShowPlan -VmName DSO-26H2-Home -Phase defender, av, motw, startup, dpi -Variant Folder -ArtifactPath $Release -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-String) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        @($Plan.Phases.'DSO-26H2-Home') | Should -Be @('defender', 'startup', 'av', 'motw', 'dpi')
        $Plan.Artifact.Variant | Should -Be 'Folder'
        $Plan.Artifact.ExeRelativePath | Should -Be 'DSOptimizer\DSOptimizer.exe'
        $Plan.Artifact.Sha256 | Should -BeExactly (Get-FileHash -LiteralPath $Release -Algorithm SHA256).Hash.ToLowerInvariant()
        $Plan.Plan.UsbFolderName | Should -BeExactly 'Narzędzia DSO'
    }
    It '-ShowPlan bez -Phase: defender i winutil; domyślny katalog builda w planie; -BuildDirectory przekazany' {
        $All = (& $script:E5ScriptPath -ShowPlan -VmName DSO-26H2-Home -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-String) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        @($All.Phases.'DSO-26H2-Home') | Should -Be @('defender', 'winutil')
        $All.BuildDirectory | Should -Match 'src\\DSOptimizer\.App\\bin\\Release\\net481$'
        $Own = (& $script:E5ScriptPath -ShowPlan -VmName DSO-26H2-Home -BuildDirectory 'C:\build' -OutDirectory (Join-Path $TestDrive 'w') 6>$null | Out-String) | ConvertFrom-Json
        $Own.BuildDirectory | Should -Be 'C:\build'
    }
}

AfterAll { Clear-NoLiveVm }
