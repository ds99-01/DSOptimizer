BeforeAll {
    # Wyłącznik: żaden test na mockach nie może dotknąć prawdziwego Hyper-V (patrz Support\NoLiveVm.ps1).
    . (Join-Path $PSScriptRoot 'Support\NoLiveVm.ps1')
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot '..\modules\VmHost.psm1') -Force
    # Skrypt jest dot-source'owany: przy '.' nie uruchamia przebiegu na VM, tylko definiuje funkcje.
    . (Join-Path $PSScriptRoot '..\explore\Invoke-NetExploration.ps1')
    $script:NetScriptPath = Join-Path $PSScriptRoot '..\explore\Invoke-NetExploration.ps1'
    $script:NetFiles = @(
        $script:NetScriptPath
        (Join-Path $PSScriptRoot '..\explore\net\Parse-NetTrace.ps1')
        (Join-Path $PSScriptRoot '..\explore\net\NavProbe.ps1')
        $PSCommandPath
    )

    function script:New-Connection {
        param([string]$Remote = '140.82.121.6', [int]$Port = 443, [string[]]$Hosts = @('api.github.com'), [string]$Proto = 'TCP')
        return [pscustomobject]@{ pid = 100; image = 'DSOptimizer.exe'; dsoTree = $true; proto = $Proto; remote = $Remote; remotePort = $Port; hosts = $Hosts; connects = 1 }
    }
    function script:New-Query {
        param([string]$Name = 'api.github.com')
        return [pscustomobject]@{ pid = 100; image = 'DSOptimizer.exe'; dsoTree = $true; name = $Name; type = 1; queries = 1 }
    }
    function script:New-Trace {
        # Ślad jak z Parse-NetTrace.ps1: tylko pola, które werdykt czyta (eventCount i dsoTreeProcesses opcjonalne).
        param($Dns = @(), $Connections = @(), [switch]$WithMeta)
        $Trace = [ordered]@{ dsoTreeDns = @($Dns); dsoTreeConnections = @($Connections) }
        if ($WithMeta) {
            $Trace.eventCount = 5000
            $Trace.dsoTreeProcesses = @([pscustomobject]@{ pid = 100; image = 'DSOptimizer.exe' })
        }
        return [pscustomobject]$Trace
    }
}

Describe 'Get-NetVerdict: start-on' {
    It 'start-on: tylko api.github.com na 443 przechodzi' {
        $Trace = New-Trace -Dns @(New-Query) -Connections @(New-Connection) -WithMeta
        $Result = Get-NetVerdict -Scenario 'start-on' -Trace $Trace
        $Result.Passed | Should -BeTrue
        $Result.Detail | Should -Match 'api\.github\.com'
    }
    It 'start-on: brak jakiegokolwiek połączenia nie przechodzi (sprawdzanie musiało się wykonać)' {
        $Result = Get-NetVerdict -Scenario 'start-on' -Trace (New-Trace)
        $Result.Passed | Should -BeFalse
        $Result.Detail | Should -Match 'Brak jakiegokolwiek połączenia'
    }
    It 'start-on: dodatkowy host w drzewie programu nie przechodzi' {
        $Trace = New-Trace -Dns @(New-Query; New-Query -Name 'example.org') -Connections @(New-Connection; New-Connection -Remote '93.184.216.34' -Hosts @('example.org'))
        $Result = Get-NetVerdict -Scenario 'start-on' -Trace $Trace
        $Result.Passed | Should -BeFalse
        $Result.Detail | Should -Match 'example\.org'
    }
    It 'start-on: samo zapytanie DNS o dodatkowy host (bez połączenia) też nie przechodzi' {
        $Trace = New-Trace -Dns @(New-Query; New-Query -Name 'telemetria.example') -Connections @(New-Connection)
        (Get-NetVerdict -Scenario 'start-on' -Trace $Trace).Passed | Should -BeFalse
    }
    It 'start-on: api.github.com na porcie innym niż 443 nie przechodzi' {
        $Trace = New-Trace -Dns @(New-Query) -Connections @(New-Connection -Port 80)
        (Get-NetVerdict -Scenario 'start-on' -Trace $Trace).Passed | Should -BeFalse
    }
    It 'start-on: UDP do api.github.com nie przechodzi' {
        $Trace = New-Trace -Dns @(New-Query) -Connections @(New-Connection -Proto 'UDP')
        (Get-NetVerdict -Scenario 'start-on' -Trace $Trace).Passed | Should -BeFalse
    }
    It 'start-on: połączenie bez ustalonego hosta nie przechodzi' {
        $Trace = New-Trace -Dns @(New-Query) -Connections @(New-Connection; New-Connection -Remote '203.0.113.9' -Hosts @())
        $Result = Get-NetVerdict -Scenario 'start-on' -Trace $Trace
        $Result.Passed | Should -BeFalse
        $Result.Detail | Should -Match 'host nieustalony'
    }
    It 'start-on: wielkość liter i kropka na końcu nazwy nie mają znaczenia' {
        $Trace = New-Trace -Dns @(New-Query -Name 'API.GitHub.com.') -Connections @(New-Connection -Hosts @('Api.Github.Com.'))
        (Get-NetVerdict -Scenario 'start-on' -Trace $Trace).Passed | Should -BeTrue
    }
    It 'start-on: kilka połączeń do api.github.com (różne adresy) przechodzi' {
        $Trace = New-Trace -Dns @(New-Query) -Connections @(New-Connection; New-Connection -Remote '140.82.121.5')
        (Get-NetVerdict -Scenario 'start-on' -Trace $Trace).Passed | Should -BeTrue
    }
}

