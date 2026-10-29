#Requires -Version 5.1
<#
.SYNOPSIS
    Raport braków tłumaczeń: polskie teksty z L.T/L.F/L.P i {l:T '…'} bez angielskiego odpowiednika oraz polska
    dokumentacja bez aktualnej wersji w docs\en.

.DESCRIPTION
    # Instrukcja uruchomienia (Windows PowerShell 5.1, z katalogu repo):
    #     .\tools\i18n\Get-MissingTranslations.ps1                    # raport, kod 0
    #     .\tools\i18n\Get-MissingTranslations.ps1 -FailOnMissing     # kod 1, gdy są braki
    #     .\tools\i18n\Get-MissingTranslations.ps1 -Root <repo>       # inny korzeń repo
    # Opis działania:
    #     Przeszukuje src\**\*.cs (wywołania L.T("…"), L.F("…", …) i L.P("…", "…", "…", n) – z L.P trzy klucze: formy
    #     one, few i many; few nie jest kluczem angielskim, ale dostaje tłumaczenie równe many) oraz src\**\*.xaml ({l:T '…'}), pomija foldery obj i bin
    #     oraz src\DSOptimizer.Core\Localization\L.cs. Zasady literałów pilnuje test TranslationCallRulesTests, więc wystarczą
    #     proste wyrażenia regularne. Teksty są rozkodowywane (C#: \\ \" \' \0 \n \r \t \uXXXX; XAML: encje XML,
    #     potem \' i \\). Tłumaczenia czyta z src\DSOptimizer.Core\Localization\Translations\*.json
    #     ({"language":"en","entries":[{"pl":"…","en":"…"}]}, tabele danych z "data": true tak samo); tekst jest przetłumaczony,
    #     gdy ma wpis z niepustym "en". Teksty danych (L.Data) czyta z src\DSOptimizer.Catalog\catalog\*.json: pozycje tablic
    #     tweaków – title, summary, benefit, description, każda uwaga z warnings; apps.json – summary, warning (bez title, to nazwy
    #     produktów); apps-bloat.json – entries[].summary, entries[].warning, protected[].reason. Miejsce takiego tekstu:
    #     src/DSOptimizer.Catalog/catalog/<plik>#<id>.<pole>; teksty danych liczą się jako używane. Z pozycji tweaków (bez
    #     apps*.json) także komunikaty skryptów: pierwszy argument Get-DsoText '…' w polach script.apply/undo/detect/capture
    #     ('' = apostrof), miejsce src/DSOptimizer.Catalog/catalog/<plik>#<id>.script.<pole>, oraz value wpisów registry[]
    #     z "localized": true (tekst zapisywany w języku programu), miejsce <plik>#<id>.registry.value. Zły JSON katalogu = kod 2.
    #     Dokumentacja: każdy polski docs\tweaks\*.md, docs\apps\*.md i docs\prep\*.md (bez podkatalogów) ma mieć angielski
    #     docs\en\<to samo>, którego pierwsza linia to odcisk bieżącego polskiego pliku
    #     (<!-- source: docs/<x>/<plik>.md sha256: <hash> -->, wpisuje go Set-DocsTranslationStamp.ps1); brak pliku = „brak”,
    #     zły albo stary odcisk = „nieaktualne”. Pliki docs\en\**\*.md bez takiego polskiego źródła są nieużywane.
    #     Brak katalogu docs = brak dokumentów do sprawdzenia.
    #     Wypisuje sekcje „Brak tłumaczenia (N)” (plik:linia i tekst, każdy tekst raz, z pierwszym miejscem),
    #     „Dokumentacja bez aktualnego tłumaczenia (D)” (polska ścieżka i „brak” albo „nieaktualne”) oraz
    #     „Nieużywane tłumaczenia (M)” (wpisy "pl", których nie ma w źródłach, potem angielskie dokumenty bez źródła),
    #     a na końcu „Braki: N + D, nieużywane: M”.
    #     Kod wyjścia: 0 zawsze; z -FailOnMissing 1 przy N + D > 0; 2 przy nieprawidłowym pliku tłumaczeń.

