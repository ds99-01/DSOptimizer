using System;
using System.Runtime.InteropServices;

namespace DSOptimizer.App.Gui;

/// <summary>user32: monitor, na którym leży okno, jego obszar roboczy i prostokąt okna (tylko odczyt).</summary>
internal static class NativeMethods
{
    /// <summary><c>MONITOR_DEFAULTTONEAREST</c>: najbliższy monitor, gdy okno nie leży na żadnym.</summary>
    internal const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    /// <summary><c>RECT</c> w pikselach fizycznych.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary><c>MONITORINFO</c> (40 bajtów); <see cref="Size"/> trzeba ustawić przed wywołaniem.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
