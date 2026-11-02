namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Wynik sprawdzenia pliku. <see cref="Missing"/> tylko wtedy, gdy system odpowiedział „nie istnieje”; błąd dostępu, dysk sieciowy
/// albo wymienny = <see cref="Unknown"/>.
/// </summary>
public enum FileProbe
{
    Exists,
    Missing,
    Unknown,
}
