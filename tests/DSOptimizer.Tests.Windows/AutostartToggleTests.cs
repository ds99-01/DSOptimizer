using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// „Wyłącz (N)”/„Włącz (M)” strony „Autostart” w modelu głównego okna (<c>MainViewModel.ToggleAutostartAsync</c>) z atrapą okna serii.
/// Odczyt z atrapy czytnika; przełączniki wierszy leżą w <see cref="RegistrySandbox"/>, a silnik pisze wyłącznie pod kluczem przełącznika
/// wiersza – prawdziwy autostart hosta nie jest czytany ani zmieniany. Wątek STA z dispatcherem jako kontekstem synchronizacji (jak okno).
/// </summary>
[Trait("Category", "Windows")]
public sealed class AutostartToggleTests
{
    internal const string UserSid = "S-1-5-21-1-2-3-1001";

    private enum DialogMode
    {
        Cancel,
        Run,
        Canned,
    }

    [Fact]
    public void Cancel_NoRefresh_LogsCancelled_SelectionStays()
        => Run(DialogMode.Cancel, (vm, reader, dialogs, _) =>
        {
            Row(vm, "Alpha").IsSelected = true;

            Execute(vm.Autostart.DisableCommand);
            var cancelled = GuiText.CancelledBatch(GuiOperation.DisableAutostart, new[] { "Alpha" });
            PumpUntil(() => vm.LogLines.Any(line => line.Text == cancelled));
            Pump(TimeSpan.FromMilliseconds(200));

            Assert.Equal(GuiOperation.DisableAutostart, Assert.Single(dialogs.Confirmations).Operation);
            Assert.Single(reader.Contexts);
            Assert.True(Row(vm, "Alpha").IsSelected);
            Assert.Equal("Wyłącz (1)", vm.Autostart.DisableText);
            Assert.False(vm.IsBusy);
            Assert.False(vm.Autostart.IsBusy);
        });

    [Fact]
    public void Confirmed_BusyDuringSeries_DeselectsSucceeded_RefreshesAfter()
        => Run(DialogMode.Run, (vm, reader, dialogs, sandbox) =>
        {
            Row(vm, "Alpha").IsSelected = true;

            Execute(vm.Autostart.DisableCommand);
            PumpUntil(() => reader.Contexts.Count == 2 && !vm.Autostart.IsLoading);

            Assert.True(dialogs.BusyDuringRun);
            Assert.False(vm.IsBusy);
            Assert.False(vm.Autostart.IsBusy);
            var report = Assert.IsType<GuiOperationReport>(dialogs.LastReport);
            Assert.Equal(new[] { AutostartIds.For(AutostartSource.RunUser, "Alpha") }, report.SucceededIds);
            // Zapis trafił pod klucz przełącznika wiersza w piaskownicy (bit 0 = wyłączona).
            var written = new WindowsRegistry().Read(SwitchKey(sandbox), "Alpha");
            Assert.Equal(ReadStatus.Found, written.Status);
            // Odczyt po serii przywraca zaznaczenie po Id – zmienione z powodzeniem są odznaczone, przyciski wracają do zera.
            Assert.False(Row(vm, "Alpha").IsSelected);
            Assert.Equal("Wyłącz (0)", vm.Autostart.DisableText);
            Assert.Equal("Włącz (0)", vm.Autostart.EnableText);
            Assert.False(vm.Autostart.DisableCommand.CanExecute(null));
        });

    [Fact]
    public void Report_FailedRowsStaySelected()
        => Run(DialogMode.Canned, (vm, reader, dialogs, _) =>
        {
            Row(vm, "Alpha").IsSelected = true;
            Row(vm, "Beta").IsSelected = true;
            dialogs.Canned = new GuiOperationReport
            {
                TweakId = "autostart",
                Operation = GuiOperation.DisableAutostart,
                SucceededIds = new[] { AutostartIds.For(AutostartSource.RunUser, "Alpha") },
            };

            Execute(vm.Autostart.DisableCommand);
            PumpUntil(() => reader.Contexts.Count == 2 && !vm.Autostart.IsLoading);

            Assert.False(Row(vm, "Alpha").IsSelected);
            Assert.True(Row(vm, "Beta").IsSelected);
            Assert.Equal("Wyłącz (1)", vm.Autostart.DisableText);
            Assert.Equal("Zaznaczono 1 pozycję", vm.Autostart.SelectionBarTitle);
        });

