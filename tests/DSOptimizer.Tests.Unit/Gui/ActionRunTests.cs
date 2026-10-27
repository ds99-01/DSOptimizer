using System;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class ActionRunTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    private static SeriesConfirmation Confirmation(int items = 1, GuiOperation operation = GuiOperation.Execute)
    {
        var list = new SeriesItem[items];
        for (var i = 0; i < items; i++)
        {
            list[i] = new SeriesItem("repair.system-files", "Sprawdź i napraw pliki systemowe", RiskLevel.Safe, "", Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<SeriesFact>());
        }

        return new SeriesConfirmation(operation, "Uruchom", "Sprawdź i napraw pliki systemowe", Array.Empty<SeriesFact>(), list, "Uruchom", false);
    }

    private static GuiOperationReport Report(TweakOutcome outcome, string? message = null, bool reboot = false, string[]? warnings = null,
        string[]? notRestored = null)
        => new()
        {
            TweakId = "repair.system-files",
            Operation = GuiOperation.Execute,
            Result = new TweakResult
            {
                TweakId = "repair.system-files",
                Outcome = outcome,
                Message = message,
                RequiresReboot = reboot,
                Warnings = warnings ?? Array.Empty<string>(),
                NotRestored = notRestored ?? Array.Empty<string>(),
            },
            RequiresReboot = reboot,
        };

    [Fact]
    public void New_BeforeProgress_TitleOfItemNoPercentCannotStop()
    {
        var run = new ActionRun(Confirmation());

        Assert.Equal("Sprawdź i napraw pliki systemowe", run.Title);
        Assert.Equal(GuiText.ActionRunningSubtitle, run.Subtitle);
        Assert.Equal(GuiText.ActionStarting, run.Stage);
        Assert.Null(run.Percent);
        Assert.Equal(string.Empty, run.StepText);
        Assert.False(run.CanStop);
        Assert.False(run.IsFinished);
    }

    [Fact]
    public void NotStoppableProgress_IsStoppableFalse()
    {
        var run = new ActionRun(Confirmation());
        Assert.True(run.IsStoppable);

        run.OnProgress(new ActionProgress("cleanmgr", null, 0, 2, null, Stoppable: false));
        Assert.False(run.IsStoppable);

        run.OnProgress(new ActionProgress("cleanmgr", null, 0, 2, null, Stoppable: true));
        Assert.True(run.IsStoppable);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void New_NotSingleExecute_Throws(int items)
        => Assert.Throws<ArgumentException>(() => new ActionRun(Confirmation(items)));

    [Fact]
    public void New_NotExecute_Throws()
        => Assert.Throws<ArgumentException>(() => new ActionRun(Confirmation(1, GuiOperation.Apply)));

    [Fact]
    public void OnProgress_SetsStagePercentAndStep_RaisesChanged()
    {
        var run = new ActionRun(Confirmation());
        var changed = 0;
        run.Changed += (_, _) => changed++;

        run.OnProgress(new ActionProgress("DISM", 45, 0, 2));

        Assert.Equal("DISM", run.Stage);
        Assert.Equal(45, run.Percent);
        Assert.Equal(0, run.StepIndex);
        Assert.Equal(2, run.StepCount);
        Assert.Equal("Krok 1 z 2", run.StepText);
        Assert.Null(run.NextStage);
        Assert.Null(run.NextStageText);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void OnProgress_NextStage_TakenFromLatestEvent_NullMeansNoNext()
    {
        var run = new ActionRun(Confirmation());

        run.OnProgress(new ActionProgress("DISM", 45, 0, 2, "SFC – sprawdzanie plików systemowych"));
        Assert.Equal("SFC – sprawdzanie plików systemowych", run.NextStage);
        Assert.Equal("Dalej: SFC – sprawdzanie plików systemowych", run.NextStageText);

        run.OnProgress(new ActionProgress("SFC", 10, 1, 2));
        Assert.Null(run.NextStage);
        Assert.Null(run.NextStageText);
    }

    [Fact]
    public void Complete_ClearsNextStage()
    {
        var run = new ActionRun(Confirmation());
        run.OnProgress(new ActionProgress("DISM", 45, 0, 2, "SFC"));

        run.Complete(Report(TweakOutcome.Executed));

        Assert.Null(run.NextStage);
        Assert.Null(run.NextStageText);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(130, 100)]
    public void OnProgress_PercentOutOfRange_Clamped(int reported, int shown)
    {
        var run = new ActionRun(Confirmation());

        run.OnProgress(new ActionProgress("DISM", reported, 0, 2));

        Assert.Equal(shown, run.Percent);
    }

    [Fact]
    public void OnProgress_IndeterminatePercent_NoNumber()
    {
        var run = new ActionRun(Confirmation());
        run.OnProgress(new ActionProgress("DISM", 45, 0, 2));

        run.OnProgress(new ActionProgress("Czyszczenie", null, 1, 2));

        Assert.Equal("Czyszczenie", run.Stage);
        Assert.Null(run.Percent);
    }

    [Fact]
    public void CanStop_OnlyBeforeLastStep()
    {
        var run = new ActionRun(Confirmation());

        run.OnProgress(new ActionProgress("DISM", 10, 0, 2));
        Assert.True(run.CanStop);

        run.OnProgress(new ActionProgress("SFC", 10, 1, 2));
        Assert.False(run.CanStop);
    }

    [Fact]
    public void CanStop_FalseWhenStepNotStoppable_EvenBeforeLastStep()
    {
        var run = new ActionRun(Confirmation());

        run.OnProgress(new ActionProgress("Oczyszczanie dysku", null, 0, 2, "Sprawdzenie", Stoppable: false));

        Assert.False(run.CanStop);
        Assert.False(run.RequestStop());
        Assert.False(run.StopRequested);
        Assert.Equal("Sprawdzenie", run.NextStage);
    }

    [Fact]
    public void CanStop_StoppableAgainAfterNonStoppableEvent()
    {
        var run = new ActionRun(Confirmation());
        run.OnProgress(new ActionProgress("A", null, 0, 3, "B", Stoppable: false));

        run.OnProgress(new ActionProgress("B", null, 1, 3, "C"));

        Assert.True(run.CanStop);
    }

    [Fact]
    public void RequestStop_OnceThenCannotStop()
    {
        var run = new ActionRun(Confirmation());
        run.OnProgress(new ActionProgress("DISM", 10, 0, 2));

        Assert.True(run.RequestStop());

        Assert.True(run.StopRequested);
        Assert.False(run.CanStop);
        Assert.False(run.RequestStop());
    }

    [Fact]
    public void RequestStop_OnLastStep_Refused()
    {
        var run = new ActionRun(Confirmation());
        run.OnProgress(new ActionProgress("SFC", 10, 1, 2));

        Assert.False(run.RequestStop());
        Assert.False(run.StopRequested);
    }

    [Fact]
    public void Complete_ExecutedWithReboot_ResultFromTweakResult()
    {
        var run = new ActionRun(Confirmation());
        run.OnProgress(new ActionProgress("SFC", 80, 1, 2));

        run.Complete(Report(TweakOutcome.Executed, "DISM: bez uszkodzeń.\r\nSFC: naprawiono pliki.", reboot: true, warnings: new[] { "Uwaga z handlera" }));

        Assert.True(run.IsFinished);
        Assert.False(run.CanStop);
        Assert.Equal("Zakończono", run.Title);
        Assert.Equal("Wykonano", run.Subtitle);
        Assert.Equal(TweakOutcome.Executed, run.Outcome);
        Assert.Equal(100, run.Percent);
        Assert.Equal("Sprawdź i napraw pliki systemowe", run.Stage);
        Assert.Equal(new[] { "DISM: bez uszkodzeń.", "SFC: naprawiono pliki." }, run.ResultLines);
        Assert.Equal(new[] { "Uwaga z handlera" }, run.Warnings);
        Assert.True(run.RequiresReboot);
        Assert.Equal(GuiText.ActionRebootText, run.RebootText);
        Assert.Equal(
            "Zakończono: Wykonano\r\nSprawdź i napraw pliki systemowe\r\n  DISM: bez uszkodzeń.\r\n  SFC: naprawiono pliki.\r\nUwaga z handlera\r\n"
            + "Wymagany restart. " + GuiText.ActionRebootText,
            run.ResultText());
    }

    [Fact]
    public void Complete_RebootOnlyCountsForExecutedResult()
    {
        var run = new ActionRun(Confirmation());

        run.Complete(Report(TweakOutcome.NotConfirmed, "DISM nadal działa w tle.", reboot: false));

        Assert.False(run.RequiresReboot);
        Assert.Null(run.RebootText);
        Assert.Equal("Zakończono", run.Title);
        Assert.Equal("Nie potwierdzono", run.Subtitle);
        Assert.Null(run.Percent);
    }

    [Fact]
    public void Complete_Failed_TitleWithErrorAndNotRestoredLines()
    {
        var run = new ActionRun(Confirmation());

        run.Complete(Report(TweakOutcome.Failed, "Coś poszło źle.", notRestored: new[] { "usługa x" }));

        Assert.Equal("Zakończono z błędem", run.Title);
        Assert.Equal("Błąd", run.Subtitle);
        Assert.Equal(new[] { "Coś poszło źle.", "Nie przywrócono: usługa x" }, run.ResultLines);
        Assert.Null(run.Percent);
    }

    [Fact]
    public void Complete_AfterStop_TitleStoppedNoFullPercent()
    {
        var run = new ActionRun(Confirmation());
        run.OnProgress(new ActionProgress("DISM", 100, 0, 2));
        run.RequestStop();

        run.Complete(Report(TweakOutcome.NotConfirmed, "Zatrzymano po kroku DISM."));

        Assert.Equal("Zatrzymano", run.Title);
        Assert.Null(run.Percent);
    }

    [Fact]
    public void Complete_StopArrivedDuringLastStep_HandlerFinished_NotStopped()
    {
        var run = new ActionRun(Confirmation());
        run.OnProgress(new ActionProgress("DISM", 100, 0, 2));
        run.RequestStop();

        run.Complete(Report(TweakOutcome.Executed, "Oba kroki bez błędów."));

        Assert.Equal("Zakończono", run.Title);
        Assert.Equal(100, run.Percent);
    }

    [Fact]
    public void Complete_LocalRefusalWithoutResult_FailsWithReportLine()
    {
        var run = new ActionRun(Confirmation());
        var refusal = new GuiOperationReport
        {
            TweakId = "repair.system-files",
            Operation = GuiOperation.Execute,
            Lines = new[] { new GuiLogLine(T0, GuiLogLevel.Warning, "Trwa inna operacja – nic nie zmieniono.") },
        };

        run.Complete(refusal);

        Assert.True(run.IsFinished);
        Assert.Equal("Zakończono z błędem", run.Title);
        Assert.Equal(new[] { "Trwa inna operacja – nic nie zmieniono." }, run.ResultLines);
        Assert.Null(run.Outcome);
    }

    [Fact]
    public void Fail_MessageAsResult_ProgressIgnoredAfterwards()
    {
        var run = new ActionRun(Confirmation());
        run.Fail("Nieoczekiwany błąd: x");

        run.OnProgress(new ActionProgress("SFC", 50, 1, 2));

        Assert.Equal("Zakończono z błędem", run.Title);
        Assert.Equal("Błąd", run.Subtitle);
        Assert.Equal(new[] { "Nieoczekiwany błąd: x" }, run.ResultLines);
        Assert.Null(run.Percent);
        Assert.Equal("Zakończono z błędem: Błąd\r\nSprawdź i napraw pliki systemowe\r\n  Nieoczekiwany błąd: x", run.ResultText());
    }
}
