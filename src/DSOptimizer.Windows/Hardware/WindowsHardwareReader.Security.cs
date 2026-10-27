using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Windows.Native;
using Microsoft.Win32;

namespace DSOptimizer.Windows.Hardware;

public sealed partial class WindowsHardwareReader
{
    private const string SecureBootStatePath = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    private const string TpmVersionNotSupported = "Not Supported";
    private const uint HvciService = 2;

    /// <summary>Brak instancji <c>Win32_Tpm</c> = null („brak”); błąd zapytania leci dalej („brak danych”).</summary>
    public TpmInfo? ReadTpm()
        => CimReader.Query(
                CimReader.Tpm,
                "SELECT IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion FROM Win32_Tpm",
                static t => new TpmInfo(
                    TpmSpecVersion(CimReader.Text(t, "SpecVersion")),
                    CimReader.Bool(t, "IsEnabled_InitialValue") == true,
                    CimReader.Bool(t, "IsActivated_InitialValue") == true))
            .FirstOrDefault();

    /// <summary>BIOS Legacy → <see cref="SecureBootState.LegacyBios"/>; UEFI → <c>UEFISecureBootEnabled</c> (1 włączony, 0 wyłączony, brak = nieznany).</summary>
    public SecureBootState ReadSecureBoot()
    {
        switch (ReadFirmwareType())
        {
            case NativeMethods.FirmwareTypeBios:
                return SecureBootState.LegacyBios;
            case NativeMethods.FirmwareTypeUefi:
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = root.OpenSubKey(SecureBootStatePath, writable: false))
                {
                    return key?.GetValue("UEFISecureBootEnabled") switch
                    {
                        1 => SecureBootState.On,
                        0 => SecureBootState.Off,
                        _ => SecureBootState.Unknown,
                    };
                }

            default:
                return SecureBootState.Unknown;
        }
    }

    /// <summary><c>VirtualizationBasedSecurityStatus</c> (0/1/2) i HVCI = <c>SecurityServicesRunning</c> zawiera 2. Brak instancji = status nieznany.</summary>
    public VbsInfo ReadVbs()
        => CimReader.Query(
                CimReader.DeviceGuard,
                "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard",
                static d => new VbsInfo(
                    CimReader.Int(d, "VirtualizationBasedSecurityStatus"),
                    d["SecurityServicesRunning"] is uint[] running && running.Contains(HvciService)))
            .FirstOrDefault()
            ?? new VbsInfo(null, false);

    /// <summary>Woluminy z literą dysku (bez litery są pomijane).</summary>
    public IReadOnlyList<BitLockerVolume> ReadBitLocker()
        => CimReader.Query(
                CimReader.VolumeEncryption,
                "SELECT DriveLetter, ConversionStatus, ProtectionStatus FROM Win32_EncryptableVolume",
                static v => (
                    Letter: CimReader.Text(v, "DriveLetter"),
                    Conversion: CimReader.Int(v, "ConversionStatus"),
                    Protection: CimReader.Int(v, "ProtectionStatus")))
            .Where(v => v.Letter is not null)
            .Select(v => new BitLockerVolume(v.Letter!, v.Conversion, v.Protection))
            .OrderBy(v => v.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>MS zwraca <c>"Not Supported"</c>, gdy wersji nie da się ustalić – to brak wersji, nie wersja.</summary>
    private static string? TpmSpecVersion(string? specVersion)
        => string.Equals(specVersion, TpmVersionNotSupported, StringComparison.OrdinalIgnoreCase) ? null : specVersion;
}
