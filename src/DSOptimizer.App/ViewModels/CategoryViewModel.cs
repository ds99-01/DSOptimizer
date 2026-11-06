using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Strona kategorii z lewej kolumny (<see cref="NavCategoryPage"/>): pozycje sekcja po sekcji (w sekcji najpierw tweaki, potem akcje
/// i panele), widok pogrupowany i filtrowany filtrem ryzyka, zaznaczona pozycja i licznik do menu. Zwykła kategoria ma jedną sekcję
/// i grupy „Ustawienia”/„Akcje” albo (<see cref="CategoryListGrouping.CatalogGroups"/>) grupy tematyczne z licznikami; strona
/// „Naprawy i narzędzia” ma sekcje napraw, narzędzi i paneli, grupy = nazwy tych kategorii.
/// </summary>
public sealed class CategoryViewModel : ObservableObject
{
    private readonly ListCollectionView _view;
    private ItemViewModel? _selectedItem;
    private RiskFilter _filter = RiskFilter.All;
    private ShowFilter _show = ShowFilter.All;
    private int _hiddenCount;
    private int _shownDespiteFilterCount;
    private ListGroupViewModel? _firstVisibleGroup;

    public CategoryViewModel(NavCategoryPage page, MainViewModel owner)
    {
        Id = page.Id;
        Name = page.Name;
        IsService = page.IsService;
        var items = page.Sections
            .SelectMany(section =>
            {
                var sectionItems = section.Items.Select(item => new ItemViewModel(item, owner)).ToList();
                return sectionItems.Where(item => item.IsTweak).Concat(sectionItems.Where(item => !item.IsTweak));
            })
            .ToList();
        var groupKeys = CatalogGroups.For(Id);
        Grouping = IsService
            ? CategoryListGrouping.Categories
            : groupKeys.Count > 0 && items.All(item => item.Definition.Group is { } group && CatalogGroups.IsKnown(Id, group))
                ? CategoryListGrouping.CatalogGroups
                : CategoryListGrouping.Sections;
        if (Grouping == CategoryListGrouping.CatalogGroups)
        {
            // Stabilnie: kolejność grup z katalogu, w grupie najpierw tweaki, poza tym kolejność wejścia.
            items = items
                .OrderBy(item => IndexOf(groupKeys, item.Definition.Group!))
                .ThenBy(item => item.IsTweak ? 0 : 1)
                .ToList();
            Groups = groupKeys
                .Select(key => (Key: key, Items: items.Where(item => item.Definition.Group == key).ToList()))
                .Where(group => group.Items.Count > 0)
                .Select(group =>
                {
                    var listGroup = new ListGroupViewModel(group.Key, GuiText.GroupName(Id, group.Key), group.Items);
                    foreach (var item in group.Items)
                    {
                        item.ListGroup = listGroup;
                    }

                    return listGroup;
                })
                .ToList();
        }
        else
        {
            Groups = Array.Empty<ListGroupViewModel>();
        }

        Items = items;
        _view = new ListCollectionView(Items.ToList()) { Filter = PassesFilter };
        _view.GroupDescriptions.Add(new PropertyGroupDescription(Grouping switch
        {
            CategoryListGrouping.Categories => nameof(ItemViewModel.CategoryName),
            CategoryListGrouping.CatalogGroups => nameof(ItemViewModel.ListGroup),
            _ => nameof(ItemViewModel.SectionName),
        }));
        _firstVisibleGroup = FindFirstVisibleGroup();
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

    /// <summary>Strona „Naprawy i narzędzia” (<see cref="GuiNav.ServicePageId"/>).</summary>
    public bool IsService { get; }

    /// <summary>
    /// Podział listy: strona „Naprawy i narzędzia” = kategorie sekcji; kategoria z grupami w katalogu, w której każda pozycja ma znaną grupę = grupy
    /// tematyczne; w pozostałych przypadkach (także pozycja bez grupy z zewnętrznego katalogu) sekcje „Ustawienia”/„Akcje”, więc nic nie ginie.
    /// </summary>
    public CategoryListGrouping Grouping { get; }

    /// <summary>Grupy tematyczne w kolejności katalogu (tylko niepuste); pusta lista przy sekcjach i na stronie „Naprawy i narzędzia”.</summary>
    public IReadOnlyList<ListGroupViewModel> Groups { get; }

    /// <summary>
    /// Pierwsza grupa tematyczna widoku po filtrze (nagłówek w stałym wierszu nad listą, obok „Zaznacz sugerowane”); null bez grup tematycznych
    /// albo przy pustym widoku. Liczona po każdym odświeżeniu widoku.
    /// </summary>
    public ListGroupViewModel? FirstVisibleGroup
    {
        get => _firstVisibleGroup;
        private set => SetProperty(ref _firstVisibleGroup, value);
    }

    /// <summary>Licznik w menu tylko przy stronie z tweakami; strona „Naprawy i narzędzia” go nie ma.</summary>
    public bool HasCounter => !IsService && Items.Any(item => item.IsTweak);

    /// <summary>Rodzaj ikony kategorii w menu (klucz z <c>Themes/Icons.xaml</c>).</summary>
    public string IconKind => GuiIcons.ForCategory(Id);

    /// <summary>Pozycje w kolejności sekcji (przy grupach tematycznych: grup), w sekcji i grupie najpierw tweaki.</summary>
    public IReadOnlyList<ItemViewModel> Items { get; }

    public int Count => Items.Count;

    /// <summary>Licznik w menu: „zastosowane/wszystkie tweaki” albo liczba pozycji; niezależny od filtra i wyszukiwania.</summary>
    public string CounterText => ItemCounter.Text(Items);

    /// <summary>Pełny opis licznika (podpowiedź licznika, czytnik ekranu): „0 z 13 ustawień zastosowanych”, „15 paneli”.</summary>
    public string CounterDescription => ItemCounter.Description(Items);

    /// <summary>Podpowiedź w zwiniętym menu: „Nazwa – licznik”, a bez licznika sama nazwa.</summary>
    public string NavToolTip => HasCounter ? GuiText.CategoryNavToolTip(Name, CounterText) : Name;

    /// <summary>
    /// Lista do środkowej kolumny, pogrupowana według <see cref="Grouping"/>: „Ustawienia” i „Akcje”, grupy tematyczne
    /// (<see cref="ItemViewModel.ListGroup"/>) albo kategorie sekcji (tworzona na wątku UI), z filtrem ryzyka.
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

    /// <summary>Pozycje kategorii schowane przez filtr ryzyka (bez wyjątków zastosowanych i częściowych) albo przez filtr „Pokaż”.</summary>
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

    /// <summary>
    /// Filtr ryzyka i „Pokaż” widoku; bieżąca pozycja, która wypadła z widoku, przechodzi na pierwszą widoczną. Strona „Naprawy i narzędzia”
    /// nie ma listy „Pokaż”, więc zawsze stosuje <see cref="ShowFilter.All"/> – nic nie znika bez kontrolki.
    /// </summary>
    public void ApplyFilter(RiskFilter risk, ShowFilter show)
    {
        _filter = risk;
        _show = IsService ? ShowFilter.All : show;
        Refresh();
    }

    /// <summary>Pozycja przechodzi filtry tej strony (ryzyko i „Pokaż”), czyli jest w <see cref="ItemsView"/>.</summary>
    internal bool IsShown(ItemViewModel item) => PassesFilter(item);

    private static int IndexOf(IReadOnlyList<string> keys, string key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return keys.Count;
    }

    private bool PassesFilter(object value)
        => value is ItemViewModel item && ItemFilter.IsVisible(item.Definition, item.KnownState, null, _filter, _show);

    private ListGroupViewModel? FindFirstVisibleGroup()
        => _view.Groups?.OfType<CollectionViewGroup>().Select(group => group.Name).OfType<ListGroupViewModel>().FirstOrDefault();

    private void Refresh()
    {
        _view.Refresh();
        FirstVisibleGroup = FindFirstVisibleGroup();
        if (_view.CurrentItem is not ItemViewModel current || !PassesFilter(current))
        {
            _view.MoveCurrentToFirst();
        }

        SyncSelectedItem();
        HiddenCount = ItemFilter.CountHidden(
            Items.Select(item => new KeyValuePair<TweakDefinition, DetectionState?>(item.Definition, item.KnownState)),
            null,
            _filter,
            _show);
        ShownDespiteFilterCount = ItemFilter.CountShownDespiteFilter(
            Items.Select(item => new KeyValuePair<TweakDefinition, DetectionState?>(item.Definition, item.KnownState)),
            null,
            _filter,
            _show);
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
        (sender as ItemViewModel)?.ListGroup?.RefreshCounter();

        // Przy „Wszystkie” (ryzyko i stan) stan nie zmienia widoczności (wyjątek zastosowanych dotyczy tylko filtrów zawężających).
        if (_filter != RiskFilter.All || _show is ShowFilter.NotApplied or ShowFilter.Applied)
        {
            Refresh();
        }
    }
}
