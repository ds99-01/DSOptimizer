using System;
using System.Linq;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

/// <summary>Strażnik ustawień w sesji okna: <see cref="GuiSession.DriftedSettings"/> i zachowanie serii po dryfie.</summary>
public class GuiSessionDriftTests
{
    private static readonly DateTimeOffset Boot = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan After = TimeSpan.FromMinutes(5);

    private static GuiSession Session(EngineFixture f, params TweakDefinition[] definitions)
        => new(new LoadedCatalog(definitions, "sha256:test"), f.CreateServices());

    private static void Drift(EngineFixture f)
        => f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));

    private static (EngineFixture F, GuiSession Session, TweakDefinition Tweak) Drifted()
    {
        var f = new EngineFixture();
        var tweak = TestTweaks.TwoValues();
        Assert.Equal(TweakOutcome.Applied, f.CreateEngine().Apply(tweak).Outcome);
        Drift(f);
        return (f, Session(f, tweak), tweak);
    }

    private static void Seed(EngineFixture f, JournalEntry entry)
    {
        var doc = new JournalDocument { Machine = "TEST-PC" };
        doc.Entries.Add(entry);
        f.Journal.Save(doc);
    }

    [Fact]
    public void DriftedSettings_AfterOutsideChange_ListsItem_WritesNothing()
    {
        var (f, session, tweak) = Drifted();
        var saves = f.Journal.SaveCount;

        var states = session.DetectAll();

        Assert.Equal(DetectionState.Partial, states[tweak.Id]);
        Assert.Equal(new[] { tweak.Id }, session.DriftedSettings(states, Boot, Boot));
        Assert.Equal(saves, f.Journal.SaveCount);
    }

    [Fact]
    public void DriftedSettings_Reapply_SameEntryAndBaseline_ThenEmpty()
    {
        var (f, session, tweak) = Drifted();
        var before = Assert.Single(f.Journal.Current.Entries);
        var entryId = before.EntryId;
        var baseline = before.Baseline.Select(item => item.Describe()).ToList();
        var eventCount = before.Events.Count;

        var report = session.Run(tweak.Id, GuiOperation.Apply, confirmed: true);

        Assert.Equal(TweakOutcome.Applied, report.Result!.Outcome);
        var after = Assert.Single(f.Journal.Current.Entries);
        Assert.Equal(entryId, after.EntryId);
        Assert.Equal(baseline, after.Baseline.Select(item => item.Describe()).ToList());
        var added = after.Events.Skip(eventCount).Select(e => (e.Action, e.Outcome)).ToList();
        Assert.Equal(new[] { ("apply", "Started"), ("apply", "Applied") }, added);
        Assert.Empty(session.DriftedSettings(report.Refreshed, Boot, Boot));
    }

    [Fact]
    public void DriftedSettings_UndoAfterDrift_RestoresPreDsoState_AndEmpty()
    {
        var (f, session, tweak) = Drifted();

        var report = session.Run(tweak.Id, GuiOperation.Undo, confirmed: true);

        Assert.Equal(RegValue.Absent, f.Registry.Get(TestTweaks.Path, "A"));
        Assert.Equal(RegValue.Absent, f.Registry.Get(TestTweaks.Path, "B"));
        Assert.Equal(JournalEntryStatus.Undone, Assert.Single(f.Journal.Current.Entries).Status);
        Assert.Empty(session.DriftedSettings(report.Refreshed, Boot, Boot));
    }

    [Fact]
    public void DriftedSettings_UnknownBoot_UsesSessionStart()
    {
        var f = new EngineFixture();
        var tweak = TestTweaks.TwoValues("a") with { RequiresReboot = true };
        var entry = new JournalEntry { TweakId = "a", UserSid = f.User.Sid, Status = JournalEntryStatus.PendingReboot };
        entry.Events.Add(new JournalEvent { At = Boot + After, Action = "apply", Outcome = "PendingReboot" });
        Seed(f, entry);
        var session = Session(f, tweak);
        var states = new System.Collections.Generic.Dictionary<string, DetectionState> { ["a"] = DetectionState.NotApplied };

        Assert.Empty(session.DriftedSettings(states, null, Boot));
        Assert.Equal(new[] { "a" }, session.DriftedSettings(states, null, Boot + After + After));
    }

    [Fact]
    public void DriftedSettings_EditionChanged_NotSupported_Empty()
    {
        var f = new EngineFixture();
        var tweak = TestTweaks.TwoValues("a") with { Editions = new[] { EditionFamily.Home } };
        Seed(f, new JournalEntry { TweakId = "a", UserSid = f.User.Sid, Status = JournalEntryStatus.Active });
        f.SystemInfo.Edition = "Professional";
        var session = Session(f, tweak);

        var states = session.DetectAll();

        Assert.Equal(DetectionState.NotSupported, states["a"]);
        Assert.Empty(session.DriftedSettings(states, Boot, Boot));
    }

    [Fact]
    public void DriftedSettings_JournalThrows_EmptyAndFileLogWarning()
    {
        var (f, session, tweak) = Drifted();
        var states = session.DetectAll();
        f.Journal.LoadException = new JournalStoreException("brak dostępu");

        Assert.Empty(session.DriftedSettings(states, Boot, Boot));
        Assert.Contains("WARN [Gui] Nie udało się sprawdzić zastosowanych ustawień: brak dostępu", f.Log.Lines);
    }

    [Fact]
    public void Drift_Ipv6Disable_ChangedToPreferValue_OnlyDisableListed_BatchRefusesConflict()
    {
        const string disable = "net.ipv6-disable";
        const string prefer = "net.ipv6-prefer-ipv4";
        var definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions
            .Where(d => d.Id is disable or prefer).ToArray();
        var f = new EngineFixture();
        var tweaksById = definitions.ToDictionary(d => d.Id);
        Assert.Equal(TweakOutcome.Applied, f.CreateEngine().Apply(tweaksById[disable]).Outcome);
        var entryId = Assert.Single(f.Journal.Current.Entries).EntryId;
        f.Registry.Set(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", RegValue.FromDWord(0x20));
        var session = Session(f, definitions);

        var states = session.DetectAll();

        Assert.Equal(DetectionState.NotApplied, states[disable]);
        Assert.Equal(DetectionState.Applied, states[prefer]);
        Assert.Equal(new[] { disable }, session.DriftedSettings(states, Boot, Boot));

        var report = session.RunBatch(new[] { prefer, disable }, GuiOperation.Apply, confirmed: true);

        var results = report.Batch!.Results.ToDictionary(r => r.TweakId);
        Assert.Equal(TweakOutcome.Refused, results[prefer].Outcome);
        Assert.Contains(disable, results[prefer].Message);
        Assert.Contains("najpierw ją cofnij", results[prefer].Message);
        Assert.Equal(TweakOutcome.Applied, results[disable].Outcome);
        Assert.Equal(RegValue.FromDWord(0xFF), f.Registry.Get(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents"));
        Assert.Equal(entryId, Assert.Single(f.Journal.Current.Entries).EntryId);
    }
}
