using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core;
using DSOptimizer.Core.Hardware;
using Xunit;

namespace DSOptimizer.Tests.Unit.Hardware;

public class SpecificationFormatterTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    private static HardwareSnapshot Full() => new()
    {
        Processor = Reading<CpuInfo>.Done(new CpuInfo("AMD Ryzen 5 7600  ", 6, 12)),
        Memory = Reading<MemoryInfo>.Done(new MemoryInfo(
            new[]
            {
                new MemoryModule("DIMM_A2", 16 * GiB, 6000, 34, "Kingston", "KF560C36-16"),
                new MemoryModule("DIMM_B2", 16 * GiB, 6000, 34, "Kingston", "KF560C36-16"),
            },
            4)),
        Board = Reading<BoardInfo>.Done(new BoardInfo(
            "MSI", "MAG B650 TOMAHAWK WIFI", "7D75v1F", new DateTime(2025, 3, 14), FirmwareKind.Uefi,
            "Micro-Star International Co., Ltd.", "MS-7D75")),
        Graphics = Reading<IReadOnlyList<GpuInfo>>.Done(new[] { new GpuInfo("NVIDIA GeForce RTX 4060", 8 * GiB, "32.0.15.8157") }),
        Displays = Reading<IReadOnlyList<DisplayInfo>>.Done(new[] { new DisplayInfo("DELL S2721DGF", 60, 34, 2560, 1440, 165) }),
        Sound = Reading<IReadOnlyList<string>>.Done(new[] { "Realtek High Definition Audio" }),
        Disks = Reading<IReadOnlyList<DiskInfo>>.Done(new[]
        {
            new DiskInfo(0, "Samsung 990 PRO", 1000204886016UL, 17, 4, 38, "S6Z1NJ0W123456", 0),
            new DiskInfo(1, "WD Blue", 2000398934016UL, 11, 3, null, null, 0),
        }),
        Network = Reading<IReadOnlyList<NetworkAdapterInfo>>.Done(new[]
        {
            new NetworkAdapterInfo("Realtek Gaming 2.5GbE", NetworkKind.Ethernet, true, 1_000_000_000UL),
        }),
        Windows = Reading<WindowsInfo>.Done(new WindowsInfo("Professional", "26H2", 26300, 9457, new DateTime(2026, 9, 2))),
        Tpm = Reading<TpmInfo?>.Done(new TpmInfo("2.0, 0, 1.59", true, true)),
        SecureBoot = Reading<SecureBootState>.Done(SecureBootState.On),
        Vbs = Reading<VbsInfo>.Done(new VbsInfo(2, false)),
        BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[] { new BitLockerVolume("C:", 1, 1) }),
    };

    private static string Value(IReadOnlyList<SpecGroup> groups, string key)
        => groups.SelectMany(g => g.Fields).First(f => f.Key == key).Value;

    private static List<SpecField> Fields(IReadOnlyList<SpecGroup> groups, string key)
        => groups.SelectMany(g => g.Fields).Where(f => f.Key == key).ToList();

    private static readonly DateTime Now = new(2026, 10, 19, 10, 42, 0);

    [Fact]
    public void Groups_FullSnapshot_MatchesSpecExamples()
    {
        var groups = SpecificationFormatter.Groups(Full());

        Assert.Equal(
            new[] { "Procesor i pamięć", "Grafika, ekran i dźwięk", "Dyski", "Sieć", "System i zabezpieczenia" },
            groups.Select(g => g.Title).ToArray());
        Assert.Equal(
            new[] { "Procesor", "Pamięć", "Płyta główna", "Komputer" },
            groups[0].Fields.Select(f => f.Key).ToArray());
        Assert.Equal(
            new[] { "Karta graficzna", "Monitor", "Dźwięk" },
            groups[1].Fields.Select(f => f.Key).ToArray());
        Assert.Equal(new[] { "Dysk 0", "Dysk 1" }, groups[2].Fields.Select(f => f.Key).ToArray());
        Assert.Equal(new[] { "Ethernet" }, groups[3].Fields.Select(f => f.Key).ToArray());
        Assert.Equal(
            new[] { "Windows", "TPM", "Secure Boot", "VBS i HVCI", "BitLocker" },
            groups[4].Fields.Select(f => f.Key).ToArray());

        Assert.Equal("AMD Ryzen 5 7600, 6 rdzeni, 12 wątków", Value(groups, "Procesor"));
        Assert.Equal("32 GB DDR5, 6000 MT/s, 2 z 4 gniazd", Value(groups, "Pamięć"));
        Assert.Equal("MSI MAG B650 TOMAHAWK WIFI, BIOS 7D75v1F (2025-03-14), UEFI", Value(groups, "Płyta główna"));
        Assert.Equal("Micro-Star International Co., Ltd. MS-7D75", Value(groups, "Komputer"));
        Assert.Equal("NVIDIA GeForce RTX 4060, 8 GB, sterownik 32.0.15.8157", Value(groups, "Karta graficzna"));
        Assert.Equal("27 cali, 2560×1440, 165 Hz", Value(groups, "Monitor"));
        Assert.Equal("Realtek High Definition Audio", Value(groups, "Dźwięk"));
        Assert.Equal("Samsung 990 PRO 1 TB, NVMe SSD, 38 °C", Value(groups, "Dysk 0"));
        Assert.Equal("WD Blue 2 TB, SATA HDD", Value(groups, "Dysk 1"));
        Assert.Equal("Realtek Gaming 2.5GbE, połączona, 1 Gb/s", Value(groups, "Ethernet"));
        Assert.Equal("Windows 11 Pro 26H2, build 26300.9457, zainstalowany 2026-09-02", Value(groups, "Windows"));
        Assert.Equal("2.0, włączony", Value(groups, "TPM"));
        Assert.Equal("Włączony", Value(groups, "Secure Boot"));
        Assert.Equal("VBS działa, HVCI nie działa", Value(groups, "VBS i HVCI"));
        Assert.Equal("C: zaszyfrowany, ochrona włączona", Value(groups, "BitLocker"));
        Assert.All(groups.SelectMany(g => g.Fields), f => Assert.False(f.IsContinuation));
    }

    [Fact]
    public void Groups_PendingAndFailed_ShowLoadingAndNoData()
    {
        var pending = SpecificationFormatter.Groups(new HardwareSnapshot());
        var fields = pending.SelectMany(g => g.Fields).ToList();
        Assert.Equal(5, pending.Count);
        Assert.All(fields, f => Assert.Equal("Wczytywanie…", f.Value));
        Assert.Contains(fields, f => f.Key == "Procesor");
        Assert.Contains(fields, f => f.Key == "BitLocker");
        Assert.DoesNotContain(fields, f => f.Key == "Komputer");

        var failed = SpecificationFormatter.Groups(new HardwareSnapshot { Processor = Reading<CpuInfo>.Failed });
        Assert.Equal("brak danych", Value(failed, "Procesor"));
        Assert.Equal("Wczytywanie…", Value(failed, "Pamięć"));
    }

    [Fact]
    public void Groups_AllFailed_ShowNoData()
    {
        var snapshot = new HardwareSnapshot
        {
            Processor = Reading<CpuInfo>.Failed,
            Memory = Reading<MemoryInfo>.Failed,
            Board = Reading<BoardInfo>.Failed,
            Graphics = Reading<IReadOnlyList<GpuInfo>>.Failed,
            Displays = Reading<IReadOnlyList<DisplayInfo>>.Failed,
            Sound = Reading<IReadOnlyList<string>>.Failed,
            Disks = Reading<IReadOnlyList<DiskInfo>>.Failed,
            Network = Reading<IReadOnlyList<NetworkAdapterInfo>>.Failed,
            Windows = Reading<WindowsInfo>.Failed,
            Tpm = Reading<TpmInfo?>.Failed,
            SecureBoot = Reading<SecureBootState>.Failed,
            Vbs = Reading<VbsInfo>.Failed,
            BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Failed,
        };
        Assert.True(snapshot.IsComplete);
        Assert.All(
            SpecificationFormatter.Groups(snapshot).SelectMany(g => g.Fields),
            f => Assert.Equal("brak danych", f.Value));
    }

    [Fact]
    public void Snapshot_IsComplete_OnlyWithoutPending()
    {
        Assert.False(new HardwareSnapshot().IsComplete);
        Assert.False((Full() with { Vbs = Reading<VbsInfo>.Pending }).IsComplete);
        Assert.True(Full().IsComplete);
    }

    [Fact]
    public void Groups_MissingHardware_ShowsBrakAndOmitsZeros()
    {
        var snapshot = Full() with
        {
            Tpm = Reading<TpmInfo?>.Done(null),
            Network = Reading<IReadOnlyList<NetworkAdapterInfo>>.Done(Array.Empty<NetworkAdapterInfo>()),
            Displays = Reading<IReadOnlyList<DisplayInfo>>.Done(new[] { new DisplayInfo("Laptop", 0, 0, 1920, 1080, 60) }),
            Disks = Reading<IReadOnlyList<DiskInfo>>.Done(new[] { new DiskInfo(0, "Disk", 500_107_862_016UL, 17, 4, 0, null, 0) }),
            Graphics = Reading<IReadOnlyList<GpuInfo>>.Done(new[] { new GpuInfo("NVIDIA X", null, "1.2") }),
            SecureBoot = Reading<SecureBootState>.Done(SecureBootState.LegacyBios),
            Board = Reading<BoardInfo>.Done(new BoardInfo("ASUS", "PRIME", "1", new DateTime(2020, 1, 2), FirmwareKind.Unknown, null, null)),
        };

        var groups = SpecificationFormatter.Groups(snapshot);

        Assert.Equal("brak", Value(groups, "TPM"));
        var network = groups.Single(g => g.Title == "Sieć").Fields;
        var adapter = Assert.Single(network);
        Assert.Equal("Karta sieciowa", adapter.Key);
        Assert.Equal("brak", adapter.Value);
        Assert.Equal("1920×1080, 60 Hz", Value(groups, "Monitor"));
        Assert.Equal("Disk 500 GB, NVMe SSD", Value(groups, "Dysk 0"));
        Assert.Equal("NVIDIA X, sterownik 1.2", Value(groups, "Karta graficzna"));
        Assert.Equal("Niedostępny (BIOS Legacy)", Value(groups, "Secure Boot"));
        Assert.Equal("ASUS PRIME, BIOS 1 (2020-01-02)", Value(groups, "Płyta główna"));
        Assert.Empty(Fields(groups, "Komputer"));
    }

    [Theory]
    [InlineData(SecureBootState.Off, "Wyłączony")]
    [InlineData(SecureBootState.Unknown, "brak danych")]
    public void Groups_SecureBootStates(SecureBootState state, string expected)
        => Assert.Equal(
            expected,
            Value(SpecificationFormatter.Groups(Full() with { SecureBoot = Reading<SecureBootState>.Done(state) }), "Secure Boot"));

    [Fact]
    public void Groups_EmptyLists_ShowBrak()
    {
        var snapshot = Full() with
        {
            Graphics = Reading<IReadOnlyList<GpuInfo>>.Done(Array.Empty<GpuInfo>()),
            Displays = Reading<IReadOnlyList<DisplayInfo>>.Done(Array.Empty<DisplayInfo>()),
            Sound = Reading<IReadOnlyList<string>>.Done(Array.Empty<string>()),
            Disks = Reading<IReadOnlyList<DiskInfo>>.Done(Array.Empty<DiskInfo>()),
            BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(Array.Empty<BitLockerVolume>()),
        };
        var groups = SpecificationFormatter.Groups(snapshot);
        Assert.Equal("brak", Value(groups, "Karta graficzna"));
        Assert.Equal("brak", Value(groups, "Monitor"));
        Assert.Equal("brak", Value(groups, "Dźwięk"));
        Assert.Equal("brak", Value(groups, "Dysk"));
        Assert.Equal("brak", Value(groups, "BitLocker"));
    }

    [Fact]
    public void Groups_MultipleSoundDevices_ContinuationKeepsKey()
    {
        var snapshot = Full() with { Sound = Reading<IReadOnlyList<string>>.Done(new[] { "Realtek", "NVIDIA HD Audio" }) };
        var sound = Fields(SpecificationFormatter.Groups(snapshot), "Dźwięk");
        Assert.Equal(2, sound.Count);
        Assert.False(sound[0].IsContinuation);
        Assert.True(sound[1].IsContinuation);
        Assert.Equal("NVIDIA HD Audio", sound[1].Value);
    }

    [Fact]
    public void Groups_MultipleMonitors_EachHasOwnNumberedKey()
    {
        var snapshot = Full() with
        {
            Displays = Reading<IReadOnlyList<DisplayInfo>>.Done(new[]
            {
                new DisplayInfo("A", 60, 34, 2560, 1440, 165),
                new DisplayInfo("B", 53, 30, 1920, 1080, 60),
            }),
        };
        var groups = SpecificationFormatter.Groups(snapshot);
        Assert.Equal("27 cali, 2560×1440, 165 Hz", Value(groups, "Monitor 1"));
        Assert.Equal("24 cale, 1920×1080, 60 Hz", Value(groups, "Monitor 2"));
        Assert.All(groups.SelectMany(g => g.Fields), f => Assert.False(f.IsContinuation));
    }

    [Fact]
    public void Groups_NetworkAdapters_KeyByKindAndContinuation()
    {
        var snapshot = Full() with
        {
            Network = Reading<IReadOnlyList<NetworkAdapterInfo>>.Done(new[]
            {
                new NetworkAdapterInfo("Intel Ethernet", NetworkKind.Ethernet, true, 2_500_000_000UL),
                new NetworkAdapterInfo("Realtek Ethernet", NetworkKind.Ethernet, false, 1_000_000_000UL),
                new NetworkAdapterInfo("Intel Wi-Fi 6", NetworkKind.WiFi, true, 866_000_000UL),
                new NetworkAdapterInfo("VPN", NetworkKind.Other, true, null),
            }),
        };
        var network = SpecificationFormatter.Groups(snapshot).Single(g => g.Title == "Sieć").Fields;

        Assert.Equal(new[] { "Ethernet", "Ethernet", "Wi-Fi", "Karta sieciowa" }, network.Select(f => f.Key).ToArray());
        Assert.Equal(new[] { false, true, false, false }, network.Select(f => f.IsContinuation).ToArray());
        Assert.Equal("Intel Ethernet, połączona, 2,5 Gb/s", network[0].Value);
        Assert.Equal("Realtek Ethernet, rozłączona", network[1].Value);
        Assert.Equal("Intel Wi-Fi 6, połączona, 866 Mb/s", network[2].Value);
        Assert.Equal("VPN, połączona", network[3].Value);
    }

    [Theory]
    [InlineData("System manufacturer", "System Product Name")]
    [InlineData("system MANUFACTURER", "X")]
    [InlineData("ASUS", "to be filled by o.e.m.")]
    [InlineData("Default String", "Default string")]
    [InlineData("MSI", "MAG B650 TOMAHAWK WIFI")]
    [InlineData(null, null)]
    [InlineData(" ", "")]
    public void Groups_PlaceholderOrDuplicateSystemModel_Hidden(string? manufacturer, string? model)
    {
        var board = new BoardInfo("MSI", "MAG B650 TOMAHAWK WIFI", "1", null, FirmwareKind.Uefi, manufacturer, model);
        var groups = SpecificationFormatter.Groups(Full() with { Board = Reading<BoardInfo>.Done(board) });
        Assert.Empty(Fields(groups, "Komputer"));
    }

    [Theory]
    [InlineData("System Serial Number", true)]
    [InlineData("system serial number", true)]
    [InlineData("To Be Filled By O.E.M.", true)]
    [InlineData("PF2KQ7ZT", false)]
    [InlineData(null, false)]
    public void IsPlaceholder_Table(string? value, bool expected)
        => Assert.Equal(expected, SpecificationFormatter.IsPlaceholder(value));

    [Fact]
    public void Groups_LaptopModel_Shown()
    {
        var board = new BoardInfo("LNVNB161216", "LNVNB161216", "N3JET", null, FirmwareKind.Legacy, "LENOVO", "21K5");
        var groups = SpecificationFormatter.Groups(Full() with { Board = Reading<BoardInfo>.Done(board) });
        Assert.Equal("LENOVO 21K5", Value(groups, "Komputer"));
        Assert.Equal("LNVNB161216 LNVNB161216, BIOS N3JET, BIOS Legacy", Value(groups, "Płyta główna"));
    }

    [Fact]
    public void Groups_ProcessorAndMemoryPartsMissing_AreDropped()
    {
        var snapshot = Full() with
        {
            Processor = Reading<CpuInfo>.Done(new CpuInfo(null, 1, 0)),
            Memory = Reading<MemoryInfo>.Done(new MemoryInfo(
                new[] { new MemoryModule("A", 8 * GiB, null, 99, null, null) }, 1)),
        };
        var groups = SpecificationFormatter.Groups(snapshot);
        Assert.Equal("1 rdzeń", Value(groups, "Procesor"));
        Assert.Equal("8 GB, 1 z 1 gniazda", Value(groups, "Pamięć"));
    }

    [Fact]
    public void Groups_MemoryWithoutSlotCountOrModules_DropsParts()
    {
        var noSlots = Full() with
        {
            Memory = Reading<MemoryInfo>.Done(new MemoryInfo(
                new[] { new MemoryModule("A", 8 * GiB, 3200, 26, null, null) }, null)),
        };
        Assert.Equal("8 GB DDR4, 3200 MT/s", Value(SpecificationFormatter.Groups(noSlots), "Pamięć"));

        var empty = Full() with { Memory = Reading<MemoryInfo>.Done(new MemoryInfo(Array.Empty<MemoryModule>(), null)) };
        Assert.Equal("brak danych", Value(SpecificationFormatter.Groups(empty), "Pamięć"));
    }

    [Theory]
    [InlineData(24, "DDR3")]
    [InlineData(26, "DDR4")]
    [InlineData(29, "LPDDR3")]
    [InlineData(30, "LPDDR4")]
    [InlineData(34, "DDR5")]
    [InlineData(35, "LPDDR5")]
    public void Groups_MemoryTypes(int smbios, string expected)
    {
        var snapshot = Full() with
        {
            Memory = Reading<MemoryInfo>.Done(new MemoryInfo(
                new[] { new MemoryModule("A", 16 * GiB, 4800, smbios, null, null) }, 2)),
        };
        Assert.Equal($"16 GB {expected}, 4800 MT/s, 1 z 2 gniazd", Value(SpecificationFormatter.Groups(snapshot), "Pamięć"));
    }

    [Theory]
    [InlineData(1, "SCSI")]
    [InlineData(3, "ATA")]
    [InlineData(7, "USB")]
    [InlineData(8, "RAID")]
    [InlineData(10, "SAS")]
    [InlineData(11, "SATA")]
    [InlineData(17, "NVMe")]
    public void Groups_DiskBuses(int bus, string expected)
    {
        var snapshot = Full() with
        {
            Disks = Reading<IReadOnlyList<DiskInfo>>.Done(new[] { new DiskInfo(2, "X", 256_060_514_304UL, bus, 4, 40, null, 0) }),
        };
        Assert.Equal($"X 256 GB, {expected} SSD, 40 °C", Value(SpecificationFormatter.Groups(snapshot), "Dysk 2"));
    }

    [Fact]
    public void Groups_DiskUnknownBusAndMedia_AreDropped()
    {
        var snapshot = Full() with
        {
            Disks = Reading<IReadOnlyList<DiskInfo>>.Done(new[]
            {
                new DiskInfo(3, "Y", 1_500_000_000_000UL, 99, 0, null, null, null),
                new DiskInfo(null, "Z", null, null, null, null, null, null),
            }),
        };
        var groups = SpecificationFormatter.Groups(snapshot);
        Assert.Equal("Y 1,5 TB", Value(groups, "Dysk 3"));
        Assert.Equal("Z", Value(groups, "Dysk"));
    }

    [Theory]
    [InlineData(0, 0, "Windows")]
    [InlineData(19045, 5000, "Windows, build 19045.5000")]
    [InlineData(22000, 1, "Windows 11, build 22000.1")]
    public void Groups_WindowsPrefixByBuild(int build, int revision, string expected)
    {
        var snapshot = Full() with
        {
            Windows = Reading<WindowsInfo>.Done(new WindowsInfo("unknown", null, build, revision, null)),
        };
        Assert.Equal(expected, Value(SpecificationFormatter.Groups(snapshot), "Windows"));
    }

    [Fact]
    public void Groups_TpmStates()
    {
        var off = Full() with { Tpm = Reading<TpmInfo?>.Done(new TpmInfo("2.0", true, false)) };
        Assert.Equal("2.0, wyłączony", Value(SpecificationFormatter.Groups(off), "TPM"));
        var noSpec = Full() with { Tpm = Reading<TpmInfo?>.Done(new TpmInfo(null, true, true)) };
        Assert.Equal("włączony", Value(SpecificationFormatter.Groups(noSpec), "TPM"));
    }

    [Theory]
    [InlineData(2, true, "VBS działa, HVCI działa")]
    [InlineData(2, false, "VBS działa, HVCI nie działa")]
    [InlineData(1, false, "VBS włączone, nie działa")]
    [InlineData(0, false, "VBS wyłączone")]
    [InlineData(null, false, "brak danych")]
    [InlineData(7, false, "brak danych")]
    public void Groups_VbsStates(int? status, bool hvci, string expected)
        => Assert.Equal(
            expected,
            Value(SpecificationFormatter.Groups(Full() with { Vbs = Reading<VbsInfo>.Done(new VbsInfo(status, hvci)) }), "VBS i HVCI"));

    [Fact]
    public void Groups_BitLockerStates()
    {
        var snapshot = Full() with
        {
            BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[]
            {
                new BitLockerVolume("C:", 1, 0),
                new BitLockerVolume("D:", 0, 0),
                new BitLockerVolume("E:", 2, 1),
                new BitLockerVolume("F:", 3, 1),
                new BitLockerVolume("G:", 4, 1),
                new BitLockerVolume("H:", 5, 1),
                new BitLockerVolume("I:", null, null),
                new BitLockerVolume("J:", 9, null),
                new BitLockerVolume("", 1, 1),
            }),
        };
        var fields = Fields(SpecificationFormatter.Groups(snapshot), "BitLocker");
        Assert.Equal(
            new[]
            {
                "C: zaszyfrowany, ochrona wyłączona",
                "D: niezaszyfrowany",
                "E: szyfrowanie w toku, ochrona włączona",
                "F: odszyfrowywanie w toku, ochrona włączona",
                "G: szyfrowanie wstrzymane, ochrona włączona",
                "H: odszyfrowywanie wstrzymane, ochrona włączona",
                "I: brak danych",
                "J: brak danych",
            },
            fields.Select(f => f.Value).ToArray());
        Assert.False(fields[0].IsContinuation);
        Assert.All(fields.Skip(1), f => Assert.True(f.IsContinuation));
    }

    [Fact]
    public void Groups_BitLockerUnknownProtection_OmitsProtectionSuffix()
    {
        var snapshot = Full() with
        {
            BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[] { new BitLockerVolume("C:", 1, 2) }),
        };
        Assert.Equal("C: zaszyfrowany", Value(SpecificationFormatter.Groups(snapshot), "BitLocker"));
    }

    [Fact]
    public void Groups_BitLockerOnlyBlankLetters_ShowsBrak()
    {
        var snapshot = Full() with
        {
            BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[] { new BitLockerVolume(" ", 1, 1) }),
        };
        Assert.Equal("brak", Value(SpecificationFormatter.Groups(snapshot), "BitLocker"));
    }

    [Fact]
    public void Groups_MultipleGpus_ContinuationKeepsKey()
    {
        var snapshot = Full() with
        {
            Graphics = Reading<IReadOnlyList<GpuInfo>>.Done(new[]
            {
                new GpuInfo("AMD Radeon", 1024 * 1024 * 1024UL, null),
                new GpuInfo("NVIDIA", 16 * GiB, "1.0"),
            }),
        };
        var gpus = Fields(SpecificationFormatter.Groups(snapshot), "Karta graficzna");
        Assert.Equal("AMD Radeon, 1 GB", gpus[0].Value);
        Assert.True(gpus[1].IsContinuation);
        Assert.Equal("NVIDIA, 16 GB, sterownik 1.0", gpus[1].Value);
    }

    [Fact]
    public void Report_FullSnapshot_HeaderGroupsAndDetails()
    {
        var report = SpecificationFormatter.Report(Full(), "KLIENT-PC", Now);

        Assert.StartsWith(
            $"DSOptimizer {ProductInfo.Version} – specyfikacja komputera\r\nKomputer: KLIENT-PC\r\nData: 2026-10-19 10:42\r\n\r\nProcesor i pamięć\r\n  Procesor: AMD Ryzen 5 7600, 6 rdzeni, 12 wątków\r\n",
            report);
        Assert.Contains("\r\n  Pamięć: 32 GB DDR5, 6000 MT/s, 2 z 4 gniazd\r\n    DIMM_A2: 16 GB, 6000 MT/s, Kingston, KF560C36-16\r\n    DIMM_B2: 16 GB, 6000 MT/s, Kingston, KF560C36-16\r\n  Płyta główna:", report);
        Assert.Contains("  Monitor: DELL S2721DGF, 27 cali, 2560×1440, 165 Hz\r\n", report);
        Assert.Contains(
            "  Dysk 0: Samsung 990 PRO 1 TB, NVMe SSD, 38 °C\r\n    Numer seryjny: S6Z1NJ0W123456\r\n    Stan: zdrowy\r\n  Dysk 1: WD Blue 2 TB, SATA HDD\r\n    Stan: zdrowy\r\n",
            report);
        Assert.Contains("\r\n\r\nDyski\r\n", report);
        Assert.Contains("\r\n\r\nSystem i zabezpieczenia\r\n  Windows: Windows 11 Pro", report);
        Assert.EndsWith("  BitLocker: C: zaszyfrowany, ochrona włączona\r\n", report);
        Assert.DoesNotContain("\r\n\r\n\r\n", report);
        Assert.Equal(report.Count(c => c == '\n'), report.Count(c => c == '\r'));
        Assert.DoesNotContain("\n", report.Replace("\r\n", string.Empty));
    }

    [Theory]
    [InlineData(0, "zdrowy")]
    [InlineData(1, "ostrzeżenie")]
    [InlineData(2, "uszkodzony")]
    [InlineData(5, "nieznany")]
    [InlineData(null, "nieznany")]
    public void Report_DiskHealth(int? health, string expected)
    {
        var snapshot = Full() with
        {
            Disks = Reading<IReadOnlyList<DiskInfo>>.Done(new[] { new DiskInfo(0, "D", 500_000_000_000UL, 17, 4, 30, " ", health) }),
        };
        var report = SpecificationFormatter.Report(snapshot, "PC", Now);
        Assert.Contains($"  Dysk 0: D 500 GB, NVMe SSD, 30 °C\r\n    Stan: {expected}\r\n", report);
        Assert.DoesNotContain("Numer seryjny", report);
    }

    [Fact]
    public void Report_PendingParts_ShowLoading_AndMissingPartsAreDropped()
    {
        var pending = SpecificationFormatter.Report(new HardwareSnapshot(), "PC", Now);
        Assert.Contains("  Procesor: Wczytywanie…\r\n", pending);

        var snapshot = Full() with
        {
            Memory = Reading<MemoryInfo>.Done(new MemoryInfo(new[] { new MemoryModule("A1", null, 0, null, "", " ") }, 2)),
            Displays = Reading<IReadOnlyList<DisplayInfo>>.Done(new[] { new DisplayInfo(null, 0, 0, 1920, 1080, null) }),
        };
        var report = SpecificationFormatter.Report(snapshot, "PC", Now);
        Assert.Contains("  Pamięć: 1 z 2 gniazd\r\n    A1\r\n", report);
        Assert.Contains("  Monitor: 1920×1080\r\n", report);
    }

    [Fact]
    public void Report_MemoryModuleWithoutDetails_ListsSlotAndCapacityOnly()
    {
        var snapshot = Full() with
        {
            Memory = Reading<MemoryInfo>.Done(new MemoryInfo(new[] { new MemoryModule("A1", 4 * GiB, null, 26, null, null) }, 2)),
        };
        var report = SpecificationFormatter.Report(snapshot, "PC", Now);
        Assert.Contains("  Pamięć: 4 GB DDR4, 1 z 2 gniazd\r\n    A1: 4 GB\r\n", report);
    }

    [Fact]
    public void Output_IsIndependentOfCurrentCulture()
    {
        var snapshot = Full() with
        {
            Network = Reading<IReadOnlyList<NetworkAdapterInfo>>.Done(new[]
            {
                new NetworkAdapterInfo("N", NetworkKind.Ethernet, true, 2_500_000_000UL),
            }),
        };
        var expected = SpecificationFormatter.Report(snapshot, "PC", Now);
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal(expected, SpecificationFormatter.Report(snapshot, "PC", Now));
            Assert.Equal("specyfikacja-2026-10-19_1042.txt", SpecificationFormatter.FileName(Now));
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal(expected, SpecificationFormatter.Report(snapshot, "PC", Now));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }

        Assert.Contains("2,5 Gb/s", expected);
        Assert.Contains("Data: 2026-10-19 10:42", expected);
    }

    [Fact]
    public void FileName_FormatsLocalTime()
    {
        Assert.Equal("specyfikacja-2026-10-19_1042.txt", SpecificationFormatter.FileName(Now));
        Assert.Equal("specyfikacja-2026-01-02_0304.txt", SpecificationFormatter.FileName(new DateTime(2026, 1, 2, 3, 4, 59)));
    }

    [Fact]
    public void Constants_MatchApprovedTexts()
    {
        Assert.Equal("Wczytywanie…", SpecificationFormatter.LoadingText);
        Assert.Equal("brak danych", SpecificationFormatter.NoDataText);
        Assert.Equal("brak", SpecificationFormatter.AbsentText);
    }
}
