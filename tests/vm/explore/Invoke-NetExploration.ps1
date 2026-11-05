<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Jedna VM na proces. Wydanie: zip z tools\release\Build-Release.ps1 -Variant Folder (DSOptimizer-<wersja>.zip, obok SHA256SUMS.txt), np.:
        .\tools\release\Build-Release.ps1 -Variant Folder -OutRoot $env:TEMP\dso-explore\4.0.3\wydania
        .\tests\vm\explore\Invoke-NetExploration.ps1 -VmName DSO-EN-Pro -Scenario start-on,start-off,cli `
            -ArtifactPath <katalog wydania>\DSOptimizer-4.0.3.zip -OutDirectory $env:TEMP\dso-explore\4.0.3\siec\pro
    Wersja zainstalowana: zamiast -ArtifactPath podaj -InstallerPath <katalog wydania>\DSOptimizer-<wersja>-setup.exe (obok SHA256SUMS.txt
    z jego sumą). Parametry wykluczają się: dokładnie jeden z dwóch jest wymagany (błąd przed dotknięciem VM). W VM zamiast rozpakowania
    działa cicha instalacja (/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-, wymagany kod 0) do C:\Program Files\DSOptimizer, a pomiar
    dotyczy C:\Program Files\DSOptimizer\DSOptimizer.exe (appVersion = FileVersion zainstalowanego pliku). net.json ma pole variant
    (Folder albo Installer). Instalator uruchamia się wyłącznie w VM, nigdy na hoście.
        .\tests\vm\explore\Invoke-NetExploration.ps1 -VmName DSO-EN-Pro -InstallerPath <katalog wydania>\DSOptimizer-4.0.4-setup.exe `
            -OutDirectory $env:TEMP\dso-explore\instalator\siec\pro
    -Scenario: start-on, start-off, cli (domyślnie wszystkie trzy, w tej kolejności).
    -ShowPlan: wypisuje rozstrzygnięte argumenty i plan (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    Czas: ok. 10-15 min na scenariusz GUI (start-on, start-off) i ok. 5 min na cli; każdy scenariusz na świeżo przywróconej VM.
Opis działania:
    Pomiar połączeń programu (zasada: przy starcie wyłącznie api.github.com, po wyłączeniu sprawdzania i w trybie wiersza poleceń zero).
    Port metody z audytu sieci: ślad ETW w VM (logman) z dostawcami Microsoft-Windows-Kernel-Network, Microsoft-Windows-Kernel-Process
    i Microsoft-Windows-DNS-Client, parser net\Parse-NetTrace.ps1 przypisuje ruch do drzewa procesów DSOptimizer.exe (PID rodzica,
    potomkowie dowolnej głębokości) i do zapytań DNS z tego drzewa. Sieć: Default Switch, podłączona dopiero po starcie VM i na czas
    scenariusza. W VM próba zatrzymania wuauserv, UsoSvc, DoSvc i BITS (wynik każdej w polu services; DoSvc odmawia zmiany: Odmowa dostępu) (usługi Windows pobierające w tle zalewały ślad i nie należą do programu).
    Scenariusze (każdy: revert do checkpointu, start VM bez sieci, wydanie z -ArtifactPath rozpakowane do C:\dso\app albo z -InstallerPath zainstalowane po cichu, konto 'dso'):
      start-on  - settings.json z {"schemaVersion":1,"language":"<język VM: pl albo en>"} (pole checkUpdatesAtStart nieobecne = test wartości domyślnej),
                  start okna i net\NavProbe.ps1 po wszystkich stronach bez klikania przycisków;
      start-off - jak start-on, ale settings.json dodatkowo z "checkUpdatesAtStart":false;
      cli       - settings.json jak w start-on (sprawdzanie włączone), potem --list-install i --list-apps.
    settings.json jest zapisywany przed startem z zaufanym właścicielem i DACL (Administratorzy + SYSTEM), inaczej program odłożyłby go
    na bok jako niezaufany.
    Werdykt liczy czysta funkcja Get-NetVerdict -Scenario <nazwa> -Trace <obiekt z dsoTreeDns i dsoTreeConnections> -> { Passed; Detail }:
      start-on: co najmniej jedno połączenie TCP 443 do api.github.com, zapytania DNS drzewa wyłącznie o api.github.com, brak innych
                połączeń (brak połączenia = nie przechodzi, bo sprawdzenie musiało się wykonać);
      start-off i cli: zero zapytań DNS i zero połączeń drzewa programu.
    Werdykt jest fail-closed: nie przechodzi ślad bez pól eventCount i dsoTreeProcesses (albo z zerem zdarzeń lub procesów), bez ruchu
    kontrolnego, z utraconymi buforami lub zdarzeniami (albo bez odczytu tych liczb) i z rozbieżnym drzewem procesów w audycie 4688.
    Ruch kontrolny: tuż przed zatrzymaniem śladu osobny proces w VM (poza drzewem programu) robi zapytanie DNS i połączenie TCP 443 do
    example.com; werdykt każdego scenariusza wymaga, żeby oba były widoczne w śladzie. Sieć jest podłączona w każdym scenariuszu, przed
    uruchomieniem programu, i czekamy tylko na łączność (bez rozwiązywania nazw, żeby nie rozgrzewać pamięci podręcznej DNS).
    Audyt 4688 (tworzenie procesów) jest włączany w VM; po scenariuszu drzewo DSOptimizer.exe z audytu jest porównywane z dsoTreeProcesses.
    Zakres: wynik dotyczy drzewa procesów programu (DSOptimizer.exe i jego potomkowie), nie całego komputera. Ruch zlecony usługom
    Windows (Windows Update, Delivery Optimization, BITS) nie jest liczony; usługi (wuauserv, UsoSvc, DoSvc, BITS) próbowano zatrzymać
    na czas pomiaru, jak w metodzie archiwalnej (wynik prób w polu services; DoSvc odmawia zmiany). Pomiar mówi z kim i ile bajtów, nie co (patrz audyt sieci).
    RISK: skrypt przywraca VM do checkpointu 'czysta-26H2' przed każdym scenariuszem i po nim (zmiany w VM przepadają) oraz na czas
    scenariusza podłącza kartę VM do Default Switch (ruch NAT na hoście); po pracy karta jest odłączona, a VM wyłączona. Nie usuwa VM.
    Wynik: <OutDirectory>\<VmName>\net.json (UTF-8), surowe pliki scenariusza (ślad sieci w JSON, wynik sondy, raporty --out)
    w <OutDirectory>\<VmName>\raw-<scenariusz>.
    Kod wyjścia: 0 gdy wszystkie wybrane scenariusze zebrane bez błędu i z werdyktem Passed, 1 w przeciwnym razie (wynik i tak w net.json).
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro', 'DSO-PL-Home')][string]$VmName = 'DSO-EN-Pro',
    [ValidateSet('start-on', 'start-off', 'cli')][string[]]$Scenario = @('start-on', 'start-off', 'cli'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$ShowPlan,
    # Plik wydania Folder (DSOptimizer-<wersja>.zip z Build-Release.ps1), obok SHA256SUMS.txt.
    [string]$ArtifactPath,
    # Instalator DSOptimizer-<wersja>-setup.exe (obok SHA256SUMS.txt z jego sumą); wyklucza się z -ArtifactPath.
    [string]$InstallerPath
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\VmSmoke.psm1')
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\VmText.psm1')

# ---------------------------------------------------------------------------------------------------------------------
# Plan i werdykt (czyste funkcje, bez dotykania VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-NetNavTexts {
    # Nazwy z okna dla sondy nawigacji (net\NavProbe.ps1) w języku VM: JSON { lists, slowPages }. Teksty wyłącznie z tabel tłumaczeń
    # repo (Get-DsoUiText; brak tłumaczenia = wyjątek); polskie klucze to nazwy list i stron okna.
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][ValidateSet('pl', 'en')][string]$Language, [string]$Root)
    $Text = {
        param([string]$Polish)
        $Splat = @{ Polish = $Polish }
        if ($Root) { $Splat['Root'] = $Root }
        # Klucz spoza tabel = błąd także w pl (Get-DsoUiText dla pl zwraca klucz bez sprawdzenia).
        if (-not (Test-DsoTextKey @Splat)) { throw "brak tłumaczenia: '$Polish' (tabele tłumaczeń repo)." }
        Get-DsoUiText @Splat -Language $Language
    }
    $Texts = [ordered]@{
        lists     = @(@('Menu', 'Ustawienia') | ForEach-Object { & $Text $_ })
        slowPages = @(@('Aplikacje', 'Instalacja', 'Przygotowanie', 'Specyfikacja') | ForEach-Object { & $Text $_ })
    }
    return (ConvertTo-Json -InputObject $Texts -Depth 4 -Compress)
}

