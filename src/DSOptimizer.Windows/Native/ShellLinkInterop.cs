using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DSOptimizer.Windows.Native;

/// <summary>
/// COM <c>ShellLink</c> (shell32) do odczytu skrótów <c>.lnk</c> w procesie programu: <see cref="IShellLinkW"/>, <c>IPersistFile</c>
/// (<see cref="System.Runtime.InteropServices.ComTypes.IPersistFile"/>) i <see cref="IShellLinkDataList"/>. Klasa <c>ThreadingModel=Both</c>,
/// więc działa też w wątku MTA (odczyt strony w tle). Obiekt zwalnia wołający (<see cref="Marshal.FinalReleaseComObject"/>).
/// </summary>
internal static class ShellLinkInterop
{
    /// <summary><c>SLDF_HAS_DARWINID</c>: skrót MSI „advertised” (cel z identyfikatora Instalatora Windows, nie ze ścieżki).</summary>
    internal const uint SldfHasDarwinId = 0x1000;

    /// <summary><c>SLGP_RAWPATH</c>: ścieżka celu tak, jak zapisana (zmienne środowiskowe nierozwinięte), bez rozwiązywania skrótu.</summary>
    internal const uint SlgpRawPath = 0x4;

    /// <summary><c>STGM_READ</c>.</summary>
    internal const int StgmRead = 0;

    /// <summary><c>STGM_SHARE_DENY_NONE</c>: inni mogą plik czytać, zapisywać i usuwać, gdy jest otwarty do odczytu.</summary>
    internal const int StgmShareDenyNone = 0x40;

    internal static IShellLinkW Create() => (IShellLinkW)new ShellLink();

    /// <summary>CoClass <c>ShellLink</c> (<c>CLSID_ShellLink</c>).</summary>
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    private class ShellLink
    {
    }
}

/// <summary><c>IShellLinkW</c> – pełna tablica metod w kolejności z <c>ShObjIdl_core.h</c>.</summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxChars, IntPtr findData, uint flags);

    void GetIDList(out IntPtr idList);

    void SetIDList(IntPtr idList);

    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxChars);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxChars);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxChars);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

    void GetHotkey(out short hotkey);

    void SetHotkey(short hotkey);

    void GetShowCmd(out int showCmd);

    void SetShowCmd(int showCmd);

    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxChars, out int iconIndex);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, int reserved);

    void Resolve(IntPtr window, uint flags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

/// <summary><c>IShellLinkDataList</c> – flagi skrótu (<c>SHELL_LINK_DATA_FLAGS</c>).</summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("45E2B4AE-B1C3-11D0-B92F-00A0C90312E1")]
internal interface IShellLinkDataList
{
    void AddDataBlock(IntPtr dataBlock);

    void CopyDataBlock(uint signature, out IntPtr dataBlock);

    void RemoveDataBlock(uint signature);

    void GetFlags(out uint flags);

    void SetFlags(uint flags);
}
