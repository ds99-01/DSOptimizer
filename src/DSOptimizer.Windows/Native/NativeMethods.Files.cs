using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

/// <summary>kernel32: sprawdzenie pliku autostartu (<c>AutostartFiles.Probe</c>) – typ dysku i atrybuty bez otwierania pliku.</summary>
internal static partial class NativeMethods
{
    /// <summary><c>GetFileExInfoStandard</c>.</summary>
    internal const int FileExInfoStandard = 0;

    /// <summary><c>DRIVE_FIXED</c>.</summary>
    internal const uint DriveFixed = 3;

    /// <summary><c>FILE_ATTRIBUTE_DIRECTORY</c>.</summary>
    internal const uint FileAttributeDirectory = 0x00000010;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFileAttributesExW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileAttributesEx(string fileName, int infoLevelId, out Win32FileAttributeData fileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDriveTypeW", ExactSpelling = true)]
    internal static extern uint GetDriveType(string rootPathName);

    /// <summary><c>WIN32_FILE_ATTRIBUTE_DATA</c> (<c>FILETIME</c> jako dwie połówki – wyrównanie 4 B).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32FileAttributeData
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint FileSizeHigh;
        public uint FileSizeLow;
    }
}
