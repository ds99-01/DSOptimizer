using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Packaging;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Prep;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Pełne lewe menu (<see cref="GuiLayout.NavFullWidth"/>) mieści każdą nazwę pozycji z licznikiem, także pogrubioną (zaznaczona pozycja),
/// bez wielokropka. Pomiar czcionką aplikacji (Hanken Grotesk z zasobów DSOptimizer.exe) – nie zastępczą Segoe UI, na której
/// wcześniejszy pomiar dał 187 px zamiast 193 px dla „Bezpieczeństwo i szyfrowanie”.
/// </summary>
[Trait("Category", "Windows")]
public class NavMenuWidthTests
{
    // Szablon kategorii i „Historii zmian” w MainWindow.xaml: ikona (Margin 16,0,12,0 + Size 20) | nazwa 14 px (Margin 0,0,8,0)
    // | licznik 12.5 px (Margin 0,0,16,0). Wiersz ListBoxItem bez wypełnienia; pasek przewijania WPF-UI rysuje się nad treścią.
    private const double IconBlock = 16 + 20 + 12;
    private const double NameRightMargin = 8;
    private const double CounterRightMargin = 16;
    private const double NameSize = 14;
    private const double CounterSize = 12.5;

    /// <summary>Najszerszy licznik dwucyfrowy („zastosowane/tweaki”); cyfry Hanken Grotesk są równej szerokości.</summary>
    private const string WidestCounter = "88/88";

    private const string FamilyName = "Hanken Grotesk";

    /// <summary>Okno ma TextFormattingMode=Display (szerokości zaokrąglane do pikseli urządzenia), więc pomiar przy typowych skalach DPI.</summary>
    private static readonly double[] PixelsPerDip = [1.0, 1.25, 1.5, 1.75, 2.0];

    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    [Theory]
    [InlineData(400)]
    [InlineData(600)]
    public void BrandFont_IsHankenGroteskFromAppResources_NotSimulated(int weight)
    {
        var fontWeight = FontWeight.FromOpenTypeWeight(weight);
        var glyphs = GlyphTypefaceOf(fontWeight);

        Assert.Contains(FamilyName, glyphs.FamilyNames.Values);
        Assert.Contains("DSOptimizer;component", Uri.UnescapeDataString(glyphs.FontUri.ToString()), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fontWeight, glyphs.Weight);
        Assert.Equal(StyleSimulations.None, glyphs.StyleSimulations);
        foreach (var name in MenuNames())
        {
            var missing = name.Where(ch => !glyphs.CharacterToGlyphMap.ContainsKey(ch)).ToList();
            Assert.True(missing.Count == 0, $"„{name}”: brak znaków {string.Concat(missing)} w {FamilyName}");
        }
    }

    [Fact]
    public void MenuNames_FitFullNav_SemiBoldWithTwoDigitCounter()
    {
        var available = GuiLayout.NavFullWidth - IconBlock - NameRightMargin - CounterRightMargin;
        var failures = new List<string>();
        foreach (var name in MenuNames())
        {
            var widest = Modes()
                .Select(mode => (Mode: mode, Name: Width(name, FontWeights.SemiBold, NameSize, mode), Counter: Width(WidestCounter, FontWeights.Normal, CounterSize, mode)))
                .OrderByDescending(m => m.Name + m.Counter)
                .First();
            if (widest.Name + widest.Counter > available)
            {
                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "„{0}” {1:0.##} + licznik {2:0.##} > {3:0.##} (menu {4}, {5})",
                    name, widest.Name, widest.Counter, available, GuiLayout.NavFullWidth, widest.Mode.Label));
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Nazwy kategorii z katalogu wbudowanego i z kolejności menu oraz „Przygotowanie”, „Aplikacje”, „Instalacja” i „Historia zmian” (pomiar z licznikiem
    /// także dla stron bez licznika – zapas na przyszłość).
    /// </summary>
    private static IEnumerable<string> MenuNames()
    {
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        return catalog.Definitions.Select(item => item.Category)
            .Concat(GuiText.CategoryOrder)
            .Distinct(StringComparer.Ordinal)
            .Select(GuiText.CategoryName)
            .Append(PrepText.PageTitle)
            .Append(AppsPageText.PageTitle)
            .Append(InstallText.PageTitle)
            .Append(HistoryText.PageTitle)
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<(string Label, TextFormattingMode Mode, double PixelsPerDip)> Modes()
        => PixelsPerDip.Select(ppd => ("Display " + ppd.ToString("0.##", CultureInfo.InvariantCulture), TextFormattingMode.Display, ppd))
            .Append(("Ideal", TextFormattingMode.Ideal, 1.0));

    private static double Width(string text, FontWeight weight, double size, (string Label, TextFormattingMode Mode, double PixelsPerDip) mode)
    {
        var formatted = new FormattedText(text, Polish, FlowDirection.LeftToRight, Typeface(weight), size, Brushes.Black, null, mode.Mode, mode.PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static GlyphTypeface GlyphTypefaceOf(FontWeight weight)
    {
        Assert.True(Typeface(weight).TryGetGlyphTypeface(out var glyphs), $"{FamilyName} {weight}: nie wczytano czcionki z zasobów DSOptimizer.exe");
        return glyphs;
    }

    private static Typeface Typeface(FontWeight weight) => new(BrandFont, FontStyles.Normal, weight, FontStretches.Normal);

    /// <summary>
    /// Czcionka jak BrandFont w Themes/Tokens.xaml, ale z jawnym zestawem DSOptimizer (w teście nie ma aplikacji WPF).
    /// Statyczny konstruktor Application rejestruje schemat pack:// i pakiet „application”.
    /// </summary>
    private static FontFamily BrandFont { get; } = CreateBrandFont();

    private static FontFamily CreateBrandFont()
    {
        _ = PackUriHelper.UriSchemePack;
        _ = Application.Current;
        return new FontFamily(new Uri("pack://application:,,,/DSOptimizer;component/"), "./Assets/Fonts/#" + FamilyName);
    }
}
