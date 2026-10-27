using System.Linq;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class SuggestedSelectionTests
{
    private static SuggestionCandidate C(string id, bool suggested, DetectionState? state) => new(id, suggested, state);

    [Fact]
    public void TakeFor_ReturnsNotAppliedAndPartialSuggested_Once()
    {
        var selection = new SuggestedSelection();
        var items = new[]
        {
            C("a", true, DetectionState.NotApplied),
            C("b", true, DetectionState.Partial),
            C("c", true, DetectionState.Applied),
            C("d", false, DetectionState.NotApplied),
        };

        Assert.Equal(new[] { "a", "b" }, selection.TakeFor("privacy", items));
        Assert.True(selection.IsConsumed("privacy"));
        Assert.Empty(selection.TakeFor("privacy", items));
    }

    [Fact]
    public void TakeFor_WaitsWhileSuggestedStateUnknown()
    {
        var selection = new SuggestedSelection();

        Assert.Empty(selection.TakeFor("privacy", new[] { C("a", true, null), C("b", true, DetectionState.NotApplied) }));
        Assert.False(selection.IsConsumed("privacy"));

        Assert.Equal(new[] { "a", "b" }, selection.TakeFor("privacy", new[] { C("a", true, DetectionState.NotApplied), C("b", true, DetectionState.NotApplied) }));
        Assert.True(selection.IsConsumed("privacy"));
    }

    [Fact]
    public void TakeFor_UnknownStateOfNotSuggestedItem_DoesNotBlock()
        => Assert.Equal(new[] { "a" }, new SuggestedSelection().TakeFor("privacy", new[] { C("a", true, DetectionState.NotApplied), C("x", false, null) }));

    [Fact]
    public void TakeFor_SkipsNotSupportedAndUnknown()
    {
        var selection = new SuggestedSelection();

        var result = selection.TakeFor("privacy", new[]
        {
            C("a", true, DetectionState.NotSupported),
            C("b", true, DetectionState.Unknown),
            C("c", true, DetectionState.NotApplied),
        });

        Assert.Equal(new[] { "c" }, result);
    }

    [Fact]
    public void TakeFor_CategoriesIndependent()
    {
        var selection = new SuggestedSelection();
        selection.TakeFor("privacy", new[] { C("a", true, DetectionState.NotApplied) });

        Assert.False(selection.IsConsumed("ux"));
        Assert.Equal(new[] { "u" }, selection.TakeFor("ux", new[] { C("u", true, DetectionState.NotApplied) }));
    }

    [Fact]
    public void TakeFor_NoSuggestedItems_ConsumesWithEmptyList()
    {
        var selection = new SuggestedSelection();

        Assert.Empty(selection.TakeFor("ux", new[] { C("a", false, DetectionState.NotApplied) }));
        Assert.True(selection.IsConsumed("ux"));
        Assert.Empty(selection.TakeFor("ux", new[] { C("b", true, DetectionState.NotApplied) }));
    }

    [Fact]
    public void TakeFor_EmptyList_Consumes()
    {
        var selection = new SuggestedSelection();
        Assert.Empty(selection.TakeFor("ux", Enumerable.Empty<SuggestionCandidate>().ToList()));
        Assert.True(selection.IsConsumed("ux"));
    }
}
