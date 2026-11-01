<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm, autologon 'dso').
    Jedna VM na proces. Wydanie: zip z tools\release\Build-Release.ps1 -Variant Folder (DSOptimizer-<wersja>.zip, obok SHA256SUMS.txt), np.:
        .\tools\release\Build-Release.ps1 -Variant Folder -OutRoot $env:TEMP\dso-explore\4.0.3\wydania
        .\tests\vm\explore\Invoke-NetExploration.ps1 -VmName DSO-26H2-Pro -Scenario start-on,start-off,cli `
            -ArtifactPath <katalog wydania>\DSOptimizer-4.0.3.zip -OutDirectory $env:TEMP\dso-explore\4.0.3\siec\pro
    -Scenario: start-on, start-off, cli (domyślnie wszystkie trzy, w tej kolejności).
    -ShowPlan: wypisuje rozstrzygnięte argumenty i plan (JSON) i kończy, nic nie uruchamia.
    -OutDirectory jest wymagany: ścieżka bezwzględna poza repo; w repo tylko katalog ignorowany przez git.
    Czas: ok. 10-15 min na scenariusz GUI (start-on, start-off) i ok. 5 min na cli; każdy scenariusz na świeżo przywróconej VM.
Opis działania:
    Pomiar połączeń programu (zasada: przy starcie wyłącznie api.github.com, po wyłączeniu sprawdzania i w trybie wiersza poleceń zero).
    Port metody z audytu sieci: ślad ETW w VM (logman) z dostawcami Microsoft-Windows-Kernel-Network, Microsoft-Windows-Kernel-Process
    i Microsoft-Windows-DNS-Client, parser net\Parse-NetTrace.ps1 przypisuje ruch do drzewa procesów DSOptimizer.exe (PID rodzica,
    potomkowie dowolnej głębokości) i do zapytań DNS z tego drzewa. Sieć: Default Switch, podłączona dopiero po starcie VM i na czas
    scenariusza. W VM zatrzymane wuauserv, UsoSvc, DoSvc i BITS (usługi Windows pobierające w tle zalewały ślad i nie należą do programu).
    Scenariusze (każdy: revert do checkpointu, start VM bez sieci, wydanie z -ArtifactPath rozpakowane do C:\dso\app, konto 'dso'):
      start-on  - settings.json z {"schemaVersion":1,"language":"pl"} (pole checkUpdatesAtStart nieobecne = test wartości domyślnej),
                  start okna i net\NavProbe.ps1 po wszystkich stronach bez klikania przycisków;
      start-off - jak start-on, ale settings.json dodatkowo z "checkUpdatesAtStart":false;
      cli       - settings.json jak w start-on (sprawdzanie włączone), potem --list-install i --list-apps.
    settings.json jest zapisywany przed startem z zaufanym właścicielem i DACL (Administratorzy + SYSTEM), inaczej program odłożyłby go
    na bok jako niezaufany.
    Werdykt liczy czysta funkcja Get-NetVerdict -Scenario <nazwa> -Trace <obiekt z dsoTreeDns i dsoTreeConnections> -> { Passed; Detail }:
      start-on: co najmniej jedno połączenie TCP 443 do api.github.com, zapytania DNS drzewa wyłącznie o api.github.com, brak innych
                połączeń (brak połączenia = nie przechodzi, bo sprawdzenie musiało się wykonać);
      start-off i cli: zero zapytań DNS i zero połączeń drzewa programu.
    Ślad bez zdarzeń, bez procesu programu albo z utraconymi buforami nie przechodzi w żadnym scenariuszu.
    Ograniczenie: pomiar mówi z kim i ile bajtów, nie co (patrz audyt sieci); ruch Windows poza drzewem programu nie jest liczony.
    RISK: skrypt przywraca VM do checkpointu 'czysta-26H2' przed każdym scenariuszem i po nim (zmiany w VM przepadają) oraz na czas
    scenariusza podłącza kartę VM do Default Switch (ruch NAT na hoście); po pracy karta jest odłączona, a VM wyłączona. Nie usuwa VM.
    Wynik: <OutDirectory>\<VmName>\net.json (UTF-8), surowe pliki scenariusza (ślad sieci w JSON, wynik sondy, raporty --out)
    w <OutDirectory>\<VmName>\raw-<scenariusz>.
    Kod wyjścia: 0 gdy wszystkie wybrane scenariusze zebrane bez błędu i z werdyktem Passed, 1 w przeciwnym razie (wynik i tak w net.json).
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName = 'DSO-26H2-Pro',
    [ValidateSet('start-on', 'start-off', 'cli')][string[]]$Scenario = @('start-on', 'start-off', 'cli'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200,
    [switch]$ShowPlan,
    # Plik wydania Folder (DSOptimizer-<wersja>.zip z Build-Release.ps1), obok SHA256SUMS.txt.
    [string]$ArtifactPath
)

Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\VmSmoke.psm1')

# ---------------------------------------------------------------------------------------------------------------------
# Plan i werdykt (czyste funkcje, bez dotykania VM).
# ---------------------------------------------------------------------------------------------------------------------

function Get-NetPlan {
    [CmdletBinding()]
    param()
    return [ordered]@{
        AllowedHost         = 'api.github.com'
        AllowedPort         = 443
        NetworkName         = 'api.github.com'
        NetworkSwitch       = 'Default Switch'
        NetworkWaitSeconds  = 300
        UserName            = 'dso'
        # Usługi Windows pobierające w tle (nie należą do programu); zatrzymane przed śladem.
        StoppedServices     = @('wuauserv', 'UsoSvc', 'DoSvc', 'BITS')
        WorkDirectory       = 'C:\dso\net'
        ResultDirectory     = 'C:\dso\net\out'
        CliDirectory        = 'C:\dso\net\out'
        SettingsDirectory   = 'C:\ProgramData\DSOptimizer'
        SettingsOn          = '{"schemaVersion":1,"language":"pl"}'
        SettingsOff         = '{"schemaVersion":1,"language":"pl","checkUpdatesAtStart":false}'
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

function Get-NetVerdict {
    # Czysta funkcja werdyktu: Scenario + obiekt śladu (pola dsoTreeDns, dsoTreeConnections z Parse-NetTrace.ps1) -> { Passed; Detail }.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('start-on', 'start-off', 'cli')][string]$Scenario,
        [Parameter(Mandatory)][AllowNull()]$Trace
    )
    $Plan = Get-NetPlan
    $Allowed = [string]$Plan.AllowedHost
    $Fail = { param([string]$Text) [pscustomobject]@{ Passed = $false; Detail = $Text } }
    $Dns = Get-NetTraceField -Trace $Trace -Name 'dsoTreeDns'
    $Connections = Get-NetTraceField -Trace $Trace -Name 'dsoTreeConnections'
    if ($null -eq $Dns -or $null -eq $Connections) { return (& $Fail 'Ślad bez pól dsoTreeDns albo dsoTreeConnections: pomiar się nie liczy.') }
    # Ślad pusty albo bez procesu programu nie dowodzi niczego (brak ruchu mógłby znaczyć, że ślad nic nie łapał).
    $EventCount = $Trace.PSObject.Properties['eventCount']
    if ($null -ne $EventCount -and [int64]$EventCount.Value -le 0) { return (& $Fail 'Ślad nie zawiera żadnych zdarzeń: pomiar się nie liczy.') }
    $Processes = Get-NetTraceField -Trace $Trace -Name 'dsoTreeProcesses'
    if ($null -ne $Processes -and $Processes.Count -eq 0) { return (& $Fail 'W śladzie nie ma procesu DSOptimizer.exe: pomiar się nie liczy.') }

    $DnsNames = @($Dns | ForEach-Object { Format-NetHost (Get-NetItemValue -Item $_ -Name 'name') } | Where-Object { $_ } | Sort-Object -Unique)
    $Describe = {
        param($Connection)
        $Hosts = @(@(Get-NetItemValue -Item $Connection -Name 'hosts') | Where-Object { $_ } | ForEach-Object { Format-NetHost $_ })
        $HostText = if ($Hosts.Count -gt 0) { $Hosts -join '/' } else { '(host nieustalony)' }
        '{0} {1}:{2} {3}' -f (Get-NetItemValue -Item $Connection -Name 'proto'), (Get-NetItemValue -Item $Connection -Name 'remote'), (Get-NetItemValue -Item $Connection -Name 'remotePort'), $HostText
    }

    if ($Scenario -ne 'start-on') {
        if ($Dns.Count -eq 0 -and $Connections.Count -eq 0) { return [pscustomobject]@{ Passed = $true; Detail = 'Zero zapytań DNS i zero połączeń w drzewie programu.' } }
        $Parts = @()
        if ($Dns.Count -gt 0) { $Parts += "zapytania DNS: $($DnsNames -join ', ')" }
        if ($Connections.Count -gt 0) { $Parts += "połączenia: $((@($Connections | ForEach-Object { & $Describe $_ }) | Sort-Object -Unique) -join '; ')" }
        return (& $Fail "Program nie powinien łączyć się z siecią w scenariuszu '$Scenario', a w śladzie jest: $($Parts -join ' | ').")
    }

    if ($Connections.Count -eq 0) {
        return (& $Fail "Brak jakiegokolwiek połączenia programu: sprawdzenie aktualizacji ($Allowed) nie wykonało się albo ślad go nie złapał.")
    }
    $Extra = @()
    $Good = 0
    foreach ($Connection in $Connections) {
        $Hosts = @(@(Get-NetItemValue -Item $Connection -Name 'hosts') | Where-Object { $_ } | ForEach-Object { Format-NetHost $_ })
        $IsGood = ([string](Get-NetItemValue -Item $Connection -Name 'proto') -ceq 'TCP') -and ([int](Get-NetItemValue -Item $Connection -Name 'remotePort') -eq [int]$Plan.AllowedPort) -and ($Hosts -contains $Allowed)
        if ($IsGood) { $Good++ } else { $Extra += (& $Describe $Connection) }
    }
    $OtherDns = @($DnsNames | Where-Object { $_ -ne $Allowed })
    if ($Extra.Count -gt 0 -or $OtherDns.Count -gt 0) {
        $Parts = @()
        if ($Extra.Count -gt 0) { $Parts += "dodatkowe połączenia: $((@($Extra) | Sort-Object -Unique) -join '; ')" }
        if ($OtherDns.Count -gt 0) { $Parts += "dodatkowe zapytania DNS: $($OtherDns -join ', ')" }
        return (& $Fail "Dozwolone jest wyłącznie $Allowed na porcie $($Plan.AllowedPort), a w śladzie są: $($Parts -join ' | ').")
    }
    if ($Good -eq 0) { return (& $Fail "Brak połączenia TCP $($Plan.AllowedPort) do $Allowed.") }
    return [pscustomobject]@{ Passed = $true; Detail = "Wyłącznie $Allowed na porcie $($Plan.AllowedPort) ($Good połączeń w drzewie programu)." }
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
    $Sums = Join-Path ([System.IO.Path]::GetDirectoryName($Full)) 'SHA256SUMS.txt'
    if (-not [System.IO.File]::Exists($Sums)) { throw "Brak '$Sums' obok pliku wydania." }
    $Expected = $null
    foreach ($Line in [System.IO.File]::ReadAllLines($Sums)) {
        if ($Line -match '^([0-9a-fA-F]{64}) [ *](.+)$' -and $Matches[2] -ceq $Name) { $Expected = $Matches[1].ToLowerInvariant() }
    }
    if ($null -eq $Expected) { throw "SHA256SUMS.txt nie ma sumy pliku '$Name'." }
    $Sha = (Get-FileHash -LiteralPath $Full -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
    if ($Sha -cne $Expected) { throw "SHA256 pliku '$Name' ($Sha) różni się od SHA256SUMS.txt ($Expected)." }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $Zip = [System.IO.Compression.ZipFile]::OpenRead($Full)
    try { $HasExe = @($Zip.Entries | Where-Object { ($_.FullName -replace '\\', '/') -ceq 'DSOptimizer/DSOptimizer.exe' }).Count -eq 1 }
    finally { $Zip.Dispose() }
    if (-not $HasExe) { throw "Zip '$Name' nie ma DSOptimizer\DSOptimizer.exe." }
    return [ordered]@{ Variant = 'Folder'; Path = $Full; Name = $Name; Version = $Version; Sha256 = $Sha; Bytes = (New-Object System.IO.FileInfo($Full)).Length }
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
        [AllowEmptyString()][AllowNull()][string]$ArtifactPath
    )
    $Order = @('start-on', 'start-off', 'cli')
    $Ordered = @($Order | Where-Object { $Scenario -contains $_ })
    if ($Ordered.Count -eq 0) { throw 'Podaj co najmniej jeden scenariusz: start-on, start-off, cli.' }
    return [ordered]@{ VmName = $VmName; Scenarios = $Ordered; OutDirectory = $OutDirectory; CheckpointName = $CheckpointName; ReadyTimeoutSeconds = $ReadyTimeoutSeconds
        Artifact = (Resolve-NetArtifact -ArtifactPath $ArtifactPath); Plan = (Get-NetPlan) }
}

# ---------------------------------------------------------------------------------------------------------------------
# Bloki wykonywane w VM (jako Administrator przez PowerShell Direct).
# ---------------------------------------------------------------------------------------------------------------------

$script:NetPrepareBlock = {
    param([string]$WorkDirectory, [string]$ResultDirectory, [string[]]$Providers, [string[]]$Services)
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
    param([string]$Name, [int]$WaitSeconds)
    $Watch = [System.Diagnostics.Stopwatch]::StartNew()
    $LastError = $null
    while ($Watch.Elapsed.TotalSeconds -lt $WaitSeconds) {
        try {
            $Addresses = @([System.Net.Dns]::GetHostAddresses($Name))
            if ($Addresses.Count -gt 0) { return [pscustomobject]@{ Resolved = $true; Seconds = [int]$Watch.Elapsed.TotalSeconds; Addresses = @($Addresses | ForEach-Object { $_.ToString() }); Error = $null } }
        }
        catch { $LastError = $_.Exception.Message }
        Start-Sleep -Seconds 3
    }
    return [pscustomobject]@{ Resolved = $false; Seconds = [int]$Watch.Elapsed.TotalSeconds; Addresses = @(); Error = $LastError }
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

$script:NetStopTraceBlock = {
    param([string]$Name, [string]$WorkDirectory, [string]$ResultDirectory, [string]$ParseScript)
    $ErrorActionPreference = 'Stop'
    $Session = "DsoNet-$Name"
    $Query = logman query $Session -ets | Out-String
    Set-Content -LiteralPath (Join-Path $ResultDirectory "$Name-logman.txt") -Value $Query -Encoding UTF8
    $BuffersLost = $null
    if ($Query -match 'Buffers Lost:\s+(\d+)') { $BuffersLost = [int]$Matches[1] }
    $StopText = logman stop $Session -ets | Out-String
    Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $WorkDirectory "$Name-procs-end.json") -Encoding UTF8
    Set-ExecutionPolicy -Scope Process Bypass -Force
    $Parse = & $ParseScript -EtlPath (Join-Path $WorkDirectory "$Name.etl") -SnapshotPath (Join-Path $WorkDirectory "$Name-procs-start.json") -OutPath (Join-Path $ResultDirectory "$Name.json")
    return [pscustomobject]@{ Stop = $StopText; Parse = ($Parse | Out-String).Trim(); BuffersLost = $BuffersLost }
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
    # Jeden scenariusz na już uruchomionej VM (sesja Administratora); sieć podłączana tu i odłączana w finally. Zwraca rekord z werdyktem.
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

    $Record.services = @(Invoke-Command -Session $Session -ScriptBlock $script:NetPrepareBlock -ArgumentList @($Plan.WorkDirectory, $Plan.ResultDirectory, @($Plan.Providers), @($Plan.StoppedServices)))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'net\Parse-NetTrace.ps1') -Destination (Join-Path $Plan.WorkDirectory 'Parse-NetTrace.ps1') -ToSession $Session -Force
    $RemoteZip = Join-Path $Plan.WorkDirectory $Context.Artifact.Name
    Copy-Item -LiteralPath $Context.Artifact.Path -Destination $RemoteZip -ToSession $Session -Force
    $Record.appVersion = [string](Invoke-Command -Session $Session -ScriptBlock $script:NetInstallBlock -ArgumentList @($RemoteZip, $Context.Artifact.Sha256, $Layout.App, $Layout.Exe) | Select-Object -Last 1)

    $SettingsJson = if ($Scenario -eq 'start-off') { $Plan.SettingsOff } else { $Plan.SettingsOn }
    $Record.settings = Invoke-Command -Session $Session -ScriptBlock $script:NetSettingsBlock -ArgumentList @($Plan.SettingsDirectory, $SettingsJson)

    # Sieć dopiero po przygotowaniu VM: brak ruchu nie może wynikać z braku sieci.
    Connect-VMNetworkAdapter -VMName $VmName -SwitchName $Plan.NetworkSwitch -ErrorAction Stop
    $Context.Timeline += "$(Get-Date -Format 'HH:mm:ss') $Scenario sieć podłączona"
    try {
        $Record.network = Invoke-Command -Session $Session -ScriptBlock $script:NetWaitNetworkBlock -ArgumentList @($Plan.NetworkName, [int]$Plan.NetworkWaitSeconds)
        if (-not $Record.network.Resolved) { throw "Brak sieci w VM: '$($Plan.NetworkName)' nie rozwiązuje się po $($Record.network.Seconds) s ($($Record.network.Error))." }

        $Trace = Invoke-Command -Session $Session -ScriptBlock $script:NetStartTraceBlock -ArgumentList @($Name, $Plan.WorkDirectory)
        $Record.traceSince = $Trace.Since
        $TraceStopped = $false
        try {
            if ($Scenario -eq 'cli') {
                $null = Invoke-Command -Session $Session -ScriptBlock { param($Directory) $null = New-Item -ItemType Directory -Path $Directory -Force } -ArgumentList @($Plan.CliDirectory)
                $Runs = @()
                foreach ($Command in $Plan.CliCommands) {
                    $OutPath = Join-Path $Plan.CliDirectory (($Command -replace '^-+', '') + '.json')
                    $Cli = Invoke-DsoCli -Session $Session -ExePath $Layout.Exe -Arguments @($Command, '--out', $OutPath) -OutPath $OutPath -TimeoutSeconds ([int]$Plan.CliTimeoutSeconds) -RunAsUser $Plan.UserName
                    $Runs += [ordered]@{ command = $Command; exitCode = $Cli.ExitCode; timedOut = $Cli.TimedOut }
                    if ($Cli.TimedOut -or $Cli.ExitCode -ne 0) { $Record.problems += "polecenie $Command : kod $($Cli.ExitCode), przekroczony czas: $($Cli.TimedOut)" }
                }
                $Record.cli = $Runs
            }
            else {
                $Record.guiStart = Start-DsoGuiInUserSession -Session $Session -Layout $Layout -UserName $Plan.UserName -TaskName $Plan.GuiTaskName
                $ProbeText = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'net\NavProbe.ps1'), [System.Text.Encoding]::UTF8)
                $Json = Invoke-DsoUserSessionScript -Session $Session -Layout $Layout -Name 'Net-Nav' -ScriptText $ProbeText -UserName $Plan.UserName -TimeoutSeconds ([int]$Plan.NavTimeoutSeconds)
                $Record.nav = if ($Json) { $Json | ConvertFrom-Json } else { $null }
                if ($null -eq $Record.nav -or $Record.nav.Ok -ne $true) { $Record.problems += "sonda nawigacji nie przeszła wszystkich stron: $(if ($Record.nav) { $Record.nav.Error } else { 'brak wyniku' })" }
            }
            Start-Sleep -Seconds ([int]$Plan.SettleBeforeStopSeconds)
        }
        finally {
            try {
                $Stop = Invoke-Command -Session $Session -ScriptBlock $script:NetStopTraceBlock -ArgumentList @($Name, $Plan.WorkDirectory, $Plan.ResultDirectory, (Join-Path $Plan.WorkDirectory 'Parse-NetTrace.ps1'))
                $TraceStopped = $true
                $Record.parse = $Stop.Parse
                $Record.buffersLost = $Stop.BuffersLost
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

    # Surowe pliki (JSON śladu, wynik sondy, raporty --out); sam ETL zostaje w VM i przepada z revertem.
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
    if ($null -ne $Record.buffersLost -and $Record.buffersLost -gt 0) {
        $Record.passed = $false
        $Record.problems += "ślad utracił bufory ($($Record.buffersLost)): pomiar niekompletny"
    }
}

function Invoke-NetVmRun {
    # Jedna VM, scenariusze po kolei; KAŻDY scenariusz: revert -> karta odłączona -> start -> gotowość (zalogowany dso) -> pomiar ->
    # ZAWSZE revert, karta odłączona, stan końcowy sprawdzony (Complete-ExploreVmCleanup). Na końcu zapis net.json.
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][string[]]$Scenarios,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Artifact,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    Assert-DsoVmLiveAllowed
    $Credential = Get-DsoVmCredential -VmName $VmName
    $Plan = Get-NetPlan
    $StartedAt = Get-Date
    $Results = [ordered]@{}
    $Errors = @()
    $Timeline = @()
    $VmDirectory = Join-Path $OutDirectory $VmName
    $Layout = Get-DsoVmLayout
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
            $Context = [ordered]@{ Session = $Session; Plan = $Plan; Layout = $Layout; VmName = $VmName; VmDirectory = $VmDirectory; Artifact = $Artifact; Timeline = $Timeline }
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
        if ($Record.error) { $Errors += "${Scenario}: $($Record.error)" }
        foreach ($Problem in @($Record.problems)) { if ($Problem) { $Errors += "${Scenario}: $Problem" } }
        # Błąd uwierzytelnienia przerywa całość (blokada konta Administrator w VM).
        if ($null -ne $AuthFailure) { break }
    }
    $Failed = @($Results.Values | Where-Object { -not $_.passed } | ForEach-Object { $_.scenario })
    $Result = [ordered]@{ schema = 1; vmName = $VmName; checkpoint = $CheckpointName; startedAt = $StartedAt.ToString('o'); finishedAt = (Get-Date).ToString('o')
        artifact = $Artifact; allPassed = ($Failed.Count -eq 0 -and $Errors.Count -eq 0 -and $Results.Count -eq $Scenarios.Count); errors = $Errors; timeline = $Timeline; scenarios = $Results }
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
            -ReadyTimeoutSeconds $ReadyTimeoutSeconds -ArtifactPath $ArtifactPath
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
        Write-Host "Wydanie: $($RunArguments.Artifact.Path) (SHA256 $($RunArguments.Artifact.Sha256))"
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
