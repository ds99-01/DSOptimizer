using System;
using System.Collections.Generic;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Widoczność pozycji listy: zapytanie wyszukiwarki, filtr ryzyka i filtr „Pokaż” (stan). Pozycja musi przejść wszystkie.</summary>
public static class ItemFilter
{
    /// <summary>Każde słowo zapytania (po <see cref="SearchText.Fold"/>) musi wystąpić w tytule, skrócie, opisie lub nazwie grupy. Puste zapytanie = wszystko.</summary>
    public static bool MatchesQuery(TweakDefinition item, string? query)
    {
        var words = SearchText.Fold(query);
        if (words.Length == 0)
        {
            return true;
        }

        var haystack = SearchText.Fold(item.Title + " " + item.Summary + " " + item.Description + " " + GuiText.GroupName(item.Category, item.Group));
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

    /// <summary>
    /// Filtr „Pokaż”: „Zastosowane” = tweak w stanie <see cref="DetectionState.Applied"/>; „Niezastosowane” = tweak obsługiwany i nie zastosowany
    /// (także częściowy, błąd odczytu i stan jeszcze nieodczytany – to pozycje do zrobienia). Akcje, panele i <see cref="DetectionState.NotSupported"/>
    /// przechodzą tylko „Wszystkie” i „Zaznaczone” (ten drugi to widok zaznaczonych – zbiór pozycji wybiera model okna).
    /// </summary>
    public static bool PassesShow(TweakDefinition item, DetectionState? state, ShowFilter filter) => filter switch
    {
        ShowFilter.All or ShowFilter.Selected => true,
        ShowFilter.Applied => item.Kind == TweakKind.Tweak && state == DetectionState.Applied,
        ShowFilter.NotApplied => item.Kind == TweakKind.Tweak && state is not (DetectionState.Applied or DetectionState.NotSupported),
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
    };

    public static bool IsVisible(TweakDefinition item, DetectionState? state, string? query, RiskFilter filter, ShowFilter show = ShowFilter.All)
        => MatchesQuery(item, query) && PassesFilters(item, state, filter, show);

    /// <summary>Pozycje pasujące do zapytania, ale odrzucone przez filtr ryzyka albo filtr „Pokaż”.</summary>
    public static int CountHidden(
        IEnumerable<KeyValuePair<TweakDefinition, DetectionState?>> items, string? query, RiskFilter filter, ShowFilter show = ShowFilter.All)
    {
        var count = 0;
        foreach (var pair in items)
        {
            if (MatchesQuery(pair.Key, query) && !PassesFilters(pair.Key, pair.Value, filter, show))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Pozycje pasujące do zapytania i widoczne (oba filtry), które filtr ryzyka by ukrył, ale są pokazane tylko dzięki wyjątkowi „zastosowane”.
    /// </summary>
    public static int CountShownDespiteFilter(
        IEnumerable<KeyValuePair<TweakDefinition, DetectionState?>> items, string? query, RiskFilter filter, ShowFilter show = ShowFilter.All)
    {
        var count = 0;
        foreach (var pair in items)
        {
            if (MatchesQuery(pair.Key, query)
                && PassesFilters(pair.Key, pair.Value, filter, show)
                && !PassesRisk(pair.Key, null, filter))
            {
                count++;
            }
        }

        return count;
    }

    private static bool PassesFilters(TweakDefinition item, DetectionState? state, RiskFilter filter, ShowFilter show)
        => PassesRisk(item, state, filter) && PassesShow(item, state, show);
}
