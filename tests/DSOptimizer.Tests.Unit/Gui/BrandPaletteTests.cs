using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class BrandPaletteTests
{
    [Fact]
    public void Light_PinnedValues()
    {
        Assert.Equal(new BrandAccentColors("#007CA5", "#006D93", "#005E82", "#FFFFFF"), BrandPalette.Light);
    }

    [Fact]
    public void Dark_PinnedValues()
    {
        Assert.Equal(new BrandAccentColors("#00A9D4", "#37B9E2", "#0093BA", "#17191B"), BrandPalette.Dark);
    }

    [Theory]
    [InlineData(false, "#007CA5")]
    [InlineData(true, "#00A9D4")]
    public void For_PicksPaletteByTheme(bool darkTheme, string accent)
    {
        Assert.Equal(accent, BrandPalette.For(darkTheme).Accent);
    }

    [Fact]
    public void NavMarkerAndRing_SameCyanInBothThemes()
    {
        Assert.Equal("#00A9D4", BrandPalette.NavMarker);
        Assert.Equal("#00A9D4", BrandPalette.MarkRing);
    }
}
