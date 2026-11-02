using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Pozycja listy widziana przez <see cref="SuggestedPick"/>: czy jest sugerowana i jaki ma znany stan (null = jeszcze nie sprawdzony).</summary>
public sealed record SuggestionCandidate(string Id, bool Suggested, DetectionState? KnownState);

/// <summary>
/// Reguła przycisku „Zaznacz sugerowane” (bez WPF): które widoczne pozycje trafiają do zaznaczenia. Program niczego nie zaznacza sam,
/// reguła działa wyłącznie na klik; nie wykonuje też żadnej zmiany.
/// </summary>
public static class SuggestedPick
{
    /// <summary>Sugerowane ustawienia ze znanym stanem „niezastosowane” albo „częściowo” (reszta nie ma czego zmieniać albo jest niepewna).</summary>
    public static IReadOnlyList<string> Settings(IReadOnlyList<SuggestionCandidate> visible)
        => visible
            .Where(item => item.Suggested && item.KnownState is DetectionState.NotApplied or DetectionState.Partial)
            .Select(item => item.Id)
            .ToList();

    /// <summary>Sugerowane aplikacje, które wolno usunąć (<see cref="AppRow.CanRemove"/>, jedyna reguła pola wyboru i serii).</summary>
    public static IReadOnlyList<string> Apps(IReadOnlyList<AppRow> visibleRows)
        => visibleRows
            .Where(row => row.Group == AppGroup.Suggested && row.CanRemove)
            .Select(row => row.App.Id)
            .ToList();

    /// <summary>
    /// Sugerowane pozycje autostartu (<see cref="AutostartRow.Suggested"/>: włączone, przełączalne, z bazy „można wyłączyć” albo bez pliku).
    /// Wywołujący podaje wiersze pasujące do wyszukiwania ze wszystkich grup, także „Bez pliku”; nic nie jest odznaczane.
    /// </summary>
    public static IReadOnlyList<string> Autostart(IReadOnlyList<AutostartRow> rows)
        => rows
            .Where(row => row.Suggested)
            .Select(row => row.Id)
            .ToList();
}
