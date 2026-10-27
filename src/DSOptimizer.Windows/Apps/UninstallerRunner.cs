using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Windows.Apps;

/// <summary>
/// Start deinstalatora z planu (<see cref="UninstallPlan"/>) przez <c>CreateProcessW</c> z <c>bInheritHandles=FALSE</c>, pełna ścieżka
/// w cudzysłowie, katalog roboczy = katalog pliku (nigdy ShellExecute, <c>PATH</c> ani katalog bieżący). Proces NIE trafia do Job Objectu
/// i nigdy nie jest zabijany: po limicie czasu albo anulowaniu program tylko przestaje czekać (<c>TimedOut=true</c>), a deinstalator
/// działa dalej (przeżywa też zamknięcie DSOptimizera).
/// </summary>
public static class UninstallerRunner
{
    /// <summary>Tylko <c>X:\…</c> (jak parser <c>UninstallCommand</c>): odpada UNC, ścieżka względna, <c>\x.exe</c> i <c>C:x.exe</c>.</summary>
    private static readonly Regex DrivePath = new(@"^[A-Za-z]:\\", RegexOptions.CultureInvariant);

    /// <summary>Uruchamia i czeka najwyżej <paramref name="timeout"/>. Plik musi istnieć pod pełną lokalną ścieżką (inaczej wyjątek bez startu).</summary>
    public static UninstallerRun Run(UninstallPlan plan, TimeSpan timeout, CancellationToken ct)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (!DrivePath.IsMatch(plan.FileName))
        {
            throw new ArgumentException("Deinstalator musi mieć pełną lokalną ścieżkę: " + plan.FileName, nameof(plan));
        }

        if (!File.Exists(plan.FileName))
        {
            throw new FileNotFoundException("Nie ma pliku deinstalatora.", plan.FileName);
        }

        var commandLine = (NativeProcessStarter.CommandLine(plan.FileName, plan.Arguments) + "\0").ToCharArray();
        var startupInfo = new NativeMethods.StartupInfo { Size = (uint)Marshal.SizeOf<NativeMethods.StartupInfo>() };
        if (!NativeMethods.CreateProcess(
                plan.FileName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: false,
                creationFlags: 0,
                IntPtr.Zero,
                Path.GetDirectoryName(plan.FileName),
                ref startupInfo,
                out var processInformation))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        NativeMethods.CloseHandle(processInformation.Thread);
        using var process = new ProcessWaitHandle(processInformation.Process);
        if (WaitHandle.WaitAny(new WaitHandle[] { process, ct.WaitHandle }, timeout) != 0)
        {
            return new UninstallerRun(null, TimedOut: true);
        }

        return NativeMethods.GetExitCodeProcess(processInformation.Process, out var code) && code != NativeMethods.StillActive
            ? new UninstallerRun(unchecked((int)code), TimedOut: false)
            : new UninstallerRun(null, TimedOut: false);
    }

    /// <summary>Uchwyt procesu jako <see cref="WaitHandle"/>; Dispose zamyka tylko uchwyt, proces działa dalej.</summary>
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(IntPtr handle)
        {
            SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
        }
    }
}
