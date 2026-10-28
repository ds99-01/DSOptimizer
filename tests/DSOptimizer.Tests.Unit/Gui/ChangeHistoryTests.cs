using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Catalog;
using DSOptimizer.Tests.Unit.Engine;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class ChangeHistoryTests
{
    private static readonly DateTimeOffset Boot = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan After = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Before = TimeSpan.FromMinutes(-5);
    private const string Me = "S-1-5-21-1";

    private static TweakDefinition Plain(string id) => TestTweaks.TwoValues(id) with { Title = "Tytuł " + id };

    private static TweakDefinition Reboot(string id) => Plain(id) with { RequiresReboot = true };

    private static TweakDefinition UserScoped(string id) => Plain(id) with { Scope = TweakScope.User };

    private static GuiCategory[] Cats(params TweakDefinition[] items)
        => new[] { new GuiCategory("kat", "Kategoria testowa", items) };

    private static JournalEntry Entry(
        string id,
        string sid,
        JournalEntryStatus status,
        TimeSpan appliedOffset,
        params (string Action, string Outcome, TimeSpan Offset)[] events)
    {
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            TweakId = id,
            UserSid = sid,
            Status = status,
            AppliedAt = Boot + appliedOffset,
        };
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

    private static IReadOnlyList<HistoryEntry> Build(JournalDocument doc, IReadOnlyList<GuiCategory> cats, Func<string, string?>? resolver = null)
        => ChangeHistory.Build(doc, cats, Boot, Me, resolver ?? (sid => "Konto-" + sid));

    [Fact]
    public void Build_StatesFromStatusAndReboot()
    {
        var doc = Doc(
            Entry("act", Me, JournalEntryStatus.Active, Before, ("apply", "Applied", Before)),
            Entry("pen", Me, JournalEntryStatus.PendingReboot, After, ("apply", "PendingReboot", After)),
            Entry("upen", Me, JournalEntryStatus.Undone, Before, ("apply", "Applied", Before), ("undo", "Undone", After)),
            Entry("und", Me, JournalEntryStatus.Undone, Before, ("apply", "Applied", Before), ("undo", "Undone", Before)),
            Entry("abo", Me, JournalEntryStatus.Aborted, Before, ("apply", "Started", Before), ("apply", "Failed", Before)));
        var cats = Cats(Plain("act"), Reboot("pen"), Reboot("upen"), Plain("und"), Plain("abo"));

        var states = Build(doc, cats).ToDictionary(e => e.TweakId, e => e.State);

        Assert.Equal(HistoryState.Active, states["act"]);
        Assert.Equal(HistoryState.PendingReboot, states["pen"]);
        Assert.Equal(HistoryState.UndonePendingReboot, states["upen"]);
        Assert.Equal(HistoryState.Undone, states["und"]);
        Assert.Equal(HistoryState.Aborted, states["abo"]);
    }

    [Fact]
    public void Build_PendingOnlyOnEntryWithLatestEvent()
    {
        var oldEntry = Entry("r", Me, JournalEntryStatus.Undone, Before, ("apply", "Applied", Before), ("undo", "Undone", Before));
        var newEntry = Entry("r", Me, JournalEntryStatus.Active, After, ("apply", "Applied", After));

        var result = Build(Doc(oldEntry, newEntry), Cats(Reboot("r"))).ToDictionary(e => e.EntryId, e => e.State);

        Assert.Equal(HistoryState.Undone, result[oldEntry.EntryId]);
        Assert.Equal(HistoryState.PendingReboot, result[newEntry.EntryId]);
    }

    [Fact]
    public void Build_AfterBoot_PendingRebootStatusShownActive()
    {
        var doc = Doc(Entry("r", Me, JournalEntryStatus.PendingReboot, Before, ("apply", "PendingReboot", Before)));

        var entry = Assert.Single(Build(doc, Cats(Reboot("r"))));

        Assert.Equal(HistoryState.Active, entry.State);
        Assert.True(entry.IsActive);
    }

    [Fact]
    public void Build_UnknownTweakAndEvents_ShownWithoutUndo()
    {
        var doc = Doc(Entry("stary.id", Me, JournalEntryStatus.Active, Before, ("foo", "Bar", Before)));

        var entry = Assert.Single(Build(doc, Cats(Plain("inny"))));

        Assert.Equal("stary.id", entry.Title);
        Assert.False(entry.InCatalog);
        Assert.Null(entry.CategoryName);
        Assert.Equal("nieznane", entry.AccountText);
        Assert.Equal("foo: Bar", Assert.Single(entry.Events).Text);
        Assert.Equal(HistoryText.UndoOutOfCatalog, entry.UndoBlockedReason);
    }

    [Fact]
    public void Build_EntryWithoutEvents_StateFromStatus()
    {
        var doc = Doc(
            Entry("a", Me, JournalEntryStatus.Active, Before),
            Entry("b", Me, JournalEntryStatus.Undone, Before),
            Entry("c", Me, JournalEntryStatus.Aborted, Before));

        var states = Build(doc, Cats(Reboot("a"), Reboot("b"), Reboot("c"))).ToDictionary(e => e.TweakId, e => e.State);

        Assert.Equal(HistoryState.Active, states["a"]);
        Assert.Equal(HistoryState.Undone, states["b"]);
        Assert.Equal(HistoryState.Aborted, states["c"]);
    }

    [Fact]
    public void Build_CatalogEntry_TitleAndCategoryFromCatalog()
    {
        var entry = Assert.Single(Build(Doc(Entry("a", Me, JournalEntryStatus.Active, Before)), Cats(Plain("a"))));

        Assert.Equal("Tytuł a", entry.Title);
        Assert.True(entry.InCatalog);
        Assert.Equal("Kategoria testowa", entry.CategoryName);
        Assert.Null(entry.UndoBlockedReason);
    }

    [Fact]
    public void Build_UserScope_OtherSid_UndoBlockedWithName()
    {
        var doc = Doc(Entry("u", "S-1-5-21-2", JournalEntryStatus.Active, Before));

        var entry = Assert.Single(Build(doc, Cats(UserScoped("u")), _ => "Jan"));

        Assert.Equal("Jan", entry.AccountText);
        Assert.Equal("Ta zmiana dotyczy konta Jan. Żeby ją cofnąć, uruchom program na tym koncie.", entry.UndoBlockedReason);
    }

    [Fact]
    public void Build_UserScope_SameSidDifferentCase_Undoable()
    {
        var doc = Doc(Entry("u", Me.ToLowerInvariant(), JournalEntryStatus.Active, Before));

        var entry = Assert.Single(Build(doc, Cats(UserScoped("u")), _ => "Ja"));

        Assert.Null(entry.UndoBlockedReason);
    }

    [Fact]
    public void Build_MachineScope_OtherSid_UndoableAndAccountWholeComputer()
    {
        var doc = Doc(Entry("m", "S-1-5-21-2", JournalEntryStatus.Active, Before));

        var entry = Assert.Single(Build(doc, Cats(Plain("m"))));

        Assert.Equal("cały komputer", entry.AccountText);
        Assert.Null(entry.UndoBlockedReason);
    }

    [Fact]
    public void Build_TwoActiveEntriesSameTweak_OnlyNewestUndoable()
    {
        var older = Entry("m", Me, JournalEntryStatus.Active, Before);
        var newer = Entry("m", Me, JournalEntryStatus.Active, After);

        var result = Build(Doc(older, newer), Cats(Plain("m"))).ToDictionary(e => e.EntryId);

        Assert.Equal(HistoryText.UndoNotNewest, result[older.EntryId].UndoBlockedReason);
        Assert.Null(result[newer.EntryId].UndoBlockedReason);
    }

    [Fact]
    public void Build_UndoneEntry_NoUndoReason()
    {
        var entry = Assert.Single(Build(Doc(Entry("m", Me, JournalEntryStatus.Undone, Before)), Cats(Plain("m"))));

        Assert.False(entry.IsActive);
        Assert.Null(entry.UndoBlockedReason);
    }

    [Fact]
    public void Build_ResolverCalledOncePerSid()
    {
        var doc = Doc(
            Entry("u", "S-1", JournalEntryStatus.Active, Before),
            Entry("u", "s-1", JournalEntryStatus.Undone, After),
            Entry("u", "S-2", JournalEntryStatus.Undone, After),
            Entry("m", "S-3", JournalEntryStatus.Active, After));
        var calls = new List<string>();

        Build(doc, Cats(UserScoped("u"), Plain("m")), sid =>
        {
            calls.Add(sid);
            return "x";
        });

        Assert.Equal(2, calls.Count);
        Assert.Contains("S-2", calls);
    }

    [Fact]
    public void Build_ResolverNullOrThrows_ShowsSid()
    {
        var doc = Doc(
            Entry("u", "S-null", JournalEntryStatus.Undone, Before),
            Entry("u", "S-empty", JournalEntryStatus.Undone, Before),
            Entry("u", "S-boom", JournalEntryStatus.Undone, Before));

        var accounts = Build(doc, Cats(UserScoped("u")), sid => sid switch
        {
            "S-null" => null,
            "S-empty" => " ",
            _ => throw new InvalidOperationException("boom"),
        }).Select(e => e.AccountText).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "S-boom", "S-empty", "S-null" }, accounts);
    }

    [Fact]
    public void Build_500Entries_NewestFirst()
    {
        var entries = new List<JournalEntry>();
        for (var i = 0; i < 500; i++)
        {
            entries.Add(Entry("m", Me, JournalEntryStatus.Undone, TimeSpan.FromMinutes(i / 2)));
        }

        var result = Build(Doc(entries.ToArray()), Cats(Plain("m")));

        Assert.Equal(500, result.Count);
        var expected = entries
            .Select((entry, index) => (entry, index))
            .OrderByDescending(p => p.entry.AppliedAt)
            .ThenByDescending(p => p.index)
            .Select(p => p.entry.EntryId)
            .ToArray();
        Assert.Equal(expected, result.Select(e => e.EntryId).ToArray());
        Assert.Equal(entries[499].EntryId, result[0].EntryId);
        Assert.Equal(entries[498].EntryId, result[1].EntryId);
    }

    [Fact]
    public void Build_EventFailureFlag_AndOrder()
    {
        var doc = Doc(Entry("m", Me, JournalEntryStatus.Aborted, Before,
            ("apply", "Started", Before), ("apply", "FailedRolledBack", Before), ("undo", "UndoIncomplete", Before), ("apply", "Refused", Before),
            ("apply", "Failed", Before), ("apply", "FailedRollbackIncomplete", Before), ("apply", "Applied", Before), ("apply", "Nonsens", Before)));

        var flags = Assert.Single(Build(doc, Cats(Plain("m")))).Events.Select(e => e.IsFailure).ToArray();

        Assert.Equal(new[] { false, true, true, true, true, true, false, false }, flags);
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ChangeHistory.Build(null!, Cats(), Boot, Me, _ => null));
        Assert.Throws<ArgumentNullException>(() => ChangeHistory.Build(new JournalDocument(), null!, Boot, Me, _ => null));
        Assert.Throws<ArgumentNullException>(() => ChangeHistory.Build(new JournalDocument(), Cats(), Boot, Me, null!));
        Assert.Throws<ArgumentNullException>(() => ChangeHistory.ActiveCount(null!));
    }

    [Theory]
    [InlineData("apply", "Started", null, "Zastosowanie rozpoczęte")]
    [InlineData("apply", "Applied", null, "Zastosowano")]
    [InlineData("apply", "PendingReboot", null, "Zastosowano – zadziała po restarcie")]
    [InlineData("apply", "Failed", null, "Zastosowanie: Błąd")]
    [InlineData("apply", "Skipped", null, "Zastosowanie: Pominięto")]
    [InlineData("undo", "Undone", null, "Cofnięto")]
    [InlineData("undo", "UndoIncomplete", null, "Cofnięcie: Cofnięcie niepełne")]
    [InlineData("detect", "Applied", null, "Po restarcie: zmiana aktywna")]
    [InlineData("detect", "Applied", "Zmiana aktywna po restarcie.", "Po restarcie: zmiana aktywna")]
    [InlineData("detect", "Applied", "", "Po restarcie: zmiana aktywna")]
    [InlineData("detect", "NotApplied", null, "detect: NotApplied")]
    [InlineData("foo", "Bar", null, "foo: Bar")]
    [InlineData("apply", "Nonsens", null, "apply: Nonsens")]
    [InlineData("undo", "99", null, "undo: 99")]
    [InlineData("apply", "Applied", "klucz zajęty", "Zastosowano – klucz zajęty")]
    [InlineData("foo", "Bar", "x", "foo: Bar – x")]
    public void Event_Texts(string action, string outcome, string? message, string expected)
        => Assert.Equal(expected, HistoryText.Event(action, outcome, message));

    [Theory]
    [InlineData(HistoryFilter.All, true, true, true, true, true)]
    [InlineData(HistoryFilter.Active, true, true, false, false, false)]
    [InlineData(HistoryFilter.PendingReboot, false, true, true, false, false)]
    [InlineData(HistoryFilter.Inactive, false, false, true, true, true)]
    public void Matches_Table(HistoryFilter filter, bool active, bool pending, bool undonePending, bool undone, bool aborted)
    {
        Assert.Equal(active, ChangeHistory.Matches(HistoryState.Active, filter));
        Assert.Equal(pending, ChangeHistory.Matches(HistoryState.PendingReboot, filter));
        Assert.Equal(undonePending, ChangeHistory.Matches(HistoryState.UndonePendingReboot, filter));
        Assert.Equal(undone, ChangeHistory.Matches(HistoryState.Undone, filter));
        Assert.Equal(aborted, ChangeHistory.Matches(HistoryState.Aborted, filter));
    }

    [Fact]
    public void Matches_UnknownFilter_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ChangeHistory.Matches(HistoryState.Active, (HistoryFilter)99));

    [Fact]
    public void ActiveCount_CountsIsActive()
    {
        var doc = Doc(
            Entry("a", Me, JournalEntryStatus.Active, Before),
            Entry("b", Me, JournalEntryStatus.PendingReboot, Before),
            Entry("c", Me, JournalEntryStatus.Undone, Before),
            Entry("d", Me, JournalEntryStatus.Aborted, Before));

        Assert.Equal(2, ChangeHistory.ActiveCount(Build(doc, Cats(Plain("a"), Plain("b"), Plain("c"), Plain("d")))));
    }

    [Fact]
    public void Date_Time_LocalFormat()
    {
        var at = new DateTimeOffset(2026, 3, 7, 22, 45, 9, TimeSpan.Zero);

        Assert.Equal(at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), HistoryText.Date(at));
        Assert.Equal(at.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture), HistoryText.Time(at));
    }

    [Fact]
    public void RowName_TitleKindStateAndDate()
    {
        var at = new DateTimeOffset(2026, 10, 20, 12, 5, 0, TimeSpan.Zero);

        Assert.Equal(
            $"RustDesk, Instalacja aplikacji, Nie zainstalowano – brak połączenia z GitHubem, {HistoryText.Date(at)}",
            HistoryText.RowName("RustDesk", "Instalacja aplikacji", "Nie zainstalowano – brak połączenia z GitHubem", at));
    }

    [Fact]
    public void Texts_Literal()
    {
        Assert.Equal("Aktywna", HistoryText.State(HistoryState.Active));
        Assert.Equal("Czeka na restart", HistoryText.State(HistoryState.PendingReboot));
        Assert.Equal("Cofnięta, czeka na restart", HistoryText.State(HistoryState.UndonePendingReboot));
        Assert.Equal("Cofnięta", HistoryText.State(HistoryState.Undone));
        Assert.Equal("Przerwana", HistoryText.State(HistoryState.Aborted));
        Assert.Equal("Wszystkie wpisy", HistoryText.FilterName(HistoryFilter.All));
        Assert.Equal("Aktywne", HistoryText.FilterName(HistoryFilter.Active));
        Assert.Equal("Czekające na restart", HistoryText.FilterName(HistoryFilter.PendingReboot));
        Assert.Equal("Cofnięte i przerwane", HistoryText.FilterName(HistoryFilter.Inactive));
        Assert.Equal("Historia zmian", HistoryText.PageTitle);
        Assert.Equal("Zmiany zrobione programem na tym komputerze, od najnowszej.", HistoryText.PageSubtitle);
        Assert.Equal("Program nie zmienił jeszcze niczego na tym komputerze.", HistoryText.Empty);
        Assert.Equal("Żaden wpis nie pasuje do filtra.", HistoryText.EmptyFiltered);
        Assert.Equal("Nie udało się odczytać historii zmian: brak dostępu", HistoryText.LoadFailed("brak dostępu"));
        Assert.Equal("pozycja spoza tego katalogu", HistoryText.OutOfCatalog);
        Assert.Equal("Tej pozycji nie ma w katalogu tej wersji programu, więc nie da się jej cofnąć.", HistoryText.UndoOutOfCatalog);
        Assert.Equal("Nowszy wpis tej pozycji jest aktywny – cofnięcie dotyczy tamtego wpisu.", HistoryText.UndoNotNewest);
        Assert.Equal(
            new[] { "Stan", "Konto", "Kategoria", "Zastosowano", "Przebieg", "Cofnij" },
            new[] { HistoryText.StateLabel, HistoryText.AccountLabel, HistoryText.CategoryLabel, HistoryText.AppliedLabel, HistoryText.EventsLabel, HistoryText.UndoButton });
        Assert.Equal(
            new[] { "Data", "Pozycja", "Filtr" },
            new[] { HistoryText.DateLabel, HistoryText.ItemLabel, HistoryText.FilterLabel });
        Assert.Throws<ArgumentOutOfRangeException>(() => HistoryText.State((HistoryState)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => HistoryText.FilterName((HistoryFilter)99));
    }

    // --- usunięcia aplikacji (Kind="app-removal") ---

    private static JournalEntry Removal(
        JournalEntryStatus status,
        TimeSpan appliedOffset,
        params (string Outcome, TimeSpan Offset)[] events)
    {
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = JournalEntry.AppRemovalKind,
            Title = "Wiadomości",
            TweakId = "appx:Microsoft.BingNews_8wekyb3d8bbwe",
            UserSid = Me,
            Status = status,
            AppliedAt = Boot + appliedOffset,
        };
        foreach (var (outcome, offset) in events)
        {
            entry.Events.Add(new JournalEvent { At = Boot + offset, Action = "remove-app", Outcome = outcome });
        }

        return entry;
    }

    private static HistoryEntry BuildRemoval(JournalEntry entry)
        => Assert.Single(Build(Doc(entry), Cats(Plain("a"))));

    [Fact]
    public void Build_AppRemoval_RemovedHasTitleCategoryNoUndo()
    {
        var item = BuildRemoval(Removal(JournalEntryStatus.Active, Before, ("Started", Before), ("Removed", Before)));

        Assert.Equal(HistoryState.Removed, item.State);
        Assert.Equal("Wiadomości", item.Title);
        Assert.Equal(HistoryText.AppsCategory, item.CategoryName);
        Assert.True(item.InCatalog);
        Assert.False(item.IsActive);
        Assert.Null(item.UndoBlockedReason);
        Assert.Equal("Konto-" + Me, item.AccountText);
        Assert.Equal(2, item.Events.Count);
        Assert.Equal("Usuwanie rozpoczęte", item.Events[0].Text);
        Assert.Equal("Usunięto i sprawdzono", item.Events[1].Text);
    }

    [Fact]
    public void Build_AppRemoval_FailedIsRemovalFailed()
    {
        var item = BuildRemoval(Removal(JournalEntryStatus.Aborted, Before, ("Started", Before), ("Failed", Before)));

        Assert.Equal(HistoryState.RemovalFailed, item.State);
        Assert.False(item.IsActive);
        Assert.True(item.Events[1].IsFailure);
    }

    [Theory]
    [InlineData("NotConfirmed")]
    [InlineData("Started")]
    [InlineData("Cokolwiek")]
    public void Build_AppRemoval_AbortedNotConfirmedOrInterrupted_IsRemovalNotConfirmed(string last)
    {
        var events = last == "Started" ? new[] { ("Started", Before) } : new[] { ("Started", Before), (last, Before) };
        Assert.Equal(HistoryState.RemovalNotConfirmed, BuildRemoval(Removal(JournalEntryStatus.Aborted, Before, events)).State);
    }

    [Fact]
    public void Build_AppRemoval_NoEvents_IsRemovalNotConfirmed()
        => Assert.Equal(HistoryState.RemovalNotConfirmed, BuildRemoval(Removal(JournalEntryStatus.Aborted, Before)).State);

    [Fact]
    public void Build_AppRemoval_PendingRebootAfterBoot_IsPendingReboot_BeforeBootFallsBackToOutcome()
    {
        Assert.Equal(
            HistoryState.PendingReboot,
            BuildRemoval(Removal(JournalEntryStatus.PendingReboot, After, ("Started", After), ("Removed", After))).State);
        Assert.Equal(
            HistoryState.PendingReboot,
            BuildRemoval(Removal(JournalEntryStatus.PendingReboot, After, ("Started", After), ("NotConfirmed", After))).State);
        Assert.Equal(
            HistoryState.Removed,
            BuildRemoval(Removal(JournalEntryStatus.PendingReboot, Before, ("Started", Before), ("Removed", Before))).State);
        Assert.Equal(
            HistoryState.RemovalNotConfirmed,
            BuildRemoval(Removal(JournalEntryStatus.PendingReboot, Before, ("Started", Before), ("NotConfirmed", Before))).State);
    }

    [Fact]
    public void Build_AppRemoval_DoesNotAffectTweakEntries()
    {
        var doc = Doc(
            Entry("a", Me, JournalEntryStatus.Active, Before, ("apply", "Applied", Before)),
            Removal(JournalEntryStatus.Active, After, ("Started", After), ("Removed", After)));

        var items = Build(doc, Cats(Plain("a")));

        Assert.Equal(2, items.Count);
        var tweak = items.Single(i => i.TweakId == "a");
        Assert.True(tweak.IsActive);
        Assert.Equal(HistoryState.Active, tweak.State);
    }

    [Fact]
    public void Matches_RemovalStatesAcrossFilters()
    {
        Assert.True(ChangeHistory.Matches(HistoryState.Removed, HistoryFilter.Active));
        Assert.False(ChangeHistory.Matches(HistoryState.Removed, HistoryFilter.PendingReboot));
        Assert.False(ChangeHistory.Matches(HistoryState.Removed, HistoryFilter.Inactive));
        Assert.True(ChangeHistory.Matches(HistoryState.Removed, HistoryFilter.All));
        Assert.True(ChangeHistory.Matches(HistoryState.PendingReboot, HistoryFilter.PendingReboot));
        foreach (var state in new[] { HistoryState.RemovalFailed, HistoryState.RemovalNotConfirmed })
        {
            Assert.True(ChangeHistory.Matches(state, HistoryFilter.Inactive));
            Assert.False(ChangeHistory.Matches(state, HistoryFilter.Active));
            Assert.False(ChangeHistory.Matches(state, HistoryFilter.PendingReboot));
        }
    }

    [Fact]
    public void ActiveCount_MatchesActiveFilter_IncludingRemoved()
    {
        var doc = Doc(
            Entry("a", Me, JournalEntryStatus.Active, Before, ("apply", "Applied", Before)),
            Removal(JournalEntryStatus.Active, Before, ("Started", Before), ("Removed", Before)),
            Removal(JournalEntryStatus.PendingReboot, After, ("Started", After), ("Removed", After)),
            Removal(JournalEntryStatus.Aborted, Before, ("Started", Before), ("Failed", Before)));

        var items = Build(doc, Cats(Plain("a")));

        Assert.Equal(3, ChangeHistory.ActiveCount(items));
        Assert.Equal(ChangeHistory.ActiveCount(items), items.Count(i => ChangeHistory.Matches(i.State, HistoryFilter.Active)));
    }

    // --- instalacje aplikacji (Kind="app-install") ---

    private static JournalEntry Install(
        JournalEntryStatus status,
        TimeSpan appliedOffset,
        params (string Outcome, TimeSpan Offset)[] events)
    {
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = JournalEntry.AppInstallKind,
            Title = "7-Zip",
            TweakId = "install.7zip",
            UserSid = Me,
            Status = status,
            AppliedAt = Boot + appliedOffset,
        };
        foreach (var (outcome, offset) in events)
        {
            entry.Events.Add(new JournalEvent { At = Boot + offset, Action = "install-app", Outcome = outcome, Message = "winget: 7zip.7zip" });
        }

        return entry;
    }

    private static HistoryEntry BuildInstall(JournalEntry entry)
        => Assert.Single(Build(Doc(entry), Cats(Plain("a"))));

    [Fact]
    public void InstallEntry_TitleCategoryNoUndo()
    {
        var item = BuildInstall(Install(JournalEntryStatus.Active, Before, ("Started", Before), ("Installed", Before)));

        Assert.Equal(HistoryState.Installed, item.State);
        Assert.Equal("7-Zip", item.Title);
        Assert.Equal("install.7zip", item.TweakId);
        Assert.Equal(HistoryText.InstallCategory, item.CategoryName);
        Assert.True(item.InCatalog);
        Assert.False(item.IsActive);
        Assert.Equal(HistoryText.InstallUndoBlocked, item.UndoBlockedReason);
        Assert.Equal("Konto-" + Me, item.AccountText);
        Assert.Equal(
            new[] { "Instalacja rozpoczęta – winget: 7zip.7zip", "Zainstalowano i sprawdzono – winget: 7zip.7zip" },
            item.Events.Select(e => e.Text));
        Assert.All(item.Events, e => Assert.False(e.IsFailure));
    }

    [Fact]
    public void InstallEntry_NoTitle_UsesId()
    {
        var entry = Install(JournalEntryStatus.Active, Before, ("Installed", Before));
        entry.Title = null;

        Assert.Equal("install.7zip", BuildInstall(entry).Title);
    }

    [Theory]
    [InlineData(JournalEntryStatus.Active, "InstalledUnverified", HistoryState.Installed)]
    [InlineData(JournalEntryStatus.Aborted, "AlreadyInstalled", HistoryState.Installed)]
    [InlineData(JournalEntryStatus.Aborted, "NotConfirmed", HistoryState.InstallNotConfirmed)]
    [InlineData(JournalEntryStatus.Aborted, "Started", HistoryState.InstallNotConfirmed)]
    [InlineData(JournalEntryStatus.Aborted, "Failed", HistoryState.InstallNotDone)]
    [InlineData(JournalEntryStatus.Aborted, "Cokolwiek", HistoryState.InstallNotConfirmed)]
    public void InstallEntry_States(JournalEntryStatus status, string last, HistoryState expected)
    {
        var events = last == "Started" ? new[] { ("Started", Before) } : new[] { ("Started", Before), (last, Before) };

        var item = BuildInstall(Install(status, Before, events));

        Assert.Equal(expected, item.State);
        Assert.False(item.IsActive);
        Assert.Equal(last == "Failed", item.Events[item.Events.Count - 1].IsFailure);
    }

    [Fact]
    public void InstallEntry_NoEvents_IsInstallNotConfirmed()
        => Assert.Equal(HistoryState.InstallNotConfirmed, BuildInstall(Install(JournalEntryStatus.Aborted, Before)).State);

    [Fact]
    public void InstallEntry_PendingRebootAfterBoot_IsPendingReboot_BeforeBootIsInstalled()
    {
        Assert.Equal(
            HistoryState.PendingReboot,
            BuildInstall(Install(JournalEntryStatus.PendingReboot, After, ("Started", After), ("Installed", After))).State);
        Assert.Equal(
            HistoryState.Installed,
            BuildInstall(Install(JournalEntryStatus.PendingReboot, Before, ("Started", Before), ("Installed", Before))).State);
    }

    [Fact]
    public void Matches_InstallStatesAcrossFilters()
    {
        Assert.True(ChangeHistory.Matches(HistoryState.Installed, HistoryFilter.Active));
        Assert.False(ChangeHistory.Matches(HistoryState.Installed, HistoryFilter.Inactive));
        Assert.False(ChangeHistory.Matches(HistoryState.Installed, HistoryFilter.PendingReboot));
        foreach (var state in new[] { HistoryState.InstallNotConfirmed, HistoryState.InstallNotDone })
        {
            Assert.True(ChangeHistory.Matches(state, HistoryFilter.Inactive));
            Assert.False(ChangeHistory.Matches(state, HistoryFilter.Active));
            Assert.False(ChangeHistory.Matches(state, HistoryFilter.PendingReboot));
            Assert.True(ChangeHistory.Matches(state, HistoryFilter.All));
        }
    }

    [Fact]
    public void InstallAndRemoval_BothShown_NewestFirst()
    {
        var doc = Doc(
            Install(JournalEntryStatus.Active, Before, ("Started", Before), ("Installed", Before)),
            Removal(JournalEntryStatus.Active, After, ("Started", After), ("Removed", After)));

        var items = Build(doc, Cats(Plain("a")));

        Assert.Equal(new[] { HistoryState.Removed, HistoryState.Installed }, items.Select(i => i.State));
        Assert.Equal(2, ChangeHistory.ActiveCount(items));
    }

    // --- rodzaj wpisu, ikona i powód ---

    [Fact]
    public void InstallFailed_HasKindIconAndShortReason()
    {
        var entry = Install(JournalEntryStatus.Aborted, Before, ("Started", Before), ("Failed", Before));
        entry.Events[1].Reason = "Nie wykonano – brak połączenia z GitHubem";
        var item = BuildInstall(entry);

        Assert.Equal(HistoryKind.AppInstall, item.Kind);
        Assert.Equal(GuiIcons.Install, item.IconKind);
        Assert.Equal(HistoryState.InstallNotDone, item.State);
        Assert.Equal("brak połączenia z GitHubem", item.Reason);
        Assert.Equal(
            new[] { "Instalacja rozpoczęta – winget: 7zip.7zip", "Nie wykonano – brak połączenia z GitHubem" },
            item.Events.Select(e => e.Text));
    }

    [Fact]
    public void InstallFailed_OldJournalWithoutReason_NullReason()
    {
        var item = BuildInstall(Install(JournalEntryStatus.Aborted, Before, ("Started", Before), ("Failed", Before)));

        Assert.Null(item.Reason);
    }

    [Fact]
    public void InstallSucceeded_NoReason()
    {
        var entry = Install(JournalEntryStatus.Active, Before, ("Started", Before), ("Installed", Before));
        entry.Events[1].Reason = "cokolwiek";

        Assert.Null(BuildInstall(entry).Reason);
    }

    [Fact]
    public void RemovalFailed_ReasonFromMessage()
    {
        var entry = Removal(JournalEntryStatus.Aborted, Before, ("Started", Before), ("Failed", Before));
        entry.Events[1].Message = "Nie wykonano – deinstalator zakończył się kodem 5";
        var item = BuildRemoval(entry);

        Assert.Equal(HistoryKind.AppRemoval, item.Kind);
        Assert.Equal(GuiIcons.Apps, item.IconKind);
        Assert.Equal("deinstalator zakończył się kodem 5", item.Reason);
    }

    [Fact]
    public void RemovalRemoved_NoReason()
    {
        var entry = Removal(JournalEntryStatus.Active, Before, ("Started", Before), ("Removed", Before));
        entry.Events[1].Message = "Usunięto";

        Assert.Null(BuildRemoval(entry).Reason);
    }

    [Fact]
    public void Tweak_HasChangeKindAndCategoryIcon()
    {
        var cats = new[] { new GuiCategory("privacy", "Prywatność i AI", new[] { Plain("a") }) };
        var item = Assert.Single(Build(
            Doc(Entry("a", Me, JournalEntryStatus.Active, Before, ("apply", "Applied", Before))), cats));

        Assert.Equal(HistoryKind.Change, item.Kind);
        Assert.Equal("Privacy", item.IconKind);
        Assert.Null(item.Reason);
    }

    [Fact]
    public void Tweak_OutOfCatalog_ToolsIcon()
    {
        var item = Assert.Single(Build(
            Doc(Entry("nie-ma", Me, JournalEntryStatus.Active, Before, ("apply", "Applied", Before))), Cats(Plain("a"))));

        Assert.Equal("Tools", item.IconKind);
    }

    [Fact]
    public void History_EntryWrittenInPolish_ShowsEnglishTitle()
    {
        var entry = Entry("a", Me, JournalEntryStatus.Active, Before, ("apply", "Applied", Before));
        entry.Title = "Tytuł a";
        var catalog = new LoadedCatalog(new[] { Plain("a") }, "sha256:x");
        using (L.UseTable(CatalogTranslationTests.DataTable(("Tytuł a", "Title a"))))
        using (L.Use(UiLanguage.English))
        {
            var translated = CatalogTranslation.Translate(catalog);

            var item = Assert.Single(Build(Doc(entry), Cats(translated.Definitions.ToArray())));

            Assert.Equal("Title a", item.Title);
            Assert.True(item.InCatalog);
        }
    }
}
