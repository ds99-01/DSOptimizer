<#
Instrukcja uruchomienia:
    Import-Module .\tests\vm\modules\VmHost.psm1 -Force
    (używane przez tests\vm\Invoke-VmTests.ps1; testy: Invoke-Pester -Path tests/vm/tests/VmHost.Tests.ps1)
Opis działania:
    Strona hosta przebiegu testów VM: przywracanie VM do czystego checkpointu, start i czekanie na gotowość
    (PowerShell Direct + zalogowany użytkownik 'dso'), kopiowanie builda, uruchomienie scenariuszy Pester
    przeciw VM, raport JSON i sprzątanie (VM zostaje wyłączona na czystym checkpoincie).
    Hasło VM nigdy nie trafia do wyjścia, raportów ani logów.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# UWAGA: funkcje wołane z domknięcia Reconnect (Wait-VmHeartbeatDrop, Wait-VmReady) NIE czytają zmiennych $script: modułu.
# Gdy Invoke-Pester jest uruchomiony z wnętrza modułu (Invoke-DsoVmRun), takie odczyty z domknięcia kończą się błędem
# "variable ... has not been set". Wartości (limit prób logowania, interwały) są więc parametrami z literalnymi domyślnymi.

# Wspólny znacznik błędu uwierzytelnienia PowerShell Direct: Wait-VmReady wkłada go do komunikatu i ustawia flagę Data['VmAuthFailure'].
# Skrypty zbierające (tests\vm\explore) rozpoznają błąd przez Test-VmAuthFailure, a nie przez własną kopię tekstu.
function Get-VmAuthFailureMarker {
    [CmdletBinding()]
    [OutputType([string])]
    param()
    # Literał w funkcji, nie zmienna $script: - wołana z Wait-VmReady w domknięciu Reconnect (patrz uwaga na górze pliku).
    return 'błąd uwierzytelnienia PowerShell Direct'
}

function Test-VmAuthFailure {
    # $true, gdy wyjątek (albo którykolwiek z jego InnerException) niesie flagę Data['VmAuthFailure'] lub znacznik w komunikacie.
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)][AllowNull()][System.Exception]$Exception)
    $Marker = Get-VmAuthFailureMarker
    for ($Current = $Exception; $null -ne $Current; $Current = $Current.InnerException) {
        if ($Current.Data.Contains('VmAuthFailure') -and $Current.Data['VmAuthFailure']) { return $true }
        if ($Current.Message -like "*$Marker*") { return $true }
    }
    return $false
}

function Assert-DsoVmLiveAllowed {
    # Wyłącznik niezależny od mocków: testy Pester (tests/vm/tests/Support/NoLiveVm.ps1) ustawiają DSO_VM_TESTS_MOCK_ONLY,
    # a każda funkcja dotykająca prawdziwego Hyper-V / PowerShell Direct woła to na początku. Nazwa zmiennej jest literałem
    # w funkcji, nie zmienną $script: (patrz uwaga na górze pliku o domknięciu Reconnect).
    [CmdletBinding()]
    param()
    if ($env:DSO_VM_TESTS_MOCK_ONLY) {
        throw 'Tryb DSO_VM_TESTS_MOCK_ONLY: operacje na prawdziwym Hyper-V / PowerShell Direct są zablokowane (test na mockach nie może dotykać VM).'
    }
}

function Restore-VmCleanState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^DSO-26H2-[A-Za-z]+\z')][string]$VmName,
        [Parameter(Mandatory)][string]$CheckpointName
    )
    Assert-DsoVmLiveAllowed
    $Vm = Get-VM -Name $VmName
    if ($Vm.State -ne 'Off') {
        Stop-VM -Name $VmName -TurnOff -Force
    }
    Restore-VMCheckpoint -VMName $VmName -Name $CheckpointName -Confirm:$false
}

