using System.Windows;
using System.Windows.Controls;
using DSOptimizer.App.ViewModels;

namespace DSOptimizer.App.Views;

/// <summary>
/// Kontener elementu listy strony „Instalacja”: wiersz aplikacji dostaje zwykły kontener listy pozycji (<see cref="RowStyle"/> =
/// <c>ItemListBoxItem</c> z <c>Themes/Tokens.xaml</c>), a linia <see cref="InstallListLine"/> (nagłówek, notatka) – kontener bez fokusu,
/// tła i kreski (<see cref="LineStyle"/>), więc strzałki i klik jej nie zaznaczają.
/// </summary>
public sealed class InstallListContainerSelector : StyleSelector
{
    public Style? RowStyle { get; set; }

    public Style? LineStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
        => item is InstallListLine ? LineStyle : RowStyle;
}
