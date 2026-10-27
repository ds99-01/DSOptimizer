<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z modułami testów VM (tests\vm\modules) i tests\vm\explore\ExploreCommon.psm1.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm).
    Obie VM, po kolei (Home, potem Pro):
        .\tests\vm\explore\Invoke-C2Exploration.ps1 -OutDirectory C:\ścieżka\do\wyników
    Jedna VM:
        .\tests\vm\explore\Invoke-C2Exploration.ps1 -VmName DSO-26H2-Pro -OutDirectory C:\ścieżka\do\wyników
    Bez -OutDirectory wynik trafia do tests\vm\out\c2-<data> (katalog out\ jest w .gitignore).
Opis działania:
    Skrypt zbiera dowody z prawdziwej VM 26H2 dla partii C2 (pozycje perf.*). Wartości rejestru i stanu nie pochodzą z pamięci
    modelu: zmianę wykonuje oficjalne narzędzie (Set-Service, Stop-Service, Start-Service, sc.exe), a skutek jest odczytany po zmianie.
    Sondy:
      sysmain          - Win32_Service (StartMode, State, DelayedAutoStart, ExitCode, ProcessId), wartości Start i DelayedAutoStart
                         pod HKLM\SYSTEM\CurrentControlSet\Services\SysMain, DependentServices i ServicesDependedOn (nazwy + Status).
                         Sekwencja: odczyt -> Set-Service -StartupType Disabled -> Stop-Service BEZ -Force (usługi zależne mają
                         zablokować zatrzymanie) -> odczyt -> przywrócenie typu startu z odczytu startowego (delayed-auto przez
                         'sc.exe config SysMain start= delayed-auto', bo Set-Service w PS 5.1 nie zna opóźnionego startu) i
                         Start-Service, jeśli usługa działała -> odczyt -> MatchesBaseline + Differences.
      memoryManagement - zrzut całego klucza HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management (wszystkie
                         wartości z typami; w tym DisablePagingExecutive, FeatureSettingsOverride, FeatureSettingsOverrideMask).
                         Tylko odczyt.
      vbsHvci          - tylko odczyt: Win32_DeviceGuard (root\Microsoft\Windows\DeviceGuard, wszystkie właściwości z typami, tablice
                         jako tablice), rekurencyjny zrzut ...\Control\DeviceGuard (z Scenarios\*), ...\Control\CI\State,
                         ...\Control\Lsa\LsaCfgFlags oraz surowe 'bcdedit.exe /enum {current}' (tylko informacyjnie: tekst zależy
                         od języka systemu, NIE do detekcji). Zmiana VBS i jej skutek po restarcie to osobny przebieg (Reboot).
    Metadane: CurrentBuild, UBR, EditionID, DisplayVersion, ProductName, Culture/UICulture, PSVersion.
    Każdy krok zapisuje: komendę, kod wyjścia, surowe stdout/stderr (strona OEM), wartości rejestru jako {Exists, Kind, Value, Hex}
    i czas. Brak wartości (Exists=false) to nie to samo co wartość 0.
    Przebieg na każdej VM (moduł ExploreCommon): przywrócenie checkpointu -> start -> Wait-VmReady -> sesja PowerShell Direct ->
    zbieranie, a na końcu ZAWSZE (finally) zatrzymanie i powrót do checkpointu. Pierwszy błąd uwierzytelnienia przerywa cały skrypt
    (bez prób na drugiej VM, blokada konta). Inny błąd jednej VM jest zapisany i nie przerywa drugiej.
    Wynik: <OutDirectory>\<VmName>\c2-exploration.json (UTF-8).
    Skrypt dotyka wyłącznie VM DSO-26H2-Home i DSO-26H2-Pro (ValidateSet), na hoście niczego nie zmienia.
    Kod wyjścia: 0 gdy wszystkie wybrane VM zebrane bez błędu, 1 w przeciwnym razie.
