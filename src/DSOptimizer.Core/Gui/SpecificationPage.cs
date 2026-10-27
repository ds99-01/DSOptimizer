using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Core.Gui;

/// <summary>
/// Stan strony „Specyfikacja”: bieżący snapshot i numer odczytu. Odświeżenie w trakcie poprzedniego odczytu zaczyna nowy
/// numer, a aktualizacje i wynik starszego odczytu są odrzucane. Używany tylko z wątku UI (bez blokad).
/// </summary>
public sealed class SpecificationPage
{
    private int _load;

    public HardwareSnapshot Snapshot { get; private set; } = new();

    /// <summary>Wszystkie części odczytane (udane albo nie) – dopiero wtedy raport można zapisać i skopiować.</summary>
    public bool IsComplete => Snapshot.IsComplete;

    /// <summary>Nowy odczyt: wszystkie pola wracają do „Wczytywanie…”. Zwraca numer, który trzeba podać w <see cref="Accept"/>.</summary>
    public int BeginLoad()
    {
        _load++;
        Snapshot = new HardwareSnapshot();
        return _load;
    }

    /// <summary>
    /// Przyjmuje snapshot odczytu <paramref name="load"/>. Odrzuca snapshot starszego odczytu oraz spóźnioną aktualizację
    /// po komplecie (snapshot kompletny jest ostateczny). Zwraca, czy snapshot podmieniono.
    /// </summary>
    public bool Accept(int load, HardwareSnapshot snapshot)
    {
        if (load != _load || Snapshot.IsComplete)
        {
            return false;
        }

        Snapshot = snapshot;
        return true;
    }
}
