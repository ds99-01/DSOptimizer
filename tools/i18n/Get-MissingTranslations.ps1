#Requires -Version 5.1
<#
.SYNOPSIS
    Raport braków tłumaczeń: polskie teksty z L.T/L.F i {l:T '…'} bez angielskiego odpowiednika.

.DESCRIPTION
    # Instrukcja uruchomienia (Windows PowerShell 5.1, z katalogu repo):
    #     .\tools\i18n\Get-MissingTranslations.ps1                    # raport, kod 0
    #     .\tools\i18n\Get-MissingTranslations.ps1 -FailOnMissing     # kod 1, gdy są braki
    #     .\tools\i18n\Get-MissingTranslations.ps1 -Root <repo>       # inny korzeń repo
    # Opis działania:
    #     Przeszukuje src\**\*.cs (wywołania L.T("…") i L.F("…", …)) oraz src\**\*.xaml ({l:T '…'}), pomija foldery obj i bin
    #     oraz src\DSOptimizer.Core\Localization\L.cs. Zasady literałów pilnuje test TranslationCallRulesTests, więc wystarczą
    #     proste wyrażenia regularne. Teksty są rozkodowywane (C#: \\ \" \' \0 \n \r \t \uXXXX; XAML: encje XML,
    #     potem \' i \\). Tłumaczenia czyta z src\DSOptimizer.Core\Localization\Translations\*.json
    #     ({"language":"en","entries":[{"pl":"…","en":"…"}]}); tekst jest przetłumaczony, gdy ma wpis z niepustym "en".
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

function Get-SourceTexts {
    param([string]$Path, [string]$Relative)
    $Content = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    $IsXaml = $Path.EndsWith('.xaml', [System.StringComparison]::OrdinalIgnoreCase)
    if ($IsXaml) {
        $Pattern = '\{l:T ''((?:[^''\\]|\\.)*)''\}'
    }
    else {
        $Pattern = '(?<![\w.])L\.[TF]\("((?:[^"\\\r\n]|\\.)*)"'
    }
    foreach ($Match in [regex]::Matches($Content, $Pattern)) {
        $Line = 1
        for ($Index = 0; $Index -lt $Match.Index; $Index++) {
            if ($Content[$Index] -eq "`n") { $Line++ }
        }
        if ($IsXaml) { $Decoded = ConvertFrom-XamlLiteral -Text $Match.Groups[1].Value }
        else { $Decoded = ConvertFrom-CSharpLiteral -Text $Match.Groups[1].Value }
        [pscustomobject]@{
            Text  = [string]$Decoded
            Where = ('{0}:{1}' -f $Relative, $Line)
        }
    }
}

function Format-Shown {
    param([string]$Text)
    return $Text.Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
}

if ([string]::IsNullOrEmpty($Root)) {
    $ScriptDirectory = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
    $Root = Join-Path -Path $ScriptDirectory -ChildPath '..\..'
}
$RootPath = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
$SrcPath = Join-Path -Path $RootPath -ChildPath 'src'
$Translations = @{}

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
            if ($Relative.EndsWith('Localization/L.cs', [System.StringComparison]::Ordinal)) { continue }
            $Paths.Add($Relative)
            $Full.Add($File.FullName)
        }
    }
}
$PathKeys = $Paths.ToArray()
$FullItems = $Full.ToArray()
[System.Array]::Sort($PathKeys, $FullItems, [System.StringComparer]::Ordinal)

$Used = @{}
$Missing = New-Object -TypeName 'System.Collections.Generic.List[object]'
for ($Index = 0; $Index -lt $PathKeys.Length; $Index++) {
    foreach ($Item in @(Get-SourceTexts -Path $FullItems[$Index] -Relative $PathKeys[$Index])) {
        if ($Used.ContainsKey($Item.Text)) { continue }
        $Used[$Item.Text] = $true
        if (-not ($Translations.ContainsKey($Item.Text) -and $Translations[$Item.Text].Length -gt 0)) {
            $Missing.Add($Item)
        }
    }
}

$Unused = [string[]]@($Translations.Keys | Where-Object { -not $Used.ContainsKey($_) })
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