function Wait-VmHeartbeatDrop {
    # Czeka po stronie hosta (tylko Get-VM, zero logowań do gościa), aż heartbeat opuści OkApplications*, czyli stara instancja
    # po Restart-Computer zgaśnie. Po limicie nie zawodzi: ostrzega i zwraca $false - przypadek i tak chroni kontrola LastBootUpTime.
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][ValidatePattern('^DSO-26H2-[A-Za-z]+\z')][string]$VmName,
        [ValidateRange(1, 60)][int]$IntervalSeconds = 2,
        [ValidateRange(1, 600)][int]$TimeoutSeconds = 120
    )
    Assert-DsoVmLiveAllowed
    $MaxPolls = [int][Math]::Ceiling($TimeoutSeconds / $IntervalSeconds)
    for ($Poll = 0; $Poll -lt $MaxPolls; $Poll++) {
        $Heartbeat = [string](Get-VM -VMName $VmName -ErrorAction Stop).Heartbeat
        if ($Heartbeat -notlike 'OkApplications*') { return $true }
        Start-Sleep -Seconds $IntervalSeconds
    }
    Write-Warning "VM '$VmName': heartbeat nie opuścił stanu OK w ciągu $TimeoutSeconds s po restarcie; łączę się mimo to (restart weryfikuje LastBootUpTime)."
    return $false
}

function Wait-VmReady {
    [CmdletBinding()]
    [OutputType([System.Management.Automation.Runspaces.PSSession])]
    param(
        [Parameter(Mandatory)][ValidatePattern('^DSO-26H2-[A-Za-z]+\z')][string]$VmName,
        [Parameter(Mandatory)][pscredential]$Credential,
        [int]$TimeoutSeconds = 1200,
        # Twardy limit prób New-PSSession (ochrona przed blokadą konta Administrator w VM); nie do podniesienia ponad 8.
        [ValidateRange(1, 8)][int]$MaxSessionAttempts = 8,
        # Bez wymagania interaktywnej sesji dso (explorer.exe): wystarczy działający PowerShell Direct (np. odczyt końcowy ręcznego testu UAC).
        [switch]$SkipInteractiveUserCheck
    )
    Assert-DsoVmLiveAllowed
    $Deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $Session = $null
    $Probe = {
        $Owners = Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction Stop |
            ForEach-Object { (Invoke-CimMethod -InputObject $_ -MethodName GetOwner).User }
        return [bool](@($Owners) -contains 'dso')
    }
    if ($SkipInteractiveUserCheck) { $Probe = { return $true } }
    # Etap 1: heartbeat usług integracji (VM wystartowała i gość odpowiada), dopiero potem PowerShell Direct.
    $Heartbeat = '(brak odczytu)'
    $HeartbeatOk = $false
    do {
        $Heartbeat = [string](Get-VM -VMName $VmName -ErrorAction Stop).Heartbeat
        if ($Heartbeat -in @('OkApplicationsHealthy', 'OkApplicationsUnknown')) { $HeartbeatOk = $true; break }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $Deadline)
    if (-not $HeartbeatOk) {
        throw "VM '$VmName': brak heartbeatu usług integracji w ciągu $TimeoutSeconds s (ostatni stan: $Heartbeat). Nie próbuję logowania."
    }
    # Etap 2: logowanie z twardym limitem prób (VM ma lockout konta). Klasyfikacja po tekście komunikatu (zależna od języka)
    # służy WYŁĄCZNIE do diagnozy w komunikacie; decyduje limit prób. Wyjątek: wyraźny błąd uwierzytelnienia przerywa od razu.
    $AuthPattern = 'credential is invalid|logon failure|password|access is denied|unauthorized|locked out|account.*(disabled|restricted)|nieprawidłow|hasł|odmowa dostępu|zablokowan'
    $AttemptCount = 0
    $LastError = $null
    $MarkerText = Get-VmAuthFailureMarker
    while ((Get-Date) -lt $Deadline) {
        if ($null -eq $Session) {
            if ($AttemptCount -ge $MaxSessionAttempts) {
                $LooksLikeAuth = [bool]($LastError -match $AuthPattern)
                $Hint = if ($LooksLikeAuth) { ' (wygląda na błąd uwierzytelnienia)' } else { '' }
                $Exhausted = [System.InvalidOperationException]::new("VM '$VmName': New-PSSession nie powiodło się po $AttemptCount próbach, przerywam$Hint. Ostatni błąd: $LastError")
                # Jak przy natychmiastowym przerwaniu: flaga dla Test-VmAuthFailure (stop całego skryptu eksploracji po błędzie uwierzytelnienia).
                if ($LooksLikeAuth) { $Exhausted.Data['VmAuthFailure'] = $true }
                throw $Exhausted
            }
            $AttemptCount++
            try { $Session = New-PSSession -VMName $VmName -Credential $Credential -ErrorAction Stop }
            catch {
                $LastError = $_.Exception.Message
                if ($LastError -match $AuthPattern) {
                    $AuthFailure = [System.InvalidOperationException]::new("VM '$VmName': $MarkerText, przerywam bez ponawiania (blokada konta po 10 próbach): $LastError")
                    $AuthFailure.Data['VmAuthFailure'] = $true
                    throw $AuthFailure
                }
                Start-Sleep -Seconds 5
                continue
            }
        }
        $Ready = $false
        try { $Ready = [bool](Invoke-Command -Session $Session -ScriptBlock $Probe) }
        catch {
            $LastError = $_.Exception.Message
            Remove-PSSession -Session $Session -ErrorAction SilentlyContinue
            $Session = $null
        }
        if ($Ready) { return $Session }
        Start-Sleep -Seconds 5
    }
    if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
    $Detail = if ($LastError) { " Ostatni błąd: $LastError" } else { '' }
    throw "VM '$VmName' nie była gotowa w ciągu $TimeoutSeconds s (PowerShell Direct + explorer.exe użytkownika dso).$Detail"
}
function Get-VmBootObservation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session
    )
    $Raw = Invoke-Command -Session $Session -ScriptBlock {
        Get-CimInstance -ClassName Win32_Service -ErrorAction Stop |
            Where-Object { $_.State -like '*Pending' } |
            ForEach-Object { [pscustomobject]@{ Name = [string]$_.Name; State = [string]$_.State; StartMode = [string]$_.StartMode } }
    }
    $Items = @($Raw | Where-Object { $null -ne $_ } | ForEach-Object { [pscustomobject]@{ Name = $_.Name; State = $_.State; StartMode = $_.StartMode } })
    return , $Items
}