.PARAMETER Root
    Korzeń repo (domyślnie dwa katalogi nad skryptem).

.PARAMETER FailOnMissing
    Kod wyjścia 1, gdy są teksty albo dokumenty bez aktualnego tłumaczenia.
#>
[CmdletBinding()]
param(
    [string]$Root,
    [switch]$FailOnMissing
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertFrom-CSharpLiteral {
    param([string]$Text)
    return [regex]::Replace($Text, '\\(u[0-9A-Fa-f]{4}|.)', {
            param($Match)
            $Code = $Match.Groups[1].Value
            if ($Code.Length -eq 5) { return [string][char][Convert]::ToInt32($Code.Substring(1), 16) }
            switch ($Code) {
                'n' { return "`n" }
                'r' { return "`r" }
                't' { return "`t" }
                'a' { return [string][char]7 }
                'b' { return [string][char]8 }
                'f' { return [string][char]12 }
                'v' { return [string][char]11 }
                '0' { return [string][char]0 }
                default { return $Code }
            }
        })
}

function ConvertFrom-XamlLiteral {
    param([string]$Text)
    $Decoded = [regex]::Replace($Text, '&(#x[0-9A-Fa-f]+|#[0-9]+|amp|lt|gt|quot|apos);', {
            param($Match)
            $Code = $Match.Groups[1].Value
            if ($Code.StartsWith('#x')) { return [char]::ConvertFromUtf32([Convert]::ToInt32($Code.Substring(2), 16)) }
            if ($Code.StartsWith('#')) { return [char]::ConvertFromUtf32([int]$Code.Substring(1)) }
            switch ($Code) {
                'amp' { return '&' }
                'lt' { return '<' }
                'gt' { return '>' }
                'quot' { return '"' }
                default { return "'" }
            }
        })
    return [regex]::Replace($Decoded, '\\([\\''])', '$1')
}

function Hide-Comments {
    param([string]$Text, [bool]$IsXaml)
    $Blank = {
        param($Match)
        return [regex]::Replace($Match.Value, '[^\r\n]', ' ')
    }
    if ($IsXaml) {
        return [regex]::Replace($Text, '<!--[\s\S]*?-->', $Blank)
    }
    $Tokens = '@"(?:[^"]|"")*"|"(?:[^"\\\r\n]|\\.)*"|''(?:[^''\\\r\n]|\\.)+''|//[^\r\n]*|/\*[\s\S]*?\*/'
    return [regex]::Replace($Text, $Tokens, {
            param($Match)
            if ($Match.Value.StartsWith('//') -or $Match.Value.StartsWith('/*')) {
                return [regex]::Replace($Match.Value, '[^\r\n]', ' ')
            }
            return $Match.Value
        })
}

function Get-SourceTexts {
    param([string]$Path, [string]$Relative)
    $Content = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    $IsXaml = $Path.EndsWith('.xaml', [System.StringComparison]::OrdinalIgnoreCase)
    $Content = Hide-Comments -Text $Content -IsXaml $IsXaml
    if ($IsXaml) {
        $Pattern = '\{l:T ''((?:[^''\\]|\\.)*)''\}'
    }
    else {
        # L.T("…") i L.F("…", …) – jeden klucz; L.P("…", "…", "…", n) – trzy klucze (formy one, few, many).
        $Literal = '"((?:[^"\\\r\n]|\\.)*)"'
        $Pattern = '(?<![\w.])L\.(?:[TF]\(' + $Literal + '|P\(' + $Literal + '\s*,\s*' + $Literal + '\s*,\s*' + $Literal + ')'
    }
    foreach ($Match in [regex]::Matches($Content, $Pattern)) {
        $Line = 1
        for ($Index = 0; $Index -lt $Match.Index; $Index++) {
            if ($Content[$Index] -eq "`n") { $Line++ }
        }
        for ($Group = 1; $Group -lt $Match.Groups.Count; $Group++) {
            if (-not $Match.Groups[$Group].Success) { continue }
            $Value = $Match.Groups[$Group].Value
            if (-not $IsXaml -and -not [regex]::IsMatch($Value, '^(?:[^\\]|\\[^xU])*$')) {
                throw ('Nieobsługiwana sekwencja ucieczki (\x lub \U) w {0}:{1}' -f $Relative, $Line)
            }
            if ($IsXaml) { $Decoded = ConvertFrom-XamlLiteral -Text $Value }
            else { $Decoded = ConvertFrom-CSharpLiteral -Text $Value }
            [pscustomobject]@{
                Text  = [string]$Decoded
                Where = ('{0}:{1}' -f $Relative, $Line)
            }
        }
    }
}

function Get-OptionalText {
    param($Object, [string]$Name)
    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$Name]) { return $null }
    $Value = $Object.$Name
    if ($Value -is [string] -and $Value.Length -gt 0) { return $Value }
    return $null
}

