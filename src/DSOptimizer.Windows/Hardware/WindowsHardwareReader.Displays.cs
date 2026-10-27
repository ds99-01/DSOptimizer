using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows.Hardware;

public sealed partial class WindowsHardwareReader
{
    private sealed record WmiMonitor(string InstanceName, string? Name, int? WidthCm, int? HeightCm);

    /// <summary>
    /// Ekrany pulpitu z user32 (rozdzielczość i odświeżanie bieżącego trybu), sparowane po identyfikatorze urządzenia z WMI
    /// (<c>WmiMonitorID</c> – nazwa, <c>WmiMonitorBasicDisplayParams</c> – rozmiar w cm). Monitor bez pary w WMI = bez nazwy i rozmiaru.
    /// </summary>
    public IReadOnlyList<DisplayInfo> ReadDisplays()
    {
        var monitors = ReadWmiMonitors();
        var displays = new List<DisplayInfo>();
        foreach (var (adapterName, width, height, refreshHz) in DesktopAdapters())
        {
            var interfaces = MonitorInterfaces(adapterName);
            if (interfaces.Count == 0)
            {
                displays.Add(new DisplayInfo(null, null, null, width, height, refreshHz));
                continue;
            }

            foreach (var deviceInterface in interfaces)
            {
                var wmi = monitors.FirstOrDefault(m => MonitorIdentity.Matches(m.InstanceName, deviceInterface));
                displays.Add(new DisplayInfo(wmi?.Name, wmi?.WidthCm, wmi?.HeightCm, width, height, refreshHz));
            }
        }

        return displays;
    }

    /// <summary>Nazwa z <c>UserFriendlyName</c>: kody UTF-16 do pierwszego zera.</summary>
    internal static string? MonitorName(ushort[]? userFriendlyName)
    {
        if (userFriendlyName is null)
        {
            return null;
        }

        var chars = userFriendlyName.TakeWhile(code => code != 0).Select(code => (char)code).ToArray();
        return CimReader.Clean(new string(chars));
    }

    /// <summary>Karty GDI przyłączone do pulpitu z bieżącym trybem (<c>ENUM_CURRENT_SETTINGS</c>); tryb nieodczytany = null.</summary>
    private static List<(string Name, int? Width, int? Height, int? RefreshHz)> DesktopAdapters()
    {
        var adapters = new List<(string, int?, int?, int?)>();
        for (uint index = 0; ; index++)
        {
            var device = NewDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            if ((device.StateFlags & NativeMethods.DisplayDeviceAttachedToDesktop) == 0 || string.IsNullOrEmpty(device.DeviceName))
            {
                continue;
            }

            var mode = new NativeMethods.DevMode { Size = (ushort)Marshal.SizeOf<NativeMethods.DevMode>() };
            if (NativeMethods.EnumDisplaySettings(device.DeviceName, NativeMethods.EnumCurrentSettings, ref mode))
            {
                adapters.Add((device.DeviceName, Positive(mode.PelsWidth), Positive(mode.PelsHeight), RefreshRate(mode.DisplayFrequency)));
            }
            else
            {
                adapters.Add((device.DeviceName, null, null, null));
            }
        }

        return adapters;
    }

    /// <summary>
    /// Nazwy interfejsów aktywnych monitorów karty GDI (<c>EDD_GET_DEVICE_INTERFACE_NAME</c>). Monitor bez <c>DISPLAY_DEVICE_ACTIVE</c>
    /// (host: drugi monitor podłączony, ale nie na pulpicie) nie dostaje trybu tej karty.
    /// </summary>
    private static List<string> MonitorInterfaces(string adapterName)
    {
        var interfaces = new List<string>();
        for (uint index = 0; ; index++)
        {
            var monitor = NewDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(adapterName, index, ref monitor, NativeMethods.EddGetDeviceInterfaceName))
            {
                break;
            }

            if ((monitor.StateFlags & NativeMethods.DisplayDeviceActive) != 0 && !string.IsNullOrEmpty(monitor.DeviceId))
            {
                interfaces.Add(monitor.DeviceId);
            }
        }

        return interfaces;
    }

    /// <summary>
    /// Monitory z <c>root\wmi</c> sparowane po <c>InstanceName</c>. Błąd WMI nie przekreśla odczytu z user32:
    /// ekrany zostają bez nazwy i rozmiaru (jak monitor bez pary).
    /// </summary>
    private static List<WmiMonitor> ReadWmiMonitors()
    {
        try
        {
            var names = CimReader.Query(
                CimReader.Wmi,
                "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID",
                static m => (Instance: m["InstanceName"] as string, Name: MonitorName(m["UserFriendlyName"] as ushort[])));
            var sizes = CimReader.Query(
                CimReader.Wmi,
                "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams",
                static m => (Instance: m["InstanceName"] as string, Width: Positive(CimReader.UInt(m, "MaxHorizontalImageSize")), Height: Positive(CimReader.UInt(m, "MaxVerticalImageSize"))));

            var instances = names.Select(n => n.Instance).Concat(sizes.Select(s => s.Instance))
                .Where(i => !string.IsNullOrEmpty(i))
                .Select(i => i!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return instances
                .Select(instance =>
                {
                    var size = sizes.FirstOrDefault(s => string.Equals(s.Instance, instance, StringComparison.OrdinalIgnoreCase));
                    var name = names.FirstOrDefault(n => string.Equals(n.Instance, instance, StringComparison.OrdinalIgnoreCase)).Name;
                    return new WmiMonitor(instance, name, size.Width, size.Height);
                })
                .ToList();
        }
        catch (ManagementException)
        {
            return new List<WmiMonitor>();
        }
        catch (COMException)
        {
            return new List<WmiMonitor>();
        }
    }

    /// <summary><c>dmDisplayFrequency</c>: 0 i 1 oznaczają „domyślne odświeżanie sprzętu” (MS DEVMODE) – to brak wartości, nie 1 Hz.</summary>
    internal static int? RefreshRate(uint displayFrequency) => displayFrequency > 1 ? Positive(displayFrequency) : null;

    private static NativeMethods.DisplayDevice NewDisplayDevice()
        => new() { Size = (uint)Marshal.SizeOf<NativeMethods.DisplayDevice>() };

    private static int? Positive(uint? value) => value is > 0 and <= int.MaxValue ? (int)value.Value : null;
}
