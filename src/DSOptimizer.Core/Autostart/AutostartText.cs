using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Autostart;

/// <summary>Teksty modułu Autostart (w bieżącym języku, <see cref="L"/>).</summary>
public static class AutostartText
{
    public static string EnvironmentUnreadable(string key, string error) => L.F("Nie udało się odczytać zmiennych konta z {0}: {1}", key, error);
}
