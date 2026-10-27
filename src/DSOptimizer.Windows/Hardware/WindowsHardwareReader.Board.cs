using System;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows.Hardware;

public sealed partial class WindowsHardwareReader
{
    private const int BiosDateLength = 8;

    public BoardInfo ReadBoard()
    {
        var board = CimReader.Query(
                CimReader.Cimv2,
                "SELECT Manufacturer, Product FROM Win32_BaseBoard",
                static b => (Manufacturer: CimReader.Text(b, "Manufacturer"), Product: CimReader.Text(b, "Product")))
            .FirstOrDefault();

        var bios = CimReader.Query(
                CimReader.Cimv2,
                "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS",
                static b => (Version: CimReader.Text(b, "SMBIOSBIOSVersion"), Date: BiosDate(CimReader.RawDateTime(b, "ReleaseDate"))))
            .FirstOrDefault();

        var system = CimReader.Query(
                CimReader.Cimv2,
                "SELECT Manufacturer, Model FROM Win32_ComputerSystem",
                static s => (Manufacturer: CimReader.Text(s, "Manufacturer"), Model: CimReader.Text(s, "Model")))
            .FirstOrDefault();

        var firmware = ReadFirmwareType() switch
        {
            NativeMethods.FirmwareTypeUefi => FirmwareKind.Uefi,
            NativeMethods.FirmwareTypeBios => FirmwareKind.Legacy,
            _ => FirmwareKind.Unknown,
        };

        return new BoardInfo(board.Manufacturer, board.Product, bios.Version, bios.Date, firmware, system.Manufacturer, system.Model);
    }

    /// <summary>
    /// Data BIOS z pierwszych 8 znaków surowego napisu CIM (<c>yyyyMMdd</c>), bez przeliczania strefy:
    /// <c>ManagementDateTimeConverter</c> przesunąłby ją o dzień na zachód od UTC.
    /// </summary>
    internal static DateTime? BiosDate(string? raw)
    {
        if (raw is null || raw.Length < BiosDateLength)
        {
            return null;
        }

        return DateTime.TryParseExact(raw.Substring(0, BiosDateLength), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary><c>GetFirmwareType</c>; błąd wywołania = 0 (<c>FirmwareTypeUnknown</c>).</summary>
    private static int ReadFirmwareType() => NativeMethods.GetFirmwareType(out var type) ? type : 0;
}
