using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Autostart;

/// <summary>Teksty modułu Autostart (w bieżącym języku, <see cref="L"/>).</summary>
public static class AutostartText
{
    /// <summary>Nazwa wiersza dla wartości Run o pustej nazwie.</summary>
    public static string NoName => L.T("(bez nazwy)");

    public static string NoMeasurement => L.T("Brak pomiaru Windows – w kolumnie pomiaru będzie „nie zmierzono”.");

    public static string EnvironmentUnreadable(string key, string error) => L.F("Nie udało się odczytać zmiennych konta z {0}: {1}", key, error);

    public static string SourceUnreadable(string where, string error) => L.F("Nie udało się odczytać {0}: {1}", where, error);

    public static string ValueSkipped(string key, string name) => L.F("Pominięto wartość {1} w {0}: nieobsługiwany typ.", key, name);

    public static string MeasurementFileSkipped(string file) => L.F("Pominięto plik pomiaru Windows {0}: błędny format.", file);

    public static string StoreUnreadable(string error) => L.F("Nie udało się odczytać aplikacji ze Sklepu: {0}", error);
}
