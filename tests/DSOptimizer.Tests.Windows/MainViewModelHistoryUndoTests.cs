using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Microsoft.Win32;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// „Cofnij” w Historii zmian dla wpisu autostartu: przez okno serii (atrapa, która potwierdza albo anuluje) i sesję autostartu, nigdy
/// ścieżką katalogu tweaków. Wpis journala leży w katalogu tymczasowym, a jego baseline wskazuje wyłącznie klucz
/// <c>HKCU\Software\DSOptimizer.Tests\&lt;guid&gt;</c> – prawdziwy autostart hosta nie jest czytany ani zmieniany.
/// </summary>
[Trait("Category", "Windows")]
public class MainViewModelHistoryUndoTests
{
    private const string ValueName = "DsoUndoTest";
    private const string ItemId = "run:machine:" + ValueName;

    // Stan sprzed zmiany (włączona) i stan po wyłączeniu – format StartupApproved, 12 B.
    private static readonly byte[] Baseline = { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] Disabled = { 0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    [Fact]
    public void HistoryUndo_AutostartEntry_NeverReachesCatalogPath()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using var sandbox = new RegistrySandbox();
            var entryId = SeedActiveEntry(temp, sandbox);
            var dialogs = new FakeSeriesDialogs(confirm: true);
            var vm = Create(temp, dialogs, out _);

            vm.SelectedPage = vm.History;
            PumpUntil(() => !vm.History.IsLoading && vm.History.Entries.Count == 1, "wczytanie historii");
            var row = vm.History.SelectedEntry!;
            Assert.Equal(entryId, row.EntryId);
            Assert.True(vm.History.UndoCommand.CanExecute(null));

            Wait(vm.History.UndoCommand.ExecuteAsync(null));
            PumpUntil(() => !vm.IsBusy && vm.LogLines.Count > 0, "linie dziennika");

            var notFound = GuiText.ItemNotFound(GuiOperation.Undo, ItemId);
            Assert.DoesNotContain(vm.LogLines, line => line.Text == notFound);
            Assert.Empty(dialogs.Confirms);
            var confirmation = Assert.Single(dialogs.Series);
            Assert.Equal(GuiOperation.Undo, confirmation.Operation);
            Assert.Equal(ItemId, Assert.Single(confirmation.Items).Id);
            Assert.Equal(JournalEntryStatus.Undone, Status(temp, entryId));
        });
    }

    [Fact]
    public void HistoryUndo_AutostartEntry_GoesToAutostartSession()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using var sandbox = new RegistrySandbox();
            var entryId = SeedActiveEntry(temp, sandbox);
            var dialogs = new FakeSeriesDialogs(confirm: true);
            var vm = Create(temp, dialogs, out _);
            var entry = LoadEntry(vm, entryId);

            Wait(vm.UndoFromHistoryAsync(entry));
            PumpUntil(() => vm.LogLines.Any(line => line.Text.StartsWith(entry.Title + ":", StringComparison.Ordinal)), "linia wyniku");

            var confirmation = Assert.Single(dialogs.Series);
            Assert.Equal(AutostartText.UndoConfirmTitle, confirmation.Title);
            var item = Assert.Single(confirmation.Items);
            Assert.Equal(ItemId, item.Id);
            Assert.Equal(entry.Title, item.Title);
            Assert.Empty(dialogs.Confirms);
            Assert.Equal(
                new[] { (ItemId, BatchStepPhase.Started), (ItemId, BatchStepPhase.Finished) },
                dialogs.Steps.Select(step => (step.TweakId!, step.Phase)));
            Assert.Equal(JournalEntryStatus.Undone, Status(temp, entryId));
            Assert.Equal(Baseline, ReadValue(sandbox));
            var line = vm.LogLines.Single(candidate => candidate.Text.StartsWith(entry.Title + ":", StringComparison.Ordinal));
            Assert.Equal(GuiLogLevel.Info, line.Level);
            Assert.Contains(AutostartText.UndoneFromHistory, line.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(vm.LogLines, candidate => candidate.Level != GuiLogLevel.Info);
            Assert.False(vm.IsBusy);
        });
    }

    [Fact]
    public void HistoryUndo_ChangeEntry_GoesToRunAsync()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var dialogs = new FakeSeriesDialogs(confirm: true);
            var vm = Create(temp, dialogs, out _);
            // Id spoza katalogu: RunAsync(id, Undo) kończy się linią „nie ma pozycji” i niczego nie uruchamia.
            var entry = new HistoryEntry(
                Guid.NewGuid(), "tweak.spoza-katalogu", "tweak.spoza-katalogu", false, null, DateTimeOffset.UtcNow, HistoryText.UnknownAccount,
                HistoryState.Active, Array.Empty<HistoryEvent>(), true, null);

            Wait(vm.UndoFromHistoryAsync(entry));
            var notFound = GuiText.ItemNotFound(GuiOperation.Undo, entry.TweakId);
            PumpUntil(() => vm.LogLines.Count > 0, "linia dziennika");

            var line = Assert.Single(vm.LogLines);
            Assert.Equal((GuiLogLevel.Warning, notFound), (line.Level, line.Text));
            Assert.Empty(dialogs.Series);
            Assert.Empty(dialogs.Confirms);
        });
    }

    [Fact]
    public void HistoryUndo_Autostart_CancelInConfirmation_NothingChanged()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using var sandbox = new RegistrySandbox();
            var entryId = SeedActiveEntry(temp, sandbox);
            var dialogs = new FakeSeriesDialogs(confirm: false);
            var vm = Create(temp, dialogs, out var reader);
            var entry = LoadEntry(vm, entryId);
            var readsBefore = reader.Contexts.Count;

            Wait(vm.UndoFromHistoryAsync(entry));
            var cancelled = MainViewModel.CanceledText(GuiOperation.Undo, entry.Title);
            PumpUntil(() => vm.LogLines.Count > 0, "linia anulowania");

            Assert.Single(dialogs.Series);
            Assert.Empty(dialogs.Steps);
            var line = Assert.Single(vm.LogLines);
            Assert.Equal((GuiLogLevel.Info, cancelled), (line.Level, line.Text));
            Assert.Equal(JournalEntryStatus.Active, Status(temp, entryId));
            Assert.Equal(Disabled, ReadValue(sandbox));
            Assert.Equal(readsBefore, reader.Contexts.Count);
            Assert.False(vm.IsBusy);
            Assert.True(vm.RefreshCommand.CanExecute(null));
        });
    }

    [Fact]
    public void HistoryUndo_Autostart_RefreshesHistoryAndPage()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using var sandbox = new RegistrySandbox();
            var entryId = SeedActiveEntry(temp, sandbox);
            var dialogs = new FakeSeriesDialogs(confirm: true);
            var vm = Create(temp, dialogs, out var reader);
            vm.SelectedPage = vm.History;
            PumpUntil(() => !vm.History.IsLoading && vm.History.Entries.Count == 1, "wczytanie historii");
            Assert.Equal(1, vm.History.ActiveCount);
            Assert.Equal(HistoryState.Active, vm.History.SelectedEntry!.State);
            var readsBefore = reader.Contexts.Count;

            Wait(vm.UndoFromHistoryAsync(vm.History.SelectedEntry.Entry));

            PumpUntil(
                () => !vm.History.IsLoading && vm.History.SelectedEntry?.State == HistoryState.Undone,
                "odświeżona historia");
            Assert.Equal(0, vm.History.ActiveCount);
            Assert.Equal(entryId, vm.History.SelectedEntry!.EntryId);
            Assert.False(vm.History.UndoCommand.CanExecute(null));
            PumpUntil(() => reader.Contexts.Count > readsBefore && !vm.Autostart.IsLoading, "odczyt strony Autostart");
        });
    }

    /// <summary>Wpis z historii tak, jak widzi go strona (ChangeHistory przez sesję okna).</summary>
    private static HistoryEntry LoadEntry(MainViewModel vm, Guid entryId)
    {
        _ = vm.History.RefreshAsync();
        PumpUntil(() => !vm.History.IsLoading && vm.History.Entries.Count == 1, "wczytanie historii");
        var entry = vm.History.Entries.Single().Entry;
        Assert.Equal(entryId, entry.EntryId);
        Assert.Equal(HistoryKind.Autostart, entry.Kind);
        Assert.True(entry.IsActive);
        Assert.Null(entry.UndoBlockedReason);
        return entry;
    }

    private static byte[] ReadValue(RegistrySandbox sandbox)
    {
        using var root = sandbox.OpenOrCreate();
        using var key = root.OpenSubKey(@"StartupApproved\Run")!;
        return (byte[])key.GetValue(ValueName)!;
    }

    /// <summary>Wpis aktywny „wyłączono” pozycji wszystkich kont z baseline w piaskownicy; w piaskownicy stan po wyłączeniu.</summary>
    private static Guid SeedActiveEntry(TempDirectory temp, RegistrySandbox sandbox)
    {
        using (var root = sandbox.OpenOrCreate())
        using (var key = root.CreateSubKey(@"StartupApproved\Run", writable: true)!)
        {
            key.SetValue(ValueName, Disabled, RegistryValueKind.Binary);
        }

        var journal = Journal(temp);
        var document = journal.Load().Document;
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = JournalEntry.AutostartKind,
            TweakId = ItemId,
            Title = ValueName,
            UserSid = AppComposition.Create(temp.AppRoot, null).Services.User.Sid,
            Status = JournalEntryStatus.Active,
            AppliedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        entry.Baseline.Add(SnapshotRecord.ForRegistry(sandbox.HkcuPath + @"\StartupApproved\Run", ValueName, RegValue.FromBinary(Baseline)));
        entry.Events.Add(new JournalEvent { At = entry.AppliedAt, Action = AutostartEngine.DisableAction, Outcome = "Started" });
        entry.Events.Add(new JournalEvent { At = entry.AppliedAt, Action = AutostartEngine.DisableAction, Outcome = nameof(TweakOutcome.Applied) });
        document.Entries.Add(entry);
        journal.Save(document);
        return entry.EntryId;
    }

    private static MainViewModel Create(TempDirectory temp, FakeSeriesDialogs dialogs, out FakeAutostartReader reader)
    {
        // Kontynuacje await wracają na wątek testu (jak w oknie), a PumpUntil wykonuje je razem z liniami dziennika.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        reader = new FakeAutostartReader();
        return MainViewModelTests.Create(temp, reader, dialogs);
    }

    private static IJournalStore Journal(TempDirectory temp) => AppComposition.Create(temp.AppRoot, null).Services.Journal;

    private static JournalEntryStatus Status(TempDirectory temp, Guid entryId)
        => Journal(temp).Load().Document.Entries.Single(entry => entry.EntryId == entryId).Status;

    private static void Wait(Task task)
    {
        PumpUntil(() => task.IsCompleted, "koniec operacji");
        task.GetAwaiter().GetResult();
    }

    /// <summary>Kolejka dispatchera wątku testu aż do spełnienia warunku (najwyżej 30 s).</summary>
    private static void PumpUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Nie doczekano się: " + what);
            }

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    /// <summary>Okno serii bez okna: zapisuje treść potwierdzenia i potwierdza (seria biegnie od razu) albo anuluje.</summary>
    private sealed class FakeSeriesDialogs : ISeriesDialogs
    {
        private readonly bool _confirm;

        public FakeSeriesDialogs(bool confirm) => _confirm = confirm;

        public List<SeriesConfirmation> Series { get; } = new();

        public List<SeriesConfirmation> Confirms { get; } = new();

        public List<BatchStepEvent> Steps { get; } = new();

        public Task<bool> ConfirmAsync(SeriesConfirmation confirmation)
        {
            // Pojedyncza pozycja katalogu nigdy nie jest tu potwierdzana – test nie zmienia prawdziwego ustawienia.
            Confirms.Add(confirmation);
            return Task.FromResult(false);
        }

        public async Task<SeriesDialogResult> RunSeriesAsync(SeriesConfirmation confirmation, Func<IProgress<BatchStepEvent>, BatchStop, Task<GuiOperationReport>> run)
        {
            Series.Add(confirmation);
            if (!_confirm)
            {
                return new SeriesDialogResult(null, false);
            }

            var report = await run(new ListProgress(Steps), new BatchStop()).ConfigureAwait(true);
            return new SeriesDialogResult(report, false);
        }

        public Task<SeriesDialogResult> RunActionAsync(SeriesConfirmation confirmation, Func<IProgress<ActionProgress>, CancellationToken, Task<GuiOperationReport>> run)
            => throw new InvalidOperationException("Test nie uruchamia napraw.");

        public Task<bool> ConfirmRestartAsync() => Task.FromResult(false);

        public Task<bool> ConfirmContinueWithoutRestorePointAsync(string reason) => Task.FromResult(false);
    }

    private sealed class ListProgress : IProgress<BatchStepEvent>
    {
        private readonly List<BatchStepEvent> _items;

        public ListProgress(List<BatchStepEvent> items) => _items = items;

        public void Report(BatchStepEvent value)
        {
            lock (_items)
            {
                _items.Add(value);
            }
        }
    }
}
