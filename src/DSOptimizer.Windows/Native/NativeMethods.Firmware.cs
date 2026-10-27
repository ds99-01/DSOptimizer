using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

/// <summary>kernel32: typ oprogramowania układowego (<c>FIRMWARE_TYPE</c>).</summary>
internal static partial class NativeMethods
{
    /// <summary><c>FirmwareTypeBios</c>.</summary>
    internal const int FirmwareTypeBios = 1;

    /// <summary><c>FirmwareTypeUefi</c>.</summary>
    internal const int FirmwareTypeUefi = 2;

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFirmwareType(out int firmwareType);
}
