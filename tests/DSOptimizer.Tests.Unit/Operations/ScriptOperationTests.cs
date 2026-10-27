using System;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Operations;

public class ScriptOperationTests
{
    private static readonly ScriptSet Full = new() { Apply = "APPLY", Undo = "UNDO", Detect = "DETECT", Capture = "CAPTURE" };
    private readonly FakeScriptRunner _runner = new();

    private ScriptOperation Operation(ScriptSet? script = null)
        => new(script ?? Full, _runner, "S-1-5-21-7", TimeSpan.FromSeconds(30), output: null);

    [Theory]
    [InlineData("Applied", OperationState.Match)]
    [InlineData("NotApplied", OperationState.Mismatch)]
    [InlineData("NotSupported", OperationState.NotSupported)]
    public void Detect_MapsLastLine(string line, OperationState expected)
    {
        _runner.On("DETECT", 0, "log", line);
        Assert.Equal(expected, Operation().Detect(CancellationToken.None));
    }

    [Theory]
    [InlineData("applied")]
    [InlineData("Applied!")]
    [InlineData("")]
    public void Detect_UnexpectedOutput_IsUnknown(string line)
    {
        _runner.On("DETECT", 0, line);
        Assert.Equal(OperationState.Unknown, Operation().Detect(CancellationToken.None));
    }

    [Fact]
    public void Detect_TrimmedOutput_IsMatch()
    {
        _runner.On("DETECT", 0, "  Applied  ", "   ");
        Assert.Equal(OperationState.Match, Operation().Detect(CancellationToken.None));
    }

    [Fact]
    public void Detect_NonZeroExitOrRunnerFailure_IsUnknown()
    {
        _runner.On("DETECT", 1, "Applied");
        Assert.Equal(OperationState.Unknown, Operation().Detect(CancellationToken.None));
        _runner.Throw("DETECT", OperationErrorKind.Timeout);
        Assert.Equal(OperationState.Unknown, Operation().Detect(CancellationToken.None));
    }

    [Fact]
    public void Detect_WithoutScript_DoesNotParticipate()
    {
        var operation = Operation(Full with { Detect = null });
        Assert.False(operation.ParticipatesInDetection);
        Assert.Equal(OperationState.NotApplicable, operation.Detect(CancellationToken.None));
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void Capture_StoresLastLine()
    {
        _runner.On("CAPTURE", 0, "noise", "2");
        Assert.Equal("2", Operation().Capture(CancellationToken.None).Capture);
        Assert.Null(Operation(Full with { Capture = null }).Capture(CancellationToken.None).Capture);
    }

    [Fact]
    public void Capture_Failure_Throws()
    {
        _runner.On("CAPTURE", 2);
        Assert.Equal(OperationErrorKind.ScriptFailed, Assert.Throws<OperationException>(() => Operation().Capture(CancellationToken.None)).Kind);
    }

    [Fact]
    public void Apply_PassesUserSidAndTimeout()
    {
        _runner.On("APPLY", 0);
        Operation().Apply(CancellationToken.None);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("S-1-5-21-7", call.UserSid);
        Assert.Equal(TimeSpan.FromSeconds(30), call.Timeout);
    }

    [Fact]
    public void Apply_NonZeroExit_ThrowsWithStderr()
    {
        _runner.On("APPLY", 3);
        var ex = Assert.Throws<OperationException>(() => Operation().Apply(CancellationToken.None));
        Assert.Equal(OperationErrorKind.ScriptFailed, ex.Kind);
        Assert.Contains("fake stderr", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_PassesSnapshotToUndo()
    {
        _runner.On("UNDO", 0);
        Operation().Restore(SnapshotRecord.ForScript("2"), CancellationToken.None);
        Assert.Equal("2", _runner.Calls.Single().Snapshot);
    }

    [Fact]
    public void Restore_WithoutUndo_ThrowsInsteadOfPretendingToRestore()
    {
        var operation = Operation(Full with { Undo = null });

        var ex = Assert.Throws<OperationException>(() => operation.Restore(SnapshotRecord.ForScript("2"), CancellationToken.None));

        Assert.Contains("undo", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void DetectOnlyAndDefaults_DependOnScripts()
    {
        Assert.True(Operation(new ScriptSet { Detect = "DETECT" }).IsDetectOnly);
        Assert.False(Operation().IsDetectOnly);
        Assert.NotNull(Operation().DefaultsSnapshot());
        Assert.Null(Operation(Full with { Undo = null }).DefaultsSnapshot());
    }
}
