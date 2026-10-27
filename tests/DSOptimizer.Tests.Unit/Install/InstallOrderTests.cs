using System;
using System.Linq;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class InstallOrderTests
{
    /// <summary>Lista instalacji z wpisów (id, wingetId, kategoria, displayName reguły match).</summary>
    internal static AppsInstallCatalog Catalog(params (string Id, string WingetId, string Category, string DisplayName)[] items)
    {
        var entries = items.Select(item =>
            "{ \"id\": \"" + item.Id + "\", \"wingetId\": \"" + item.WingetId + "\", \"title\": \"Tytuł " + item.Id
            + "\", \"summary\": \"Opis.\", \"category\": \"" + item.Category + "\", \"match\": { \"displayName\": \""
            + item.DisplayName + "\" }, \"source\": \"https://example.com/" + item.WingetId + "\" }");
        return AppsInstallCatalog.Load("{ \"entries\": [" + string.Join(",", entries) + "] }");
    }

    private static readonly AppsInstallCatalog Sample = Catalog(
        ("install.7zip", "7zip.7zip", "tools", "7-Zip*"),
        ("install.vcredist", "Microsoft.VCRedist.2015+.x64", "runtimes", "Microsoft Visual C++*"),
        ("install.firefox", "Mozilla.Firefox", "browsers", "Mozilla Firefox*"),
        ("install.dotnet", "Microsoft.DotNet.DesktopRuntime.8", "runtimes", "Microsoft Windows Desktop Runtime*"));

    private static InstallTarget Entry(string id) => InstallTarget.FromEntry(Sample.Find(id)!);

    [Fact]
    public void RuntimesFirst_ThenCatalogOrder_ThenSearch()
    {
        var input = new[]
        {
            InstallTarget.FromSearch("Zeta.App", "Zeta"),
            Entry("install.firefox"),
            Entry("install.dotnet"),
            InstallTarget.FromSearch("Alpha.App", "Alpha"),
            Entry("install.7zip"),
            Entry("install.vcredist"),
        };

        var sorted = InstallOrder.Sort(input, Sample);

        Assert.Equal(
            new[] { "install.vcredist", "install.dotnet", "install.7zip", "install.firefox", "winget:Zeta.App", "winget:Alpha.App" },
            sorted.Select(t => t.Id));
    }

    [Fact]
    public void Sort_EmptyInput_Empty()
        => Assert.Empty(InstallOrder.Sort(Array.Empty<InstallTarget>(), Sample));

    [Fact]
    public void Sort_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => InstallOrder.Sort(null!, Sample));
        Assert.Throws<ArgumentNullException>(() => InstallOrder.Sort(Array.Empty<InstallTarget>(), null!));
    }

    [Fact]
    public void FromEntry_CopiesFields()
    {
        var entry = Sample.Find("install.firefox")!;

        var target = InstallTarget.FromEntry(entry);

        Assert.Equal("install.firefox", target.Id);
        Assert.Equal("Mozilla.Firefox", target.WingetId);
        Assert.Equal("Tytuł install.firefox", target.Title);
        Assert.Equal(InstallCategory.Browsers, target.Category);
        Assert.Same(entry.Match, target.Match);
        Assert.Null(target.Warning);
        Assert.Throws<ArgumentNullException>(() => InstallTarget.FromEntry(null!));
    }

    [Fact]
    public void FromSearch_WingetIdPrefixNoCategoryNoMatch()
    {
        var target = InstallTarget.FromSearch("Notepad++.Notepad++", "Notepad++");

        Assert.Equal("winget:Notepad++.Notepad++", target.Id);
        Assert.Equal("Notepad++.Notepad++", target.WingetId);
        Assert.Equal("Notepad++", target.Title);
        Assert.Null(target.Category);
        Assert.Null(target.Match);
        Assert.Null(target.Warning);
    }

    [Theory]
    [InlineData("Notepad++.Notepad…")]
    [InlineData("")]
    [InlineData("a b")]
    public void FromSearch_InvalidWingetId_Throws(string wingetId)
        => Assert.Throws<ArgumentException>(() => InstallTarget.FromSearch(wingetId, "Nazwa"));

    [Fact]
    public void FromSearch_EmptyName_TitleIsWingetId()
        => Assert.Equal("Vendor.App", InstallTarget.FromSearch("Vendor.App", "  ").Title);
}
