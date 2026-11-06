using System;
using System.Collections.Generic;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class SettingsDriftTests
{
    private static readonly DateTimeOffset Boot = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan After = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Before = TimeSpan.FromMinutes(-5);

    private static JournalEntry Entry(string id, string sid, JournalEntryStatus status, params (string Action, string Outcome, TimeSpan Offset)[] events)
    {
        var entry = new JournalEntry { TweakId = id, UserSid = sid, Status = status };
        foreach (var (action, outcome, offset) in events)
        {
            entry.Events.Add(new JournalEvent { At = Boot + offset, Action = action, Outcome = outcome });
        }

        return entry;
    }

    private static JournalEntry Active(string id, string sid = "S1")
        => Entry(id, sid, JournalEntryStatus.Active, ("apply", "Started", Before), ("apply", "Applied", Before));

    private static JournalDocument Doc(params JournalEntry[] entries)
    {
        var doc = new JournalDocument();
        doc.Entries.AddRange(entries);
        return doc;
    }

    private static Dictionary<string, DetectionState> States(DetectionState state, params string[] ids)
    {
        var map = new Dictionary<string, DetectionState>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            map[id] = state;
        }

        return map;
    }

    private static IReadOnlyList<string> Compute(JournalDocument doc, TweakDefinition[] tweaks, IReadOnlyDictionary<string, DetectionState> states, string sid = "S1")
        => SettingsDrift.Compute(doc, tweaks, states, sid, Boot);

    private static TweakDefinition[] A() => new[] { TestTweaks.TwoValues("a") };

    [Theory]
    [InlineData(DetectionState.NotApplied)]
    [InlineData(DetectionState.Partial)]
    public void Compute_ActiveNotAppliedOrPartial_Drift(DetectionState s)
        => Assert.Equal(new[] { "a" }, Compute(Doc(Active("a")), A(), States(s, "a")));

    [Theory]
    [InlineData(DetectionState.Applied)]
    [InlineData(DetectionState.Unknown)]
    [InlineData(DetectionState.NotSupported)]
    public void Compute_ActiveOtherState_NotDrift(DetectionState s)
        => Assert.Empty(Compute(Doc(Active("a")), A(), States(s, "a")));

    [Fact]
    public void Compute_ActiveOnlyStarted_NotDrift()
        => Assert.Empty(Compute(
            Doc(Entry("a", "S1", JournalEntryStatus.Active, ("apply", "Started", Before))), A(), States(DetectionState.NotApplied, "a")));

    [Fact]
    public void Compute_ActiveFailedRollbackIncomplete_NotDrift()
        => Assert.Empty(Compute(
            Doc(Entry("a", "S1", JournalEntryStatus.Active,
                ("apply", "Started", Before), ("apply", nameof(TweakOutcome.FailedRollbackIncomplete), Before))),
            A(), States(DetectionState.Partial, "a")));

    [Fact]
    public void Compute_StateMissing_NotDrift()
        => Assert.Empty(Compute(Doc(Active("a")), A(), States(DetectionState.NotApplied)));

    [Fact]
    public void Compute_EmptyJournal_NotDrift()
        => Assert.Empty(Compute(Doc(), A(), States(DetectionState.NotApplied, "a")));

    [Theory]
    [InlineData(JournalEntryStatus.Undone)]
    [InlineData(JournalEntryStatus.Aborted)]
    public void Compute_InactiveEntry_NotDrift(JournalEntryStatus status)
        => Assert.Empty(Compute(Doc(Entry("a", "S1", status)), A(), States(DetectionState.NotApplied, "a")));

    [Fact]
    public void Compute_PendingReboot_ApplyAfterSince_NotDrift()
        => Assert.Empty(Compute(
            Doc(Entry("a", "S1", JournalEntryStatus.PendingReboot, ("apply", "PendingReboot", After))), A(), States(DetectionState.NotApplied, "a")));

    [Fact]
    public void Compute_PendingReboot_ApplyBeforeSince_Drift()
        => Assert.Equal(new[] { "a" }, Compute(
            Doc(Entry("a", "S1", JournalEntryStatus.PendingReboot, ("apply", "PendingReboot", Before))), A(), States(DetectionState.NotApplied, "a")));

    [Fact]
    public void Compute_PendingReboot_NoApplyEvent_NotDrift()
        => Assert.Empty(Compute(
            Doc(Entry("a", "S1", JournalEntryStatus.PendingReboot, ("detect", "Applied", Before))), A(), States(DetectionState.NotApplied, "a")));

    [Theory]
    [InlineData(-5)]
    [InlineData(5)]
    public void Compute_NeverOverlapsRebootPending(int minutes)
    {
        var tweak = TestTweaks.TwoValues("a") with { RequiresReboot = true };
        var doc = Doc(Entry("a", "S1", JournalEntryStatus.PendingReboot, ("apply", "PendingReboot", TimeSpan.FromMinutes(minutes))));
        var catalog = new Dictionary<string, TweakDefinition>(StringComparer.Ordinal) { ["a"] = tweak };

        var pending = RebootPending.Compute(doc, catalog, Boot);
        var drift = SettingsDrift.Compute(doc, new[] { tweak }, States(DetectionState.NotApplied, "a"), "S1", Boot);

        Assert.Empty(System.Linq.Enumerable.Intersect(pending, drift));
        Assert.Equal(1, System.Linq.Enumerable.Count(System.Linq.Enumerable.Concat(pending, drift), id => id == "a"));
    }

    [Fact]
    public void Compute_UserScope_OtherSid_NotDrift()
        => Assert.Empty(Compute(
            Doc(Active("a", "S2")), new[] { TestTweaks.TwoValues("a") with { Scope = TweakScope.User } }, States(DetectionState.NotApplied, "a"), "S1"));

    [Fact]
    public void Compute_UserScope_SidIgnoresCase_Drift()
        => Assert.Equal(new[] { "a" }, Compute(
            Doc(Active("a", "s-1-5-21-1000")), new[] { TestTweaks.TwoValues("a") with { Scope = TweakScope.User } },
            States(DetectionState.NotApplied, "a"), "S-1-5-21-1000"));

    [Fact]
    public void Compute_MachineScope_OtherSid_Drift()
        => Assert.Equal(new[] { "a" }, Compute(Doc(Active("a", "S2")), A(), States(DetectionState.NotApplied, "a"), "S1"));

    [Theory]
    [InlineData("app-removal")]
    [InlineData("app-install")]
    [InlineData("autostart")]
    public void Compute_EntryWithKind_NotDrift(string kind)
    {
        var entry = Active("a");
        entry.Kind = kind;
        Assert.Empty(Compute(Doc(entry), A(), States(DetectionState.NotApplied, "a")));
    }

    [Fact]
    public void Compute_EntryNotInTweaks_NotDrift()
        => Assert.Empty(Compute(Doc(Active("nie.ma")), A(), States(DetectionState.NotApplied, "a", "nie.ma")));

    [Fact]
    public void Compute_ActionInTweaks_Skipped()
        => Assert.Empty(Compute(
            Doc(Active("a")), new[] { TestTweaks.TwoValues("a") with { Kind = TweakKind.Action } }, States(DetectionState.NotApplied, "a")));

    [Fact]
    public void Compute_NotReversible_Drift()
        => Assert.Equal(new[] { "a" }, Compute(
            Doc(Active("a")), new[] { TestTweaks.TwoValues("a") with { Reversible = false } }, States(DetectionState.NotApplied, "a")));

    [Fact]
    public void Compute_NewestActiveEntryDecides()
        => Assert.Empty(Compute(
            Doc(Active("a"), Entry("a", "S1", JournalEntryStatus.PendingReboot, ("apply", "PendingReboot", After))),
            A(), States(DetectionState.NotApplied, "a")));

    [Fact]
    public void Compute_OrderFollowsTweaks()
        => Assert.Equal(new[] { "b", "a" }, Compute(
            Doc(Active("a"), Active("b")),
            new[] { TestTweaks.TwoValues("b"), TestTweaks.TwoValues("a") },
            States(DetectionState.NotApplied, "a", "b")));

    [Fact]
    public void Compute_NullArguments_Throw()
    {
        var states = States(DetectionState.NotApplied, "a");
        Assert.Throws<ArgumentNullException>(() => SettingsDrift.Compute(null!, A(), states, "S1", Boot));
        Assert.Throws<ArgumentNullException>(() => SettingsDrift.Compute(new JournalDocument(), null!, states, "S1", Boot));
        Assert.Throws<ArgumentNullException>(() => SettingsDrift.Compute(new JournalDocument(), A(), null!, "S1", Boot));
        Assert.Throws<ArgumentNullException>(() => SettingsDrift.Compute(new JournalDocument(), A(), states, null!, Boot));
    }
}
