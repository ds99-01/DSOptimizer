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
        param([string]$Remote = '140.82.121.6', [int]$Port = 443, [string[]]$Hosts = @('api.github.com'), [string]$Proto = 'TCP', [bool]$InTree = $true)
        return [pscustomobject]@{ pid = 100; image = 'DSOptimizer.exe'; dsoTree = $InTree; proto = $Proto; remote = $Remote; remotePort = $Port; hosts = $Hosts; connects = 1 }
    }
    function script:New-Query {
        param([string]$Name = 'api.github.com', [bool]$InTree = $true)
        return [pscustomobject]@{ pid = 100; image = 'DSOptimizer.exe'; dsoTree = $InTree; name = $Name; type = 1; queries = 1 }
    }
    function script:New-Trace {
        # Ślad jak z Parse-NetTrace.ps1. Dns/Connections = ruch drzewa programu; domyślnie jest też metadane (eventCount, dsoTreeProcesses)
        # i kontrolny ruch spoza drzewa (pola dns i connections obejmują oba rodzaje, jak w prawdziwym śladzie).
        param($Dns = @(), $Connections = @(), [switch]$NoMeta, [switch]$NoCanary, [switch]$CanaryDnsOnly, [switch]$CanaryConnectionOnly)
        $CanaryDns = @(); $CanaryConnections = @()
        if (-not $NoCanary) {
            if (-not $CanaryConnectionOnly) { $CanaryDns = @((New-Query -Name 'example.com' -InTree $false)) }
            if (-not $CanaryDnsOnly) { $CanaryConnections = @((New-Connection -Remote '93.184.216.34' -Hosts @('example.com') -InTree $false)) }
        }
        $Trace = [ordered]@{ dsoTreeDns = @($Dns); dsoTreeConnections = @($Connections); dns = @(@($Dns) + $CanaryDns); connections = @(@($Connections) + $CanaryConnections) }
        if (-not $NoMeta) {
            $Trace.eventCount = 5000
            $Trace.dsoTreeProcesses = @([pscustomobject]@{ pid = 100; image = 'DSOptimizer.exe' })
        }
        return [pscustomobject]$Trace
    }
    function script:New-AuditEvent {
        param([int]$NewPid, [int]$ParentPid, [string]$Image)
        return [pscustomobject]@{ time = '2026-11-01T10:00:00Z'; newPid = $NewPid; image = $Image; parentPid = $ParentPid; commandLine = '' }
    }
}

