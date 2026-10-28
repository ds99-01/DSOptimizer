#Requires -Version 5.1
<#
.SYNOPSIS
    Porównanie polskich tekstów źródeł: które zniknęły względem wskazanej wersji (gita), a które są nowe.

.DESCRIPTION
    # Instrukcja uruchomienia (Windows PowerShell 5.1, z katalogu repo):
    #     .\tools\i18n\Compare-PolishTexts.ps1                       # porównanie z gałęzią main
    #     .\tools\i18n\Compare-PolishTexts.ps1 -Base <commit|gałąź>  # porównanie z inną wersją
    #     .\tools\i18n\Compare-PolishTexts.ps1 -Root <repo>          # inny korzeń repo
    # Opis działania:
    #     Zbiera teksty z src\**\*.cs i src\**\*.xaml (bez folderów obj i bin) w wersji -Base (lista plików przez
    #     git ls-tree, treść przez git show) i w drzewie roboczym. C#: literały zwykłe, dosłowne (@"…"), interpolowane
    #     ($"…" – dziura interpolacji to {}, a literały w dziurach liczą się osobno) i surowe ("""…"""), bez komentarzy
    #     i literałów znakowych; sekwencje ucieczki są rozkodowywane. XAML: wartości atrybutów (rozszerzenie {…} pomijane,
    #     z wyjątkiem tekstu w {l:T '…'}; prefiks {} to zwykły tekst) i treść elementów, po rozkodowaniu encji XML.
    #     Symbole zastępcze {0}, {1:N1}, {x} są sprowadzane do {}, więc $"Plik {name}" i L.F("Plik {0}", name) to ten
    #     sam tekst. Liczą się tylko teksty polskie: z polską literą albo słowem z listy (jak w teście PolishLiteralRulesTests).
    #     Porównanie jest zbiorcze po całym src (teksty przenoszą się między plikami): „Zniknęły (N)” – teksty z -Base,
    #     których nie ma nigdzie w drzewie, z miejscem w -Base; „Nowe (M)” – teksty drzewa, których nie było w -Base,
    #     z miejscem w drzewie; na końcu „Zniknęły: N, nowe: M”. Rozbicie sklejki na jedno L.F daje „zniknięte”
    #     fragmenty – recenzent sprawdza je ręcznie. Kod wyjścia: 0 zawsze; 2 przy błędzie gita albo odczytu.

.PARAMETER Base
    Wersja do porównania (gałąź, tag albo commit); domyślnie main.

.PARAMETER Root
    Korzeń repo (domyślnie dwa katalogi nad skryptem).
