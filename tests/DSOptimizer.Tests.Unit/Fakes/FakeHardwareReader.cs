using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Domyślnie zwraca stałe wartości; każdy odczyt można podmienić (rzucający albo blokujący).</summary>
public sealed class FakeHardwareReader : IHardwareReader
{
    public Func<CpuInfo> Processor { get; set; } = () => new CpuInfo("Ryzen 7 9800X3D", 8, 16);

    public Func<MemoryInfo> Memory { get; set; } = () => new MemoryInfo(Array.Empty<MemoryModule>(), 4);

    public Func<BoardInfo> Board { get; set; } = () => new BoardInfo("ASUS", "ROG", "1.0", null, FirmwareKind.Uefi, null, null);

    public Func<IReadOnlyList<GpuInfo>> Graphics { get; set; } = () => new[] { new GpuInfo("RTX 5080", 17179869184UL, "1.0") };

    public Func<IReadOnlyList<DisplayInfo>> Displays { get; set; } = () => new[] { new DisplayInfo("Monitor", 60, 34, 2560, 1440, 144) };

    public Func<IReadOnlyList<string>> Sound { get; set; } = () => new[] { "Realtek" };

    public Func<IReadOnlyList<DiskInfo>> Disks { get; set; } = () => new[] { new DiskInfo(0, "NVMe", 1000204886016UL, 17, 4, 40, null, 0) };

    public Func<IReadOnlyList<NetworkAdapterInfo>> Network { get; set; } = () => new[] { new NetworkAdapterInfo("Ethernet", NetworkKind.Ethernet, true, 1000000000UL) };

    public Func<WindowsInfo> Windows { get; set; } = () => new WindowsInfo("Pro", "26H2", 26300, 1, null);

    public Func<TpmInfo?> Tpm { get; set; } = () => new TpmInfo("2.0", true, true);

    public Func<SecureBootState> SecureBoot { get; set; } = () => SecureBootState.On;

    public Func<VbsInfo> Vbs { get; set; } = () => new VbsInfo(2, true);

    public Func<IReadOnlyList<BitLockerVolume>> BitLocker { get; set; } = () => new[] { new BitLockerVolume("C:", 1, 0) };

    public CpuInfo ReadProcessor() => Processor();

    public MemoryInfo ReadMemory() => Memory();

    public BoardInfo ReadBoard() => Board();

    public IReadOnlyList<GpuInfo> ReadGraphics() => Graphics();

    public IReadOnlyList<DisplayInfo> ReadDisplays() => Displays();

    public IReadOnlyList<string> ReadSound() => Sound();

    public IReadOnlyList<DiskInfo> ReadDisks() => Disks();

    public IReadOnlyList<NetworkAdapterInfo> ReadNetwork() => Network();

    public WindowsInfo ReadWindows() => Windows();

    public TpmInfo? ReadTpm() => Tpm();

    public SecureBootState ReadSecureBoot() => SecureBoot();

    public VbsInfo ReadVbs() => Vbs();

    public IReadOnlyList<BitLockerVolume> ReadBitLocker() => BitLocker();
}
