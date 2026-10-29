#Requires -Version 5.1
<#
.SYNOPSIS
    Wpisuje odcisk polskiego źródła w pierwszej linii angielskiego dokumentu docs/en/(tweaks|apps|prep)/*.md.

.DESCRIPTION
    # Instrukcja uruchomienia (Windows PowerShell 5.1, z katalogu repo):
    #     .\tools\i18n\Set-DocsTranslationStamp.ps1 -Path docs\en\tweaks\README.md
    #     .\tools\i18n\Set-DocsTranslationStamp.ps1 -Path docs\en\apps\README.md,docs\en\prep\README.md
    #     .\tools\i18n\Set-DocsTranslationStamp.ps1 -Path docs\en\tweaks\<id>.md -Root <repo>   # inny korzeń repo
    # Uruchamiaj tylko po przetłumaczeniu aktualnej polskiej treści – odcisk mówi, że angielski odpowiada temu źródłu.
    # Opis działania:
    #     Każda ścieżka (względna wobec -Root albo bezwzględna) musi wskazywać istniejący plik docs/en/<x>/<plik>.md, gdzie
    #     <x> to tweaks, apps albo prep (bez podkatalogów), a polskie źródło docs/<x>/<plik>.md musi istnieć. Najpierw
    #     sprawdza wszystkie ścieżki; przy pierwszym błędzie wypisuje go i kończy z kodem 2, nie zmieniając żadnego pliku.
    #     Wielkość liter ścieżki (np. PERF.sysmain.md) jest brana z systemu plików, więc odcisk ma prawdziwą nazwę źródła.
    #     Hash źródła: SHA-256 bajtów UTF-8 polskiego pliku po usunięciu BOM i zamianie CRLF na LF. Linia odcisku:
    #     <!-- source: docs/<x>/<plik>.md sha256: <64 małe hex> -->. Jeśli angielski plik zaczyna się od "<!-- source:",
    #     ta linia jest zastępowana; inaczej na początku dopisywana jest linia odcisku i pusta linia. Końce linii pliku
    #     (CRLF albo LF) zostają, zapis UTF-8 bez BOM. Raport Get-MissingTranslations.ps1 porównuje ten odcisk
    #     z bieżącym polskim plikiem (nieaktualny = brak tłumaczenia).
    #     Kod wyjścia: 0 = odciski wpisane; 2 = ścieżka spoza docs/en/(tweaks|apps|prep)/, brak pliku EN, brak polskiego
    #     źródła albo inny błąd.

.PARAMETER Path
    Angielskie dokumenty do oznaczenia (jeden albo kilka).

.PARAMETER Root
    Korzeń repo (domyślnie dwa katalogi nad skryptem).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,
    [string]$Root
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DocsSourceHash {
    # Ten sam hash co Get-DocsSourceHash w tools\i18n\Get-MissingTranslations.ps1 – zmiana tu wymaga zmiany tam.
    param([string]$FullPath)
    $Text = [System.IO.File]::ReadAllText($FullPath, [System.Text.Encoding]::UTF8)
    $Bytes = (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false).GetBytes($Text.Replace("`r`n", "`n"))
    $Sha = [System.Security.Cryptography.SHA256]::Create()
    try { $Hash = $Sha.ComputeHash($Bytes) } finally { $Sha.Dispose() }
    return (($Hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-ActualCaseRelativePath {
    # Ścieżka względna wobec $RootPath z wielkością liter z systemu plików (Windows nie rozróżnia wielkości liter,
    # a odcisk i raport porównują ścieżki dokładnie). Segment, którego nie ma na dysku, zostaje bez zmian.
    param([string]$RootPath, [string]$Relative)
    $Current = $RootPath
    $Segments = New-Object -TypeName 'System.Collections.Generic.List[string]'
    $Exists = $true
    foreach ($Segment in $Relative.Split('/')) {
        $Name = $Segment
        if ($Exists -and [System.IO.Directory]::Exists($Current)) {
            $Entry = [System.IO.DirectoryInfo]::new($Current).GetFileSystemInfos() |
                Where-Object { [string]::Equals($_.Name, $Segment, [System.StringComparison]::OrdinalIgnoreCase) } |
                Select-Object -First 1
            if ($null -ne $Entry) { $Name = $Entry.Name } else { $Exists = $false }
        }
        else { $Exists = $false }
        $Segments.Add($Name)
        $Current = Join-Path -Path $Current -ChildPath $Name
    }
    return ($Segments -join '/')
}

try {
    if ([string]::IsNullOrEmpty($Root)) {
        $ScriptDirectory = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
        $Root = Join-Path -Path $ScriptDirectory -ChildPath '..\..'
    }
    $RootPath = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')

    $Targets = New-Object -TypeName 'System.Collections.Generic.List[object]'
    foreach ($Item in $Path) {
        if ([System.IO.Path]::IsPathRooted($Item)) { $Full = [System.IO.Path]::GetFullPath($Item) }
        else { $Full = [System.IO.Path]::GetFullPath((Join-Path -Path $RootPath -ChildPath $Item)) }
        $Relative = $Item.Replace('\', '/')
        if ($Full.StartsWith($RootPath + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            $Relative = Get-ActualCaseRelativePath -RootPath $RootPath -Relative $Full.Substring($RootPath.Length + 1).Replace('\', '/')
            $Full = Join-Path -Path $RootPath -ChildPath $Relative.Replace('/', '\')
        }
        $Match = [regex]::Match($Relative, '^docs/en/((?:tweaks|apps|prep)/[^/]+\.md)$')
        if (-not $Match.Success) {
            Write-Output ('Błąd: {0} – odcisk dotyczy tylko docs/en/(tweaks|apps|prep)/*.md.' -f $Relative)
            exit 2
        }
        if (-not (Test-Path -LiteralPath $Full -PathType Leaf)) {
            Write-Output ('Błąd: nie ma pliku {0}.' -f $Relative)
            exit 2
        }
        $Source = Get-ActualCaseRelativePath -RootPath $RootPath -Relative ('docs/' + $Match.Groups[1].Value)
        $SourceFull = Join-Path -Path $RootPath -ChildPath $Source.Replace('/', '\')
        if (-not (Test-Path -LiteralPath $SourceFull -PathType Leaf)) {
            Write-Output ('Błąd: {0} – nie ma polskiego źródła {1}.' -f $Relative, $Source)
            exit 2
        }
        $Targets.Add([pscustomobject]@{ Full = $Full; Relative = $Relative; Source = $Source; SourceFull = $SourceFull })
    }

    $Utf8NoBom = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
    foreach ($Target in $Targets) {
        $Stamp = '<!-- source: {0} sha256: {1} -->' -f $Target.Source, (Get-DocsSourceHash -FullPath $Target.SourceFull)
        $Content = [System.IO.File]::ReadAllText($Target.Full, [System.Text.Encoding]::UTF8)
        $NewLine = "`n"
        if ($Content.Contains("`r`n")) { $NewLine = "`r`n" }
        if ($Content.StartsWith('<!-- source:', [System.StringComparison]::Ordinal)) {
            $End = $Content.IndexOf("`n")
            if ($End -lt 0) { $Rest = '' }
            else {
                $LineEnd = $End
                if ($End -gt 0 -and $Content[$End - 1] -eq "`r") { $LineEnd = $End - 1 }
                $Rest = $Content.Substring($LineEnd)
            }
            $Updated = $Stamp + $Rest
        }
        else {
            $Updated = $Stamp + $NewLine + $NewLine + $Content
        }
        [System.IO.File]::WriteAllText($Target.Full, $Updated, $Utf8NoBom)
        Write-Output ('Odcisk: {0} <- {1}' -f $Target.Relative, $Target.Source)
    }
    exit 0
}
catch {
    Write-Output ('Błąd: {0}' -f $_.Exception.Message)
    exit 2
}