#>
[CmdletBinding()]
param(
    [string]$Base = 'main',
    [string]$Root
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PolishWords = 'nie', 'Nie', 'jest', 'oraz', 'lub', 'albo', 'przez', 'dla', 'jako', 'Tak', 'Brak', 'Zastosuj', 'Uruchom', 'Cofnij',
'Zamknij', 'Anuluj', 'Szukaj', 'Wszystkie', 'Pozycje', 'Dalej', 'Odczyt', 'Zaznaczone'
$PolishPattern = '[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]|\b(?:' + ($PolishWords -join '|') + ')\b'

$CSharpString = '"(?:[^"\\\r\n]|\\.)*"'
$CSharpChar = '''(?:[^''\\\r\n]|\\.)+'''
$HoleContent = '(?:[^{}"''\r\n]|' + $CSharpString + '|' + $CSharpChar + ')*'
$CSharpTokens = [regex]('//[^\r\n]*|/\*[\s\S]*?\*/' +
    '|(?<raw>\$*(?<q>"{3,})[\s\S]*?\k<q>)' +
    '|(?<vi>(?:\$@|@\$)"(?:[^"]|"")*")' +
    '|(?<v>@"(?:[^"]|"")*")' +
    '|(?<i>\$"(?:[^"\\\r\n{]|\\.|\{\{|\{' + $HoleContent + '\})*")' +
    '|(?<s>' + $CSharpString + ')' +
    '|' + $CSharpChar)
$InterpolationHole = [regex]('\{\{|\}\}|\{(' + $HoleContent + ')\}')
$TranslateExtension = [regex]'\{l:T ''((?:[^''\\]|\\.)*)''\}'

function ConvertFrom-CSharpEscapes {
    param([string]$Text)
    return [regex]::Replace($Text, '\\(u[0-9A-Fa-f]{4}|.)', {
            param($Match)
            $Code = $Match.Groups[1].Value
            if ($Code.Length -eq 5) { return [string][char][Convert]::ToInt32($Code.Substring(1), 16) }
            switch -CaseSensitive ($Code) {
                'n' { return "`n" }
                'r' { return "`r" }
                't' { return "`t" }
                '0' { return [string][char]0 }
                default { return $Code }
            }
        })
}

function ConvertTo-Normalized {
    param([string]$Text)
    return [regex]::Replace($Text, '\{[^{}]*\}', '{}')
}

function New-LineIndex {
    param([string]$Content)
    $Starts = New-Object -TypeName 'System.Collections.Generic.List[int]'
    $Starts.Add(0)
    for ($Index = $Content.IndexOf("`n"); $Index -ge 0; $Index = $Content.IndexOf("`n", $Index + 1)) { $Starts.Add($Index + 1) }
    return , $Starts.ToArray()
}

function Get-LineNumber {
    param([int[]]$Starts, [int]$Position)
    $Found = [System.Array]::BinarySearch($Starts, $Position)
    if ($Found -lt 0) { $Found = (-bnot $Found) - 1 }
    return $Found + 1
}

function Get-CSharpTexts {
    param([string]$Content)
    $Starts = New-LineIndex -Content $Content
    foreach ($Match in $CSharpTokens.Matches($Content)) {
        $Line = Get-LineNumber -Starts $Starts -Position $Match.Index
        $Value = $Match.Value
        if ($Match.Groups['raw'].Success) {
            $Dollars = $Value.Length - $Value.TrimStart('$').Length
            $Quotes = $Match.Groups['q'].Value.Length
            $Inner = $Value.Substring($Dollars + $Quotes, $Value.Length - $Dollars - 2 * $Quotes)
            if ($Dollars -gt 0) { $Inner = [regex]::Replace($Inner, '\{+[^{}]*\}+', '{}') }
            [pscustomobject]@{ Text = $Inner; Line = $Line }
        }
        elseif ($Match.Groups['vi'].Success -or $Match.Groups['v'].Success) {
            $Prefix = if ($Match.Groups['vi'].Success) { 3 } else { 2 }
            $Inner = $Value.Substring($Prefix, $Value.Length - $Prefix - 1).Replace('""', '"')
            if ($Match.Groups['vi'].Success) { $Inner = [regex]::Replace($Inner, '\{\{|\}\}|\{[^{}]*\}', { param($Hole) if ($Hole.Value.Length -eq 2 -and $Hole.Value[0] -eq $Hole.Value[1]) { $Hole.Value } else { '{}' } }) }
            [pscustomobject]@{ Text = $Inner; Line = $Line }
        }
        elseif ($Match.Groups['i'].Success) {
            $Inner = $Value.Substring(2, $Value.Length - 3)
            foreach ($Hole in $InterpolationHole.Matches($Inner)) {
                if (-not $Hole.Groups[1].Success) { continue }
                foreach ($Nested in [regex]::Matches($Hole.Groups[1].Value, $CSharpString)) {
                    [pscustomobject]@{ Text = (ConvertFrom-CSharpEscapes -Text $Nested.Value.Substring(1, $Nested.Value.Length - 2)); Line = $Line }
                }
            }
            $Inner = $InterpolationHole.Replace($Inner, { param($Hole) if ($Hole.Groups[1].Success) { '{}' } else { $Hole.Value } })
            [pscustomobject]@{ Text = (ConvertFrom-CSharpEscapes -Text $Inner); Line = $Line }
        }
        elseif ($Match.Groups['s'].Success) {
            [pscustomobject]@{ Text = (ConvertFrom-CSharpEscapes -Text $Value.Substring(1, $Value.Length - 2)); Line = $Line }
        }
    }
}

function Get-XamlTexts {
    param([string]$Content)
    $Settings = New-Object -TypeName System.Xml.XmlReaderSettings
    $Settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $Settings.XmlResolver = $null
    $Reader = [System.Xml.XmlReader]::Create((New-Object -TypeName System.IO.StringReader -ArgumentList $Content.TrimStart([char]0xFEFF)), $Settings)
    try {
        $Info = [System.Xml.IXmlLineInfo]$Reader
        while ($Reader.Read()) {
            if ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $Reader.MoveToFirstAttribute()) {
                do {
                    $Value = $Reader.Value
                    if ($Value.StartsWith('{}')) {
                        [pscustomobject]@{ Text = $Value.Substring(2); Line = $Info.LineNumber }
                    }
                    elseif ($Value.StartsWith('{')) {
                        foreach ($Translated in $TranslateExtension.Matches($Value)) {
                            [pscustomobject]@{ Text = [regex]::Replace($Translated.Groups[1].Value, '\\([\\''])', '$1'); Line = $Info.LineNumber }
                        }
                    }
                    else {
                        [pscustomobject]@{ Text = $Value; Line = $Info.LineNumber }
                    }
                } while ($Reader.MoveToNextAttribute())
                $null = $Reader.MoveToElement()
            }
            elseif ($Reader.NodeType -eq [System.Xml.XmlNodeType]::Text -or $Reader.NodeType -eq [System.Xml.XmlNodeType]::CDATA) {
                [pscustomobject]@{ Text = $Reader.Value.Trim(); Line = $Info.LineNumber }
            }
        }
    }
    finally {
        $Reader.Dispose()
    }
}

