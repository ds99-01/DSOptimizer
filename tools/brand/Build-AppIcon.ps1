#Requires -Version 5.1
<#
.SYNOPSIS
    Renderuje znak „DŚO” z Themes\BrandMark.xaml do wieloklatkowej ikony aplikacji (dsoptimizer.ico).

.DESCRIPTION
    Instrukcja uruchomienia (Windows PowerShell 5.1, z katalogu repo, bez uprawnień administratora):
        .\tools\brand\Build-AppIcon.ps1                  # odtwarza src\DSOptimizer.App\Assets\dsoptimizer.ico
        .\tools\brand\Build-AppIcon.ps1 -ExportLetters   # wypisuje kontury „DŚ” do wklejenia w BrandMarkLettersGeometry

    Opis działania:
        Bez parametru skrypt ładuje src\DSOptimizer.App\Themes\BrandMark.xaml (XamlReader z .NET Framework, bez Tokens.xaml),
        renderuje klatki 16, 20, 24, 32, 40, 48, 64 i 256 px przez RenderTargetBitmap (klatki do 20 px z BrandMarkSmallImage,
        większe z BrandMarkImage), koduje je jako PNG i zapisuje plik .ico (ICONDIR + wpisy 16-bajtowe + dane PNG).
        Ten sam plik XAML daje zawsze te same bajty .ico.

        -ExportLetters buduje „DŚ” z pliku src\DSOptimizer.App\Assets\Fonts\HankenGrotesk-Bold.ttf (nie z czcionki systemu)
        w układzie kierunku E z renderu 19 (Hanken Grotesk Bold 88 px, pierścień 65 px / kreska 16 px, siatka 256 z kwadratem
        240 px) przeliczonym na siatkę 256 px z kwadratem na całą klatkę. Wypisuje dane ścieżki liter; środek i promienie
        pierścienia „O” z tego samego układu idą do strumienia informacyjnego.

.PARAMETER ExportLetters
    Zamiast ikony wypisuje dane ścieżki „DŚ” (BrandMarkLettersGeometry).
