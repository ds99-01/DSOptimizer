using System;

namespace DSOptimizer.Core.Apps;

/// <summary>Limity czasu usuwania aplikacji i okno weryfikacji (deinstalator może skończyć się od razu i dokańczać w tle).</summary>
public static class AppRemovalTimeouts
{
    public static readonly TimeSpan Store = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan Msi = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan Interactive = TimeSpan.FromMinutes(30);

    /// <summary>Jak długo po próbie czekamy na zniknięcie pakietu albo wpisu Uninstall.</summary>
    public static readonly TimeSpan VerifyWindow = TimeSpan.FromSeconds(60);

    /// <summary>Odstęp między odczytami obecności w oknie weryfikacji.</summary>
    public static readonly TimeSpan VerifyPoll = TimeSpan.FromSeconds(1);

    /// <summary>Limit czekania na deinstalator według trybu planu.</summary>
    public static TimeSpan For(UninstallMode mode) => mode switch
    {
        UninstallMode.Quiet => Quiet,
        UninstallMode.Msi => Msi,
        UninstallMode.Interactive => Interactive,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
