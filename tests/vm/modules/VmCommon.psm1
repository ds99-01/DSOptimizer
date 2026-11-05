<#
Instrukcja uruchomienia:
    Import-Module .\tests\vm\modules\VmCommon.psm1 -Force
Opis działania:
    Wspólny moduł testów VM: poświadczenia VM (%LOCALAPPDATA%\DSOptimizer-vm\<VmName>.credential.xml,
    Export-Clixml/DPAPI) oraz układ katalogów roboczych wewnątrz VM. Hasło kont Administrator i dso
    to `admin` (VM testowe bez danych, cofane do checkpointu); poświadczenie zapisuje się też poza
    repo (DPAPI), skąd czytają je skrypty.
    Lista VM testów (nazwa, edycja, język, region, role) ma jedno źródło: Get-DsoVmDefinitions.
    Bezpieczniki w VM są literałami pilnowanymi przez VmNames.Tests.ps1.
#>
Set-StrictMode -Version Latest

function Get-DsoRepoRoot {
    [CmdletBinding()]
    [OutputType([string])]
    param()
    # modules -> vm -> tests -> korzeń repo
    return (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
}

function Get-DsoVmSecretDirectory {
    [CmdletBinding()]
    [OutputType([string])]
    param()
    return (Join-Path $env:LOCALAPPDATA 'DSOptimizer-vm')
}

function Get-DsoVmCredentialPath {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$VmName
    )
    $Path = [System.IO.Path]::GetFullPath((Join-Path (Get-DsoVmSecretDirectory) "$VmName.credential.xml"))
    $RepoRoot = [System.IO.Path]::GetFullPath((Get-DsoRepoRoot)).TrimEnd('\')
    if ($Path.StartsWith($RepoRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Ścieżka poświadczeń '$Path' leży wewnątrz repo ('$RepoRoot'). Sekrety muszą być poza repo."
    }
    return $Path
}

function Save-DsoVmCredential {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$VmName,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Password
    )
    $Path = Get-DsoVmCredentialPath -VmName $VmName
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force
    $Secure = ConvertTo-SecureString -String $Password -AsPlainText -Force
    $Credential = New-Object System.Management.Automation.PSCredential('Administrator', $Secure)
    $Credential | Export-Clixml -Path $Path
    return $Path
}

function Get-DsoVmCredential {
    [CmdletBinding()]
    [OutputType([pscredential])]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$VmName
    )
    $Path = Get-DsoVmCredentialPath -VmName $VmName
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Brak poświadczeń VM '$VmName' – uruchom New-TestVm.ps1"
    }
    return (Import-Clixml -LiteralPath $Path)
}

function Get-DsoVmLayout {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param()
    return @{
        Root    = 'C:\dso'
        App     = 'C:\dso\app'
        Exe     = 'C:\dso\app\DSOptimizer.exe'
        Catalog = 'C:\dso\vm-test-catalog.json'
        Out     = 'C:\dso\out'
    }
}

function New-VmSkipRecord {
    # Jedyny kształt rekordu przypadku pominiętego (Skip): używa go scenariusz (BeforeDiscovery) i Write-VmSkippedRun.
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Case)
    if (-not $Case.ContainsKey('Skip')) { throw "Przypadek '$($Case.Name)' nie ma klucza 'Skip' - nie można utworzyć rekordu pominięcia." }
    return [pscustomobject]@{ Name = $Case.Name; TweakId = $Case.TweakId; Skipped = $true; SkipReason = [string]$Case.Skip }
}

function New-VmSkipReport {
    # Szkielet report.json przebiegu złożonego wyłącznie z pominięć (passed = 0, skipped = liczba rekordów).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$VmName,
        [Parameter(Mandatory)][string]$RunLabel,
        [Parameter(Mandatory)][datetime]$StartedAt,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Records
    )
    return [ordered]@{
        vmName          = $VmName
        runLabel        = $RunLabel
        startedAt       = $StartedAt.ToString('o')
        finishedAt      = (Get-Date).ToString('o')
        bootObservation = @()
        pester          = [ordered]@{ passed = 0; failed = 0; skipped = @($Records).Count; failedTests = @() }
        cases           = @($Records)
    }
}

function Get-DsoVmDefinitions {
    # Jedyne źródło listy VM testów; każde wywołanie zwraca nowe obiekty.
    $Rows = @(
        @{ Name = 'DSO-EN-Home'; Edition = 'Home'; EditionId = 'Core'; Language = 'en'; Culture = 'en-US'; InputLocale = '0409:00000409'; FullRun = $true; SmokeLanguages = @('en'); ReleaseExplorations = $true }
        @{ Name = 'DSO-EN-Pro'; Edition = 'Pro'; EditionId = 'Professional'; Language = 'en'; Culture = 'en-US'; InputLocale = '0409:00000409'; FullRun = $true; SmokeLanguages = @('en', 'pl'); ReleaseExplorations = $true }
        @{ Name = 'DSO-PL-Pro'; Edition = 'Pro'; EditionId = 'Professional'; Language = 'pl'; Culture = 'pl-PL'; InputLocale = '0415:00000415'; FullRun = $true; SmokeLanguages = @('pl', 'en'); ReleaseExplorations = $true }
        @{ Name = 'DSO-PL-Home'; Edition = 'Home'; EditionId = 'Core'; Language = 'pl'; Culture = 'pl-PL'; InputLocale = '0415:00000415'; FullRun = $false; SmokeLanguages = @('pl'); ReleaseExplorations = $false }
    )
    foreach ($Row in $Rows) {
        [pscustomobject][ordered]@{
            Name                = [string]$Row.Name
            ComputerName        = ([string]$Row.Name).ToUpperInvariant()
            Edition             = [string]$Row.Edition
            EditionId           = [string]$Row.EditionId
            Language            = [string]$Row.Language
            Culture             = [string]$Row.Culture
            InputLocale         = [string]$Row.InputLocale
            FullRun             = [bool]$Row.FullRun
            SmokeLanguages      = [string[]]@($Row.SmokeLanguages)
            ReleaseExplorations = [bool]$Row.ReleaseExplorations
        }
    }
}

function Get-DsoVmDefinition {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$VmName)
    $All = @(Get-DsoVmDefinitions)
    foreach ($Definition in $All) { if ($Definition.Name -ieq $VmName) { return $Definition } }
    throw "Nieznana VM '$VmName'. Znane: $((@($All | ForEach-Object { $_.Name })) -join ', ')."
}

function Get-DsoVmNames {
    [CmdletBinding()]
    [OutputType([string[]])]
    param([switch]$FullRun, [ValidateSet('en', 'pl')][string]$Language)
    $Items = @(Get-DsoVmDefinitions)
    if ($FullRun) { $Items = @($Items | Where-Object { $_.FullRun }) }
    if ($Language) { $Items = @($Items | Where-Object { $_.Language -eq $Language }) }
    return [string[]]@($Items | ForEach-Object { $_.Name })
}

function Get-DsoVmNamePattern { return '^DSO-(EN|PL)-(Home|Pro)\z' }
Export-ModuleMember -Function New-VmSkipRecord, New-VmSkipReport, Get-DsoRepoRoot, Get-DsoVmSecretDirectory, Get-DsoVmCredentialPath, Save-DsoVmCredential, Get-DsoVmCredential, Get-DsoVmLayout, Get-DsoVmDefinitions, Get-DsoVmDefinition, Get-DsoVmNames, Get-DsoVmNamePattern
