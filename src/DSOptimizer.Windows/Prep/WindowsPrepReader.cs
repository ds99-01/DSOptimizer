using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Prep;
using DSOptimizer.Windows.Hardware;

namespace DSOptimizer.Windows.Prep;

/// <summary>
/// Odczyt danych strony „Przygotowanie” przez CIM, wyłącznie do odczytu. Bez własnych limitów czasu
/// (<c>SoftwareLicensingProduct</c> i <c>Win32_PnPSignedDriver</c> bywają wolne – limit jest w <see cref="PrepLoader"/>).
/// </summary>
public sealed class WindowsPrepReader : IPrepReader
{
    /// <summary>ApplicationID systemu Windows w <c>SoftwareLicensingProduct</c>.</summary>
    private const string WindowsApplicationId = "55c92734-d682-4d71-983e-d6ec3f16059f";

    public PrepProduct ReadProduct()
    {
        var product = CimReader.Query(
                CimReader.Cimv2,
                "SELECT Version, IdentifyingNumber FROM Win32_ComputerSystemProduct",
                static p => (Version: CimReader.Text(p, "Version"), Number: CimReader.Text(p, "IdentifyingNumber")))
            .FirstOrDefault();
        var biosSerial = CimReader.Query(
                CimReader.Cimv2,
                "SELECT SerialNumber FROM Win32_BIOS",
                static b => CimReader.Text(b, "SerialNumber"))
            .FirstOrDefault();
        var cpuManufacturer = CimReader.Query(
                CimReader.Cimv2,
                "SELECT Manufacturer FROM Win32_Processor",
                static c => CimReader.Text(c, "Manufacturer"))
            .FirstOrDefault();

        return new PrepProduct(product.Version, FirstSerial(product.Number, biosSerial), cpuManufacturer);
    }

    public ActivationInfo ReadActivation()
    {
        var row = CimReader.Query(
                CimReader.Cimv2,
                "SELECT LicenseStatus, Name FROM SoftwareLicensingProduct WHERE ApplicationID='" + WindowsApplicationId + "' AND PartialProductKey IS NOT NULL",
                static l => new ActivationInfo(CimReader.UInt(l, "LicenseStatus"), CimReader.Text(l, "Name")))
            .FirstOrDefault();

        return row ?? new ActivationInfo(null, null);
    }

    public IReadOnlyList<PrepGpu> ReadGraphics()
    {
        var controllers = CimReader.Query(
            CimReader.Cimv2,
            "SELECT Name, PNPDeviceID, InfFilename, DriverVersion, DriverDate FROM Win32_VideoController",
            static v => (
                Name: CimReader.Text(v, "Name"),
                PnpDeviceId: CimReader.Text(v, "PNPDeviceID"),
                Inf: CimReader.Text(v, "InfFilename"),
                Version: CimReader.Text(v, "DriverVersion"),
                Date: WindowsHardwareReader.BiosDate(CimReader.RawDateTime(v, "DriverDate"))));
        if (controllers.Count == 0)
        {
            return Array.Empty<PrepGpu>();
        }

        var signed = CimReader.Query(
            CimReader.Cimv2,
            "SELECT DeviceID, DriverProviderName, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass='DISPLAY'",
            static d => (
                DeviceId: CimReader.Text(d, "DeviceID"),
                Provider: CimReader.Text(d, "DriverProviderName"),
                Date: WindowsHardwareReader.BiosDate(CimReader.RawDateTime(d, "DriverDate"))));

        return MergeGraphics(controllers, signed);
    }

    public IReadOnlyList<ProblemDevice> ReadProblemDevices()
        => CimReader.Query(
            CimReader.Cimv2,
            "SELECT Name, ConfigManagerErrorCode, HardwareID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0",
            static d => new ProblemDevice(
                CimReader.Text(d, "Name"),
                CimReader.UInt(d, "ConfigManagerErrorCode") ?? 0,
                HardwareIds(d["HardwareID"])));

    /// <summary>Łączy kontrolery z podpisanymi sterownikami po <c>PNPDeviceID</c> = <c>DeviceID</c> (bez rozróżniania wielkości liter); data z kontrolera, a gdy jej brak, ze sterownika.</summary>
    internal static IReadOnlyList<PrepGpu> MergeGraphics(
        IEnumerable<(string? Name, string? PnpDeviceId, string? Inf, string? Version, DateTime? Date)> controllers,
        IEnumerable<(string? DeviceId, string? Provider, DateTime? Date)> signedDrivers)
    {
        var drivers = signedDrivers.ToList();
        return controllers
            .Select(c =>
            {
                var matches = drivers.Where(d => c.PnpDeviceId is not null
                    && string.Equals(d.DeviceId, c.PnpDeviceId, StringComparison.OrdinalIgnoreCase)).ToList();
                var provider = matches.Select(static d => d.Provider).FirstOrDefault(static p => p is not null);
                var date = c.Date ?? matches.Select(static d => d.Date).FirstOrDefault(static d => d is not null);
                return new PrepGpu(c.Name, c.PnpDeviceId, c.Inf, c.Version, date, provider);
            })
            .ToList();
    }

    /// <summary>Pierwsza wartość niepusta i niebędąca zastępczą (<see cref="SpecificationFormatter.IsPlaceholder"/>).</summary>
    internal static string? FirstSerial(string? productIdentifyingNumber, string? biosSerial)
        => new[] { productIdentifyingNumber, biosSerial }
            .Select(CimReader.Clean)
            .FirstOrDefault(static v => v is not null && !SpecificationFormatter.IsPlaceholder(v));

    private static IReadOnlyList<string> HardwareIds(object? raw)
        => raw is string[] ids
            ? ids.Select(CimReader.Clean).Where(static i => i is not null).Select(static i => i!).ToList()
            : Array.Empty<string>();
}
