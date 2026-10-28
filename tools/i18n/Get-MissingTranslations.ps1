#Requires -Version 5.1
<#
.SYNOPSIS
    Raport braków tłumaczeń: polskie teksty z L.T/L.F/L.P i {l:T '…'} bez angielskiego odpowiednika.

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
    #     src/DSOptimizer.Catalog/catalog/<plik>#<id>.<pole>; teksty danych liczą się jako używane. Zły JSON katalogu = kod 2.
    #     Wypisuje sekcje „Brak tłumaczenia (N)” (plik:linia i tekst, każdy tekst raz, z pierwszym miejscem) oraz
    #     „Nieużywane tłumaczenia (M)” (wpisy "pl", których nie ma w źródłach), a na końcu „Braki: N, nieużywane: M”.
    #     Kod wyjścia: 0 zawsze; z -FailOnMissing 1 przy N > 0; 2 przy nieprawidłowym pliku tłumaczeń.

.PARAMETER Root
    Korzeń repo (domyślnie dwa katalogi nad skryptem).

.PARAMETER FailOnMissing
    Kod wyjścia 1, gdy są teksty bez tłumaczenia.
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
    # Teksty dla człowieka z plików katalogu (tłumaczone przez L.Data): pozycje tweaków – title, summary, benefit, description, warnings;
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
    }
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

    Write-Output ('Brak tłumaczenia ({0})' -f $Missing.Count)
    foreach ($Item in $Missing) {
        Write-Output ('  {0}  {1}' -f $Item.Where, (Format-Shown -Text $Item.Text))
    }
    Write-Output ('Nieużywane tłumaczenia ({0})' -f $Unused.Count)
    foreach ($Text in $Unused) {
        Write-Output ('  {0}' -f (Format-Shown -Text $Text))
    }
    Write-Output ('Braki: {0}, nieużywane: {1}' -f $Missing.Count, $Unused.Count)

    if ($FailOnMissing -and $Missing.Count -gt 0) { exit 1 }
    exit 0
}
catch {
    Write-Output ('Błąd: {0}' -f $_.Exception.Message)
    exit 2
}
