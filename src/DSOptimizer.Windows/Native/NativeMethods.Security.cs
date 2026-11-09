using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Windows.Native;

/// <summary>
/// advapi32/kernel32: deskryptor bezpieczeństwa (właściciel + DACL) odczytany z już otwartego uchwytu pliku
/// (<c>ProtectedFileReader</c>), bez ponownego rozwiązywania ścieżki.
/// </summary>
internal static partial class NativeMethods
{
    internal const uint SeFileObject = 1;
    internal const uint OwnerSecurityInformation = 0x1;
    internal const uint DaclSecurityInformation = 0x4;

    /// <summary>Zwraca kod błędu Win32 (0 = sukces); deskryptor zwalnia <see cref="LocalFree"/>.</summary>
    [DllImport("advapi32.dll", ExactSpelling = true)]
    internal static extern uint GetSecurityInfo(
        SafeFileHandle handle,
        uint objectType,
        uint securityInfo,
        IntPtr owner,
        IntPtr group,
        IntPtr dacl,
        IntPtr sacl,
        out IntPtr securityDescriptor);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    internal static extern uint GetSecurityDescriptorLength(IntPtr securityDescriptor);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    internal static extern IntPtr LocalFree(IntPtr memory);
}