function Get-Items {
    param($Object, [string]$Name)
    if ($null -eq $Object -or $Object -is [array] -or $null -eq $Object.PSObject.Properties[$Name]) { return @() }
    return @($Object.$Name)
}

function Get-CatalogTexts {
    param([string]$Path, [string]$Relative)
    # Teksty dla człowieka z plików katalogu (tłumaczone przez L.Data): pozycje tweaków – title, summary, benefit, description, warnings
    # i value wpisów registry[] z "localized": true;
    # apps.json – summary, warning (title to nazwa produktu); apps-bloat.json – entries[].summary/warning, protected[].reason.
    try {
        $Json = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    }
    catch {
        throw ('Nieprawidłowy JSON pliku katalogu {0}: {1}' -f $Relative, $_.Exception.Message)
    }
    $Name = [System.IO.Path]::GetFileName($Path)
    $Fields = New-Object -TypeName 'System.Collections.Generic.List[object]'
    if ($Name -ceq 'apps.json') {
        foreach ($Entry in @(Get-Items -Object $Json -Name 'entries')) { $Fields.Add(@($Entry, @('summary', 'warning'))) }
    }
    elseif ($Name -ceq 'apps-bloat.json') {
        foreach ($Entry in @(Get-Items -Object $Json -Name 'entries')) { $Fields.Add(@($Entry, @('summary', 'warning'))) }
        foreach ($Entry in @(Get-Items -Object $Json -Name 'protected')) { $Fields.Add(@($Entry, @('reason'))) }
    }
    else {
        foreach ($Entry in @($Json)) { $Fields.Add(@($Entry, @('title', 'summary', 'benefit', 'description', 'warnings'))) }
    }
    foreach ($Item in $Fields) {
        $Entry = $Item[0]
        $Id = Get-OptionalText -Object $Entry -Name 'id'
        foreach ($Field in $Item[1]) {
            if ($null -eq $Entry -or $null -eq $Entry.PSObject.Properties[$Field]) { continue }
            foreach ($Value in @($Entry.$Field)) {
                if (-not ($Value -is [string]) -or $Value.Length -eq 0) { continue }
                [pscustomobject]@{
                    Text  = [string]$Value
                    Where = ('{0}#{1}.{2}' -f $Relative, $Id, $Field)
                }
            }
        }
        if ($Name -ceq 'apps.json' -or $Name -ceq 'apps-bloat.json') { continue }
        # Wpisy rejestru z "localized": true – tekst wartości zapisywany w języku programu (L.Data w RegistryValueOperation).
        foreach ($Registry in @(Get-Items -Object $Entry -Name 'registry')) {
            if ($null -eq $Registry -or $Registry -is [string] -or $Registry -is [array]) { continue }
            if ($null -eq $Registry.PSObject.Properties['localized'] -or $Registry.localized -isnot [bool] -or -not $Registry.localized) { continue }
            $Value = Get-OptionalText -Object $Registry -Name 'value'
            if ($null -eq $Value) { continue }
            [pscustomobject]@{
                Text  = $Value
                Where = ('{0}#{1}.registry.value' -f $Relative, $Id)
            }
        }
        # Komunikaty skryptów pozycji tweaków: pierwszy argument Get-DsoText '…' (ten sam wzorzec co ScriptTexts.Find w Core).
        $Script = $null
        if ($null -ne $Entry -and -not ($Entry -is [array]) -and $null -ne $Entry.PSObject.Properties['script']) { $Script = $Entry.script }
        if ($null -eq $Script -or $Script -is [string] -or $Script -is [array]) { continue }
        foreach ($Field in 'apply', 'undo', 'detect', 'capture') {
            if ($null -eq $Script.PSObject.Properties[$Field]) { continue }
            $Body = (@($Script.$Field) | Where-Object { $_ -is [string] }) -join "`n"
            foreach ($Match in [regex]::Matches($Body, "Get-DsoText\s+'((?:[^']|'')*)'")) {
                [pscustomobject]@{
                    Text  = $Match.Groups[1].Value.Replace("''", "'")
                    Where = ('{0}#{1}.script.{2}' -f $Relative, $Id, $Field)
                }
            }
        }
    }
}