function Get-NetPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('pl', 'en')][string]$Language)
    return [ordered]@{
        Language            = $Language
        AllowedHost         = 'api.github.com'
        AllowedPort         = 443
        # Ruch kontrolny osobnego procesu przed zatrzymaniem śladu (dowód, że ślad łapie sieć); host inny niż dozwolony, żeby go nie mylić.
        CanaryHost          = 'example.com'
        CanaryPort          = 443
        CanaryTimeoutSeconds = 30
        # Podkategoria audytu 'Process Creation' (GUID, niezależny od języka).
        AuditSubcategory    = '{0CCE922B-69AE-11D9-BED3-505054503030}'
        Scope               = 'Drzewo procesów programu (DSOptimizer.exe i potomkowie). Ruch zlecony usługom Windows (Windows Update, Delivery Optimization, BITS) nie jest liczony; usługi wuauserv, UsoSvc, DoSvc i BITS próbowano zatrzymać na czas pomiaru, wynik każdej próby jest w polu services.'
        NetworkName         = 'api.github.com'
        NetworkSwitch       = 'Default Switch'
        NetworkWaitSeconds  = 300
        UserName            = 'dso'
        # Usługi Windows pobierające w tle (nie należą do programu); przed śladem próba zatrzymania, jej wynik trafia do pola services.
        StoppedServices     = @('wuauserv', 'UsoSvc', 'DoSvc', 'BITS')
        WorkDirectory       = 'C:\dso\net'
        ResultDirectory     = 'C:\dso\net\out'
        CliDirectory        = 'C:\dso\net\out'
        SettingsDirectory   = 'C:\ProgramData\DSOptimizer'
        SettingsOn          = '{"schemaVersion":1,"language":"' + $Language + '"}'
        SettingsOff         = '{"schemaVersion":1,"language":"' + $Language + '","checkUpdatesAtStart":false}'
        SettleAfterLogonSeconds = 45
        SettleBeforeStopSeconds = 15
        GuiTaskName         = 'DSO-Net-Gui'
        NavTimeoutSeconds   = 900
        CliTimeoutSeconds   = 300
        CliCommands         = @('--list-install', '--list-apps')
        Providers           = @(
            '"Microsoft-Windows-Kernel-Network" 0xFFFFFFFFFFFFFFFF 0xff'
            '"Microsoft-Windows-Kernel-Process" 0x10 0xff'
            '"Microsoft-Windows-DNS-Client" 0xFFFFFFFFFFFFFFFF 0xff'
        )
    }
}

function Get-NetTraceField {
    # Pole obiektu śladu jako tablica; $null, gdy pola brak (odróżnia "pusto" od "ślad bez tego pola").
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()]$Trace, [Parameter(Mandatory)][string]$Name)
    if ($null -eq $Trace) { return $null }
    $Property = $Trace.PSObject.Properties[$Name]
    if ($null -eq $Property) { return $null }
    return , @(@($Property.Value) | Where-Object { $null -ne $_ })
}

function Get-NetItemValue {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Item, [Parameter(Mandatory)][string]$Name)
    $Property = $Item.PSObject.Properties[$Name]
    if ($null -eq $Property) { return $null }
    return $Property.Value
}

function Format-NetHost {
    [CmdletBinding()]
    [OutputType([string])]
    param([AllowNull()]$Name)
    return ([string]$Name).Trim().TrimEnd('.').ToLowerInvariant()
}

function Test-NetCanary {
    # Dowód, że ślad w ogóle łapie sieć: kontrolny ruch (zapytanie DNS i połączenie TCP) osobnego procesu spoza drzewa programu musi
    # być w śladzie (pola dns i connections z dsoTree = false). Brak = pomiar się nie liczy. Czysta funkcja.
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()]$Trace)
    $Plan = Get-NetPlan -Language pl   # pola używane tu nie zależą od języka
    $CanaryHost = Format-NetHost $Plan.CanaryHost
    $All = Get-NetTraceField -Trace $Trace -Name 'dns'
    $Connections = Get-NetTraceField -Trace $Trace -Name 'connections'
    if ($null -eq $All -or $null -eq $Connections) {
        return [pscustomobject]@{ Seen = $false; Detail = 'Ślad bez pól dns albo connections: nie da się potwierdzić, że ślad łapie sieć.' }
    }
    $DnsSeen = @($All | Where-Object { (Format-NetHost (Get-NetItemValue -Item $_ -Name 'name')) -eq $CanaryHost -and (Get-NetItemValue -Item $_ -Name 'dsoTree') -ne $true }).Count -gt 0
    $ConnectionSeen = @($Connections | Where-Object {
            $Hosts = @(@(Get-NetItemValue -Item $_ -Name 'hosts') | Where-Object { $_ } | ForEach-Object { Format-NetHost $_ })
            ((Get-NetItemValue -Item $_ -Name 'dsoTree') -ne $true) -and ([string](Get-NetItemValue -Item $_ -Name 'proto') -ceq 'TCP') -and
            ([int](Get-NetItemValue -Item $_ -Name 'remotePort') -eq [int]$Plan.CanaryPort) -and ($Hosts -contains $CanaryHost)
        }).Count -gt 0
    if ($DnsSeen -and $ConnectionSeen) {
        return [pscustomobject]@{ Seen = $true; Detail = "Ruch kontrolny ($CanaryHost) widoczny w śladzie." }
    }
    $Missing = @()
    if (-not $DnsSeen) { $Missing += 'zapytania DNS' }
    if (-not $ConnectionSeen) { $Missing += "połączenia TCP $($Plan.CanaryPort)" }
    return [pscustomobject]@{ Seen = $false; Detail = "W śladzie brakuje kontrolnego ruchu ($CanaryHost) spoza drzewa programu: $($Missing -join ', '); ślad mógł nie łapać sieci." }
}