# Teksty polskie jednej wersji: słownik tekst → pierwsze miejsce (plik:linia), pliki w kolejności porządkowej ścieżek.
function Get-PolishTextMap {
    param([string[]]$Paths, [scriptblock]$ReadContent)
    $Map = New-Object -TypeName 'System.Collections.Generic.Dictionary[string,string]' -ArgumentList ([System.StringComparer]::Ordinal)
    $Sorted = [string[]]$Paths.Clone()
    [System.Array]::Sort($Sorted, [System.StringComparer]::Ordinal)
    foreach ($Path in $Sorted) {
        # Blob w gicie ma LF, drzewo robocze CRLF (.gitattributes) – wielowierszowy literał ma być tym samym tekstem.
        $Content = ([string](& $ReadContent $Path)).Replace("`r`n", "`n")
        try {
            if ($Path.EndsWith('.xaml', [System.StringComparison]::OrdinalIgnoreCase)) { $Items = @(Get-XamlTexts -Content $Content) }
            else { $Items = @(Get-CSharpTexts -Content $Content) }
        }
        catch {
            throw ('{0}: {1}' -f $Path, $_.Exception.Message)
        }
        foreach ($Item in $Items) {
            $Text = ConvertTo-Normalized -Text $Item.Text
            if (-not ($Text -cmatch $PolishPattern)) { continue }
            if (-not $Map.ContainsKey($Text)) { $Map[$Text] = ('{0}:{1}' -f $Path, $Item.Line) }
        }
    }
    return , $Map
}

function Test-SourcePath {
    param([string]$Path)
    if (-not ($Path.EndsWith('.cs', [System.StringComparison]::Ordinal) -or $Path.EndsWith('.xaml', [System.StringComparison]::Ordinal))) { return $false }
    $Segments = $Path.Split('/')
    return -not (($Segments -contains 'obj') -or ($Segments -contains 'bin'))
}

function ConvertTo-ProcessArgument {
    param([string]$Value)
    $Escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $Escaped = [regex]::Replace($Escaped, '(\\+)$', '$1$1')
    return '"' + $Escaped + '"'
}

function Invoke-Git {
    param([string[]]$Arguments)
    $Info = New-Object -TypeName System.Diagnostics.ProcessStartInfo
    $Info.FileName = 'git'
    $Info.Arguments = ((@('-C', $RootPath) + $Arguments) | ForEach-Object { ConvertTo-ProcessArgument -Value $_ }) -join ' '
    $Info.UseShellExecute = $false
    $Info.CreateNoWindow = $true
    $Info.RedirectStandardOutput = $true
    $Info.RedirectStandardError = $true
    $Info.StandardOutputEncoding = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
    $Info.StandardErrorEncoding = $Info.StandardOutputEncoding
    $Process = [System.Diagnostics.Process]::Start($Info)
    try {
        $ErrorText = $Process.StandardError.ReadToEndAsync()
        $Output = $Process.StandardOutput.ReadToEnd()
        $Process.WaitForExit()
        if ($Process.ExitCode -ne 0) {
            throw ('git {0} – kod {1}: {2}' -f ($Arguments -join ' '), $Process.ExitCode, $ErrorText.Result.Trim())
        }
        return $Output
    }
    finally {
        $Process.Dispose()
    }
}