#>
[CmdletBinding()]
param(
    [ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200
)

# Wspólna orkiestracja i narzędzia odczytu (rejestr, program natywny, rekord kroku) są w ExploreCommon.psm1.
Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force

# ---------------------------------------------------------------------------------------------------------------------
# Funkcje wysyłane do VM (samowystarczalne: wołają tylko siebie nawzajem, funkcje Explore z modułu i cmdlety systemowe).
# Lista w Get-C2RemoteFunctionNames (część wspólna: Get-ExploreCommonRemoteFunctionNames).
# ---------------------------------------------------------------------------------------------------------------------

function Get-C2ServiceSnapshot {
    # Stan usługi: Win32_Service (CIM) oraz usługi zależne i te, od których zależy (nazwa + Status). Brak usługi = Found=false.
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)
    $Snapshot = [ordered]@{ Found = $false; Error = $null; Cim = $null; Dependents = @(); DependsOn = @() }
    try {
        $Cim = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction Stop
        if ($null -ne $Cim) {
            $Snapshot.Found = $true
            $Snapshot.Cim = [ordered]@{ StartMode = [string]$Cim.StartMode; State = [string]$Cim.State; DelayedAutoStart = $Cim.DelayedAutoStart; ExitCode = $Cim.ExitCode; ProcessId = $Cim.ProcessId }
        }
        if ($Snapshot.Found) {
            $Service = Get-Service -Name $Name -ErrorAction Stop
            $Snapshot.Dependents = @(@($Service.DependentServices) | ForEach-Object { [ordered]@{ Name = [string]$_.Name; Status = [string]$_.Status } })
            $Snapshot.DependsOn = @(@($Service.ServicesDependedOn) | ForEach-Object { [ordered]@{ Name = [string]$_.Name; Status = [string]$_.Status } })
        }
    }
    catch { $Snapshot.Error = $_.Exception.Message }
    return $Snapshot
}

function Compare-C2ServiceState {
    # Różnice między dwoma wynikami Get-C2ServiceSnapshot: StartMode, State, DelayedAutoStart i zależności (nazwa:Status).
    # ProcessId i ExitCode celowo pominięte (zmieniają się po zatrzymaniu i starcie usługi).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Baseline,
        [Parameter(Mandatory)]$Current
    )
    $Differences = @()
    if (-not $Baseline.Found -or -not $Current.Found) { return @("usługa: Found $($Baseline.Found) -> $($Current.Found)") }
    foreach ($Field in 'StartMode', 'State', 'DelayedAutoStart') {
        if ([string]$Baseline.Cim[$Field] -ne [string]$Current.Cim[$Field]) { $Differences += "Win32_Service.${Field}: $($Baseline.Cim[$Field]) -> $($Current.Cim[$Field])" }
    }
    foreach ($Relation in 'Dependents', 'DependsOn') {
        $Before = (@($Baseline[$Relation]) | ForEach-Object { "$($_.Name):$($_.Status)" } | Sort-Object) -join ','
        $After = (@($Current[$Relation]) | ForEach-Object { "$($_.Name):$($_.Status)" } | Sort-Object) -join ','
        if ($Before -ne $After) { $Differences += "${Relation}: [$Before] -> [$After]" }
    }
    return $Differences
}

function Get-C2StartTypeRestore {
    # Typ startu do przywrócenia wyprowadzony WYŁĄCZNIE z odczytu startowego rejestru (Start: 2 auto, 3 manual, 4 disabled;
    # DelayedAutoStart=1 przy Start=2 = opóźniony). Inne wartości, brak wartości albo zły typ: Kind=$null z powodem (nie zgadujemy).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Start,
        [Parameter(Mandatory)]$DelayedAutoStart
    )
    $Result = [ordered]@{ Kind = $null; Reason = $null }
    if (-not $Start.Exists) { $Result.Reason = 'wartość Start nie istniała na starcie'; return [pscustomobject]$Result }
    if ($Start.Kind -ne 'DWord') { $Result.Reason = "wartość Start ma nieoczekiwany typ $($Start.Kind)"; return [pscustomobject]$Result }
    switch ([int]$Start.Value) {
        2 { $Result.Kind = if ($DelayedAutoStart.Exists -and $DelayedAutoStart.Kind -eq 'DWord' -and [int]$DelayedAutoStart.Value -eq 1) { 'AutomaticDelayed' } else { 'Automatic' } }
        3 { $Result.Kind = 'Manual' }
        4 { $Result.Kind = 'Disabled' }
        default { $Result.Reason = "Start=$($Start.Value) nie jest jedną z wartości 2/3/4" }
    }
    return [pscustomobject]$Result
}