function Get-NetVerdict {
    # Czysta funkcja werdyktu (fail-closed): Scenario + obiekt śladu (dsoTreeDns, dsoTreeConnections, dsoTreeProcesses, eventCount, dns,
    # connections z Parse-NetTrace.ps1) -> { Passed; Detail }. Dotyczy drzewa procesów programu; ruch zlecony usługom Windows nie jest liczony.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('start-on', 'start-off', 'cli')][string]$Scenario,
        [Parameter(Mandatory)][AllowNull()]$Trace
    )
    $Plan = Get-NetPlan -Language pl   # pola używane tu nie zależą od języka
    $Allowed = [string]$Plan.AllowedHost
    $Fail = { param([string]$Text) [pscustomobject]@{ Passed = $false; Detail = $Text } }
    $Dns = Get-NetTraceField -Trace $Trace -Name 'dsoTreeDns'
    $Connections = Get-NetTraceField -Trace $Trace -Name 'dsoTreeConnections'
    if ($null -eq $Dns -or $null -eq $Connections) { return (& $Fail 'Ślad bez pól dsoTreeDns albo dsoTreeConnections: pomiar się nie liczy.') }
    # Ślad bez zdarzeń albo bez procesu programu nie dowodzi niczego; brak tych pól to też porażka.
    $EventCount = $Trace.PSObject.Properties['eventCount']
    if ($null -eq $EventCount -or $null -eq $EventCount.Value -or [int64]$EventCount.Value -le 0) { return (& $Fail 'Ślad bez pola eventCount albo bez zdarzeń: pomiar się nie liczy.') }
    $Processes = Get-NetTraceField -Trace $Trace -Name 'dsoTreeProcesses'
    if ($null -eq $Processes -or $Processes.Count -eq 0) { return (& $Fail 'Ślad bez pola dsoTreeProcesses albo bez procesu DSOptimizer.exe: pomiar się nie liczy.') }
    $Canary = Test-NetCanary -Trace $Trace
    if (-not $Canary.Seen) { return (& $Fail $Canary.Detail) }

    $DnsNames = @($Dns | ForEach-Object { Format-NetHost (Get-NetItemValue -Item $_ -Name 'name') } | Where-Object { $_ } | Sort-Object -Unique)
    $Describe = {
        param($Connection)
        $Hosts = @(@(Get-NetItemValue -Item $Connection -Name 'hosts') | Where-Object { $_ } | ForEach-Object { Format-NetHost $_ })
        $HostText = if ($Hosts.Count -gt 0) { $Hosts -join '/' } else { '(host nieustalony)' }
        '{0} {1}:{2} {3}' -f (Get-NetItemValue -Item $Connection -Name 'proto'), (Get-NetItemValue -Item $Connection -Name 'remote'), (Get-NetItemValue -Item $Connection -Name 'remotePort'), $HostText
    }

    if ($Scenario -ne 'start-on') {
        if ($Dns.Count -eq 0 -and $Connections.Count -eq 0) { return [pscustomobject]@{ Passed = $true; Detail = 'Zero zapytań DNS i zero połączeń w drzewie procesów programu (ruch kontrolny widoczny w śladzie).' } }
        $Parts = @()
        if ($Dns.Count -gt 0) { $Parts += "zapytania DNS: $($DnsNames -join ', ')" }
        if ($Connections.Count -gt 0) { $Parts += "połączenia: $((@($Connections | ForEach-Object { & $Describe $_ }) | Sort-Object -Unique) -join '; ')" }
        return (& $Fail "Drzewo procesów programu nie powinno łączyć się z siecią w scenariuszu '$Scenario', a w śladzie jest: $($Parts -join ' | ').")
    }

    if ($Connections.Count -eq 0) {
        return (& $Fail "Brak jakiegokolwiek połączenia w drzewie procesów programu: sprawdzenie aktualizacji ($Allowed) nie wykonało się albo ślad go nie złapał.")
    }
    $Extra = @()
    $Good = 0
    foreach ($Connection in $Connections) {
        $Hosts = @(@(Get-NetItemValue -Item $Connection -Name 'hosts') | Where-Object { $_ } | ForEach-Object { Format-NetHost $_ })
        # Wszystkie nazwy hosta połączenia muszą być równe dozwolonej; inne (adres współdzielony z inną nazwą) = porażka.
        $OnlyAllowed = ($Hosts.Count -gt 0) -and (@($Hosts | Where-Object { $_ -ne $Allowed }).Count -eq 0)
        $IsGood = ([string](Get-NetItemValue -Item $Connection -Name 'proto') -ceq 'TCP') -and ([int](Get-NetItemValue -Item $Connection -Name 'remotePort') -eq [int]$Plan.AllowedPort) -and $OnlyAllowed
        if ($IsGood) { $Good++ } else { $Extra += (& $Describe $Connection) }
    }
    $OtherDns = @($DnsNames | Where-Object { $_ -ne $Allowed })
    if ($Extra.Count -gt 0 -or $OtherDns.Count -gt 0) {
        $Parts = @()
        if ($Extra.Count -gt 0) { $Parts += "dodatkowe połączenia: $((@($Extra) | Sort-Object -Unique) -join '; ')" }
        if ($OtherDns.Count -gt 0) { $Parts += "dodatkowe zapytania DNS: $($OtherDns -join ', ')" }
        return (& $Fail "Dozwolone jest wyłącznie $Allowed na porcie $($Plan.AllowedPort), a w śladzie drzewa procesów programu są: $($Parts -join ' | ').")
    }
    if ($Good -eq 0) { return (& $Fail "Brak połączenia TCP $($Plan.AllowedPort) do $Allowed.") }
    return [pscustomobject]@{ Passed = $true; Detail = "Drzewo procesów programu: wyłącznie $Allowed na porcie $($Plan.AllowedPort) ($Good połączeń); ruch kontrolny widoczny w śladzie." }
}

function Get-NetCliProblem {
    # Wynik jednego polecenia CLI (z Invoke-DsoCli) -> opis problemu albo $null. Plik --out musi istnieć i nie być pusty.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Command, [AllowNull()]$Result)
    if ($null -eq $Result) { return "polecenie ${Command}: brak wyniku." }
    if ($Result.TimedOut -eq $true) { return "polecenie ${Command}: przekroczony czas." }
    if ([int]$Result.ExitCode -ne 0) { return "polecenie ${Command}: kod wyjścia $($Result.ExitCode)." }
    if ([string]::IsNullOrWhiteSpace([string]$Result.Json)) { return "polecenie ${Command}: plik --out nie istnieje albo jest pusty." }
    return $null
}

function Get-NetLossProblem {
    # Odczyt utraconych buforów i zdarzeń sesji ETW (z bloku zatrzymania śladu) -> lista problemów; brak odczytu też jest problemem.
    [CmdletBinding()]
    param([AllowNull()]$Loss)
    if ($null -eq $Loss) { return @('brak odczytu utraconych buforów i zdarzeń śladu') }
    $Problems = @()
    $ReadError = Get-NetItemValue -Item $Loss -Name 'Error'
    if ($ReadError) { $Problems += "brak odczytu utraconych buforów i zdarzeń śladu: $ReadError" }
    $Fields = [ordered]@{ BuffersLost = 'buforów'; EventsLost = 'zdarzeń'; RealTimeBuffersLost = 'buforów czasu rzeczywistego' }
    foreach ($Name in $Fields.Keys) {
        $Value = Get-NetItemValue -Item $Loss -Name $Name
        if ($null -eq $Value) { if (-not $ReadError) { $Problems += "brak odczytu utraconych $($Fields[$Name]) ($Name)" }; continue }
        if ([int64]$Value -gt 0) { $Problems += "ślad utracił $Value $($Fields[$Name]): pomiar niekompletny" }
    }
    return $Problems
}

