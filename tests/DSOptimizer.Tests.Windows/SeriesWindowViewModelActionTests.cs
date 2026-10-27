using System;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Okno serii w trybie długiej akcji (potwierdzenie → postęp w procentach → wynik). Bez okna WPF: sam ViewModel, postęp przez
/// <see cref="Progress{T}"/> bez kontekstu synchronizacji (zdarzenia na puli wątków, więc test czeka na ich obsłużenie).
/// </summary>
[Trait("Category", "Windows")]
public class SeriesWindowViewModelActionTests
{
    private const string Title = "Sprawdź i napraw pliki systemowe";

    private static SeriesConfirmation Confirmation()
        => new(
            GuiOperation.Execute,
            "Uruchom",
            Title,
            Array.Empty<SeriesFact>(),
            new[] { new SeriesItem("repair.system-files", Title, RiskLevel.Safe, "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SeriesFact>()) },
            "Uruchom",
            false);

    private static GuiOperationReport Report(TweakOutcome outcome, string? message = null, bool reboot = false)
        => new()
        {
            TweakId = "repair.system-files",
            Operation = GuiOperation.Execute,
            Result = new TweakResult { TweakId = "repair.system-files", Outcome = outcome, Message = message, RequiresReboot = reboot },
            RequiresReboot = reboot,
        };

    private sealed class Harness
    {
        public TaskCompletionSource<GuiOperationReport> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IProgress<ActionProgress>? Progress { get; private set; }

        public CancellationToken Stop { get; private set; }

        public string? Copied { get; private set; }

        public SeriesWindowViewModel Create(bool withRestart = false)
            => SeriesWindowViewModel.ForAction(
                Confirmation(),
                (progress, stop) =>
                {
                    Progress = progress;
                    Stop = stop;
                    return Finish.Task;
                },
                text => Copied = text,
                withRestart ? () => Task.FromResult(false) : null,
                new FixedClock());
    }

    /// <summary>
    /// Czeka bez blokowania wątku: zdarzenia <see cref="Progress{T}"/> idą przez kontekst synchronizacji xUnit (ograniczona pula
    /// wątków testów), więc blokujące czekanie przy obciążonej puli mogło przekroczyć limit (niestabilność przy równoległych testach).
    /// </summary>
    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Warunek nie został spełniony w 10 s.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public void Confirm_ActionMode_LikeSeriesConfirmation()
    {
        var vm = new Harness().Create();

        Assert.True(vm.IsActionMode);
        Assert.True(vm.IsConfirm);
        Assert.True(vm.CanClose);
        Assert.Equal("Uruchom", vm.HeaderTitle);
        Assert.Equal(Title, vm.HeaderSubtitle);
        Assert.False(vm.ShowActionPanel);
    }

    [Fact]
    public async Task Running_CannotClose_ResultAfterComplete()
    {
        var harness = new Harness();
        var vm = harness.Create();

        var confirming = vm.PrimaryCommand.ExecuteAsync(null);

        Assert.True(vm.IsRunning);
        Assert.False(vm.CanClose);
        Assert.False(vm.CloseCommand.CanExecute(null));
        Assert.False(vm.ShowSeriesProgressLine);
        Assert.True(vm.ShowActionPanel);
        Assert.Equal(Title, vm.HeaderTitle);
        Assert.Equal(GuiText.ActionRunningSubtitle, vm.HeaderSubtitle);
        Assert.Equal(GuiText.ActionProgressHint, vm.ActionHint);
        vm.HandleEscape();
        Assert.True(vm.IsRunning);

        var report = Report(TweakOutcome.Executed, "Bez błędów.");
        harness.Finish.SetResult(report);
        await confirming;

        Assert.True(vm.IsResult);
        Assert.True(vm.CanClose);
        Assert.True(vm.CloseCommand.CanExecute(null));
        Assert.Same(report, vm.Report);
        Assert.True(vm.Confirmed);
        Assert.Equal("Zakończono", vm.HeaderTitle);
        Assert.Equal("Wykonano", vm.HeaderSubtitle);
        Assert.Equal("100%", vm.ActionPercentText);
        Assert.Equal(Title, vm.ActionStage);
        Assert.Equal(new[] { "Bez błędów." }, vm.ActionResultLines);
        Assert.Null(vm.ActionHint);
        Assert.Empty(vm.Steps);
        Assert.False(vm.ShowRestartButton);
    }

    [Fact]
    public async Task Progress_PercentStageAndStep_ThenStopCancelsToken()
    {
        var harness = new Harness();
        var vm = harness.Create();
        var confirming = vm.PrimaryCommand.ExecuteAsync(null);
        Assert.False(vm.StopCommand.CanExecute(null));

        harness.Progress!.Report(new ActionProgress("DISM", 45, 0, 2, "SFC – sprawdzanie plików systemowych"));
        await WaitFor(() => vm.ActionPercentText == "45%");

        Assert.True(vm.HasActionPercent);
        Assert.Equal("DISM", vm.ActionStage);
        Assert.Equal("Krok 1 z 2", vm.ActionStepText);
        Assert.True(vm.HasActionNextStage);
        Assert.Equal("Dalej: SFC – sprawdzanie plików systemowych", vm.ActionNextStageText);
        Assert.Equal(GuiText.ActionStopText, vm.StopButtonText);
        Assert.True(vm.StopCommand.CanExecute(null));

        vm.StopCommand.Execute(null);

        Assert.True(harness.Stop.IsCancellationRequested);
        Assert.Equal(GuiText.ActionStoppingText, vm.StopButtonText);
        Assert.False(vm.StopCommand.CanExecute(null));
        Assert.False(vm.CanClose);

        harness.Finish.SetResult(Report(TweakOutcome.NotConfirmed, "Zatrzymano po kroku DISM."));
        await confirming;
        Assert.Equal("Zatrzymano", vm.HeaderTitle);
        Assert.False(vm.HasActionPercent);
    }

    [Fact]
    public async Task NotStoppable_StopFooterHidden()
    {
        var harness = new Harness();
        var vm = harness.Create();
        var confirming = vm.PrimaryCommand.ExecuteAsync(null);

        harness.Progress!.Report(new ActionProgress("cleanmgr", null, 0, 2, null, Stoppable: false));
        await WaitFor(() => vm.ActionStage == "cleanmgr");

        Assert.True(vm.IsRunning);
        Assert.False(vm.ShowStopFooter);

        harness.Finish.SetResult(Report(TweakOutcome.Executed));
        await confirming;
    }

    [Fact]
    public async Task Stoppable_StopFooterShown()
    {
        var harness = new Harness();
        var vm = harness.Create();
        var confirming = vm.PrimaryCommand.ExecuteAsync(null);

        harness.Progress!.Report(new ActionProgress("DISM", 10, 0, 2, "SFC"));
        await WaitFor(() => vm.ActionStage == "DISM");

        Assert.True(vm.ShowStopFooter);

        harness.Finish.SetResult(Report(TweakOutcome.Executed));
        await confirming;
    }

    [Fact]
    public async Task Progress_LastStep_StopInactive_IndeterminateHidesNumber()
    {
        var harness = new Harness();
        var vm = harness.Create();
        var confirming = vm.PrimaryCommand.ExecuteAsync(null);

        harness.Progress!.Report(new ActionProgress("SFC", null, 1, 2));
        await WaitFor(() => vm.ActionStage == "SFC");

        Assert.False(vm.HasActionPercent);
        Assert.False(vm.StopCommand.CanExecute(null));
        Assert.Equal("Krok 2 z 2", vm.ActionStepText);
        Assert.False(vm.HasActionNextStage);
        Assert.Null(vm.ActionNextStageText);

        harness.Finish.SetResult(Report(TweakOutcome.Executed));
        await confirming;
    }

    [Fact]
    public async Task Result_RebootAndCopy_FromActionRun()
    {
        var harness = new Harness();
        var vm = harness.Create(withRestart: true);
        var confirming = vm.PrimaryCommand.ExecuteAsync(null);

        harness.Finish.SetResult(Report(TweakOutcome.Executed, "SFC naprawił pliki.", reboot: true));
        await confirming;
        vm.CopyResultCommand.Execute(null);

        Assert.True(vm.ShowRestartButton);
        Assert.Equal(GuiText.ActionRebootText, vm.RebootText);
        Assert.Contains("SFC naprawił pliki.", harness.Copied);
        Assert.StartsWith("Zakończono: Wykonano", harness.Copied);
    }

    [Fact]
    public async Task RunThrows_ResultWithErrorAndFailureReport()
    {
        var vm = SeriesWindowViewModel.ForAction(
            Confirmation(),
            (_, _) => Task.FromException<GuiOperationReport>(new InvalidOperationException("padło")),
            null,
            null,
            new FixedClock());

        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.True(vm.IsResult);
        Assert.Equal("Zakończono z błędem", vm.HeaderTitle);
        Assert.Equal("repair.system-files", vm.Report!.TweakId);
        Assert.Equal(GuiLogLevel.Error, Assert.Single(vm.Report.Lines).Level);
        Assert.Contains("padło", Assert.Single(vm.ActionResultLines));
    }

    [Fact]
    public void Cancel_InConfirm_NoReport()
    {
        var vm = new Harness().Create();

        vm.CancelCommand.Execute(null);

        Assert.False(vm.Confirmed);
        Assert.Null(vm.Report);
    }
}