function Invoke-C2SysMainProbe {
    [CmdletBinding()]
    param()
    $ServiceName = 'SysMain'
    $ServiceKey = "HKLM\SYSTEM\CurrentControlSet\Services\$ServiceName"
    $Regs = @(
        @{ Path = $ServiceKey; Name = 'Start' }
        @{ Path = $ServiceKey; Name = 'DelayedAutoStart' }
    )
    # Stan ze startu (przed jakąkolwiek zmianą): do niego wracamy i z nim porównujemy na końcu.
    $BaseRegs = @($Regs | ForEach-Object { Read-ExploreRegistryValue -Path $_.Path -Name $_.Name })
    $BaseService = Get-C2ServiceSnapshot -Name $ServiceName
    $Show = { @{ Service = (Get-C2ServiceSnapshot -Name $ServiceName) } }
    Invoke-ExploreStep -Name 'baseline' -Command "Get-CimInstance Win32_Service SysMain; Get-Service SysMain; rejestr $ServiceKey (Start, DelayedAutoStart)" -Registry $Regs -After $Show
    if (-not $BaseService.Found) { return }

    Invoke-ExploreStep -Name 'set-disabled' -Command 'Set-Service -Name SysMain -StartupType Disabled' -Registry $Regs -After $Show -Action {
        Set-Service -Name $ServiceName -StartupType Disabled -ErrorAction Stop
        [pscustomobject]@{ ExitCode = $null; Stdout = 'Set-Service -StartupType Disabled wykonane bez wyjątku'; Stderr = $null; Error = $null }
    }
    # Bez -Force: usługi zależne mają zablokować zatrzymanie (to jest właśnie pytanie badawcze); błąd ląduje w polu Error kroku.
    Invoke-ExploreStep -Name 'stop-service' -Command 'Stop-Service -Name SysMain -Force:$false' -Registry $Regs -After $Show -Action {
        Stop-Service -Name $ServiceName -Force:$false -ErrorAction Stop
        [pscustomobject]@{ ExitCode = $null; Stdout = 'Stop-Service bez -Force wykonane bez wyjątku'; Stderr = $null; Error = $null }
    }

    $Restore = Get-C2StartTypeRestore -Start $BaseRegs[0] -DelayedAutoStart $BaseRegs[1]
    switch ($Restore.Kind) {
        'AutomaticDelayed' {
            Invoke-ExploreStep -Name 'przywroc-typ-startu' -Command 'sc.exe config SysMain start= delayed-auto' -Registry $Regs -After { @{ RestoreKind = $Restore.Kind; Service = (Get-C2ServiceSnapshot -Name $ServiceName) } } `
                -Action { Invoke-ExploreNative -FilePath 'sc.exe' -Arguments @('config', $ServiceName, 'start=', 'delayed-auto') }
        }
        { $_ -in 'Automatic', 'Manual' } {
            $StartupType = $Restore.Kind
            Invoke-ExploreStep -Name 'przywroc-typ-startu' -Command "Set-Service -Name SysMain -StartupType $StartupType" -Registry $Regs -After { @{ RestoreKind = $Restore.Kind; Service = (Get-C2ServiceSnapshot -Name $ServiceName) } } -Action {
                Set-Service -Name $ServiceName -StartupType $StartupType -ErrorAction Stop
                [pscustomobject]@{ ExitCode = $null; Stdout = "Set-Service -StartupType $StartupType wykonane bez wyjątku"; Stderr = $null; Error = $null }
            }
        }
        default {
            $Reason = if ($Restore.Kind -eq 'Disabled') { 'startowo Disabled: stan po Set-Service Disabled jest już zgodny ze startowym' } else { "nie zgaduję przywrócenia: $($Restore.Reason)" }
            Invoke-ExploreStep -Name 'przywroc-typ-startu' -Command '(pominięte: brak zmiany typu startu do przywrócenia)' -Registry $Regs `
                -After { @{ RestoreSkipped = $true; Reason = $Reason; RestoreKind = $Restore.Kind; Service = (Get-C2ServiceSnapshot -Name $ServiceName) } }
        }
    }
    $WasRunning = ($BaseService.Cim.State -eq 'Running')
    Invoke-ExploreStep -Name 'start-service-jesli-dzialala' -Command 'Start-Service -Name SysMain (gdy działała na starcie i obecnie nie działa)' -Registry $Regs -After $Show -Action {
        $Now = Get-C2ServiceSnapshot -Name $ServiceName
        $Text = 'bez zmian: usługa nie działała na starcie albo już działa'
        if ($WasRunning -and $Now.Found -and $Now.Cim.State -ne 'Running') {
            Start-Service -Name $ServiceName -ErrorAction Stop
            $Text = 'Start-Service wykonane bez wyjątku'
        }
        [pscustomobject]@{ ExitCode = $null; Stdout = $Text; Stderr = $null; Error = $null }
    }
    Invoke-ExploreStep -Name 'po-przywroceniu' -Command '(odczyt po przywróceniu, bez zmian)' -Registry $Regs -After $Show
    Invoke-ExploreStep -Name 'porownanie-z-baseline' -Command '(odczyt i porównanie ze stanem startowym)' -Registry $Regs -After {
        $NowRegs = @($Regs | ForEach-Object { Read-ExploreRegistryValue -Path $_.Path -Name $_.Name })
        $NowService = Get-C2ServiceSnapshot -Name $ServiceName
        $Diff = @(Compare-ExploreRegistry -Baseline $BaseRegs -Current $NowRegs) + @(Compare-C2ServiceState -Baseline $BaseService -Current $NowService)
        @{ MatchesBaseline = ($Diff.Count -eq 0); Differences = @($Diff); BaselineService = $BaseService; Service = $NowService }
    }
}

