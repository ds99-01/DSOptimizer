using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DSOptimizer.App.ViewModels;

/// <summary>Jak strona kategorii dzieli środkową listę: sekcje „Ustawienia”/„Akcje”, grupy tematyczne z katalogu albo (strona „Naprawy i narzędzia”) kategorie sekcji.</summary>
public enum CategoryListGrouping
{
    Sections,
    CatalogGroups,
    Categories,
}

/// <summary>
/// Grupa tematyczna w środkowej liście (<see cref="CategoryListGrouping.CatalogGroups"/>): nagłówek z nazwą i licznikiem „zastosowane/wszystkie”
/// jak licznik kategorii w menu, odświeżanym przy zmianie stanu pozycji grupy.
/// </summary>
public sealed class ListGroupViewModel : ObservableObject
{
    private readonly IReadOnlyList<ItemViewModel> _items;

    public ListGroupViewModel(string key, string title, IReadOnlyList<ItemViewModel> items)
    {
        Key = key;
        Title = title;
        _items = items;
    }

    /// <summary>Klucz grupy z katalogu (<c>CatalogGroups</c>).</summary>
    public string Key { get; }

    public string Title { get; }

    public bool HasCounter => _items.Any(item => item.IsTweak);

    public string CounterText => ItemCounter.Text(_items);

    /// <summary>Podpowiedź licznika i nazwa dla czytnika ekranu: „1 z 4 ustawień zastosowanych”.</summary>
    public string CounterDescription => ItemCounter.Description(_items);

    /// <summary>Nazwa grupy tam, gdzie WPF pokazuje grupę jako tekst (np. nazwa elementu grupy dla czytnika ekranu).</summary>
    public override string ToString() => Title;

    internal void RefreshCounter()
    {
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(CounterDescription));
    }
}
