using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class GuiNavTests
{
    private static List<GuiCategory> Cats(params string[] ids)
        => ids.Select(id => new GuiCategory(id, id, new List<DSOptimizer.Core.Model.TweakDefinition>())).ToList();

    [Fact]
    public void CategoryPages_SettingsThenServiceWithThreeSections()
    {
        var pages = GuiNav.CategoryPages(Cats("privacy", "perf", "disk", "net", "sec", "ux", "repair", "tools", "panels"));

        Assert.Equal(new[] { "privacy", "perf", "disk", "net", "sec", "ux", "service" }, pages.Select(p => p.Id));
        Assert.Equal(new[] { "repair", "tools", "panels" }, pages[6].Sections.Select(s => s.Id));
        Assert.True(pages[6].IsService);
        Assert.Equal("Naprawy i narzędzia", pages[6].Name);
    }

    [Fact]
    public void CategoryPages_ServiceSectionsInFixedOrder()
    {
        var pages = GuiNav.CategoryPages(Cats("panels", "perf", "repair"));

        Assert.Equal(new[] { "repair", "panels" }, pages[pages.Count - 1].Sections.Select(s => s.Id));
    }

    [Fact]
    public void CategoryPages_UnknownCategoryStaysAmongSettings()
        => Assert.Equal(new[] { "perf", "aaa", "service" }, GuiNav.CategoryPages(Cats("perf", "aaa", "repair")).Select(p => p.Id));

    [Fact]
    public void CategoryPages_NoServiceCategories_NoServicePage()
        => Assert.DoesNotContain(GuiNav.CategoryPages(Cats("perf", "ux")), p => p.IsService);
}
