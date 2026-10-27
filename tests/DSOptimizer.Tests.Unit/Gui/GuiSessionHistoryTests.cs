using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class GuiSessionHistoryTests
{
    private static readonly DateTimeOffset Boot = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private const string Me = "S-1-5-21-1000";

    private sealed class Names : IAccountNames
    {
        private readonly Func<string, string?> _resolve;

        public Names(Func<string, string?> resolve) => _resolve = resolve;

        public string? NameOf(string sid) => _resolve(sid);
    }

    private static TweakDefinition UserTweak() => TestTweaks.TwoValues("hist.user") with { Scope = TweakScope.User, Title = "Tytuł" };

    private static GuiSession Session(EngineFixture f)
        => new(new LoadedCatalog(new[] { UserTweak() }, "sha256:test"), f.CreateServices());

    private static void AddEntry(EngineFixture f, string sid, DateTimeOffset appliedAt)
    {
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            TweakId = "hist.user",
            UserSid = sid,
            Status = JournalEntryStatus.Active,
            AppliedAt = appliedAt,
        };
        entry.Events.Add(new JournalEvent { At = appliedAt, Action = "apply", Outcome = "Applied" });
        var doc = new JournalDocument();
        doc.Entries.Add(entry);
        f.Journal.Save(doc);
    }

    [Fact]
    public void LoadHistory_ReadsJournalAndCurrentSid()
    {
        var f = new EngineFixture();
        f.User.Sid = Me;
        AddEntry(f, Me, Boot);

        var snapshot = Session(f).LoadHistory(Boot, Boot, new Names(sid => "Ola"));

        Assert.Null(snapshot.Warning);
        var entry = Assert.Single(snapshot.Entries);
        Assert.Equal("Ola", entry.AccountText);
        Assert.True(entry.IsActive);
        Assert.Null(entry.UndoBlockedReason);
    }

    [Fact]
    public void LoadHistory_OtherAccount_BlocksUndo()
    {
        var f = new EngineFixture();
        f.User.Sid = Me;
        AddEntry(f, "S-1-5-21-2000", Boot);

        var snapshot = Session(f).LoadHistory(Boot, Boot, new Names(sid => "Ktos"));

        Assert.NotNull(Assert.Single(snapshot.Entries).UndoBlockedReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadHistory_JournalThrows_EmptyWithWarning(bool contractException)
    {
        var f = new EngineFixture();
        if (contractException)
        {
            f.Journal.FailLoad = true;
        }
        else
        {
            f.Journal.LoadException = new InvalidOperationException("boom");
        }

        var snapshot = Session(f).LoadHistory(Boot, Boot, new Names(_ => null));

        Assert.Empty(snapshot.Entries);
        Assert.StartsWith("Nie udało się odczytać historii zmian: ", snapshot.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadHistory_LoadWarning_Passed()
    {
        var f = new EngineFixture();
        f.Journal.LoadWarning = "uszkodzony plik odłożony";
        AddEntry(f, Me, Boot);

        var snapshot = Session(f).LoadHistory(Boot, Boot, new Names(_ => null));

        Assert.Equal("uszkodzony plik odłożony", snapshot.Warning);
        Assert.Single(snapshot.Entries);
    }

    [Fact]
    public void LoadHistory_UnknownBoot_UsesSessionStart()
    {
        var f = new EngineFixture();
        f.User.Sid = Me;
        var tweak = TestTweaks.TwoValues("hist.user") with { Scope = TweakScope.User, RequiresReboot = true };
        var session = new GuiSession(new LoadedCatalog(new[] { tweak }, "sha256:test"), f.CreateServices());
        var entry = new JournalEntry { EntryId = Guid.NewGuid(), TweakId = "hist.user", UserSid = Me, Status = JournalEntryStatus.PendingReboot, AppliedAt = Boot };
        entry.Events.Add(new JournalEvent { At = Boot, Action = "apply", Outcome = "PendingReboot" });
        var doc = new JournalDocument();
        doc.Entries.Add(entry);
        f.Journal.Save(doc);

        var afterSessionStart = session.LoadHistory(null, Boot.AddMinutes(10), new Names(_ => "Ola"));
        var beforeSessionStart = session.LoadHistory(null, Boot.AddMinutes(-10), new Names(_ => "Ola"));

        Assert.Equal(HistoryState.Active, Assert.Single(afterSessionStart.Entries).State);
        Assert.Equal(HistoryState.PendingReboot, Assert.Single(beforeSessionStart.Entries).State);
    }

    [Fact]
    public void LoadHistory_NameResolverThrows_ShowsSid()
    {
        var f = new EngineFixture();
        f.User.Sid = Me;
        AddEntry(f, Me, Boot);

        var snapshot = Session(f).LoadHistory(Boot, Boot, new Names(_ => throw new InvalidOperationException("lsa")));

        Assert.Null(snapshot.Warning);
        Assert.Equal(Me, Assert.Single(snapshot.Entries).AccountText);
    }

    [Fact]
    public async Task LoadHistory_DuringOperation_WaitsForLock()
    {
        var f = new EngineFixture();
        var toggle = new ToggleScript();
        toggle.Register(f.Scripts);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Scripts.On("APPLY", _ =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
            toggle.Applied = true;
            return FakeScriptRunner.Result(0);
        });
        var session = new GuiSession(new LoadedCatalog(new[] { TestTweaks.Scripted() }, "sha256:test"), f.CreateServices());

        var running = Task.Run(() => session.Run("test.script", GuiOperation.Apply, confirmed: true));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
        var loading = Task.Run(() => session.LoadHistory(Boot, Boot, new Names(_ => null)));

        Assert.NotSame(loading, await Task.WhenAny(loading, Task.Delay(300)));

        release.Set();
        await running;
        var snapshot = await loading;
        Assert.Null(snapshot.Warning);
        Assert.NotEmpty(snapshot.Entries);
    }

    [Fact]
    public void PendingReboot_IncludesAppRemovalTitles_WithoutIds()
    {
        var f = new EngineFixture();
        f.User.Sid = Me;
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = JournalEntry.AppRemovalKind,
            Title = "Wiadomości",
            TweakId = "appx:Microsoft.BingNews_8wekyb3d8bbwe",
            UserSid = Me,
            Status = JournalEntryStatus.PendingReboot,
            AppliedAt = Boot + TimeSpan.FromMinutes(5),
        };
        entry.Events.Add(new JournalEvent { At = Boot + TimeSpan.FromMinutes(5), Action = "remove-app", Outcome = "Removed" });
        var doc = new JournalDocument();
        doc.Entries.Add(entry);
        f.Journal.Save(doc);
        var session = Session(f);

        var status = session.PendingReboot(Boot, Boot);

        Assert.Empty(status.Ids);
        Assert.Empty(status.UndoneIds);
        Assert.Equal(new[] { "Wiadomości" }, status.Titles);
        Assert.Empty(session.PendingReboot(Boot + TimeSpan.FromMinutes(10), Boot).Titles);
    }

    [Fact]
    public void PendingReboot_IncludesAppInstallTitles_WithoutIds()
    {
        var f = new EngineFixture();
        f.User.Sid = Me;
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = JournalEntry.AppInstallKind,
            Title = "7-Zip",
            TweakId = "install.7zip",
            UserSid = Me,
            Status = JournalEntryStatus.PendingReboot,
            AppliedAt = Boot + TimeSpan.FromMinutes(5),
        };
        entry.Events.Add(new JournalEvent { At = Boot + TimeSpan.FromMinutes(5), Action = "install-app", Outcome = "Installed" });
        var doc = new JournalDocument();
        doc.Entries.Add(entry);
        f.Journal.Save(doc);
        var session = Session(f);

        var status = session.PendingReboot(Boot, Boot);

        Assert.Empty(status.Ids);
        Assert.Equal(new[] { "7-Zip" }, status.Titles);
        Assert.Empty(session.PendingReboot(Boot + TimeSpan.FromMinutes(10), Boot).Titles);
    }
}
