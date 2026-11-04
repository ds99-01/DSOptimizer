#Requires -Version 5.1
<#
.SYNOPSIS
    Buduje plik wydania DSOptimizer (jeden exe albo zip folderu) i SHA256SUMS.txt.

.DESCRIPTION
    Instrukcja uruchomienia (Windows PowerShell 5.1, z katalogu repo, bez uprawnień administratora, wymaga .NET SDK z global.json):
        .\tools\release\Build-Release.ps1 -Variant SingleExe                    # wydania\v<wersja>-SingleExe\DSOptimizer-<wersja>.exe
        .\tools\release\Build-Release.ps1 -Variant Folder                       # wydania\v<wersja>-Folder\ z zipem, instalatorem i SHA256SUMS.txt
        .\tools\release\Build-Release.ps1 -Variant Folder -NoInstaller          # tylko zip (bez Inno Setup), na wyraźne życzenie
        .\tools\release\Build-Release.ps1 -Variant Folder -IsccPath 'C:\...\ISCC.exe'   # jawna ścieżka do ISCC.exe
        .\tools\release\Build-Release.ps1 -Variant SingleExe -Tag v4.0.0 -OutRoot out   # CI: tag musi zgadzać się z wersją

    Opis działania:
        Wersję czyta z <Version> w Directory.Build.props. Podany -Tag inny niż „v<wersja>” = błąd i kod 1, nic nie jest budowane.
        Buduje od zera src\DSOptimizer.App w Release (`dotnet build --no-incremental`) do katalogu pośredniego w %TEMP% (usuwanego na końcu):
        SingleExe z /p:DsoSingleExe=true (Costura osadza biblioteki w exe bez kompresji, FodyWeavers.xml), Folder z
        /p:DsoSingleExe=false (zwykły folder exe + DLL). Potem w <OutRoot>\v<wersja>-<wariant>\ zapisuje:
          - SingleExe: DSOptimizer-<wersja>.exe (gdy obok exe zostały biblioteki .dll, Costura nie zadziałała = błąd),
          - Folder: DSOptimizer-<wersja>.zip z katalogiem DSOptimizer\ i całym folderem programu bez plików .pdb,
            oraz (bez -NoInstaller) DSOptimizer-<wersja>-setup.exe skompilowany przez ISCC z tego samego katalogu buildu co zip
            (DSOptimizer.iss obok skryptu; /DAppVersion, /DProgramDir, /O, /F, /Q),
        oraz SHA256SUMS.txt w formacie sha256sum („<sha256 małymi literami>  <nazwa>”, LF, UTF-8 bez BOM): jedna linia
        dla SingleExe i dla Folder z -NoInstaller, dwie dla Folder z instalatorem (najpierw zip, potem instalator).
        ISCC i DSOptimizer.iss są sprawdzane przed budowaniem (brak = błąd, kod 1, nic nie zbudowane). Kolejność szukania
        ISCC bez -IsccPath: $env:DSO_ISCC, %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe, ${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe,
        $env:ProgramFiles\Inno Setup 6\ISCC.exe. Podany -IsccPath albo ustawiony DSO_ISCC, którego nie ma, to błąd (bez dalszego szukania).
        Gdy ISCC zawiedzie (kod różny od 0 albo brak pliku), skrypt usuwa z katalogu wydania zip, instalator i SHA256SUMS.txt
        tego przebiegu. Istniejące pliki wydania o tych nazwach są nadpisywane, inne pliki w folderze zostają. Na wyjściu:
        pełne ścieżki plików wydania (najpierw zip, potem instalator). Skrypt niczego nie zaciemnia ani nie szyfruje
        (spec Etapu 5, sekcja 5).

.PARAMETER Variant
    SingleExe (jeden exe przez Costura.Fody) albo Folder (zip folderu programu).

.PARAMETER OutRoot
    Katalog wydań; domyślnie <repo>\wydania (w .gitignore).

.PARAMETER Tag
    Tag wydania (np. v4.0.0); musi być równy „v” + <Version> z Directory.Build.props.

.PARAMETER NoInstaller
    Wariant Folder bez instalatora (tylko zip i jedna linia w SHA256SUMS.txt); tylko na wyraźne życzenie.

.PARAMETER IsccPath
    Jawna ścieżka do ISCC.exe (Inno Setup 6); nieistniejąca = błąd. Bez niej: $env:DSO_ISCC (ustawiony, ale bez pliku = błąd)
    i typowe katalogi instalacji.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('SingleExe', 'Folder')]
    [string]$Variant,

    [string]$OutRoot,

    [string]$Tag,

    [switch]$NoInstaller,

    [string]$IsccPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -Parent
