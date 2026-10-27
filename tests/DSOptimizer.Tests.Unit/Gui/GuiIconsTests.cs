using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class GuiIconsTests
{
    private static readonly string[] AllKinds =
    {
        "Specification", "Performance", "Disk", "Network", "Security", "Explorer", "Panels", "Tools", "Settings",
        "Refresh", "Copy", "SaveReport", "Search", "Info", "Warning", "OpenLocation", "History", "Privacy", "Suggested", "Apps", "Lock", "Prep", "Repair",
    };

    [Fact]
    public void ForCategory_EveryBuiltInCategoryHasOwnIcon()
    {
        var kinds = GuiText.CategoryOrder.Select(GuiIcons.ForCategory).ToList();

        Assert.Equal(kinds.Count, kinds.Distinct().Count());
        Assert.Equal("Tools", GuiIcons.ForCategory("tools"));
        Assert.DoesNotContain("Tools", GuiText.CategoryOrder.Where(id => id != "tools").Select(GuiIcons.ForCategory));
    }

    [Theory]
    [InlineData("privacy", "Privacy")]
    [InlineData("perf", "Performance")]
    [InlineData("disk", "Disk")]
    [InlineData("net", "Network")]
    [InlineData("sec", "Security")]
    [InlineData("ux", "Explorer")]
    [InlineData("panels", "Panels")]
    [InlineData("repair", "Repair")]
    [InlineData("tools", "Tools")]
    public void ForCategory_MapsKnownIds(string id, string expected)
        => Assert.Equal(expected, GuiIcons.ForCategory(id));

    [Fact]
    public void ForCategory_Unknown_FallsBackToTools()
        => Assert.Equal("Tools", GuiIcons.ForCategory("nieznana"));

    [Fact]
    public void Constants_AreNamedAfterKinds()
    {
        Assert.Equal("Specification", GuiIcons.Specification);
        Assert.Equal("Settings", GuiIcons.Settings);
        Assert.Equal("History", GuiIcons.History);
        Assert.Equal("Suggested", GuiIcons.Suggested);
        Assert.Equal("Apps", GuiIcons.Apps);
        Assert.Equal("Lock", GuiIcons.Lock);
        Assert.Equal("Prep", GuiIcons.Prep);
        Assert.Equal("Repair", GuiIcons.Repair);
    }

    [Fact]
    public void IconsXaml_DefinesStrokeForEveryKind()
    {
        var xaml = File.ReadAllText(RepoRoot.Combine("src", "DSOptimizer.App", "Themes", "Icons.xaml"));
        var keys = Regex.Matches(xaml, "x:Key=\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToHashSet();

        foreach (var kind in AllKinds)
        {
            Assert.Contains($"Icon{kind}Stroke", keys);
        }
    }
}
