using System;
using System.Collections.Generic;
using System.Threading;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Engine;

public class ActionHandlerExecuteTests
{
    private sealed class FakeHandler : IActionHandler
    {
        public FakeHandler(string name) => Name = name;

        public string Name { get; }

        public Func<TweakDefinition, IProgress<string>?, ActionHandlerResult> OnRun { get; set; }
            = (_, _) => new ActionHandlerResult { Outcome = TweakOutcome.Executed };

        public List<(TweakDefinition Tweak, IProgress<string>? Output, IProgress<ActionProgress>? Progress)> Calls { get; } = new();

        public ActionHandlerResult Run(TweakDefinition tweak, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
        {
            Calls.Add((tweak, output, progress));
            return OnRun(tweak, output);
        }
    }

    private static TweakDefinition HandlerAction(string handler = "wu-reset") => TestTweaks.Action() with { Script = null, Handler = handler };

    private static (EngineFixture Fixture, FakeHandler Handler, TweakEngine Engine) Setup(string name = "wu-reset")
    {
        var fixture = new EngineFixture();
        var handler = new FakeHandler(name);
        fixture.Handlers = new ActionHandlerSet(new[] { handler });
        return (fixture, handler, fixture.CreateEngine());
    }

    [Fact]
    public void Execute_Handler_CallsHandlerWithSameDefinitionAndOutput()
    {
        var (_, handler, engine) = Setup();
        var definition = HandlerAction();
        var progress = new ListProgress();

        var result = engine.Execute(definition, progress);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        var call = Assert.Single(handler.Calls);
        Assert.Same(definition, call.Tweak);
        Assert.Same(progress, call.Output);
    }

    [Fact]
    public void Execute_Handler_PassesActionProgressByReference()
    {
        var (_, handler, engine) = Setup();
        var output = new ListProgress();
        var progress = new ListProgress<ActionProgress>();

        engine.Execute(HandlerAction(), output, progress, CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Same(progress, call.Progress);
        Assert.Same(output, call.Output);
    }

    [Fact]
    public void Execute_OldSignature_PassesNullActionProgress()
    {
        var (_, handler, engine) = Setup();

        engine.Execute(HandlerAction(), new ListProgress(), CancellationToken.None);

        Assert.Null(Assert.Single(handler.Calls).Progress);
    }

    [Fact]
    public void Execute_ScriptTimeout_LogsOutputCollectedSoFar()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", _ => throw new OperationException(OperationErrorKind.Timeout, "Limit czasu."));
        f.Scripts.BeforeThrow = output =>
        {
            output?.Report("linia 1");
            output?.Report("linia 2");
        };
        var forwarded = new ListProgress();

        var result = f.CreateEngine().Execute(TestTweaks.Action(), forwarded);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(new[] { "linia 1", "linia 2" }, forwarded.Items);
        var logged = Assert.Single(f.Log.Lines, line => line.Contains("wyjście skryptu przed błędem:"));
        Assert.Contains("linia 1", logged, StringComparison.Ordinal);
        Assert.Contains("linia 2", logged, StringComparison.Ordinal);
        Assert.Contains(TestTweaks.Action().Id, logged, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ScriptTimeoutWithoutOutput_LogsNothingExtra()
    {
        var f = new EngineFixture();
        f.Scripts.Throw("RUN", OperationErrorKind.Timeout);

        f.CreateEngine().Execute(TestTweaks.Action());

        Assert.DoesNotContain(f.Log.Lines, line => line.Contains("wyjście skryptu przed błędem"));
    }

    [Fact]
    public void Execute_Handler_RequiresRebootFromHandlerPropagates()
    {
        var (_, handler, engine) = Setup();
        handler.OnRun = (_, _) => new ActionHandlerResult { Outcome = TweakOutcome.Executed, RequiresReboot = true };

        Assert.True(engine.Execute(HandlerAction()).RequiresReboot);
    }

    [Fact]
    public void Execute_Handler_ExecutedWithoutFlag_UsesCatalogRequiresReboot()
    {
        var (_, _, engine) = Setup();

        Assert.True(engine.Execute(HandlerAction() with { RequiresReboot = true }).RequiresReboot);
        Assert.False(engine.Execute(HandlerAction()).RequiresReboot);
    }

    [Theory]
    [InlineData(TweakOutcome.Failed)]
    [InlineData(TweakOutcome.Refused)]
    [InlineData(TweakOutcome.NotConfirmed)]
    [InlineData(TweakOutcome.NotSupported)]
    public void Execute_Handler_NotExecuted_NeverCarriesReboot(TweakOutcome outcome)
    {
        var (_, handler, engine) = Setup();
        handler.OnRun = (_, _) => new ActionHandlerResult { Outcome = outcome, RequiresReboot = true };

        Assert.False(engine.Execute(HandlerAction() with { RequiresReboot = true }).RequiresReboot);
    }

    [Fact]
    public void Execute_Handler_CopiesWarningsNotRestoredAndMessage()
    {
        var (_, handler, engine) = Setup();
        handler.OnRun = (_, _) => new ActionHandlerResult
        {
            Outcome = TweakOutcome.Executed,
            Message = "zrobione",
            Warnings = new[] { "uwaga" },
            NotRestored = new[] { "usługa X" },
        };

        var result = engine.Execute(HandlerAction());

        Assert.Equal("zrobione", result.Message);
        Assert.Equal(new[] { "uwaga" }, result.Warnings);
        Assert.Equal(new[] { "usługa X" }, result.NotRestored);
        Assert.False(result.RequiresReboot);
    }

    [Theory]
    [InlineData(TweakOutcome.Failed)]
    [InlineData(TweakOutcome.Refused)]
    [InlineData(TweakOutcome.NotConfirmed)]
    [InlineData(TweakOutcome.NotSupported)]
    public void Execute_Handler_AllowedOutcomesPassThrough(TweakOutcome outcome)
    {
        var (_, handler, engine) = Setup();
        handler.OnRun = (_, _) => new ActionHandlerResult { Outcome = outcome, Message = "m" };

        var result = engine.Execute(HandlerAction());

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal("m", result.Message);
    }

    [Fact]
    public void Execute_Handler_DisallowedOutcome_ThrowsArgumentException()
    {
        var (_, handler, engine) = Setup();
        handler.OnRun = (_, _) => new ActionHandlerResult { Outcome = TweakOutcome.Applied };

        Assert.Throws<ArgumentException>(() => engine.Execute(HandlerAction()));
    }

    [Fact]
    public void Execute_HandlerMissingInSet_IsRefused()
    {
        var f = new EngineFixture();

        var result = f.CreateEngine().Execute(HandlerAction("x"));

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal("Brak implementacji akcji 'x'.", result.Message);
    }

    [Fact]
    public void Execute_HandlerThrowsUnexpectedException_FailsAndLogsError()
    {
        var (f, handler, engine) = Setup();
        handler.OnRun = (_, _) => throw new InvalidOperationException("handler boom");

        var result = engine.Execute(HandlerAction());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("handler boom", result.Message);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal) && line.Contains("handler boom"));
    }

