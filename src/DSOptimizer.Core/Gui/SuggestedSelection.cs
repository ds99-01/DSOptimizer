using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Pozycja kategorii widziana przez <see cref="SuggestedSelection"/>: czy jest sugerowana i jaki ma znany stan (null = jeszcze nie sprawdzony).</summary>
public sealed record SuggestionCandidate(string Id, bool Suggested, DetectionState? KnownState);

/// <summary>
/// Pamięć „sugestie tej kategorii już zaznaczone” na czas życia okna (bez WPF). Sugerowane pozycje niezastosowane
/// (<see cref="DetectionState.NotApplied"/> albo <see cref="DetectionState.Partial"/>) zaznaczają się dokładnie raz,
/// dopiero gdy wszystkie sugerowane pozycje kategorii mają znany stan.
/// </summary>
public sealed class SuggestedSelection
{
    private readonly HashSet<string> _consumed = new();

    public IReadOnlyList<string> TakeFor(string categoryId, IReadOnlyList<SuggestionCandidate> items)
    {
        if (_consumed.Contains(categoryId))
        {
            return new List<string>();
        }

        var suggested = items.Where(item => item.Suggested).ToList();
        if (suggested.Any(item => item.KnownState is null))
        {
            return new List<string>();
        }

        _consumed.Add(categoryId);
        return suggested
            .Where(item => item.KnownState is DetectionState.NotApplied or DetectionState.Partial)
            .Select(item => item.Id)
            .ToList();
    }

    public bool IsConsumed(string categoryId) => _consumed.Contains(categoryId);
}
