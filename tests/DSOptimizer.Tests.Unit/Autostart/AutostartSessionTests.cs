using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartSessionTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";

    private static readonly AutostartLocations Locations = AutostartLocations.ForUser(Sid);

    private static readonly AutostartKnownCatalog Empty = AutostartKnownCatalog.Load("{ \"entries\": [] }");

    private static readonly DateTimeOffset MeasuredAt = new(2026, 11, 2, 7, 14, 0, TimeSpan.Zero);

    /// <summary>Wartość <c>StartupApproved</c> pozycji wyłączonej (bit 0 = 1, FILETIME niezerowy).</summary>
    private static readonly byte[] DisabledValue = { 0x03, 0, 0, 0, 0x00, 0x6C, 0x1B, 0x4F, 0x9F, 0x6A, 0xDD, 0x01 };

    private sealed class Fixture
    {
        public Fixture(TimeSpan? readTimeout = null, IAutostartReader? reader = null)
        {
            Engine.User.Sid = Sid;
            Engine.User.IsCertain = true;
            Engine.Clock.UtcNow = new DateTimeOffset(2026, 11, 2, 8, 37, 53, TimeSpan.Zero);
            Engine.Registry.CreateKey(Locations.UserRoot);
            Engine.Registry.CreateKey(Locations.UserClassesRoot);
            Gui = new GuiSession(new LoadedCatalog(new[] { TestTweaks.Scripted() }, "sha256:test"), Engine.CreateServices());
            AutostartEngine = new AutostartEngine(Engine.Registry, Engine.Journal, Engine.Log, Engine.Clock);
            Session = new AutostartSession(
                Gui, reader ?? Reader, AutostartEngine, Empty, Engine.User, Sid, Engine.Registry, Engine.Log, Engine.Clock, readTimeout);
        }

        public EngineFixture Engine { get; } = new();

        public FakeAutostartReader Reader { get; } = new();

        public GuiSession Gui { get; }

        public AutostartEngine AutostartEngine { get; }

        public AutostartSession Session { get; }

        public FakeRegistry Registry => Engine.Registry;

        public AutostartEntry Entry(AutostartSource source, string name, string? publisher = "Producent")
        {
            var current = AutostartSwitchReader.Read(
                Registry, AutostartSwitchKind.StartupApproved, Locations.StartupApprovedKey(source), name, enabledInManifest: false);
            return new AutostartEntry
            {
                Id = AutostartIds.For(source, name),
                Source = source,
                EntryName = name,
                Name = name + " app",
                Location = "loc",
                FileStatus = AutostartFileStatus.Exists,
                Switch = current,
                Publisher = publisher,
            };
        }

        /// <summary>Wiersze z odczytu strony (ta sama droga co w oknie).</summary>
        public IReadOnlyList<AutostartRow> Load(params AutostartEntry[] entries)
        {
            Reader.Reading = Reading(entries);
            var page = Session.LoadAsync().GetAwaiter().GetResult();
            return page.List!.Rows;
        }
    }

    /// <summary>Czytnik, który czeka na sygnał (odczyt zawieszony).</summary>
    private sealed class HangingReader : IAutostartReader, IDisposable
    {
        public ManualResetEventSlim Release { get; } = new();

        public AutostartReading Read(AutostartContext context)
        {
            Release.Wait(TimeSpan.FromSeconds(30));
            return Reading(Array.Empty<AutostartEntry>());
        }

        public void Dispose() => Release.Dispose();
    }

    private static AutostartReading Reading(IReadOnlyList<AutostartEntry> entries, DateTimeOffset? measuredAt = null)
        => new(entries, UserEnvironment.FromVariables(new Dictionary<string, string>()), measuredAt, new[] { "ostrzeżenie odczytu (test)" });

    private static string ApprovedKey(AutostartSource source) => Locations.StartupApprovedKey(source);

    /// <summary>Próba blokady sesji z osobnego wątku (Task.Run z czekaniem może wykonać się na bieżącym wątku, a blokada jest wielowejściowa).</summary>
    private static bool LockFreeOnOtherThread(GuiSession gui)
    {
        var free = true;
        var thread = new Thread(() => free = gui.TryRunExclusive(() => 0, out _));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        return free;
    }

    private static AutostartRow Row(IReadOnlyList<AutostartRow> rows, string name) => rows.Single(row => row.Entry.EntryName == name);

    private static string Fact(SeriesConfirmation confirmation, string name) => confirmation.Facts.Single(fact => fact.Name == name).Value;

    private static HistoryEntry History(Guid entryId, string tweakId, string title = "Autostart: wyłączono OneDrive app") => new(
        entryId,
        tweakId,
        title,
        true,
        "Autostart",
        DateTimeOffset.UtcNow,
        "Dawid",
        HistoryState.Active,
        Array.Empty<HistoryEvent>(),
        true,
        null,
        HistoryKind.Autostart,
        GuiIcons.Autostart);

    [Fact]
    public async Task LoadAsync_ReturnsRowsAndMeasuredAt()
    {
        var f = new Fixture();
        f.Reader.Reading = Reading(new[] { f.Entry(AutostartSource.RunUser, "OneDrive") }, MeasuredAt);

        var page = await f.Session.LoadAsync();
        var again = await f.Session.LoadAsync();

        Assert.Null(page.Error);
        Assert.Equal(f.Engine.Clock.UtcNow, page.ReadAt);
        var row = Assert.Single(page.List!.Rows);
        Assert.Equal("run:user:OneDrive", row.Id);
        Assert.True(row.CanToggle);
        Assert.Equal(MeasuredAt, page.List.MeasuredAt);
        Assert.Equal(new[] { "ostrzeżenie odczytu (test)" }, page.List.Warnings);
        Assert.Null(again.Error);
        Assert.Equal(Sid, f.Session.UserSid);
        // Kontekst konta jest świeży przy każdym odczycie (gałęzie HKU sprawdzane od nowa).
        Assert.Equal(2, f.Reader.Contexts.Count);
        Assert.NotSame(f.Reader.Contexts[0], f.Reader.Contexts[1]);
        Assert.All(f.Reader.Contexts, context => Assert.Equal(Sid, context.UserSid));
        Assert.All(f.Reader.Contexts, context => Assert.True(context.UserHiveLoaded));
        Assert.Equal(TimeSpan.FromSeconds(90), AutostartSession.DefaultReadTimeout);
    }

    [Fact]
    public async Task LoadAsync_ReaderHangs_ErrorAfterTimeout()
    {
        using var reader = new HangingReader();
        var f = new Fixture(TimeSpan.FromMilliseconds(200), reader);

        var page = await f.Session.LoadAsync();
        reader.Release.Set();

        Assert.Null(page.List);
        Assert.Equal(AutostartText.ReadFailed, page.Error);
        Assert.Equal(f.Engine.Clock.UtcNow, page.ReadAt);
        var line = Assert.Single(f.Engine.Log.Lines);
        Assert.StartsWith("WARN [Autostart] Odczyt „lista autostartu” trwał ponad 0.2 s", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ReaderThrows_Error()
    {
        var f = new Fixture();
        f.Reader.Failure = new InvalidOperationException("rejestr (test)");

        var page = await f.Session.LoadAsync();

        Assert.Null(page.List);
        Assert.Equal("Nie udało się odczytać listy autostartu.", page.Error);
        Assert.Contains(f.Engine.Log.Lines, line => line.StartsWith("WARN [Autostart] ", StringComparison.Ordinal) && line.Contains("rejestr (test)"));
    }

    [Fact]
    public void ConfirmationFor_Disable_FactsAndMachineWarning()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"), f.Entry(AutostartSource.RunMachine, "Teams", publisher: null));

        var confirmation = f.Session.ConfirmationFor(rows, AutostartState.Disabled);

        Assert.Equal(GuiOperation.DisableAutostart, confirmation.Operation);
        Assert.Equal("Wyłącz w autostarcie", confirmation.Title);
        Assert.Equal("2 pozycje", confirmation.Subtitle);
        Assert.Equal("Wyłącz (2)", confirmation.PrimaryText);
        Assert.False(confirmation.CreatesRestorePoint);
        Assert.False(confirmation.RestorePointRequired);
        Assert.Empty(confirmation.Warnings);
        Assert.Equal(
            new[] { ("Działa od", "Następnego logowania"), ("Punkt przywracania", "Niepotrzebny – zmianę cofniesz w Historii zmian"), ("Cofanie", "W Historii zmian") },
            confirmation.Facts.Select(fact => (fact.Name, fact.Value)));
        var user = confirmation.Items[0];
        Assert.Equal("run:user:OneDrive", user.Id);
        Assert.Equal("OneDrive app", user.Title);
        Assert.Equal(RiskLevel.Safe, user.Risk);
        Assert.Equal("Producent · to konto", user.Summary);
        Assert.Equal(new[] { "Program zostaje zainstalowany, tylko przestaje startować sam." }, user.Notes);
        Assert.Empty(user.Warnings);
        Assert.Empty(user.Technical);
        var machine = confirmation.Items[1];
        Assert.Equal("wszystkie konta", machine.Summary);
        Assert.Equal(new[] { "Dotyczy wszystkich kont na tym komputerze." }, machine.Warnings);
    }

    [Fact]
    public void ConfirmationFor_Enable_Notes()
    {
        var f = new Fixture();
        f.Registry.Set(ApprovedKey(AutostartSource.RunUser), "OneDrive", RegValue.FromBinary(DisabledValue));
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"));

        var confirmation = f.Session.ConfirmationFor(rows, AutostartState.Enabled);

        Assert.Equal(GuiOperation.EnableAutostart, confirmation.Operation);
        Assert.Equal("Włącz w autostarcie", confirmation.Title);
        Assert.Equal("1 pozycja", confirmation.Subtitle);
        Assert.Equal("Włącz (1)", confirmation.PrimaryText);
        Assert.Equal("Następnego logowania", Fact(confirmation, "Działa od"));
        var item = Assert.Single(confirmation.Items);
        Assert.Equal(new[] { "Program znowu będzie startował sam po zalogowaniu." }, item.Notes);
        Assert.Empty(item.Warnings);
    }

    [Fact]
    public void ConfirmationFor_RowCannotToggle_Throws()
    {
        var f = new Fixture();
        var row = Row(f.Load(f.Entry(AutostartSource.RunUser, "OneDrive")), "OneDrive");
        var blocked = row with { Blocker = AutostartBlocker.UserUncertain };

        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(new[] { row, blocked }, AutostartState.Disabled));
        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(Array.Empty<AutostartRow>(), AutostartState.Disabled));
        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(null!, AutostartState.Disabled));
        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(new AutostartRow[] { null! }, AutostartState.Disabled));
    }

    [Fact]
    public void ConfirmationFor_WrongState_Throws()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"));

        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(rows, AutostartState.Enabled));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.Session.ConfirmationFor(rows, AutostartState.Unknown));
    }

    [Fact]
    public void Set_UnderLock_ReportLines()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"), f.Entry(AutostartSource.RunMachine, "Teams"));
        bool? lockFree = null;
        f.Registry.AfterWrite = (_, _, _) => lockFree ??= LockFreeOnOtherThread(f.Gui);
        var steps = new ListProgress<BatchStepEvent>();

        var report = f.Session.Set(rows, AutostartState.Disabled, new AutostartRunOptions { Steps = steps });

        Assert.False(lockFree);
        Assert.Equal(GuiOperation.DisableAutostart, report.Operation);
        Assert.Equal("autostart", report.TweakId);
        Assert.Equal(2, report.Batch!.Results.Count);
        Assert.All(report.Batch.Results, result => Assert.Equal(TweakOutcome.Applied, result.Outcome));
        Assert.Equal(new[] { "run:user:OneDrive", "run:machine:Teams" }, report.SucceededIds);
        Assert.Equal(4, steps.Items.Count);
        Assert.Equal(
            new[]
            {
                (GuiLogLevel.Info, "OneDrive app: Wyłączono i sprawdzono – Wyłączona – zadziała od następnego logowania."),
                (GuiLogLevel.Info, "Teams app: Wyłączono i sprawdzono – Wyłączona – zadziała od następnego logowania."),
                (GuiLogLevel.Info, "Wyłączono 2 z 2."),
            },
            report.Lines.Select(line => (line.Level, line.Text)));
        Assert.All(report.Lines, line => Assert.Equal(f.Engine.Clock.UtcNow, line.At));
    }

    [Fact]
    public void Set_EnableAndOutcomes_HonestSummary()
    {
        var f = new Fixture();
        f.Registry.Set(ApprovedKey(AutostartSource.RunUser), "OneDrive", RegValue.FromBinary(DisabledValue));
        f.Registry.Set(ApprovedKey(AutostartSource.RunUser), "Steam", RegValue.FromBinary(DisabledValue));
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"), f.Entry(AutostartSource.RunUser, "Steam"));
        // Między odczytem strony a kliknięciem Steam włączono poza programem: silnik widzi to świeżym odczytem.
        f.Registry.Set(ApprovedKey(AutostartSource.RunUser), "Steam", RegValue.FromBinary(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));

        var report = f.Session.Set(rows, AutostartState.Enabled, new AutostartRunOptions());

        Assert.Equal(GuiOperation.EnableAutostart, report.Operation);
        Assert.Equal(
            new[]
            {
                (GuiLogLevel.Info, "OneDrive app: Włączono i sprawdzono – Włączona – zadziała od następnego logowania."),
                (GuiLogLevel.Info, "Steam app: Bez zmian – Była już włączona – nic nie zmieniono."),
                (GuiLogLevel.Info, "Włączono 1 z 2."),
            },
            report.Lines.Select(line => (line.Level, line.Text)));
        Assert.Equal(new[] { "run:user:OneDrive", "run:user:Steam" }, report.SucceededIds);
    }

    [Fact]
    public void Set_WriteFails_ErrorLine()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"));
        var writes = 0;
        f.Registry.WriteInterceptor = (_, _, _) => ++writes == 1 ? new InvalidOperationException("odmowa (test)") : null;

        var report = f.Session.Set(rows, AutostartState.Disabled, new AutostartRunOptions());

        var result = Assert.Single(report.Batch!.Results);
        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Equal(
            new[]
            {
                (GuiLogLevel.Error, "OneDrive app: Nie wykonano – Odczyt po zapisie nie potwierdził zmiany – przywrócono poprzedni stan."),
                (GuiLogLevel.Info, "Wyłączono 0 z 1."),
            },
            report.Lines.Select(line => (line.Level, line.Text)));
        Assert.Empty(report.SucceededIds);
    }

    [Fact]
    public async Task Set_LockBusy_RefusalNothingWritten()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => f.Gui.TryRunExclusive(
            () =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
                return 1;
            },
            out _));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));

        var report = f.Session.Set(rows, AutostartState.Disabled, new AutostartRunOptions());

        release.Set();
        Assert.True(await holder);
        Assert.Null(report.Batch);
        Assert.Equal(GuiOperation.DisableAutostart, report.Operation);
        var line = Assert.Single(report.Lines);
        Assert.Equal(GuiLogLevel.Warning, line.Level);
        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", line.Text);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Empty(f.Engine.Journal.Current.Entries);
    }

    [Fact]
    public void Set_RowCannotToggleOrWrongState_RefusalNothingRun()
    {
        var f = new Fixture();
        var row = Row(f.Load(f.Entry(AutostartSource.RunUser, "OneDrive")), "OneDrive");
        var blocked = row with { Blocker = AutostartBlocker.UnknownFormat };

        var refusedBlocked = f.Session.Set(new[] { row, blocked }, AutostartState.Disabled, new AutostartRunOptions());
        var refusedState = f.Session.Set(new[] { row }, AutostartState.Enabled, new AutostartRunOptions());

        foreach (var report in new[] { refusedBlocked, refusedState })
        {
            Assert.Null(report.Batch);
            var line = Assert.Single(report.Lines);
            Assert.Equal(GuiLogLevel.Warning, line.Level);
            Assert.Equal("Wśród zaznaczonych jest pozycja, której nie można tak przełączyć – nic nie zmieniono. Odśwież listę.", line.Text);
        }

        Assert.Equal(GuiOperation.EnableAutostart, refusedState.Operation);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Empty(f.Engine.Journal.Current.Entries);
        Assert.Throws<ArgumentException>(() => f.Session.Set(Array.Empty<AutostartRow>(), AutostartState.Disabled, new AutostartRunOptions()));
        Assert.Throws<ArgumentNullException>(() => f.Session.Set(new[] { row }, AutostartState.Disabled, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.Session.Set(new[] { row }, AutostartState.Unknown, new AutostartRunOptions()));
    }

    [Fact]
    public void Set_ContextFails_ErrorLineNoCrash()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"));
        // Kontekst liczony pod blokadą rzuca (SID z „\” odrzuca AutostartLocations.ForUser) – silnik nie rusza.
        f.Engine.User.Sid = @"S-1-5-21\zly";

        var report = f.Session.Set(rows, AutostartState.Disabled, new AutostartRunOptions());

        Assert.Null(report.Batch);
        Assert.Equal(GuiOperation.DisableAutostart, report.Operation);
        Assert.Equal("autostart", report.TweakId);
        var line = Assert.Single(report.Lines);
        Assert.Equal(GuiLogLevel.Error, line.Level);
        Assert.StartsWith("Nieoczekiwany błąd: Nieprawidłowy SID", line.Text, StringComparison.Ordinal);
        Assert.Contains(f.Engine.Log.Lines, logged => logged.StartsWith("ERROR [Autostart] Nieoczekiwany błąd w serii przy autostart.", StringComparison.Ordinal));
        Assert.Empty(f.Registry.WriteLog);
        Assert.Empty(f.Engine.Journal.Current.Entries);
        Assert.True(f.Gui.TryRunExclusive(() => 0, out _));
    }

    [Fact]
    public void Undo_ContextFails_FailedResult()
    {
        var f = new Fixture();
        f.Engine.User.Sid = @"S-1-5-21\zly";

        var report = f.Session.Undo(History(Guid.NewGuid(), "run:user:OneDrive"), new AutostartRunOptions());

        Assert.Equal(TweakOutcome.Failed, report.Result!.Outcome);
        Assert.Equal(GuiLogLevel.Error, Assert.Single(report.Lines).Level);
        Assert.StartsWith("Autostart: wyłączono OneDrive app: Błąd – Nieoczekiwany błąd: Nieprawidłowy SID", report.Lines[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_NoRebootFlag()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"));

        var report = f.Session.Set(rows, AutostartState.Disabled, new AutostartRunOptions());

        Assert.False(report.RequiresReboot);
        Assert.All(report.Batch!.Results, result => Assert.False(result.RequiresReboot));
        Assert.Null(report.Batch.RestorePoint);
    }

    [Fact]
    public void ConfirmationForUndo_OneItem()
    {
        var f = new Fixture();
        var entryId = Guid.NewGuid();

        var user = f.Session.ConfirmationForUndo(History(entryId, "run:user:OneDrive"));
        var machine = f.Session.ConfirmationForUndo(History(entryId, "run:machine:Teams", "Autostart: wyłączono Teams app") with { AccountText = "Cały komputer" });

        Assert.Equal(GuiOperation.Undo, user.Operation);
        Assert.Equal("Cofnij zmianę w autostarcie", user.Title);
        Assert.Equal("1 pozycja", user.Subtitle);
        Assert.Equal("Cofnij", user.PrimaryText);
        Assert.False(user.CreatesRestorePoint);
        Assert.Equal(new[] { ("Działa od", "Następnego logowania") }, user.Facts.Select(fact => (fact.Name, fact.Value)));
        var item = Assert.Single(user.Items);
        Assert.Equal("run:user:OneDrive", item.Id);
        Assert.Equal("Autostart: wyłączono OneDrive app", item.Title);
        Assert.Equal(RiskLevel.Safe, item.Risk);
        Assert.Equal("Dawid", item.Summary);
        Assert.Empty(item.Warnings);
        Assert.Equal(new[] { "Dotyczy wszystkich kont na tym komputerze." }, Assert.Single(machine.Items).Warnings);
        Assert.Equal("Cały komputer", machine.Items[0].Summary);
        Assert.Throws<ArgumentNullException>(() => f.Session.ConfirmationForUndo(null!));
        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationForUndo(History(entryId, "tweak.x") with { Kind = HistoryKind.Change }));
    }

    [Fact]
    public void Undo_StepsAndBatch_Undone()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"));
        f.Session.Set(rows, AutostartState.Disabled, new AutostartRunOptions());
        var journalEntry = Assert.Single(f.Engine.Journal.Current.Entries);
        var steps = new ListProgress<BatchStepEvent>();
        bool? lockFree = null;
        f.Registry.AfterWrite = (_, _, _) => lockFree ??= LockFreeOnOtherThread(f.Gui);

        var report = f.Session.Undo(History(journalEntry.EntryId, journalEntry.TweakId), new AutostartRunOptions { Steps = steps });

        Assert.False(lockFree);
        Assert.Equal(GuiOperation.Undo, report.Operation);
        Assert.Equal("run:user:OneDrive", report.TweakId);
        var result = Assert.Single(report.Batch!.Results);
        Assert.Same(result, report.Result);
        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(new[] { "run:user:OneDrive" }, report.SucceededIds);
        Assert.False(report.RequiresReboot);
        Assert.Equal(
            new[] { (0, 1, "run:user:OneDrive", BatchStepPhase.Started), (0, 1, "run:user:OneDrive", BatchStepPhase.Finished) },
            steps.Items.Select(step => (step.Index, step.Count, step.TweakId!, step.Phase)));
        Assert.All(steps.Items, step => Assert.Equal(BatchStepKind.Item, step.Kind));
        Assert.Same(result, steps.Items[1].Result);
        Assert.Equal(
            new[] { (GuiLogLevel.Info, "Autostart: wyłączono OneDrive app: Cofnięto – Przywrócono stan sprzed zmiany – zadziała od następnego logowania.") },
            report.Lines.Select(line => (line.Level, line.Text)));
        Assert.Equal(RegValue.Absent, f.Registry.Get(ApprovedKey(AutostartSource.RunUser), "OneDrive"));
    }

    [Fact]
    public void Undo_StateAlreadyAtBaseline_ReportsEngineResult()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"));
        f.Session.Set(rows, AutostartState.Disabled, new AutostartRunOptions());
        var journalEntry = Assert.Single(f.Engine.Journal.Current.Entries);
        // Menedżer zadań włączył pozycję z powrotem: stan = stan sprzed zmiany, a wpis w Historii nadal aktywny.
        f.Registry.Set(ApprovedKey(AutostartSource.RunUser), "OneDrive", RegValue.Absent);

        var report = f.Session.Undo(History(journalEntry.EntryId, journalEntry.TweakId), new AutostartRunOptions());

        Assert.Equal(TweakOutcome.Undone, report.Result!.Outcome);
        Assert.Equal(GuiLogLevel.Info, Assert.Single(report.Lines).Level);
    }

    [Fact]
    public void Undo_Incomplete_LineWithNotRestored()
    {
        var f = new Fixture();
        var rows = f.Load(f.Entry(AutostartSource.RunUser, "OneDrive"));
        f.Session.Set(rows, AutostartState.Disabled, new AutostartRunOptions());
        var journalEntry = Assert.Single(f.Engine.Journal.Current.Entries);
        f.Registry.FailWrite(ApprovedKey(AutostartSource.RunUser), "OneDrive");

        var report = f.Session.Undo(History(journalEntry.EntryId, journalEntry.TweakId), new AutostartRunOptions());

        var result = report.Result!;
        Assert.Equal(TweakOutcome.UndoIncomplete, result.Outcome);
        Assert.Null(result.Message);
        Assert.NotEmpty(result.NotRestored);
        Assert.Empty(report.SucceededIds);
        var line = Assert.Single(report.Lines);
        Assert.Equal(GuiLogLevel.Error, line.Level);
        Assert.Equal(
            "Autostart: wyłączono OneDrive app: Cofnięcie niepełne – Nie udało się w pełni przywrócić stanu sprzed zmiany: " + string.Join("; ", result.NotRestored),
            line.Text);
    }

    [Fact]
    public async Task Undo_LockBusy_Refusal()
    {
        var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => f.Gui.TryRunExclusive(
            () =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
                return 1;
            },
            out _));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));

        var report = f.Session.Undo(History(Guid.NewGuid(), "run:user:OneDrive"), new AutostartRunOptions());

        release.Set();
        Assert.True(await holder);
        Assert.Null(report.Batch);
        Assert.Null(report.Result);
        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", Assert.Single(report.Lines).Text);
        Assert.Throws<ArgumentNullException>(() => f.Session.Undo(null!, new AutostartRunOptions()));
        Assert.Throws<ArgumentNullException>(() => f.Session.Undo(History(Guid.NewGuid(), "run:user:OneDrive"), null!));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var f = new Fixture();
        var e = f.Engine;
        Assert.Throws<ArgumentNullException>(() => new AutostartSession(null!, f.Reader, f.AutostartEngine, Empty, e.User, Sid, e.Registry, e.Log, e.Clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartSession(f.Gui, null!, f.AutostartEngine, Empty, e.User, Sid, e.Registry, e.Log, e.Clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartSession(f.Gui, f.Reader, null!, Empty, e.User, Sid, e.Registry, e.Log, e.Clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartSession(f.Gui, f.Reader, f.AutostartEngine, null!, e.User, Sid, e.Registry, e.Log, e.Clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartSession(f.Gui, f.Reader, f.AutostartEngine, Empty, null!, Sid, e.Registry, e.Log, e.Clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartSession(f.Gui, f.Reader, f.AutostartEngine, Empty, e.User, null!, e.Registry, e.Log, e.Clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartSession(f.Gui, f.Reader, f.AutostartEngine, Empty, e.User, Sid, null!, e.Log, e.Clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartSession(f.Gui, f.Reader, f.AutostartEngine, Empty, e.User, Sid, e.Registry, null!, e.Clock));
        Assert.Throws<ArgumentNullException>(() => new AutostartSession(f.Gui, f.Reader, f.AutostartEngine, Empty, e.User, Sid, e.Registry, e.Log, null!));
    }
}
