using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using DSOptimizer.Core.Hardware;
using Microsoft.Win32;

namespace DSOptimizer.Windows.Hardware;

/// <summary>Podklucz klasy wyświetlania (<c>...\Class\{4d36e968-...}\NNNN</c>) – tylko to, czego trzeba do pamięci karty.</summary>
internal sealed record DisplayClassEntry(string SubKey, string? MatchingDeviceId, string? DriverDesc, ulong? MemorySize);

public sealed partial class WindowsHardwareReader
{
    private const string DisplayClassPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string QwMemorySizeValue = "HardwareInformation.qwMemorySize";
    private const int QwordLength = 8;

    public IReadOnlyList<GpuInfo> ReadGraphics()
    {
        // Karty wyłącznie z Win32_VideoController; rejestr tylko dociąga pamięć (podklucze iGPU bez instancji nie dodają kart).
        var controllers = CimReader.Query(
            CimReader.Cimv2,
            "SELECT Name, DriverVersion, AdapterRAM, PNPDeviceID FROM Win32_VideoController",
            static v => (
                Name: CimReader.Text(v, "Name"),
                DriverVersion: CimReader.Text(v, "DriverVersion"),
                AdapterRam: CimReader.UInt(v, "AdapterRAM"),
                PnpDeviceId: CimReader.Text(v, "PNPDeviceID")));
        if (controllers.Count == 0)
        {
            return Array.Empty<GpuInfo>();
        }

        var entries = ReadDisplayClassEntries();
        return controllers
            .Select(c => new GpuInfo(c.Name, GpuMemory.Resolve(SelectGpuMemory(entries, c.PnpDeviceId, c.Name), c.AdapterRam), c.DriverVersion))
            .ToList();
    }

    /// <summary>
    /// <c>qwMemorySize</c> podklucza pasującego do karty: <c>PNPDeviceID</c> zaczyna się od <c>MatchingDeviceId</c>, albo <c>DriverDesc</c> = nazwa.
    /// Kolejność kandydatów: obie reguły, potem z pamięcią, potem nazwa podklucza. Brak kandydata = null.
    /// </summary>
    internal static ulong? SelectGpuMemory(IReadOnlyList<DisplayClassEntry> entries, string? pnpDeviceId, string? name)
    {
        return entries
            .Select(e => (Entry: e, ById: MatchesById(e, pnpDeviceId), ByName: MatchesByName(e, name)))
            .Where(c => c.ById || c.ByName)
            .OrderByDescending(c => c.ById && c.ByName)
            .ThenByDescending(c => c.Entry.MemorySize.HasValue)
            .ThenBy(c => c.Entry.SubKey, StringComparer.Ordinal)
            .Select(c => c.Entry.MemorySize)
            .FirstOrDefault();
    }

    private static bool MatchesById(DisplayClassEntry entry, string? pnpDeviceId)
        => !string.IsNullOrEmpty(entry.MatchingDeviceId)
            && pnpDeviceId is not null
            && pnpDeviceId.StartsWith(entry.MatchingDeviceId, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesByName(DisplayClassEntry entry, string? name)
        => !string.IsNullOrEmpty(entry.DriverDesc)
            && name is not null
            && string.Equals(entry.DriverDesc!.Trim(), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Podklucze <c>NNNN</c> klasy wyświetlania (rejestr 64-bit, tylko odczyt); podklucz bez dostępu jest pomijany.</summary>
    private static List<DisplayClassEntry> ReadDisplayClassEntries()
    {
        var entries = new List<DisplayClassEntry>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var classKey = root.OpenSubKey(DisplayClassPath, writable: false);
        if (classKey is null)
        {
            return entries;
        }

        foreach (var subKeyName in classKey.GetSubKeyNames().Where(IsAdapterSubKey).OrderBy(n => n, StringComparer.Ordinal))
        {
            try
            {
                using var subKey = classKey.OpenSubKey(subKeyName, writable: false);
                if (subKey is not null)
                {
                    entries.Add(new DisplayClassEntry(
                        subKeyName,
                        subKey.GetValue("MatchingDeviceId") as string,
                        subKey.GetValue("DriverDesc") as string,
                        MemorySize(subKey.GetValue(QwMemorySizeValue))));
                }
            }
            catch (SecurityException)
            {
                // Podklucz bez prawa odczytu nie opisuje karty – pomijamy go.
            }
            catch (UnauthorizedAccessException)
            {
                // j.w.
            }
        }

        return entries;
    }

    private static bool IsAdapterSubKey(string name) => name.Length == 4 && name.All(c => c is >= '0' and <= '9');

    /// <summary>REG_QWORD → bajty; dla pewności też 8 bajtów little-endian i dodatni DWORD. Zero lub mniej = brak.</summary>
    private static ulong? MemorySize(object? raw) => raw switch
    {
        long value when value > 0 => (ulong)value,
        int value when value > 0 => (ulong)value,
        byte[] bytes when bytes.Length == QwordLength && BitConverter.ToUInt64(bytes, 0) is var value && value > 0 => value,
        _ => null,
    };
}
