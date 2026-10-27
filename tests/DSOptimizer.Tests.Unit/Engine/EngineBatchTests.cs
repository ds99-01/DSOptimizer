using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Engine;

public class EngineBatchTests
{
    [Fact]
    public void Batch_ContinuesAfterFailureAndSummarizes()
    {
        var f = new EngineFixture();
        f.Registry.FailWrite(TestTweaks.Path, "B");
        f.Scripts.On("RUN", 0);

        var batch = f.CreateEngine().ApplyBatch(
            new[] { TestTweaks.UserMenuDelay(), TestTweaks.TwoValues(), TestTweaks.Action() },
            new BatchOptions());

        Assert.Equal(
            new[] { TweakOutcome.Applied, TweakOutcome.FailedRolledBack, TweakOutcome.Executed },
            batch.Results.Select(result => result.Outcome).ToArray());
        Assert.Equal(2, batch.SucceededCount);
        Assert.Equal(1, batch.FailedCount);
        Assert.Equal(0, batch.SkippedCount);
        Assert.False(batch.Aborted);
        Assert.Single(f.RestorePoints.Descriptions);
    }

    [Fact]
    public void Batch_RequiredRestorePointFails_AbortsBeforeChanges()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "ochrona wyłączona");

        var batch = f.CreateEngine().ApplyBatch(
            new[] { TestTweaks.TwoValues() },
            new BatchOptions { RestorePointPolicy = RestorePointPolicy.Required });

        Assert.True(batch.Aborted);
        Assert.Contains("ochrona wyłączona", batch.AbortReason, StringComparison.Ordinal);
        Assert.Equal(TweakOutcome.Skipped, Assert.Single(batch.Results).Outcome);
        Assert.Empty(f.Registry.WriteLog);
    }

    [Fact]
    public void Batch_IfAvailableRestorePointFails_WarnsAndContinues()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "ochrona wyłączona");

        var batch = f.CreateEngine().ApplyBatch(new[] { TestTweaks.TwoValues() }, new BatchOptions());

        Assert.Single(batch.Warnings);
        Assert.Equal(TweakOutcome.Applied, Assert.Single(batch.Results).Outcome);
    }

    [Fact]
    public void Batch_SkipPolicy_DoesNotCreateRestorePoint()
    {
        var f = new EngineFixture();

        f.CreateEngine().ApplyBatch(new[] { TestTweaks.TwoValues() }, new BatchOptions { RestorePointPolicy = RestorePointPolicy.Skip });

        Assert.Empty(f.RestorePoints.Descriptions);
    }

    [Fact]
    public void Batch_SkipPolicy_RestorePointIsNull()
    {
        var f = new EngineFixture();

        var batch = f.CreateEngine().ApplyBatch(new[] { TestTweaks.TwoValues() }, new BatchOptions { RestorePointPolicy = RestorePointPolicy.Skip });

        Assert.Null(batch.RestorePoint);
    }

    [Fact]
    public void Batch_RestorePointCreated_ResultCarriesIt()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Created, null, 0);

        var batch = f.CreateEngine().ApplyBatch(new[] { TestTweaks.TwoValues() }, new BatchOptions());

        Assert.NotNull(batch.RestorePoint);
        Assert.Equal(RestorePointStatus.Created, batch.RestorePoint.Status);
    }

    [Fact]
    public void Batch_RequiredRestorePointFails_AbortedResultCarriesCode()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "x", 1058u);

        var batch = f.CreateEngine().ApplyBatch(
            new[] { TestTweaks.TwoValues() },
            new BatchOptions { RestorePointPolicy = RestorePointPolicy.Required });

        Assert.True(batch.Aborted);
        Assert.NotNull(batch.RestorePoint);
        Assert.Equal(1058u, batch.RestorePoint.Code);
    }

    [Fact]
    public void Batch_IfAvailableRestorePointFails_ResultCarriesIt()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "x", 2u);

        var batch = f.CreateEngine().ApplyBatch(new[] { TestTweaks.TwoValues() }, new BatchOptions());

        Assert.NotNull(batch.RestorePoint);
        Assert.Equal(RestorePointStatus.Failed, batch.RestorePoint.Status);
        Assert.Equal(TweakOutcome.Applied, Assert.Single(batch.Results).Outcome);
    }

    [Fact]
    public void Batch_CancelledMidway_SkipsRemaining()
    {
        var f = new EngineFixture();
        using var cts = new CancellationTokenSource();
        f.Scripts.On("RUN", _ =>
        {
            cts.Cancel();
            return FakeScriptRunner.Result(0);
        });

        var batch = f.CreateEngine().ApplyBatch(
            new[] { TestTweaks.Action(), TestTweaks.TwoValues() },
            new BatchOptions { RestorePointPolicy = RestorePointPolicy.Skip },
            cancellationToken: cts.Token);

        Assert.Equal(TweakOutcome.Executed, batch.Results[0].Outcome);
        Assert.Equal(TweakOutcome.Skipped, batch.Results[1].Outcome);
        Assert.Equal(1, batch.SkippedCount);
        Assert.Empty(f.Registry.WriteLog);
    }

    [Fact]
    public void Batch_RequiresReboot_WhenAnyTweakNeedsIt()
    {
        var f = new EngineFixture();
        new ToggleScript { ApplyWithoutEffect = true }.Register(f.Scripts);

        var batch = f.CreateEngine().ApplyBatch(
            new[] { TestTweaks.Scripted() with { RequiresReboot = true } },
            new BatchOptions { RestorePointPolicy = RestorePointPolicy.Skip });

        Assert.True(batch.RequiresReboot);
    }

    [Fact]
    public void Batch_RestorePointServiceThrows_RequiredAbortsBeforeChanges()
    {
        var f = new EngineFixture();
        f.RestorePoints.ThrowOnCreate = new InvalidOperationException("WMI padło");

        var batch = f.CreateEngine().ApplyBatch(
            new[] { TestTweaks.TwoValues() },
            new BatchOptions { RestorePointPolicy = RestorePointPolicy.Required });

        Assert.True(batch.Aborted);
        Assert.Contains("WMI padło", batch.AbortReason, StringComparison.Ordinal);
        Assert.Equal(TweakOutcome.Skipped, Assert.Single(batch.Results).Outcome);
        Assert.Empty(f.Registry.WriteLog);
    }

    [Fact]
    public void Batch_RestorePointServiceThrows_IfAvailableWarnsAndContinues()
    {
        var f = new EngineFixture();
        f.RestorePoints.ThrowOnCreate = new InvalidOperationException("WMI padło");

        var batch = f.CreateEngine().ApplyBatch(new[] { TestTweaks.TwoValues() }, new BatchOptions());

        Assert.False(batch.Aborted);
        Assert.Single(batch.Warnings);
        Assert.Equal(TweakOutcome.Applied, Assert.Single(batch.Results).Outcome);
    }

    [Fact]
    public void Batch_UnexpectedExceptionFromOneTweak_FailsItAndContinues()
    {
        var f = new EngineFixture();
        var log = new ThrowingOnStartLog(f.Log, TestTweaks.UserMenuDelay().Id);
        var engine = new TweakEngine(
            new EngineServices
            {
                Registry = f.Registry,
                Services = f.Services,
                Scripts = f.Scripts,
                Launcher = f.Launcher,
                Journal = f.Journal,
                RestorePoints = f.RestorePoints,
                SystemInfo = f.SystemInfo,
                User = f.User,
                Log = log,
                Clock = f.Clock,
            },
            "sha256:test");

        var batch = engine.ApplyBatch(
            new[] { TestTweaks.UserMenuDelay(), TestTweaks.TwoValues() },
            new BatchOptions { RestorePointPolicy = RestorePointPolicy.Skip });

        Assert.Equal(
            new[] { TweakOutcome.Failed, TweakOutcome.Applied },
            batch.Results.Select(result => result.Outcome).ToArray());
        Assert.Equal(1, batch.FailedCount);
    }

    /// <summary>Pozycja o własnej ścieżce rejestru, by kolejne w serii nie kończyły się AlreadyApplied.</summary>
    private static TweakDefinition Item(string id)
    {
        var template = TestTweaks.TwoValues(id);
        return template with { Registry = template.Registry.Select(entry => entry with { Path = $"{TestTweaks.Path}\\{id}" }).ToArray() };
    }

    private static BatchOptions Opts(ListProgress<BatchStepEvent>? steps = null, BatchStop? stop = null, RestorePointPolicy policy = RestorePointPolicy.IfAvailable)
        => new() { RestorePointPolicy = policy, Steps = steps, Stop = stop };

    [Fact]
    public void ApplyBatch_ReportsStepsInOrder()
    {
        var f = new EngineFixture();
        var steps = new ListProgress<BatchStepEvent>();
        var seenClock = new List<DateTimeOffset>();
        steps.OnReport = _ =>
        {
            seenClock.Add(f.Clock.UtcNow);
            f.Clock.UtcNow = f.Clock.UtcNow.AddMinutes(1);
        };
        f.Registry.WriteInterceptor = (_, _, _) =>
        {
            f.Clock.UtcNow = f.Clock.UtcNow.AddSeconds(10);
            return null;
        };

        f.CreateEngine().ApplyBatch(new[] { Item("a"), Item("b") }, Opts(steps));

        var e = steps.Items;
        Assert.Equal(6, e.Count);
        Assert.All(e, ev => Assert.Equal(3, ev.Count));
        Assert.Equal(new[] { 0, 0, 1, 1, 2, 2 }, e.Select(ev => ev.Index).ToArray());
        Assert.Equal(
            new[] { BatchStepKind.RestorePoint, BatchStepKind.RestorePoint, BatchStepKind.Item, BatchStepKind.Item, BatchStepKind.Item, BatchStepKind.Item },
            e.Select(ev => ev.Kind).ToArray());
        Assert.Equal(
            new[] { BatchStepPhase.Started, BatchStepPhase.Finished, BatchStepPhase.Started, BatchStepPhase.Finished, BatchStepPhase.Started, BatchStepPhase.Finished },
            e.Select(ev => ev.Phase).ToArray());
        Assert.Equal(new string?[] { null, null, "a", "a", "b", "b" }, e.Select(ev => ev.TweakId).ToArray());
        Assert.Null(e[0].RestorePoint);
        Assert.Equal(RestorePointStatus.Created, e[1].RestorePoint!.Status);
        Assert.Null(e[2].Result);
        Assert.Equal(TweakOutcome.Applied, e[3].Result!.Outcome);
        Assert.Equal(TweakOutcome.Applied, e[5].Result!.Outcome);
        Assert.Equal(seenClock, e.Select(ev => ev.At).ToList());
        Assert.True(e[5].At > e[0].At);
    }

    [Fact]
    public void ApplyBatch_SkipPolicy_NoRestorePointStep()
    {
        var f = new EngineFixture();
        var steps = new ListProgress<BatchStepEvent>();

        f.CreateEngine().ApplyBatch(new[] { Item("a"), Item("b") }, Opts(steps, policy: RestorePointPolicy.Skip));

        Assert.All(steps.Items, ev => Assert.Equal(2, ev.Count));
        Assert.Equal(BatchStepKind.Item, steps.Items[0].Kind);
        Assert.Equal(0, steps.Items[0].Index);
        Assert.DoesNotContain(steps.Items, ev => ev.Kind == BatchStepKind.RestorePoint);
    }

    [Fact]
    public void ApplyBatch_StopBetweenItems_SkipsRest()
    {
        var f = new EngineFixture();
        var stop = new BatchStop();
        var steps = new ListProgress<BatchStepEvent>();
        f.Registry.WriteInterceptor = (_, _, _) =>
        {
            stop.Request();
            return null;
        };

        var batch = f.CreateEngine().ApplyBatch(
            new[] { Item("a"), Item("b"), Item("c") }, Opts(steps, stop));

        Assert.Equal(new[] { TweakOutcome.Applied, TweakOutcome.Skipped, TweakOutcome.Skipped }, batch.Results.Select(r => r.Outcome).ToArray());
        Assert.Equal(TweakEngine.StoppedMessage, batch.Results[1].Message);
        Assert.Equal("Pominięte – zatrzymano.", batch.Results[2].Message);
        Assert.True(batch.Stopped);
        Assert.DoesNotContain(steps.Items, ev => ev.TweakId is "b" or "c");
    }

    [Fact]
    public void ApplyBatch_StopDuringRestorePoint_SkipsAllItems()
    {
        var f = new EngineFixture();
        var stop = new BatchStop();
        var steps = new ListProgress<BatchStepEvent>();
        steps.OnReport = ev =>
        {
            if (ev.Kind == BatchStepKind.RestorePoint && ev.Phase == BatchStepPhase.Started)
            {
                stop.Request();
            }
        };

        var batch = f.CreateEngine().ApplyBatch(new[] { Item("a"), Item("b") }, Opts(steps, stop));

        Assert.Equal(RestorePointStatus.Created, batch.RestorePoint!.Status);
        Assert.All(batch.Results, r => Assert.Equal(TweakOutcome.Skipped, r.Outcome));
        Assert.True(batch.Stopped);
        Assert.Empty(f.Registry.WriteLog);
    }

    [Fact]
    public void ApplyBatch_StopDuringLastItem_NotStopped()
    {
        var f = new EngineFixture();
        var stop = new BatchStop();
        var steps = new ListProgress<BatchStepEvent>();
        steps.OnReport = ev =>
        {
            if (ev.TweakId == "b" && ev.Phase == BatchStepPhase.Started)
            {
                stop.Request();
            }
        };

        var batch = f.CreateEngine().ApplyBatch(new[] { Item("a"), Item("b") }, Opts(steps, stop));

        Assert.All(batch.Results, r => Assert.Equal(TweakOutcome.Applied, r.Outcome));
        Assert.False(batch.Stopped);
    }

    [Fact]
    public void ApplyBatch_StopAlreadyRequested_SkipsAll()
    {
        var f = new EngineFixture();
        var stop = new BatchStop();
        stop.Request();
        stop.Request();

        var batch = f.CreateEngine().ApplyBatch(new[] { Item("a"), Item("b") }, Opts(stop: stop));

        Assert.Single(f.RestorePoints.Descriptions);
        Assert.NotNull(batch.RestorePoint);
        Assert.All(batch.Results, r => Assert.Equal(TweakOutcome.Skipped, r.Outcome));
        Assert.True(batch.Stopped);
    }

    [Fact]
    public void ApplyBatch_RequiredRestorePointFails_NoItemEventsNotStopped()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "x");
        var steps = new ListProgress<BatchStepEvent>();

        var batch = f.CreateEngine().ApplyBatch(new[] { Item("a") }, Opts(steps, policy: RestorePointPolicy.Required));

        Assert.True(batch.Aborted);
        Assert.False(batch.Stopped);
        Assert.DoesNotContain(steps.Items, ev => ev.Kind == BatchStepKind.Item);
        Assert.Equal(RestorePointStatus.Failed, steps.Items[^1].RestorePoint!.Status);
    }

    [Fact]
    public void ApplyBatch_StepsReportThrows_BatchContinues()
    {
        var f = new EngineFixture();
        var steps = new ListProgress<BatchStepEvent> { OnReport = _ => throw new InvalidOperationException("ui padło") };

        var batch = f.CreateEngine().ApplyBatch(new[] { Item("a"), Item("b") }, Opts(steps));

        Assert.All(batch.Results, r => Assert.Equal(TweakOutcome.Applied, r.Outcome));
        Assert.Contains(f.Log.Lines, line => line.IndexOf("ui padło", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void UndoBatch_UndoesEachAndReportsSteps()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        var a = Item("a");
        var b = Item("b");
        engine.ApplyBatch(new[] { a, b }, Opts(policy: RestorePointPolicy.Skip));
        var steps = new ListProgress<BatchStepEvent>();

        var batch = engine.UndoBatch(new[] { a, b }, steps);

        Assert.Equal(4, steps.Items.Count);
        Assert.All(steps.Items, ev => Assert.Equal(2, ev.Count));
        Assert.All(steps.Items, ev => Assert.Equal(BatchStepKind.Item, ev.Kind));
        Assert.All(steps.Items, ev => Assert.Null(ev.RestorePoint));
        Assert.Equal(new[] { 0, 0, 1, 1 }, steps.Items.Select(ev => ev.Index).ToArray());
        Assert.Equal(
            new[] { BatchStepPhase.Started, BatchStepPhase.Finished, BatchStepPhase.Started, BatchStepPhase.Finished },
            steps.Items.Select(ev => ev.Phase).ToArray());
        Assert.Equal(2, batch.Results.Count);
        Assert.Null(batch.RestorePoint);
        Assert.False(batch.Stopped);
        Assert.Empty(f.RestorePoints.Descriptions);
    }

    [Fact]
    public void UndoBatch_ExceptionInOne_ContinuesWithRest()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        var a = Item("a");
        var b = Item("b");
        engine.ApplyBatch(new[] { a, b }, Opts(policy: RestorePointPolicy.Skip));
        var throwing = new TweakEngine(
            new EngineServices
            {
                Registry = f.Registry,
                Services = f.Services,
                Scripts = f.Scripts,
                Launcher = f.Launcher,
                Journal = f.Journal,
                RestorePoints = f.RestorePoints,
                SystemInfo = f.SystemInfo,
                User = f.User,
                Log = new ThrowingOnStartLog(f.Log, "a", "Undo"),
                Clock = f.Clock,
            },
            "sha256:test");

        var batch = throwing.UndoBatch(new[] { a, b });

        Assert.Equal(2, batch.Results.Count);
        Assert.Equal(TweakOutcome.Failed, batch.Results[0].Outcome);
        Assert.Contains("Nieoczekiwany błąd: log padł", batch.Results[0].Message, StringComparison.Ordinal);
        Assert.NotEqual(TweakOutcome.Failed, batch.Results[1].Outcome);
    }

    [Fact]
    public void UndoBatch_Stop_SkipsRest()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        var a = Item("a");
        var b = Item("b");
        engine.ApplyBatch(new[] { a, b }, Opts(policy: RestorePointPolicy.Skip));
        var stop = new BatchStop();
        f.Registry.WriteInterceptor = (_, _, _) =>
        {
            stop.Request();
            return null;
        };

        var batch = engine.UndoBatch(new[] { a, b }, stop: stop);

        Assert.Equal(TweakOutcome.Skipped, batch.Results[1].Outcome);
        Assert.Equal(TweakEngine.StoppedMessage, batch.Results[1].Message);
        Assert.True(batch.Stopped);
    }

    /// <summary>Rzuca z Info("Apply &lt;id&gt;: start") dla wskazanego tweaka – symuluje nieoczekiwany wyjątek w Apply.</summary>
    private sealed class ThrowingOnStartLog : ILog
    {
        private readonly ILog _inner;
        private readonly string _tweakId;
        private readonly string _verb;

        public ThrowingOnStartLog(ILog inner, string tweakId, string verb = "Apply")
        {
            _inner = inner;
            _tweakId = tweakId;
            _verb = verb;
        }

        public void Info(string component, string message)
        {
            if (message.IndexOf($"{_verb} {_tweakId}: start", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException("log padł");
            }

            _inner.Info(component, message);
        }

        public void Warn(string component, string message) => _inner.Warn(component, message);

        public void Fail(string component, string message, Exception? exception = null) => _inner.Fail(component, message, exception);
    }
}
