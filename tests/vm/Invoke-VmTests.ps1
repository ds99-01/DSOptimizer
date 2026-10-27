<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, na hoście z Hyper-V, z Pesterem 5+.
    VM utworzone wcześniej przez New-TestVm.ps1 (checkpoint 'czysta-26H2', poświadczenia w %LOCALAPPDATA%\DSOptimizer-vm).
    Przed przebiegiem zbuduj aplikację:
        dotnet build DSOptimizer.sln -c Release
    Obie VM (DSO-26H2-Home i DSO-26H2-Pro):
        .\tests\vm\Invoke-VmTests.ps1
    Jedna VM:
        .\tests\vm\Invoke-VmTests.ps1 -VmName DSO-26H2-Pro
Opis działania:
    Dla każdej VM: przywraca checkpoint, startuje VM, czeka na PowerShell Direct i zalogowanego użytkownika 'dso',
    kopiuje build i katalog testowy, uruchamia scenariusze Pester (tests\vm\scenarios) przeciw VM i zapisuje raport
    <OutDirectory>\<VM>\<RunLabel>\report.json. Przypadki Flow='Reboot' (restartują VM) dostają osobny przebieg
    (RunLabel reboot-<nazwa>) od czystego checkpointu; wszystkie pozostałe idą razem w przebiegu 'main'. Każdy przebieg
    kończy się (także po błędzie) przywróceniem VM do czystego checkpointu, a po ostatnim VM zostaje wyłączona.
    Podsumowanie sumuje przebiegi (także ukończone przed błędem). Przypadki ze Skip, gdy nie ma aktywnych zwykłych przypadków, dostają przebieg
    'skipped' bez VM (rekordy pominięcia). Błąd jednej VM jest zapisywany i nie przerywa przebiegu drugiej. Kod wyjścia: 0 gdy wszystko zaliczone
    i zaliczono co najmniej jeden test, 1 w przeciwnym razie (same pominięcia nigdy nie dają 0).
    Hasło VM nigdy nie jest wypisywane.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^DSO-26H2-[A-Za-z]+\z')][string[]]$VmName = @('DSO-26H2-Home', 'DSO-26H2-Pro'),
    [string]$BuildDirectory,
    [string]$CatalogPath,
    [string]$CasesPath,
    [string]$OutDirectory,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(60, 7200)][int]$ReadyTimeoutSeconds = 1200
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitCode = 1
try {
    $VmRoot = $PSScriptRoot
    Import-Module (Join-Path $VmRoot 'modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $VmRoot 'modules\VmRun.psm1') -Force
    Import-Module (Join-Path $VmRoot 'modules\VmHost.psm1') -Force
    $RepoRoot = Get-DsoRepoRoot
    if (-not $BuildDirectory) { $BuildDirectory = Join-Path $RepoRoot 'src\DSOptimizer.App\bin\Release\net481' }
    if (-not $CatalogPath) { $CatalogPath = Join-Path $VmRoot 'catalog\vm-test-catalog.json' }
    if (-not $CasesPath) { $CasesPath = Join-Path $VmRoot 'cases.psd1' }
    if (-not $OutDirectory) { $OutDirectory = Join-Path $VmRoot ('out\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }

    if (-not (Get-Module -ListAvailable -Name Pester | Where-Object { $_.Version -ge [version]'5.0' })) {
        throw 'Brak modułu Pester 5 lub nowszego. Zainstaluj: Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser'
    }
    Import-Module Pester -MinimumVersion 5.0

    # Import-VmCases zwraca tablicę jednym obiektem (, $Result); @() owinęłoby ją w drugą tablicę i Cases nie zbindowałoby się do hashtable[].
    $Cases = Import-VmCases -CasesPath $CasesPath -CatalogPath $CatalogPath
    if ($null -eq $Cases -or @($Cases).Count -eq 0) { throw "Brak przypadków testowych w '$CasesPath' - przebieg nie sprawdziłby niczego." }

    $Principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $Principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Uruchom skrypt w sesji PowerShell jako administrator (Hyper-V i PowerShell Direct tego wymagają).'
    }
    $Exe = Join-Path $BuildDirectory 'DSOptimizer.exe'
    if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
        throw "Brak '$Exe'. Zbuduj aplikację: dotnet build DSOptimizer.sln -c Release"
    }
    if (-not (Test-Path -LiteralPath $CatalogPath -PathType Leaf)) { throw "Brak katalogu testowego '$CatalogPath'." }

    $null = New-Item -ItemType Directory -Path $OutDirectory -Force
    Write-Host "Raporty: $OutDirectory"

    $Runs = [System.Collections.Generic.List[object]]::new()
    $Errors = [System.Collections.Generic.List[string]]::new()
    foreach ($Name in $VmName) {
        Write-Host "=== VM $Name ==="
        try {
            $Credential = Get-DsoVmCredential -VmName $Name
            $VmRuns = Invoke-DsoVmRunSet -VmName $Name -Credential $Credential -BuildDirectory $BuildDirectory -CatalogPath $CatalogPath `
                -Cases $Cases -OutDirectory $OutDirectory -CheckpointName $CheckpointName -ReadyTimeoutSeconds $ReadyTimeoutSeconds
            foreach ($Run in $VmRuns) { $Runs.Add($Run) }
        }
        catch {
            # Ukończone przebiegi tej VM (przed wyjątkiem) zostają w podsumowaniu.
            if ($_.Exception.Data.Contains('CompletedRuns')) {
                foreach ($Run in @($_.Exception.Data['CompletedRuns'])) { $Runs.Add($Run) }
            }
            $Message = "VM '$Name': $($_.Exception.Message)"
            Write-Host "BŁĄD $Message" -ForegroundColor Red
            Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
            $Errors.Add($Message)
        }
    }

    Write-Host ''
    if ($Runs.Count -gt 0) { Format-VmRunSummary -Runs $Runs.ToArray() | Write-Host }
    foreach ($Message in $Errors) { Write-Host "BŁĄD $Message" -ForegroundColor Red }

    # 0 tylko gdy bez błędów, bez nieudanych testów i z co najmniej jednym zaliczonym (same pominięcia to nie sukces).
    $ExitCode = Get-VmRunsExitCode -Runs $Runs.ToArray() -ErrorCount $Errors.Count
}
catch {
    Write-Host "BŁĄD: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    $ExitCode = 1
}
exit $ExitCode
