using System;
using System.Collections.Generic;
using System.Globalization;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Localization;
using DSOptimizer.Windows;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Tekst o komputerze na pasku marki, np. „DESKTOP-7Q2K, Windows 11 Pro 26300.9457, użytkownik Ola”.
/// Dane z <see cref="ISystemInfo"/> (<see cref="Format"/>, od razu przy starcie); użytkownik dopisywany później
/// (<see cref="WithUser"/>), bo <see cref="ResolveUserName"/> pyta LSA/kontroler domeny i musi iść poza wątkiem UI.
/// Nazwa tylko wtedy, gdy sesja jest pewna (<see cref="IUserContext.IsCertain"/>) i SID da się przetłumaczyć na nazwę konta –
/// inaczej ta część jest pomijana (bez zgadywania).
/// </summary>
public static class MachineSummary
{
    private const int FirstWindows11Build = 22000;

    public static string Format(ISystemInfo system)
    {
        if (system is null)
        {
            throw new ArgumentNullException(nameof(system));
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(system.MachineName))
        {
            parts.Add(system.MachineName);
        }

        var windows = system.Build >= FirstWindows11Build ? "Windows 11" : "Windows";
        var edition = WindowsEditions.DisplayName(system.Edition);
        if (edition is not null)
        {
            windows += " " + edition;
        }

        if (system.Build > 0)
        {
            windows += string.Format(CultureInfo.InvariantCulture, " {0}.{1}", system.Build, system.Revision);
        }

        parts.Add(windows);
        return string.Join(", ", parts);
    }

    /// <summary>Tekst z <see cref="Format"/> uzupełniony o „użytkownik X”; bez nazwy zwraca tekst bez zmian.</summary>
    public static string WithUser(string machineText, string? userName)
        => string.IsNullOrWhiteSpace(userName) ? machineText : L.F("{0}, użytkownik {1}", machineText, userName);

    /// <summary>
    /// Nazwa konta zalogowanego użytkownika albo null (sesja niepewna, SID nieprzetłumaczalny, błąd LSA/domeny).
    /// Może blokować (kontroler domeny poza zasięgiem), więc wołać tylko poza wątkiem UI.
    /// </summary>
    public static string? ResolveUserName(IUserContext user)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        return user.IsCertain ? AccountName(user.Sid) : null;
    }

    private static string? AccountName(string sid) => new WindowsAccountNames().NameOf(sid);
}
