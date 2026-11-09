using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>Dowiązanie twarde i link symboliczny do pliku przez P/Invoke w procesie testu (bez <c>mklink</c>).</summary>
public static class FileLinks
{
    private const int SymbolicLinkFlagAllowUnprivilegedCreate = 0x2;

    public static void HardLink(string link, string existing)
    {
        if (!CreateHardLinkW(link, existing, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public static void FileSymlink(string link, string target)
    {
        if (!CreateSymbolicLinkW(link, target, SymbolicLinkFlagAllowUnprivilegedCreate))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CreateSymbolicLinkW(string symlinkFileName, string targetFileName, int flags);
}
