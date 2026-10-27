using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DSOptimizer.Core.Hardware;

public sealed record SpecField(string Key, string Value, bool IsContinuation);

public sealed record SpecGroup(string Title, IReadOnlyList<SpecField> Fields);

/// <summary>
/// Zamiana <see cref="HardwareSnapshot"/> na grupy ekranu „Specyfikacja” i na raport tekstowy.
/// Brakujące części wartości (null, zero) są pomijane razem z przecinkiem; wynik nie zależy od kultury wątku
/// (przecinek dziesiętny tylko przez pl-PL, daty i liczby niezmiennicze).
/// </summary>
public static class SpecificationFormatter
{
    public const string LoadingText = "Wczytywanie…";
    public const string NoDataText = "brak danych";
    public const string AbsentText = "brak";

    private const double Gib = 1024d * 1024d * 1024d;
    private const int FirstWindows11Build = 22000;
    private const string Indent = "  ";
    private const string DetailIndent = "    ";
    private const string NewLine = "\r\n";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Polish = new("pl-PL");

    private static readonly string[] PlaceholderNames =
    {
        "System manufacturer",
        "System Product Name",
        "To Be Filled By O.E.M.",
        "Default string",
        "System Serial Number",
    };

    private sealed record Entry(SpecField Field, IReadOnlyList<string> Details);

    private sealed record BuiltGroup(string Title, IReadOnlyList<Entry> Entries);

    public static IReadOnlyList<SpecGroup> Groups(HardwareSnapshot snapshot)
        => Build(snapshot, forReport: false)
            .Select(g => new SpecGroup(g.Title, g.Entries.Select(e => e.Field).ToList()))
            .ToList();

    public static string Report(HardwareSnapshot snapshot, string machineName, DateTime localNow)
    {
        var text = new StringBuilder();
        text.Append("DSOptimizer ").Append(ProductInfo.Version).Append(" – specyfikacja komputera").Append(NewLine);
        text.Append("Komputer: ").Append(machineName).Append(NewLine);
        text.Append("Data: ").Append(localNow.ToString("yyyy-MM-dd HH:mm", Invariant)).Append(NewLine);

        foreach (var group in Build(snapshot, forReport: true))
        {
            text.Append(NewLine).Append(group.Title).Append(NewLine);
            foreach (var entry in group.Entries)
            {
                text.Append(Indent).Append(entry.Field.Key).Append(": ").Append(entry.Field.Value).Append(NewLine);
                foreach (var detail in entry.Details)
                {
                    text.Append(DetailIndent).Append(detail).Append(NewLine);
                }
            }
        }

        return text.ToString();
    }

    public static string FileName(DateTime localNow)
        => "specyfikacja-" + localNow.ToString("yyyy-MM-dd_HHmm", Invariant) + ".txt";

    private static List<BuiltGroup> Build(HardwareSnapshot snapshot, bool forReport) => new()
    {
        new BuiltGroup("Procesor i pamięć", ProcessorAndMemory(snapshot, forReport)),
        new BuiltGroup("Grafika, ekran i dźwięk", GraphicsAndSound(snapshot, forReport)),
        new BuiltGroup("Dyski", DiskEntries(snapshot.Disks).ToList()),
        new BuiltGroup("Sieć", NetworkEntries(snapshot.Network).ToList()),
        new BuiltGroup("System i zabezpieczenia", SystemEntries(snapshot)),
    };

    // ---------- grupy ----------

    private static List<Entry> ProcessorAndMemory(HardwareSnapshot snapshot, bool forReport)
    {
        var entries = new List<Entry>();
        entries.AddRange(Single("Procesor", snapshot.Processor, ProcessorText));
        entries.AddRange(MemoryEntries(snapshot.Memory, forReport));
        entries.AddRange(Single("Płyta główna", snapshot.Board, BoardText));
        if (snapshot.Board.State == ReadingState.Done && snapshot.Board.Value is { } board)
        {
            var computer = ComputerText(board);
            if (computer is not null)
            {
                entries.Add(One("Komputer", computer));
            }
        }

        return entries;
    }

    private static List<Entry> GraphicsAndSound(HardwareSnapshot snapshot, bool forReport)
    {
        var entries = new List<Entry>();
        entries.AddRange(ListEntries("Karta graficzna", snapshot.Graphics, GpuText));
        entries.AddRange(MonitorEntries(snapshot.Displays, forReport));
        entries.AddRange(ListEntries("Dźwięk", snapshot.Sound, name => name));
        return entries;
    }

    private static List<Entry> SystemEntries(HardwareSnapshot snapshot)
    {
        var entries = new List<Entry>();
        entries.AddRange(Single("Windows", snapshot.Windows, WindowsText));
        entries.AddRange(Single("TPM", snapshot.Tpm, TpmText));
        entries.AddRange(Single("Secure Boot", snapshot.SecureBoot, SecureBootText));
        entries.AddRange(Single("VBS i HVCI", snapshot.Vbs, VbsText));
        entries.AddRange(BitLockerEntries(snapshot.BitLocker));
        return entries;
    }

