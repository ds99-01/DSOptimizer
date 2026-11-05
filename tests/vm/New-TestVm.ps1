<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, funkcja Hyper-V włączona.
    VM angielskie (ISO en-US):
        .\tests\vm\New-TestVm.ps1 -IsoPath '<ścieżka ISO en-US>' -VmName DSO-EN-Home,DSO-EN-Pro
    VM polskie (ISO pl-PL):
        .\tests\vm\New-TestVm.ps1 -IsoPath '<ścieżka ISO pl-PL>' -VmName DSO-PL-Home,DSO-PL-Pro
    Z siecią (Default Switch): dodaj -WithNetwork
Opis działania:
    Tworzy VM Hyper-V Gen2 (Secure Boot, vTPM) z Windows 11 26H2: wybiera z ISO obraz po EditionId
    (Core = Home, Professional = Pro) i języku VM, nakłada go offline (Expand-WindowsImage + bcdboot),
    dokłada unattend.xml z językiem celu, czeka na koniec pierwszej konfiguracji, porównuje
    EditionID i język z celem, wyłącza VM i robi checkpoint 'czysta-26H2'. Gdy obraz nie pasuje
    (brak edycji, dwa obrazy, zły język ISO), skrypt kończy się błędem, zanim cokolwiek utworzy.
    Hasło kont Administrator i dso to `admin` (VM testowe bez danych, cofane do checkpointu);
    poświadczenie zapisuje się też poza repo (DPAPI), skąd czytają je skrypty. Raport budowy
    trafia do %LOCALAPPDATA%\DSOptimizer-vm\<VmName>.build.json.
    Gdy VM lub jej VHDX już istnieje, skrypt odmawia i niczego nie nadpisuje (także poświadczeń).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IsoPath,
    [Parameter(Mandatory)][ValidateSet('DSO-EN-Home', 'DSO-EN-Pro', 'DSO-PL-Pro', 'DSO-PL-Home')][string[]]$VmName,
    [string]$VhdDirectory,
    [ValidateRange(20, 512)][int]$VhdSizeGB = 64,
    [ValidateRange(2, 64)][int]$MemoryGB = 4,
    [ValidateRange(1, 16)][int]$ProcessorCount = 4,
    [switch]$WithNetwork,
    [string]$CheckpointName = 'czysta-26H2',
    [ValidateRange(0.001, 600)][double]$FirstBootTimeoutMinutes = 45
)
Set-StrictMode -Version Latest

try {
    Import-Module (Join-Path $PSScriptRoot 'modules\VmCommon.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'modules\VmBuild.psm1') -Force

    $Targets = Get-TestVmTargets -VmName $VmName

    $BuildArguments = @{
        IsoPath                 = $IsoPath
        Targets                 = $Targets
        VhdSizeGB               = $VhdSizeGB
        MemoryGB                = $MemoryGB
        ProcessorCount          = $ProcessorCount
        CheckpointName          = $CheckpointName
        FirstBootTimeoutMinutes = $FirstBootTimeoutMinutes
        WithNetwork             = $WithNetwork
    }
    if ($VhdDirectory) { $BuildArguments.VhdDirectory = $VhdDirectory }
    New-DsoTestVm @BuildArguments
}
catch {
    Write-Error -Message ($_.Exception.Message + [Environment]::NewLine + $_.ScriptStackTrace)
    exit 1
}
