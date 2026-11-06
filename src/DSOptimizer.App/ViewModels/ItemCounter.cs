using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;

namespace DSOptimizer.App.ViewModels;

/// <summary>Licznik zbioru pozycji (kategoria w menu, grupa tematyczna w liście): „zastosowane/wszystkie tweaki” i jego pełny opis.</summary>
internal static class ItemCounter
{
    /// <summary>„1/4” albo, bez tweaków, liczba pozycji (<see cref="GuiText.CategoryCounter"/>).</summary>
    public static string Text(IEnumerable<ItemViewModel> items)
        => GuiText.CategoryCounter(Applied(items), Tweaks(items), items.Count());

    /// <summary>„1 z 4 ustawień zastosowanych”, „15 paneli” (<see cref="GuiText.CategoryCounterDescription"/>).</summary>
    public static string Description(IEnumerable<ItemViewModel> items)
        => GuiText.CategoryCounterDescription(
            Applied(items),
            Tweaks(items),
            items.Count(item => item.Definition.Kind == TweakKind.Action),
            items.Count(item => item.Definition.Kind == TweakKind.Panel));

    private static int Applied(IEnumerable<ItemViewModel> items) => items.Count(item => item.IsTweak && item.KnownState == DetectionState.Applied);

    private static int Tweaks(IEnumerable<ItemViewModel> items) => items.Count(item => item.IsTweak);
}
