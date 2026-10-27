using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>Pozycje kategorii ux (partia C1): wartości z docs/zrodla/2026-10-17-partia-c1-zrodla.md i z odczytu VM 26H2.</summary>
public class UxCatalogTests
{
    private static TweakDefinition Get(string id)
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == id);

    [Fact]
    public void UxEntries_ArePresentInUxCategory()
    {
        var ux = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Category == "ux").Select(d => d.Id).OrderBy(i => i, StringComparer.Ordinal);
        Assert.Equal(new[] { "ux.classic-context-menu", "ux.game-dvr-policy", "ux.menu-delay", "ux.taskbar-end-task" }, ux);
    }

    [Theory]
    [InlineData("ux_menu", "ux.menu-delay")]
    [InlineData("ux_gamedvr", "ux.game-dvr-policy")]
    public void LegacyAlias_PointsToExactlyOneEntry(string alias, string id)
    {
        var owners = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Aliases.Contains(alias)).Select(d => d.Id).ToList();
        Assert.Equal(new[] { id }, owners);
    }

    [Fact]
    public void MenuDelay_IsUserStringWithVmDefault400()
    {
        var def = Get("ux.menu-delay");
        Assert.Equal(TweakScope.User, def.Scope);
        Assert.Equal(RiskLevel.Safe, def.Risk);
        var entry = Assert.Single(def.Registry);
        Assert.Equal(@"HKCU\Control Panel\Desktop", entry.Path);
        Assert.Equal("MenuShowDelay", entry.Name);
        Assert.Equal(RegValue.FromString("10"), entry.Value);
        Assert.Equal(RegValue.FromString("400"), entry.Original);
        Assert.NotEmpty(def.Warnings);
    }

    [Fact]
    public void GameDvrUser_IsNotPortedButDocumented()
    {
        // Brak źródła MS dla skutku GameDVR_Enabled=0 (spec 11: pozycja odpada), więc nie ma jej w katalogu; docs z werdyktem zostają.
        var ids = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Select(d => d.Id);
        Assert.DoesNotContain("ux.game-dvr", ids);
        var docs = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", "ux.game-dvr.md"), Encoding.UTF8);
        Assert.Contains("nie portowane", docs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameDvrPolicy_IsMachinePolicyRemovedOnUndo()
    {
        var def = Get("ux.game-dvr-policy");
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        var entry = Assert.Single(def.Registry);
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", entry.Path);
        Assert.Equal("AllowGameDVR", entry.Name);
        Assert.Equal(RegValue.FromDWord(0), entry.Value);
        Assert.Equal(RegValue.Absent, entry.Original);
    }

    [Fact]
    public void AppCaptureEnabled_IsNotInCatalog()
    {
        // Na 26H2 wartość nie istnieje (VM), a źródło MS brak: nie wchodzi do katalogu.
        var names = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.SelectMany(d => d.Registry).Select(e => e.Name);
        Assert.DoesNotContain("AppCaptureEnabled", names);
    }

    private const string ClassicMenuKey = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    [Fact]
    public void ClassicContextMenu_IsEmptyDefaultStringInUserClasses()
    {
        var def = Get("ux.classic-context-menu");
        Assert.Equal(TweakKind.Tweak, def.Kind);
        Assert.Equal(TweakScope.User, def.Scope);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.False(def.Suggested);
        Assert.False(def.RequiresReboot);
        var entry = Assert.Single(def.Registry);
        Assert.Equal(ClassicMenuKey, entry.Path);
        Assert.Equal(string.Empty, entry.Name);
        Assert.Equal(RegValue.FromString(string.Empty), entry.Value);
        Assert.Equal(RegValue.Absent, entry.Original);
    }

    [Fact]
    public void TaskbarEndTask_IsUserDwordOneInDeveloperSettings()
    {
        var def = Get("ux.taskbar-end-task");
        Assert.Equal(TweakKind.Tweak, def.Kind);
        Assert.Equal(TweakScope.User, def.Scope);
        Assert.Equal(RiskLevel.Safe, def.Risk);
        Assert.False(def.Suggested);
        Assert.False(def.RequiresReboot);
        var entry = Assert.Single(def.Registry);
        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", entry.Path);
        Assert.Equal("TaskbarEndTask", entry.Name);
        Assert.Equal(RegValue.FromDWord(1), entry.Value);
        Assert.Equal(RegValue.Absent, entry.Original);
    }

    [Fact]
    public void ClassicContextMenu_TextStaysWithinObservation()
    {
        var def = Get("ux.classic-context-menu");
        var text = def.Summary + " " + def.Benefit;
        Assert.DoesNotContain("pełne", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pokaż więcej opcji", text, StringComparison.Ordinal);
        Assert.Contains("Win32", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskbarEndTask_WarnsNeutrallyAboutProcessTermination()
    {
        var text = string.Join(" ", Get("ux.taskbar-end-task").Warnings);
        Assert.DoesNotContain("bez zapisywania danych", text, StringComparison.Ordinal);
        Assert.Contains("kończy proces programu; niezapisane dane w tym programie mogą przepaść", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ux.classic-context-menu")]
    [InlineData("ux.taskbar-end-task")]
    public void ConvenienceItems_WarnAboutExplorerRestartAndPointToAction(string id)
    {
        // Spec opisów 2026-10-26: akcja wskazana nazwą kategorii, bez identyfikatora; „sam nie restartuje” w szczegółach technicznych.
        var def = Get(id);
        var text = string.Join(" ", def.Warnings);
        Assert.Contains("restarcie Eksploratora w „Narzędziach”", text, StringComparison.Ordinal);
        Assert.Contains("nie restartuje", def.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ux.classic-context-menu")]
    [InlineData("ux.taskbar-end-task")]
    public void ConvenienceItems_HaveSummaryBenefitAndValidEntry(string id)
    {
        var def = Get(id);
        Assert.False(string.IsNullOrWhiteSpace(def.Summary));
        Assert.False(string.IsNullOrWhiteSpace(def.Benefit));
        Assert.Empty(CatalogValidator.Validate(new[] { def }));
    }

    [Theory]
    [InlineData("ux.classic-context-menu")]
    [InlineData("ux.taskbar-end-task")]
    public void ConvenienceItems_DocHasObservationSectionForHomeAndPro(string id)
    {
        var doc = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", id + ".md"), Encoding.UTF8);
        Assert.Contains("## Obserwacja skutku", doc, StringComparison.Ordinal);
        Assert.Contains("2026-10-21", doc, StringComparison.Ordinal);
        Assert.Contains("**Home**", doc, StringComparison.Ordinal);
        Assert.Contains("**Pro**", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicContextMenu_DocMentionsObservedWin32MenuWindow()
    {
        var doc = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", "ux.classic-context-menu.md"), Encoding.UTF8);
        Assert.Contains("#32768", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void GameDvrUser_StaysDroppedWithoutNewObservation()
    {
        // Task 4: na VM brak sprzętowego kodera, więc skutek nierozstrzygnięty; werdykt bez zmian względem C1.
        var docs = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", "ux.game-dvr.md"), Encoding.UTF8);
        Assert.DoesNotContain("## Obserwacja skutku", docs, StringComparison.Ordinal);
    }
}
