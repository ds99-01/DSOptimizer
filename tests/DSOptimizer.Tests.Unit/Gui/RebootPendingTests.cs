using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class RebootPendingTests
{
    private static readonly DateTimeOffset Boot = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static TweakDefinition Reboot(string id) => TestTweaks.TwoValues(id) with { RequiresReboot = true };

    private static Dictionary<string, TweakDefinition> Catalog(params TweakDefinition[] items)
        => items.ToDictionary(item => item.Id, StringComparer.Ordinal);

    private static JournalEntry Entry(string id, string sid, params (string Action, string Outcome, TimeSpan Offset)[] events)
    {
        var entry = new JournalEntry { TweakId = id, UserSid = sid };
        foreach (var (action, outcome, offset) in events)
        {
            entry.Events.Add(new JournalEvent { At = Boot + offset, Action = action, Outcome = outcome });
        }

        return entry;
    }

    private static JournalDocument Doc(params JournalEntry[] entries)
    {
        var doc = new JournalDocument();
        doc.Entries.AddRange(entries);
        return doc;
    }

    private static IReadOnlyList<string> Compute(JournalDocument doc, params TweakDefinition[] catalog)
        => RebootPending.Compute(doc, Catalog(catalog), Boot);

    private static readonly TimeSpan After = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Before = TimeSpan.FromMinutes(-5);

    [Fact]
    public void Compute_ApplyAfterBoot_Pending()
        => Assert.Equal(new[] { "a" }, Compute(Doc(Entry("a", "S1", ("apply", "Applied", After))), Reboot("a")));

    [Fact]
    public void Compute_ApplyBeforeBoot_NotPending()
        => Assert.Empty(Compute(Doc(Entry("a", "S1", ("apply", "Applied", Before))), Reboot("a")));

    [Fact]
    public void Compute_PendingRebootOutcome_Pending()
        => Assert.Equal(new[] { "a" }, Compute(Doc(Entry("a", "S1", ("apply", "PendingReboot", After))), Reboot("a")));

    [Fact]
    public void Compute_UndoAfterBoot_Pending()
        => Assert.Equal(new[] { "a" }, Compute(Doc(Entry("a", "S1", ("apply", "Applied", Before), ("undo", "Undone", After))), Reboot("a")));

    [Fact]
    public void Compute_FailedEvents_Ignored()
        => Assert.Empty(Compute(Doc(Entry("a", "S1", ("apply", "Started", After), ("apply", "FailedRolledBack", After), ("undo", "UndoIncomplete", After))), Reboot("a")));

    [Fact]
    public void Compute_DetectEventAfterBoot_Ignored()
        => Assert.Empty(Compute(Doc(Entry("a", "S1", ("apply", "PendingReboot", Before), ("detect", "Applied", After))), Reboot("a")));

    [Fact]
    public void Compute_NoRebootTweak_Ignored()
        => Assert.Empty(Compute(Doc(Entry("a", "S1", ("apply", "Applied", After))), TestTweaks.TwoValues("a")));

    [Fact]
    public void Compute_UnknownId_Ignored()
        => Assert.Empty(Compute(Doc(Entry("nie.ma", "S1", ("apply", "Applied", After))), Reboot("a")));

    [Fact]
    public void Compute_TwoUsersSameTweak_OneId()
        => Assert.Equal(new[] { "a" }, Compute(
            Doc(Entry("a", "S1", ("apply", "Applied", After)), Entry("a", "S2", ("apply", "Applied", After + After))), Reboot("a")));

    [Fact]
    public void Compute_OrderedByLatestEvent_Chronological()
        => Assert.Equal(new[] { "a", "b" }, Compute(
            Doc(Entry("b", "S1", ("apply", "Applied", After + After)), Entry("a", "S1", ("apply", "Applied", After))), Reboot("a"), Reboot("b")));

    [Fact]
    public void Compute_LastRelevantEventDecides_OlderAfterBootNewerBeforeIgnored()
        => Assert.Empty(Compute(Doc(Entry("a", "S1", ("apply", "Applied", Before), ("detect", "Applied", After))), Reboot("a")));

    [Fact]
    public void Compute_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => RebootPending.Compute(null!, Catalog(), Boot));
        Assert.Throws<ArgumentNullException>(() => RebootPending.Compute(new JournalDocument(), null!, Boot));
    }

    private static IReadOnlyDictionary<string, RebootPendingKind> Kinds(JournalDocument doc, params TweakDefinition[] catalog)
        => RebootPending.ComputeKinds(doc, Catalog(catalog), Boot);

    [Fact]
    public void ComputeKinds_ApplyAfterBoot_Applied()
        => Assert.Equal(RebootPendingKind.Applied, Kinds(Doc(Entry("a", "S1", ("apply", "PendingReboot", After))), Reboot("a"))["a"]);

    [Fact]
    public void ComputeKinds_UndoAfterBoot_Undone()
        => Assert.Equal(RebootPendingKind.Undone, Kinds(Doc(Entry("a", "S1", ("apply", "Applied", Before), ("undo", "Undone", After))), Reboot("a"))["a"]);

    [Fact]
    public void ComputeKinds_ApplyUndoApply_LatestApplied()
        => Assert.Equal(RebootPendingKind.Applied, Kinds(Doc(Entry(
            "a", "S1", ("apply", "Applied", After), ("undo", "Undone", After + After), ("apply", "Applied", After + After + After))), Reboot("a"))["a"]);

    [Fact]
    public void ComputeKinds_BeforeBoot_Absent()
        => Assert.Empty(Kinds(Doc(Entry("a", "S1", ("apply", "Applied", Before))), Reboot("a")));

    [Fact]
    public void Compute_SameIdsAsComputeKinds()
    {
        var doc = Doc(
            Entry("b", "S1", ("apply", "Applied", After + After)),
            Entry("a", "S1", ("undo", "Undone", After)),
            Entry("c", "S1", ("apply", "Applied", Before)));
        var catalog = new[] { Reboot("a"), Reboot("b"), Reboot("c") };

        Assert.Equal(Compute(doc, catalog), Kinds(doc, catalog).Keys.OrderBy(id => id, StringComparer.Ordinal).ToList());
        Assert.Equal(new[] { "a", "b" }, Compute(doc, catalog));
    }

    [Fact]
    public void PendingReboot_UndoneIds_SubsetWithUndoneKind()
    {
        var f = new EngineFixture();
        var doc = new JournalDocument();
        doc.Entries.Add(Entry("a", "S1", ("apply", "Applied", Before), ("undo", "Undone", After)));
        doc.Entries.Add(Entry("b", "S1", ("apply", "Applied", After)));
        f.Journal.Save(doc);

        var status = Session(f, Reboot("a"), Reboot("b")).PendingReboot(Boot, Boot);

        Assert.Equal(new[] { "a", "b" }, status.Ids);
        Assert.Equal(new[] { "a" }, status.UndoneIds);
    }

    [Fact]
    public void PendingReboot_JournalThrows_EmptyUndoneIds()
    {
        var f = new EngineFixture();
        f.Journal.LoadException = new JournalStoreException("brak dostępu");

        Assert.Empty(Session(f, Reboot("a")).PendingReboot(Boot, Boot).UndoneIds);
    }

    private static GuiSession Session(EngineFixture f, params TweakDefinition[] definitions)
        => new(new LoadedCatalog(definitions, "sha256:test"), f.CreateServices());

    private static void Seed(EngineFixture f, string id, TimeSpan offset)
    {
        var doc = new JournalDocument();
        doc.Entries.Add(Entry(id, "S1", ("apply", "Applied", offset)));
        f.Journal.Save(doc);
    }

    [Fact]
    public void Pending_UnknownBoot_UsesSessionStart()
    {
        var f = new EngineFixture();
        Seed(f, "a", After);
        var session = Session(f, Reboot("a"));

        Assert.Equal(new[] { "a" }, session.PendingReboot(null, Boot).Ids);
        Assert.Empty(session.PendingReboot(null, Boot + After + After).Ids);
    }

    [Fact]
    public void Pending_KnownBoot_IgnoresSessionStart()
    {
        var f = new EngineFixture();
        Seed(f, "a", After);
        var session = Session(f, Reboot("a"));

        Assert.Empty(session.PendingReboot(Boot + After + After, Boot).Ids);
    }

    [Fact]
    public void Pending_JournalThrows_EmptyWithWarning()
    {
        var f = new EngineFixture();
        f.Journal.LoadException = new JournalStoreException("brak dostępu");
        var status = Session(f, Reboot("a")).PendingReboot(Boot, Boot);

        Assert.Empty(status.Ids);
        Assert.Empty(status.Titles);
        Assert.Equal("Nie udało się sprawdzić, czy czeka restart: brak dostępu", status.Warning);
    }

    [Fact]
    public void Pending_JournalWarning_Passed()
    {
        var f = new EngineFixture();
        f.Journal.LoadWarning = "journal uszkodzony";
        Assert.Equal("journal uszkodzony", Session(f, Reboot("a")).PendingReboot(Boot, Boot).Warning);
    }

    [Fact]
    public void Pending_TitlesFromCatalog()
    {
        var f = new EngineFixture();
        Seed(f, "a", After);
        var status = Session(f, Reboot("a") with { Title = "Tytuł A" }).PendingReboot(Boot, Boot);

        Assert.Equal(new[] { "a" }, status.Ids);
        Assert.Equal(new[] { "Tytuł A" }, status.Titles);
        Assert.Null(status.Warning);
    }

    [Fact]
    public void Text_RebootConstants()
    {
        Assert.Equal("Wymagany restart.", GuiText.RebootTitle);
        Assert.Equal("Komputer uruchomi się ponownie. Zapisz pracę w otwartych programach.", GuiText.RestartConfirmText);
    }

    // --- usunięcia aplikacji ---

    private static JournalEntry Removal(string id, string title, JournalEntryStatus status, params (string Outcome, TimeSpan Offset)[] events)
    {
        var entry = new JournalEntry { Kind = JournalEntry.AppRemovalKind, Title = title, TweakId = id, UserSid = "S1", Status = status };
        foreach (var (outcome, offset) in events)
        {
            entry.Events.Add(new JournalEvent { At = Boot + offset, Action = "remove-app", Outcome = outcome });
        }

        return entry;
    }

    [Fact]
    public void ComputeAppRemovals_PendingAfterBoot_ReturnsTitles_DisappearsAfterNextBoot()
    {
        var doc = Doc(Removal("appx:A", "Aplikacja A", JournalEntryStatus.PendingReboot, ("Started", After), ("Removed", After)));

        var pending = RebootPending.ComputeAppRemovals(doc, Boot);

        Assert.Equal(new[] { "Aplikacja A" }, pending.Select(e => e.Title));
        Assert.Empty(RebootPending.ComputeAppRemovals(doc, Boot + TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ComputeAppRemovals_NotConfirmedWithReboot_Counts()
        => Assert.Single(RebootPending.ComputeAppRemovals(
            Doc(Removal("win32:x", "Program", JournalEntryStatus.PendingReboot, ("Started", After), ("NotConfirmed", After))), Boot));

    [Fact]
    public void ComputeAppRemovals_OtherStatusesOrBeforeBoot_Ignored()
    {
        var doc = Doc(
            Removal("appx:A", "A", JournalEntryStatus.Active, ("Started", After), ("Removed", After)),
            Removal("appx:B", "B", JournalEntryStatus.Aborted, ("Started", After), ("Failed", After)),
            Removal("appx:C", "C", JournalEntryStatus.PendingReboot, ("Started", Before), ("Removed", Before)),
            Removal("appx:D", "D", JournalEntryStatus.PendingReboot));

        Assert.Empty(RebootPending.ComputeAppRemovals(doc, Boot));
    }

    [Fact]
    public void ComputeAppRemovals_OrderedByEventTime_AndTweakApisIgnoreRemovals()
    {
        var late = Removal("appx:L", "Późna", JournalEntryStatus.PendingReboot, ("Removed", After + After));
        var early = Removal("appx:E", "Wczesna", JournalEntryStatus.PendingReboot, ("Removed", After));
        var doc = Doc(late, early, Entry("a", "S1", ("apply", "Applied", After)));

        Assert.Equal(new[] { "Wczesna", "Późna" }, RebootPending.ComputeAppRemovals(doc, Boot).Select(e => e.Title));
        Assert.Equal(new[] { "a" }, Compute(doc, Reboot("a")));
        Assert.Equal(new[] { "a" }, RebootPending.ComputeKinds(doc, Catalog(Reboot("a")), Boot).Keys);
    }

    [Fact]
    public void Compute_RemovalEntryWithCatalogId_IsNotATweak()
    {
        var entry = Removal("a", "Zła kolizja", JournalEntryStatus.PendingReboot, ("Applied", After));
        entry.Events[0].Action = "apply";

        Assert.Empty(Compute(Doc(entry), Reboot("a")));
    }

    // --- instalacje aplikacji ---

    private static JournalEntry Install(string id, string title, JournalEntryStatus status, params (string Outcome, TimeSpan Offset)[] events)
    {
        var entry = new JournalEntry { Kind = JournalEntry.AppInstallKind, Title = title, TweakId = id, UserSid = "S1", Status = status };
        foreach (var (outcome, offset) in events)
        {
            entry.Events.Add(new JournalEvent { At = Boot + offset, Action = "install-app", Outcome = outcome });
        }

        return entry;
    }

    [Fact]
    public void ComputeAppInstalls_PendingAfterBoot_DisappearsAfterNextBoot()
    {
        var doc = Doc(Install("install.7zip", "7-Zip", JournalEntryStatus.PendingReboot, ("Started", After), ("Installed", After)));

        Assert.Equal(new[] { "7-Zip" }, RebootPending.ComputeAppInstalls(doc, Boot).Select(e => e.Title));
        Assert.Empty(RebootPending.ComputeAppInstalls(doc, Boot + TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ComputeAppInstalls_OtherStatusesOrBeforeBoot_Ignored()
    {
        var doc = Doc(
            Install("install.a", "A", JournalEntryStatus.Active, ("Started", After), ("Installed", After)),
            Install("install.b", "B", JournalEntryStatus.Aborted, ("Started", After), ("Failed", After)),
            Install("install.c", "C", JournalEntryStatus.PendingReboot, ("Started", Before), ("Installed", Before)),
            Install("install.d", "D", JournalEntryStatus.PendingReboot));

        Assert.Empty(RebootPending.ComputeAppInstalls(doc, Boot));
    }

    [Fact]
    public void ComputeAppInstalls_OrderedByEventTime_RemovalsAndInstallsSeparate()
    {
        var late = Install("install.l", "Późna", JournalEntryStatus.PendingReboot, ("Installed", After + After));
        var early = Install("install.e", "Wczesna", JournalEntryStatus.PendingReboot, ("InstalledUnverified", After));
        var removal = Removal("appx:R", "Usunięta", JournalEntryStatus.PendingReboot, ("Removed", After));
        var doc = Doc(late, early, removal);

        Assert.Equal(new[] { "Wczesna", "Późna" }, RebootPending.ComputeAppInstalls(doc, Boot).Select(e => e.Title));
        Assert.Equal(new[] { "Usunięta" }, RebootPending.ComputeAppRemovals(doc, Boot).Select(e => e.Title));
    }

    [Fact]
    public void ComputeAppInstalls_EventOfOtherAction_Ignored()
    {
        var entry = Install("install.x", "X", JournalEntryStatus.PendingReboot, ("Installed", After));
        entry.Events[0].Action = "remove-app";

        Assert.Empty(RebootPending.ComputeAppInstalls(Doc(entry), Boot));
        Assert.Throws<ArgumentNullException>(() => RebootPending.ComputeAppInstalls(null!, Boot));
    }
}