Describe 'Get-NetVerdict: start-on' {
    It 'start-on: tylko api.github.com na 443 przechodzi' {
        $Trace = New-Trace -Dns @(New-Query) -Connections @(New-Connection)
        $Result = Get-NetVerdict -Scenario 'start-on' -Trace $Trace
        $Result.Passed | Should -BeTrue
        $Result.Detail | Should -Match 'api\.github\.com'
        $Result.Detail | Should -Match 'procesów programu'
    }
    It 'start-on: brak jakiegokolwiek połączenia nie przechodzi (sprawdzanie musiało się wykonać)' {
        $Result = Get-NetVerdict -Scenario 'start-on' -Trace (New-Trace)
        $Result.Passed | Should -BeFalse
        $Result.Detail | Should -Match 'Brak jakiegokolwiek połączenia'
    }
    It 'start-on: dodatkowy host w drzewie programu nie przechodzi' {
        $Trace = New-Trace -Dns @(New-Query; New-Query -Name 'example.org') -Connections @(New-Connection; New-Connection -Remote '93.184.216.35' -Hosts @('example.org'))
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
    It 'start-on: adres współdzielony z inną nazwą (host połączenia = api.github.com i inna nazwa) nie przechodzi, a Detail ją nazywa' {
        $Trace = New-Trace -Dns @(New-Query) -Connections @(New-Connection -Hosts @('api.github.com', 'inny.example'))
        $Result = Get-NetVerdict -Scenario 'start-on' -Trace $Trace
        $Result.Passed | Should -BeFalse
        $Result.Detail | Should -Match 'api\.github\.com/inny\.example'
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
            $Result = Get-NetVerdict -Scenario $Scenario -Trace (New-Trace)
            $Result.Passed | Should -BeTrue -Because $Scenario
            $Result.Detail | Should -Match 'drzewie procesów programu'
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

Describe 'Get-NetVerdict: fail-closed i wiarygodność śladu' {
    It 'ślad bez pól eventCount i dsoTreeProcesses nie przechodzi w żadnym scenariuszu (także przy zerze ruchu)' {
        $Trace = New-Trace -NoMeta
        foreach ($Scenario in 'start-on', 'start-off', 'cli') {
            $Result = Get-NetVerdict -Scenario $Scenario -Trace $Trace
            $Result.Passed | Should -BeFalse -Because $Scenario
            $Result.Detail | Should -Match 'eventCount'
        }
    }
    It 'ślad z eventCount, ale bez dsoTreeProcesses, albo odwrotnie nie przechodzi' {
        $OnlyCount = New-Trace; $OnlyCount.PSObject.Properties.Remove('dsoTreeProcesses')
        (Get-NetVerdict -Scenario 'cli' -Trace $OnlyCount).Passed | Should -BeFalse
        $OnlyProcesses = New-Trace; $OnlyProcesses.PSObject.Properties.Remove('eventCount')
        (Get-NetVerdict -Scenario 'cli' -Trace $OnlyProcesses).Passed | Should -BeFalse
    }
    It 'ślad bez zdarzeń (eventCount = 0) nie przechodzi w żadnym scenariuszu' {
        $Trace = New-Trace
        $Trace.eventCount = 0
        foreach ($Scenario in 'start-on', 'start-off', 'cli') {
            (Get-NetVerdict -Scenario $Scenario -Trace $Trace).Passed | Should -BeFalse -Because $Scenario
        }
    }
    It 'ślad bez procesu programu nie przechodzi, także gdy jest zero połączeń' {
        $Trace = New-Trace
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
        $Trace = New-Trace -Dns @(New-Query) -Connections @(New-Connection) | ConvertTo-Json -Depth 6 | ConvertFrom-Json
        (Get-NetVerdict -Scenario 'start-on' -Trace $Trace).Passed | Should -BeTrue
        $Empty = New-Trace | ConvertTo-Json -Depth 6 | ConvertFrom-Json
        (Get-NetVerdict -Scenario 'cli' -Trace $Empty).Passed | Should -BeTrue
    }
}

Describe 'Ruch kontrolny (dowód, że ślad łapie sieć)' {
    It 'ślad z ruchem kontrolnym spoza drzewa: Test-NetCanary widzi go, a werdykt przechodzi' {
        (Test-NetCanary -Trace (New-Trace)).Seen | Should -BeTrue
        (Get-NetVerdict -Scenario 'cli' -Trace (New-Trace)).Passed | Should -BeTrue
    }
    It 'brak ruchu kontrolnego w śladzie = porażka w każdym scenariuszu, nawet przy idealnym ruchu programu' {
        $Good = @{ Dns = @(New-Query); Connections = @(New-Connection) }
        (Get-NetVerdict -Scenario 'start-on' -Trace (New-Trace @Good -NoCanary)).Passed | Should -BeFalse
        (Get-NetVerdict -Scenario 'start-off' -Trace (New-Trace -NoCanary)).Passed | Should -BeFalse
        $Result = Get-NetVerdict -Scenario 'cli' -Trace (New-Trace -NoCanary)
        $Result.Passed | Should -BeFalse
        $Result.Detail | Should -Match 'kontroln'
    }
    It 'sam DNS albo samo połączenie kontrolne to za mało' {
        (Get-NetVerdict -Scenario 'cli' -Trace (New-Trace -CanaryDnsOnly)).Passed | Should -BeFalse
        (Get-NetVerdict -Scenario 'cli' -Trace (New-Trace -CanaryConnectionOnly)).Passed | Should -BeFalse
        (Test-NetCanary -Trace (New-Trace -CanaryDnsOnly)).Detail | Should -Match 'połączenia TCP'
        (Test-NetCanary -Trace (New-Trace -CanaryConnectionOnly)).Detail | Should -Match 'zapytania DNS'
    }
    It 'ruch kontrolny w drzewie programu się nie liczy (musi być spoza drzewa)' {
        $Trace = New-Trace -NoCanary
        $Trace.dns = @(New-Query -Name 'example.com' -InTree $true)
        $Trace.connections = @(New-Connection -Remote '93.184.216.34' -Hosts @('example.com') -InTree $true)
        (Test-NetCanary -Trace $Trace).Seen | Should -BeFalse
    }
    It 'ślad bez pól dns albo connections nie dowodzi niczego' {
        $Trace = New-Trace
        $Trace.PSObject.Properties.Remove('dns')
        (Test-NetCanary -Trace $Trace).Seen | Should -BeFalse
        (Get-NetVerdict -Scenario 'cli' -Trace $Trace).Passed | Should -BeFalse
    }
    It 'plan: host kontrolny różni się od dozwolonego, a sieć jest podłączana przed programem i przed śladem w KAŻDYM scenariuszu' {
        $Plan = Get-NetPlan
        $Plan.CanaryHost | Should -Not -Be $Plan.AllowedHost
        $Body = (Get-Command Invoke-NetScenario).ScriptBlock.ToString()
        $Connect = $Body.IndexOf('Connect-VMNetworkAdapter')
        $Connect | Should -BeGreaterThan 0
        foreach ($Later in 'NetWaitNetworkBlock', 'NetStartTraceBlock', 'Start-DsoGuiInUserSession', 'Invoke-DsoCli', 'NetCanaryBlock') {
            $Body.IndexOf($Later) | Should -BeGreaterThan $Connect -Because $Later
        }
        # Ruch kontrolny po pracy programu, a przed zatrzymaniem śladu; połączenie sieci nie jest warunkowane scenariuszem.
        $Body.IndexOf('NetCanaryBlock -') | Should -BeLessThan $Body.IndexOf('NetStopTraceBlock -')
        $Body.Substring(0, $Connect).Split("`n")[-1] | Should -Not -Match 'if'
    }
    It 'czekanie na sieć nie rozwiązuje nazw (nie rozgrzewa pamięci podręcznej DNS)' {
        $Text = $script:NetWaitNetworkBlock.ToString()
        $Text | Should -Not -Match 'GetHostAddresses|Resolve-DnsName|nslookup|Test-NetConnection'
        $Text | Should -Match 'Get-NetRoute'
    }
}

Describe 'Utracone bufory i zdarzenia' {
    It 'Get-NetLossProblem: same zera = brak problemów' {
        @(Get-NetLossProblem -Loss ([pscustomobject]@{ BuffersLost = 0; EventsLost = 0; RealTimeBuffersLost = 0; Error = $null })) | Should -HaveCount 0
    }
    It 'Get-NetLossProblem: utrata buforów, zdarzeń albo buforów czasu rzeczywistego to problem z liczbą' {
        $Problems = @(Get-NetLossProblem -Loss ([pscustomobject]@{ BuffersLost = 2; EventsLost = 7; RealTimeBuffersLost = 1; Error = $null }))
        $Problems | Should -HaveCount 3
        ($Problems -join ' ') | Should -Match '2 buforów'
        ($Problems -join ' ') | Should -Match '7 zdarzeń'
    }
    It 'Get-NetLossProblem: brak odczytu (null, błąd, brakujące pole) to problem, nie zero' {
        @(Get-NetLossProblem -Loss $null) | Should -HaveCount 1
        $WithError = @(Get-NetLossProblem -Loss ([pscustomobject]@{ BuffersLost = $null; EventsLost = $null; RealTimeBuffersLost = $null; Error = 'kod 87' }))
        $WithError | Should -HaveCount 1
        $WithError[0] | Should -Match 'kod 87'
        @(Get-NetLossProblem -Loss ([pscustomobject]@{ BuffersLost = 0; RealTimeBuffersLost = 0; Error = $null })) | Should -HaveCount 1
    }
    It 'odczyt utrat nie zależy od etykiet logman (liczby z ControlTraceW)' {
        $Text = $script:NetStopTraceBlock.ToString()
        $Text | Should -Match 'ControlTraceW'
        $Text | Should -Not -Match 'Buffers Lost'
    }
}

Describe 'Kontrola krzyżowa 4688' {
    BeforeAll {
        $script:Tree = @([pscustomobject]@{ pid = 100; image = 'DSOptimizer.exe' }, [pscustomobject]@{ pid = 101; image = 'powershell.exe' }, [pscustomobject]@{ pid = 102; image = 'conhost.exe' })
        $script:Events = @(
            (New-AuditEvent -NewPid 50 -ParentPid 4 -Image 'C:\Windows\explorer.exe')
            (New-AuditEvent -NewPid 100 -ParentPid 50 -Image 'C:\dso\app\DSOptimizer.exe')
            (New-AuditEvent -NewPid 101 -ParentPid 100 -Image 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe')
            (New-AuditEvent -NewPid 102 -ParentPid 101 -Image 'C:\Windows\System32\conhost.exe')
            (New-AuditEvent -NewPid 300 -ParentPid 50 -Image 'C:\Windows\System32\notepad.exe')
        )
    }
    It 'zgodne drzewo (korzeń po nazwie obrazu, potomkowie po PID rodzica) przechodzi, proces spoza drzewa nie przeszkadza' {
        $Result = Compare-NetProcessAudit -Audit $script:Events -TreeProcesses $script:Tree
        $Result.Match | Should -BeTrue
        $Result.Detail | Should -Match '3 procesów'
    }
    It 'proces w audycie, którego nie ma w śladzie, to rozbieżność' {
        $Result = Compare-NetProcessAudit -Audit $script:Events -TreeProcesses @($script:Tree[0], $script:Tree[1])
        $Result.Match | Should -BeFalse
        $Result.OnlyInAudit | Should -Be @(102)
        $Result.Detail | Should -Match 'tylko w audycie'
    }
    It 'proces w śladzie, którego nie ma w audycie, to rozbieżność' {
        $Extra = @($script:Tree) + [pscustomobject]@{ pid = 999; image = 'winget.exe' }
        $Result = Compare-NetProcessAudit -Audit $script:Events -TreeProcesses $Extra
        $Result.Match | Should -BeFalse
        $Result.OnlyInTrace | Should -Be @(999)
        $Result.Detail | Should -Match 'tylko w śladzie'
    }
    It 'pusty audyt, uszkodzone wpisy albo brak programu po obu stronach to porażka (fail-closed)' {
        (Compare-NetProcessAudit -Audit @() -TreeProcesses $script:Tree).Match | Should -BeFalse
        (Compare-NetProcessAudit -Audit $null -TreeProcesses $script:Tree).Match | Should -BeFalse
        (Compare-NetProcessAudit -Audit @([pscustomobject]@{ error = 'x' }) -TreeProcesses $script:Tree).Match | Should -BeFalse
        (Compare-NetProcessAudit -Audit @((New-AuditEvent -NewPid 300 -ParentPid 50 -Image 'notepad.exe')) -TreeProcesses @()).Match | Should -BeFalse
    }
    It 'PID zwolniony przez proces drzewa i użyty ponownie przez obcy proces nie robi z jego dzieci potomków (rozbieżność z pomiaru VM)' {
        $Events = @(
            (New-AuditEvent -NewPid 100 -ParentPid 50 -Image 'C:\dso\app\DSOptimizer.exe')
            (New-AuditEvent -NewPid 101 -ParentPid 100 -Image 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe')
            (New-AuditEvent -NewPid 101 -ParentPid 900 -Image 'C:\Windows\System32\UIEOrchestrator.exe')
            (New-AuditEvent -NewPid 700 -ParentPid 101 -Image 'C:\Windows\System32\conhost.exe')
        )
        $Result = Compare-NetProcessAudit -Audit $Events -TreeProcesses @($script:Tree[0], $script:Tree[1])
        $Result.Match | Should -BeTrue
        $Result.OnlyInAudit | Should -HaveCount 0
    }
    It 'audyt po przejściu przez JSON (jeden wpis nie jest tablicą) też działa' {
        $One = @((New-AuditEvent -NewPid 100 -ParentPid 50 -Image 'C:\x\DSOptimizer.exe')) | ConvertTo-Json -Depth 3 | ConvertFrom-Json
        (Compare-NetProcessAudit -Audit $One -TreeProcesses @($script:Tree[0])).Match | Should -BeTrue
    }
}

Describe 'Polecenia CLI' {
    It 'Get-NetCliProblem: kod 0 i niepusty plik --out przechodzi' {
        Get-NetCliProblem -Command '--list-apps' -Result ([pscustomobject]@{ ExitCode = 0; TimedOut = $false; Json = '{"ok":true}' }) | Should -BeNullOrEmpty
    }
    It 'Get-NetCliProblem: brak pliku, pusty plik, zły kod, przekroczony czas i brak wyniku to problem' {
        Get-NetCliProblem -Command '--list-apps' -Result ([pscustomobject]@{ ExitCode = 0; TimedOut = $false; Json = $null }) | Should -Match 'nie istnieje albo jest pusty'
        Get-NetCliProblem -Command '--list-apps' -Result ([pscustomobject]@{ ExitCode = 0; TimedOut = $false; Json = "  `r`n" }) | Should -Match 'pusty'
        Get-NetCliProblem -Command '--list-install' -Result ([pscustomobject]@{ ExitCode = 3; TimedOut = $false; Json = '{}' }) | Should -Match 'kod wyjścia 3'
        Get-NetCliProblem -Command '--list-install' -Result ([pscustomobject]@{ ExitCode = -1; TimedOut = $true; Json = '{}' }) | Should -Match 'czas'
        Get-NetCliProblem -Command '--list-install' -Result $null | Should -Match 'brak wyniku'
    }
}

Describe 'Plan i argumenty' {
    It 'plan: zatrzymane usługi Windows, dozwolony host, ustawienia start-on bez pola i start-off z wyłączonym sprawdzaniem, zakres' {
        $Plan = Get-NetPlan
        @($Plan.StoppedServices) | Should -Be @('wuauserv', 'UsoSvc', 'DoSvc', 'BITS')
        $Plan.AllowedHost | Should -BeExactly 'api.github.com'
        $Plan.NetworkSwitch | Should -BeExactly 'Default Switch'
        $Plan.UserName | Should -BeExactly 'dso'
        $Plan.Scope | Should -Match 'Drzewo procesów programu'
        $Plan.Scope | Should -Match 'usługom Windows'
        $Plan.AuditSubcategory | Should -Match '^\{[0-9A-F-]{36}\}$'
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

Describe 'Invoke-NetVmRun: sprzątanie i zaliczenia' {
    BeforeEach {
        $script:Calls = [System.Collections.Generic.List[string]]::new()
        $script:Saved = $null
        $script:ScenarioBehavior = 'throw'
        # Wszystko, co dotyka Hyper-V albo PowerShell Direct, jest zamockowane; Assert-DsoVmLiveAllowed też (test nie zdejmuje wyłącznika).
        Mock Assert-DsoVmLiveAllowed { }
        Mock Get-DsoVmCredential { New-Object System.Management.Automation.PSCredential('x', (ConvertTo-SecureString 'x' -AsPlainText -Force)) }
        Mock Restore-VmCleanState { $script:Calls.Add('revert') }
        Mock Disconnect-VMNetworkAdapter { $script:Calls.Add('disconnect') }
        Mock Start-VM { $script:Calls.Add('start') }
        Mock Wait-VmReady { $script:Calls.Add('ready'); $null }
        Mock Start-Sleep { }
        Mock Remove-PSSession -RemoveParameterValidation 'Session' { }
        Mock Complete-ExploreVmCleanup { $script:Calls.Add('cleanup'); [pscustomobject]@{ Ok = $true; Problems = @() } }
        Mock Save-ExploreResult { $script:Calls.Add('save'); $script:Saved = $Result; 'C:\w\net.json' }
        Mock Invoke-NetScenario {
            $script:Calls.Add('scenario')
            if ($script:ScenarioBehavior -eq 'throw') { throw 'boom w scenariuszu' }
            $Record.passed = $true
            $Record.detail = 'ok'
            if ($script:ScenarioBehavior -eq 'problem') { $Record.problems += 'brak odczytu utraconych buforów' }
        }
        $script:Artifact = [ordered]@{ Name = 'DSOptimizer-4.0.3.zip' }
    }
    It 'wyjątek w scenariuszu: Complete-ExploreVmCleanup nadal wywołany, wynik zapisany z błędem, przebieg zgłasza błąd' {
        { Invoke-NetVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Scenarios @('cli') -Artifact $script:Artifact 6>$null } | Should -Throw '*boom w scenariuszu*'
        Should -Invoke Complete-ExploreVmCleanup -Times 1 -Exactly
        $script:Calls | Should -Contain 'cleanup'
        $script:Calls.IndexOf('cleanup') | Should -BeGreaterThan $script:Calls.IndexOf('scenario')
        $script:Saved.scenarios['cli'].error | Should -Match 'boom'
        $script:Saved.scenarios['cli'].passed | Should -BeFalse
        $script:Saved.allPassed | Should -BeFalse
    }
    It 'wyjątek przed gotowością VM (revert rzuca): sprzątanie nadal wywołane' {
        Mock Restore-VmCleanState { throw 'revert padł' }
        { Invoke-NetVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Scenarios @('start-on') -Artifact $script:Artifact 6>$null } | Should -Throw '*revert padł*'
        Should -Invoke Complete-ExploreVmCleanup -Times 1 -Exactly
        Should -Invoke Invoke-NetScenario -Times 0 -Exactly
    }
    It 'sprzątanie raz na scenariusz (3 scenariusze = 3 sprzątania)' {
        $script:ScenarioBehavior = 'ok'
        $Path = Invoke-NetVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Scenarios @('start-on', 'start-off', 'cli') -Artifact $script:Artifact 6>$null
        $Path | Should -Be 'C:\w\net.json'
        Should -Invoke Complete-ExploreVmCleanup -Times 3 -Exactly
        $script:Saved.allPassed | Should -BeTrue
        $script:Saved.scope | Should -Match 'Drzewo procesów programu'
    }
    It 'scenariusz z werdyktem Passed, ale z problemem (np. brak odczytu utrat) nie jest zaliczony' {
        $script:ScenarioBehavior = 'problem'
        { Invoke-NetVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Scenarios @('cli') -Artifact $script:Artifact 6>$null } | Should -Throw '*utraconych buforów*'
        $script:Saved.scenarios['cli'].passed | Should -BeFalse
        $script:Saved.allPassed | Should -BeFalse
    }
    It 'niepoprawne sprzątanie VM odbiera zaliczenie' {
        $script:ScenarioBehavior = 'ok'
        Mock Complete-ExploreVmCleanup { [pscustomobject]@{ Ok = $false; Problems = @('karta nadal podłączona') } }
        { Invoke-NetVmRun -VmName 'DSO-26H2-Pro' -OutDirectory $TestDrive -Scenarios @('cli') -Artifact $script:Artifact 6>$null 3>$null } | Should -Throw '*karta nadal podłączona*'
        $script:Saved.scenarios['cli'].passed | Should -BeFalse
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
    It 'nagłówek mówi o drzewie procesów programu i o ruchu usług Windows, który nie jest liczony' {
        $Text = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $script:NetScriptPath).Path, [System.Text.Encoding]::UTF8)
        $Text | Should -Match 'drzewa procesów programu'
        $Text | Should -Match 'usługom\s+Windows'
        $Text | Should -Match 'nie jest liczony'
    }
    It 'NavProbe.ps1 jest ASCII poza nagłówkiem (PowerShell 5.1 w VM) i nie klika przycisków' {
        $Text = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $script:NetFiles[2]).Path, [System.Text.Encoding]::UTF8)
        $Code = $Text.Substring($Text.IndexOf('#>') + 2)
        $Code | Should -Not -Match '[^\x00-\x7F]'
        $Code | Should -Not -Match 'InvokePattern'
    }
}

AfterAll { Clear-NoLiveVm }