function Format-Shown {
    param([string]$Text)
    return $Text.Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
}

# Teksty z $From, których nie ma w $Other, posortowane po ścieżce (porządkowo) i linii.
function Get-Difference {
    param([System.Collections.Generic.Dictionary[string, string]]$From, [System.Collections.Generic.Dictionary[string, string]]$Other)
    $Rows = New-Object -TypeName 'System.Collections.Generic.List[object]'
    foreach ($Text in $From.Keys) {
        if ($Other.ContainsKey($Text)) { continue }
        $Where = $From[$Text]
        $Separator = $Where.LastIndexOf(':')
        $Rows.Add([pscustomobject]@{ Text = $Text; Where = $Where; Path = $Where.Substring(0, $Separator); Line = [int]$Where.Substring($Separator + 1) })
    }
    $Comparison = [System.Comparison[object]] {
        param($Left, $Right)
        $ByPath = [string]::CompareOrdinal($Left.Path, $Right.Path)
        if ($ByPath -ne 0) { return $ByPath }
        return $Left.Line.CompareTo($Right.Line)
    }
    $Rows.Sort($Comparison)
    return , $Rows.ToArray()
}

function Write-Section {
    param([string]$Title, [object[]]$Rows)
    Write-Output ('{0} ({1})' -f $Title, $Rows.Count)
    foreach ($Row in $Rows) {
        Write-Output ('  {0}  {1}' -f $Row.Where, (Format-Shown -Text $Row.Text))
    }
}

try {
    if ([string]::IsNullOrEmpty($Root)) {
        $ScriptDirectory = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
        $Root = Join-Path -Path $ScriptDirectory -ChildPath '..\..'
    }
    $RootPath = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')

    $null = Invoke-Git -Arguments @('rev-parse', '--verify', '--quiet', ($Base + '^{commit}'))
    $BasePaths = @((Invoke-Git -Arguments @('ls-tree', '-r', '-z', '--name-only', $Base, '--', 'src')).Split([char]0) |
        Where-Object { $_.Length -gt 0 -and (Test-SourcePath -Path $_) })
    $BaseMap = Get-PolishTextMap -Paths $BasePaths -ReadContent { param($Path) Invoke-Git -Arguments @('show', ($Base + ':' + $Path)) }

    $SrcPath = Join-Path -Path $RootPath -ChildPath 'src'
    $TreePaths = @()
    if (Test-Path -LiteralPath $SrcPath) {
        $TreePaths = @(Get-ChildItem -LiteralPath $SrcPath -Recurse -File |
            ForEach-Object { $_.FullName.Substring($RootPath.Length).TrimStart('\', '/').Replace('\', '/') } |
            Where-Object { Test-SourcePath -Path $_ })
    }
    $Utf8 = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
    $TreeMap = Get-PolishTextMap -Paths $TreePaths -ReadContent {
        param($Path)
        [System.IO.File]::ReadAllText((Join-Path -Path $RootPath -ChildPath $Path), $Utf8)
    }

    Write-Output ('Polskie teksty: {0} → drzewo robocze (pliki: {1} w {0}, {2} w drzewie)' -f $Base, $BasePaths.Count, $TreePaths.Count)
    $Gone = Get-Difference -From $BaseMap -Other $TreeMap
    $New = Get-Difference -From $TreeMap -Other $BaseMap
    Write-Section -Title 'Zniknęły' -Rows $Gone
    Write-Section -Title 'Nowe' -Rows $New
    Write-Output ('Zniknęły: {0}, nowe: {1}' -f $Gone.Count, $New.Count)
    exit 0
}
catch {
    Write-Output ('Błąd: {0}' -f $_.Exception.Message)
    exit 2
}