function Compare-NetProcessAudit {
    # Kontrola krzyżowa: drzewo procesów z audytu 4688 (korzeń = DSOptimizer.exe, potomkowie po PID rodzica, w kolejności zdarzeń)
    # kontra dsoTreeProcesses ze śladu ETW. Rozbieżność w którąkolwiek stronę = Match false. Czysta funkcja.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()]$Audit,
        [Parameter(Mandatory)][AllowNull()]$TreeProcesses,
        [string]$RootImage = 'DSOptimizer.exe'
    )
    $Events = @(@($Audit) | Where-Object { $null -ne $_ })
    $Tree = @(@($TreeProcesses) | Where-Object { $null -ne $_ })
    if ($Events.Count -eq 0) { return [pscustomobject]@{ Match = $false; Detail = 'Audyt 4688 bez zdarzeń: nie da się porównać drzewa procesów.'; OnlyInAudit = @(); OnlyInTrace = @() } }
    if (@($Events | Where-Object { $null -eq $_.PSObject.Properties['newPid'] -or $null -eq $_.PSObject.Properties['parentPid'] }).Count -gt 0) {
        return [pscustomobject]@{ Match = $false; Detail = 'Audyt 4688 zawiera uszkodzone wpisy (brak newPid lub parentPid).'; OnlyInAudit = @(); OnlyInTrace = @() }
    }
    # Numery PID są ponownie używane po zakończeniu procesu, a audyt 4688 nie ma wpisów o zakończeniu. Każde zdarzenie tworzenia
    # ustala więc od nowa, czy jego PID jest teraz w drzewie ($Current; z niego bierze się rodzic). Porównanie ze śladem liczy
    # wcielenia: ile razy dany PID wszedł do drzewa w audycie i ile razy występuje w śladzie. Zgubione w śladzie drugie wcielenie
    # tego samego PID jest rozbieżnością, tak samo PID, który wrócił do drzewa po użyciu przez obcy proces.
    $Current = New-Object 'System.Collections.Generic.HashSet[int]'
    $AuditCount = New-Object 'System.Collections.Generic.Dictionary[int,int]'
    foreach ($Event in $Events) {
        $Leaf = [System.IO.Path]::GetFileName([string](Get-NetItemValue -Item $Event -Name 'image'))
        $NewPid = [int]$Event.newPid
        if ($Leaf -ieq $RootImage -or $Current.Contains([int]$Event.parentPid)) {
            [void]$Current.Add($NewPid)
            $AuditCount[$NewPid] = $(if ($AuditCount.ContainsKey($NewPid)) { $AuditCount[$NewPid] + 1 } else { 1 })
        }
        else { [void]$Current.Remove($NewPid) }
    }
    $TraceCount = New-Object 'System.Collections.Generic.Dictionary[int,int]'
    foreach ($Process in $Tree) {
        $TracePid = [int](Get-NetItemValue -Item $Process -Name 'pid')
        $TraceCount[$TracePid] = $(if ($TraceCount.ContainsKey($TracePid)) { $TraceCount[$TracePid] + 1 } else { 1 })
    }
    $AuditTotal = 0; foreach ($Count in $AuditCount.Values) { $AuditTotal += $Count }
    $TraceTotal = 0; foreach ($Count in $TraceCount.Values) { $TraceTotal += $Count }
    if ($AuditTotal -eq 0 -and $TraceTotal -eq 0) { return [pscustomobject]@{ Match = $false; Detail = 'Ani audyt 4688, ani ślad nie zawierają procesu DSOptimizer.exe.'; OnlyInAudit = @(); OnlyInTrace = @() } }
    $AllPids = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($Key in $AuditCount.Keys) { [void]$AllPids.Add($Key) }
    foreach ($Key in $TraceCount.Keys) { [void]$AllPids.Add($Key) }
    $OnlyAudit = @(); $OnlyTrace = @(); $Parts = @()
    foreach ($Key in @($AllPids | Sort-Object)) {
        $InAudit = if ($AuditCount.ContainsKey($Key)) { $AuditCount[$Key] } else { 0 }
        $InTrace = if ($TraceCount.ContainsKey($Key)) { $TraceCount[$Key] } else { 0 }
        if ($InAudit -gt $InTrace) { $OnlyAudit += $Key; $Parts += "PID $Key ma w audycie 4688 $InAudit wcieleń, w śladzie $InTrace" }
        elseif ($InTrace -gt $InAudit) { $OnlyTrace += $Key; $Parts += "PID $Key ma w śladzie $InTrace wcieleń, w audycie 4688 $InAudit" }
    }
    if ($Parts.Count -eq 0) {
        return [pscustomobject]@{ Match = $true; Detail = "Drzewo procesów zgodne w audycie 4688 i w śladzie ($AuditTotal procesów)."; OnlyInAudit = @(); OnlyInTrace = @() }
    }
    $Detail = @()
    if ($OnlyAudit.Count -gt 0) { $Detail += "tylko w audycie 4688: PID $($OnlyAudit -join ', ')" }
    if ($OnlyTrace.Count -gt 0) { $Detail += "tylko w śladzie: PID $($OnlyTrace -join ', ')" }
    return [pscustomobject]@{ Match = $false; Detail = "Rozbieżność drzewa procesów: $($Detail -join ' | ') ($($Parts -join '; '))."; OnlyInAudit = $OnlyAudit; OnlyInTrace = $OnlyTrace }
}
function Assert-NetSha256Sums {
    # Plik obok SHA256SUMS.txt z tą samą sumą; zwraca sumę (małe litery). Każdy błąd = wyjątek.
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$FullPath, [Parameter(Mandatory)][string]$Name)
    $Sums = Join-Path ([System.IO.Path]::GetDirectoryName($FullPath)) 'SHA256SUMS.txt'
    if (-not [System.IO.File]::Exists($Sums)) { throw "Brak '$Sums' obok pliku wydania." }
    $Expected = $null
    foreach ($Line in [System.IO.File]::ReadAllLines($Sums)) {
        if ($Line -match '^([0-9a-fA-F]{64}) [ *](.+)$' -and $Matches[2] -ceq $Name) { $Expected = $Matches[1].ToLowerInvariant() }
    }
    if ($null -eq $Expected) { throw "SHA256SUMS.txt nie ma sumy pliku '$Name'." }
    $Sha = (Get-FileHash -LiteralPath $FullPath -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
    if ($Sha -cne $Expected) { throw "SHA256 pliku '$Name' ($Sha) różni się od SHA256SUMS.txt ($Expected)." }
    return $Sha
}

function Resolve-NetArtifact {
    # -ArtifactPath -> opis wydania Folder z Build-Release.ps1 (DSOptimizer-<wersja>.zip obok SHA256SUMS.txt z tą samą sumą; w zipie
    # DSOptimizer\DSOptimizer.exe). Każdy błąd = wyjątek, zanim cokolwiek dotknie VM.
    [CmdletBinding()]
    param([AllowEmptyString()][AllowNull()][string]$ArtifactPath)
    if ([string]::IsNullOrWhiteSpace($ArtifactPath)) { throw '-ArtifactPath jest wymagany (DSOptimizer-<wersja>.zip z tools\release\Build-Release.ps1 -Variant Folder).' }
    if ($ArtifactPath -notmatch '^[A-Za-z]:\\' -and $ArtifactPath -notmatch '^\\\\[^\\]+\\[^\\]+') { throw "-ArtifactPath musi być ścieżką bezwzględną: '$ArtifactPath'." }
    $Full = [System.IO.Path]::GetFullPath($ArtifactPath)
    if (-not [System.IO.File]::Exists($Full)) { throw "Brak pliku wydania '$Full'." }
    $Name = [System.IO.Path]::GetFileName($Full)
    if ($Name -notmatch '^DSOptimizer-(\d+\.\d+\.\d+)\.zip\z') { throw "Plik '$Name' nie jest wydaniem Folder (oczekiwano DSOptimizer-<wersja>.zip)." }
    $Version = $Matches[1]
    $Sha = Assert-NetSha256Sums -FullPath $Full -Name $Name
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $Zip = [System.IO.Compression.ZipFile]::OpenRead($Full)
    try { $HasExe = @($Zip.Entries | Where-Object { ($_.FullName -replace '\\', '/') -ceq 'DSOptimizer/DSOptimizer.exe' }).Count -eq 1 }
    finally { $Zip.Dispose() }
    if (-not $HasExe) { throw "Zip '$Name' nie ma DSOptimizer\DSOptimizer.exe." }
    return [ordered]@{ Variant = 'Folder'; Path = $Full; Name = $Name; Version = $Version; Sha256 = $Sha; Bytes = (New-Object System.IO.FileInfo($Full)).Length }
}

function Resolve-NetInstaller {
    # -InstallerPath -> opis instalatora (DSOptimizer-<wersja>-setup.exe obok SHA256SUMS.txt z tą samą sumą). Plik jest tylko czytany
    # (suma); instalator nigdy nie startuje na hoście. Każdy błąd = wyjątek, zanim cokolwiek dotknie VM.
    [CmdletBinding()]
    param([AllowEmptyString()][AllowNull()][string]$InstallerPath)
    if ([string]::IsNullOrWhiteSpace($InstallerPath)) { throw '-InstallerPath jest wymagany (DSOptimizer-<wersja>-setup.exe z tools\release\Build-Release.ps1).' }
    if ($InstallerPath -notmatch '^[A-Za-z]:\\' -and $InstallerPath -notmatch '^\\\\[^\\]+\\[^\\]+') { throw "-InstallerPath musi być ścieżką bezwzględną: '$InstallerPath'." }
    $Full = [System.IO.Path]::GetFullPath($InstallerPath)
    if (-not [System.IO.File]::Exists($Full)) { throw "Brak pliku instalatora '$Full'." }
    $Name = [System.IO.Path]::GetFileName($Full)
    if ($Name -cnotmatch '^DSOptimizer-(\d+\.\d+\.\d+)-setup\.exe\z') { throw "Plik '$Name' nie jest instalatorem (oczekiwano DSOptimizer-<wersja>-setup.exe)." }
    $Version = $Matches[1]
    $Sha = Assert-NetSha256Sums -FullPath $Full -Name $Name
    return [ordered]@{ Variant = 'Installer'; Path = $Full; Name = $Name; Version = $Version; Sha256 = $Sha; Bytes = (New-Object System.IO.FileInfo($Full)).Length }
}

function Get-NetLayout {
    # Układ katalogów w VM: wydanie Folder rozpakowane do C:\dso\app (jak reszta testów VM), instalator -> C:\Program Files\DSOptimizer
    # (folder ze spacją: ścieżka idzie wyłącznie do pól typu Execute/FilePath/LiteralPath, nigdy do składanej linii poleceń).
    [CmdletBinding()]
    param([AllowNull()]$Artifact)
    $Layout = Get-DsoVmLayout
    $Variant = if ($null -ne $Artifact -and $Artifact.Contains('Variant')) { [string]$Artifact['Variant'] } else { 'Folder' }
    if ($Variant -eq 'Installer') {
        $Layout['App'] = 'C:\Program Files\DSOptimizer'
        $Layout['Exe'] = 'C:\Program Files\DSOptimizer\DSOptimizer.exe'
    }
    return $Layout
}