$AppProject = Join-Path -Path $RepoRoot -ChildPath 'src\DSOptimizer.App\DSOptimizer.App.csproj'
$ExeName = 'DSOptimizer.exe'
$ZipFolderName = 'DSOptimizer'
$SumsFileName = 'SHA256SUMS.txt'

function Get-ReleaseVersion {
    param([Parameter(Mandatory)][string]$PropsPath)

    [xml]$Props = [System.IO.File]::ReadAllText($PropsPath, [System.Text.Encoding]::UTF8)
    $Versions = @($Props.SelectNodes('/Project/PropertyGroup/Version') | ForEach-Object { $_.InnerText.Trim() } | Where-Object { $_ })
    if ($Versions.Count -ne 1) {
        throw "Directory.Build.props musi mieć dokładnie jedno <Version>, znaleziono: $($Versions.Count)."
    }

    if ($Versions[0] -notmatch '^\d+\.\d+\.\d+$') {
        throw "Nieprawidłowa wersja w Directory.Build.props: '$($Versions[0])' (oczekiwano X.Y.Z)."
    }

    return $Versions[0]
}

function Get-FullPath {
    param([Parameter(Mandatory)][string]$Path)
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Invoke-AppBuild {
    param(
        [Parameter(Mandatory)][string]$OutputDirectory,
        [Parameter(Mandatory)][bool]$SingleExe
    )

    $SingleExeValue = if ($SingleExe) { 'true' } else { 'false' }
    # Wartość podana jawnie także dla Folder: zmienna środowiskowa DsoSingleExe nie może po cichu zmienić wariantu.
    # --no-incremental: wydanie zawsze ze źródeł, nigdy z resztek obj po wcześniejszym buildzie.
    & dotnet build $AppProject -c Release "/p:DsoSingleExe=$SingleExeValue" -o $OutputDirectory --no-incremental --nologo | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build zakończył się kodem $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath (Join-Path -Path $OutputDirectory -ChildPath $ExeName) -PathType Leaf)) {
        throw "Po buildzie brak $ExeName w $OutputDirectory."
    }
}