function Invoke-C2MemoryManagementProbe {
    # Tylko odczyt: cały klucz Memory Management + jawnie wartości istotne dla perf.paging-executive i perf.spectre-mitigations.
    [CmdletBinding()]
    param()
    $Key = 'HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'
    $Regs = @(
        @{ Path = $Key; Name = 'DisablePagingExecutive' }
        @{ Path = $Key; Name = 'FeatureSettingsOverride' }
        @{ Path = $Key; Name = 'FeatureSettingsOverrideMask' }
    )
    Invoke-ExploreStep -Name 'memory-management-zrzut' -Command '(odczyt rejestru, bez zmian)' -Registry $Regs -RegistryKeys @($Key)
}

function Get-C2DeviceGuardInfo {
    # Win32_DeviceGuard: wszystkie właściwości instancji z typem CIM; tablice zachowane jako tablice (IsArray). Tylko odczyt.
    [CmdletBinding()]
    param()
    $Result = [ordered]@{ Ok = $false; Error = $null; Instances = @() }
    try {
        $Instances = @(Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' -ClassName Win32_DeviceGuard -ErrorAction Stop)
        $Converted = @()
        foreach ($Instance in $Instances) {
            $Properties = @()
            foreach ($Property in $Instance.CimInstanceProperties) {
                $Raw = $Property.Value
                $IsArray = $Raw -is [array]
                $Value = $Raw
                if ($IsArray) { $Value = [object[]]@($Raw) }
                $Properties += [pscustomobject]@{ Name = [string]$Property.Name; CimType = [string]$Property.CimType; IsArray = $IsArray; Value = $Value }
            }
            $Converted += [pscustomobject]@{ Properties = @($Properties) }
        }
        $Result.Instances = @($Converted)
        $Result.Ok = $true
    }
    catch { $Result.Error = $_.Exception.Message }
    return $Result
}

function Invoke-C2VbsProbe {
    # Tylko odczyt (bez zmian VBS: skutek po restarcie sprawdza przebieg Reboot w testach VM).
    [CmdletBinding()]
    param()
    $Control = 'HKLM\SYSTEM\CurrentControlSet\Control'
    Invoke-ExploreStep -Name 'win32-deviceguard' -Command 'Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard' -After { @{ DeviceGuard = (Get-C2DeviceGuardInfo) } }
    Invoke-ExploreStep -Name 'rejestr-deviceguard' -Command "(odczyt rejestru rekurencyjnie: $Control\DeviceGuard)" -RegistryTrees @("$Control\DeviceGuard")
    Invoke-ExploreStep -Name 'rejestr-ci-state' -Command "(odczyt rejestru: $Control\CI\State)" -RegistryKeys @("$Control\CI\State")
    Invoke-ExploreStep -Name 'rejestr-lsacfgflags' -Command "(odczyt rejestru: $Control\Lsa\LsaCfgFlags)" -Registry @(@{ Path = "$Control\Lsa"; Name = 'LsaCfgFlags' })
    # Surowe wyjście bcdedit tylko informacyjnie (hypervisorlaunchtype); tekst zależy od języka, więc NIE służy do detekcji.
    Invoke-ExploreStep -Name 'bcdedit-enum-current' -Command 'bcdedit.exe /enum {current}' -Action { Invoke-ExploreNative -FilePath 'bcdedit.exe' -Arguments @('/enum', '{current}') }
}

function Invoke-C2Collection {
    # Punkt wejścia po stronie VM: zwraca JEDEN string JSON (meta + wszystkie sondy).
    [CmdletBinding()]
    param()
    $Probes = [ordered]@{
        sysmain          = { Invoke-C2SysMainProbe }
        memoryManagement = { Invoke-C2MemoryManagementProbe }
        vbsHvci          = { Invoke-C2VbsProbe }
    }
    return (Invoke-ExploreCollection -Probe $Probes)
}

# ---------------------------------------------------------------------------------------------------------------------
# Strona hosta.
# ---------------------------------------------------------------------------------------------------------------------

function Get-C2RemoteFunctionNames {
    return @(
        'Get-C2ServiceSnapshot', 'Compare-C2ServiceState', 'Get-C2StartTypeRestore', 'Invoke-C2SysMainProbe', 'Invoke-C2MemoryManagementProbe',
        'Get-C2DeviceGuardInfo', 'Invoke-C2VbsProbe', 'Invoke-C2Collection'
    )
}

function Get-C2RemoteScript {
    # Tekst definicji funkcji zdalnych: część wspólna z modułu + funkcje C2 (FunctionInfo pobrane tu, w zasięgu skryptu).
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $Names = @(Get-ExploreCommonRemoteFunctionNames) + @(Get-C2RemoteFunctionNames)
    return (ConvertTo-ExploreRemoteScript -Command @($Names | ForEach-Object { Get-Command -Name $_ -CommandType Function -ErrorAction Stop }))
}

function Invoke-C2Exploration {
    # Wszystkie wybrane VM po kolei. Błąd uwierzytelnienia = stop całości; inny błąd = zapis i dalej (logika we wspólnym module).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DSO-26H2-Home', 'DSO-26H2-Pro')][string[]]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    return (Invoke-ExploreAll -VmName $VmName -OutDirectory $OutDirectory -FileName 'c2-exploration.json' -RemoteScript (Get-C2RemoteScript) `
            -EntryCommand 'Invoke-C2Collection' -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds)
}

