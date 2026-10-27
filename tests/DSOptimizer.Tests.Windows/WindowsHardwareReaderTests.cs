using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Windows.Hardware;
using DSOptimizer.Windows.Native;
using Xunit;
using Xunit.Abstractions;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Tylko odczyt sprzętu hosta (CIM, rejestr bez zapisu, user32, GetFirmwareType); bez asercji konkretnego sprzętu.
/// Reguły dopasowania i czyszczenia wartości są sprawdzane na danych z dokumentu źródeł (bez systemu).
/// </summary>
[Trait("Category", "Windows")]
public class WindowsHardwareReaderTests
{
    private readonly ITestOutputHelper _output;

    public WindowsHardwareReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ---------- odczyt hosta ----------

    [Fact]
    public void ReadProcessor_HostHasCoresAndThreads()
    {
        var cpu = new WindowsHardwareReader().ReadProcessor();

        Assert.False(string.IsNullOrWhiteSpace(cpu.Name));
        Assert.True(cpu.Cores >= 1);
        Assert.True(cpu.Threads >= cpu.Cores);
    }

    [Fact]
    public void ReadMemory_HostHasModules()
    {
        var memory = new WindowsHardwareReader().ReadMemory();

        Assert.NotEmpty(memory.Modules);
        Assert.True(memory.Modules.Sum(m => (decimal)(m.CapacityBytes ?? 0)) > 0);
    }

    [Fact]
    public void ReadWindows_BuildAtLeast22000()
    {
        var windows = new WindowsHardwareReader().ReadWindows();

        Assert.True(windows.Build >= 22000);
        Assert.False(string.IsNullOrWhiteSpace(windows.DisplayVersion));
    }

    [Fact]
    public void ReadDisks_AtLeastOneWithSize()
    {
        var disks = new WindowsHardwareReader().ReadDisks();

        Assert.Contains(disks, d => d.SizeBytes > 0);
    }

    [Fact]
    public void EveryRead_DoesNotThrowOnHost()
    {
        var reader = new WindowsHardwareReader();

        _ = reader.ReadProcessor();
        _ = reader.ReadMemory();
        _ = reader.ReadBoard();
        _ = reader.ReadGraphics();
        _ = reader.ReadDisplays();
        _ = reader.ReadSound();
        _ = reader.ReadDisks();
        _ = reader.ReadNetwork();
        _ = reader.ReadWindows();
        _ = reader.ReadTpm();
        _ = reader.ReadSecureBoot();
        _ = reader.ReadVbs();
        _ = reader.ReadBitLocker();
    }

    [Fact]
    public async Task HostReadout_PrintsAllParts()
    {
        var loader = new SpecificationLoader(new WindowsHardwareReader(), new OutputLog(_output), SpecificationLoader.DefaultTimeout);

        var snapshot = await loader.LoadAsync(static _ => { });

        Assert.True(snapshot.IsComplete);
        _output.WriteLine(SpecificationFormatter.Report(snapshot, Environment.MachineName, DateTime.Now));
    }

    // ---------- reguły z dokumentu źródeł (bez systemu) ----------

    [Theory]
    [InlineData("DIMM 1", "P0 CHANNEL A", "DIMM 1 (P0 CHANNEL A)")]
    [InlineData("M0001", "None", "M0001")]
    [InlineData("M0001", "not specified", "M0001")]
    [InlineData(" ChannelA-DIMM0 ", "ChannelA-DIMM0", "ChannelA-DIMM0")]
    [InlineData("DIMM 2", "", "DIMM 2")]
    [InlineData("", "BANK 0", "BANK 0")]
    [InlineData(null, "None", null)]
    [InlineData("  ", null, null)]
    public void MemorySlot_FollowsLocatorAndBankRule(string? locator, string? bank, string? expected)
        => Assert.Equal(expected, WindowsHardwareReader.MemorySlot(locator, bank));