function Write-ZipFromFolder {
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$ZipPath
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $Prefix = $SourceDirectory.TrimEnd('\') + '\'
    # Kolejność porządkowa i separator „/” w nazwach wpisów (Compress-Archive w 5.1 zapisuje „\”), bez plików .pdb.
    $Files = @(Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File |
        Where-Object { $_.Extension -ne '.pdb' } |
        ForEach-Object { [pscustomobject]@{ Path = $_.FullName; Entry = $ZipFolderName + '/' + $_.FullName.Substring($Prefix.Length).Replace('\', '/') } } |
        Sort-Object -Property Entry -CaseSensitive)
    if ($Files.Count -eq 0) {
        throw "Brak plików do spakowania w $SourceDirectory."
    }

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    $Archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($File in $Files) {
            $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive, $File.Path, $File.Entry, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        $Archive.Dispose()
    }
}

function Resolve-IsccPath {
    param([string]$ExplicitPath)

    # Podany jawnie -IsccPath, którego nie ma, to błąd, a nie dalsze szukanie.
    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "Nie znaleziono ISCC.exe pod ścieżką z -IsccPath: $ExplicitPath."
        }

        return (Get-FullPath -Path $ExplicitPath)
    }

    # Ustawiony DSO_ISCC, którego nie ma, to też błąd (jak -IsccPath), a nie cichy powrót do innego ISCC.
    if ($env:DSO_ISCC) {
        if (-not (Test-Path -LiteralPath $env:DSO_ISCC -PathType Leaf)) {
            throw "Nie znaleziono ISCC.exe pod ścieżką ze zmiennej DSO_ISCC: $($env:DSO_ISCC)."
        }

        return $env:DSO_ISCC
    }

    # Kolejność: %LOCALAPPDATA%\Programs, ProgramFiles(x86), ProgramFiles; nieistniejący kandydat jest pomijany.
    $Candidates = @()
    if ($env:LOCALAPPDATA) { $Candidates += (Join-Path -Path $env:LOCALAPPDATA -ChildPath 'Programs\Inno Setup 6\ISCC.exe') }
    if (${env:ProgramFiles(x86)}) { $Candidates += (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath 'Inno Setup 6\ISCC.exe') }
    if ($env:ProgramFiles) { $Candidates += (Join-Path -Path $env:ProgramFiles -ChildPath 'Inno Setup 6\ISCC.exe') }
    foreach ($Candidate in $Candidates) {
        if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
            return $Candidate
        }
    }

    throw 'Nie znaleziono ISCC.exe (Inno Setup 6). Podaj -IsccPath, ustaw DSO_ISCC albo zainstaluj Inno Setup; -NoInstaller buduje samo zip.'
}

function Invoke-InstallerBuild {
    param(
        [Parameter(Mandatory)][string]$Iscc,
        [Parameter(Mandatory)][string]$IssFile,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$ProgramDirectory,
        [Parameter(Mandatory)][string]$OutputDirectory,
        [Parameter(Mandatory)][string]$InstallerPath
    )

    & $Iscc "/DAppVersion=$Version" "/DProgramDir=$ProgramDirectory" "/O$OutputDirectory" "/FDSOptimizer-$Version-setup" '/Q' $IssFile | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC zakończył się kodem $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
        throw "ISCC nie utworzył $InstallerPath."
    }
}

function Write-Sha256Sums {
    param(
        [Parameter(Mandatory)][string[]]$ReleaseFiles,
        [Parameter(Mandatory)][string]$SumsPath
    )

    $Hashes = @()
    $Text = ''
    foreach ($ReleaseFile in $ReleaseFiles) {
        $Hash = (Get-FileHash -LiteralPath $ReleaseFile -Algorithm SHA256).Hash.ToLowerInvariant()
        $Hashes += $Hash
        $Text += ('{0}  {1}' -f $Hash, (Split-Path -Path $ReleaseFile -Leaf)) + "`n"
    }

    [System.IO.File]::WriteAllText($SumsPath, $Text, (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false))
    return $Hashes
}

