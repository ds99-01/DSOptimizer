using System.Diagnostics.CodeAnalysis;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Cienka linia między grupami menu (<see cref="MainViewModel.NavItems"/>). Nie jest stroną: kontener się nie zaznacza i nie bierze
/// fokusu (styl NavSeparatorItem), czytnik ekranu jej nie widzi (<see cref="Views.NavMenuListBox"/>), a
/// <see cref="MainViewModel.SelectedPage"/> ją pomija. Pusta nazwa jak u każdej pozycji menu (pozycje menu mają właściwość Name).
/// </summary>
public sealed class NavSeparatorViewModel
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF binding reads instance properties.")]
    public string Name => string.Empty;

    /// <summary>Pusty tekst zamiast nazwy typu CLR, gdyby coś (np. peer automatyzacji) wzięło opis z elementu.</summary>
    public override string ToString() => string.Empty;
}
