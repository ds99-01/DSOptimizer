using System;
using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

/// <summary>kernel32: start procesu przez <c>CreateProcessW</c> (bez dziedziczenia uchwytów) i zamykanie uchwytów.</summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// <c>CreateProcessW</c>. <paramref name="commandLine"/> musi być zapisywalnym buforem zakończonym znakiem <c>\0</c>
    /// (funkcja może go zmieniać), dlatego tablica znaków, a nie <see cref="string"/>.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcess(
        string? applicationName,
        [In, Out] char[]? commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    /// <summary><c>STARTUPINFOW</c>; <see cref="Size"/> (<c>cb</c>) = <c>Marshal.SizeOf</c>, reszta zerowa.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        public uint Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    /// <summary><c>PROCESS_INFORMATION</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }
}
