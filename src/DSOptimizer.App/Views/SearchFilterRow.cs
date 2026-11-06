using System;
using System.Windows;
using System.Windows.Controls;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Views;

/// <summary>
/// Wiersz szukania strony kategorii: pierwsze dziecko = wyszukiwarka, kolejne = grupy list („Pokaż”, „Ryzyko”, każda z etykietą) w jednym rzędzie.
/// Przy szerokim wierszu rząd list stoi po prawej obok wyszukiwarki (odstęp <see cref="GuiLayout.SearchFiltersGap"/>), a gdy wyszukiwarka miałaby
/// mniej niż <see cref="GuiLayout.SearchMinWidth"/>, schodzi do drugiego wiersza pod nią, wyrównany do lewej (<see cref="GuiLayout.SearchFiltersWrap"/>).
/// Decyzja zależy tylko od szerokości wiersza i ZAREZERWOWANEJ szerokości wszystkich grup (także ukrytych przez <see cref="IsShownProperty"/>),
/// nie od bieżącej szerokości wyszukiwarki ani od tego, czy „Pokaż” jest widoczne, więc układ nie przeskakuje przy pisaniu ani przy wejściu w „Zaznaczone”.
/// </summary>
public sealed class SearchFilterRow : Panel
{
    private static readonly DependencyPropertyKey IsWrappedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsWrapped), typeof(bool), typeof(SearchFilterRow), new PropertyMetadata(false));

    /// <summary>Listy stoją w drugim wierszu pod wyszukiwarką.</summary>
    public static readonly DependencyProperty IsWrappedProperty = IsWrappedPropertyKey.DependencyProperty;

    public bool IsWrapped
    {
        get => (bool)GetValue(IsWrappedProperty);
        private set => SetValue(IsWrappedPropertyKey, value);
    }

    /// <summary>
    /// Grupa list pokazana (domyślnie). False: grupa ma <see cref="Visibility.Hidden"/> (bez rysowania, fokusu i trafień), w rzędzie nie zajmuje miejsca,
    /// ale jej szerokość liczy się do decyzji o drugim wierszu, więc pokazanie i ukrycie grupy nie przestawia wiersza.
    /// </summary>
    public static readonly DependencyProperty IsShownProperty = DependencyProperty.RegisterAttached(
        "IsShown", typeof(bool), typeof(SearchFilterRow),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange, OnIsShownChanged));

    public static bool GetIsShown(UIElement element) => element is null || (bool)element.GetValue(IsShownProperty);

    public static void SetIsShown(UIElement element, bool value) => element?.SetValue(IsShownProperty, value);

    private static void OnIsShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => d.SetCurrentValue(VisibilityProperty, (bool)e.NewValue ? Visibility.Visible : Visibility.Hidden);

    protected override Size MeasureOverride(Size availableSize)
    {
        // Kontrakt: pierwsze dziecko = wyszukiwarka, kolejne = grupy list (etykieta + lista) w jednym rzędzie. Bez dzieci wiersz jest pusty.
        if (InternalChildren.Count == 0)
        {
            return default;
        }

        var search = InternalChildren[0];
        var (laidWidth, reservedWidth, filtersHeight) = MeasureFilters(availableSize.Height);
        var hasFilters = reservedWidth > 0;
        IsWrapped = hasFilters && !double.IsInfinity(availableSize.Width) && GuiLayout.SearchFiltersWrap(availableSize.Width, reservedWidth);

        if (IsWrapped)
        {
            search.Measure(new Size(availableSize.Width, availableSize.Height));
            return new Size(
                Math.Max(search.DesiredSize.Width, laidWidth),
                search.DesiredSize.Height + GuiLayout.SearchFiltersRowGap + filtersHeight);
        }

        var side = laidWidth > 0 ? GuiLayout.SearchFiltersGap + laidWidth : 0;
        search.Measure(new Size(Math.Max(0, availableSize.Width - side), availableSize.Height));
        return new Size(search.DesiredSize.Width + side, Math.Max(search.DesiredSize.Height, filtersHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        var search = InternalChildren[0];
        var laidWidth = 0.0;
        var filtersHeight = 0.0;
        for (var i = 1; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            filtersHeight = Math.Max(filtersHeight, child.DesiredSize.Height);
            if (GetIsShown(child))
            {
                laidWidth += child.DesiredSize.Width;
            }
        }

        double x;
        double top;
        if (IsWrapped)
        {
            var searchHeight = search.DesiredSize.Height;
            search.Arrange(new Rect(0, 0, finalSize.Width, searchHeight));
            x = 0;
            top = searchHeight + GuiLayout.SearchFiltersRowGap;
        }
        else
        {
            var side = laidWidth > 0 ? GuiLayout.SearchFiltersGap + laidWidth : 0;
            search.Arrange(new Rect(0, 0, Math.Max(0, finalSize.Width - side), finalSize.Height));
            x = finalSize.Width - laidWidth;
            top = (finalSize.Height - filtersHeight) / 2;
        }

        for (var i = 1; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (!GetIsShown(child))
            {
                child.Arrange(new Rect(x, top, 0, 0));
                continue;
            }

            var size = child.DesiredSize;
            child.Arrange(new Rect(x, top + ((filtersHeight - size.Height) / 2), size.Width, size.Height));
            x += size.Width;
        }

        return finalSize;
    }

    // Grupy list mierzone bez ograniczenia szerokości. Ukryta grupa (IsShown = false, Visibility.Hidden) mierzy się normalnie, więc jej szerokość
    // i wysokość są znane bez pokazywania: wchodzą do szerokości zarezerwowanej i wysokości rzędu, ale nie do szerokości ułożonej.
    private (double LaidWidth, double ReservedWidth, double Height) MeasureFilters(double availableHeight)
    {
        var laid = 0.0;
        var reserved = 0.0;
        var height = 0.0;
        for (var i = 1; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(double.PositiveInfinity, availableHeight));
            var size = child.DesiredSize;
            reserved += size.Width;
            height = Math.Max(height, size.Height);
            if (GetIsShown(child))
            {
                laid += size.Width;
            }
        }

        return (laid, reserved, height);
    }
}
