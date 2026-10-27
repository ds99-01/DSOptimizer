using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>Procesy <c>ping.exe</c> uruchomione przez proces testów (test limitu czasu runnera bez zabijania).</summary>
internal static class ChildPings
{
    /// <summary>PID-y procesów <c>ping.exe</c>, których rodzicem jest proces testów (ParentProcessId przez CIM).</summary>
    public static HashSet<int> Current()
    {
        var self = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
        using var searcher = new ManagementObjectSearcher($"SELECT ProcessId FROM Win32_Process WHERE Name = 'PING.EXE' AND ParentProcessId = {self}");
        using var found = searcher.Get();
        var ids = new HashSet<int>();
        foreach (var item in found)
        {
            using (item)
            {
                ids.Add(Convert.ToInt32(item["ProcessId"], CultureInfo.InvariantCulture));
            }
        }

        return ids;
    }

    public static bool IsAlive(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Zamyka proces testu (tylko gdy to nadal <c>ping</c>).</summary>
    public static void Kill(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            if (string.Equals(process.ProcessName, "ping", StringComparison.OrdinalIgnoreCase))
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch (ArgumentException)
        {
            // Już się zakończył.
        }
        catch (InvalidOperationException)
        {
            // j.w.
        }
    }
}