    [Fact]
    public void Execute_HandlerThrowsOperationException_FailsWithoutErrorLogEntry()
    {
        var (f, handler, engine) = Setup();
        handler.OnRun = (_, _) => throw new OperationException(OperationErrorKind.Unexpected, "znany błąd");

        var result = engine.Execute(HandlerAction());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("znany błąd", result.Message);
        Assert.DoesNotContain(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_HandlerAfterCancellation_FailsWithoutCallingHandler()
    {
        var (_, handler, engine) = Setup();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = engine.Execute(HandlerAction(), null, cts.Token);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Anulowano przed uruchomieniem.", result.Message);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void Execute_HandlerTogetherWithLaunchOrApply_IsRefusedWithoutRunning()
    {
        var (f, handler, engine) = Setup();

        var withLaunch = engine.Execute(TestTweaks.Panel() with { Handler = "wu-reset" });
        var withApply = engine.Execute(TestTweaks.Action() with { Handler = "wu-reset" });

        Assert.Equal(TweakOutcome.Refused, withLaunch.Outcome);
        Assert.Equal(TweakOutcome.Refused, withApply.Outcome);
        Assert.Empty(handler.Calls);
        Assert.Empty(f.Launcher.Calls);
        Assert.Empty(f.Scripts.Calls);
    }

    [Fact]
    public void Execute_TweakWithHandler_IsRefusedWithoutRunning()
    {
        var (_, handler, engine) = Setup();

        var result = engine.Execute(TestTweaks.TwoValues() with { Handler = "wu-reset" });

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void Execute_ScriptActionWithRequiresReboot_PropagatesOnSuccessOnly()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0);
        var action = TestTweaks.Action() with { RequiresReboot = true };

        Assert.True(f.CreateEngine().Execute(action).RequiresReboot);

        var failing = new EngineFixture();
        failing.Scripts.On("RUN", 1);
        Assert.False(failing.CreateEngine().Execute(action).RequiresReboot);
    }

    [Fact]
    public void ActionHandlerSet_DuplicateName_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(() => new ActionHandlerSet(new[] { new FakeHandler("a"), new FakeHandler("a") }));

    [Fact]
    public void ActionHandlerSet_ExposesNamesAndLookup()
    {
        var set = new ActionHandlerSet(new[] { new FakeHandler("a"), new FakeHandler("b") });

        Assert.True(set.TryGet("a", out var handler));
        Assert.Equal("a", handler.Name);
        Assert.False(set.TryGet("c", out _));
        Assert.Equal(2, set.Names.Count);
        Assert.Empty(ActionHandlerSet.Empty.Names);
    }

    [Fact]
    public void ActionHandlerNames_AllContainsKnownNames()
        => Assert.Equal(new[] { "wu-reset", "wu-reset-full", "restore-point", "system-files", "disk-cleanup", "windows-old", "winutil" }, ActionHandlerNames.All);

    [Fact]
    public void ActionHandlerNames_LongRunningAreTheThreeServicingRepairs()
        => Assert.Equal(new[] { "system-files", "disk-cleanup", "windows-old" }, ActionHandlerNames.LongRunning);
}
