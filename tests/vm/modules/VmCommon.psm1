<#
Instrukcja uruchomienia:
    Import-Module .\tests\vm\modules\VmCommon.psm1 -Force
Opis działania:
    Wspólny moduł testów VM: generowanie hasła, poświadczenia VM trzymane wyłącznie poza repo
    (%LOCALAPPDATA%\DSOptimizer-vm\<VmName>.credential.xml, Export-Clixml/DPAPI) oraz układ
    katalogów roboczych wewnątrz VM. Hasło nigdy nie trafia do repo, logów ani wyjścia.
#>
Set-StrictMode -Version Latest

$script:UpperChars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
$script:LowerChars = 'abcdefghijklmnopqrstuvwxyz'
$script:DigitChars = '0123456789'
$script:SymbolChars = '-_.!#+='

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

function Get-DsoRandomIndex {
    # Równomierny losowy indeks [0, Max) bez obciążenia modulo (rejection sampling).
    param(
        [Parameter(Mandatory)][System.Security.Cryptography.RandomNumberGenerator]$Generator,
        [Parameter(Mandatory)][int]$Max
    )
    $Buffer = New-Object byte[] 4
    $Limit = [uint32]::MaxValue - ([uint32]::MaxValue % [uint32]$Max)
    do {
        $Generator.GetBytes($Buffer)
        $Value = [BitConverter]::ToUInt32($Buffer, 0)
    } while ($Value -ge $Limit)
    return [int]($Value % [uint32]$Max)
}

function New-DsoVmPassword {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [ValidateRange(4, 128)][int]$Length = 20
    )
    $Classes = @($script:UpperChars, $script:LowerChars, $script:DigitChars, $script:SymbolChars)
    $All = $Classes -join ''
    $Generator = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    try {
        $Chars = New-Object System.Collections.Generic.List[char]
        foreach ($Class in $Classes) {
            $Chars.Add($Class[(Get-DsoRandomIndex -Generator $Generator -Max $Class.Length)])
        }
        while ($Chars.Count -lt $Length) {
            $Chars.Add($All[(Get-DsoRandomIndex -Generator $Generator -Max $All.Length)])
        }
        # Fisher-Yates, żeby wymuszone znaki nie stały zawsze na początku.
        for ($i = $Chars.Count - 1; $i -gt 0; $i--) {
            $j = Get-DsoRandomIndex -Generator $Generator -Max ($i + 1)
            $Temp = $Chars[$i]; $Chars[$i] = $Chars[$j]; $Chars[$j] = $Temp
        }
        return (-join $Chars)
    }
    finally {
        $Generator.Dispose()
    }
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

Export-ModuleMember -Function New-VmSkipRecord, New-VmSkipReport, Get-DsoRepoRoot, Get-DsoVmSecretDirectory, New-DsoVmPassword, Get-DsoVmCredentialPath, Save-DsoVmCredential, Get-DsoVmCredential, Get-DsoVmLayout