    [Theory]
    [InlineData("None", null)]
    [InlineData(" unknown ", null)]
    [InlineData("Not Specified", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    [InlineData("F5-6000J3038F16G              ", "F5-6000J3038F16G")]
    [InlineData("G Skill Intl", "G Skill Intl")]
    public void ModuleText_DropsPlaceholders(string? raw, string? expected)
        => Assert.Equal(expected, WindowsHardwareReader.ModuleText(raw));

    [Theory]
    [InlineData(0u, null)]
    [InlineData(1u, null)]
    [InlineData(60u, 60)]
    [InlineData(144u, 144)]
    public void RefreshRate_ZeroAndOneAreHardwareDefault(uint displayFrequency, int? expected)
        => Assert.Equal(expected, WindowsHardwareReader.RefreshRate(displayFrequency));

    [Theory]
    [InlineData("20260615000000.000000+000", 2026, 6, 15)]
    [InlineData("20250925000000.000000-480", 2025, 9, 25)]
    public void BiosDate_UsesFirstEightCharsWithoutTimeZone(string raw, int year, int month, int day)
        => Assert.Equal(new DateTime(year, month, day), WindowsHardwareReader.BiosDate(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026061")]
    [InlineData("abcdefgh000000.000000+000")]
    public void BiosDate_InvalidOrShort_IsNull(string? raw)
        => Assert.Null(WindowsHardwareReader.BiosDate(raw));

    [Theory]
    [InlineData("AMD Ryzen 7 9800X3D 8-Core Processor           ", "AMD Ryzen 7 9800X3D 8-Core Processor")]
    [InlineData("Linia\r\ndruga\tkolumna", "Linia  druga kolumna")]
    [InlineData("  \r\n ", null)]
    [InlineData(null, null)]
    public void Clean_TrimsAndReplacesControlChars(string? raw, string? expected)
        => Assert.Equal(expected, CimReader.Clean(raw));

    [Fact]
    public void SelectGpuMemory_PrefixOfPnpDeviceId_WinsOverOtherSubkeys()
    {
        var entries = new[]
        {
            new DisplayClassEntry("0000", "pci\\ven_10de&dev_2c02", "NVIDIA GeForce RTX 5080", 17094934528UL),
            new DisplayClassEntry("0001", "PCI\\VEN_1002&DEV_164E&SUBSYS_88771043&REV_C1", "AMD Radeon(TM) Graphics", 536870912UL),
        };

        var memory = WindowsHardwareReader.SelectGpuMemory(entries, "PCI\\VEN_10DE&DEV_2C02&SUBSYS_89D71043&REV_A1\\4&1234&0&0019", "NVIDIA GeForce RTX 5080");

        Assert.Equal(17094934528UL, memory);
    }

    [Fact]
    public void SelectGpuMemory_DriverDescFallback_PrefersEntryWithMemory()
    {
        var entries = new[]
        {
            new DisplayClassEntry("0001", "inny\\identyfikator", "AMD Radeon(TM) Graphics", null),
            new DisplayClassEntry("0002", null, "amd radeon(tm) graphics", 536870912UL),
        };

        Assert.Equal(536870912UL, WindowsHardwareReader.SelectGpuMemory(entries, "PCI\\VEN_1002&DEV_164E\\x", "AMD Radeon(TM) Graphics"));
    }

    [Fact]
    public void SelectGpuMemory_BothRulesBeatSingleRule_ThenSubkeyOrder()
    {
        var entries = new[]
        {
            new DisplayClassEntry("0001", null, "Karta", 2UL),
            new DisplayClassEntry("0003", "pci\\ven_1", "Karta", 3UL),
            new DisplayClassEntry("0002", "pci\\ven_1", "Karta", 4UL),
        };

        Assert.Equal(4UL, WindowsHardwareReader.SelectGpuMemory(entries, "PCI\\VEN_1&DEV_2", "Karta"));
    }

    [Fact]
    public void SelectGpuMemory_NoCandidate_IsNull()
    {
        var entries = new[] { new DisplayClassEntry("0000", "pci\\ven_10de", "NVIDIA", 1UL) };

        Assert.Null(WindowsHardwareReader.SelectGpuMemory(entries, "VMBUS\\{DA0A7802}", "Microsoft Hyper-V Video"));
    }

    [Theory]
    [InlineData(14u, NetworkKind.Ethernet)]
    [InlineData(9u, NetworkKind.WiFi)]
    [InlineData(1u, NetworkKind.WiFi)]
    [InlineData(19u, NetworkKind.Other)]
    [InlineData(0u, NetworkKind.Other)]
    [InlineData(null, NetworkKind.Other)]
    public void NetworkKindOf_MapsNdisPhysicalMedium(uint? medium, NetworkKind expected)
        => Assert.Equal(expected, WindowsHardwareReader.NetworkKindOf(medium));

    [Fact]
    public void MonitorName_DecodesUtf16WithoutTrailingZeros()
        => Assert.Equal("T24d-30", WindowsHardwareReader.MonitorName(new ushort[] { 84, 50, 52, 100, 45, 51, 48, 0, 0, 0, 0, 0, 0 }));

    [Fact]
    public void MonitorName_OnlyZeros_IsNull()
        => Assert.Null(WindowsHardwareReader.MonitorName(new ushort[] { 0, 0, 0 }));

    [Theory]
    [InlineData("0025_38B1_2142_A1B2.", "0025_38B1_2142_A1B2")]
    [InlineData("  S5GXNX0T  ", "S5GXNX0T")]
    [InlineData(" . ", null)]
    [InlineData(null, null)]
    public void DiskSerial_TrimsTrailingDot(string? raw, string? expected)
        => Assert.Equal(expected, WindowsHardwareReader.DiskSerial(raw));

    [Fact]
    public void NativeDisplayStructs_HaveDocumentedSizes()
    {
        Assert.Equal(220, Marshal.SizeOf<NativeMethods.DevMode>());
        Assert.Equal(840, Marshal.SizeOf<NativeMethods.DisplayDevice>());
        Assert.Equal(172, Marshal.OffsetOf<NativeMethods.DevMode>(nameof(NativeMethods.DevMode.PelsWidth)).ToInt32());
        Assert.Equal(184, Marshal.OffsetOf<NativeMethods.DevMode>(nameof(NativeMethods.DevMode.DisplayFrequency)).ToInt32());
    }

    private sealed class OutputLog : ILog
    {
        private readonly ITestOutputHelper _output;

        public OutputLog(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Info(string component, string message) => _output.WriteLine($"INFO [{component}] {message}");

        public void Warn(string component, string message) => _output.WriteLine($"WARN [{component}] {message}");

        public void Fail(string component, string message, Exception? exception = null) => _output.WriteLine($"FAIL [{component}] {message} {exception}");
    }
}
