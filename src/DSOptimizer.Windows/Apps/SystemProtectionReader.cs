using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using DSOptimizer.Core.Abstractions;
using Microsoft.Win32;

namespace DSOptimizer.Windows.Apps;

/// <summary>
/// Stan ochrony systemu dysku C: bez jej zmiany (pomiar VM 26H2, Task 2): wartość REG_MULTI_SZ
/// <c>{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}</c> w <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients</c> (widok 64-bit).
/// Włączona, gdy któryś wpis zawiera GUID woluminu C: z <c>Win32_Volume</c> (<c>DriveLetter='C:'</c>, <c>DeviceID</c>);
/// brak wartości (albo klucza) = wyłączona; błąd odczytu, inny typ wartości albo brak GUID-u woluminu = null.
/// </summary>
public sealed class SystemProtectionReader : ISystemProtectionReader
{
    public const string ClientsKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients";

    public const string ClientsValueName = "{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}";

    private readonly RegistryHive _hive;
    private readonly string _keyPath;
    private readonly Func<string?> _systemVolumeId;

    public SystemProtectionReader()
        : this(RegistryHive.LocalMachine, ClientsKeyPath, SystemVolumeDeviceId)
    {
    }

    /// <summary>Wstrzykiwany korzeń rejestru i identyfikator woluminu (testy na piaskownicy HKCU).</summary>
    public SystemProtectionReader(RegistryHive hive, string keyPath, Func<string?> systemVolumeId)
    {
        _hive = hive;
        _keyPath = keyPath ?? throw new ArgumentNullException(nameof(keyPath));
        _systemVolumeId = systemVolumeId ?? throw new ArgumentNullException(nameof(systemVolumeId));
    }

    public bool? IsEnabled()
    {
        try
        {
            var guid = VolumeGuid(_systemVolumeId());
            if (guid is null)
            {
                return null;
            }

            using var root = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(_keyPath, writable: false);
            var value = key?.GetValue(ClientsValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value switch
            {
                null => false,
                string[] entries => entries.Any(entry => entry.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or ManagementException or COMException)
        {
            return null;
        }
    }

    /// <summary><c>{GUID}</c> z <c>\\?\Volume{GUID}\</c>; null, gdy identyfikator nie ma tej postaci.</summary>
    internal static string? VolumeGuid(string? deviceId)
    {
        if (deviceId is null)
        {
            return null;
        }

        var start = deviceId.IndexOf('{');
        var end = start < 0 ? -1 : deviceId.IndexOf('}', start);
        return end > start + 1 ? deviceId.Substring(start, end - start + 1) : null;
    }

    private static string? SystemVolumeDeviceId()
    {
        using var searcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_Volume WHERE DriveLetter = 'C:'");
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                if (item["DeviceID"] is string deviceId)
                {
                    return deviceId;
                }
            }
        }

        return null;
    }
}
