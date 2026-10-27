<#
Instrukcja uruchomienia:
    Sesja PowerShell uruchomiona jako administrator, funkcja Hyper-V włączona.
    Obie VM (Home index 1 i Pro index 5):
        .\tests\vm\New-TestVm.ps1 -IsoPath '<ścieżka ISO>'
    Jedna VM:
        .\tests\vm\New-TestVm.ps1 -IsoPath '<ścieżka ISO>' -VmName DSO-26H2-Pro -EditionIndex 5
    Z siecią (Default Switch):
        .\tests\vm\New-TestVm.ps1 -IsoPath '<ścieżka ISO>' -WithNetwork
Opis działania:
    Tworzy VM Hyper-V Gen2 (Secure Boot, vTPM) z Windows 11 26H2: nakłada obraz z ISO offline
    (Expand-WindowsImage + bcdboot), dokłada unattend.xml, czeka na koniec pierwszej konfiguracji,
    wyłącza VM i robi checkpoint 'czysta-26H2'. Hasło generuje i zapisuje poza repo
    (%LOCALAPPDATA%\DSOptimizer-vm\<VmName>.credential.xml); nie jest nigdzie wypisywane.
    Gdy VM lub jej VHDX już istnieje, skrypt odmawia i niczego nie nadpisuje (także poświadczeń).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IsoPath,
    [string]$VmName,
    [int]$EditionIndex,
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

    $TargetArguments = @{}
    if ($PSBoundParameters.ContainsKey('VmName')) { $TargetArguments.VmName = $VmName }
    if ($PSBoundParameters.ContainsKey('EditionIndex')) { $TargetArguments.EditionIndex = $EditionIndex }
    $Targets = Get-TestVmTargets @TargetArguments

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