function Get-NetRunArguments {
    # Argumenty przebiegu rozstrzygane przed jakimkolwiek dotknięciem VM; kolejność scenariuszy stała, duplikaty usunięte.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VmName,
        [Parameter(Mandatory)][string[]]$Scenario,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string]$CheckpointName,
        [Parameter(Mandatory)][int]$ReadyTimeoutSeconds,
        [AllowEmptyString()][AllowNull()][string]$ArtifactPath,
        [AllowEmptyString()][AllowNull()][string]$InstallerPath
    )
    $HasArtifact = -not [string]::IsNullOrWhiteSpace($ArtifactPath)
    $HasInstaller = -not [string]::IsNullOrWhiteSpace($InstallerPath)
    if ($HasArtifact -and $HasInstaller) { throw '-ArtifactPath i -InstallerPath wykluczają się: podaj dokładnie jeden.' }
    if (-not $HasArtifact -and -not $HasInstaller) { throw 'Podaj -ArtifactPath (zip Folder) albo -InstallerPath (DSOptimizer-<wersja>-setup.exe).' }
    $Order = @('start-on', 'start-off', 'cli')
    $Ordered = @($Order | Where-Object { $Scenario -contains $_ })
    if ($Ordered.Count -eq 0) { throw 'Podaj co najmniej jeden scenariusz: start-on, start-off, cli.' }
    # Teksty sondy w języku VM sprawdzone przed czymkolwiek innym (brak tłumaczenia = wyjątek także dla pl).
    $null = Get-NetNavTexts -Language (Get-DsoVmDefinition -VmName $VmName).Language
    $Artifact = if ($HasInstaller) { Resolve-NetInstaller -InstallerPath $InstallerPath } else { Resolve-NetArtifact -ArtifactPath $ArtifactPath }
    return [ordered]@{ VmName = $VmName; Scenarios = $Ordered; OutDirectory = $OutDirectory; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds
        Artifact = $Artifact; Layout = (Get-NetLayout -Artifact $Artifact); Plan = (Get-NetPlan -Language (Get-DsoVmDefinition -VmName $VmName).Language) }
}

# ---------------------------------------------------------------------------------------------------------------------
# Bloki wykonywane w VM (jako Administrator przez PowerShell Direct).
# ---------------------------------------------------------------------------------------------------------------------

$script:NetPrepareBlock = {
    param([string]$WorkDirectory, [string]$ResultDirectory, [string[]]$Providers, [string[]]$Services, [string]$AuditSubcategory)
    $ErrorActionPreference = 'Stop'
    $null = New-Item -ItemType Directory -Path $WorkDirectory -Force
    $null = New-Item -ItemType Directory -Path $ResultDirectory -Force
    Set-Content -LiteralPath (Join-Path $WorkDirectory 'providers.txt') -Value $Providers -Encoding Ascii
    $Report = @()
    foreach ($Service in $Services) {
        try {
            Stop-Service -Name $Service -Force -ErrorAction Stop
            Set-Service -Name $Service -StartupType Disabled -ErrorAction Stop
            $Report += "$Service stop+disabled"
        }
        catch { $Report += "$Service BLAD: $($_.Exception.Message)" }
    }
    # Audyt tworzenia procesów (4688) z linią poleceń: kontrola krzyżowa drzewa procesów (tylko w VM; revert checkpointu to cofa).
    # Podkategoria po GUID, więc niezależnie od języka systemu.
    $null = auditpol /set /subcategory:"$AuditSubcategory" /success:enable
    if ($LASTEXITCODE -ne 0) { throw "auditpol zakończył się kodem ${LASTEXITCODE}: audyt 4688 nie został włączony." }
    $AuditKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Policies\System\Audit'
    $null = New-Item -Path $AuditKey -Force
    Set-ItemProperty -Path $AuditKey -Name ProcessCreationIncludeCmdLine_Enabled -Value 1 -Type DWord
    $Report += 'audyt 4688 włączony'
    return $Report
}

$script:NetInstallBlock = {
    # Zip (już skopiowany do $WorkDirectory) -> weryfikacja SHA256 w VM -> C:\dso\app (układ jak reszta testów VM).
    param([string]$ZipPath, [string]$ExpectedSha256, [string]$AppDirectory, [string]$ExePath)
    $ErrorActionPreference = 'Stop'
    $Sha = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Sha -ne $ExpectedSha256) { throw "SHA256 zipa w VM ($Sha) różni się od oczekiwanego ($ExpectedSha256)." }
    $Staging = Join-Path (Split-Path -Parent $AppDirectory) 'net-unzip'
    foreach ($Path in @($Staging, $AppDirectory)) { if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force } }
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $Staging -Force
    Move-Item -LiteralPath (Join-Path $Staging 'DSOptimizer') -Destination $AppDirectory
    Remove-Item -LiteralPath $Staging -Recurse -Force
    if (-not (Test-Path -LiteralPath $ExePath)) { throw "Po rozpakowaniu brak $ExePath." }
    return (Get-Item -LiteralPath $ExePath).VersionInfo.FileVersion
}

$script:NetInstallerBlock = {
    # Instalator (już skopiowany do $WorkDirectory) -> weryfikacja SHA256 w VM -> cicha instalacja do Program Files (kod 0 wymagany).
    # Ścieżka $ExePath (folder ze spacją) trafia tylko do -LiteralPath; instalator leży w C:\dso\net (bez spacji), argumenty to tablica.
    param([string]$InstallerPath, [string]$ExpectedSha256, [string]$ExePath)
    $ErrorActionPreference = 'Stop'
    $Sha = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Sha -ne $ExpectedSha256) { throw "SHA256 instalatora w VM ($Sha) różni się od oczekiwanego ($ExpectedSha256)." }
    $Process = Start-Process -FilePath $InstallerPath -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-') -Wait -PassThru
    if ($Process.ExitCode -ne 0) { throw "Instalator zakończył się kodem $($Process.ExitCode) (oczekiwano 0)." }
    if (-not (Test-Path -LiteralPath $ExePath)) { throw "Po instalacji brak $ExePath." }
    return (Get-Item -LiteralPath $ExePath).VersionInfo.FileVersion
}

$script:NetSettingsBlock = {
    # settings.json z zaufanym właścicielem i DACL (Administratorzy + SYSTEM, bez dziedziczenia) w zaufanym katalogu - te same zasady
    # co SecureDirectory/JsonFileSettingsStore; inaczej program odłożyłby plik na bok jako niezaufany i użył wartości domyślnych.
    param([string]$Directory, [string]$Json)
    $ErrorActionPreference = 'Stop'
    $Admins = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    $System = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    if (-not (Test-Path -LiteralPath $Directory)) {
        $null = New-Item -ItemType Directory -Path $Directory
        $DirSecurity = New-Object System.Security.AccessControl.DirectorySecurity
        $DirSecurity.SetOwner($Admins)
        $DirSecurity.SetAccessRuleProtection($true, $false)
        foreach ($Sid in @($Admins, $System)) {
            $DirSecurity.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($Sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')))
        }
        (Get-Item -LiteralPath $Directory).SetAccessControl($DirSecurity)
    }
    $Path = Join-Path $Directory 'settings.json'
    [System.IO.File]::WriteAllText($Path, $Json, (New-Object System.Text.UTF8Encoding($false)))
    $FileSecurity = New-Object System.Security.AccessControl.FileSecurity
    $FileSecurity.SetOwner($Admins)
    $FileSecurity.SetAccessRuleProtection($true, $false)
    foreach ($Sid in @($Admins, $System)) { $FileSecurity.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($Sid, 'FullControl', 'Allow'))) }
    (Get-Item -LiteralPath $Path).SetAccessControl($FileSecurity)
    $Check = Get-Acl -LiteralPath $Path
    return [pscustomobject]@{ Path = $Path; Owner = [string]$Check.Owner; Protected = [bool]$Check.AreAccessRulesProtected; Rules = @($Check.Access | ForEach-Object { [string]$_.IdentityReference }); Json = (Get-Content -LiteralPath $Path -Raw) }
}