    // ---------- szkielet odczytów ----------

    private static Entry One(string key, string value, bool continuation = false, IReadOnlyList<string>? details = null)
        => new(new SpecField(key, value, continuation), details ?? Array.Empty<string>());

    private static string? StateText(ReadingState state) => state switch
    {
        ReadingState.Pending => LoadingText,
        ReadingState.Failed => NoDataText,
        _ => null,
    };

    private static Entry[] Single<T>(string key, Reading<T> reading, Func<T, string?> format)
    {
        var stateText = StateText(reading.State);
        if (stateText is not null)
        {
            return new[] { One(key, stateText) };
        }

        var text = format(reading.Value!);
        return new[] { One(key, string.IsNullOrWhiteSpace(text) ? NoDataText : text!) };
    }

    private static IEnumerable<Entry> ListEntries<T>(string key, Reading<IReadOnlyList<T>> reading, Func<T, string> format)
    {
        var stateText = StateText(reading.State);
        if (stateText is not null)
        {
            return new[] { One(key, stateText) };
        }

        var values = reading.Value!.Select(format).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (values.Count == 0)
        {
            return new[] { One(key, AbsentText) };
        }

        return values.Select((value, index) => One(key, value, continuation: index > 0)).ToList();
    }

    // ---------- procesor, pamięć, płyta ----------

    private static string? ProcessorText(CpuInfo cpu) => Comma(
        cpu.Name?.Trim(),
        cpu.Cores is > 0 ? N(cpu.Cores.Value) + " " + PolishPlural.Choose(cpu.Cores.Value, "rdzeń", "rdzenie", "rdzeni") : null,
        cpu.Threads is > 0 ? N(cpu.Threads.Value) + " " + PolishPlural.Choose(cpu.Threads.Value, "wątek", "wątki", "wątków") : null);

    private static Entry[] MemoryEntries(Reading<MemoryInfo> reading, bool forReport)
    {
        var stateText = StateText(reading.State);
        if (stateText is not null)
        {
            return new[] { One("Pamięć", stateText) };
        }

        var memory = reading.Value!;
        var modules = memory.Modules ?? Array.Empty<MemoryModule>();
        ulong totalBytes = 0;
        foreach (var module in modules)
        {
            totalBytes += module.CapacityBytes ?? 0;
        }

        var type = modules.Select(m => MemoryType(m.SmbiosType)).FirstOrDefault(t => t is not null);
        var speed = modules.Select(m => m.SpeedMts ?? 0).DefaultIfEmpty(0).Max();
        string? slots = null;
        if (modules.Count > 0 && memory.SlotCount is > 0)
        {
            var noun = memory.SlotCount == 1 ? "gniazda" : "gniazd";
            slots = N(modules.Count) + " z " + N(memory.SlotCount.Value) + " " + noun;
        }

        var value = Comma(Words(GibText(totalBytes), type), speed > 0 ? N(speed) + " MT/s" : null, slots);
        var details = forReport ? modules.Select(ModuleLine).Where(l => l.Length > 0).ToArray() : null;
        return new[] { One("Pamięć", value.Length == 0 ? NoDataText : value, false, details) };
    }

    private static string ModuleLine(MemoryModule module)
    {
        var rest = Comma(
            GibText(module.CapacityBytes ?? 0),
            module.SpeedMts is > 0 ? N(module.SpeedMts.Value) + " MT/s" : null,
            module.Manufacturer?.Trim(),
            module.PartNumber?.Trim());
        var slot = module.Slot?.Trim();
        if (string.IsNullOrEmpty(slot))
        {
            return rest;
        }

        return rest.Length == 0 ? slot! : slot + ": " + rest;
    }

    private static string? MemoryType(int? smbios) => smbios switch
    {
        24 => "DDR3",
        26 => "DDR4",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null,
    };

    private static string? GibText(ulong bytes)
    {
        var gib = (ulong)Math.Round(bytes / Gib, MidpointRounding.AwayFromZero);
        return gib > 0 ? N(gib) + " GB" : null;
    }

    private static string BoardName(BoardInfo board) => Words(board.Manufacturer?.Trim(), board.Product?.Trim());

    private static string? BoardText(BoardInfo board)
    {
        string? bios = null;
        var version = board.BiosVersion?.Trim();
        if (!string.IsNullOrEmpty(version) || board.BiosDate is not null)
        {
            var date = board.BiosDate is { } d ? "(" + d.ToString("yyyy-MM-dd", Invariant) + ")" : null;
            bios = Words("BIOS", version, date);
        }

        var firmware = board.Firmware switch
        {
            FirmwareKind.Uefi => "UEFI",
            FirmwareKind.Legacy => "BIOS Legacy",
            _ => null,
        };
        return Comma(BoardName(board), bios, firmware);
    }

