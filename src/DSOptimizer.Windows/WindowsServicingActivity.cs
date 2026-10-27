using System;
using System.Collections.Generic;
using System.Diagnostics;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Windows;

/// <summary>
/// Trwająca operacja serwisowania po nazwach procesów (<see cref="Process.GetProcessesByName(string)"/> czyta listę procesów
/// systemu, bez otwierania procesów, więc nie potrzebuje do nich uprawnień). Lista z pomiaru VM (Task 2 partii 2 Etapu 4b):
/// <c>dism</c>, <c>sfc</c>, <c>cleanmgr</c>; <c>TiWorker</c>, <c>TrustedInstaller</c> i <c>DismHost</c> działają też na
/// bezczynnym systemie albo przy Windows Update, więc dawałyby fałszywe alarmy.
/// </summary>
public sealed class WindowsServicingActivity : IServicingActivity
{
    private readonly IReadOnlyList<string> _names;

    public WindowsServicingActivity()
        : this(ProcessNames)
    {
    }

    /// <summary>Lista nazw do testów (bez uruchamiania prawdziwych narzędzi serwisowania).</summary>
    internal WindowsServicingActivity(IReadOnlyList<string> names) => _names = names ?? throw new ArgumentNullException(nameof(names));

    /// <summary>Nazwy procesów (bez <c>.exe</c>) w kolejności sprawdzania.</summary>
    public static IReadOnlyList<string> ProcessNames { get; } = new[] { "dism", "sfc", "cleanmgr" };

    public string? ActiveOperation()
    {
        foreach (var name in _names)
        {
            var found = Process.GetProcessesByName(name);
            foreach (var process in found)
            {
                process.Dispose();
            }

            if (found.Length > 0)
            {
                return name + ".exe";
            }
        }

        return null;
    }
}
