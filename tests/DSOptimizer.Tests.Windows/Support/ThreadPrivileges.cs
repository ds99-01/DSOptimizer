using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Wyłącza przywileje tylko w tokenie bieżącego wątku testu (M10 final review): <c>ImpersonateSelf(SecurityImpersonation)</c> daje
/// wątkowi kopię tokenu procesu, <c>AdjustTokenPrivileges</c> wyłącza w niej przywileje, a <see cref="Dispose"/> woła
/// <c>RevertToSelf</c> – token procesu i inne wątki zostają bez zmian. Powód: Git Bash (MSYS) uruchamia procesy z włączonymi
/// <c>SeBackupPrivilege</c>/<c>SeRestorePrivilege</c>, które omijają odmowę z ACL, więc test odmowy dostępu zależał od powłoki.
/// Bez uruchamiania procesów i bez zmian w systemie. Wołać i zwalniać na tym samym wątku (test synchroniczny).
/// </summary>
public sealed class ThreadPrivileges : IDisposable
{
    public const string Backup = "SeBackupPrivilege";
    public const string Restore = "SeRestorePrivilege";

    private const int SecurityImpersonation = 2;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const int ErrorNotAllAssigned = 1300;

    private bool _reverted;

    private ThreadPrivileges()
    {
    }

    /// <summary>Wyłącza podane przywileje w tokenie wątku (przywilej nieobecny w tokenie = nic do zrobienia).</summary>
    public static ThreadPrivileges Disable(params string[] privileges)
    {
        if (privileges is null)
        {
            throw new ArgumentNullException(nameof(privileges));
        }

        if (!ImpersonateSelf(SecurityImpersonation))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ImpersonateSelf nie powiodło się.");
        }

        var scope = new ThreadPrivileges();
        try
        {
            if (!OpenThreadToken(GetCurrentThread(), TokenAdjustPrivileges | TokenQuery, true, out var token))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenThreadToken nie powiodło się.");
            }

            using (token)
            {
                foreach (var name in privileges)
                {
                    if (!LookupPrivilegeValueW(null, name, out var luid))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupPrivilegeValue nie powiodło się: " + name);
                    }

                    // Atrybuty 0 = przywilej wyłączony (w kopii tokenu wątku, którą RevertToSelf porzuca).
                    var state = new TokenPrivilege { PrivilegeCount = 1, Luid = luid, Attributes = 0 };
                    var ok = AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
                    var error = Marshal.GetLastWin32Error();
                    if (!ok || (error != 0 && error != ErrorNotAllAssigned))
                    {
                        throw new Win32Exception(error, "AdjustTokenPrivileges nie powiodło się: " + name);
                    }
                }
            }

            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    /// <summary>Czy przywilej jest włączony w tokenie, którego wątek używa teraz (token wątku albo procesu).</summary>
    public static bool IsEnabled(string privilege)
    {
        if (!LookupPrivilegeValueW(null, privilege, out var luid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupPrivilegeValue nie powiodło się: " + privilege);
        }

        var set = new PrivilegeSet { PrivilegeCount = 1, Control = 0, Luid = luid, Attributes = 0 };
        if (!OpenThreadToken(GetCurrentThread(), TokenQuery, true, out var token))
        {
            token.Dispose();
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken nie powiodło się.");
            }
        }

        using (token)
        {
            if (!PrivilegeCheck(token, ref set, out var enabled))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "PrivilegeCheck nie powiodło się.");
            }

            return enabled;
        }
    }

    public void Dispose()
    {
        if (_reverted)
        {
            return;
        }

        _reverted = true;
        if (!RevertToSelf())
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RevertToSelf nie powiodło się.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary><c>TOKEN_PRIVILEGES</c> z jednym elementem.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivilege
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    /// <summary><c>PRIVILEGE_SET</c> z jednym elementem.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PrivilegeSet
    {
        public uint PrivilegeCount;
        public uint Control;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImpersonateSelf(int impersonationLevel);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RevertToSelf();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenThreadToken(
        IntPtr thread, uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool openAsSelf, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        SafeAccessTokenHandle token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivilege newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrivilegeCheck(
        SafeAccessTokenHandle token, ref PrivilegeSet requiredPrivileges, [MarshalAs(UnmanagedType.Bool)] out bool result);
}