# Przy dot-sourcingu (testy) tylko definicje; uruchomienie przebiegu tylko przy zwykłym wywołaniu skryptu.
if ($MyInvocation.InvocationName -ne '.') {
    $ExitCode = 1
    try {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $VmRoot = Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
        Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
        # ExploreCommon (zaimportowany wyżej, przed tymi modułami) trzyma referencję do starej instancji VmHost; ponowny import po nich wiąże go z bieżącą.
        Import-Module (Join-Path $PSScriptRoot 'ExploreCommon.psm1') -Force
        $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
        }
        if (-not $OutDirectory) { $OutDirectory = Join-Path $VmRoot ('out\c2-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
        $null = New-Item -ItemType Directory -Path $OutDirectory -Force
        Write-Host "Wyniki: $OutDirectory"
        $Outcome = Invoke-C2Exploration -VmName $VmName -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
        foreach ($Path in $Outcome.Paths) { Write-Host "Zapisano: $Path" }
        foreach ($Message in $Outcome.Errors) { Write-Host "BŁĄD $Message" -ForegroundColor Red }
        $ExitCode = if (@($Outcome.Errors).Count -eq 0 -and @($Outcome.Paths).Count -eq @($VmName).Count) { 0 } else { 1 }
    }
    catch {
        Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
        $ExitCode = 1
    }
    exit $ExitCode
}