$script:NetWaitNetworkBlock = {
    # Czeka na samą łączność (adres IPv4 spoza 169.254 na interfejsie trasy domyślnej, a gdy trasa ma bramę - odpowiedź bramy na ping).
    # Celowo bez rozwiązywania nazw: rozgrzany cache DNS ukryłby zapytania programu i dał fałszywe "host nieustalony" w start-on.
    param([int]$WaitSeconds)
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $LastError = 'brak trasy domyślnej'
    while ($Watch.Elapsed.TotalSeconds -lt $WaitSeconds) {
        try {
            $Route = @(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop | Sort-Object RouteMetric | Select-Object -First 1)
            if ($Route.Count -gt 0) {
                $Address = @(Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $Route[0].InterfaceIndex -ErrorAction Stop | Where-Object { $_.IPAddress -notlike '169.254.*' })
                if ($Address.Count -eq 0) { $LastError = 'interfejs trasy domyślnej bez adresu IPv4' }
                else {
                    $Gateway = [string]$Route[0].NextHop
                    $Pinged = $true
                    if ($Gateway -and $Gateway -ne '0.0.0.0') {
                        $Pinged = $false
                        $Ping = New-Object System.Net.NetworkInformation.Ping
                        try { $Pinged = ($Ping.Send($Gateway, 2000).Status -eq 'Success') } finally { $Ping.Dispose() }
                        if (-not $Pinged) { $LastError = "brama $Gateway nie odpowiada" }
                    }
                    if ($Pinged) { return [pscustomobject]@{ Ready = $true; Seconds = [int]$Watch.Elapsed.TotalSeconds; Gateway = $Gateway; Address = [string]$Address[0].IPAddress; Error = $null } }
                }
            }
        }
        catch { $LastError = $_.Exception.Message }
        Start-Sleep -Seconds 3
    }
    return [pscustomobject]@{ Ready = $false; Seconds = [int]$Watch.Elapsed.TotalSeconds; Gateway = $null; Address = $null; Error = $LastError }
}

$script:NetStartTraceBlock = {
    param([string]$Name, [string]$WorkDirectory)
    $ErrorActionPreference = 'Stop'
    $Session = "DsoNet-$Name"
    $Etl = Join-Path $WorkDirectory "$Name.etl"
    $null = cmd /c "logman stop $Session -ets 2>&1"
    if (Test-Path -LiteralPath $Etl) { Remove-Item -LiteralPath $Etl -Force }
    Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $WorkDirectory "$Name-procs-start.json") -Encoding UTF8
    $Since = (Get-Date).ToString('o')
    $Text = logman start $Session -pf (Join-Path $WorkDirectory 'providers.txt') -o $Etl -ets -bs 1024 -nb 128 640 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "logman start zakończył się kodem ${LASTEXITCODE}: $Text" }
    return [pscustomobject]@{ Since = $Since; Logman = $Text }
}

$script:NetCanaryBlock = {
    # Kontrolny ruch osobnego procesu (rodzic: sesja PowerShell Direct, poza drzewem programu): zapytanie DNS i połączenie TCP do znanego
    # hosta. Werdykt wymaga, żeby ten ruch był widoczny w śladzie; inaczej ślad mógł nic nie łapać.
    param([string]$HostName, [int]$Port, [int]$TimeoutSeconds)
    $ErrorActionPreference = 'Stop'
    $Code = "try { `$null = [System.Net.Dns]::GetHostAddresses('$HostName'); `$Client = New-Object System.Net.Sockets.TcpClient; `$Client.Connect('$HostName', $Port); `$Client.Close(); exit 0 } catch { exit 1 }"
    $Encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($Code))
    $PowerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $Process = Start-Process -FilePath $PowerShellPath -ArgumentList @('-NoProfile', '-NonInteractive', '-EncodedCommand', $Encoded) -PassThru -WindowStyle Hidden
    $null = $Process.Handle
    $Done = $Process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $Done) { try { $Process.Kill() } catch { $null = $_ } }
    # Chwila na zapis zdarzeń do bufora śladu przed zatrzymaniem.
    Start-Sleep -Seconds 3
    return [pscustomobject]@{ Pid = $Process.Id; TimedOut = (-not $Done); ExitCode = $(if ($Done) { $Process.ExitCode } else { -1 }) }
}