function Copy-DsoBuildToVm {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$BuildDirectory,
        [Parameter(Mandatory)][string]$CatalogPath,
        [Parameter(Mandatory)][hashtable]$Layout
    )
    Assert-DsoVmLiveAllowed
    Invoke-Command -Session $Session -ArgumentList @($Layout.Root, $Layout.App, $Layout.Out) -ScriptBlock {
        param([string]$Root, [string]$App, [string]$Out)
        $ErrorActionPreference = 'Stop'
        if ($Root -notmatch '^[A-Za-z]:\\[^\\]+$') { throw "Niebezpieczny katalog roboczy VM: '$Root'." }
        if (Test-Path -LiteralPath $Root) { Remove-Item -LiteralPath $Root -Recurse -Force }
        New-Item -ItemType Directory -Path $App -Force | Out-Null
        New-Item -ItemType Directory -Path $Out -Force | Out-Null
    } | Out-Null
    Copy-Item -Path (Join-Path $BuildDirectory '*') -Destination $Layout.App -ToSession $Session -Recurse -Force
    Copy-Item -Path $CatalogPath -Destination $Layout.Catalog -ToSession $Session -Force
}

function Get-PesterFailureEntry {
    # Zamienia wpisy Failed/FailedBlocks/FailedContainers z wyniku Pestera na { name, error } bez zależności od wersji.
    param($Result, [string]$PropertyName)
    $Property = $Result.PSObject.Properties[$PropertyName]
    if ($null -eq $Property -or $null -eq $Property.Value) { return @() }
    $Entries = foreach ($Item in @($Property.Value)) {
        $Name = ''
        foreach ($Candidate in @('ExpandedPath', 'Name', 'Path', 'Item')) {
            $Member = $Item.PSObject.Properties[$Candidate]
            if ($null -ne $Member -and $null -ne $Member.Value -and [string]$Member.Value -ne '') { $Name = [string]$Member.Value; break }
        }
        $Messages = @()
        if ($Item.PSObject.Properties['ErrorRecord']) { $Messages = @($Item.ErrorRecord | ForEach-Object { $_.ToString() }) }
        [pscustomobject]@{ name = $Name; error = ($Messages -join '; ') }
    }
    return @($Entries)
}

