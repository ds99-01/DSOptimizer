using System;
using System.Threading;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Engine;

public class EngineExecuteTests
{
    [Fact]
    public void Execute_Success_ReturnsExecutedAndStreamsOutput()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0, "ok");
        var progress = new ListProgress();

        var result = f.CreateEngine().Execute(TestTweaks.Action(), progress);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal(new[] { "ok" }, progress.Items);
        var call = Assert.Single(f.Scripts.Calls);
        Assert.Equal(f.User.Sid, call.UserSid);
        Assert.Equal(TweakDefinition.DefaultTimeout, call.Timeout);
    }

    [Fact]
    public void Execute_Success_PutsScriptStdoutInMessage_WithoutTrailingEmptyLines()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0, "pierwsza", "druga", string.Empty, string.Empty);

        var result = f.CreateEngine().Execute(TestTweaks.Action());

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("pierwsza" + Environment.NewLine + "druga", result.Message);
    }

    [Fact]
    public void Execute_SuccessWithoutOutput_HasNoMessage()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0);

        Assert.Null(f.CreateEngine().Execute(TestTweaks.Action()).Message);
    }

    [Fact]
    public void Execute_NonZeroExit_FailsWithCode()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 3);

        var result = f.CreateEngine().Execute(TestTweaks.Action());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("3", result.Message, StringComparison.Ordinal);
        Assert.Contains("fake stderr", result.Message, StringComparison.Ordinal); // Failed nadal niesie stderr, nie stdout
    }

    [Fact]
    public void Execute_NonZeroExit_WritesScriptStdoutToLog_ButMessageKeepsOnlyStderr()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 1, "Konfiguracja IP przed resetem:", "IPv4 192.168.1.10", string.Empty);

        var result = f.CreateEngine().Execute(TestTweaks.Action());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("192.168.1.10", result.Message, StringComparison.Ordinal);
        var logged = Assert.Single(f.Log.Lines, line => line.Contains("wyjście skryptu przed błędem"));
        Assert.StartsWith("INFO", logged, StringComparison.Ordinal);
        Assert.Contains(TestTweaks.Action().Id + ": wyjście skryptu przed błędem:", logged, StringComparison.Ordinal);
        Assert.Contains("Konfiguracja IP przed resetem:" + Environment.NewLine + "IPv4 192.168.1.10", logged, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NonZeroExitWithoutStdout_LogsNoScriptOutput()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 1, string.Empty);

        f.CreateEngine().Execute(TestTweaks.Action());

        Assert.DoesNotContain(f.Log.Lines, line => line.Contains("wyjście skryptu przed błędem"));
    }

    [Fact]
    public void Execute_Success_DoesNotLogScriptOutput()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0, "zmieniono");

        var result = f.CreateEngine().Execute(TestTweaks.Action());

        Assert.Equal("zmieniono", result.Message);
        Assert.DoesNotContain(f.Log.Lines, line => line.Contains("wyjście skryptu"));
    }

    [Fact]
    public void Execute_RunnerTimeout_Fails()
    {
        var f = new EngineFixture();
        f.Scripts.Throw("RUN", OperationErrorKind.Timeout);

        Assert.Equal(TweakOutcome.Failed, f.CreateEngine().Execute(TestTweaks.Action()).Outcome);
    }

    [Fact]
    public void Execute_RunnerThrowsUnexpectedException_FailsWithoutThrowing()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", _ => throw new InvalidOperationException("runner boom"));

        var result = f.CreateEngine().Execute(TestTweaks.Action());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("runner boom", result.Message, StringComparison.Ordinal);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal) && line.Contains("runner boom"));
    }

    [Fact]
    public void Execute_OnTweakOrWithoutApply_IsRefused()
    {
        var engine = new EngineFixture().CreateEngine();
        Assert.Equal(TweakOutcome.Refused, engine.Execute(TestTweaks.TwoValues()).Outcome);
        Assert.Equal(TweakOutcome.Refused, engine.Execute(TestTweaks.Action() with { Script = new ScriptSet() }).Outcome);
    }
    [Fact]
    public void Execute_Launch_CallsLauncherWithoutScriptAndReturnsExecuted()
    {
        var f = new EngineFixture();

        var result = f.CreateEngine().Execute(TestTweaks.Panel());

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Uruchomiono compmgmt.msc", result.Message);
        var call = Assert.Single(f.Launcher.Calls);
        Assert.Equal("compmgmt.msc", call.File);
        Assert.Equal("/s", call.Arguments);
        Assert.Empty(f.Scripts.Calls);
    }

    [Fact]
    public void Execute_LaunchOnAction_IsSupported()
    {
        var f = new EngineFixture();
        var action = TestTweaks.Action() with { Script = null, Launch = new LaunchSpec { File = "powershell.exe", Arguments = "-NoProfile" } };

        var result = f.CreateEngine().Execute(action);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Uruchomiono powershell.exe", result.Message);
        Assert.Single(f.Launcher.Calls);
    }

    [Fact]
    public void Execute_LaunchFails_FailedWithLauncherMessage()
    {
        var f = new EngineFixture();
        f.Launcher.Failure = new OperationException(OperationErrorKind.Unexpected, "Nie można uruchomić compmgmt.msc: Odmowa dostępu");

        var result = f.CreateEngine().Execute(TestTweaks.Panel());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("Nie można uruchomić compmgmt.msc: Odmowa dostępu", result.Message);
        Assert.DoesNotContain(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_LauncherThrowsUnexpectedException_FailsAndLogsError()
    {
        var f = new EngineFixture();
        f.Launcher.Failure = new InvalidOperationException("launcher boom");

        var result = f.CreateEngine().Execute(TestTweaks.Panel());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("launcher boom", result.Message, StringComparison.Ordinal);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal) && line.Contains("launcher boom"));
    }

    [Fact]
    public void Execute_LaunchAfterCancellation_FailsWithoutLaunching()
    {
        var f = new EngineFixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = f.CreateEngine().Execute(TestTweaks.Panel(), null, cts.Token);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Empty(f.Launcher.Calls);
    }

    [Fact]
    public void Execute_LaunchTogetherWithApply_IsRefusedWithoutRunningAnything()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0);

        var result = f.CreateEngine().Execute(TestTweaks.Panel() with { Script = new ScriptSet { Apply = "RUN" } });

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Empty(f.Launcher.Calls);
        Assert.Empty(f.Scripts.Calls);
    }

    [Fact]
    public void Execute_TweakWithLaunch_IsRefusedWithoutLaunching()
    {
        var f = new EngineFixture();

        var result = f.CreateEngine().Execute(TestTweaks.TwoValues() with { Launch = new LaunchSpec { File = "control.exe" } });

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Empty(f.Launcher.Calls);
    }
}
