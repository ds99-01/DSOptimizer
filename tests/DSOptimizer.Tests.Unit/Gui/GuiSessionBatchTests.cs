using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class GuiSessionBatchTests
{
    private static TweakDefinition Caution(string id = "test.caution")
        => TestTweaks.TwoValues(id) with { Risk = RiskLevel.Caution, Warnings = new[] { "Uwaga 1" }, Description = "Opis" };

    private static GuiSession Session(EngineFixture f, params TweakDefinition[] definitions)
        => new(new LoadedCatalog(definitions, "sha256:test"), f.CreateServices());

    [Theory]
    [InlineData(GuiOperation.Apply)]
    [InlineData(GuiOperation.Undo)]
    public void RunBatch_ApplyWithoutConfirmation_WhenAnyCaution_DoesNotTouchEngine(GuiOperation operation)
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.safe"), Caution());

        var report = session.RunBatch(new[] { "t.safe", "test.caution" }, operation, confirmed: false);

        Assert.Equal("seria", report.TweakId);
        Assert.Null(report.Result);
        var line = Assert.Single(report.Lines);
        Assert.Equal(GuiLogLevel.Warning, line.Level);
        Assert.Equal("Operacja wymaga potwierdzenia – nic nie zmieniono.", line.Text);
        Assert.Empty(report.Refreshed);
        Assert.Empty(report.SucceededIds);
        Assert.Equal(0, f.Journal.SaveCount);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Empty(f.RestorePoints.Descriptions);
    }

    [Fact]
    public void RunBatch_RefreshDetectionThrows_KeepsSummaryAndMarksItemUnknown()
    {
        var f = new EngineFixture();
        f.Registry.WriteInterceptor = (_, name, _) =>
        {
            if (name == "B")
            {
                f.Journal.LoadException = new InvalidOperationException("journal padł");
            }

            return null;
        };
        var session = Session(f, TestTweaks.TwoValues("t.safe"));

        var report = session.RunBatch(new[] { "t.safe" }, GuiOperation.Apply, confirmed: false);

        Assert.Equal(new[] { "t.safe" }, report.SucceededIds);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Info && line.Text == "Dwie wartości: Zastosowano");
        Assert.Contains(report.Lines, line => line.Text == "Seria: 1 z 1 zakończonych powodzeniem.");
        var error = Assert.Single(report.Lines, line => line.Level == GuiLogLevel.Error);
        Assert.Contains("journal padł", error.Text, StringComparison.Ordinal);
        Assert.Equal(DetectionState.Unknown, report.Refreshed["t.safe"]);
    }

    [Fact]
    public void RunBatch_Apply_CreatesOneRestorePointForAll()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.safe"), Caution());

        var report = session.RunBatch(new[] { "t.safe", "test.caution" }, GuiOperation.Apply, confirmed: true);

        Assert.Equal("DSOptimizer – przed zmianami (2)", Assert.Single(f.RestorePoints.Descriptions));
        Assert.Equal(DetectionState.Applied, report.Refreshed["t.safe"]);
        Assert.Equal(DetectionState.Applied, report.Refreshed["test.caution"]);
        Assert.Equal(new[] { "t.safe", "test.caution" }, report.SucceededIds);
        Assert.Equal("Dwie wartości: Zastosowano", report.Lines[0].Text);
        var summary = report.Lines[^1];
        Assert.Equal(GuiLogLevel.Info, summary.Level);
        Assert.Equal("Seria: 2 z 2 zakończonych powodzeniem.", summary.Text);
        Assert.False(report.RequiresReboot);
    }

    [Fact]
    public void RunBatch_Apply_RestorePointDisabled_CreatesNone()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.one"), TestTweaks.TwoValues("t.two"));
        session.CreateRestorePoint = false;

        var report = session.RunBatch(new[] { "t.one", "t.two" }, GuiOperation.Apply, confirmed: true);

        Assert.Empty(f.RestorePoints.Descriptions);
        Assert.Equal(new[] { "t.one", "t.two" }, report.SucceededIds);
        Assert.DoesNotContain(report.Lines, line => line.Text.StartsWith("Punkt przywracania", StringComparison.Ordinal));
    }

    [Fact]
    public void RunBatch_TwoSafeUnconfirmed_Refuses()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.one"), TestTweaks.TwoValues("t.two"));

        var report = session.RunBatch(new[] { "t.one", "t.two" }, GuiOperation.Apply, confirmed: false);

        Assert.Null(report.Result);
        Assert.Null(report.Batch);
        var line = Assert.Single(report.Lines);
        Assert.Equal("Operacja wymaga potwierdzenia – nic nie zmieniono.", line.Text);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Empty(f.RestorePoints.Descriptions);
    }

    [Fact]
    public void RunBatch_OneSafeUnconfirmed_Runs()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.one"));

        var report = session.RunBatch(new[] { "t.one" }, GuiOperation.Apply, confirmed: false);

        Assert.Equal(new[] { "t.one" }, report.SucceededIds);
        Assert.NotNull(report.Batch);
    }

    [Fact]
    public void RunBatch_PassesStepsAndStop()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.one"), TestTweaks.TwoValues("t.two"));
        session.CreateRestorePoint = false;
        var stop = new BatchStop();
        var steps = new ListProgress<BatchStepEvent>
        {
            OnReport = step =>
            {
                if (step.Kind == BatchStepKind.Item && step.TweakId == "t.one" && step.Phase == BatchStepPhase.Started)
                {
                    stop.Request();
                }
            },
        };

        var report = session.RunBatch(new[] { "t.one", "t.two" }, GuiOperation.Apply, confirmed: true, steps, stop);

        Assert.True(report.Batch!.Stopped);
        Assert.Equal(new[] { "t.one" }, report.SucceededIds);
        Assert.Equal(
            new[] { ("t.one", BatchStepPhase.Started), ("t.one", BatchStepPhase.Finished) },
            steps.Items.Select(step => (step.TweakId!, step.Phase)));
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Warning && line.Text == "Dwie wartości: Pominięto – zatrzymano.");
    }

    [Fact]
    public void RunBatch_Stopped_LogLineWithoutRepeatedSkipped()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.one"), TestTweaks.TwoValues("t.two"));
        session.CreateRestorePoint = false;
        var stop = new BatchStop();
        stop.Request();

        var report = session.RunBatch(new[] { "t.one", "t.two" }, GuiOperation.Apply, confirmed: true, steps: null, stop);

        Assert.Equal(2, report.Lines.Count(line => line.Text == "Dwie wartości: Pominięto – zatrzymano."));
        Assert.DoesNotContain(report.Lines, line => line.Text.Contains("Pominięte"));
    }

    [Fact]
    public void RunBatch_Undo_UsesEngineUndoBatch()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.one"), TestTweaks.TwoValues("t.two"));
        var ids = new[] { "t.one", "t.two" };
        session.RunBatch(ids, GuiOperation.Apply, confirmed: true);
        var steps = new ListProgress<BatchStepEvent>();

        var report = session.RunBatch(ids, GuiOperation.Undo, confirmed: true, steps);

        Assert.Equal(4, steps.Items.Count);
        Assert.All(steps.Items, step => Assert.Equal(BatchStepKind.Item, step.Kind));
        Assert.Equal(2, report.Batch!.Results.Count);
        Assert.False(report.Batch.Stopped);
        Assert.Equal(ids, report.SucceededIds);
    }

    [Fact]
    public void Run_Apply_RestorePointDisabled_CreatesNone()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.one"));
        session.CreateRestorePoint = false;

        var report = session.Run("t.one", GuiOperation.Apply, confirmed: false);

        Assert.Empty(f.RestorePoints.Descriptions);
        Assert.True(report.Result!.IsSuccess);
    }

    [Fact]
    public void RunBatch_Apply_RestorePointFailure_WarnsOnceBeforeItems_AndRebootIsOrOfResults()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "brak");
        var session = Session(f, TestTweaks.TwoValues("t.one") with { RequiresReboot = true }, TestTweaks.TwoValues("t.two"));

        var report = session.RunBatch(new[] { "t.one", "t.two" }, GuiOperation.Apply, confirmed: true);

        Assert.Equal(GuiLogLevel.Warning, report.Lines[0].Level);
        Assert.Contains("Punkt przywracania nie powstał", report.Lines[0].Text);
        Assert.Single(report.Lines, line => line.Text.StartsWith("Punkt przywracania", StringComparison.Ordinal));
        Assert.True(report.RequiresReboot);
        Assert.Equal("Seria: 2 z 2 zakończonych powodzeniem.", report.Lines[^1].Text);
    }

    [Fact]
    public void RunBatch_Undo_ContinuesAfterFailureOfOne()
    {
        var f = new EngineFixture();
        var toggle = new ToggleScript();
        toggle.Register(f.Scripts);
        var session = Session(f, TestTweaks.Scripted(), Caution());
        var ids = new[] { "test.script", "test.caution" };
        session.RunBatch(ids, GuiOperation.Apply, confirmed: true);
        f.Scripts.On("UNDO", _ => FakeScriptRunner.Result(1));

        var report = session.RunBatch(ids, GuiOperation.Undo, confirmed: true);

        Assert.Single(f.RestorePoints.Descriptions);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Error && line.Text.StartsWith("Skrypt: ", StringComparison.Ordinal));
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Info && line.Text.StartsWith("Dwie wartości: ", StringComparison.Ordinal));
        Assert.Equal("Seria: 1 z 2 zakończonych powodzeniem.", report.Lines[^1].Text);
        Assert.Equal(new[] { "test.caution" }, report.SucceededIds);
        Assert.Equal(DetectionState.NotApplied, report.Refreshed["test.caution"]);
    }

    [Fact]
    public async Task RunBatch_WhileAnotherRunInProgress_Refuses()
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
        var session = Session(f, TestTweaks.Scripted(), TestTweaks.TwoValues("t.other"));

        var first = Task.Run(() => session.Run("test.script", GuiOperation.Apply, confirmed: true));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
        var restorePointsBefore = f.RestorePoints.Descriptions.Count;

        var batch = session.RunBatch(new[] { "t.other" }, GuiOperation.Apply, confirmed: true);

        Assert.Null(batch.Result);
        var refusal = Assert.Single(batch.Lines);
        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", refusal.Text);
        Assert.Equal(GuiLogLevel.Warning, refusal.Level);
        Assert.Equal(restorePointsBefore, f.RestorePoints.Descriptions.Count);
        Assert.Empty(batch.Refreshed);

        release.Set();
        await first;
        Assert.Equal(restorePointsBefore, f.RestorePoints.Descriptions.Count);
    }

    [Theory]
    [InlineData("nie.ma", GuiOperation.Apply)]
    [InlineData("test.action", GuiOperation.Apply)]
    [InlineData("test.panel", GuiOperation.Undo)]
    [InlineData("t.safe", GuiOperation.Execute)]
    public void RunBatch_UnknownIdOrNonTweakOrExecute_Throws(string id, GuiOperation operation)
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.safe"), TestTweaks.Action(), TestTweaks.Panel());

        Assert.Throws<ArgumentException>(() => session.RunBatch(new[] { "t.safe", id }, operation, confirmed: true));
        Assert.Throws<ArgumentException>(() => session.ConfirmationForSeries(new[] { "t.safe", id }, operation, null));
        Assert.Equal(0, f.Journal.SaveCount);
        Assert.Empty(f.RestorePoints.Descriptions);
    }

    [Fact]
    public void RunBatch_EmptyList_Throws()
    {
        var session = Session(new EngineFixture(), TestTweaks.TwoValues());
        Assert.Throws<ArgumentException>(() => session.RunBatch(Array.Empty<string>(), GuiOperation.Apply, confirmed: true));
        Assert.Throws<ArgumentException>(() => session.RunBatch(null!, GuiOperation.Apply, confirmed: true));
    }
}