function Get-PesterCount {
    param($Result, [string]$PropertyName)
    $Property = $Result.PSObject.Properties[$PropertyName]
    if ($null -eq $Property -or $null -eq $Property.Value) { return 0 }
    return [int]$Property.Value
}

function Clear-VmRecordDirectory {
    # Usuwa stare rekordy z poprzedniego przebiegu; tylko pliki *.json w katalogu leżącym wewnątrz OutDirectory.
    param([Parameter(Mandatory)][string]$RecordDirectory, [Parameter(Mandatory)][string]$OutDirectory)
    $Root = [System.IO.Path]::GetFullPath($OutDirectory).TrimEnd('\') + '\'
    $Target = [System.IO.Path]::GetFullPath($RecordDirectory)
    if (-not $Target.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Katalog rekordów '$Target' leży poza katalogiem przebiegu '$Root'."
    }
    if (Test-Path -LiteralPath $Target) {
        Get-ChildItem -LiteralPath $Target -Filter '*.json' -File | Remove-Item -Force
    }
    $null = New-Item -ItemType Directory -Path $Target -Force
}

function Test-VmSessionOpen {
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowNull()]$Session)
    if ($null -eq $Session) { return $false }
    return ([string]$Session.State -eq 'Opened')
}

function Invoke-DsoVmRun {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^DSO-26H2-[A-Za-z]+\z')][string]$VmName,
        [Parameter(Mandatory)][pscredential]$Credential,
        [Parameter(Mandatory)][string]$BuildDirectory,
        [Parameter(Mandatory)][string]$CatalogPath,
        [Parameter(Mandatory)][AllowEmptyCollection()][hashtable[]]$Cases,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200,
        # Etykieta przebiegu: raport i rekordy trafiają do <OutDirectory>\<VmName>\<RunLabel>\ (np. main, reboot-<nazwa>).
        [ValidatePattern('^[A-Za-z0-9_][A-Za-z0-9._-]*\z')][string]$RunLabel = 'main'
    )
    $Layout = Get-DsoVmLayout
    $VmDirectory = Join-Path (Join-Path $OutDirectory $VmName) $RunLabel
    $RecordDirectory = Join-Path $VmDirectory 'records'
    Clear-VmRecordDirectory -RecordDirectory $RecordDirectory -OutDirectory $OutDirectory
    $ReportPath = Join-Path $VmDirectory 'report.json'
    $StartedAt = Get-Date
    $Session = $null
    $ReconnectState = @{ Session = $null }
    $PrimaryError = $null
    try {
        Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName
        Start-VM -Name $VmName
        $Session = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
        $BootObservation = @(Get-VmBootObservation -Session $Session)
        Copy-DsoBuildToVm -Session $Session -BuildDirectory $BuildDirectory -CatalogPath $CatalogPath -Layout $Layout

        # Reconnect dla przypadków Reboot: ta sama Wait-VmReady (z jej limitem prób i natychmiastowym przerwaniem przy błędzie uwierzytelnienia).
        # Ostatnia sesja z Reconnect trafia do $ReconnectState: po restarcie stara sesja jest zamknięta, więc przez nią kopiujemy logi i ją zamykamy.
        $ReconnectBlock = {
            param([int]$Attempt = 1)
            # Pierwsza próba po restarcie: najpierw host czeka, aż stara instancja zgaśnie (bez logowań).
            if ($Attempt -le 1) { $null = Wait-VmHeartbeatDrop -VmName $VmName }
            $NewSession = Wait-VmReady -VmName $VmName -Credential $Credential -TimeoutSeconds $ReadyTimeoutSeconds
            $ReconnectState.Session = $NewSession
            $NewSession
        }.GetNewClosure()

        $UserSid = [string](Invoke-Command -Session $Session -ScriptBlock { (Get-LocalUser -Name 'dso').SID.Value })
        if ([string]::IsNullOrWhiteSpace($UserSid)) { throw "Nie udało się odczytać SID użytkownika 'dso' w VM '$VmName'." }
        $ExpandedCases = @($Cases | ForEach-Object { Expand-VmCasePlaceholders -Case $_ -UserSid $UserSid })

        Import-Module Pester -MinimumVersion 5.0 -ErrorAction Stop
        $ScenarioPath = Join-Path $PSScriptRoot '..\scenarios\VmScenarios.Tests.ps1'
        $Container = New-PesterContainer -Path $ScenarioPath -Data @{
            Session         = $Session
            Cases           = $ExpandedCases
            Layout          = $Layout
            RecordDirectory = $RecordDirectory
            Reconnect       = $ReconnectBlock
        }
        $Configuration = New-PesterConfiguration
        $Configuration.Run.Container = $Container
        $Configuration.Run.PassThru = $true
        $Configuration.Output.Verbosity = 'Detailed'
        try {
            $Result = Invoke-Pester -Configuration $Configuration
        }
        finally {
            # Logi przez najnowszą żywą sesję: ostatnia z Reconnect, w razie braku oryginalna; martwej sesji nie używamy.
            $LogSession = $null
            if (Test-VmSessionOpen -Session $ReconnectState.Session) { $LogSession = $ReconnectState.Session }
            elseif (Test-VmSessionOpen -Session $Session) { $LogSession = $Session }
            if ($null -eq $LogSession) {
                Write-Warning "Nie skopiowano logów aplikacji z VM '$VmName': brak otwartej sesji PowerShell Direct (restart lub błąd połączenia)."
            }
            else {
                try { Copy-Item -FromSession $LogSession -Path 'C:\ProgramData\DSOptimizer\logs' -Destination (Join-Path $VmDirectory 'logs') -Recurse -Force }
                catch { Write-Warning "Nie skopiowano logów aplikacji z VM '$VmName': $($_.Exception.Message)" }
            }
        }

        $Passed = Get-PesterCount -Result $Result -PropertyName 'PassedCount'
        $Skipped = Get-PesterCount -Result $Result -PropertyName 'SkippedCount'
        $Failed = (Get-PesterCount -Result $Result -PropertyName 'FailedCount') +
            (Get-PesterCount -Result $Result -PropertyName 'FailedBlocksCount') +
            (Get-PesterCount -Result $Result -PropertyName 'FailedContainersCount')
        if (($Passed + $Failed) -eq 0) {
            throw "Pester nie wykonał żadnego testu dla VM '$VmName' (przypadków: $($ExpandedCases.Count)) - wynik nie jest wiarygodny."
        }
        $FailedTests = @(Get-PesterFailureEntry -Result $Result -PropertyName 'Failed') +
            @(Get-PesterFailureEntry -Result $Result -PropertyName 'FailedBlocks') +
            @(Get-PesterFailureEntry -Result $Result -PropertyName 'FailedContainers')

        $Records = @(Get-ChildItem -LiteralPath $RecordDirectory -Filter '*.json' -File -ErrorAction SilentlyContinue |
                ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json })
        $Report = [ordered]@{
            vmName          = $VmName
            runLabel        = $RunLabel
            startedAt       = $StartedAt.ToString('o')
            finishedAt      = (Get-Date).ToString('o')
            bootObservation = $BootObservation
            pester          = [ordered]@{ passed = $Passed; failed = $Failed; skipped = $Skipped; failedTests = $FailedTests }
            cases           = $Records
        }
        $Json = $Report | ConvertTo-Json -Depth 20
        [System.IO.File]::WriteAllText($ReportPath, $Json, (New-Object System.Text.UTF8Encoding($true)))

        return [pscustomobject]@{ VmName = $VmName; RunLabel = $RunLabel; Passed = $Passed; Failed = $Failed; Skipped = $Skipped; ReportPath = $ReportPath }
    }
    catch {
        $PrimaryError = $_.Exception.Message
        throw
    }
    finally {
        if ($null -ne $Session) { Remove-PSSession -Session $Session -ErrorAction SilentlyContinue }
        if ($null -ne $ReconnectState -and $null -ne $ReconnectState.Session -and -not [object]::ReferenceEquals($ReconnectState.Session, $Session)) {
            Remove-PSSession -Session $ReconnectState.Session -ErrorAction SilentlyContinue
        }
        try { Restore-VmCleanState -VmName $VmName -CheckpointName $CheckpointName }
        catch {
            $RestoreMessage = "Przywracanie checkpointu '$CheckpointName' VM '$VmName' nie powiodło się: $($_.Exception.Message). VM może nie być w czystym stanie."
            if ($null -ne $PrimaryError) { throw "$PrimaryError | $RestoreMessage" }
            throw $RestoreMessage
        }
    }
}
function Write-VmSkippedRun {
    # Przebieg złożony wyłącznie z pominiętych przypadków: bez VM i bez Pestera zapisuje rekord pominięcia każdego przypadku
    # (z powodem) i report.json z passed = 0. Taki przebieg nigdy nie jest "zaliczony" (patrz Get-VmRunsExitCode).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VmName,
        [Parameter(Mandatory)][string]$OutDirectory,
        [Parameter(Mandatory)][hashtable[]]$Cases,
        [ValidatePattern('^[A-Za-z0-9_][A-Za-z0-9._-]*\z')][string]$RunLabel = 'skipped'
    )
    $StartedAt = Get-Date
    $VmDirectory = Join-Path (Join-Path $OutDirectory $VmName) $RunLabel
    $RecordDirectory = Join-Path $VmDirectory 'records'
    Clear-VmRecordDirectory -RecordDirectory $RecordDirectory -OutDirectory $OutDirectory
    $Records = [System.Collections.Generic.List[object]]::new()
    foreach ($Case in $Cases) {
        $Record = New-VmSkipRecord -Case $Case
        $SafeName = [string]$Case.Name -replace '[^A-Za-z0-9._-]', '_'
        [System.IO.File]::WriteAllText((Join-Path $RecordDirectory "$SafeName.json"), ($Record | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($true)))
        $Records.Add($Record)
    }
    $ReportPath = Join-Path $VmDirectory 'report.json'
    $Report = New-VmSkipReport -VmName $VmName -RunLabel $RunLabel -StartedAt $StartedAt -Records $Records.ToArray()
    [System.IO.File]::WriteAllText($ReportPath, ($Report | ConvertTo-Json -Depth 20), (New-Object System.Text.UTF8Encoding($true)))
    return [pscustomobject]@{ VmName = $VmName; RunLabel = $RunLabel; Passed = 0; Failed = 0; Skipped = $Records.Count; ReportPath = $ReportPath }
}

