using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Kategoria z lewej kolumny: pozycje (najpierw tweaki, potem akcje i panele), widok pogrupowany na sekcje i filtrowany
/// filtrem ryzyka, zaznaczona pozycja i licznik do menu.
/// </summary>
public sealed class CategoryViewModel : ObservableObject
{
    private readonly ListCollectionView _view;
    private ItemViewModel? _selectedItem;
    private RiskFilter _filter = RiskFilter.All;
    private int _hiddenCount;
    private int _shownDespiteFilterCount;

    public CategoryViewModel(GuiCategory category, MainViewModel owner)
    {
        Id = category.Id;
        Name = category.Name;
        var items = category.Items.Select(item => new ItemViewModel(item, owner)).ToList();
        Items = items.Where(item => item.IsTweak).Concat(items.Where(item => !item.IsTweak)).ToList();
        _view = new ListCollectionView(Items.ToList()) { Filter = PassesFilter };
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ItemViewModel.SectionName)));
        _selectedItem = Items.Count > 0 ? Items[0] : null;
        _view.MoveCurrentTo(_selectedItem);
        _view.CurrentChanged += (_, _) => SyncSelectedItem();
        foreach (var item in Items)
        {
            item.PropertyChanged += OnItemChanged;
        }
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Rodzaj ikony kategorii w menu (klucz z <c>Themes/Icons.xaml</c>).</summary>
    public string IconKind => GuiIcons.ForCategory(Id);

    public IReadOnlyList<ItemViewModel> Items { get; }

    public int Count => Items.Count;

    /// <summary>Licznik w menu: „zastosowane/wszystkie tweaki” albo liczba pozycji; niezależny od filtra i wyszukiwania.</summary>
    public string CounterText => GuiText.CategoryCounter(
        Items.Count(item => item.IsTweak && item.KnownState == DetectionState.Applied),
        Items.Count(item => item.IsTweak),
        Items.Count);

    /// <summary>Pełny opis licznika (podpowiedź licznika, czytnik ekranu): „0 z 13 ustawień zastosowanych”, „15 paneli”.</summary>
    public string CounterDescription => GuiText.CategoryCounterDescription(
        Items.Count(item => item.IsTweak && item.KnownState == DetectionState.Applied),
        Items.Count(item => item.IsTweak),
        Items.Count(item => item.Definition.Kind == TweakKind.Action),
        Items.Count(item => item.Definition.Kind == TweakKind.Panel));

    /// <summary>Podpowiedź w zwiniętym menu: „Nazwa – licznik”.</summary>
    public string NavToolTip => GuiText.CategoryNavToolTip(Name, CounterText);

    /// <summary>
    /// Lista do środkowej kolumny, pogrupowana na „Ustawienia” i „Akcje” (tworzona na wątku UI), z filtrem ryzyka.
    /// Zaznaczenie wiersza idzie przez bieżący element widoku (ListBox z IsSynchronizedWithCurrentItem), więc przy zmianie
    /// kategorii lista od razu pokazuje zapamiętaną pozycję tej kategorii, bez wyścigu dwóch wiązań (ItemsSource i SelectedItem).
    /// </summary>
    public ICollectionView ItemsView => _view;

    /// <summary>Pozycja pokazana w panelu szczegółów; pamiętana osobno dla każdej kategorii. Null tylko przy pustym widoku.</summary>
    public ItemViewModel? SelectedItem
    {
        get => _selectedItem;
        private set => SetProperty(ref _selectedItem, value);
    }

    /// <summary>Pozycje kategorii schowane przez filtr ryzyka (bez wyjątków zastosowanych i częściowych).</summary>
    public int HiddenCount
    {
        get => _hiddenCount;
        private set => SetProperty(ref _hiddenCount, value);
    }

    /// <summary>Zastosowane lub częściowe pozycje kategorii pokazane mimo filtra ryzyka (wyjątek „zastosowane”).</summary>
    public int ShownDespiteFilterCount
    {
        get => _shownDespiteFilterCount;
        private set => SetProperty(ref _shownDespiteFilterCount, value);
    }

    /// <summary>Filtr ryzyka widoku; bieżąca pozycja, która wypadła z widoku, przechodzi na pierwszą widoczną.</summary>
    public void ApplyFilter(RiskFilter filter)
    {
        _filter = filter;
        Refresh();
    }

    private bool PassesFilter(object value)
        => value is ItemViewModel item && ItemFilter.PassesRisk(item.Definition, item.KnownState, _filter);

    private void Refresh()
    {
        _view.Refresh();
        if (_view.CurrentItem is not ItemViewModel current || !PassesFilter(current))
        {
            _view.MoveCurrentToFirst();
        }

        SyncSelectedItem();
        HiddenCount = ItemFilter.CountHidden(
            Items.Select(item => new KeyValuePair<TweakDefinition, DetectionState?>(item.Definition, item.KnownState)),
            null,
            _filter);
        ShownDespiteFilterCount = ItemFilter.CountShownDespiteFilter(
            Items.Select(item => new KeyValuePair<TweakDefinition, DetectionState?>(item.Definition, item.KnownState)),
            null,
            _filter);
    }

    /// <summary>Pusty widok = brak pozycji w panelu szczegółów; chwilowy null z widoku przy niepustej liście jest ignorowany.</summary>
    private void SyncSelectedItem()
    {
        if (_view.CurrentItem is ItemViewModel item)
        {
            SelectedItem = item;
        }
        else if (_view.IsEmpty)
        {
            SelectedItem = null;
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Null = „Sprawdzanie…”: KnownState bez zmian, więc ani licznik, ani widoczność się nie zmieniają.
        if (e.PropertyName != nameof(ItemViewModel.State) || sender is ItemViewModel { State: null })
        {
            return;
        }

        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(CounterDescription));
        OnPropertyChanged(nameof(NavToolTip));

        // Przy „Wszystkie” stan nie zmienia widoczności (wyjątek zastosowanych dotyczy tylko filtrów zawężających).
        if (_filter != RiskFilter.All)
        {
            Refresh();
        }
    }
}
