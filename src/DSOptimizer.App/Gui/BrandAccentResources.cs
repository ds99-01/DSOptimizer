using System;
using System.Windows;
using System.Windows.Media;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Zasoby akcentu treści (klucze z <c>Themes/Tokens.xaml</c>) podmieniane przy zmianie motywu na kolory z <see cref="BrandPalette"/>.
/// Widoki biorą je przez <c>DynamicResource</c>, więc nowe pędzle trafiają od razu do otwartych okien.
/// </summary>
public static class BrandAccentResources
{
    public const string AccentColorKey = "BrandAccentColor";
    public const string AccentBrushKey = "BrandAccentBrush";
    public const string HoverBrushKey = "BrandAccentHoverBrush";
    public const string PressedBrushKey = "BrandAccentPressedBrush";
    public const string OnAccentBrushKey = "BrandOnAccentBrush";

    /// <summary>Wpisuje kolory do słownika (zamrożone pędzle) i zwraca kolor akcentu dla WPF-UI.</summary>
    public static Color Apply(ResourceDictionary resources, BrandAccentColors colors)
    {
        if (resources is null)
        {
            throw new ArgumentNullException(nameof(resources));
        }

        if (colors is null)
        {
            throw new ArgumentNullException(nameof(colors));
        }

        var accent = Parse(colors.Accent);
        resources[AccentColorKey] = accent;
        resources[AccentBrushKey] = Brush(accent);
        resources[HoverBrushKey] = Brush(Parse(colors.Hover));
        resources[PressedBrushKey] = Brush(Parse(colors.Pressed));
        resources[OnAccentBrushKey] = Brush(Parse(colors.OnAccent));
        return accent;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
