using System;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Engine;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

public class CliExitCodesTests
{
    [Theory]
    [InlineData(TweakOutcome.Applied, CliExitCode.Success)]
    [InlineData(TweakOutcome.AlreadyApplied, CliExitCode.Success)]
    [InlineData(TweakOutcome.PendingReboot, CliExitCode.Success)]
    [InlineData(TweakOutcome.Undone, CliExitCode.Success)]
    [InlineData(TweakOutcome.UndoneFromDefaults, CliExitCode.Success)]
    [InlineData(TweakOutcome.Executed, CliExitCode.Success)]
    [InlineData(TweakOutcome.Failed, CliExitCode.OperationFailed)]
    [InlineData(TweakOutcome.FailedRolledBack, CliExitCode.OperationFailed)]
    [InlineData(TweakOutcome.Refused, CliExitCode.OperationFailed)]
    [InlineData(TweakOutcome.NotSupported, CliExitCode.OperationFailed)]
    [InlineData(TweakOutcome.Skipped, CliExitCode.OperationFailed)]
    [InlineData(TweakOutcome.FailedRollbackIncomplete, CliExitCode.Incomplete)]
    [InlineData(TweakOutcome.UndoIncomplete, CliExitCode.Incomplete)]
    [InlineData(TweakOutcome.Removed, CliExitCode.Success)]
    [InlineData(TweakOutcome.NotConfirmed, CliExitCode.Incomplete)]
    public void FromOutcome_MapsEveryOutcome(TweakOutcome outcome, CliExitCode expected)
        => Assert.Equal(expected, CliExitCodes.FromOutcome(outcome));

    [Fact]
    public void FromOutcome_AllEnumValuesMapped()
    {
        foreach (TweakOutcome outcome in Enum.GetValues(typeof(TweakOutcome)))
        {
            Assert.True(Enum.IsDefined(typeof(CliExitCode), CliExitCodes.FromOutcome(outcome)));
        }
    }

    [Fact]
    public void FromOutcome_UnknownValue_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => CliExitCodes.FromOutcome((TweakOutcome)999));

    [Theory]
    [InlineData(TweakOutcome.Installed, CliExitCode.Success)]
    [InlineData(TweakOutcome.AlreadyInstalled, CliExitCode.Success)]
    [InlineData(TweakOutcome.InstalledUnverified, CliExitCode.Incomplete)]
    public void FromOutcome_InstallOutcomes(TweakOutcome outcome, CliExitCode expected)
        => Assert.Equal(expected, CliExitCodes.FromOutcome(outcome));
}
