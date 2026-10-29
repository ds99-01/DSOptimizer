using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Prep;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization.English;

/// <summary>Teksty Specyfikacji (raport, grupy, log odczytu) i strony „Przygotowanie” po angielsku: całe zdania, odmiana liczebników.</summary>
public class HardwareEnglishTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    private static readonly DateTime Now = new(2026, 10, 19, 10, 42, 0);

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    // ---------- Specyfikacja ----------

    /// <summary>Dane jak w SpecificationFormatterTests.Full(), procesor 8 rdzeni / 16 wątków.</summary>
    private static HardwareSnapshot Full() => new()
    {
        Processor = Reading<CpuInfo>.Done(new CpuInfo("AMD Ryzen 5 7600  ", 8, 16)),
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

    private static string Value(HardwareSnapshot snapshot, string key)
        => SpecificationFormatter.Groups(snapshot).SelectMany(g => g.Fields).First(f => f.Key == key).Value;

    [Fact]
    public void Specification_Report_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var expected =
                $"DSOptimizer {ProductInfo.Version} – computer specification\r\n" +
                "Computer: KLIENT-PC\r\n" +
                "Date: 2026-10-19 10:42\r\n" +
                "\r\n" +
                "Processor and memory\r\n" +
                "  Processor: AMD Ryzen 5 7600, 8 cores, 16 threads\r\n" +
                "  Memory: 32 GB DDR5, 6000 MT/s, 2 of 4 slots\r\n" +
                "    DIMM_A2: 16 GB, 6000 MT/s, Kingston, KF560C36-16\r\n" +
                "    DIMM_B2: 16 GB, 6000 MT/s, Kingston, KF560C36-16\r\n" +
                "  Motherboard: MSI MAG B650 TOMAHAWK WIFI, BIOS 7D75v1F (2025-03-14), UEFI\r\n" +
                "  Computer: Micro-Star International Co., Ltd. MS-7D75\r\n" +
                "\r\n" +
                "Graphics, display and sound\r\n" +
                "  Graphics card: NVIDIA GeForce RTX 4060, 8 GB, driver 32.0.15.8157\r\n" +
                "  Monitor: DELL S2721DGF, 27 inches, 2560×1440, 165 Hz\r\n" +
                "  Sound: Realtek High Definition Audio\r\n" +
                "\r\n" +
                "Disks\r\n" +
                "  Disk 0: Samsung 990 PRO 1 TB, NVMe SSD, 38 °C\r\n" +
                "    Serial number: S6Z1NJ0W123456\r\n" +
                "    Status: healthy\r\n" +
                "  Disk 1: WD Blue 2 TB, SATA HDD\r\n" +
                "    Status: healthy\r\n" +
                "\r\n" +
                "Network\r\n" +
                "  Ethernet: Realtek Gaming 2.5GbE, connected, 1 Gb/s\r\n" +
                "\r\n" +
                "System and security\r\n" +
                "  Windows: Windows 11 Pro 26H2, build 26300.9457, installed 2026-09-02\r\n" +
                "  TPM: 2.0, enabled\r\n" +
                "  Secure Boot: On\r\n" +
                "  VBS and HVCI: VBS running, HVCI not running\r\n" +
                "  BitLocker: C: encrypted, protection on\r\n";
            Assert.Equal(expected, SpecificationFormatter.Report(Full(), "KLIENT-PC", Now));
            Assert.Equal("specyfikacja-2026-10-19_1042.txt", SpecificationFormatter.FileName(Now));
        }
    }

    [Fact]
    public void Specification_Report_PolishUnchanged()
    {
        var expected =
            $"DSOptimizer {ProductInfo.Version} – specyfikacja komputera\r\n" +
            "Komputer: KLIENT-PC\r\n" +
            "Data: 2026-10-19 10:42\r\n" +
            "\r\n" +
            "Procesor i pamięć\r\n" +
            "  Procesor: AMD Ryzen 5 7600, 8 rdzeni, 16 wątków\r\n" +
            "  Pamięć: 32 GB DDR5, 6000 MT/s, 2 z 4 gniazd\r\n" +
            "    DIMM_A2: 16 GB, 6000 MT/s, Kingston, KF560C36-16\r\n" +
            "    DIMM_B2: 16 GB, 6000 MT/s, Kingston, KF560C36-16\r\n" +
            "  Płyta główna: MSI MAG B650 TOMAHAWK WIFI, BIOS 7D75v1F (2025-03-14), UEFI\r\n" +
            "  Komputer: Micro-Star International Co., Ltd. MS-7D75\r\n" +
            "\r\n" +
            "Grafika, ekran i dźwięk\r\n" +
            "  Karta graficzna: NVIDIA GeForce RTX 4060, 8 GB, sterownik 32.0.15.8157\r\n" +
            "  Monitor: DELL S2721DGF, 27 cali, 2560×1440, 165 Hz\r\n" +
            "  Dźwięk: Realtek High Definition Audio\r\n" +
            "\r\n" +
            "Dyski\r\n" +
            "  Dysk 0: Samsung 990 PRO 1 TB, NVMe SSD, 38 °C\r\n" +
            "    Numer seryjny: S6Z1NJ0W123456\r\n" +
            "    Stan: zdrowy\r\n" +
            "  Dysk 1: WD Blue 2 TB, SATA HDD\r\n" +
            "    Stan: zdrowy\r\n" +
            "\r\n" +
            "Sieć\r\n" +
            "  Ethernet: Realtek Gaming 2.5GbE, połączona, 1 Gb/s\r\n" +
            "\r\n" +
            "System i zabezpieczenia\r\n" +
            "  Windows: Windows 11 Pro 26H2, build 26300.9457, zainstalowany 2026-09-02\r\n" +
            "  TPM: 2.0, włączony\r\n" +
            "  Secure Boot: Włączony\r\n" +
            "  VBS i HVCI: VBS działa, HVCI nie działa\r\n" +
            "  BitLocker: C: zaszyfrowany, ochrona włączona\r\n";
        Assert.Equal(expected, SpecificationFormatter.Report(Full(), "KLIENT-PC", Now));
    }

    [Theory]
    [InlineData(1, 1, "X, 1 core, 1 thread")]
    [InlineData(2, 2, "X, 2 cores, 2 threads")]
    [InlineData(22, 44, "X, 22 cores, 44 threads")]
    public void Specification_CoresAndThreads_English(int cores, int threads, string expected)
    {
        using (L.Use(UiLanguage.English))
        {
            var snapshot = Full() with { Processor = Reading<CpuInfo>.Done(new CpuInfo("X", cores, threads)) };
            Assert.Equal(expected, Value(snapshot, "Processor"));
        }
    }

    [Theory]
    [InlineData(2, 1, "1 inch, 1920×1080")]
    [InlineData(4, 3, "2 inches, 1920×1080")]
    public void Specification_Diagonal_English(int widthCm, int heightCm, string expected)
    {
        using (L.Use(UiLanguage.English))
        {
            var snapshot = Full() with
            {
                Displays = Reading<IReadOnlyList<DisplayInfo>>.Done(new[] { new DisplayInfo("M", widthCm, heightCm, 1920, 1080, null) }),
            };
            Assert.Equal(expected, Value(snapshot, "Monitor"));
        }
    }

    [Theory]
    [InlineData(1, "4 GB DDR4, 1 of 1 slot")]
    [InlineData(2, "4 GB DDR4, 1 of 2 slots")]
    public void Specification_MemorySlots_English(int slots, string expected)
    {
        using (L.Use(UiLanguage.English))
        {
            var snapshot = Full() with
            {
                Memory = Reading<MemoryInfo>.Done(new MemoryInfo(new[] { new MemoryModule("A1", 4 * GiB, null, 26, null, null) }, slots)),
            };
            Assert.Equal(expected, Value(snapshot, "Memory"));
        }
    }

    [Fact]
    public void Specification_GroupsAndStates_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var pending = SpecificationFormatter.Groups(new HardwareSnapshot());
            Assert.Equal(
                new[] { "Processor and memory", "Graphics, display and sound", "Disks", "Network", "System and security" },
                pending.Select(g => g.Title).ToArray());
            Assert.Equal(
                new[]
                {
                    "Processor", "Memory", "Motherboard", "Graphics card", "Monitor", "Sound", "Disk", "Network adapter",
                    "Windows", "TPM", "Secure Boot", "VBS and HVCI", "BitLocker",
                },
                pending.SelectMany(g => g.Fields).Select(f => f.Key).ToArray());
            Assert.All(pending.SelectMany(g => g.Fields), f => Assert.Equal("Loading…", f.Value));
            Assert.Equal("Loading…", SpecificationFormatter.LoadingText);
            Assert.Equal("no data", SpecificationFormatter.NoDataText);
            Assert.Equal("none", SpecificationFormatter.AbsentText);

            var failed = Full() with { Processor = Reading<CpuInfo>.Failed, Tpm = Reading<TpmInfo?>.Done(null) };
            Assert.Equal("no data", Value(failed, "Processor"));
            Assert.Equal("none", Value(failed, "TPM"));

            var empty = Full() with
            {
                Graphics = Reading<IReadOnlyList<GpuInfo>>.Done(Array.Empty<GpuInfo>()),
                Network = Reading<IReadOnlyList<NetworkAdapterInfo>>.Done(Array.Empty<NetworkAdapterInfo>()),
                Disks = Reading<IReadOnlyList<DiskInfo>>.Done(Array.Empty<DiskInfo>()),
            };
            Assert.Equal("none", Value(empty, "Graphics card"));
            Assert.Equal("none", Value(empty, "Network adapter"));
            Assert.Equal("none", Value(empty, "Disk"));
        }
    }

    [Fact]
    public void Specification_KeysWithNumbers_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var snapshot = Full() with
            {
                Disks = Reading<IReadOnlyList<DiskInfo>>.Done(new[] { new DiskInfo(null, "D", 500_000_000_000UL, 17, 4, null, null, 0) }),
                Displays = Reading<IReadOnlyList<DisplayInfo>>.Done(new[]
                {
                    new DisplayInfo("A", null, null, 1920, 1080, 60),
                    new DisplayInfo("B", null, null, 2560, 1440, 144),
                }),
                Network = Reading<IReadOnlyList<NetworkAdapterInfo>>.Done(new[]
                {
                    new NetworkAdapterInfo("Wi-Fi 7", NetworkKind.WiFi, false, null),
                    new NetworkAdapterInfo("Bluetooth PAN", NetworkKind.Other, true, 3_000_000UL),
                }),
            };
            var fields = SpecificationFormatter.Groups(snapshot).SelectMany(g => g.Fields).ToList();
            Assert.Contains(fields, f => f.Key == "Disk" && f.Value == "D 500 GB, NVMe SSD");
            Assert.Contains(fields, f => f.Key == "Monitor 1" && f.Value == "1920×1080, 60 Hz");
            Assert.Contains(fields, f => f.Key == "Monitor 2" && f.Value == "2560×1440, 144 Hz");
            Assert.Contains(fields, f => f.Key == "Wi-Fi" && f.Value == "Wi-Fi 7, disconnected");
            Assert.Contains(fields, f => f.Key == "Network adapter" && f.Value == "Bluetooth PAN, connected, 3 Mb/s");
        }
    }

    [Theory]
    [InlineData(0, "Status: healthy")]
    [InlineData(1, "Status: warning")]
    [InlineData(2, "Status: unhealthy")]
    [InlineData(null, "Status: unknown")]
    public void Specification_DiskHealth_English(int? health, string expected)
    {
        using (L.Use(UiLanguage.English))
        {
            var snapshot = Full() with
            {
                Disks = Reading<IReadOnlyList<DiskInfo>>.Done(new[] { new DiskInfo(0, "D", 500_000_000_000UL, 17, 4, 30, null, health) }),
            };
            var report = SpecificationFormatter.Report(snapshot, "PC", Now);
            Assert.Contains($"  Disk 0: D 500 GB, NVMe SSD, 30 °C\r\n    {expected}\r\n", report);
        }
    }

    [Fact]
    public void Specification_SystemStates_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Off", Value(Full() with { SecureBoot = Reading<SecureBootState>.Done(SecureBootState.Off) }, "Secure Boot"));
            Assert.Equal(
                "Not available (Legacy BIOS)",
                Value(Full() with { SecureBoot = Reading<SecureBootState>.Done(SecureBootState.LegacyBios) }, "Secure Boot"));
            Assert.Equal("no data", Value(Full() with { SecureBoot = Reading<SecureBootState>.Done(SecureBootState.Unknown) }, "Secure Boot"));

            Assert.Equal("VBS running, HVCI running", Value(Full() with { Vbs = Reading<VbsInfo>.Done(new VbsInfo(2, true)) }, "VBS and HVCI"));
            Assert.Equal("VBS enabled, not running", Value(Full() with { Vbs = Reading<VbsInfo>.Done(new VbsInfo(1, false)) }, "VBS and HVCI"));
            Assert.Equal("VBS disabled", Value(Full() with { Vbs = Reading<VbsInfo>.Done(new VbsInfo(0, false)) }, "VBS and HVCI"));

            Assert.Equal("1.2, disabled", Value(Full() with { Tpm = Reading<TpmInfo?>.Done(new TpmInfo("1.2", true, false)) }, "TPM"));

            var legacy = Full() with
            {
                Board = Reading<BoardInfo>.Done(new BoardInfo("ACME", "B1", null, null, FirmwareKind.Legacy, null, null)),
            };
            Assert.Equal("ACME B1, Legacy BIOS", Value(legacy, "Motherboard"));
        }
    }

    [Fact]
    public void Specification_BitLocker_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var snapshot = Full() with
            {
                BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[]
                {
                    new BitLockerVolume("C:", 0, 0),
                    new BitLockerVolume("D:", 1, 0),
                    new BitLockerVolume("E:", 2, 1),
                    new BitLockerVolume("F:", 3, null),
                    new BitLockerVolume("G:", 4, 0),
                    new BitLockerVolume("H:", 5, 1),
                    new BitLockerVolume("I:", 9, 1),
                }),
            };
            var values = SpecificationFormatter.Groups(snapshot).SelectMany(g => g.Fields).Where(f => f.Key == "BitLocker").Select(f => f.Value);
            Assert.Equal(
                new[]
                {
                    "C: not encrypted",
                    "D: encrypted, protection off",
                    "E: encryption in progress, protection on",
                    "F: decryption in progress",
                    "G: encryption paused, protection off",
                    "H: decryption paused, protection on",
                    "I: no data",
                },
                values.ToArray());
        }
    }

    [Theory]
    [InlineData(HardwarePart.Processor, "Processor")]
    [InlineData(HardwarePart.Memory, "Memory")]
    [InlineData(HardwarePart.Board, "Motherboard")]
    [InlineData(HardwarePart.Graphics, "Graphics card")]
    [InlineData(HardwarePart.Displays, "Monitors")]
    [InlineData(HardwarePart.Sound, "Sound")]
    [InlineData(HardwarePart.Disks, "Disks")]
    [InlineData(HardwarePart.Network, "Network")]
    [InlineData(HardwarePart.Windows, "Windows")]
    [InlineData(HardwarePart.Vbs, "VBS and HVCI")]
    public void Specification_PartNames_English(HardwarePart part, string expected)
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(expected, SpecificationLoader.PartName(part));
        }
    }

    [Fact]
    public async Task Specification_ReadFailed_LogEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var log = new ListLog();
            var reader = new FakeHardwareReader { Tpm = () => throw new InvalidOperationException("WMI") };
            await new SpecificationLoader(reader, log, Generous).LoadAsync(_ => { });
            Assert.Equal("WARN [Specification] Couldn't read \"TPM\": WMI", Assert.Single(log.Lines));
        }
    }

    [Fact]
    public async Task Specification_ReadTimedOut_LogEnglish()
    {
        using var gate = new ManualResetEventSlim(false);
        using (L.Use(UiLanguage.English))
        {
            var log = new ListLog();
            var reader = new FakeHardwareReader
            {
                Tpm = () =>
                {
                    gate.Wait();
                    return null;
                },
            };
            try
            {
                await new SpecificationLoader(reader, log, TimeSpan.FromSeconds(0.2)).LoadAsync(_ => { });
            }
            finally
            {
                gate.Set();
            }

            Assert.Equal("WARN [Specification] Reading \"TPM\" took more than 0.2 s – showing \"no data\".", Assert.Single(log.Lines));
        }
    }

    [Fact]
    public async Task Specification_RefreshFailed_LogEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var log = new ListLog();
            await new SpecificationLoader(new FakeHardwareReader(), log, Generous).LoadAsync(_ => throw new InvalidOperationException("UI"));
            Assert.Equal("WARN [Specification] Couldn't refresh the Specification screen: UI", Assert.Single(log.Lines));
        }
    }

    // ---------- Przygotowanie ----------

    private static PrepSnapshot Lenovo() => new()
    {
        Board = Reading<BoardInfo>.Done(new BoardInfo(
            "LENOVO", "20XW003GPB", "N32ET86W (1.62)", new DateTime(2024, 3, 14), FirmwareKind.Uefi, "LENOVO", "20XW003GPB")),
        Product = Reading<PrepProduct>.Done(new PrepProduct("ThinkPad X1 Carbon Gen 9", "PF2KQ7ZT", "GenuineIntel")),
        BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[] { new BitLockerVolume("C:", 1, 0) }),
        Activation = Reading<ActivationInfo>.Done(new ActivationInfo(1, "Windows(R), Professional edition")),
        Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[]
        {
            new PrepGpu("Intel(R) Iris(R) Xe Graphics", @"PCI\VEN_8086&DEV_9A49&SUBSYS_22D817AA&REV_01\3&11583659&0&10", "oem42.inf",
                "31.0.101.5186", new DateTime(2024, 1, 12), "Intel Corporation"),
            new PrepGpu("Podstawowa karta graficzna Microsoft", @"PCI\VEN_10DE&DEV_1F9D&SUBSYS_22D817AA&REV_A1\4&1", null,
                "10.0.26100.1", new DateTime(2006, 6, 21), "Microsoft"),
        }),
        Devices = Reading<IReadOnlyList<ProblemDevice>>.Done(new[]
        {
            new ProblemDevice("Kontroler PCI Simple Communications", 28, new[] { @"PCI\VEN_8086&DEV_A0E8&SUBSYS_22D817AA&REV_20" }),
            new ProblemDevice("  ", 22, new[] { @"USB\VID_0BDA&PID_8771&REV_0100" }),
            new ProblemDevice("Sensor", 10, new[] { @"ACPI\INT3472" }),
        }),
    };

    private static PrepSnapshot HyperV() => new()
    {
        Board = Reading<BoardInfo>.Done(new BoardInfo(
            "Microsoft Corporation", "Virtual Machine", "Hyper-V UEFI Release v4.1", new DateTime(2025, 3, 4), FirmwareKind.Uefi,
            "Microsoft Corporation", "Virtual Machine")),
        Product = Reading<PrepProduct>.Done(new PrepProduct("Hyper-V UEFI Release v4.1", "7491-1319", "AuthenticAMD")),
        BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[] { new BitLockerVolume("C:", 1, 1) }),
        Activation = Reading<ActivationInfo>.Done(new ActivationInfo(5, "Windows(R), Professional edition")),
        Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[]
        {
            new PrepGpu("Podstawowa karta graficzna Microsoft", @"PCI\VEN_1414&DEV_5353\3&1", null, null, null, null),
        }),
        Devices = Reading<IReadOnlyList<ProblemDevice>>.Done(Array.Empty<ProblemDevice>()),
    };

    private static IReadOnlyList<PrepStep> Build(PrepSnapshot snapshot, bool settingsOpenable = true)
        => PrepSteps.Build(snapshot, "C:", settingsOpenable);

    private static PrepStep Step(IReadOnlyList<PrepStep> steps, PrepStepKind kind) => steps.Single(s => s.Kind == kind);

    private static string[] Lines(PrepStep step) => step.Lines.Select(l => l.Label + " = " + l.Value).ToArray();

    [Fact]
    public void PrepSteps_Titles_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                new[]
                {
                    "Computer", "Windows Update", "Optional driver updates", "Chipset drivers", "Graphics", "Manufacturer support",
                    "Devices without a driver",
                },
                Build(Lenovo()).Select(s => s.Title).ToArray());
        }
    }

    [Fact]
    public void Prep_PageTexts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Preparation", PrepText.PageTitle);
            Assert.Equal("What to check after installing Windows. The program doesn't install or change anything here.", PrepText.PageSubtitle);
            Assert.Equal("Refresh status", PrepText.RefreshButton);
            Assert.Equal("Loading…", PrepText.Loading);
            Assert.Equal("Couldn't read", PrepText.Failed);
            Assert.Equal("Unknown", PrepText.Unknown);
        }
    }

    [Theory]
    [InlineData(1u, "Active")]
    [InlineData(0u, "Not active")]
    [InlineData(2u, "Grace period")]
    [InlineData(5u, "Activation required")]
    [InlineData(9u, "Unknown")]
    [InlineData(null, "No product key")]
    public void Prep_Activation_English(uint? status, string expected)
    {
        using (L.Use(UiLanguage.English))
        {
            var snapshot = Lenovo() with
            {
                Activation = Reading<ActivationInfo>.Done(new ActivationInfo(status, status is null ? null : "Windows")),
            };
            var line = Step(Build(snapshot), PrepStepKind.Computer).Lines.Single(l => l.Label == "Windows activation");
            Assert.Equal(expected, line.Value);
        }
    }

    [Fact]
    public void Prep_VmNote_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var support = Step(Build(HyperV()), PrepStepKind.Support);
            Assert.Equal("Virtual machine – no manufacturer page.", support.Note);
            Assert.Equal(new[] { "Manufacturer = Microsoft Corporation", "Serial number = 7491-1319" }, Lines(support));
            Assert.Empty(support.Actions);
        }
    }

    [Fact]
    public void Prep_Computer_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var computer = Step(Build(Lenovo()), PrepStepKind.Computer);
            Assert.Equal(
                new[]
                {
                    "Manufacturer and model = Lenovo ThinkPad X1 Carbon Gen 9 (20XW003GPB)",
                    "BIOS = N32ET86W (1.62), 14/03/2024",
                    "BitLocker = Drive encrypted, protection off",
                    "Windows activation = Active",
                },
                Lines(computer));
            Assert.Null(computer.Note);

            var on = Step(Build(HyperV()), PrepStepKind.Computer);
            Assert.Equal("On", on.Lines.Single(l => l.Label == "BitLocker").Value);
            Assert.Equal("Save the recovery key before you change the BIOS or the drive.", on.Note);
        }
    }

    [Theory]
    [InlineData(0, 0, "Off")]
    [InlineData(2, 0, "Encryption in progress")]
    [InlineData(3, 1, "Decryption in progress")]
    [InlineData(9, 9, "Unknown")]
    public void Prep_BitLockerStates_English(int conversion, int protection, string expected)
    {
        using (L.Use(UiLanguage.English))
        {
            var snapshot = Lenovo() with
            {
                BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[] { new BitLockerVolume("C:", conversion, protection) }),
            };
            Assert.Equal(expected, Step(Build(snapshot), PrepStepKind.Computer).Lines.Single(l => l.Label == "BitLocker").Value);
        }
    }

    [Fact]
    public void Prep_PendingAndFailed_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var pending = Build(new PrepSnapshot());
            Assert.Equal(
                new[]
                {
                    "Manufacturer and model = Loading…", "BIOS = Loading…", "BitLocker = Loading…", "Windows activation = Loading…",
                    "Processor = Loading…", "Graphics cards = Loading…", "Manufacturer = Loading…", "Serial number = Loading…",
                    "Devices with a problem = Loading…",
                },
                pending.SelectMany(Lines).ToArray());

            var failed = Lenovo() with { Product = Reading<PrepProduct>.Failed };
            Assert.Equal(new[] { "Processor = Couldn't read" }, Lines(Step(Build(failed), PrepStepKind.Chipset)));
        }
    }

    [Fact]
    public void Prep_SettingsSteps_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var openable = Build(Lenovo(), settingsOpenable: true);
            Assert.Equal("Open Windows Update", Step(openable, PrepStepKind.WindowsUpdate).Actions.Single().Label);
            Assert.Equal("Open optional updates", Step(openable, PrepStepKind.OptionalUpdates).Actions.Single().Label);

            var notes = Build(Lenovo(), settingsOpenable: false);
            Assert.Equal("Open Settings › Windows Update.", Step(notes, PrepStepKind.WindowsUpdate).Note);
            Assert.Equal(
                "Open Settings › Windows Update › Advanced options › Optional updates.",
                Step(notes, PrepStepKind.OptionalUpdates).Note);
        }
    }

    [Fact]
    public void Prep_Chipset_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var intel = Step(Build(Lenovo()), PrepStepKind.Chipset);
            Assert.Equal(new[] { "Processor = Intel" }, Lines(intel));
            Assert.Equal("Intel drivers page", intel.Actions.Single().Label);

            var unknown = Step(
                Build(Lenovo() with { Product = Reading<PrepProduct>.Done(new PrepProduct(null, null, null)) }),
                PrepStepKind.Chipset);
            Assert.Equal(new[] { "Processor = Unknown" }, Lines(unknown));
            Assert.Equal("Windows Update will install the chipset drivers.", unknown.Note);
        }
    }

    [Fact]
    public void Prep_Graphics_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var graphics = Step(Build(Lenovo()), PrepStepKind.Graphics);
            Assert.Equal(
                new[]
                {
                    "Intel(R) Iris(R) Xe Graphics = driver 31.0.101.5186, 12/01/2024, Intel Corporation",
                    "NVIDIA card = No driver (Microsoft Basic Display Adapter)",
                },
                Lines(graphics));
            Assert.Equal(new[] { "Intel drivers page", "NVIDIA drivers page" }, graphics.Actions.Select(a => a.Label).ToArray());

            var vm = Step(Build(HyperV()), PrepStepKind.Graphics);
            Assert.Equal(new[] { "Graphics card = No driver (Microsoft Basic Display Adapter)" }, Lines(vm));

            var unknownPci = Lenovo() with
            {
                Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[] { new PrepGpu(null, @"PCI\VEN_1A03&DEV_2000&SUBSYS_1&REV_41", null, null, null, null) }),
            };
            var line = Step(Build(unknownPci), PrepStepKind.Graphics).Lines.Single();
            Assert.Equal("Graphics card = Unknown", line.Label + " = " + line.Value);
            Assert.Equal("Search for a driver", line.Action!.Label);

            var none = Lenovo() with { Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(Array.Empty<PrepGpu>()) };
            Assert.Equal(new[] { "Graphics cards = No graphics card found" }, Lines(Step(Build(none), PrepStepKind.Graphics)));
        }
    }

    [Fact]
    public void Prep_Support_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var lenovo = Step(Build(Lenovo()), PrepStepKind.Support);
            Assert.Equal(new[] { "Manufacturer = Lenovo", "Serial number = PF2KQ7ZT" }, Lines(lenovo));
            Assert.Equal("Lenovo support page", lenovo.Actions.Single().Label);

            var selfBuilt = Lenovo() with
            {
                Board = Reading<BoardInfo>.Done(new BoardInfo(
                    "ASUSTeK COMPUTER INC.", "ROG STRIX B550-F GAMING", "2803", null, FirmwareKind.Uefi, "System manufacturer", "System Product Name")),
            };
            Assert.Equal("Search for a support page", Step(Build(selfBuilt), PrepStepKind.Support).Actions.Single().Label);
        }
    }

    [Fact]
    public void Prep_Devices_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var devices = Step(Build(Lenovo()), PrepStepKind.Devices);
            Assert.Equal(
                new[]
                {
                    "Kontroler PCI Simple Communications = no driver",
                    "Unknown device = disabled",
                    "Sensor = problem, code 10",
                },
                Lines(devices));
            Assert.Equal("Search Google", devices.Lines[0].Action!.Label);
            Assert.Null(devices.Lines[1].Action);

            var ok = Step(Build(HyperV()), PrepStepKind.Devices);
            Assert.Equal("All devices have a driver.", ok.Lines.Single().Value);
        }
    }

    [Fact]
    public async Task Prep_ReadFailed_LogEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var log = new ListLog();
            var prep = new FakePrepReader { Activation = () => throw new InvalidOperationException("SLS") };
            await new PrepLoader(new FakeHardwareReader(), prep, log, Generous).LoadAsync(_ => { });
            Assert.Equal("WARN [Preparation] Couldn't read \"Activation\": SLS", Assert.Single(log.Lines));
        }
    }

    [Theory]
    [InlineData("Board", "Computer")]
    [InlineData("Product", "Product")]
    [InlineData("BitLocker", "BitLocker")]
    [InlineData("Graphics", "Graphics")]
    [InlineData("Devices", "Devices")]
    public async Task Prep_PartNames_LogEnglish(string part, string expected)
    {
        using (L.Use(UiLanguage.English))
        {
            var log = new ListLog();
            var hardware = new FakeHardwareReader();
            var prep = new FakePrepReader();
            switch (part)
            {
                case "Board": hardware.Board = () => throw new InvalidOperationException("X"); break;
                case "Product": prep.Product = () => throw new InvalidOperationException("X"); break;
                case "BitLocker": hardware.BitLocker = () => throw new InvalidOperationException("X"); break;
                case "Graphics": prep.Graphics = () => throw new InvalidOperationException("X"); break;
                default: prep.Devices = () => throw new InvalidOperationException("X"); break;
            }

            await new PrepLoader(hardware, prep, log, Generous).LoadAsync(_ => { });
            Assert.Equal($"WARN [Preparation] Couldn't read \"{expected}\": X", Assert.Single(log.Lines));
        }
    }

    [Fact]
    public async Task Prep_RefreshFailed_LogEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var log = new ListLog();
            await new PrepLoader(new FakeHardwareReader(), new FakePrepReader(), log, Generous).LoadAsync(_ => throw new InvalidOperationException("UI"));
            Assert.Equal("WARN [Preparation] Couldn't refresh the Preparation screen: UI", Assert.Single(log.Lines));
        }
    }
}
