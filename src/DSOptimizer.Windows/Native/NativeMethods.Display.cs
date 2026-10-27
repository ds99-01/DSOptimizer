using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

/// <summary>user32: wyliczanie ekranów pulpitu i ich bieżącego trybu (tylko odczyt).</summary>
internal static partial class NativeMethods
{
    /// <summary><c>DISPLAY_DEVICE_ATTACHED_TO_DESKTOP</c> (karta/źródło GDI należy do pulpitu).</summary>
    internal const uint DisplayDeviceAttachedToDesktop = 0x00000001;

    /// <summary><c>DISPLAY_DEVICE_ACTIVE</c> u monitora: GDI pokazuje go jako włączony (monitor podłączony, ale odłączony od pulpitu nie ma tej flagi).</summary>
    internal const uint DisplayDeviceActive = 0x00000001;

    /// <summary><c>EDD_GET_DEVICE_INTERFACE_NAME</c>: <c>DeviceID</c> monitora = nazwa interfejsu urządzenia.</summary>
    internal const uint EddGetDeviceInterfaceName = 0x00000001;

    /// <summary><c>ENUM_CURRENT_SETTINGS</c>: bieżący tryb ekranu.</summary>
    internal const int EnumCurrentSettings = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(string? device, uint deviceIndex, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    /// <summary><c>DISPLAY_DEVICEW</c> (840 bajtów).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public uint Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    /// <summary><c>DEVMODEW</c> w wariancie ekranu (220 bajtów; <c>dmPelsWidth</c> 172, <c>dmPelsHeight</c> 176, <c>dmDisplayFrequency</c> 184).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }
}
