using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Prep;
using Xunit;

namespace DSOptimizer.Tests.Unit.Prep;

public class PrepStepsTests
{
    private const string Loading = SpecificationFormatter.LoadingText;
    private const string Failed = "Nie udało się odczytać";
    private const string SystemDrive = "C:";

    private static readonly string IntelGraphicsUrl = PrepLinks.Graphics(GpuVendor.Intel)!.Url;
    private static readonly string NvidiaGraphicsUrl = PrepLinks.Graphics(GpuVendor.Nvidia)!.Url;
    private static readonly string AmdGraphicsUrl = PrepLinks.Graphics(GpuVendor.Amd)!.Url;

    // ---------- scenariusze ----------

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
            new ProblemDevice("Kontroler PCI Simple Communications", 28,
                new[] { @"PCI\VEN_8086&DEV_A0E8&SUBSYS_22D817AA&REV_20", @"PCI\VEN_8086&DEV_A0E8&CC_078000" }),
            new ProblemDevice("  ", 28, new[] { @"USB\VID_0BDA&PID_8771&REV_0100", @"USB\VID_0BDA&PID_8771" }),
        }),
    };

    private static PrepSnapshot HyperV() => new()
    {
        Board = Reading<BoardInfo>.Done(new BoardInfo(
            "Microsoft Corporation", "Virtual Machine", "Hyper-V UEFI Release v4.1", new DateTime(2025, 3, 4), FirmwareKind.Uefi,
            "Microsoft Corporation", "Virtual Machine")),
        Product = Reading<PrepProduct>.Done(new PrepProduct("Hyper-V UEFI Release v4.1", "7491-1319-6942-4807-0972-0051-44", "AuthenticAMD")),
        BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[] { new BitLockerVolume("C:", 0, 0) }),
        Activation = Reading<ActivationInfo>.Done(new ActivationInfo(5, "Windows(R), Professional edition")),
        Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[]
        {
            new PrepGpu("Microsoft Hyper-V Video", @"VMBUS\{DA0A7802-E377-4AAC-8E77-0558EB1073F8}\{5620E0C7-8062-4DCE-AEB7-520C7EF76171}",
                "wvmbusvideo.inf", "10.0.26100.1", new DateTime(2006, 6, 21), "Microsoft"),
            new PrepGpu("Podstawowa karta graficzna Microsoft", @"PCI\VEN_1414&DEV_5353\3&1", null, null, null, null),
        }),
        Devices = Reading<IReadOnlyList<ProblemDevice>>.Done(Array.Empty<ProblemDevice>()),
    };

    private static PrepSnapshot SelfBuilt() => Lenovo() with
    {
        Board = Reading<BoardInfo>.Done(new BoardInfo(
            "ASUSTeK COMPUTER INC.", "ROG STRIX B550-F GAMING", "2803", new DateTime(2022, 4, 27), FirmwareKind.Uefi,
            "System manufacturer", "System Product Name")),
        Product = Reading<PrepProduct>.Done(new PrepProduct("System Version", "System Serial Number", "AuthenticAMD")),
    };

    private static PrepSnapshot Odd() => Lenovo() with
    {
        Board = Reading<BoardInfo>.Done(new BoardInfo(
            "Płyty \"Ząb\" & Syn", "Model\\1 #2", "v1", null, FirmwareKind.Uefi, "Płyty \"Ząb\" & Syn", "Model\\1 #2 'x' (y)")),
        Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[]
        {
            new PrepGpu("Karta \"&\"", @"PCI\VEN_1A03&DEV_2000&SUBSYS_1&REV_41", null, null, null, null),
            new PrepGpu("Inna karta", "PCI\\VEN_ŁĄ&DEV_\"x\" #1", null, null, null, null),
        }),
        Devices = Reading<IReadOnlyList<ProblemDevice>>.Done(new[]
        {
            new ProblemDevice("Urządzenie \"A&B\"", 1, new[] { "ROOT\\ŻÓŁW&\"1\" #2 + 3" }),
        }),
    };

    private static IReadOnlyList<PrepStep> Build(PrepSnapshot snapshot) => PrepSteps.Build(snapshot, SystemDrive, settingsOpenable: true);

    private static PrepStep Step(IReadOnlyList<PrepStep> steps, PrepStepKind kind) => steps.Single(s => s.Kind == kind);

    private static PrepLine Line(PrepStep step, string label) => step.Lines.Single(l => l.Label == label);

    private static IEnumerable<PrepAction> AllActions(IReadOnlyList<PrepStep> steps)
        => steps.SelectMany(s => s.Actions.Concat(s.Lines.Where(l => l.Action is not null).Select(l => l.Action!)));

    // ---------- testy ----------

    [Fact]
    public void Build_AlwaysSevenStepsInOrder()
    {
        foreach (var snapshot in new[] { new PrepSnapshot(), Lenovo(), HyperV() })
        {
            var steps = Build(snapshot);

            Assert.Equal(
                new[]
                {
                    PrepStepKind.Computer, PrepStepKind.WindowsUpdate, PrepStepKind.OptionalUpdates, PrepStepKind.Chipset,
                    PrepStepKind.Graphics, PrepStepKind.Support, PrepStepKind.Devices,
                },
                steps.Select(s => s.Kind).ToArray());
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, steps.Select(s => s.Number).ToArray());
            Assert.Equal(
                new[]
                {
                    "Komputer", "Windows Update", "Opcjonalne aktualizacje sterowników", "Sterowniki chipsetu", "Grafika",
                    "Wsparcie producenta", "Urządzenia bez sterownika",
                },
                steps.Select(s => s.Title).ToArray());
        }
    }

    [Fact]
    public void PageTexts_AreExact()
    {
        Assert.Equal("Przygotowanie", PrepText.PageTitle);
        Assert.Equal("Co sprawdzić po instalacji Windows. Program niczego tu nie instaluje ani nie zmienia.", PrepText.PageSubtitle);
        Assert.Equal(Loading, PrepText.Loading);
        Assert.Equal(Failed, PrepText.Failed);
    }

    [Fact]
    public void Build_LenovoUsesProductVersion()
    {
        var computer = Step(Build(Lenovo()), PrepStepKind.Computer);

        Assert.Equal(new[] { "Producent i model", "BIOS", "BitLocker", "Aktywacja Windows" }, computer.Lines.Select(l => l.Label).ToArray());
        Assert.Equal("Lenovo ThinkPad X1 Carbon Gen 9 (20XW003GPB)", Line(computer, "Producent i model").Value);
        Assert.Equal("N32ET86W (1.62), 14.03.2024", Line(computer, "BIOS").Value);
        Assert.Equal(new PrepLine("BitLocker", "Dysk zaszyfrowany, ochrona wyłączona", PrepTone.Attention), Line(computer, "BitLocker"));
        Assert.Equal(new PrepLine("Aktywacja Windows", "Aktywowany", PrepTone.Normal), Line(computer, "Aktywacja Windows"));
        Assert.Null(computer.Note);
        Assert.Empty(computer.Actions);
    }

    [Theory]
    [InlineData("Lenovo IdeaPad 5 14ALC05", "Lenovo IdeaPad 5 14ALC05 (20XW003GPB)")]
    [InlineData("  ThinkPad T14 Gen 3  ", "Lenovo ThinkPad T14 Gen 3 (20XW003GPB)")]
    [InlineData("", "LENOVO 20XW003GPB")]
    [InlineData(null, "LENOVO 20XW003GPB")]
    [InlineData("Default string", "LENOVO 20XW003GPB")]
    public void Build_LenovoVersionVariants(string? version, string expected)
    {
        var snapshot = Lenovo() with { Product = Reading<PrepProduct>.Done(new PrepProduct(version, "PF2KQ7ZT", "GenuineIntel")) };

        Assert.Equal(expected, Line(Step(Build(snapshot), PrepStepKind.Computer), "Producent i model").Value);
    }

    [Fact]
    public void Build_LenovoProductPending_ShowsSystemNames()
        => Assert.Equal(
            "LENOVO 20XW003GPB",
            Line(Step(Build(Lenovo() with { Product = Reading<PrepProduct>.Pending }), PrepStepKind.Computer), "Producent i model").Value);

    [Fact]
    public void Build_NonLenovoIgnoresProductVersion()
    {
        var snapshot = HyperV();

        Assert.Equal("Microsoft Corporation Virtual Machine", Line(Step(Build(snapshot), PrepStepKind.Computer), "Producent i model").Value);
        Assert.Equal("Hyper-V UEFI Release v4.1, 04.03.2025", Line(Step(Build(snapshot), PrepStepKind.Computer), "BIOS").Value);
    }

    [Theory]
    [InlineData(1, 1, "Włączony", PrepTone.Attention)]
    [InlineData(2, 0, "Szyfrowanie w toku", PrepTone.Attention)]
    [InlineData(1, 0, "Dysk zaszyfrowany, ochrona wyłączona", PrepTone.Attention)]
    [InlineData(3, 0, "Odszyfrowywanie w toku", PrepTone.Normal)]
    [InlineData(0, 0, "Wyłączony", PrepTone.Normal)]
    [InlineData(null, null, "Nie wiadomo", PrepTone.Normal)]
    public void Build_BitLockerTexts(int? conversion, int? protection, string value, PrepTone tone)
    {
        var snapshot = Lenovo() with { BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Done(new[] { new BitLockerVolume("C:", conversion, protection) }) };

        var computer = Step(Build(snapshot), PrepStepKind.Computer);

        Assert.Equal(new PrepLine("BitLocker", value, tone), Line(computer, "BitLocker"));
        Assert.Equal(value == "Włączony" ? "Zapisz klucz odzyskiwania, zanim zmienisz BIOS albo dysk." : null, computer.Note);
    }

    [Theory]
    [InlineData(1u, "Aktywowany", PrepTone.Normal)]
    [InlineData(0u, "Nieaktywowany", PrepTone.Problem)]
    [InlineData(2u, "Okres próbny", PrepTone.Attention)]
    [InlineData(5u, "Wymaga aktywacji", PrepTone.Problem)]
    [InlineData(null, "Brak klucza produktu", PrepTone.Problem)]
    [InlineData(8u, "Nie wiadomo", PrepTone.Normal)]
    public void Build_ActivationTexts(uint? status, string value, PrepTone tone)
    {
        var snapshot = Lenovo() with { Activation = Reading<ActivationInfo>.Done(new ActivationInfo(status, null)) };

        Assert.Equal(new PrepLine("Aktywacja Windows", value, tone), Line(Step(Build(snapshot), PrepStepKind.Computer), "Aktywacja Windows"));
    }

    [Fact]
    public void Build_HyperVm_NoSupportButton_VmNote()
    {
        var steps = Build(HyperV());

        var support = Step(steps, PrepStepKind.Support);
        Assert.Empty(support.Actions);
        Assert.Equal("Maszyna wirtualna – brak strony producenta.", support.Note);
        Assert.Equal("Microsoft Corporation", Line(support, "Producent").Value);
        Assert.Equal("7491-1319-6942-4807-0972-0051-44", Line(support, "Numer seryjny").Value);

        var graphics = Step(steps, PrepStepKind.Graphics);
        Assert.Empty(graphics.Actions);
        Assert.Equal(new PrepLine("Microsoft Hyper-V Video", "sterownik 10.0.26100.1, 21.06.2006, Microsoft", PrepTone.Normal), graphics.Lines[0]);
        Assert.Equal(new PrepLine("Karta graficzna", "Brak sterownika (Podstawowa karta graficzna Microsoft)", PrepTone.Problem), graphics.Lines[1]);

        var chipset = Step(steps, PrepStepKind.Chipset);
        Assert.Equal(new PrepLine("Procesor", "AMD", PrepTone.Normal), Assert.Single(chipset.Lines));
        Assert.Equal(new PrepAction("Strona sterowników AMD", PrepLinks.Chipset(CpuVendor.Amd)!.Url), Assert.Single(chipset.Actions));

        var surface = PrepLinks.Oem("Microsoft Corporation")!.Url;
        Assert.DoesNotContain(AllActions(steps), a => a.Url == surface);
        Assert.DoesNotContain(AllActions(steps), a => a.Url.StartsWith("https://www.google.com/", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Surface_SupportLink()
    {
        var snapshot = HyperV() with
        {
            Board = Reading<BoardInfo>.Done(new BoardInfo(
                "Microsoft Corporation", "Surface Laptop 5", "1.2", null, FirmwareKind.Uefi, "Microsoft Corporation", "Surface Laptop 5")),
        };

        var support = Step(Build(snapshot), PrepStepKind.Support);

        Assert.Equal(new PrepAction("Strona wsparcia Microsoft Surface", PrepLinks.Oem("Microsoft Corporation")!.Url), Assert.Single(support.Actions));
        Assert.Equal("Microsoft Surface", Line(support, "Producent").Value);
        Assert.Null(support.Note);
    }

    [Fact]
    public void Build_Lenovo_SupportLinkWithoutSerialInUrl()
    {
        var support = Step(Build(Lenovo()), PrepStepKind.Support);

        Assert.Equal(new[] { "Producent", "Numer seryjny" }, support.Lines.Select(l => l.Label).ToArray());
        Assert.Equal("Lenovo", Line(support, "Producent").Value);
        Assert.Equal("PF2KQ7ZT", Line(support, "Numer seryjny").Value);
        var action = Assert.Single(support.Actions);
        Assert.Equal(new PrepAction("Strona wsparcia Lenovo", "https://pcsupport.lenovo.com/pl/pl/"), action);
        Assert.DoesNotContain("PF2KQ7ZT", action.Url, StringComparison.Ordinal);
        Assert.Null(support.Note);
    }

    [Fact]
    public void Build_SelfBuiltPc_SearchesByBoard()
    {
        var steps = Build(SelfBuilt());

        var support = Step(steps, PrepStepKind.Support);
        Assert.Equal(
            new PrepAction("Szukaj strony wsparcia", PrepLinks.Search("ASUSTeK COMPUTER INC. ROG STRIX B550-F GAMING support")),
            Assert.Single(support.Actions));
        Assert.Equal("ASUSTeK COMPUTER INC.", Line(support, "Producent").Value);
        Assert.DoesNotContain(support.Lines, l => l.Label == "Numer seryjny");
        Assert.Equal("ASUSTeK COMPUTER INC. ROG STRIX B550-F GAMING", Line(Step(steps, PrepStepKind.Computer), "Producent i model").Value);
    }

    [Fact]
    public void Build_UnknownManufacturer_SearchesBySystemNames()
    {
        var snapshot = Lenovo() with
        {
            Board = Reading<BoardInfo>.Done(new BoardInfo(
                "Gigabyte Technology Co., Ltd.", "B650 AORUS ELITE", "F20", null, FirmwareKind.Uefi, "Gigabyte Technology Co., Ltd.", "B650 AORUS ELITE AX")),
        };

        var support = Step(Build(snapshot), PrepStepKind.Support);

        Assert.Equal(
            new PrepAction("Szukaj strony wsparcia", PrepLinks.Search("Gigabyte Technology Co., Ltd. B650 AORUS ELITE AX support")),
            Assert.Single(support.Actions));
        Assert.Equal("Gigabyte Technology Co., Ltd.", Line(support, "Producent").Value);
    }

    [Fact]
    public void Build_AllNamesPlaceholder_NoSupportSearch()
    {
        var snapshot = SelfBuilt() with
        {
            Board = Reading<BoardInfo>.Done(new BoardInfo(
                "To Be Filled By O.E.M.", "  ", null, null, FirmwareKind.Unknown, "System manufacturer", "System Product Name")),
        };

        var steps = Build(snapshot);

        Assert.Empty(Step(steps, PrepStepKind.Support).Actions);
        Assert.Equal("Nie wiadomo", Line(Step(steps, PrepStepKind.Support), "Producent").Value);
        Assert.Equal("Nie wiadomo", Line(Step(steps, PrepStepKind.Computer), "Producent i model").Value);
        Assert.Equal("Nie wiadomo", Line(Step(steps, PrepStepKind.Computer), "BIOS").Value);
    }

    [Fact]
    public void Build_TwoGpusBasicAndNvidia_TwoLinesOneNvidiaButton_BasicIsProblem()
    {
        var graphics = Step(Build(Lenovo()), PrepStepKind.Graphics);

        Assert.Equal(
            new[]
            {
                new PrepLine("Intel(R) Iris(R) Xe Graphics", "sterownik 31.0.101.5186, 12.01.2024, Intel Corporation", PrepTone.Normal),
                new PrepLine("Karta NVIDIA", "Brak sterownika (Podstawowa karta graficzna Microsoft)", PrepTone.Problem),
            },
            graphics.Lines.ToArray());
        Assert.Equal(
            new[]
            {
                new PrepAction("Strona sterowników Intel", IntelGraphicsUrl),
                new PrepAction("Strona sterowników NVIDIA", NvidiaGraphicsUrl),
            },
            graphics.Actions.ToArray());
        Assert.Null(graphics.Note);
    }

    [Fact]
    public void Build_TwoCardsSameVendor_OneButton()
    {
        var snapshot = Lenovo() with
        {
            Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[]
            {
                new PrepGpu("AMD Radeon(TM) Graphics", @"PCI\VEN_1002&DEV_1638", null, "31.0.1", null, null),
                new PrepGpu("Microsoft Basic Display Adapter", @"PCI\VEN_1002&DEV_73DF", null, null, null, null),
            }),
        };

        var graphics = Step(Build(snapshot), PrepStepKind.Graphics);

        Assert.Equal(new PrepAction("Strona sterowników AMD", AmdGraphicsUrl), Assert.Single(graphics.Actions));
        Assert.Equal(new PrepLine("AMD Radeon(TM) Graphics", "sterownik 31.0.1", PrepTone.Normal), graphics.Lines[0]);
        Assert.Equal(new PrepLine("Karta AMD", "Brak sterownika (Podstawowa karta graficzna Microsoft)", PrepTone.Problem), graphics.Lines[1]);
    }

    [Fact]
    public void Build_UnknownGpuVendor_SearchButton_NoVendorLink()
    {
        var snapshot = Lenovo() with
        {
            Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[]
            {
                new PrepGpu("ASPEED Graphics Family(WDDM)", @"PCI\VEN_1A03&DEV_2000&SUBSYS_20001A03&REV_41\4&2", null, "9.0.10", null, "ASPEED"),
                new PrepGpu("  ", null, null, null, null, null),
            }),
        };

        var graphics = Step(Build(snapshot), PrepStepKind.Graphics);

        Assert.Empty(graphics.Actions);
        Assert.Equal(
            new PrepLine("ASPEED Graphics Family(WDDM)", "sterownik 9.0.10, ASPEED", PrepTone.Normal,
                Action: new PrepAction("Szukaj sterownika", PrepLinks.Search(@"PCI\VEN_1A03&DEV_2000"))),
            graphics.Lines[0]);
        Assert.Equal(new PrepLine("Karta graficzna", "Nie wiadomo", PrepTone.Normal), graphics.Lines[1]);
    }

    [Fact]
    public void Build_TwoUnknownPciCards_TwoLinksInTwoLines_NoneInHeader()
    {
        var snapshot = Lenovo() with
        {
            Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[]
            {
                new PrepGpu("ASPEED Graphics Family(WDDM)", @"PCI\VEN_1A03&DEV_2000&SUBSYS_20001A03&REV_41\4&2", null, "9.0.10", null, "ASPEED"),
                new PrepGpu("Matrox G200eR2", @"PCI\VEN_102B&DEV_0534&SUBSYS_0000&REV_01\4&3", null, null, null, null),
                new PrepGpu("NVIDIA T400", @"PCI\VEN_10DE&DEV_1FF2", null, "32.0.1", null, "NVIDIA"),
            }),
        };

        var graphics = Step(Build(snapshot), PrepStepKind.Graphics);

        Assert.Equal(new PrepAction("Szukaj sterownika", PrepLinks.Search(@"PCI\VEN_1A03&DEV_2000")), graphics.Lines[0].Action);
        Assert.Equal(new PrepAction("Szukaj sterownika", PrepLinks.Search(@"PCI\VEN_102B&DEV_0534")), graphics.Lines[1].Action);
        Assert.Null(graphics.Lines[2].Action);
        Assert.Equal(new PrepAction("Strona sterowników NVIDIA", NvidiaGraphicsUrl), Assert.Single(graphics.Actions));
    }

    [Theory]
    [InlineData("Microsoft Remote Display Adapter", @"SWD\REMOTEDISPLAYENUM\RDPIDD_INDIRECTDISPLAY&SESSIONID_0002")]
    [InlineData("DisplayLink USB Device", @"USB\VID_17E9&PID_6006\1234")]
    [InlineData("Microsoft Hyper-V Video", @"VMBUS\{DA0A7802-E377-4AAC-8E77-0558EB1073F8}\{5620E0C7-8062-4DCE-AEB7-520C7EF76171}")]
    [InlineData("Podstawowa karta graficzna Microsoft", @"ROOT\BASICDISPLAY\0000")]
    public void Build_UnknownNonPciCard_NoLink(string name, string pnpDeviceId)
    {
        var snapshot = Lenovo() with
        {
            Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[] { new PrepGpu(name, pnpDeviceId, null, "1.0", null, "Microsoft") }),
        };

        var graphics = Step(Build(snapshot), PrepStepKind.Graphics);

        Assert.Empty(graphics.Actions);
        Assert.Null(Assert.Single(graphics.Lines).Action);
    }

    [Fact]
    public void Build_BasicDisplayUnknownPciVendor_LinkInLine()
    {
        var snapshot = Lenovo() with
        {
            Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[] { new PrepGpu("Microsoft Basic Display Adapter", @"pci\ven_1a03&dev_2000\4&2", null, null, null, null) }),
        };

        var line = Assert.Single(Step(Build(snapshot), PrepStepKind.Graphics).Lines);

        Assert.Equal(
            new PrepLine("Karta graficzna", "Brak sterownika (Podstawowa karta graficzna Microsoft)", PrepTone.Problem,
                Action: new PrepAction("Szukaj sterownika", PrepLinks.Search(@"pci\ven_1a03&dev_2000"))),
            line);
    }

    [Fact]
    public void Build_BoardFailed_VmbusCardWithoutLink()
    {
        // Błąd odczytu płyty = traktowane jak PC; karta Hyper-V (VMBUS) i tak nie dostaje linku, karta PCI o nieznanym VEN – w wierszu.
        var graphics = Step(Build(HyperV() with { Board = Reading<BoardInfo>.Failed }), PrepStepKind.Graphics);

        Assert.Empty(graphics.Actions);
        Assert.Null(graphics.Lines[0].Action);
        Assert.Equal(new PrepAction("Szukaj sterownika", PrepLinks.Search(@"PCI\VEN_1414&DEV_5353")), graphics.Lines[1].Action);
    }

    [Fact]
    public void Build_Vm_UnknownPciCard_NoLineLink()
        => Assert.All(Step(Build(HyperV()), PrepStepKind.Graphics).Lines, l => Assert.Null(l.Action));

    [Fact]
    public void Build_NoGraphicsCards_Line()
    {
        var graphics = Step(Build(Lenovo() with { Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(Array.Empty<PrepGpu>()) }), PrepStepKind.Graphics);

        Assert.Equal(new PrepLine("Karty graficzne", "Nie znaleziono karty graficznej", PrepTone.Normal), Assert.Single(graphics.Lines));
        Assert.Empty(graphics.Actions);
    }

    [Fact]
    public void Build_ProblemDevices_SearchOnlyForMissingDriver_CodesTexts()
    {
        var snapshot = Lenovo() with
        {
            Devices = Reading<IReadOnlyList<ProblemDevice>>.Done(new[]
            {
                new ProblemDevice("Kontroler PCI Simple Communications", 28, new[] { @"PCI\VEN_8086&DEV_A0E8&SUBSYS_22D817AA&REV_20" }),
                new ProblemDevice(null, 28, new[] { @"USB\VID_0BDA&PID_8771&REV_0100" }),
                new ProblemDevice("Composite Bus Enumerator", 22, new[] { @"ROOT\CompositeBus" }),
                new ProblemDevice(" Czytnik ", 10, Array.Empty<string>()),
            }),
        };

        var devices = Step(Build(snapshot), PrepStepKind.Devices);

        Assert.Equal(
            new[]
            {
                new PrepLine("Kontroler PCI Simple Communications", "brak sterownika", PrepTone.Problem,
                    @"PCI\VEN_8086&DEV_A0E8", new PrepAction("Szukaj w Google", PrepLinks.Search(@"PCI\VEN_8086&DEV_A0E8"))),
                new PrepLine("Nieznane urządzenie", "brak sterownika", PrepTone.Problem,
                    @"USB\VID_0BDA&PID_8771", new PrepAction("Szukaj w Google", PrepLinks.Search(@"USB\VID_0BDA&PID_8771"))),
                new PrepLine("Composite Bus Enumerator", "wyłączone", PrepTone.Problem, @"ROOT\CompositeBus"),
                new PrepLine("Czytnik", "problem, kod 10", PrepTone.Problem),
            },
            devices.Lines.ToArray());
        Assert.Empty(devices.Actions);
        Assert.Null(devices.Note);
    }

    [Fact]
    public void Build_NoProblemDevices_AllGoodLine()
    {
        var devices = Step(Build(HyperV()), PrepStepKind.Devices);

        Assert.Equal(new PrepLine(string.Empty, "Wszystkie urządzenia mają sterownik.", PrepTone.Normal), Assert.Single(devices.Lines));
        Assert.Empty(devices.Actions);
    }

    [Fact]
    public void Build_PendingPart_LoadingText_NoDependentButtons()
    {
        var steps = Build(new PrepSnapshot());

        var computer = Step(steps, PrepStepKind.Computer);
        Assert.All(computer.Lines, l => Assert.Equal(new PrepLine(l.Label, Loading, PrepTone.Normal), l));
        Assert.Equal(4, computer.Lines.Count);
        Assert.Null(computer.Note);

        Assert.Equal(new PrepLine("Procesor", Loading, PrepTone.Normal), Assert.Single(Step(steps, PrepStepKind.Chipset).Lines));
        Assert.Equal(new PrepLine("Karty graficzne", Loading, PrepTone.Normal), Assert.Single(Step(steps, PrepStepKind.Graphics).Lines));
        Assert.Equal(
            new[] { new PrepLine("Producent", Loading, PrepTone.Normal), new PrepLine("Numer seryjny", Loading, PrepTone.Normal) },
            Step(steps, PrepStepKind.Support).Lines.ToArray());
        Assert.Equal(new PrepLine("Urządzenia z problemem", Loading, PrepTone.Normal), Assert.Single(Step(steps, PrepStepKind.Devices).Lines));

        foreach (var kind in new[] { PrepStepKind.Computer, PrepStepKind.Chipset, PrepStepKind.Graphics, PrepStepKind.Support, PrepStepKind.Devices })
        {
            Assert.Empty(Step(steps, kind).Actions);
            Assert.Null(Step(steps, kind).Note);
        }

        Assert.Single(Step(steps, PrepStepKind.WindowsUpdate).Actions);
        Assert.Single(Step(steps, PrepStepKind.OptionalUpdates).Actions);
    }

    [Fact]
    public void Build_BoardPending_NoGraphicsOrSupportButtons()
    {
        // Bez płyty nie wiadomo, czy to maszyna wirtualna – linki grafiki i wsparcia czekają.
        var steps = Build(Lenovo() with { Board = Reading<BoardInfo>.Pending });

        Assert.Empty(Step(steps, PrepStepKind.Graphics).Actions);
        Assert.Empty(Step(steps, PrepStepKind.Support).Actions);
        Assert.Equal(2, Step(steps, PrepStepKind.Graphics).Lines.Count);
        Assert.Equal(new PrepLine("Producent", Loading, PrepTone.Normal), Line(Step(steps, PrepStepKind.Support), "Producent"));
        Assert.Equal("PF2KQ7ZT", Line(Step(steps, PrepStepKind.Support), "Numer seryjny").Value);
        Assert.Single(Step(steps, PrepStepKind.Chipset).Actions);
    }

    [Fact]
    public void Build_ProductPending_ChipsetLoading_SupportStillLinked()
    {
        var steps = Build(Lenovo() with { Product = Reading<PrepProduct>.Pending });

        var chipset = Step(steps, PrepStepKind.Chipset);
        Assert.Equal(new PrepLine("Procesor", Loading, PrepTone.Normal), Assert.Single(chipset.Lines));
        Assert.Empty(chipset.Actions);
        Assert.Null(chipset.Note);

        var support = Step(steps, PrepStepKind.Support);
        Assert.Equal(new PrepLine("Numer seryjny", Loading, PrepTone.Normal), Line(support, "Numer seryjny"));
        Assert.Single(support.Actions);
    }

    [Fact]
    public void Build_FailedActivation_OthersStillBuilt()
    {
        var steps = Build(Lenovo() with { Activation = Reading<ActivationInfo>.Failed });

        var computer = Step(steps, PrepStepKind.Computer);
        Assert.Equal(new PrepLine("Aktywacja Windows", Failed, PrepTone.Normal), Line(computer, "Aktywacja Windows"));
        Assert.Equal("Lenovo ThinkPad X1 Carbon Gen 9 (20XW003GPB)", Line(computer, "Producent i model").Value);
        Assert.Equal(2, Step(steps, PrepStepKind.Graphics).Actions.Count);
        Assert.Single(Step(steps, PrepStepKind.Support).Actions);
        Assert.Equal(2, Step(steps, PrepStepKind.Devices).Lines.Count);
    }

    [Fact]
    public void Build_EveryPartFailed_FailedTextsNoDependentButtons()
    {
        var snapshot = new PrepSnapshot
        {
            Board = Reading<BoardInfo>.Failed,
            Product = Reading<PrepProduct>.Failed,
            BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Failed,
            Activation = Reading<ActivationInfo>.Failed,
            Graphics = Reading<IReadOnlyList<PrepGpu>>.Failed,
            Devices = Reading<IReadOnlyList<ProblemDevice>>.Failed,
        };

        var steps = Build(snapshot);

        foreach (var kind in new[] { PrepStepKind.Computer, PrepStepKind.Chipset, PrepStepKind.Graphics, PrepStepKind.Support, PrepStepKind.Devices })
        {
            var step = Step(steps, kind);
            Assert.All(step.Lines, l => Assert.Equal(new PrepLine(l.Label, Failed, PrepTone.Normal), l));
            Assert.Empty(step.Actions);
            Assert.Null(step.Note);
        }

        Assert.Equal(new[] { "Producent", "Numer seryjny" }, Step(steps, PrepStepKind.Support).Lines.Select(l => l.Label).ToArray());
        Assert.Equal("Urządzenia z problemem", Assert.Single(Step(steps, PrepStepKind.Devices).Lines).Label);
    }

    [Fact]
    public void Build_GraphicsFailedButBoardVm_StillNoButtons()
        => Assert.Empty(Step(Build(HyperV() with { Graphics = Reading<IReadOnlyList<PrepGpu>>.Failed }), PrepStepKind.Graphics).Actions);

    [Fact]
    public void Build_BoardFailed_GraphicsLinksStillShown()
        => Assert.Equal(2, Step(Build(Lenovo() with { Board = Reading<BoardInfo>.Failed }), PrepStepKind.Graphics).Actions.Count);

    [Theory]
    [InlineData("GenuineIntel", "Intel", "Strona sterowników Intel")]
    [InlineData("AuthenticAMD", "AMD", "Strona sterowników AMD")]
    public void Build_ChipsetKnownVendor(string manufacturer, string value, string label)
    {
        var snapshot = Lenovo() with { Product = Reading<PrepProduct>.Done(new PrepProduct(null, null, manufacturer)) };

        var chipset = Step(Build(snapshot), PrepStepKind.Chipset);

        Assert.Equal(new PrepLine("Procesor", value, PrepTone.Normal), Assert.Single(chipset.Lines));
        Assert.Equal(label, Assert.Single(chipset.Actions).Label);
        Assert.Null(chipset.Note);
    }

    [Theory]
    [InlineData(" CentaurHauls ", "CentaurHauls")]
    [InlineData(null, "Nie wiadomo")]
    public void Build_ChipsetUnknownVendor_NoteNoButton(string? manufacturer, string value)
    {
        var snapshot = Lenovo() with { Product = Reading<PrepProduct>.Done(new PrepProduct(null, null, manufacturer)) };

        var chipset = Step(Build(snapshot), PrepStepKind.Chipset);

        Assert.Equal(new PrepLine("Procesor", value, PrepTone.Normal), Assert.Single(chipset.Lines));
        Assert.Empty(chipset.Actions);
        Assert.Equal("Sterowniki chipsetu przyjdą z Windows Update.", chipset.Note);
    }

    [Fact]
    public void Build_SettingsNotOpenable_NoteInsteadOfButton()
    {
        var steps = PrepSteps.Build(Lenovo(), SystemDrive, settingsOpenable: false);

        var update = Step(steps, PrepStepKind.WindowsUpdate);
        Assert.Empty(update.Actions);
        Assert.Empty(update.Lines);
        Assert.Equal("Otwórz Ustawienia › Windows Update.", update.Note);

        var optional = Step(steps, PrepStepKind.OptionalUpdates);
        Assert.Empty(optional.Actions);
        Assert.Empty(optional.Lines);
        Assert.Equal("Otwórz Ustawienia › Windows Update › Opcje zaawansowane › Aktualizacje opcjonalne.", optional.Note);

        Assert.DoesNotContain(AllActions(steps), a => a.Url.StartsWith("ms-settings:", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_SettingsOpenable_TwoSettingsButtons()
    {
        var steps = PrepSteps.Build(Lenovo(), SystemDrive, settingsOpenable: true);

        var update = Step(steps, PrepStepKind.WindowsUpdate);
        Assert.Equal(new PrepAction("Otwórz Windows Update", PrepLinks.WindowsUpdate), Assert.Single(update.Actions));
        Assert.Null(update.Note);
        Assert.Empty(update.Lines);

        var optional = Step(steps, PrepStepKind.OptionalUpdates);
        Assert.Equal(new PrepAction("Otwórz aktualizacje opcjonalne", PrepLinks.OptionalUpdates), Assert.Single(optional.Actions));
        Assert.Null(optional.Note);
    }

    [Fact]
    public void Build_DefaultOverload_UsesMeasuredSettingsOpenable()
    {
        var steps = PrepSteps.Build(Lenovo(), SystemDrive);

        Assert.Equal(PrepLinks.SettingsOpenable ? 1 : 0, Step(steps, PrepStepKind.WindowsUpdate).Actions.Count);
        Assert.Equal(PrepLinks.SettingsOpenable ? 1 : 0, Step(steps, PrepStepKind.OptionalUpdates).Actions.Count);
    }

    [Fact]
    public void Build_NullSnapshot_Throws()
        => Assert.Throws<ArgumentNullException>(() => PrepSteps.Build(null!, SystemDrive));

    [Fact]
    public void Build_OddCharacters_SearchOnlyEncodedUrls()
    {
        var steps = Build(Odd());

        var support = Step(steps, PrepStepKind.Support);
        Assert.Equal(
            PrepLinks.Search("Płyty \"Ząb\" & Syn Model\\1 #2 'x' (y) support"),
            Assert.Single(support.Actions).Url);
        var graphics = Step(steps, PrepStepKind.Graphics);
        Assert.Empty(graphics.Actions);
        Assert.All(graphics.Lines, l => Assert.NotNull(l.Action));
        Assert.Equal(PrepLinks.Search("PCI\\VEN_ŁĄ&DEV_\"x\" #1"), graphics.Lines[1].Action!.Url);
        Assert.Single(AllActions(new[] { Step(steps, PrepStepKind.Devices) }));
    }

    public static TheoryData<string> Scenarios => new() { "Lenovo", "HyperV", "SelfBuilt", "Odd", "Pending", "Failed", "UnknownGpu" };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Build_EveryActionUrl_IsAllowedByPolicy(string scenario)
    {
        var snapshot = scenario switch
        {
            "Lenovo" => Lenovo(),
            "HyperV" => HyperV(),
            "SelfBuilt" => SelfBuilt(),
            "Odd" => Odd(),
            "Pending" => new PrepSnapshot(),
            "Failed" => Lenovo() with { Activation = Reading<ActivationInfo>.Failed, Devices = Reading<IReadOnlyList<ProblemDevice>>.Failed },
            "UnknownGpu" => Lenovo() with
            {
                Graphics = Reading<IReadOnlyList<PrepGpu>>.Done(new[] { new PrepGpu("ASPEED", @"PCI\VEN_1A03&DEV_2000", null, null, null, null) }),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        foreach (var settingsOpenable in new[] { true, false })
        {
            var actions = AllActions(PrepSteps.Build(snapshot, SystemDrive, settingsOpenable)).ToList();

            if (scenario != "Pending")
            {
                Assert.Contains(actions, a => !a.Url.StartsWith("ms-settings:", StringComparison.Ordinal));
            }

            Assert.All(actions, a =>
            {
                Assert.False(string.IsNullOrWhiteSpace(a.Label));
                Assert.True(ExternalLinkPolicy.IsAllowed(a.Url), a.Url);
            });
        }
    }
}