Describe 'Get-NetVerdict: start-off i cli' {
    It 'start-off i cli: zero DNS i zero połączeń przechodzi' {
        foreach ($Scenario in 'start-off', 'cli') {
            $Result = Get-NetVerdict -Scenario $Scenario -Trace (New-Trace -WithMeta)
            $Result.Passed | Should -BeTrue -Because $Scenario
        }
    }
    It 'start-off: jedno zapytanie DNS nie przechodzi' {
        $Result = Get-NetVerdict -Scenario 'start-off' -Trace (New-Trace -Dns @(New-Query))
        $Result.Passed | Should -BeFalse
        $Result.Detail | Should -Match 'api\.github\.com'
    }
    It 'start-off i cli: jedno połączenie, nawet do api.github.com, nie przechodzi' {
        foreach ($Scenario in 'start-off', 'cli') {
            $Result = Get-NetVerdict -Scenario $Scenario -Trace (New-Trace -Connections @(New-Connection))
            $Result.Passed | Should -BeFalse -Because $Scenario
            $Result.Detail | Should -Match '140\.82\.121\.6:443'
        }
    }
    It 'cli: zapytanie DNS bez połączenia nie przechodzi' {
        (Get-NetVerdict -Scenario 'cli' -Trace (New-Trace -Dns @(New-Query -Name 'winget.azureedge.net'))).Passed | Should -BeFalse
    }
}

Describe 'Get-NetVerdict: wiarygodność śladu' {
    It 'ślad bez zdarzeń nie przechodzi w żadnym scenariuszu (brak ruchu mógłby znaczyć, że nic nie złapano)' {
        $Trace = New-Trace -WithMeta
        $Trace.eventCount = 0
        foreach ($Scenario in 'start-on', 'start-off', 'cli') {
            (Get-NetVerdict -Scenario $Scenario -Trace $Trace).Passed | Should -BeFalse -Because $Scenario
        }
    }
    It 'ślad bez procesu programu nie przechodzi, także gdy jest zero połączeń' {
        $Trace = New-Trace -WithMeta
        $Trace.dsoTreeProcesses = @()
        (Get-NetVerdict -Scenario 'start-off' -Trace $Trace).Passed | Should -BeFalse
    }
    It 'ślad bez pól dsoTreeDns albo dsoTreeConnections nie przechodzi' {
        (Get-NetVerdict -Scenario 'cli' -Trace ([pscustomobject]@{ dsoTreeDns = @() })).Passed | Should -BeFalse
        (Get-NetVerdict -Scenario 'cli' -Trace ([pscustomobject]@{ dsoTreeConnections = @() })).Passed | Should -BeFalse
        (Get-NetVerdict -Scenario 'cli' -Trace $null).Passed | Should -BeFalse
    }
    It 'nieznany scenariusz to błąd, nie ciche przejście' {
        { Get-NetVerdict -Scenario 'inny' -Trace (New-Trace) } | Should -Throw
    }
    It 'werdykt czyta ślad po przejściu przez JSON (jak z pliku z VM), także z jednym elementem' {
        $Trace = New-Trace -Dns @(New-Query) -Connections @(New-Connection) -WithMeta | ConvertTo-Json -Depth 6 | ConvertFrom-Json
        (Get-NetVerdict -Scenario 'start-on' -Trace $Trace).Passed | Should -BeTrue
        $Empty = New-Trace -WithMeta | ConvertTo-Json -Depth 6 | ConvertFrom-Json
        (Get-NetVerdict -Scenario 'cli' -Trace $Empty).Passed | Should -BeTrue
    }
}

