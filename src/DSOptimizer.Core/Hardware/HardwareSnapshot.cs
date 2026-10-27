using System.Collections.Generic;

namespace DSOptimizer.Core.Hardware;

/// <summary>Odczyty wszystkich części sprzętu; każda część domyślnie czeka (<see cref="ReadingState.Pending"/>).</summary>
public sealed record HardwareSnapshot
{
    public Reading<CpuInfo> Processor { get; init; } = Reading<CpuInfo>.Pending;

    public Reading<MemoryInfo> Memory { get; init; } = Reading<MemoryInfo>.Pending;

    public Reading<BoardInfo> Board { get; init; } = Reading<BoardInfo>.Pending;

    public Reading<IReadOnlyList<GpuInfo>> Graphics { get; init; } = Reading<IReadOnlyList<GpuInfo>>.Pending;

    public Reading<IReadOnlyList<DisplayInfo>> Displays { get; init; } = Reading<IReadOnlyList<DisplayInfo>>.Pending;

    public Reading<IReadOnlyList<string>> Sound { get; init; } = Reading<IReadOnlyList<string>>.Pending;

    public Reading<IReadOnlyList<DiskInfo>> Disks { get; init; } = Reading<IReadOnlyList<DiskInfo>>.Pending;

    public Reading<IReadOnlyList<NetworkAdapterInfo>> Network { get; init; } = Reading<IReadOnlyList<NetworkAdapterInfo>>.Pending;

    public Reading<WindowsInfo> Windows { get; init; } = Reading<WindowsInfo>.Pending;

    /// <summary><c>Done(null)</c> = komputer bez TPM.</summary>
    public Reading<TpmInfo?> Tpm { get; init; } = Reading<TpmInfo?>.Pending;

    public Reading<SecureBootState> SecureBoot { get; init; } = Reading<SecureBootState>.Pending;

    public Reading<VbsInfo> Vbs { get; init; } = Reading<VbsInfo>.Pending;

    public Reading<IReadOnlyList<BitLockerVolume>> BitLocker { get; init; } = Reading<IReadOnlyList<BitLockerVolume>>.Pending;

    /// <summary>Żadna część nie czeka już na odczyt.</summary>
    public bool IsComplete =>
        Processor.State != ReadingState.Pending
        && Memory.State != ReadingState.Pending
        && Board.State != ReadingState.Pending
        && Graphics.State != ReadingState.Pending
        && Displays.State != ReadingState.Pending
        && Sound.State != ReadingState.Pending
        && Disks.State != ReadingState.Pending
        && Network.State != ReadingState.Pending
        && Windows.State != ReadingState.Pending
        && Tpm.State != ReadingState.Pending
        && SecureBoot.State != ReadingState.Pending
        && Vbs.State != ReadingState.Pending
        && BitLocker.State != ReadingState.Pending;
}
