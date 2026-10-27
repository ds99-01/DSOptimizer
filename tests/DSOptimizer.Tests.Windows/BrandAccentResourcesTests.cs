using System.Windows;
using System.Windows.Media;
using DSOptimizer.App.Gui;
using DSOptimizer.Core.Gui;
using Wpf.Ui.Appearance;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Akcent zależny od motywu (S4, wariant B): ThemeController wybiera paletę, BrandAccentResources wpisuje ją do zasobów.</summary>
[Trait("Category", "Windows")]
public class BrandAccentResourcesTests
{
    [Theory]
    [InlineData(ApplicationTheme.Dark, true)]
    [InlineData(ApplicationTheme.Light, false)]
    [InlineData(ApplicationTheme.HighContrast, false)]
    [InlineData(ApplicationTheme.Unknown, false)]
    public void PaletteFor_DarkOnlyForDarkTheme(ApplicationTheme theme, bool dark)
    {
        Assert.Same(dark ? BrandPalette.Dark : BrandPalette.Light, ThemeController.PaletteFor(theme));
    }

    [Fact]
    public void Apply_Dark_WritesFrozenBrushesAndReturnsAccent()
    {
        var resources = new ResourceDictionary();

        var accent = BrandAccentResources.Apply(resources, BrandPalette.Dark);

        Assert.Equal(Color.FromRgb(0x00, 0xA9, 0xD4), accent);
        Assert.Equal(accent, (Color)resources[BrandAccentResources.AccentColorKey]);
        AssertBrush(resources, BrandAccentResources.AccentBrushKey, 0x00, 0xA9, 0xD4);
        AssertBrush(resources, BrandAccentResources.HoverBrushKey, 0x37, 0xB9, 0xE2);
        AssertBrush(resources, BrandAccentResources.PressedBrushKey, 0x00, 0x93, 0xBA);
        AssertBrush(resources, BrandAccentResources.OnAccentBrushKey, 0x17, 0x19, 0x1B);
    }

    [Fact]
    public void Apply_LightAfterDark_ReplacesEveryKey()
    {
        var resources = new ResourceDictionary();
        BrandAccentResources.Apply(resources, BrandPalette.Dark);

        var accent = BrandAccentResources.Apply(resources, BrandPalette.Light);

        Assert.Equal(Color.FromRgb(0x00, 0x7C, 0xA5), accent);
        Assert.Equal(accent, (Color)resources[BrandAccentResources.AccentColorKey]);
        AssertBrush(resources, BrandAccentResources.AccentBrushKey, 0x00, 0x7C, 0xA5);
        AssertBrush(resources, BrandAccentResources.HoverBrushKey, 0x00, 0x6D, 0x93);
        AssertBrush(resources, BrandAccentResources.PressedBrushKey, 0x00, 0x5E, 0x82);
        AssertBrush(resources, BrandAccentResources.OnAccentBrushKey, 0xFF, 0xFF, 0xFF);
    }

    private static void AssertBrush(ResourceDictionary resources, string key, byte r, byte g, byte b)
    {
        var brush = Assert.IsType<SolidColorBrush>(resources[key]);
        Assert.True(brush.IsFrozen, $"{key} nie jest zamrożony.");
        Assert.Equal(Color.FromRgb(r, g, b), brush.Color);
    }
}
