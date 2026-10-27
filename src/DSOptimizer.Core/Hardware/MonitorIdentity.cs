using System;

namespace DSOptimizer.Core.Hardware;

/// <summary>
/// Parowanie monitora z WMI (<c>WmiMonitorID.InstanceName</c>, np. <c>DISPLAY\GSM5B7F\5&amp;1a2b3c4d&amp;0&amp;UID4352_0</c>)
/// z nazwą interfejsu z <c>EnumDisplayDevices(EDD_GET_DEVICE_INTERFACE_NAME)</c>
/// (np. <c>\\?\DISPLAY#GSM5B7F#5&amp;1a2b3c4d&amp;0&amp;UID4352#{GUID}</c>). Obie postacie sprowadza do identyfikatora urządzenia.
/// </summary>
public static class MonitorIdentity
{
    private const string InterfacePrefix = @"\\?\";

    public static bool Matches(string? wmiInstanceName, string? deviceInterfaceName)
    {
        var fromWmi = FromWmiInstance(wmiInstanceName);
        var fromInterface = FromDeviceInterface(deviceInterfaceName);
        return fromWmi.Length > 0 && string.Equals(fromWmi, fromInterface, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Obcina końcowe <c>_N</c> (numer instancji WMI).</summary>
    private static string FromWmiInstance(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var underscore = name!.LastIndexOf('_');
        if (underscore >= 0 && underscore < name.Length - 1 && AllDigits(name, underscore + 1))
        {
            name = name.Substring(0, underscore);
        }

        return name.Trim();
    }

    /// <summary>Obcina <c>\\?\</c> i końcowe <c>#{GUID}</c>, zamienia <c>#</c> na <c>\</c>.</summary>
    private static string FromDeviceInterface(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        if (name!.StartsWith(InterfacePrefix, StringComparison.Ordinal))
        {
            name = name.Substring(InterfacePrefix.Length);
        }

        var guid = name.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0 && name.EndsWith("}", StringComparison.Ordinal))
        {
            name = name.Substring(0, guid);
        }

        return name.Replace('#', '\\').Trim();
    }

    private static bool AllDigits(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