$script:NetStopTraceBlock = {
    param([string]$Name, [string]$WorkDirectory, [string]$ResultDirectory, [string]$ParseScript, [string]$Since)
    $ErrorActionPreference = 'Stop'
    $Session = "DsoNet-$Name"
    # Surowy opis sesji do dokumentacji; liczby utrat NIE są z niego parsowane (etykiety zależą od języka logman).
    $Query = logman query $Session -ets | Out-String
    Set-Content -LiteralPath (Join-Path $ResultDirectory "$Name-logman.txt") -Value $Query -Encoding UTF8
    # Utracone bufory i zdarzenia: EVENT_TRACE_PROPERTIES z ControlTraceW(EVENT_TRACE_CONTROL_QUERY) - liczby, bez etykiet. Get-EtwTraceSession
    # nie wystawia tych liczników. Offsety x64: EventsLost 88, LogBuffersLost 96, RealTimeBuffersLost 100.
    $Loss = [ordered]@{ BuffersLost = $null; EventsLost = $null; RealTimeBuffersLost = $null; Error = $null }
    try {
        if ([IntPtr]::Size -ne 8) { throw 'Odczyt utrat zakłada 64-bitowy proces.' }
        if (-not ('DsoEtwQuery' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DsoEtwQuery
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ControlTraceW(ulong traceId, string instanceName, IntPtr properties, uint controlCode);

    public static uint[] Query(string name)
    {
        const int Header = 120;
        int total = Header + 2 * 1024 * 2;
        IntPtr buffer = Marshal.AllocHGlobal(total);
        try
        {
            Marshal.Copy(new byte[total], 0, buffer, total);
            Marshal.WriteInt32(buffer, 0, total);
            Marshal.WriteInt32(buffer, 44, 0x00020000);
            Marshal.WriteInt32(buffer, 112, Header + 2 * 1024);
            Marshal.WriteInt32(buffer, 116, Header);
            uint status = ControlTraceW(0UL, name, buffer, 0);
            return new uint[] { status, (uint)Marshal.ReadInt32(buffer, 88), (uint)Marshal.ReadInt32(buffer, 96), (uint)Marshal.ReadInt32(buffer, 100) };
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
'@
        }
        $Result = [DsoEtwQuery]::Query($Session)
        if ($Result[0] -ne 0) { throw "ControlTraceW(QUERY) zwróciło kod $($Result[0])." }
        $Loss.EventsLost = [int64]$Result[1]
        $Loss.BuffersLost = [int64]$Result[2]
        $Loss.RealTimeBuffersLost = [int64]$Result[3]
    }
    catch { $Loss.Error = $_.Exception.Message }
    $StopText = logman stop $Session -ets | Out-String
    Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $WorkDirectory "$Name-procs-end.json") -Encoding UTF8
    # Audyt 4688 od startu śladu (indeksy pól jak w metodzie archiwalnej: 4 = NewProcessId, 5 = NewProcessName, 7 = ProcessId rodzica, 8 = linia poleceń).
    $AuditError = $null
    $Audit = @()
    try {
        $Raw = @(Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4688; StartTime = [datetime]$Since } -ErrorAction Stop | Sort-Object TimeCreated)
        $Audit = @(foreach ($Event in $Raw) {
                $P = $Event.Properties
                [ordered]@{ time = $Event.TimeCreated.ToUniversalTime().ToString('o'); newPid = [Convert]::ToInt64([string]$P[4].Value, 10); image = [string]$P[5].Value
                    parentPid = [Convert]::ToInt64([string]$P[7].Value, 10); commandLine = [string]$P[8].Value }
            })
    }
    catch {
        if ([string]$_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { $AuditError = $_.Exception.Message }
    }
    ConvertTo-Json -InputObject @($Audit) -Depth 4 | Set-Content -LiteralPath (Join-Path $ResultDirectory "$Name-4688.json") -Encoding UTF8
    Set-ExecutionPolicy -Scope Process Bypass -Force
    $Parse = & $ParseScript -EtlPath (Join-Path $WorkDirectory "$Name.etl") -SnapshotPath (Join-Path $WorkDirectory "$Name-procs-start.json") -OutPath (Join-Path $ResultDirectory "$Name.json")
    return [pscustomobject]@{ Stop = $StopText; Parse = ($Parse | Out-String).Trim(); Loss = [pscustomobject]$Loss; AuditCount = @($Audit).Count; AuditError = $AuditError }
}

$script:NetStopGuiBlock = {
    param([string]$TaskName)
    Get-Process -Name 'DSOptimizer' -ErrorAction SilentlyContinue | Stop-Process -Force
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------------------------------------------------
# Przebieg na VM.
# ---------------------------------------------------------------------------------------------------------------------

function Read-NetJsonFile {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if (-not [System.IO.File]::Exists($Path)) { return $null }
    return ([System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF) | ConvertFrom-Json)
}

function Invoke-NetScenario {
    # Jeden scenariusz na już uruchomionej VM (sesja Administratora); sieć podłączana tu (PRZED uruchomieniem programu) i odłączana w finally.
    # Zwraca rekord z werdyktem. Zakres: drzewo procesów programu.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Scenario,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Context,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Record
    )
    $Session = $Context.Session
    $Plan = $Context.Plan
    $Layout = $Context.Layout
    $VmName = $Context.VmName
    $Name = $Scenario -replace '[^A-Za-z0-9]', ''
    $Record.name = $Name

    $Record.services = @(Invoke-Command -Session $Session -ScriptBlock $script:NetPrepareBlock -ArgumentList @($Plan.WorkDirectory, $Plan.ResultDirectory, @($Plan.Providers), @($Plan.StoppedServices), $Plan.AuditSubcategory))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'net\Parse-NetTrace.ps1') -Destination (Join-Path $Plan.WorkDirectory 'Parse-NetTrace.ps1') -ToSession $Session -Force
    $RemoteZip = Join-Path $Plan.WorkDirectory $Context.Artifact.Name
    Copy-Item -LiteralPath $Context.Artifact.Path -Destination $RemoteZip -ToSession $Session -Force
    if ($Context.Artifact['Variant'] -eq 'Installer') {
        $Record.appVersion = [string](Invoke-Command -Session $Session -ScriptBlock $script:NetInstallerBlock -ArgumentList @($RemoteZip, $Context.Artifact.Sha256, $Layout.Exe) | Select-Object -Last 1)
    }
    else {
        $Record.appVersion = [string](Invoke-Command -Session $Session -ScriptBlock $script:NetInstallBlock -ArgumentList @($RemoteZip, $Context.Artifact.Sha256, $Layout.App, $Layout.Exe) | Select-Object -Last 1)
    }

    $SettingsJson = if ($Scenario -eq 'start-off') { $Plan.SettingsOff } else { $Plan.SettingsOn }
    $Record.settings = Invoke-Command -Session $Session -ScriptBlock $script:NetSettingsBlock -ArgumentList @($Plan.SettingsDirectory, $SettingsJson)

    # Sieć w KAŻDYM scenariuszu i przed uruchomieniem programu: brak ruchu nie może wynikać z braku sieci, a ruch kontrolny wymaga sieci.
    Connect-VMNetworkAdapter -VMName $VmName -SwitchName $Plan.NetworkSwitch -ErrorAction Stop
    $Context.Timeline += "$(Get-Date -Format 'HH:mm:ss') $Scenario sieć podłączona"
    try {
        $Record.network = Invoke-Command -Session $Session -ScriptBlock $script:NetWaitNetworkBlock -ArgumentList @([int]$Plan.NetworkWaitSeconds)
        if (-not $Record.network.Ready) { throw "Brak łączności w VM po $($Record.network.Seconds) s ($($Record.network.Error))." }

        $Trace = Invoke-Command -Session $Session -ScriptBlock $script:NetStartTraceBlock -ArgumentList @($Name, $Plan.WorkDirectory)
        $Record.traceSince = $Trace.Since
        $TraceStopped = $false
        try {
            if ($Scenario -eq 'cli') {
                $null = Invoke-Command -Session $Session -ScriptBlock { param($Directory) $null = New-Item -ItemType Directory -Path $Directory -Force } -ArgumentList @($Plan.CliDirectory)
                $Runs = @()
                foreach ($Command in $Plan.CliCommands) {
                    $OutPath = Join-Path $Plan.CliDirectory (($Command -replace '^-+', '') + '.json')
                    $Cli = Invoke-DsoCli -Session $Session -ExePath $Layout.Exe -Arguments @($Command, '--out', $OutPath) -OutPath $OutPath -TimeoutSeconds ([int]$Plan.CliTimeoutSeconds) -RunAsUser $Plan.UserName -Language $Plan.Language
                    $Runs += [ordered]@{ command = $Command; exitCode = $Cli.ExitCode; timedOut = $Cli.TimedOut; reportBytes = ([string]$Cli.Json).Length }
                    $CliProblem = Get-NetCliProblem -Command $Command -Result $Cli
                    if ($CliProblem) { $Record.problems += $CliProblem }
                }
                $Record.cli = $Runs
            }
            else {
                $Record.guiStart = Start-DsoGuiInUserSession -Session $Session -Layout $Layout -UserName $Plan.UserName -TaskName $Plan.GuiTaskName
                $ProbeText = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'net\NavProbe.ps1'), [System.Text.Encoding]::UTF8)
                $TextsName = 'net-nav-texts.json'
                $NavTexts = [string]$Context.NavTexts
                $Json = Invoke-DsoUserSessionScript -Session $Session -Layout $Layout -Name 'Net-Nav' -ScriptText $ProbeText -UserName $Plan.UserName -TimeoutSeconds ([int]$Plan.NavTimeoutSeconds) `
                    -ArgumentText "-TextsFile $($Layout.Out)\$TextsName" -DataFiles @{ $TextsName = $NavTexts }
                $Record.nav = if ($Json) { $Json | ConvertFrom-Json } else { $null }
                if ($null -eq $Record.nav -or $Record.nav.Ok -ne $true) { $Record.problems += "sonda nawigacji nie przeszła wszystkich stron: $(if ($Record.nav) { $Record.nav.Error } else { 'brak wyniku' })" }
            }
            Start-Sleep -Seconds ([int]$Plan.SettleBeforeStopSeconds)
            # Ruch kontrolny tuż przed zatrzymaniem śladu (osobny proces spoza drzewa programu).
            $Record.canary = Invoke-Command -Session $Session -ScriptBlock $script:NetCanaryBlock -ArgumentList @([string]$Plan.CanaryHost, [int]$Plan.CanaryPort, [int]$Plan.CanaryTimeoutSeconds)
            if ($Record.canary.TimedOut -or $Record.canary.ExitCode -ne 0) { $Record.problems += "ruch kontrolny ($($Plan.CanaryHost)) nie powiódł się: kod $($Record.canary.ExitCode), przekroczony czas: $($Record.canary.TimedOut)" }
        }
        finally {
            try {
                $Stop = Invoke-Command -Session $Session -ScriptBlock $script:NetStopTraceBlock -ArgumentList @($Name, $Plan.WorkDirectory, $Plan.ResultDirectory, (Join-Path $Plan.WorkDirectory 'Parse-NetTrace.ps1'), $Trace.Since)
                $TraceStopped = $true
                $Record.parse = $Stop.Parse
                $Record.loss = $Stop.Loss
                $Record.auditCount = $Stop.AuditCount
                if ($Stop.AuditError) { $Record.problems += "odczyt audytu 4688: $($Stop.AuditError)" }
                foreach ($LossProblem in @(Get-NetLossProblem -Loss $Stop.Loss)) { if ($LossProblem) { $Record.problems += $LossProblem } }
            }
            catch { $Record.problems += "zatrzymanie śladu: $($_.Exception.Message)" }
            if ($Scenario -ne 'cli') {
                try { Invoke-Command -Session $Session -ScriptBlock $script:NetStopGuiBlock -ArgumentList @($Plan.GuiTaskName) }
                catch { $Record.problems += "zamknięcie okna: $($_.Exception.Message)" }
            }
        }
        if (-not $TraceStopped) { return }
    }
    finally {
        try { Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop; $Context.Timeline += "$(Get-Date -Format 'HH:mm:ss') $Scenario sieć odłączona" }
        catch { $Record.problems += "odłączenie sieci: $($_.Exception.Message)" }
    }

    # Surowe pliki (JSON śladu, audyt 4688, wynik sondy, raporty --out); sam ETL zostaje w VM i przepada z revertem.
    $RawDirectory = Join-Path $Context.VmDirectory "raw-$Scenario"
    $Raw = @(Copy-ExploreRawFiles -Session $Session -Destination $RawDirectory -Source ([string]$Plan.ResultDirectory))
    $Record.rawFiles = @($Raw | ForEach-Object { Split-Path -Leaf $_ })
    $TraceObject = Read-NetJsonFile -Path (Join-Path $RawDirectory "$Name.json")
    if ($null -eq $TraceObject) { throw "Brak zebranego śladu $Name.json po parsowaniu." }
    $Verdict = Get-NetVerdict -Scenario $Scenario -Trace $TraceObject
    $Record.passed = [bool]$Verdict.Passed
    $Record.detail = [string]$Verdict.Detail
    $Record.dsoTreeDns = @($TraceObject.dsoTreeDns | ForEach-Object { $_.name } | Sort-Object -Unique)
    $Record.dsoTreeConnections = @($TraceObject.dsoTreeConnections | ForEach-Object { '{0} {1}:{2} {3}' -f $_.proto, $_.remote, $_.remotePort, (@($_.hosts) -join '/') } | Sort-Object -Unique)
    # Kontrola krzyżowa: drzewo procesów z audytu 4688 kontra ślad; rozbieżność w którąkolwiek stronę = porażka.
    $AuditObject = Read-NetJsonFile -Path (Join-Path $RawDirectory "$Name-4688.json")
    $Cross = Compare-NetProcessAudit -Audit $AuditObject -TreeProcesses $TraceObject.dsoTreeProcesses
    $Record.processAudit = $Cross
    if (-not $Cross.Match) { $Record.passed = $false; $Record.problems += "kontrola krzyżowa 4688: $($Cross.Detail)" }
}

function Invoke-NetVmRun {
    # Jedna VM, scenariusze po kolei; KAŻDY scenariusz: revert -> karta odłączona -> start -> gotowość (zalogowany dso) -> pomiar ->
    # ZAWSZE revert, karta odłączona, stan końcowy sprawdzony (Complete-ExploreVmCleanup). Na końcu zapis net.json.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro', 'DSO-PL-Home')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string[]]$Scenarios,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Artifact,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200,
        [string]$TranslationsRoot
    )
    Assert-DsoVmLiveAllowed
    $Language = (Get-DsoVmDefinition -VmName $VmName).Language
    # Teksty sondy przed jakimkolwiek dotknięciem VM: brak tłumaczenia = wyjątek, zanim ruszy revert albo start.
    $NavTexts = Get-NetNavTexts -Language $Language -Root $TranslationsRoot
    $Credential = Get-DsoVmCredential -VmName $VmName
    $Plan = Get-NetPlan -Language $Language
    $StartedAt = Get-Date
    $Results = [ordered]@{}
    $Errors = @()
    $Timeline = @()
    $VmDirectory = Join-Path $OutDirectory $VmName
    $Layout = Get-NetLayout -Artifact $Artifact
    $AuthFailure = $null
    foreach ($Scenario in $Scenarios) {
        $Record = [ordered]@{ scenario = $Scenario; passed = $false; detail = $null; error = $null; problems = @() }
        $Results[$Scenario] = $Record
        $Session = $null
        $Cleanup = $null
        try {
            Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
            # VM nigdy nie startuje z siecią (checkpoint mógł mieć kartę na przełączniku).
            Disconnect-VMNetworkAdapter -VMName $VmName -ErrorAction Stop
            Start-VM -Name $VmName
            $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
            $Timeline += "$(Get-Date -Format 'HH:mm:ss') $Scenario VM gotowa"
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $VmName $Scenario"
            # Chwila na dokończenie logowania dso (powłoka, pakiety) przed pierwszym zadaniem w jego sesji.
            Start-Sleep -Seconds ([int]$Plan.SettleAfterLogonSeconds)
            $Context = [ordered]@{ Session = $Session; Plan = $Plan; Layout = $Layout; VmName = $VmName; VmDirectory = $VmDirectory; Artifact = $Artifact; Timeline = $Timeline; NavTexts = $NavTexts }
            try { Invoke-NetScenario -Scenario $Scenario -Context $Context -Record $Record }
            finally { $Timeline = $Context.Timeline }
        }
        catch {
            $Record.error = $_.Exception.Message
            $Record.passed = $false
            if (Test-VmAuthFailure -Exception $_.Exception) { $AuthFailure = $_ }
        }
        finally {
            if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
            $Cleanup = Complete-ExploreVmCleanup -VmName $VmName -CheckpointName $CheckpointName
            $Record.cleanup = $Cleanup
            if (-not $Cleanup.Ok) {
                Write-Warning "VM '$VmName': sprzątanie: $(@($Cleanup.Problems) -join ' | ')"
                $Record.problems += "sprzątanie VM: $(@($Cleanup.Problems) -join ' | ')"
            }
        }
        # Jakikolwiek problem (także odczytu utrat, audytu, ruchu kontrolnego, CLI) odbiera zaliczenie.
        if (@($Record.problems | Where-Object { $_ }).Count -gt 0 -or $Record.error) { $Record.passed = $false }
        if ($Record.error) { $Errors += "${Scenario}: $($Record.error)" }
        foreach ($Problem in @($Record.problems)) { if ($Problem) { $Errors += "${Scenario}: $Problem" } }
        Write-Host ("{0}: {1} - {2}" -f $Scenario, $(if ($Record.passed) { 'PASS' } else { 'FAIL' }), $Record.detail)
        # Błąd uwierzytelnienia przerywa całość (blokada konta Administrator w VM).
        if ($null -ne $AuthFailure) { break }
    }
    $Failed = @($Results.Values | Where-Object { -not $_.passed } | ForEach-Object { $_.scenario })
    $Result = [ordered]@{ schema = 1; vmName = $VmName; language = $Language; checkpoint = $CheckpointName; variant = $(if ($Artifact.Contains('Variant')) { [string]$Artifact['Variant'] } else { 'Folder' }); startedAt = $StartedAt.ToString('o'); finishedAt = (Get-Date).ToString('o')
        scope = [string]$Plan.Scope; artifact = $Artifact; allPassed = ($Failed.Count -eq 0 -and $Errors.Count -eq 0 -and $Results.Count -eq $Scenarios.Count); errors = $Errors; timeline = $Timeline; scenarios = $Results }
    $Saved = Save-ExploreResult -Result $Result -OutDirectory $OutDirectory -VmName $VmName -FileName 'net.json'
    if ($null -ne $AuthFailure) { throw $AuthFailure }
    if (-not $Result.allPassed) {
        $Why = if ($Errors.Count -gt 0) { $Errors -join ' | ' } else { "werdykt nie przeszedł: $($Failed -join ', ')" }
        throw "VM '$VmName': $Why (wynik: $Saved)"
    }
    return $Saved
}

# Przy dot-sourcingu (testy) tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    $ExitCode = 1
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1')
        $RepoRoot = Get-DsoRepoRoot
        $OutDirectory = Assert-ExploreOutDirectory -Path $OutDirectory -RepoRoot $RepoRoot -Example '$env:TEMP\dso-explore\4.0.3\siec\pro'
        $OutDirectory = Resolve-ExploreOutDirectory -Path $OutDirectory
        $RunArguments = Get-NetRunArguments -VmName $VmName -Scenario $Scenario -OutDirectory $OutDirectory -CheckpointName $CheckpointName `
            -ReadyTimeoutSeconds $ReadyTimeoutSeconds -ArtifactPath $ArtifactPath -InstallerPath $InstallerPath
        if ($ShowPlan) {
            [pscustomobject]$RunArguments | ConvertTo-Json -Depth 6
            exit 0
        }
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon i VmSmoke trzymają referencję do starej instancji VmHost; ponowny import po nim wiąże je z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmSmoke.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        Write-Host "Wyniki: $OutDirectory"
        Write-Host "Wydanie ($($RunArguments.Artifact.Variant)): $($RunArguments.Artifact.Path) (SHA256 $($RunArguments.Artifact.Sha256))"
        Write-Host "Zakres: $($RunArguments.Plan.Scope)"
        Write-Host "VM: $VmName; scenariusze: $(@($RunArguments.Scenarios) -join ', ')"
        $Saved = Invoke-NetVmRun -VmName $VmName -OutDirectory $OutDirectory -Scenarios @($RunArguments.Scenarios) -Artifact $RunArguments.Artifact `
            -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
        Write-Host "Zapisano: $Saved"
        $ExitCode = 0
    }
    catch {
        Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
        $ExitCode = 1
    }
    exit $ExitCode
}
