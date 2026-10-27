using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Threading;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Nieszkodliwy, ukryty proces testowy (powershell.exe ze Start-Sleep) rozpoznawany po unikalnym znaczniku w linii poleceń.
/// Dispose zatrzymuje wyłącznie procesy powershell.exe, których CommandLine zawiera ten znacznik (i zgodny czas startu) – nigdy cudzych.
/// </summary>
public sealed class MarkedTestProcess : IDisposable
{
    private const double StartTimeToleranceMilliseconds = 50;

    public MarkedTestProcess()
    {
        Marker = "dso-test-" + Guid.NewGuid().ToString("N");
    }

    public string Marker { get; }

    /// <summary>Argumenty powershell.exe: ukryte okno, 60 s snu, znacznik w komentarzu.</summary>
    public string Arguments => $"-NoProfile -WindowStyle Hidden -Command \"Start-Sleep -Seconds 60 # {Marker}\"";

    public IReadOnlyList<int> FindProcessIds()
    {
        var ids = new List<int>();
        foreach (var found in FindProcesses())
        {
            ids.Add(found.Id);
        }

        return ids;
    }

    public bool WaitUntilRunning(TimeSpan timeout) => WaitFor(() => FindProcessIds().Count > 0, timeout);

    public bool WaitUntilGone(TimeSpan timeout) => WaitFor(() => FindProcessIds().Count == 0, timeout);

    /// <summary>
    /// Zatrzymuje tylko proces, którego uchwyt należy do tego samego procesu, który WMI pokazało ze znacznikiem: uchwyt jest
    /// otwierany przed porównaniem czasu startu (WMI CreationDate vs Process.StartTime), więc PID nie może zostać ponownie
    /// użyty między sprawdzeniem a Kill. Przy niezgodności proces zostaje.
    /// </summary>
    public void Dispose()
    {
        foreach (var found in FindProcesses())
        {
            try
            {
                using var process = Process.GetProcessById(found.Id);
                _ = process.Handle;
                if (Math.Abs((process.StartTime.ToUniversalTime() - found.CreatedUtc).TotalMilliseconds) > StartTimeToleranceMilliseconds)
                {
                    continue;
                }

                process.Kill();
                process.WaitForExit(5000);
            }
            catch (ArgumentException)
            {
                // Proces zakończył się między odczytem a zatrzymaniem.
            }
            catch (InvalidOperationException)
            {
                // j.w.
            }
            catch (Win32Exception)
            {
                // Proces właśnie się kończy (odmowa dostępu przy zamykaniu).
            }
        }
    }

    private List<(int Id, DateTime CreatedUtc)> FindProcesses()
    {
        var found = new List<(int Id, DateTime CreatedUtc)>();
        using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CreationDate, CommandLine FROM Win32_Process WHERE Name = 'powershell.exe'");
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                if (item["CommandLine"] is string commandLine
                    && commandLine.IndexOf(Marker, StringComparison.Ordinal) >= 0
                    && item["CreationDate"] is string created)
                {
                    found.Add((
                        Convert.ToInt32(item["ProcessId"], CultureInfo.InvariantCulture),
                        ManagementDateTimeConverter.ToDateTime(created).ToUniversalTime()));
                }
            }
        }

        return found;
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (condition())
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(200);
        }
    }
}
