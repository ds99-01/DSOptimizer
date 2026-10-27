#Requires -Version 5.1
<#
.SYNOPSIS
    Buduje plik wydania DSOptimizer (jeden exe albo zip folderu) i SHA256SUMS.txt.

.DESCRIPTION
    Instrukcja uruchomienia (Windows PowerShell 5.1, z katalogu repo, bez uprawnień administratora, wymaga .NET SDK z global.json):
        .\tools\release\Build-Release.ps1 -Variant SingleExe                    # wydania\v<wersja>-SingleExe\DSOptimizer-<wersja>.exe
        .\tools\release\Build-Release.ps1 -Variant Folder                       # wydania\v<wersja>-Folder\DSOptimizer-<wersja>.zip
        .\tools\release\Build-Release.ps1 -Variant SingleExe -Tag v4.0.0 -OutRoot out   # CI: tag musi zgadzać się z wersją

    Opis działania:
        Wersję czyta z <Version> w Directory.Build.props. Podany -Tag inny niż „v<wersja>” = błąd i kod 1, nic nie jest budowane.
        Buduje od zera src\DSOptimizer.App w Release (`dotnet build --no-incremental`) do katalogu pośredniego w %TEMP% (usuwanego na końcu):
        SingleExe z /p:DsoSingleExe=true (Costura osadza biblioteki w exe bez kompresji, FodyWeavers.xml), Folder z
        /p:DsoSingleExe=false (zwykły folder exe + DLL). Potem w <OutRoot>\v<wersja>-<wariant>\ zapisuje:
          - SingleExe: DSOptimizer-<wersja>.exe (gdy obok exe zostały biblioteki .dll, Costura nie zadziałała = błąd),
          - Folder: DSOptimizer-<wersja>.zip z katalogiem DSOptimizer\ i całym folderem programu bez plików .pdb,
        oraz SHA256SUMS.txt w formacie sha256sum („<sha256 małymi literami>  <nazwa>”, LF, UTF-8 bez BOM).
        Istniejące pliki wydania o tych nazwach są nadpisywane, inne pliki w folderze zostają. Na wyjściu: pełna ścieżka
        pliku wydania. Skrypt niczego nie zaciemnia, nie szyfruje ani nie pakuje (spec Etapu 5, sekcja 5).

.PARAMETER Variant
    SingleExe (jeden exe przez Costura.Fody) albo Folder (zip folderu programu).

.PARAMETER OutRoot
    Katalog wydań; domyślnie <repo>\wydania (w .gitignore).

.PARAMETER Tag
    Tag wydania (np. v4.0.0); musi być równy „v” + <Version> z Directory.Build.props.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('SingleExe', 'Folder')]
    [string]$Variant,

    [string]$OutRoot,

    [string]$Tag
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

function Write-Sha256Sums {
    param(
        [Parameter(Mandatory)][string]$ReleaseFile,
        [Parameter(Mandatory)][string]$SumsPath
    )

    $Hash = (Get-FileHash -LiteralPath $ReleaseFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $Line = '{0}  {1}' -f $Hash, (Split-Path -Path $ReleaseFile -Leaf)
    [System.IO.File]::WriteAllText($SumsPath, $Line + "`n", (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false))
    return $Hash
}

$BuildDirectory = $null
try {
    $Version = Get-ReleaseVersion -PropsPath (Join-Path -Path $RepoRoot -ChildPath 'Directory.Build.props')
    if ($PSBoundParameters.ContainsKey('Tag') -and $Tag -cne "v$Version") {
        throw "Tag '$Tag' nie zgadza się z wersją z Directory.Build.props (oczekiwano 'v$Version'). Nic nie zbudowano."
    }

    $OutRootPath = if ($OutRoot) { Get-FullPath -Path $OutRoot } else { Join-Path -Path $RepoRoot -ChildPath 'wydania' }
    $ReleaseDirectory = Join-Path -Path $OutRootPath -ChildPath ('v{0}-{1}' -f $Version, $Variant)
    $BuildDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('dso-release-' + [guid]::NewGuid().ToString('N'))

    Invoke-AppBuild -OutputDirectory $BuildDirectory -SingleExe ($Variant -eq 'SingleExe')

    if ($Variant -eq 'SingleExe') {
        $Leftovers = @(Get-ChildItem -LiteralPath $BuildDirectory -Recurse -File -Filter '*.dll' | ForEach-Object { $_.Name })
        if ($Leftovers.Count -gt 0) {
            throw "Costura nie osadziła bibliotek – obok exe zostały: $($Leftovers -join ', ')."
        }
    }

    $null = New-Item -ItemType Directory -Path $ReleaseDirectory -Force
    if ($Variant -eq 'SingleExe') {
        $ReleaseFile = Join-Path -Path $ReleaseDirectory -ChildPath ('DSOptimizer-{0}.exe' -f $Version)
        Copy-Item -LiteralPath (Join-Path -Path $BuildDirectory -ChildPath $ExeName) -Destination $ReleaseFile -Force
    }
    else {
        $ReleaseFile = Join-Path -Path $ReleaseDirectory -ChildPath ('DSOptimizer-{0}.zip' -f $Version)
        Write-ZipFromFolder -SourceDirectory $BuildDirectory -ZipPath $ReleaseFile
    }

    $Hash = Write-Sha256Sums -ReleaseFile $ReleaseFile -SumsPath (Join-Path -Path $ReleaseDirectory -ChildPath $SumsFileName)
    $Size = (Get-Item -LiteralPath $ReleaseFile).Length
    Write-Information -MessageData ('Wydanie {0}: {1} ({2} B), SHA256 {3}' -f $Variant, $ReleaseFile, $Size, $Hash) -InformationAction Continue
    Write-Output $ReleaseFile
}
catch {
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    exit 1
}
finally {
    if ($BuildDirectory -and (Test-Path -LiteralPath $BuildDirectory)) {
        Remove-Item -LiteralPath $BuildDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
