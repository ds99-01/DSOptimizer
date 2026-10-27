using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows;

/// <summary>
/// Start procesu przez <c>CreateProcessW</c> z <c>bInheritHandles=FALSE</c>. <c>Process.Start</c> z <c>UseShellExecute=false</c>
/// dziedziczy uchwyty (w tym przekierowany stdout), więc CLI przechwytujące wyjście czekałoby na zamknięcie okna panelu (R32).
/// Bez czekania na proces: oba uchwyty z <c>PROCESS_INFORMATION</c> są zamykane od razu.
/// </summary>
internal static class NativeProcessStarter
{
    /// <summary>Uruchamia <paramref name="fileName"/> (pełna ścieżka) z <paramref name="arguments"/>; porażka = <see cref="Win32Exception"/>.</summary>
    public static void Start(string fileName, string arguments, string workingDirectory)
    {
        if (fileName is null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        var commandLine = (CommandLine(fileName, arguments) + "\0").ToCharArray();
        var startupInfo = new NativeMethods.StartupInfo { Size = (uint)Marshal.SizeOf<NativeMethods.StartupInfo>() };
        if (!NativeMethods.CreateProcess(
                fileName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: false,
                creationFlags: 0,
                IntPtr.Zero,
                workingDirectory,
                ref startupInfo,
                out var processInformation))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        NativeMethods.CloseHandle(processInformation.Thread);
        NativeMethods.CloseHandle(processInformation.Process);
    }

    /// <summary>Linia poleceń: plik w cudzysłowie, a po spacji argumenty, gdy są.</summary>
    internal static string CommandLine(string fileName, string arguments)
    {
        var quoted = "\"" + fileName + "\"";
        return string.IsNullOrEmpty(arguments) ? quoted : quoted + " " + arguments;
    }
}
