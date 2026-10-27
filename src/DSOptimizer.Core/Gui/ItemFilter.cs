using System;
using System.Collections.Generic;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Widoczność pozycji listy: zapytanie wyszukiwarki i filtr ryzyka.</summary>
public static class ItemFilter
{
    /// <summary>Każde słowo zapytania (po <see cref="SearchText.Fold"/>) musi wystąpić w tytule, skrócie lub opisie. Puste zapytanie = wszystko.</summary>
    public static bool MatchesQuery(TweakDefinition item, string? query)
    {
        var words = SearchText.Fold(query);
        if (words.Length == 0)
        {
            return true;
        }

        var haystack = SearchText.Fold(item.Title + " " + item.Summary + " " + item.Description);
        foreach (var word in words.Split(' '))
        {
            if (haystack.IndexOf(word, StringComparison.Ordinal) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Tweak zastosowany lub częściowy jest pokazany zawsze, niezależnie od filtra.</summary>
    public static bool PassesRisk(TweakDefinition item, DetectionState? state, RiskFilter filter)
    {
        if (item.Kind == TweakKind.Tweak && state is DetectionState.Applied or DetectionState.Partial)
        {
            return true;
        }

        return filter switch
        {
            RiskFilter.All => true,
            RiskFilter.NoExperimental => item.Risk != RiskLevel.Experimental,
            RiskFilter.SafeOnly => item.Risk == RiskLevel.Safe,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
        };
    }

    public static bool IsVisible(TweakDefinition item, DetectionState? state, string? query, RiskFilter filter)
        => MatchesQuery(item, query) && PassesRisk(item, state, filter);

    /// <summary>Pozycje pasujące do zapytania, ale odrzucone przez filtr ryzyka.</summary>
    public static int CountHidden(IEnumerable<KeyValuePair<TweakDefinition, DetectionState?>> items, string? query, RiskFilter filter)
    {
        var count = 0;
        foreach (var pair in items)
        {
            if (MatchesQuery(pair.Key, query) && !PassesRisk(pair.Key, pair.Value, filter))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Pozycje pasujące do zapytania, które filtr ryzyka by ukrył, ale są pokazane tylko dzięki wyjątkowi „zastosowane”.</summary>
    public static int CountShownDespiteFilter(IEnumerable<KeyValuePair<TweakDefinition, DetectionState?>> items, string? query, RiskFilter filter)
    {
        var count = 0;
        foreach (var pair in items)
        {
            if (MatchesQuery(pair.Key, query)
                && PassesRisk(pair.Key, pair.Value, filter)
                && !PassesRisk(pair.Key, null, filter))
            {
                count++;
            }
        }

        return count;
    }
}
