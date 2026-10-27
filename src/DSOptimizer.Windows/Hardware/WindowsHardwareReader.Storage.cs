using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Windows.Hardware;

public sealed partial class WindowsHardwareReader
{
    private const string ReliabilityCounterClass = "MSFT_StorageReliabilityCounter";

    /// <summary>
    /// Dyski fizyczne z <c>MSFT_PhysicalDisk</c>; temperatura z powiązanego <c>MSFT_StorageReliabilityCounter</c> (0 °C = brak).
    /// Liczników nie da się wyliczyć samą klasą (host: 0 instancji), tylko przez powiązanie z dyskiem – tak jak w sondzie.
    /// Błąd odczytu licznika nie przekreśla dysku – zostaje bez temperatury.
    /// </summary>
    public IReadOnlyList<DiskInfo> ReadDisks()
        => CimReader.Query(
                CimReader.Storage,
                "SELECT ObjectId, DeviceId, FriendlyName, Size, BusType, MediaType, HealthStatus, SerialNumber FROM MSFT_PhysicalDisk",
                static m => new DiskInfo(
                    int.TryParse(m["DeviceId"] as string, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null,
                    CimReader.Text(m, "FriendlyName"),
                    CimReader.ULong(m, "Size"),
                    CimReader.Int(m, "BusType"),
                    CimReader.Int(m, "MediaType"),
                    DiskTemperature(m),
                    DiskSerial(m["SerialNumber"] as string),
                    CimReader.Int(m, "HealthStatus")))
            .OrderBy(d => d.Number ?? int.MaxValue)
            .ToList();

    /// <summary>Numer seryjny bez spacji i końcowej kropki (format NVMe <c>xxxx_xxxx.</c>).</summary>
    internal static string? DiskSerial(string? raw) => CimReader.Clean(CimReader.Clean(raw)?.TrimEnd('.'));

    private static int? DiskTemperature(ManagementBaseObject disk)
    {
        try
        {
            return CimReader.Related(CimReader.Storage, disk, ReliabilityCounterClass, static c => CimReader.Int(c, "Temperature"))
                .FirstOrDefault(t => t is > 0);
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }
}
