using System.Collections.Generic;
using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Synchroniczne odczyty sprzętu; każdy wywoływany osobno (i może się zawiesić), dlatego loader daje im limit czasu.</summary>
public interface IHardwareReader
{
    CpuInfo ReadProcessor();

    MemoryInfo ReadMemory();

    BoardInfo ReadBoard();

    IReadOnlyList<GpuInfo> ReadGraphics();

    IReadOnlyList<DisplayInfo> ReadDisplays();

    IReadOnlyList<string> ReadSound();

    IReadOnlyList<DiskInfo> ReadDisks();

    IReadOnlyList<NetworkAdapterInfo> ReadNetwork();

    WindowsInfo ReadWindows();

    /// <summary><c>null</c> = komputer bez TPM.</summary>
    TpmInfo? ReadTpm();

    SecureBootState ReadSecureBoot();

    VbsInfo ReadVbs();

    IReadOnlyList<BitLockerVolume> ReadBitLocker();
}
