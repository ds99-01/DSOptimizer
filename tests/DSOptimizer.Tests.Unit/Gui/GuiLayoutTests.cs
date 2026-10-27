using System;
using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class GuiLayoutTests
{
    [Fact]
    public void InitialBounds_Laptop1366()
    {
        var r = GuiLayout.InitialBounds(new LayoutRect(0, 0, 1366, 720));

        Assert.Equal(1229.4, r.Width, 3);
        Assert.Equal(648, r.Height, 3);
        Assert.Equal((1366 - 1229.4) / 2, r.Left, 3);
        Assert.Equal((720 - 648) / 2.0, r.Top, 3);
    }

    [Fact]
    public void InitialBounds_LargeMonitor_CappedAt1600x1000()
    {
        var r = GuiLayout.InitialBounds(new LayoutRect(0, 0, 2560, 1400));

        Assert.Equal(1600, r.Width, 3);
        Assert.Equal(1000, r.Height, 3);
        Assert.Equal(480, r.Left, 3);
        Assert.Equal(200, r.Top, 3);
    }

    [Fact]
    public void InitialBounds_SmallerThanMinimum_FillsWorkArea()
    {
        var r = GuiLayout.InitialBounds(new LayoutRect(0, 0, 683, 373));

        Assert.Equal(683, r.Width, 3);
        Assert.Equal(373, r.Height, 3);
        Assert.Equal(0, r.Left, 3);
        Assert.Equal(0, r.Top, 3);
    }

    [Fact]
    public void InitialBounds_BetweenNinetyPercentAndMinimum_UsesMinimum()
    {
        var r = GuiLayout.InitialBounds(new LayoutRect(0, 0, 850, 560));

        Assert.Equal(800, r.Width, 3);
        Assert.Equal(520, r.Height, 3);
    }

    [Fact]
    public void InitialBounds_OffsetMonitor_CentersInsideIt()
    {
        var r = GuiLayout.InitialBounds(new LayoutRect(-1920, 0, 1920, 1040));

        Assert.Equal(-1920 + (1920 - 1600) / 2.0, r.Left, 3);
        Assert.Equal((1040 - 936) / 2.0, r.Top, 3);
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1366, 0)]
    [InlineData(-5, 720)]
    [InlineData(1366, -1)]
    public void InitialBounds_NonPositiveSize_Throws(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiLayout.InitialBounds(new LayoutRect(0, 0, width, height)));
    }

    [Theory]
    [InlineData(700, 280)]
    [InlineData(640, 280)]
    [InlineData(1000, 1000 / 3.0)]
    [InlineData(1500, 420)]
    public void DetailColumnWidth_Table(double contentWidth, double expected)
    {
        Assert.Equal(expected, GuiLayout.DetailColumnWidth(contentWidth), 2);
    }

    /// <summary>Wąska treść: szczegóły ustępują liście (lista ≥ 360 z odstępem), ale nie schodzą poniżej 240.</summary>
    [Theory]
    [InlineData(620, 260)]
    [InlineData(600, 240)]
    [InlineData(486, 240)]
    public void DetailColumnWidth_NarrowContent_ShrinksForList(double contentWidth, double expected)
    {
        Assert.Equal(expected, GuiLayout.DetailColumnWidth(contentWidth), 2);
    }

    /// <summary>
    /// Ekran 1024×768 (obszar roboczy bez paska zadań 48 px): okno z InitialBounds, minus menu 280 i marginesy treści 22 + 16.
    /// Kolumna listy (z odstępem 20 przed szczegółami) ma co najmniej 360 px, czyli sama lista ≥ 340.
    /// Najgorszy przypadek z pełnym menu; przy tej szerokości okna menu jest zwinięte (patrz DetailColumnWidth_CompactNav922_…).
    /// </summary>
    [Fact]
    public void DetailColumnWidth_Screen1024x768_LeavesListAtLeast360()
    {
        var window = GuiLayout.InitialBounds(new LayoutRect(0, 0, 1024, 768 - 48));
        var content = window.Width - 280 - 22 - 16;

        var detail = GuiLayout.DetailColumnWidth(content);

        Assert.True(content - detail >= 360 - 0.01, $"lista {content - detail:0.#} px");
        Assert.True(detail >= 240, $"szczegóły {detail:0.#} px");
    }

    [Theory]
    [InlineData(1099.9, true)]
    [InlineData(1100, false)]
    [InlineData(800, true)]
    public void IsNavCompact_BelowThreshold(double windowWidth, bool expected)
    {
        Assert.Equal(expected, GuiLayout.IsNavCompact(windowWidth));
        Assert.Equal(expected ? GuiLayout.NavCompactWidth : GuiLayout.NavFullWidth, GuiLayout.NavWidth(windowWidth));
    }

    [Fact]
    public void NavWidths_FullAndCompact()
    {
        Assert.Equal(304, GuiLayout.NavFullWidth);
        Assert.Equal(56, GuiLayout.NavCompactWidth);
        Assert.Equal(1100, GuiLayout.NavCompactBelow);
    }

    /// <summary>Ekran 1024×768 (obszar roboczy 1024×720): okno startowe ok. 922 px, menu zwinięte do ikon.</summary>
    [Fact]
    public void IsNavCompact_Screen1024x768_InitialWindow()
    {
        var window = GuiLayout.InitialBounds(new LayoutRect(0, 0, 1024, 720));

        Assert.True(GuiLayout.IsNavCompact(window.Width), $"okno {window.Width:0.#} px");
        Assert.Equal(GuiLayout.NavCompactWidth, GuiLayout.NavWidth(window.Width));
    }

    /// <summary>Laptop 1366×768 przy 100% (obszar roboczy 1366×720): okno 1229,4 px, menu pełne.</summary>
    [Fact]
    public void IsNavCompact_Laptop1366_InitialWindow_Full()
    {
        var window = GuiLayout.InitialBounds(new LayoutRect(0, 0, 1366, 720));

        Assert.False(GuiLayout.IsNavCompact(window.Width), $"okno {window.Width:0.#} px");
        Assert.Equal(GuiLayout.NavFullWidth, GuiLayout.NavWidth(window.Width));
    }

    /// <summary>
    /// Pełne menu przy najwęższym oknie (próg zwijania 1100) i oknie startowym laptopa 1366×768 (1229,4): kolumna listy
    /// (treść − szczegóły, z odstępem przed szczegółami) ≥ 360, szczegóły ≥ 280.
    /// </summary>
    [Theory]
    [InlineData(1100)]
    [InlineData(1229.4)]
    public void DetailColumnWidth_FullNav_LeavesListAtLeast360(double window)
    {
        Assert.False(GuiLayout.IsNavCompact(window));
        var content = window - GuiLayout.NavWidth(window) - 22 - 16;

        var detail = GuiLayout.DetailColumnWidth(content);

        Assert.True(content - detail >= 360, $"lista {content - detail:0.#} px");
        Assert.True(detail >= 280, $"szczegóły {detail:0.#} px");
    }

    /// <summary>Okno 922 px przy zwiniętym menu: treść = okno − menu 56 − marginesy 22 + 16; lista (treść − szczegóły) ≥ 480.</summary>
    [Fact]
    public void DetailColumnWidth_CompactNav922_LeavesListAtLeast480()
    {
        const double window = 922;
        var content = window - GuiLayout.NavWidth(window) - 22 - 16;

        var detail = GuiLayout.DetailColumnWidth(content);

        Assert.True(content - detail >= 480, $"lista {content - detail:0.#} px");
    }

    [Theory]
    [InlineData(520, 146)]
    [InlineData(1000, 200)]
    [InlineData(1400, 240)]
    public void InitialLogHeight_Table(double windowHeight, double expected)
    {
        Assert.Equal(expected, GuiLayout.InitialLogHeight(windowHeight), 2);
    }

    [Theory]
    [InlineData(520, 260)]
    [InlineData(250, 146)]
    public void MaxLogHeight_Table(double windowHeight, double expected)
    {
        Assert.Equal(expected, GuiLayout.MaxLogHeight(windowHeight), 2);
    }
}