    private static string? ComputerText(BoardInfo board)
    {
        var manufacturer = board.SystemManufacturer?.Trim();
        var model = board.SystemModel?.Trim();
        if (IsPlaceholder(manufacturer) || IsPlaceholder(model))
        {
            return null;
        }

        var computer = Words(manufacturer, model);
        if (computer.Length == 0 || string.Equals(computer, BoardName(board), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return computer;
    }

    public static bool IsPlaceholder(string? value)
        => value is not null && PlaceholderNames.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase));

    // ---------- grafika, monitory ----------

    private static string GpuText(GpuInfo gpu) => Comma(
        gpu.Name?.Trim(),
        GibText(gpu.MemoryBytes ?? 0),
        string.IsNullOrWhiteSpace(gpu.DriverVersion) ? null : "sterownik " + gpu.DriverVersion!.Trim());

    private static IEnumerable<Entry> MonitorEntries(Reading<IReadOnlyList<DisplayInfo>> reading, bool forReport)
    {
        var stateText = StateText(reading.State);
        if (stateText is not null)
        {
            return new[] { One("Monitor", stateText) };
        }

        var displays = reading.Value!;
        if (displays.Count == 0)
        {
            return new[] { One("Monitor", AbsentText) };
        }

        var entries = new List<Entry>();
        for (var i = 0; i < displays.Count; i++)
        {
            var key = displays.Count == 1 ? "Monitor" : "Monitor " + N(i + 1);
            var text = Comma(forReport ? displays[i].Name?.Trim() : null, ScreenText(displays[i]));
            entries.Add(One(key, text.Length == 0 ? NoDataText : text));
        }

        return entries;
    }

    private static string ScreenText(DisplayInfo display)
    {
        string? diagonal = null;
        if (display.WidthCm is > 0 && display.HeightCm is > 0)
        {
            double w = display.WidthCm.Value;
            double h = display.HeightCm.Value;
            var inches = (long)Math.Round(Math.Sqrt((w * w) + (h * h)) / 2.54, MidpointRounding.AwayFromZero);
            diagonal = N(inches) + " " + PolishPlural.Choose(inches, "cal", "cale", "cali");
        }

        var resolution = display.Width is > 0 && display.Height is > 0
            ? N(display.Width.Value) + "×" + N(display.Height.Value)
            : null;
        var refresh = display.RefreshHz is > 0 ? N(display.RefreshHz.Value) + " Hz" : null;
        return Comma(diagonal, resolution, refresh);
    }

    // ---------- dyski, sieć ----------

    private static IEnumerable<Entry> DiskEntries(Reading<IReadOnlyList<DiskInfo>> reading)
    {
        var stateText = StateText(reading.State);
        if (stateText is not null)
        {
            return new[] { One("Dysk", stateText) };
        }

        var disks = reading.Value!;
        if (disks.Count == 0)
        {
            return new[] { One("Dysk", AbsentText) };
        }

        return disks.Select(DiskEntry).ToList();
    }

    private static Entry DiskEntry(DiskInfo disk)
    {
        var key = disk.Number is { } number ? "Dysk " + N(number) : "Dysk";
        var text = Comma(
            Words(disk.Model?.Trim(), DecimalSize(disk.SizeBytes ?? 0)),
            Words(DiskBus(disk.BusType), DiskMedia(disk.MediaType)),
            disk.TemperatureC is > 0 ? N(disk.TemperatureC.Value) + " °C" : null);

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(disk.SerialNumber))
        {
            details.Add("Numer seryjny: " + disk.SerialNumber!.Trim());
        }

