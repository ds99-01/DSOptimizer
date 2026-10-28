using System.Collections.Generic;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Catalog;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class ItemFilterTests
{
    private static TweakDefinition Item(
        RiskLevel risk = RiskLevel.Safe,
        TweakKind kind = TweakKind.Tweak,
        string id = "disk.hibernate",
        string title = "Wyłącz hibernację") => new()
    {
        Id = id,
        Category = "disk",
        Kind = kind,
        Title = title,
        Summary = "Zwalnia miejsce na dysku C:.",
        Description = "powercfg /h off (0x20)",
        Risk = risk,
        Scope = TweakScope.Machine,
    };

    [Theory]
    [InlineData("wylacz")]
    [InlineData("HIBERNACJE")]
    [InlineData("lacz")]
    public void MatchesQuery_AsciiQuery_FindsPolishTitle(string query)
        => Assert.True(ItemFilter.MatchesQuery(Item(), query));

    [Theory]
    [InlineData("miejsce", true)]
    [InlineData("powercfg", true)]
    [InlineData("rejestr", false)]
    public void MatchesQuery_SearchesSummaryAndDescription(string query, bool expected)
        => Assert.Equal(expected, ItemFilter.MatchesQuery(Item(), query));

    [Theory]
    [InlineData("wylacz dysku", true)]
    [InlineData("wylacz siec", false)]
    public void MatchesQuery_AllWordsMustMatch(string query, bool expected)
        => Assert.Equal(expected, ItemFilter.MatchesQuery(Item(), query));

    [Theory]
    [InlineData("c:", true)]
    [InlineData("(0x20)", true)]
    [InlineData("*", false)]
    [InlineData("[", false)]
    public void MatchesQuery_SpecialCharacters_PlainContains(string query, bool expected)
        => Assert.Equal(expected, ItemFilter.MatchesQuery(Item(), query));

    [Fact]
    public void MatchesQuery_VeryLongQuery_NoMatchNoException()
        => Assert.False(ItemFilter.MatchesQuery(Item(), new string('x', 500)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MatchesQuery_WhitespaceOnly_MatchesAll(string? query)
        => Assert.True(ItemFilter.MatchesQuery(Item(), query));

    [Theory]
    [InlineData(RiskFilter.All, true, true, true)]
    [InlineData(RiskFilter.NoExperimental, true, true, false)]
    [InlineData(RiskFilter.SafeOnly, true, false, false)]
    public void PassesRisk_MatrixByFilter(RiskFilter filter, bool safe, bool caution, bool experimental)
    {
        Assert.Equal(safe, ItemFilter.PassesRisk(Item(RiskLevel.Safe), DetectionState.NotApplied, filter));
        Assert.Equal(caution, ItemFilter.PassesRisk(Item(RiskLevel.Caution), DetectionState.NotApplied, filter));
        Assert.Equal(experimental, ItemFilter.PassesRisk(Item(RiskLevel.Experimental), DetectionState.NotApplied, filter));
    }

    [Fact]
    public void PassesRisk_HiddenRiskButAppliedOrPartial_Passes()
    {
        var item = Item(RiskLevel.Experimental);
        Assert.True(ItemFilter.PassesRisk(item, DetectionState.Applied, RiskFilter.SafeOnly));
        Assert.True(ItemFilter.PassesRisk(item, DetectionState.Partial, RiskFilter.SafeOnly));
        Assert.False(ItemFilter.PassesRisk(item, DetectionState.Unknown, RiskFilter.SafeOnly));
        Assert.False(ItemFilter.PassesRisk(item, DetectionState.NotSupported, RiskFilter.SafeOnly));
        Assert.False(ItemFilter.PassesRisk(item, null, RiskFilter.SafeOnly));
        Assert.False(ItemFilter.PassesRisk(Item(RiskLevel.Experimental, TweakKind.Action), null, RiskFilter.SafeOnly));
        Assert.False(ItemFilter.PassesRisk(Item(RiskLevel.Experimental, TweakKind.Action), DetectionState.Applied, RiskFilter.SafeOnly));
    }

    [Fact]
    public void PassesRisk_UndefinedFilter_Throws()
        => Assert.Throws<System.ArgumentOutOfRangeException>(() => ItemFilter.PassesRisk(Item(), null, (RiskFilter)99));

    [Fact]
    public void IsVisible_CombinesQueryAndRisk()
    {
        var item = Item(RiskLevel.Experimental);
        Assert.True(ItemFilter.IsVisible(item, DetectionState.NotApplied, "wylacz", RiskFilter.All));
        Assert.False(ItemFilter.IsVisible(item, DetectionState.NotApplied, "wylacz", RiskFilter.SafeOnly));
        Assert.False(ItemFilter.IsVisible(item, DetectionState.NotApplied, "siec", RiskFilter.All));
    }

    private static KeyValuePair<TweakDefinition, DetectionState?> Pair(TweakDefinition d, DetectionState? s)
        => new(d, s);

    [Fact]
    public void CountHidden_SkipsAppliedExceptions()
    {
        var items = new[]
        {
            Pair(Item(RiskLevel.Experimental, id: "a"), DetectionState.NotApplied),
            Pair(Item(RiskLevel.Experimental, id: "b"), DetectionState.Partial),
            Pair(Item(RiskLevel.Experimental, id: "c"), DetectionState.NotApplied),
            Pair(Item(RiskLevel.Safe, id: "d"), DetectionState.NotApplied),
        };
        Assert.Equal(2, ItemFilter.CountHidden(items, null, RiskFilter.NoExperimental));
    }

    [Fact]
    public void CountHidden_AllHidden_ReturnsCategorySize()
    {
        var items = new[]
        {
            Pair(Item(RiskLevel.Experimental, id: "a"), DetectionState.NotApplied),
            Pair(Item(RiskLevel.Experimental, id: "b"), DetectionState.NotApplied),
            Pair(Item(RiskLevel.Experimental, id: "c"), DetectionState.NotApplied),
        };
        Assert.Equal(3, ItemFilter.CountHidden(items, string.Empty, RiskFilter.SafeOnly));
    }

    [Fact]
    public void CountHidden_RespectsQuery()
    {
        var items = new[]
        {
            Pair(Item(RiskLevel.Experimental, id: "a", title: "Alfa"), DetectionState.NotApplied),
            Pair(Item(RiskLevel.Experimental, id: "b", title: "Beta"), DetectionState.NotApplied),
            Pair(Item(RiskLevel.Experimental, id: "c", title: "Gamma"), DetectionState.NotApplied),
        };
        Assert.Equal(1, ItemFilter.CountHidden(items, "alfa", RiskFilter.SafeOnly));
    }

    [Fact]
    public void CountShownDespiteFilter_AppliedExperimental_SafeOnly_Counts1()
    {
        var items = new[]
        {
            Pair(Item(RiskLevel.Experimental, id: "a"), DetectionState.Applied),
            Pair(Item(RiskLevel.Experimental, id: "b"), DetectionState.NotApplied),
            Pair(Item(RiskLevel.Safe, id: "c"), DetectionState.Applied),
        };
        Assert.Equal(1, ItemFilter.CountShownDespiteFilter(items, null, RiskFilter.SafeOnly));
    }

    [Fact]
    public void CountShownDespiteFilter_All_Zero()
    {
        var items = new[]
        {
            Pair(Item(RiskLevel.Experimental, id: "a"), DetectionState.Applied),
            Pair(Item(RiskLevel.Experimental, id: "b"), DetectionState.Partial),
        };
        Assert.Equal(0, ItemFilter.CountShownDespiteFilter(items, null, RiskFilter.All));
    }

    [Fact]
    public void CountShownDespiteFilter_RespectsQuery()
    {
        var items = new[]
        {
            Pair(Item(RiskLevel.Experimental, id: "a", title: "Alfa"), DetectionState.Applied),
            Pair(Item(RiskLevel.Experimental, id: "b", title: "Beta"), DetectionState.Partial),
        };
        Assert.Equal(1, ItemFilter.CountShownDespiteFilter(items, "alfa", RiskFilter.SafeOnly));
    }

    [Fact]
    public void CountShownDespiteFilter_ActionNotCounted()
    {
        var items = new[]
        {
            Pair(Item(RiskLevel.Experimental, TweakKind.Action, id: "a"), DetectionState.Applied),
        };
        Assert.Equal(0, ItemFilter.CountShownDespiteFilter(items, null, RiskFilter.SafeOnly));
    }

    [Fact]
    public void Search_English_FindsByEnglishSummary()
    {
        var item = English(Item());

        Assert.True(ItemFilter.MatchesQuery(item, "space"));
        Assert.True(ItemFilter.MatchesQuery(item, "hibernation"));
    }

    [Fact]
    public void Search_English_PolishWordFindsNothing()
    {
        var item = English(Item());

        Assert.False(ItemFilter.MatchesQuery(item, "miejsce"));
        Assert.False(ItemFilter.MatchesQuery(item, "wylacz"));
    }

    private static TweakDefinition English(TweakDefinition item)
    {
        var table = CatalogTranslationTests.DataTable(
            ("Wyłącz hibernację", "Turn off hibernation"),
            ("Zwalnia miejsce na dysku C:.", "Frees up space on drive C:."));
        using (L.UseTable(table))
        using (L.Use(UiLanguage.English))
        {
            return CatalogTranslation.Translate(new LoadedCatalog(new[] { item }, "sha256:x")).Definitions[0];
        }
    }
}
