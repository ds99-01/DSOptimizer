using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>Pozycje kategorii sec (partia C4): wartości z docs/zrodla/2026-10-18-partia-c4-zrodla.md, sekcja 1.</summary>
public class SecCatalogTests
{
    private const string BitLockerKey = @"HKLM\SYSTEM\CurrentControlSet\Control\BitLocker";
    private const string PreventId = "sec.bitlocker-prevent-auto";

    private static TweakDefinition Get(string id)
        => CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Single(d => d.Id == id);

    private static string Doc(string id)
        => File.ReadAllText(Path.Combine(RepoRoot.Combine("docs", "tweaks"), id + ".md"), Encoding.UTF8);

    [Fact]
    public void SecEntries_ArePresentInSecCategory()
    {
        var ids = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Category == "sec").Select(d => d.Id).ToList();
        Assert.Contains(PreventId, ids);
        Assert.Contains("sec.bitlocker-decrypt-c", ids);
    }

    [Fact]
    public void LegacyAlias_BitlockerKill_PointsToExactlyOneEntry()
    {
        var owners = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Aliases.Contains("sec_bitlocker_kill")).Select(d => d.Id).ToList();
        Assert.Equal(new[] { PreventId }, owners);
    }

    [Fact]
    public void PreventAuto_IsCautionMachineDwordOneWithoutReboot()
    {
        var def = Get(PreventId);
        Assert.Equal("sec", def.Category);
        Assert.Equal(RiskLevel.Caution, def.Risk);
        Assert.Equal(TweakScope.Machine, def.Scope);
        Assert.False(def.RequiresReboot);
        Assert.Empty(def.Services);
        Assert.Null(def.Script);
        var entry = Assert.Single(def.Registry);
        Assert.Equal(BitLockerKey, entry.Path);
        Assert.Equal("PreventDeviceEncryption", entry.Name);
        Assert.Equal(RegValue.FromDWord(1), entry.Value);
        Assert.Equal(RegValue.Absent, entry.Original);
    }

    [Fact]
    public void PreventAuto_Warnings_StateLicenceRecallTheftAndPolicy_DetailsStateLimits()
    {
        // Spec opisów 2026-10-26: cytaty Microsoftu tylko w dokumencie (PreventAuto_Doc_HasQuotesVmReadAndUndoWithoutJournal),
        // w uwagach ich sens po polsku, a ograniczenia pozycji w szczegółach technicznych.
        var def = Get(PreventId);
        var warnings = string.Join(" ", def.Warnings);
        Assert.Contains("licencję Windows 11", warnings, StringComparison.Ordinal);
        Assert.Contains("Recall", warnings, StringComparison.Ordinal);
        Assert.Contains("kradzieży", warnings, StringComparison.Ordinal);
        Assert.Contains("polityka", warnings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("producentów urządzeń (OEM)", def.Description, StringComparison.Ordinal);
        Assert.Contains("nie odszyfrowuje dysku", def.Description, StringComparison.Ordinal);
        Assert.Contains(Get("sec.bitlocker-decrypt-c").Title, def.Description, StringComparison.Ordinal);
        Assert.Contains("skutek na już działającym systemie nie jest opisany", def.Description, StringComparison.Ordinal);
        Assert.Contains("Jeśli wartość 1 ustawił wcześniej np. producent, cofnięcie bez zapisu sprzed zmiany też ją usuwa", def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void PreventAuto_Description_PromisesOnlyTheRegistryValue()
    {
        var def = Get(PreventId);
        Assert.Contains("PreventDeviceEncryption", def.Description, StringComparison.Ordinal);
        Assert.Contains("Cofnięcie przywraca poprzednią wartość, a jeśli jej nie było – usuwa ją.", def.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("odszyfruj", def.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%", def.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void PreventAuto_Doc_HasQuotesVmReadAndUndoWithoutJournal()
    {
        var doc = Doc(PreventId);
        Assert.Contains("is prohibited as it violates the Windows 11 licensing requirements", doc, StringComparison.Ordinal);
        Assert.Contains("We do not recommend setting this registry key on devices with Recall feature", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("do uzupełnienia po eksploracji VM", doc, StringComparison.Ordinal);
        Assert.Contains("`Exists=false`", doc, StringComparison.Ordinal);
        Assert.Contains("ConversionStatus` = 2", doc, StringComparison.Ordinal);
        Assert.Contains("bez wpisu w journalu", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("usuwa wartość", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("Task", doc, StringComparison.Ordinal);
        Assert.Contains("vm-run-20261019-002807", doc, StringComparison.Ordinal);
    }

    // ---- Silnik na fake'ach (Review Focus 5) ----

    [Fact]
    public void PreventAuto_ApplyUndo_WhenValueWasAbsent_RemovesItAgain()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        var def = Get(PreventId);

        Assert.Equal(DetectionState.NotApplied, engine.Detect(def));
        var applied = engine.Apply(def);
        Assert.Equal(TweakOutcome.Applied, applied.Outcome);
        Assert.False(applied.RequiresReboot);
        Assert.Equal(DetectionState.Applied, engine.Detect(def));

        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(BitLockerKey, "PreventDeviceEncryption"));
    }

    [Fact]
    public void PreventAuto_Undo_WhenValueWasZeroBeforeApply_RestoresZeroFromJournalSnapshot()
    {
        // Snapshot z journala wygrywa z `original` (<RemoveEntry>): wartość istniejąca przed apply nie może zniknąć po undo.
        var f = new EngineFixture();
        f.Registry.Set(BitLockerKey, "PreventDeviceEncryption", RegValue.FromDWord(0));
        var engine = f.CreateEngine();
        var def = Get(PreventId);

        Assert.Equal(TweakOutcome.Applied, engine.Apply(def).Outcome);
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(BitLockerKey, "PreventDeviceEncryption"));

        Assert.Equal(TweakOutcome.Undone, engine.Undo(def).Outcome);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(BitLockerKey, "PreventDeviceEncryption"));
    }

    [Fact]
    public void PreventAuto_WhenValueOneExistedBeforeApply_IsDetectedAppliedAndApplyChangesNothing()
    {
        // Review Focus 5: wartość 1 ustawiona wcześniej (np. przez OEM) -> detect Applied; apply to AlreadyApplied bez zapisu
        // i bez wpisu w journalu, czyli narzędzie nie przejmuje własności tej wartości.
        var f = new EngineFixture();
        f.Registry.Set(BitLockerKey, "PreventDeviceEncryption", RegValue.FromDWord(1));
        var engine = f.CreateEngine();
        var def = Get(PreventId);

        Assert.Equal(DetectionState.Applied, engine.Detect(def));
        Assert.Equal(TweakOutcome.AlreadyApplied, engine.Apply(def).Outcome);
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(BitLockerKey, "PreventDeviceEncryption"));
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Fact]
    public void PreventAuto_UndoWithoutJournal_RemovesTheValue()
    {
        // Fallback `original` = <RemoveEntry>: undo bez wpisu w journalu usuwa wartość (także tę ustawioną poza narzędziem).
        var f = new EngineFixture();
        f.Registry.Set(BitLockerKey, "PreventDeviceEncryption", RegValue.FromDWord(1));
        var result = f.CreateEngine().Undo(Get(PreventId));

        Assert.Equal(TweakOutcome.UndoneFromDefaults, result.Outcome);
        Assert.Equal(RegValue.Absent, f.Registry.Get(BitLockerKey, "PreventDeviceEncryption"));
    }
}
