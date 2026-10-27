using System;
using System.Collections.Generic;
using System.Linq;

namespace DSOptimizer.Core.Apps;

/// <summary>
/// Sugestie strony „Aplikacje” na czas uruchomienia (Ruling R16): każdy wiersz <see cref="AppGroup.Suggested"/>, który można usunąć
/// (<see cref="AppRow.CanRemove"/>), zaznacza się automatycznie raz na Id aplikacji. Ręczne odznaczenie, F5 i ponowne wejście nie
/// zaznaczają tego samego Id; częściowy odczyt (np. bez aplikacji ze Sklepu) nie blokuje sugestii z późniejszego odczytu.
/// </summary>
public sealed class AppSuggestionTracker
{
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    /// <summary>Id do zaznaczenia po tym odczycie (jeszcze nie zaznaczane automatycznie); zapamiętuje je.</summary>
    public IReadOnlyList<string> TakeNew(IReadOnlyList<AppRow> rows)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        return rows
            .Where(row => row.Group == AppGroup.Suggested && row.CanRemove && _taken.Add(row.App.Id))
            .Select(row => row.App.Id)
            .ToList();
    }
}
