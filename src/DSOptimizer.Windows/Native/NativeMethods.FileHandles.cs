using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Windows.Native;

/// <summary>
/// kernel32: usuwanie przez uchwyt (<c>OldFilesCleaner</c>) – otwarcie bez podążania za linkiem w ostatnim członie, odczyt typu
/// i ostatecznej ścieżki z uchwytu, usunięcie dyspozycją na tym samym uchwycie (bez okna na podmianę ścieżki).
/// </summary>
internal static partial class NativeMethods
{
    internal const uint Delete = 0x00010000;
    internal const uint FileReadAttributes = 0x0080;
    internal const uint FileWriteAttributes = 0x0100;
    internal const uint FileShareNone = 0;
    internal const uint FileShareAll = 0x00000007;
    internal const uint OpenExistingDisposition = 3;
    internal const uint FileFlagOpenReparsePoint = 0x00200000;
    internal const uint FileFlagBackupSemantics = 0x02000000;
    internal const int FileBasicInfoClass = 0;
    internal const int FileDispositionInfoClass = 4;
    internal const int FileAttributeTagInfoClass = 9;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", ExactSpelling = true)]
    internal static extern SafeFileHandle CreateFileHandle(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        System.IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        System.IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileAttributeTagInfo information, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref FileBasicInfo information, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref FileDispositionInfo information, uint size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW", ExactSpelling = true)]
    internal static extern uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] path, uint size, uint flags);

    /// <summary>Rozwija człony 8.3 („DAWID~1”) do długich nazw; linków nie rozwiązuje.</summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetLongPathNameW", ExactSpelling = true)]
    internal static extern uint GetLongPathName(string shortPath, [Out] char[] longPath, uint bufferLength);

    /// <summary><c>FILE_ATTRIBUTE_TAG_INFO</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    /// <summary><c>FILE_BASIC_INFO</c>: czasy 0 = bez zmiany.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileBasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    /// <summary><c>FILE_DISPOSITION_INFO</c> (<c>BOOLEAN DeleteFile</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileDispositionInfo
    {
        public byte DeleteFile;
    }
}
