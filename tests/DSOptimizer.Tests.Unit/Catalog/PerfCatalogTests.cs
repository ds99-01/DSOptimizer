using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>Pozycje kategorii perf (partia C2): wartości z docs/zrodla/2026-10-18-partia-c2-zrodla.md i z odczytu VM 26H2.</summary>
public class PerfCatalogTests
{
    private const string MemoryManagementKey = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

    private static TweakDefinition Get(string id)
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == id);

    private static string Doc(string id)
        => File.ReadAllText(Path.Combine(RepoRoot.Combine("docs", "tweaks"), id + ".md"), Encoding.UTF8);

    [Fact]
    public void PerfEntries_ArePresentInPerfCategory()
    {
        var ids = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Category == "perf").Select(d => d.Id).OrderBy(i => i, StringComparer.Ordinal);
        Assert.Equal(new[] { "perf.paging-executive", "perf.sysmain", "perf.vbs-hvci" }, ids);
    }

    [Theory]
    [InlineData("io_sysmain", "perf.sysmain")]
    [InlineData("cpu_paging", "perf.paging-executive")]
    [InlineData("cpu_vbs", "perf.vbs-hvci")]
    public void LegacyAlias_PointsToExactlyOneEntry(string alias, string id)
    {
        var owners = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Aliases.Contains(alias)).Select(d => d.Id).ToList();
        Assert.Equal(new[] { id }, owners);
    }

    [Fact]
    public void SpectreMitigations_IsNotPorted_NoCatalogEntryNorAlias()
    {
        var definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;
        Assert.DoesNotContain(definitions, d => d.Id == "perf.spectre-mitigations");
        Assert.DoesNotContain(definitions, d => d.Aliases.Contains("cpu_spectre"));
        var doc = Doc("perf.spectre-mitigations");
        Assert.Contains("nie portowane", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FeatureSettingsOverride", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void AllPerfEntries_AreExperimentalMachineTweaks()
    {
        var perf = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Category == "perf").ToList();
        Assert.NotEmpty(perf);
        Assert.All(perf, d =>
        {
            Assert.Equal(RiskLevel.Experimental, d.Risk);
            Assert.Equal(TweakScope.Machine, d.Scope);
            Assert.NotEmpty(d.Warnings);
            // Spec 5: opisy bez obietnic wydajności i bez liczb spoza MS.
            var text = d.Description + " " + string.Join(" ", d.Warnings);
            Assert.DoesNotContain("%", text, StringComparison.Ordinal);
            Assert.DoesNotContain("32 GB", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("przyspiesz", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WSL2", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void SysMain_DisablesAndStopsServiceWithMeasuredAutomaticOriginal()
    {
        var def = Get("perf.sysmain");
        Assert.False(def.RequiresReboot);
        Assert.Empty(def.Registry);
        Assert.Null(def.Script);
        var service = Assert.Single(def.Services);
        Assert.Equal("SysMain", service.Name);
        Assert.Equal(ServiceStartType.Disabled, service.StartType);
        Assert.True(service.Stop);
        Assert.Equal(ServiceStartType.Automatic, service.Original); // VM 26H2 Home i Pro: Auto, DelayedAutoStart=false
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("Microsoft", warnings, StringComparison.Ordinal);
        // Spec opisów 2026-10-26: uwaga techniczna o wycofaniu zmiany przeniesiona do szczegółów technicznych.
        Assert.Contains("limicie czasu", def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void PagingExecutive_SetsDwordOneWithVmOriginalZeroAndRequiresReboot()
    {
        var def = Get("perf.paging-executive");
        Assert.True(def.RequiresReboot);
        Assert.Empty(def.Services);
        Assert.Null(def.Script);
        var entry = Assert.Single(def.Registry);
        Assert.Equal(MemoryManagementKey, entry.Path);
        Assert.Equal("DisablePagingExecutive", entry.Name);
        Assert.Equal(RegValue.FromDWord(1), entry.Value);
        Assert.Equal(RegValue.FromDWord(0), entry.Original); // VM 26H2 Home i Pro: wartość istnieje, DWORD 0
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("pamięci", warnings, StringComparison.OrdinalIgnoreCase);
        // Spec opisów 2026-10-26 (przegląd T3, R5): archiwalne źródła Microsoftu w szczegółach technicznych, nie w uwagach.
        Assert.Contains("archiw", def.Description, StringComparison.OrdinalIgnoreCase);
        // „restart” zamiast „restarc”: reguła CatalogCopyRules wymaga słowa „restart”/„ponown” (forma „restarcie” go nie zawiera).
        Assert.Contains("restart", warnings, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Silnik na fake'ach (Review Focus 1, 2, 5) ----

    [Theory]
    [InlineData(OperationErrorKind.Timeout)]
    [InlineData(OperationErrorKind.Unexpected)] // prawdziwa odmowa przy działających zależnych (WindowsServiceManager)
    public void SysMain_Apply_WhenStopRejected_RollsBackStartTypeAndLeavesServiceRunning(OperationErrorKind kind)
    {
        // Review Focus 1: odmowa stopu (zależne działają / timeout) -> czytelny błąd, typ startu wraca, nie zostaje Disabled bez śladu.
        var f = new EngineFixture();
        f.Services.Add("SysMain", ServiceStartType.Automatic, running: true);
        f.Services.FailStop("SysMain", kind);

        var result = f.CreateEngine().Apply(Get("perf.sysmain"));

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(ServiceStartType.Automatic, f.Services.StartTypeOf("SysMain"));
        Assert.True(f.Services.IsRunning("SysMain"));
        Assert.Equal(JournalEntryStatus.Aborted, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void SysMain_Undo_RestartsServiceThatWasRunningBeforeApply()
    {
        // Review Focus 2: działała przed apply -> po undo znowu Automatic + Running.
        var f = new EngineFixture();
        f.Services.Add("SysMain", ServiceStartType.Automatic, running: true);
        var engine = f.CreateEngine();
        var def = Get("perf.sysmain");

        Assert.Equal(TweakOutcome.Applied, engine.Apply(def).Outcome);
        Assert.Equal(ServiceStartType.Disabled, f.Services.StartTypeOf("SysMain"));
        Assert.False(f.Services.IsRunning("SysMain"));

        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);
        Assert.Equal(ServiceStartType.Automatic, f.Services.StartTypeOf("SysMain"));
        Assert.True(f.Services.IsRunning("SysMain"));
    }

    [Fact]
    public void SysMain_Undo_KeepsStoppedManualServiceStopped()
    {
        // Review Focus 2: stała (Manual) przed apply -> po undo Manual i dalej stoi, nie jest startowana.
        var f = new EngineFixture();
        f.Services.Add("SysMain", ServiceStartType.Manual, running: false);
        var engine = f.CreateEngine();
        var def = Get("perf.sysmain");

        Assert.Equal(TweakOutcome.Applied, engine.Apply(def).Outcome);
        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);

        Assert.Equal(ServiceStartType.Manual, f.Services.StartTypeOf("SysMain"));
        Assert.False(f.Services.IsRunning("SysMain"));
        Assert.DoesNotContain("StartService:SysMain", f.Services.MutationCalls);
    }

    [Fact]
    public void PagingExecutive_Undo_WhenValueWasAbsent_RemovesItInsteadOfWritingZero()
    {
        // Review Focus 5 (mechanizm): wartość nieistniejąca przed apply nie może wrócić jako 0. Na 26H2 Home/Pro wartość istnieje (DWORD 0),
        // ale kod katalogu ma zachować brak, jeśli na innym obrazie go nie ma; snapshot z journala wygrywa z `original`.
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        var def = Get("perf.paging-executive");

        var applied = engine.Apply(def);
        Assert.Equal(TweakOutcome.Applied, applied.Outcome);
        Assert.True(applied.RequiresReboot);
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(MemoryManagementKey, "DisablePagingExecutive"));

        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(MemoryManagementKey, "DisablePagingExecutive"));
    }

    [Fact]
    public void PagingExecutive_Undo_WhenValueWasZero_RestoresZero()
    {
        var f = new EngineFixture();
        f.Registry.Set(MemoryManagementKey, "DisablePagingExecutive", RegValue.FromDWord(0));
        var engine = f.CreateEngine();
        var def = Get("perf.paging-executive");

        var applied = engine.Apply(def);
        Assert.Equal(TweakOutcome.Applied, applied.Outcome);
        Assert.True(applied.RequiresReboot);
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(MemoryManagementKey, "DisablePagingExecutive"));

        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(MemoryManagementKey, "DisablePagingExecutive"));
    }
}
