using System.Collections.Generic;
using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Core.Prep;

/// <summary>Odczyty strony „Przygotowanie”; każda część domyślnie czeka (<see cref="ReadingState.Pending"/>).</summary>
public sealed record PrepSnapshot
{
    public Reading<BoardInfo> Board { get; init; } = Reading<BoardInfo>.Pending;

    public Reading<PrepProduct> Product { get; init; } = Reading<PrepProduct>.Pending;

    public Reading<IReadOnlyList<BitLockerVolume>> BitLocker { get; init; } = Reading<IReadOnlyList<BitLockerVolume>>.Pending;

    public Reading<ActivationInfo> Activation { get; init; } = Reading<ActivationInfo>.Pending;

    public Reading<IReadOnlyList<PrepGpu>> Graphics { get; init; } = Reading<IReadOnlyList<PrepGpu>>.Pending;

    public Reading<IReadOnlyList<ProblemDevice>> Devices { get; init; } = Reading<IReadOnlyList<ProblemDevice>>.Pending;

    /// <summary>Żadna część nie czeka już na odczyt.</summary>
    public bool IsComplete =>
        Board.State != ReadingState.Pending
        && Product.State != ReadingState.Pending
        && BitLocker.State != ReadingState.Pending
        && Activation.State != ReadingState.Pending
        && Graphics.State != ReadingState.Pending
        && Devices.State != ReadingState.Pending;
}
