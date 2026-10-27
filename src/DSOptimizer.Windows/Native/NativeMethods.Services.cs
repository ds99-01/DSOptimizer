using System;
using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

internal static partial class NativeMethods
{
    internal const uint ScManagerConnect = 0x0001;
    internal const uint ServiceQueryConfig = 0x0001;
    internal const uint ServiceChangeConfig = 0x0002;
    internal const uint ServiceNoChange = 0xFFFFFFFF;
    internal const uint ServiceConfigDelayedAutoStartInfo = 3;
    internal const uint ServiceStop = 0x0020;
    internal const uint ServiceQueryStatus = 0x0004;
    internal const uint ServiceControlStop = 0x00000001;
    internal const int ErrorAccessDenied = 5;
    internal const int ErrorDependentServicesRunning = 1051;
    internal const int ErrorServiceNotActive = 1062;
    internal const int ErrorServiceDoesNotExist = 1060;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW")]
    internal static extern SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")]
    internal static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfigW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig(
        SafeServiceHandle service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfig2W")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig2(SafeServiceHandle service, uint infoLevel, ref ServiceDelayedAutoStartInfo info);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "ControlService")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ControlService(SafeServiceHandle service, uint control, ref ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern uint WTSGetActiveConsoleSessionId();

    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceDelayedAutoStartInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool DelayedAutostart;
    }
}