function Get-DocsSourceHash {
    # Ten sam hash co Get-DocsSourceHash w tools\i18n\Set-DocsTranslationStamp.ps1 – zmiana tu wymaga zmiany tam.
    param([string]$FullPath)
    $Text = [System.IO.File]::ReadAllText($FullPath, [System.Text.Encoding]::UTF8)
    $Bytes = (New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false).GetBytes($Text.Replace("`r`n", "`n"))
    $Sha = [System.Security.Cryptography.SHA256]::Create()
    try { $Hash = $Sha.ComputeHash($Bytes) } finally { $Sha.Dispose() }
    return (($Hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-DocsStatus {
    # Polskie docs/(tweaks|apps|prep)/*.md (bez podkatalogów) bez aktualnego docs/en/<to samo> oraz angielskie sieroty.
    param([string]$RootPath)
    $Stale = New-Object -TypeName 'System.Collections.Generic.List[object]'
    $Orphans = New-Object -TypeName 'System.Collections.Generic.List[string]'
    $DocsPath = Join-Path -Path $RootPath -ChildPath 'docs'
    if (-not (Test-Path -LiteralPath $DocsPath -PathType Container)) {
        return [pscustomobject]@{ Stale = @(); Orphans = @() }
    }
    $Sources = New-Object -TypeName 'System.Collections.Generic.HashSet[string]' -ArgumentList ([System.StringComparer]::Ordinal)
    foreach ($Folder in 'tweaks', 'apps', 'prep') {
        $FolderPath = Join-Path -Path $DocsPath -ChildPath $Folder
        if (-not (Test-Path -LiteralPath $FolderPath -PathType Container)) { continue }
        foreach ($File in Get-ChildItem -LiteralPath $FolderPath -Filter '*.md' -File) {
            if ($File.Extension -ceq '.md') { $null = $Sources.Add(('docs/{0}/{1}' -f $Folder, $File.Name)) }
        }
    }
    $SortedSources = [string[]]@($Sources)
    [System.Array]::Sort($SortedSources, [System.StringComparer]::Ordinal)
    foreach ($Source in $SortedSources) {
        $English = Join-Path -Path $RootPath -ChildPath ('docs/en/' + $Source.Substring(5)).Replace('/', '\')
        if (-not (Test-Path -LiteralPath $English -PathType Leaf)) {
            $Stale.Add([pscustomobject]@{ Where = $Source; State = 'brak' })
            continue
        }
        $FirstLine = ([System.IO.File]::ReadAllText($English, [System.Text.Encoding]::UTF8) -split "`n", 2)[0].TrimEnd("`r")
        $Expected = '<!-- source: {0} sha256: {1} -->' -f $Source, (Get-DocsSourceHash -FullPath (Join-Path -Path $RootPath -ChildPath $Source.Replace('/', '\')))
        if (-not ($FirstLine -ceq $Expected)) {
            $Stale.Add([pscustomobject]@{ Where = $Source; State = 'nieaktualne' })
        }
    }
    $EnglishPath = Join-Path -Path $DocsPath -ChildPath 'en'
    if (Test-Path -LiteralPath $EnglishPath -PathType Container) {
        foreach ($File in Get-ChildItem -LiteralPath $EnglishPath -Filter '*.md' -File -Recurse) {
            if (-not ($File.Extension -ceq '.md')) { continue }
            $Relative = $File.FullName.Substring($RootPath.Length).TrimStart('\', '/').Replace('\', '/')
            if (-not $Sources.Contains('docs/' + $Relative.Substring(8))) { $Orphans.Add($Relative) }
        }
    }
    $SortedOrphans = $Orphans.ToArray()
    [System.Array]::Sort($SortedOrphans, [System.StringComparer]::Ordinal)
    # ToArray: @() na pustej List[object] w PowerShell 5.1 rzuca wyjątek „Niezgodne typy argumentów”.
    return [pscustomobject]@{ Stale = $Stale.ToArray(); Orphans = $SortedOrphans }
}

function Format-Shown {
    param([string]$Text)
    return $Text.Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
}

try {
    if ([string]::IsNullOrEmpty($Root)) {
        $ScriptDirectory = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
        $Root = Join-Path -Path $ScriptDirectory -ChildPath '..\..'
    }
    $RootPath = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    $SrcPath = Join-Path -Path $RootPath -ChildPath 'src'
    $Translations = New-Object -TypeName 'System.Collections.Generic.Dictionary[string,string]' -ArgumentList ([System.StringComparer]::Ordinal)

    $TranslationDirectory = Join-Path -Path $SrcPath -ChildPath 'DSOptimizer.Core\Localization\Translations'
    $TranslationFiles = @()
    if (Test-Path -LiteralPath $TranslationDirectory) {
        $TranslationFiles = @(Get-ChildItem -LiteralPath $TranslationDirectory -Filter '*.json' -File)
    }
    foreach ($File in $TranslationFiles) {
        try {
            $Json = [System.IO.File]::ReadAllText($File.FullName, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
            if ($null -eq $Json -or $null -eq $Json.PSObject.Properties['entries']) { throw 'brak pola "entries"' }
            foreach ($Entry in @($Json.entries)) {
                if ($null -eq $Entry.PSObject.Properties['pl'] -or [string]::IsNullOrEmpty([string]$Entry.pl)) { throw 'wpis bez pola "pl"' }
                $English = ''
                if ($null -ne $Entry.PSObject.Properties['en']) { $English = [string]$Entry.en }
                $Pl = [string]$Entry.pl
                if (-not $Translations.ContainsKey($Pl) -or $English.Length -gt 0) { $Translations[$Pl] = $English }
            }
        }
        catch {
            Write-Output ('Błąd pliku tłumaczeń {0}: {1}' -f $File.Name, $_.Exception.Message)
            exit 2
        }
    }

    $Paths = New-Object -TypeName 'System.Collections.Generic.List[string]'
    $Full = New-Object -TypeName 'System.Collections.Generic.List[string]'
    if (Test-Path -LiteralPath $SrcPath) {
        foreach ($Filter in '*.cs', '*.xaml') {
            foreach ($File in Get-ChildItem -LiteralPath $SrcPath -Recurse -File -Filter $Filter) {
                if (-not ($File.Extension -ceq '.cs' -or $File.Extension -ceq '.xaml')) { continue }
                $Relative = $File.FullName.Substring($RootPath.Length).TrimStart('\', '/').Replace('\', '/')
                $Segments = $Relative.Split('/')
                if (($Segments -contains 'obj') -or ($Segments -contains 'bin')) { continue }
                # L.cs to definicja; TExtension.cs dostaje tekst z XAML {l:T '...'}, który raport czyta osobno.
                if ($Relative.EndsWith('Localization/L.cs', [System.StringComparison]::Ordinal)) { continue }
                if ($Relative.EndsWith('App/Gui/TExtension.cs', [System.StringComparison]::Ordinal)) { continue }
                $Paths.Add($Relative)
                $Full.Add($File.FullName)
            }
        }
    }
    if (@($Paths | Where-Object { $_.EndsWith('.cs', [System.StringComparison]::Ordinal) }).Count -eq 0) {
        throw ('Nie znaleziono żadnego pliku .cs w {0} (zły -Root?).' -f $SrcPath)
    }
    $PathKeys = $Paths.ToArray()
    $FullItems = $Full.ToArray()
    [System.Array]::Sort($PathKeys, $FullItems, [System.StringComparer]::Ordinal)

    $Used = New-Object -TypeName 'System.Collections.Generic.HashSet[string]' -ArgumentList ([System.StringComparer]::Ordinal)
    $Missing = New-Object -TypeName 'System.Collections.Generic.List[object]'
    for ($Index = 0; $Index -lt $PathKeys.Length; $Index++) {
        foreach ($Item in @(Get-SourceTexts -Path $FullItems[$Index] -Relative $PathKeys[$Index])) {
            if (-not $Used.Add($Item.Text)) { continue }
            if (-not ($Translations.ContainsKey($Item.Text) -and $Translations[$Item.Text].Length -gt 0)) {
                $Missing.Add($Item)
            }
        }
    }

    $CatalogPath = Join-Path -Path $SrcPath -ChildPath 'DSOptimizer.Catalog\catalog'
    if (Test-Path -LiteralPath $CatalogPath) {
        $CatalogFiles = [string[]]@(Get-ChildItem -LiteralPath $CatalogPath -Filter '*.json' -File | Where-Object { $_.Extension -ceq '.json' } | ForEach-Object { $_.FullName })
        [System.Array]::Sort($CatalogFiles, [System.StringComparer]::Ordinal)
        foreach ($CatalogFile in $CatalogFiles) {
            $Relative = $CatalogFile.Substring($RootPath.Length).TrimStart('\', '/').Replace('\', '/')
            foreach ($Item in @(Get-CatalogTexts -Path $CatalogFile -Relative $Relative)) {
                if (-not $Used.Add($Item.Text)) { continue }
                if (-not ($Translations.ContainsKey($Item.Text) -and $Translations[$Item.Text].Length -gt 0)) {
                    $Missing.Add($Item)
                }
            }
        }
    }

    $Unused = [string[]]@(@($Translations.Keys) | Where-Object { -not $Used.Contains($_) })
    [System.Array]::Sort($Unused, [System.StringComparer]::Ordinal)

    $Docs = Get-DocsStatus -RootPath $RootPath
    $StaleDocs = @($Docs.Stale)
    $OrphanDocs = @($Docs.Orphans)
    $MissingTotal = $Missing.Count + $StaleDocs.Count
    $UnusedTotal = $Unused.Count + $OrphanDocs.Count

    Write-Output ('Brak tłumaczenia ({0})' -f $Missing.Count)
    foreach ($Item in $Missing) {
        Write-Output ('  {0}  {1}' -f $Item.Where, (Format-Shown -Text $Item.Text))
    }
    Write-Output ('Dokumentacja bez aktualnego tłumaczenia ({0})' -f $StaleDocs.Count)
    foreach ($Item in $StaleDocs) {
        Write-Output ('  {0}  {1}' -f $Item.Where, $Item.State)
    }
    Write-Output ('Nieużywane tłumaczenia ({0})' -f $UnusedTotal)
    foreach ($Text in $Unused) {
        Write-Output ('  {0}' -f (Format-Shown -Text $Text))
    }
    foreach ($Orphan in $OrphanDocs) {
        Write-Output ('  {0}' -f $Orphan)
    }
    Write-Output ('Braki: {0}, nieużywane: {1}' -f $MissingTotal, $UnusedTotal)

    if ($FailOnMissing -and $MissingTotal -gt 0) { exit 1 }
    exit 0
}
catch {
    Write-Output ('Błąd: {0}' -f $_.Exception.Message)
    exit 2
}
