namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Wynik sprawdzenia pliku. <see cref="Exists"/> tylko dla istniejącego pliku – katalog o tej ścieżce to nie <see cref="Exists"/>.
/// <see cref="Missing"/> tylko wtedy, gdy system odpowiedział „nie istnieje”; błąd dostępu, dysk sieciowy albo wymienny = <see cref="Unknown"/>.
/// </summary>
public enum FileProbe
{
    Exists,
    Missing,
    Unknown,
}