function Invoke-DsoVmRunSet {
    # Dzieli przypadki (Split-VmCaseRuns) i uruchamia kolejno: grupa "main" i po jednym przebiegu na każdy aktywny przypadek
    # Reboot, każdy od czystego checkpointu; przebieg tylko-pominięcia (SkipOnly) nie dotyka VM. Wyjątek przebiegu (np. błąd
    # uwierzytelnienia) zatrzymuje kolejne przebiegi, żeby nie ponawiać logowania do VM z blokadą konta; błędy testów
    # (Failed > 0) nie są wyjątkiem i nie przerywają. Wyjątek niesie ukończone przebiegi w $_.Exception.Data['CompletedRuns'],
    # dzięki czemu podsumowanie ich nie gubi.
    [CmdletBinding()]
    [OutputType([object[]])]
    param(
        [Parameter(Mandatory)][ValidatePattern('^DSO-26H2-[A-Za-z]+\z')][string]$VmName,
        [Parameter(Mandatory)][pscredential]$Credential,
        [Parameter(Mandatory)][string]$BuildDirectory,
        [Parameter(Mandatory)][string]$CatalogPath,
        [Parameter(Mandatory)][AllowEmptyCollection()][hashtable[]]$Cases,
        [Parameter(Mandatory)][string]$OutDirectory,
        [string]$CheckpointName = 'czysta-26H2',
        [int]$ReadyTimeoutSeconds = 1200
    )
    $Runs = [System.Collections.Generic.List[object]]::new()
    foreach ($Item in @(Split-VmCaseRuns -Cases $Cases)) {
        try {
            if ($Item.SkipOnly) {
                $Runs.Add((Write-VmSkippedRun -VmName $VmName -OutDirectory $OutDirectory -Cases $Item.Cases -RunLabel $Item.Label))
                continue
            }
            $Run = Invoke-DsoVmRun -VmName $VmName -Credential $Credential -BuildDirectory $BuildDirectory -CatalogPath $CatalogPath `
                -Cases $Item.Cases -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds -RunLabel $Item.Label
            $Runs.Add($Run)
        }
        catch {
            $Failure = [System.Exception]::new("Przebieg '$($Item.Label)': $($_.Exception.Message)", $_.Exception)
            $Failure.Data['CompletedRuns'] = $Runs.ToArray()
            throw $Failure
        }
    }
    return $Runs.ToArray()
}

function Get-VmRunsExitCode {
    # Kod wyjścia runnera: 0 tylko gdy nie było błędów VM, żaden test nie padł i zaliczono co najmniej jeden test.
    # Same pominięcia (0 zaliczonych) nigdy nie dają 0.
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Runs,
        [Parameter(Mandatory)][int]$ErrorCount
    )
    if ($ErrorCount -gt 0 -or @($Runs).Count -eq 0) { return 1 }
    if (@($Runs | Where-Object { $_.Failed -gt 0 }).Count -gt 0) { return 1 }
    $Passed = ($Runs | Measure-Object -Property Passed -Sum).Sum
    if ($null -eq $Passed -or $Passed -le 0) { return 1 }
    return 0
}

function Format-VmRunSummary {
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Runs
    )
    $Lines = [System.Collections.Generic.List[string]]::new()
    foreach ($Run in $Runs) {
        $Label = if ($Run.PSObject.Properties['RunLabel'] -and $Run.RunLabel) { " [$($Run.RunLabel)]" } else { '' }
        $Lines.Add(('{0}{1}: zaliczone {2}, błędy {3}, pominięte {4} – {5}' -f $Run.VmName, $Label, $Run.Passed, $Run.Failed, $Run.Skipped, $Run.ReportPath))
    }
    # Suma dla każdej VM, która miała więcej niż jeden przebieg (main + osobne przebiegi Reboot).
    foreach ($Group in @($Runs | Group-Object -Property VmName | Where-Object { $_.Count -gt 1 })) {
        $Passed = ($Group.Group | Measure-Object -Property Passed -Sum).Sum
        $Failed = ($Group.Group | Measure-Object -Property Failed -Sum).Sum
        $Skipped = ($Group.Group | Measure-Object -Property Skipped -Sum).Sum
        $Lines.Add(('{0} razem: zaliczone {1}, błędy {2}, pominięte {3}' -f $Group.Name, $Passed, $Failed, $Skipped))
    }
    return [string[]]$Lines.ToArray()
}

Export-ModuleMember -Function Get-VmAuthFailureMarker, Test-VmAuthFailure, Assert-DsoVmLiveAllowed, Restore-VmCleanState, Wait-VmHeartbeatDrop, Wait-VmReady, Get-VmBootObservation, Copy-DsoBuildToVm, Invoke-DsoVmRun, Invoke-DsoVmRunSet, Get-VmRunsExitCode, Format-VmRunSummary
