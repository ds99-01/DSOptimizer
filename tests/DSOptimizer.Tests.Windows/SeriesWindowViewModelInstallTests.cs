using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Okno serii w trybie instalacji (spec 5.2, 5.4): „Zatrzymaj po tej aplikacji”, status kroku z etapu i procentu winget,
/// brak zamknięcia w trakcie i wynik ze statusami instalacji. Bez okna WPF: sam ViewModel, postęp przez <see cref="Progress{T}"/>
/// bez kontekstu synchronizacji (zdarzenia na puli wątków, więc test czeka na ich obsłużenie).
/// </summary>
[Trait("Category", "Windows")]
public class SeriesWindowViewModelInstallTests
{
    private static SeriesConfirmation Confirmation()
        => new(
            GuiOperation.InstallApps,
            InstallText.ConfirmTitle,
            "2 aplikacje",
            Array.Empty<SeriesFact>(),
            new[]
            {
                new SeriesItem("install.firefox", "Mozilla Firefox", RiskLevel.Safe, "Przeglądarka.", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SeriesFact>()),
                new SeriesItem("install.vlc", "VLC", RiskLevel.Safe, "Odtwarzacz.", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SeriesFact>()),
            },
            "Zainstaluj (2)",
            false);

    private static GuiOperationReport Report(params TweakResult[] results)
        => new()
        {
            TweakId = "instalacja",
            Operation = GuiOperation.InstallApps,
            Batch = new BatchResult { Results = results },
        };

    private sealed class Harness
    {
        public TaskCompletionSource<GuiOperationReport> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IProgress<BatchStepEvent>? Steps { get; private set; }

        public BatchStop? Stop { get; private set; }

        public SeriesWindowViewModel Create()
            => new(
                Confirmation(),
                (steps, stop) =>
                {
                    Steps = steps;
                    Stop = stop;
                    return Finish.Task;
                },
                _ => { },
                null,
                new FixedClock());
    }

    private static BatchStepEvent Event(BatchStepPhase phase, string? stage = null, int? percent = null)
        => new(0, 2, BatchStepKind.Item, "install.firefox", phase, DateTimeOffset.UtcNow, Stage: stage, Percent: percent);

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
    public void Confirm_ItemsWithoutRiskMeter()
    {
        var vm = new Harness().Create();

        Assert.True(vm.IsConfirm);
        Assert.All(vm.Items, item => Assert.False(item.ShowRisk));
    }

    [Fact]
    public async Task Running_StopTextForApplications_StopSignalsBetweenItems()
    {
        var harness = new Harness();
        var vm = harness.Create();

        var confirming = vm.PrimaryCommand.ExecuteAsync(null);

        Assert.True(vm.IsRunning);
        Assert.Equal("Zatrzymaj po tej aplikacji", vm.StopButtonText);
        Assert.Equal("Bieżąca instalacja zawsze się kończy.", vm.StopNoteText);
        Assert.True(vm.StopCommand.CanExecute(null));

        vm.StopCommand.Execute(null);

        Assert.True(harness.Stop!.IsRequested);
        Assert.Equal("Zatrzymam po bieżącej aplikacji…", vm.StopButtonText);
        Assert.False(vm.StopCommand.CanExecute(null));

        harness.Finish.SetResult(Report());
        await confirming;
    }

    [Fact]
    public async Task Progress_StepStatusFromStageAndPercent_CannotClose()
    {
        var harness = new Harness();
        var vm = harness.Create();
        var confirming = vm.PrimaryCommand.ExecuteAsync(null);

        harness.Steps!.Report(Event(BatchStepPhase.Started));
        harness.Steps.Report(Event(BatchStepPhase.Progress, "Pobieranie", 45));
        await WaitFor(() => vm.Steps.Count > 0 && vm.Steps[0].StatusText == "Pobieranie 45%");

        Assert.Equal(BatchStepState.Running, vm.Steps[0].State);
        Assert.Equal("Aplikacja 1 z 2 · Dalej: VLC", vm.HeaderSubtitle);
        Assert.False(vm.CanClose);
        Assert.False(vm.CloseCommand.CanExecute(null));
        vm.HandleEscape();
        Assert.True(vm.IsRunning);

        harness.Steps.Report(Event(BatchStepPhase.Progress, "Instalowanie"));
        await WaitFor(() => vm.Steps[0].StatusText == "Instalowanie");

        harness.Finish.SetResult(Report());
        await confirming;
    }

    [Fact]
    public async Task Result_InstallStatuses_NotTweakTexts()
    {
        var harness = new Harness();
        var vm = harness.Create();
        var confirming = vm.PrimaryCommand.ExecuteAsync(null);

        harness.Finish.SetResult(Report(
            new TweakResult { TweakId = "install.firefox", Outcome = TweakOutcome.Installed, Message = InstallText.Installed },
            new TweakResult { TweakId = "install.vlc", Outcome = TweakOutcome.Failed, Message = InstallText.SkippedNoNetwork }));
        await confirming;

        Assert.True(vm.IsResult);
        Assert.True(vm.CanClose);
        Assert.Equal("Zakończono", vm.HeaderTitle);
        Assert.Equal("1 z 2 zainstalowane", vm.HeaderSubtitle);
        Assert.Equal(new[] { "Zainstalowano", "Nie wykonano" }, vm.Steps.Select(step => step.StatusText).ToArray());
        Assert.Equal(new[] { "Brak połączenia z internetem albo ze źródłem winget" }, vm.Steps[1].Details);
        Assert.True(vm.Steps[1].ShowDetails);
    }
}