    internal sealed class FixedUser : IUserContext
    {
        public string Sid => UserSid;

        public bool IsCertain => true;
    }

    private static string SwitchKey(RegistrySandbox sandbox) => sandbox.HkcuPath + @"\StartupApproved\Run";

    private static AutostartRowViewModel Row(MainViewModel vm, string name) => vm.Autostart.VisibleRows.Single(row => row.Name == name);

    private static void Execute(CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command)
    {
        Assert.True(command.CanExecute(null));
        var task = command.ExecuteAsync(null);
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void Run(DialogMode mode, Action<MainViewModel, FakeAutostartReader, FakeSeriesDialogs, RegistrySandbox> body)
        => MainViewModelTests.RunOnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            using var temp = new TempDirectory();
            using var sandbox = new RegistrySandbox();
            var reader = new FakeAutostartReader(new AutostartReading(
                new[] { Entry(sandbox, "Alpha"), Entry(sandbox, "Beta") },
                UserEnvironment.FromVariables(new Dictionary<string, string>()),
                null,
                Array.Empty<string>()));
            var dialogs = new FakeSeriesDialogs(mode);
            var vm = MainViewModelTests.Create(temp, reader, dialogs);
            dialogs.Busy = () => vm.IsBusy && vm.Autostart.IsBusy;
            vm.SelectedPage = vm.Autostart;
            PumpUntil(() => reader.Contexts.Count == 1 && !vm.Autostart.IsLoading && vm.Autostart.VisibleRows.Count == 2);
            body(vm, reader, dialogs, sandbox);
        });

    private static AutostartEntry Entry(RegistrySandbox sandbox, string name) => new()
    {
        Id = AutostartIds.For(AutostartSource.RunUser, name),
        Source = AutostartSource.RunUser,
        EntryName = name,
        Name = name,
        Location = sandbox.HkcuPath + @"\Run",
        FileStatus = AutostartFileStatus.Exists,
        Switch = new AutostartSwitch(AutostartSwitchKind.StartupApproved, SwitchKey(sandbox), name, AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null),
        Publisher = "Producent",
        Command = "\"C:\\Programs\\" + name + ".exe\"",
        FilePath = @"C:\Programs\" + name + ".exe",
    };

    private static void PumpUntil(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "Nie doczekano się warunku w 30 s.");
            Pump(TimeSpan.FromMilliseconds(10));
        }
    }

    private static void Pump(TimeSpan wait)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = wait };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Okno serii bez okna: anuluje, uruchamia przebieg z prawdziwą sesją albo zwraca gotowy raport (bez przebiegu).</summary>
    private sealed class FakeSeriesDialogs : ISeriesDialogs
    {
        private readonly DialogMode _mode;

        public FakeSeriesDialogs(DialogMode mode) => _mode = mode;

        public List<SeriesConfirmation> Confirmations { get; } = new();

        public Func<bool> Busy { get; set; } = () => false;

        public bool BusyDuringRun { get; private set; }

        public GuiOperationReport? LastReport { get; private set; }

        public GuiOperationReport? Canned { get; set; }

        public async Task<SeriesDialogResult> RunSeriesAsync(SeriesConfirmation confirmation, Func<IProgress<BatchStepEvent>, BatchStop, Task<GuiOperationReport>> run)
        {
            Confirmations.Add(confirmation);
            switch (_mode)
            {
                case DialogMode.Cancel:
                    return new SeriesDialogResult(null, false);
                case DialogMode.Canned:
                    LastReport = Canned;
                    return new SeriesDialogResult(Canned, false);
                default:
                    LastReport = await run(new Progress<BatchStepEvent>(), new BatchStop()).ConfigureAwait(true);
                    BusyDuringRun = Busy();
                    return new SeriesDialogResult(LastReport, false);
            }
        }

        public Task<bool> ConfirmAsync(SeriesConfirmation confirmation) => throw new NotSupportedException();

        public Task<SeriesDialogResult> RunActionAsync(SeriesConfirmation confirmation, Func<IProgress<ActionProgress>, CancellationToken, Task<GuiOperationReport>> run)
            => throw new NotSupportedException();

        public Task<bool> ConfirmRestartAsync() => throw new NotSupportedException();

        public Task<bool> ConfirmContinueWithoutRestorePointAsync(string reason) => throw new NotSupportedException();
    }
}