        var health = disk.HealthStatus switch
        {
            0 => "zdrowy",
            1 => "ostrzeżenie",
            2 => "uszkodzony",
            _ => "nieznany",
        };
        details.Add("Stan: " + health);
        return One(key, text.Length == 0 ? NoDataText : text, false, details);
    }

    private static string? DecimalSize(ulong bytes)
    {
        if (bytes >= 1_000_000_000_000UL)
        {
            return OneDecimal(bytes / 1e12) + " TB";
        }

        var gb = (ulong)Math.Round(bytes / 1e9, MidpointRounding.AwayFromZero);
        return gb > 0 ? N(gb) + " GB" : null;
    }

    private static string? DiskBus(int? bus) => bus switch
    {
        1 => "SCSI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        17 => "NVMe",
        _ => null,
    };

    private static string? DiskMedia(int? media) => media switch
    {
        3 => "HDD",
        4 => "SSD",
        _ => null,
    };

    private static IEnumerable<Entry> NetworkEntries(Reading<IReadOnlyList<NetworkAdapterInfo>> reading)
    {
        var stateText = StateText(reading.State);
        if (stateText is not null)
        {
            return new[] { One("Karta sieciowa", stateText) };
        }

        var adapters = reading.Value!;
        if (adapters.Count == 0)
        {
            return new[] { One("Karta sieciowa", AbsentText) };
        }

        var entries = new List<Entry>();
        string? previousKey = null;
        foreach (var adapter in adapters)
        {
            var key = adapter.Kind switch
            {
                NetworkKind.Ethernet => "Ethernet",
                NetworkKind.WiFi => "Wi-Fi",
                _ => "Karta sieciowa",
            };
            var text = Comma(
                adapter.Name?.Trim(),
                adapter.Connected ? "połączona" : "rozłączona",
                adapter.Connected ? LinkSpeed(adapter.SpeedBps ?? 0) : null);
            entries.Add(One(key, text, continuation: key == previousKey));
            previousKey = key;
        }

        return entries;
    }

    private static string? LinkSpeed(ulong bps)
    {
        if (bps >= 1_000_000_000UL)
        {
            return OneDecimal(bps / 1e9) + " Gb/s";
        }

        var mbps = (ulong)Math.Round(bps / 1e6, MidpointRounding.AwayFromZero);
        return mbps > 0 ? N(mbps) + " Mb/s" : null;
    }

    private static string OneDecimal(double value)
    {
        const string zero = ",0";
        var text = value.ToString("0.0", Polish);
        return text.EndsWith(zero, StringComparison.Ordinal) ? text.Substring(0, text.Length - zero.Length) : text;
    }

    // ---------- system i zabezpieczenia ----------

    private static string? WindowsText(WindowsInfo windows)
    {
        var head = Words(
            windows.Build >= FirstWindows11Build ? "Windows 11" : "Windows",
            WindowsEditions.DisplayName(windows.Edition),
            windows.DisplayVersion?.Trim());
        return Comma(
            head,
            windows.Build > 0 ? "build " + N(windows.Build) + "." + N(windows.Revision) : null,
            windows.InstallDate is { } date ? "zainstalowany " + date.ToString("yyyy-MM-dd", Invariant) : null);
    }

    private static string? TpmText(TpmInfo? tpm)
    {
        if (tpm is null)
        {
            return AbsentText;
        }

        var spec = tpm.SpecVersion?.Split(',')[0].Trim();
        return Comma(spec, tpm.Enabled && tpm.Activated ? "włączony" : "wyłączony");
    }

    private static string? SecureBootText(SecureBootState state) => state switch
    {
        SecureBootState.On => "Włączony",
        SecureBootState.Off => "Wyłączony",
        SecureBootState.LegacyBios => "Niedostępny (BIOS Legacy)",
        _ => NoDataText,
    };

    private static string? VbsText(VbsInfo vbs) => vbs.Status switch
    {
        2 => vbs.HvciRunning ? "VBS działa, HVCI działa" : "VBS działa, HVCI nie działa",
        1 => "VBS włączone, nie działa",
        0 => "VBS wyłączone",
        _ => NoDataText,
    };

    private static IEnumerable<Entry> BitLockerEntries(Reading<IReadOnlyList<BitLockerVolume>> reading)
    {
        var withLetters = reading.State == ReadingState.Done
            ? reading with { Value = reading.Value!.Where(v => !string.IsNullOrWhiteSpace(v.DriveLetter)).ToList() }
            : reading;
        return ListEntries("BitLocker", withLetters, BitLockerText);
    }

    private static string BitLockerText(BitLockerVolume volume)
    {
        var letter = volume.DriveLetter.Trim();
        var state = volume.ConversionStatus switch
        {
            0 => "niezaszyfrowany",
            1 => "zaszyfrowany",
            2 => "szyfrowanie w toku",
            3 => "odszyfrowywanie w toku",
            4 => "szyfrowanie wstrzymane",
            5 => "odszyfrowywanie wstrzymane",
            _ => null,
        };
        if (state is null)
        {
            return letter + " " + NoDataText;
        }

        var text = letter + " " + state;
        if (volume.ConversionStatus != 0 && volume.ProtectionStatus is 0 or 1)
        {
            text += volume.ProtectionStatus == 1 ? ", ochrona włączona" : ", ochrona wyłączona";
        }

        return text;
    }

    // ---------- pomocnicze ----------

    private static string N(long value) => value.ToString(Invariant);

    private static string N(ulong value) => value.ToString(Invariant);

    /// <summary>Łączy niepuste części przecinkiem.</summary>
    private static string Comma(params string?[] parts)
        => string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>Łączy niepuste części spacją.</summary>
    private static string Words(params string?[] parts)
        => string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
