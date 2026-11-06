using System.Windows;
using System.Windows.Controls;
using DSOptimizer.App.ViewModels;

namespace DSOptimizer.App.Views;

/// <summary>
/// Kontener pozycji menu: strona dostaje wiersz menu (<see cref="PageStyle"/> = <c>NavListBoxItem</c> z <c>Themes/Tokens.xaml</c>),
/// a linia grupy <see cref="NavSeparatorViewModel"/> – kontener bez fokusu, kliku i tła zaznaczenia (<see cref="SeparatorStyle"/> =
/// <c>NavSeparatorItem</c>), więc strzałki ją przeskakują.
/// </summary>
public sealed class NavContainerSelector : StyleSelector
{
    public Style? PageStyle { get; set; }

    public Style? SeparatorStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
        => item is NavSeparatorViewModel ? SeparatorStyle : PageStyle;
}