Describe 'Plan i argumenty' {
    It 'plan: zatrzymane usługi Windows, dozwolony host, ustawienia start-on bez pola i start-off z wyłączonym sprawdzaniem' {
        $Plan = Get-NetPlan
        @($Plan.StoppedServices) | Should -Be @('wuauserv', 'UsoSvc', 'DoSvc', 'BITS')
        $Plan.AllowedHost | Should -BeExactly 'api.github.com'
        $Plan.NetworkSwitch | Should -BeExactly 'Default Switch'
        $Plan.UserName | Should -BeExactly 'dso'
        $On = $Plan.SettingsOn | ConvertFrom-Json
        $On.schemaVersion | Should -Be 1
        $On.language | Should -Be 'pl'
        $On.PSObject.Properties.Name | Should -Not -Contain 'checkUpdatesAtStart'
        $Off = $Plan.SettingsOff | ConvertFrom-Json
        $Off.checkUpdatesAtStart | Should -BeFalse
        $Off.language | Should -Be 'pl'
        @($Plan.CliCommands) | Should -Be @('--list-install', '--list-apps')
        @($Plan.Providers) | Should -HaveCount 3
        ($Plan.Providers -join "`n") | Should -Match 'Kernel-Network'
        ($Plan.Providers -join "`n") | Should -Match 'Kernel-Process'
        ($Plan.Providers -join "`n") | Should -Match 'DNS-Client'
    }
    It 'parametry skryptu: tylko VM Home/Pro i trzy scenariusze' {
        $Parameters = (Get-Command $script:NetScriptPath).ScriptBlock.Ast.ParamBlock.Parameters
        $Set = { param($Name) $P = $Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq $Name }; @($P.Attributes | Where-Object { $_.TypeName.Name -eq 'ValidateSet' } | ForEach-Object { $_.PositionalArguments.Value }) }
        & $Set 'VmName' | Should -Be @('DSO-26H2-Home', 'DSO-26H2-Pro')
        & $Set 'Scenario' | Should -Be @('start-on', 'start-off', 'cli')
    }
    It 'Resolve-NetArtifact: zip z SHA256SUMS.txt i DSOptimizer\DSOptimizer.exe przechodzi, zła suma, brak exe i zła nazwa to wyjątek' {
        $Dir = Join-Path $TestDrive 'wydanie'
        $Source = Join-Path $Dir 'src\DSOptimizer'
        $null = New-Item -ItemType Directory -Path $Source -Force
        [System.IO.File]::WriteAllBytes((Join-Path $Source 'DSOptimizer.exe'), [byte[]](77, 90, 1))
        $Zip = Join-Path $Dir 'DSOptimizer-4.0.3.zip'
        Compress-Archive -Path $Source -DestinationPath $Zip
        $Sha = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
        [System.IO.File]::WriteAllText((Join-Path $Dir 'SHA256SUMS.txt'), "$Sha  DSOptimizer-4.0.3.zip`n")
        $Artifact = Resolve-NetArtifact -ArtifactPath $Zip
        $Artifact.Version | Should -Be '4.0.3'
        $Artifact.Sha256 | Should -Be $Sha
        [System.IO.File]::WriteAllText((Join-Path $Dir 'SHA256SUMS.txt'), ('0' * 64) + "  DSOptimizer-4.0.3.zip`n")
        { Resolve-NetArtifact -ArtifactPath $Zip } | Should -Throw '*różni się*'
        { Resolve-NetArtifact -ArtifactPath '' } | Should -Throw '*-ArtifactPath*'
        { Resolve-NetArtifact -ArtifactPath 'wydanie.zip' } | Should -Throw '*bezwzględn*'
        $Other = Join-Path $Dir 'inne.zip'
        Copy-Item -LiteralPath $Zip -Destination $Other
        { Resolve-NetArtifact -ArtifactPath $Other } | Should -Throw '*nie jest wydaniem*'
    }
    It 'Get-NetRunArguments: scenariusze w stałej kolejności bez duplikatów; pusta lista to wyjątek' {
        $Dir = Join-Path $TestDrive 'wydanie2'
        $Source = Join-Path $Dir 'src\DSOptimizer'
        $null = New-Item -ItemType Directory -Path $Source -Force
        [System.IO.File]::WriteAllBytes((Join-Path $Source 'DSOptimizer.exe'), [byte[]](77, 90, 2))
        $Zip = Join-Path $Dir 'DSOptimizer-4.0.3.zip'
        Compress-Archive -Path $Source -DestinationPath $Zip
        $Sha = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
        [System.IO.File]::WriteAllText((Join-Path $Dir 'SHA256SUMS.txt'), "$Sha  DSOptimizer-4.0.3.zip`n")
        $Arguments = Get-NetRunArguments -VmName 'DSO-26H2-Pro' -Scenario @('cli', 'start-on', 'cli') -OutDirectory 'C:\x' -CheckpointName 'czysta-26H2' -ReadyTimeoutSeconds 600 -ArtifactPath $Zip
        @($Arguments.Scenarios) | Should -Be @('start-on', 'cli')
        { Get-NetRunArguments -VmName 'DSO-26H2-Pro' -Scenario @() -OutDirectory 'C:\x' -CheckpointName 'c' -ReadyTimeoutSeconds 600 -ArtifactPath $Zip } | Should -Throw
    }
    It 'Invoke-NetVmRun odmawia pracy w trybie testów na mockach, zanim dotknie VM' {
        { Invoke-NetVmRun -VmName 'DSO-26H2-Pro' -OutDirectory (Join-Path $TestDrive 'out') -Scenarios @('cli') -Artifact ([ordered]@{}) } | Should -Throw '*DSO_VM_TESTS_MOCK_ONLY*'
    }
}

