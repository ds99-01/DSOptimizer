using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Core.Install;

/// <summary>Status „Zainstalowana” pozycji listy instalacji.</summary>
public static class InstallStatus
{
    /// <summary>
    /// Czy reguła <see cref="InstallEntry.Match"/> pasuje do którejś aplikacji. <paramref name="raw"/> to <b>surowy</b> odczyt
    /// (<c>InstalledAppsReading.Apps</c>, przed <c>AppClassifier</c>), więc liczą się też wpisy <c>SystemComponent</c>
    /// i z <c>ParentKeyName</c> (środowiska uruchomieniowe).
    /// </summary>
    public static bool IsInstalled(InstallEntry entry, IReadOnlyList<InstalledApp> raw)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (raw is null)
        {
            throw new ArgumentNullException(nameof(raw));
        }

        return raw.Any(entry.Match.Matches);
    }
}