#>
[CmdletBinding()]
param(
    [switch]$ExportLetters
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$RepoRoot = (Resolve-Path -LiteralPath (Join-Path -Path $PSScriptRoot -ChildPath '..\..')).Path
$BrandMarkPath = Join-Path -Path $RepoRoot -ChildPath 'src\DSOptimizer.App\Themes\BrandMark.xaml'
$IconPath = Join-Path -Path $RepoRoot -ChildPath 'src\DSOptimizer.App\Assets\dsoptimizer.ico'
$FontDirectory = Join-Path -Path $RepoRoot -ChildPath 'src\DSOptimizer.App\Assets\Fonts'
$FontFileName = 'HankenGrotesk-Bold.ttf'
$Invariant = [System.Globalization.CultureInfo]::InvariantCulture

$FrameSizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$SmallFrameMaxSize = 20

function Format-Number {
    param([Parameter(Mandatory)][double]$Value)

    $rounded = [Math]::Round($Value, 2, [MidpointRounding]::AwayFromZero)
    if ($rounded -eq 0) { $rounded = 0.0 }
    return $rounded.ToString('0.##', $Invariant)
}

function Get-BrandLetterTypeface {
    $baseUri = New-Object -TypeName System.Uri -ArgumentList ($FontDirectory.TrimEnd('\') + '\')
    $family = New-Object -TypeName System.Windows.Media.FontFamily -ArgumentList $baseUri, ('./' + $FontFileName + '#Hanken Grotesk')
    $typeface = New-Object -TypeName System.Windows.Media.Typeface -ArgumentList $family,
        ([System.Windows.FontStyles]::Normal), ([System.Windows.FontWeights]::Bold), ([System.Windows.FontStretches]::Normal)

    $glyphs = $null
    if (-not $typeface.TryGetGlyphTypeface([ref]$glyphs) -or
        -not $glyphs.FontUri.LocalPath.EndsWith('\' + $FontFileName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Nie udało się wczytać czcionki z pliku $FontDirectory\$FontFileName."
    }

    return $typeface
}

function Export-BrandMarkLetters {
    # Układ kierunku E z renderu 19 (gen_dso3.py, wariant „e”): siatka 256, kwadrat 240 px z marginesem 8,
    # StackPanel wyśrodkowany z górnym marginesem 14: TextBlock „DŚ” 88 px + Ellipse 65 px (kreska 16),
    # odstęp 4, dół pierścienia 24 px nad dołem wiersza tekstu.
    $fontSize = 88.0
    $ringSize = 65.0
    $ringStroke = 16.0
    $ringGap = 4.0
    $ringBottom = 24.0
    $panelTop = 14.0
    $sourceGrid = 256.0
    $sourceTileMargin = 8.0
    $scale = $sourceGrid / ($sourceGrid - (2 * $sourceTileMargin))

    $typeface = Get-BrandLetterTypeface
    $text = 'D' + [char]0x015A
    $measure = New-Object -TypeName System.Windows.Media.FormattedText -ArgumentList $text, $Invariant,
        ([System.Windows.FlowDirection]::LeftToRight), $typeface, $fontSize, ([System.Windows.Media.Brushes]::Black), 1.0

    $textWidth = $measure.WidthIncludingTrailingWhitespace
    $rowHeight = [Math]::Max($measure.Height, $ringSize + $ringBottom)
    $panelWidth = $textWidth + $ringGap + $ringSize
    $left = ($sourceGrid - $panelWidth) / 2
    $top = $panelTop + (($sourceGrid - $panelTop) - $rowHeight) / 2

    # Przeliczenie na siatkę 256 z kwadratem na całą klatkę: p' = (p - 8) * 256 / 240.
    $letters = New-Object -TypeName System.Windows.Media.FormattedText -ArgumentList $text, $Invariant,
        ([System.Windows.FlowDirection]::LeftToRight), $typeface, ($fontSize * $scale), ([System.Windows.Media.Brushes]::Black), 1.0
    $origin = New-Object -TypeName System.Windows.Point -ArgumentList (($left - $sourceTileMargin) * $scale), (($top - $sourceTileMargin) * $scale)
    $path = [System.Windows.Media.PathGeometry]::CreateFromGeometry($letters.BuildGeometry($origin))
    $path.FillRule = [System.Windows.Media.FillRule]::Nonzero

    $ringX = (($left + $textWidth + $ringGap + ($ringSize / 2)) - $sourceTileMargin) * $scale
    $ringY = (($top + $rowHeight - $ringBottom - ($ringSize / 2)) - $sourceTileMargin) * $scale
    $outer = ($ringSize / 2) * $scale
    $inner = (($ringSize / 2) - $ringStroke) * $scale
    Write-Information -MessageData ('Pierścień „O”: środek {0},{1}, promień zewnętrzny {2}, wewnętrzny {3}' -f
        (Format-Number $ringX), (Format-Number $ringY), (Format-Number $outer), (Format-Number $inner)) -InformationAction Continue

    $data = $path.ToString($Invariant)
    return [regex]::Replace($data, '-?\d+(\.\d+)?(E[-+]?\d+)?', {
            param($match)
            Format-Number ([double]::Parse($match.Value, $Invariant))
        })
}

function Read-BrandMark {
    $stream = [System.IO.File]::OpenRead($BrandMarkPath)
    try {
        return [System.Windows.Markup.XamlReader]::Load($stream)
    }
    finally {
        $stream.Dispose()
    }
}

function ConvertTo-PngFrame {
    param(
        [Parameter(Mandatory)][System.Windows.Media.ImageSource]$Image,
        [Parameter(Mandatory)][int]$Size
    )

    $visual = New-Object -TypeName System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    try {
        $context.DrawImage($Image, (New-Object -TypeName System.Windows.Rect -ArgumentList 0, 0, $Size, $Size))
    }
    finally {
        $context.Close()
    }

    $bitmap = New-Object -TypeName System.Windows.Media.Imaging.RenderTargetBitmap -ArgumentList $Size, $Size, 96.0, 96.0,
        ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = New-Object -TypeName System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $buffer = New-Object -TypeName System.IO.MemoryStream
    try {
        $encoder.Save($buffer)
        return , $buffer.ToArray()
    }
    finally {
        $buffer.Dispose()
    }
}

function Write-IconFile {
    param(
        [Parameter(Mandatory)][int[]]$Sizes,
        [Parameter(Mandatory)][object[]]$Frames,
        [Parameter(Mandatory)][string]$Path
    )

    $buffer = New-Object -TypeName System.IO.MemoryStream
    $writer = New-Object -TypeName System.IO.BinaryWriter -ArgumentList $buffer
    try {
        # ICONDIR: reserved 0, type 1 (ikona), liczba klatek.
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Sizes.Count)

        $offset = 6 + (16 * $Sizes.Count)
        for ($i = 0; $i -lt $Sizes.Count; $i++) {
            $side = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }
            $writer.Write([byte]$side)          # szerokość (0 = 256)
            $writer.Write([byte]$side)          # wysokość (0 = 256)
            $writer.Write([byte]0)              # liczba kolorów palety
            $writer.Write([byte]0)              # zarezerwowane
            $writer.Write([uint16]1)            # płaszczyzny
            $writer.Write([uint16]32)           # bity na piksel
            $writer.Write([uint32]$Frames[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $Frames[$i].Length
        }

        foreach ($frame in $Frames) {
            $writer.Write([byte[]]$frame)
        }

        $writer.Flush()
        [System.IO.File]::WriteAllBytes($Path, $buffer.ToArray())
    }
    finally {
        $writer.Dispose()
        $buffer.Dispose()
    }
}

try {
    if ($ExportLetters) {
        Export-BrandMarkLetters
        return
    }

    $brandMark = Read-BrandMark
    $frames = foreach ($size in $FrameSizes) {
        $key = if ($size -le $SmallFrameMaxSize) { 'BrandMarkSmallImage' } else { 'BrandMarkImage' }
        , (ConvertTo-PngFrame -Image $brandMark[$key] -Size $size)
    }

    Write-IconFile -Sizes $FrameSizes -Frames $frames -Path $IconPath
    Write-Information -MessageData ('Zapisano {0} ({1} klatek).' -f $IconPath, $FrameSizes.Count) -InformationAction Continue
}
catch {
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    exit 1
}