Describe 'Pliki narzędzia' {
    It 'parsują się bez błędów (PowerShell AST)' {
        foreach ($File in $script:NetFiles) {
            $Errors = $null
            $null = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $File).Path, [ref]$null, [ref]$Errors)
            @($Errors) | Should -HaveCount 0 -Because $File
        }
    }
    It 'mają BOM UTF-8; skrypty wykonywane w VM mają sekcje Instrukcja uruchomienia i Opis działania' {
        foreach ($File in $script:NetFiles) {
            $Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $File).Path)
            ($Bytes[0..2] -join ',') | Should -Be '239,187,191' -Because $File
        }
        foreach ($File in $script:NetFiles | Select-Object -First 3) {
            $Text = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $File).Path, [System.Text.Encoding]::UTF8)
            $Text | Should -Match 'Instrukcja uruchomienia' -Because $File
            $Text | Should -Match 'Opis działania' -Because $File
        }
    }
    It 'NavProbe.ps1 jest ASCII poza nagłówkiem (PowerShell 5.1 w VM) i nie klika przycisków' {
        $Text = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $script:NetFiles[2]).Path, [System.Text.Encoding]::UTF8)
        $Code = $Text.Substring($Text.IndexOf('#>') + 2)
        $Code | Should -Not -Match '[^\x00-\x7F]'
        $Code | Should -Not -Match 'InvokePattern'
    }
}

AfterAll { Clear-NoLiveVm }
