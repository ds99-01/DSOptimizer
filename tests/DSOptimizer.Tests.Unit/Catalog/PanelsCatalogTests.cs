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

/// <summary>
/// Kategoria panels (partia C5, Task 5): polecenia <c>launch</c> to wartości z odczytu VM 26H2 Home i Pro (2026-10-19),
/// zapisane w docs/zrodla/2026-10-19-partia-c5-zrodla.md (sekcje „Wyniki VM” i „Sonda uzupełniająca”),
/// a nie z samego researchu. Tabela poniżej jest celowo niezależną kopią: zmiana polecenia w katalogu musi być świadoma.
/// </summary>
public class PanelsCatalogTests
{
    private const string DevicesAndPrinters = "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}";

    // Id, launch.file, launch.arguments (kolejność jak w t3-vm-results.md: „Panele wchodzące” + warunkowe z sondy uzupełniającej).
    private static readonly (string Id, string File, string Arguments)[] Expected =
    {
        ("panels.visual-effects", "SystemPropertiesPerformance.exe", ""),
        ("panels.power-options", "control.exe", "/name Microsoft.PowerOptions"),
        ("panels.network-connections", "control.exe", "netconnections"),
        ("panels.computer-management", "compmgmt.msc", ""),
        ("panels.control-panel", "control.exe", ""),
        ("panels.mouse", "control.exe", "/name Microsoft.Mouse"),
        ("panels.devices-printers", DevicesAndPrinters, ""),
        ("panels.programs-features", "control.exe", "/name Microsoft.ProgramsAndFeatures"),
        ("panels.region", "control.exe", "/name Microsoft.RegionAndLanguage"),
        ("panels.security-maintenance", "control.exe", "wscui.cpl"),
        ("panels.sound", "control.exe", "/name Microsoft.Sound"),
        ("panels.system-properties", "control.exe", "sysdm.cpl"),
        ("panels.date-time", "control.exe", "/name Microsoft.DateAndTime"),
        ("panels.firewall", "control.exe", "/name Microsoft.WindowsFirewall"),
        ("panels.system-restore", "rstrui.exe", ""),
    };

    private static List<TweakDefinition> Panels()
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Category == "panels").ToList();

    private static TweakDefinition Get(string id) => Panels().Single(d => d.Id == id);

    public static IEnumerable<object[]> ExpectedRows() => Expected.Select(e => new object[] { e.Id, e.File, e.Arguments });

    [Fact]
    public void Catalog_LoadsWithoutValidationErrors()
    {
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        Assert.Empty(CatalogValidator.Validate(catalog.Definitions));
    }

    [Fact]
    public void PanelsCategory_HasExactlyTheFifteenAgreedIds()
    {
        Assert.Equal(
            Expected.Select(e => e.Id).OrderBy(i => i, StringComparer.Ordinal),
            Panels().Select(d => d.Id).OrderBy(i => i, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryPanel_IsKindPanelRiskSafeWithLaunchAndNoScript()
    {
        Assert.All(Panels(), d =>
        {
            Assert.Equal(TweakKind.Panel, d.Kind);
            Assert.Equal(RiskLevel.Safe, d.Risk);
            Assert.NotNull(d.Launch);
            Assert.Null(d.Script);
            Assert.Empty(d.Registry);
            Assert.Empty(d.Services);
        });
    }

    [Theory]
    [MemberData(nameof(ExpectedRows))]
    public void Launch_MatchesVmReading(string id, string file, string arguments)
    {
        var launch = Assert.IsType<LaunchSpec>(Get(id).Launch);
        Assert.Equal(file, launch.File);
        Assert.Equal(arguments, launch.Arguments);
        Assert.True(LaunchTarget.IsAllowed(launch.File));
    }

    [Theory]
    [InlineData("tool_vis", "panels.visual-effects")]
    [InlineData("tool_pow", "panels.power-options")]
    [InlineData("tool_network", "panels.network-connections")]
    public void LegacyAlias_PointsToExactlyOnePanel(string alias, string id)
    {
        var owners = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Aliases.Contains(alias)).Select(d => d.Id).ToList();
        Assert.Equal(new[] { id }, owners);
    }

    [Fact]
    public void Aliases_AreOnlyTheThreeLegacyPanels()
    {
        var aliases = Panels().SelectMany(d => d.Aliases).OrderBy(a => a, StringComparer.Ordinal);
        Assert.Equal(new[] { "tool_network", "tool_pow", "tool_vis" }, aliases);
    }

    [Fact]
    public void EveryPanel_HasDocsFileWithPortedVerdictAndLaunchCommand()
    {
        Assert.All(Panels(), d =>
        {
            var path = RepoRoot.Combine("docs", "tweaks", d.Id + ".md");
            Assert.True(File.Exists(path), $"Brak docs/tweaks/{d.Id}.md");
            var text = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains(d.Launch!.File, text, StringComparison.Ordinal);
            Assert.Contains("## Werdykt 26H2\nPortować jako `kind=panel`, `risk=safe`", text.Replace("\r\n", "\n"), StringComparison.Ordinal);
            Assert.DoesNotContain("Nie portowane", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Descriptions_StartWithPanelOpeningAndPromiseNoEffect()
    {
        Assert.All(Panels(), d =>
        {
            Assert.StartsWith("Otwiera panel systemowy Windows", d.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("Ultimate Performance Plan", d.Description, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(d.Title));
        });
    }

    [Fact]
    public void DevicesPrinters_UsesShellNamespaceOfDevicesAndPrintersAndWarnsAboutShellGuard()
    {
        var def = Get("panels.devices-printers");
        Assert.Equal("Urządzenia i drukarki", def.Title);
        Assert.Contains(def.Warnings, w => w.Contains("Eksploratora", StringComparison.Ordinal));
    }

    [Fact]
    public void DevicesPrinters_DocsDescribeFailedGuardCaseNotSkip()
    {
        var text = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", "panels.devices-printers.md"), Encoding.UTF8);
        Assert.Contains("panels-devices-printers-no-shell", text, StringComparison.Ordinal);
        Assert.Contains("Failed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("to `Skip`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("– `Skip`", text, StringComparison.Ordinal);
        Assert.Contains("nie jest `Skip`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemProperties_DescribesDefaultTabAsInferenceNotReading()
    {
        var def = Get("panels.system-properties");
        Assert.Contains("wniosek", def.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemRestore_DocsMentionWbengineSideEffectAndWizardWithProtectionOff()
    {
        var text = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", "panels.system-restore.md"), Encoding.UTF8);
        Assert.Contains("wbengine.exe", text, StringComparison.Ordinal);
        Assert.Contains("wyłączonej ochronie", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedVariants_AreDocumentedInTheirPanelsDocs()
    {
        var visual = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", "panels.visual-effects.md"), Encoding.UTF8);
        Assert.Contains("sysdm.cpl,,3", visual, StringComparison.Ordinal);
        var printers = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", "panels.devices-printers.md"), Encoding.UTF8);
        Assert.Contains("Microsoft.DevicesAndPrinters", printers, StringComparison.Ordinal);
        var system = File.ReadAllText(RepoRoot.Combine("docs", "tweaks", "panels.system-properties.md"), Encoding.UTF8);
        Assert.Contains("Microsoft.System", system, StringComparison.Ordinal);
    }
}
