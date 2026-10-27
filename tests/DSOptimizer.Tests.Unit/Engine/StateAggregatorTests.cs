using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using Xunit;

namespace DSOptimizer.Tests.Unit.Engine;

public class StateAggregatorTests
{
    [Theory]
    [InlineData(new[] { OperationState.Match, OperationState.Match }, DetectionState.Applied)]
    [InlineData(new[] { OperationState.Mismatch, OperationState.Mismatch }, DetectionState.NotApplied)]
    [InlineData(new[] { OperationState.Match, OperationState.Mismatch }, DetectionState.Partial)]
    [InlineData(new[] { OperationState.Match, OperationState.NotApplicable }, DetectionState.Applied)]
    [InlineData(new[] { OperationState.NotApplicable }, DetectionState.NotSupported)]
    [InlineData(new OperationState[0], DetectionState.NotSupported)]
    [InlineData(new[] { OperationState.Match, OperationState.Unknown }, DetectionState.Unknown)]
    [InlineData(new[] { OperationState.Unknown, OperationState.NotSupported }, DetectionState.NotSupported)]
    [InlineData(new[] { OperationState.Match, OperationState.NotSupported }, DetectionState.NotSupported)]
    public void Aggregate_FollowsSpecTable(OperationState[] states, DetectionState expected)
        => Assert.Equal(expected, StateAggregator.Aggregate(states));
}
