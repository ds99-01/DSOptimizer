using DSOptimizer.Core.Prep;

namespace DSOptimizer.Core.Gui;

/// <summary>
/// Stan strony „Przygotowanie”: bieżący snapshot i numer odczytu (wzorzec <see cref="SpecificationPage"/>). Odświeżenie w trakcie
/// poprzedniego odczytu zaczyna nowy numer, a aktualizacje i wynik starszego odczytu są odrzucane. Używany tylko z wątku UI (bez blokad).
/// </summary>
public sealed class PrepPage
{
    private int _load;

    public PrepSnapshot Snapshot { get; private set; } = new();

    /// <summary>Nowy odczyt: wszystkie części wracają do „Wczytywanie…”. Zwraca numer, który trzeba podać w <see cref="Accept"/>.</summary>
    public int BeginLoad()
    {
        _load++;
        Snapshot = new PrepSnapshot();
        return _load;
    }

    /// <summary>
    /// Przyjmuje snapshot odczytu <paramref name="load"/>. Odrzuca snapshot starszego odczytu oraz spóźnioną aktualizację
    /// po komplecie (snapshot kompletny jest ostateczny). Zwraca, czy snapshot podmieniono.
    /// </summary>
    public bool Accept(int load, PrepSnapshot snapshot)
    {
        if (load != _load || Snapshot.IsComplete)
        {
            return false;
        }

        Snapshot = snapshot;
        return true;
    }
}
