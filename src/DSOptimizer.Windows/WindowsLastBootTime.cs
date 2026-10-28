using System;
using System.Linq;
using System.Management;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;
using DSOptimizer.Windows.Hardware;

namespace DSOptimizer.Windows;

/// <summary>
/// Czas ostatniego startu systemu: <c>SELECT LastBootUpTime FROM Win32_OperatingSystem</c> (tylko odczyt CIM).
/// LastBootUpTime ma offset lokalny, więc konwersja CIM jest poprawna; wynik w UTC. Każdy błąd = null i ostrzeżenie w logu.
/// </summary>
public sealed class WindowsLastBootTime : ILastBootTime
{
    private readonly ILog _log;

    public WindowsLastBootTime(ILog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public DateTimeOffset? Read()
    {
        try
        {
            var raw = CimReader.Query(
                    CimReader.Cimv2,
                    "SELECT LastBootUpTime FROM Win32_OperatingSystem",
                    static os => CimReader.RawDateTime(os, "LastBootUpTime"))
                .FirstOrDefault(value => !string.IsNullOrEmpty(value));
            if (raw is null)
            {
                return null;
            }

            var local = ManagementDateTimeConverter.ToDateTime(raw);
            return new DateTimeOffset(local).ToUniversalTime();
        }
        catch (Exception ex)
        {
            _log.Warn("Restart", L.F("Nie udało się odczytać czasu ostatniego startu systemu: {0}", ex.Message));
            return null;
        }
    }
}
