using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Hardware;
using Microsoft.Win32;

namespace DSOptimizer.Windows.Hardware;

/// <summary>
/// Odczyt sprzętu na Windows wyłącznie ze źródeł potwierdzonych w <c>docs/zrodla/2026-10-19-etap3-a-zrodla.md</c>:
/// CIM przez <see cref="ManagementObjectSearcher"/>, rejestr 64-bit tylko do odczytu, user32 i <c>GetFirmwareType</c>.
/// Nic nie zmienia stanu systemu. Błąd źródła = wyjątek (loader pokazuje „brak danych”); brak urządzeń = pusta lista.
/// </summary>
public sealed partial class WindowsHardwareReader : IHardwareReader
{
    private const string CurrentVersionPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    // Zaślepki z dokumentu źródeł (reguła Slot); VM zwraca też PartNumber = "None".
    private static readonly string[] ModulePlaceholders = { "None", "Unknown", "Not Specified" };

    public CpuInfo ReadProcessor()
    {
        var processors = CimReader.Query(
            CimReader.Cimv2,
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor",
            static p => (Name: CimReader.Text(p, "Name"), Cores: CimReader.Int(p, "NumberOfCores"), Threads: CimReader.Int(p, "NumberOfLogicalProcessors")));

        // Wiele instancji = wiele procesorów fizycznych: rdzenie i wątki się sumują.
        return new CpuInfo(
            processors.Select(p => p.Name).FirstOrDefault(n => n is not null),
            SumOrNull(processors.Select(p => p.Cores)),
            SumOrNull(processors.Select(p => p.Threads)));
    }

    public MemoryInfo ReadMemory()
    {
        var modules = CimReader.Query(
            CimReader.Cimv2,
            "SELECT Capacity, ConfiguredClockSpeed, Speed, SMBIOSMemoryType, Manufacturer, PartNumber, DeviceLocator, BankLabel FROM Win32_PhysicalMemory",
            static m => new MemoryModule(
                MemorySlot(m["DeviceLocator"] as string, m["BankLabel"] as string),
                CimReader.ULong(m, "Capacity"),
                MemorySpeed(CimReader.Int(m, "ConfiguredClockSpeed"), CimReader.Int(m, "Speed")),
                CimReader.Int(m, "SMBIOSMemoryType"),
                ModuleText(m["Manufacturer"] as string),
                ModuleText(m["PartNumber"] as string)));

        var slotCounts = CimReader.Query(
            CimReader.Cimv2,
            "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray",
            static a => CimReader.Int(a, "MemoryDevices"));

        return new MemoryInfo(modules, SumOrNull(slotCounts));
    }

    public IReadOnlyList<string> ReadSound()
        => CimReader.Query(CimReader.Cimv2, "SELECT Name FROM Win32_SoundDevice", static s => CimReader.Text(s, "Name"))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();

    public WindowsInfo ReadWindows()
    {
        string? edition;
        string? displayVersion;
        int build;
        int revision;
        using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var key = root.OpenSubKey(CurrentVersionPath, writable: false)
            ?? throw new InvalidOperationException($@"Brak klucza HKLM\{CurrentVersionPath}."))
        {
            edition = CimReader.Clean(key.GetValue("EditionID") as string);
            displayVersion = CimReader.Clean(key.GetValue("DisplayVersion") as string);
            build = int.TryParse(key.GetValue("CurrentBuild") as string, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            revision = key.GetValue("UBR") is int ubr ? ubr : 0;
        }

        // InstallDate ma offset lokalny, więc konwersja CIM jest tu poprawna (inaczej niż data BIOS).
        var installDate = CimReader.Query(
                CimReader.Cimv2,
                "SELECT InstallDate FROM Win32_OperatingSystem",
                static os => CimReader.RawDateTime(os, "InstallDate"))
            .Where(raw => !string.IsNullOrEmpty(raw))
            .Select(raw => (DateTime?)ManagementDateTimeConverter.ToDateTime(raw))
            .FirstOrDefault();

        return new WindowsInfo(edition, displayVersion, build, revision, installDate);
    }

    /// <summary>
    /// Gniazdo modułu: <c>DeviceLocator</c>, a gdy <c>BankLabel</c> coś dodaje (niepusty, inny, nie zaślepka) – <c>"DIMM 1 (P0 CHANNEL A)"</c>.
    /// Bez lokalizatora sam bank (zaślepka = brak).
    /// </summary>
    internal static string? MemorySlot(string? deviceLocator, string? bankLabel)
    {
        var locator = CimReader.Clean(deviceLocator);
        var bank = ModuleText(bankLabel);
        if (locator is null)
        {
            return bank;
        }

        return bank is not null && !string.Equals(bank, locator, StringComparison.OrdinalIgnoreCase)
            ? $"{locator} ({bank})"
            : locator;
    }

    /// <summary>
    /// Tekst modułu pamięci (<c>BankLabel</c>, <c>Manufacturer</c>, <c>PartNumber</c>) po <see cref="CimReader.Clean"/>;
    /// zaślepka SMBIOS (VM: <c>None</c>) = brak.
    /// </summary>
    internal static string? ModuleText(string? value)
    {
        var text = CimReader.Clean(value);
        return text is not null && ModulePlaceholders.Any(p => string.Equals(p, text, StringComparison.OrdinalIgnoreCase)) ? null : text;
    }

    /// <summary><c>ConfiguredClockSpeed</c> (MT/s wg odczytu hosta), a gdy 0/brak – <c>Speed</c>.</summary>
    private static int? MemorySpeed(int? configuredClockSpeed, int? speed)
    {
        if (configuredClockSpeed is > 0)
        {
            return configuredClockSpeed;
        }

        return speed is > 0 ? speed : null;
    }

    private static int? SumOrNull(IEnumerable<int?> values)
    {
        var known = values.Where(v => v.HasValue).Select(v => (long)v!.Value).ToList();
        if (known.Count == 0)
        {
            return null;
        }

        var sum = known.Sum();
        return sum <= int.MaxValue ? (int)sum : null;
    }
}