$BuildDirectory = $null
$OwnedFiles = @()
try {
    $Version = Get-ReleaseVersion -PropsPath (Join-Path -Path $RepoRoot -ChildPath 'Directory.Build.props')
    if ($PSBoundParameters.ContainsKey('Tag') -and $Tag -cne "v$Version") {
        throw "Tag '$Tag' nie zgadza się z wersją z Directory.Build.props (oczekiwano 'v$Version'). Nic nie zbudowano."
    }

    $OutRootPath = if ($OutRoot) { Get-FullPath -Path $OutRoot } else { Join-Path -Path $RepoRoot -ChildPath 'wydania' }
    $ReleaseDirectory = Join-Path -Path $OutRootPath -ChildPath ('v{0}-{1}' -f $Version, $Variant)
    $BuildDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('dso-release-' + [guid]::NewGuid().ToString('N'))

    # ISCC i .iss sprawdzane przed budowaniem: brak = błąd, nic nie zbudowane.
    $WithInstaller = ($Variant -eq 'Folder') -and (-not $NoInstaller)
    if ($WithInstaller) {
        $IssPath = Join-Path -Path $PSScriptRoot -ChildPath 'DSOptimizer.iss'
        if (-not (Test-Path -LiteralPath $IssPath -PathType Leaf)) {
            throw "Brak skryptu instalatora $IssPath."
        }

        $Iscc = Resolve-IsccPath -ExplicitPath $IsccPath
    }

    Invoke-AppBuild -OutputDirectory $BuildDirectory -SingleExe ($Variant -eq 'SingleExe')

    if ($Variant -eq 'SingleExe') {
        $Leftovers = @(Get-ChildItem -LiteralPath $BuildDirectory -Recurse -File -Filter '*.dll' | ForEach-Object { $_.Name })
        if ($Leftovers.Count -gt 0) {
            throw "Costura nie osadziła bibliotek – obok exe zostały: $($Leftovers -join ', ')."
        }
    }

    $null = New-Item -ItemType Directory -Path $ReleaseDirectory -Force
    $SumsPath = Join-Path -Path $ReleaseDirectory -ChildPath $SumsFileName
    $ReleaseFiles = @()
    if ($Variant -eq 'SingleExe') {
        $ReleaseFile = Join-Path -Path $ReleaseDirectory -ChildPath ('DSOptimizer-{0}.exe' -f $Version)
        $OwnedFiles = @($ReleaseFile, $SumsPath)
        Copy-Item -LiteralPath (Join-Path -Path $BuildDirectory -ChildPath $ExeName) -Destination $ReleaseFile -Force
        $ReleaseFiles = @($ReleaseFile)
    }
    else {
        $ReleaseFile = Join-Path -Path $ReleaseDirectory -ChildPath ('DSOptimizer-{0}.zip' -f $Version)
        $InstallerFile = Join-Path -Path $ReleaseDirectory -ChildPath ('DSOptimizer-{0}-setup.exe' -f $Version)
        $OwnedFiles = if ($WithInstaller) { @($ReleaseFile, $InstallerFile, $SumsPath) } else { @($ReleaseFile, $SumsPath) }
        Write-ZipFromFolder -SourceDirectory $BuildDirectory -ZipPath $ReleaseFile
        $ReleaseFiles = @($ReleaseFile)
        if ($WithInstaller) {
            # Instalator z tego samego katalogu buildu co zip.
            if (Test-Path -LiteralPath $InstallerFile) {
                Remove-Item -LiteralPath $InstallerFile -Force
            }

            Invoke-InstallerBuild -Iscc $Iscc -IssFile $IssPath -Version $Version -ProgramDirectory $BuildDirectory -OutputDirectory $ReleaseDirectory -InstallerPath $InstallerFile
            $ReleaseFiles += $InstallerFile
        }
    }

    $Hashes = @(Write-Sha256Sums -ReleaseFiles $ReleaseFiles -SumsPath $SumsPath)
    $Parts = for ($Index = 0; $Index -lt $ReleaseFiles.Count; $Index++) {
        '{0} ({1} B), SHA256 {2}' -f $ReleaseFiles[$Index], (Get-Item -LiteralPath $ReleaseFiles[$Index]).Length, $Hashes[$Index]
    }

    Write-Information -MessageData ('Wydanie {0}: {1}' -f $Variant, ($Parts -join '; ')) -InformationAction Continue
    foreach ($File in $ReleaseFiles) {
        Write-Output $File
    }
}
catch {
    foreach ($File in $OwnedFiles) {
        if (Test-Path -LiteralPath $File) {
            Remove-Item -LiteralPath $File -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Error -ErrorRecord $_ -ErrorAction Continue
    exit 1
}
finally {
    if ($BuildDirectory -and (Test-Path -LiteralPath $BuildDirectory)) {
        Remove-Item -LiteralPath $BuildDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
