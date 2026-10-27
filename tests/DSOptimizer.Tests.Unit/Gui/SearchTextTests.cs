using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class SearchTextTests
{
    [Fact]
    public void Fold_PolishLetters_RemovesDiacriticsIncludingL()
    {
        Assert.Equal("zazolc gesla jazn", SearchText.Fold("Zażółć GĘŚLĄ jaźń"));
        Assert.Equal("wylacz", SearchText.Fold("Wyłącz"));
        Assert.Equal("a b", SearchText.Fold("  a \t b  "));
        Assert.Equal(string.Empty, SearchText.Fold(null));
    }
}
