using System;
using System.Collections.Concurrent;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Nazwy kont z pamięcią na całą sesję programu: każdy SID tłumaczony najwyżej raz (zapamiętany jest też brak nazwy, null).
/// Tłumaczenie przez LSA potrafi blokować sekundami (kontroler domeny poza zasięgiem), a historia czyta journal przy każdym wejściu
/// na stronę, F5 i po każdej operacji – bez tej pamięci każde odświeżenie znów by czekało. Bezpieczne z wielu wątków
/// (odczyt historii idzie w <c>Task.Run</c>); dwa równoległe pierwsze pytania o ten sam SID mogą zapytać LSA dwa razy, wynik ten sam.
/// </summary>
public sealed class CachedAccountNames : IAccountNames
{
    private readonly IAccountNames _inner;
    private readonly ConcurrentDictionary<string, string?> _names = new(StringComparer.OrdinalIgnoreCase);

    public CachedAccountNames(IAccountNames inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string? NameOf(string sid) => _names.GetOrAdd(sid, _inner.NameOf);
}
