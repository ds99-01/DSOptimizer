using System;
using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

/// <summary>kernel32: kod wyjścia procesu (wariant startu z czekaniem dla deinstalatora, bez Job Objectu).</summary>
internal static partial class NativeMethods
{
    /// <summary><c>STILL_ACTIVE</c> (259): proces jeszcze działa.</summary>
    internal const uint StillActive = 259;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
}
