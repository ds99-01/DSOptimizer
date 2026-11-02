using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Views;

/// <summary>
/// Strona „Autostart”. Pojedynczy klik pokazuje pozycję w panelu; dwuklik wiersza, pole wyboru i Spacja przełączają zaznaczenie do serii
/// (tylko wiersz przełączalny). Ctrl+F z okna głównego trafia w wyszukiwarkę tej strony (<see cref="FocusSearch"/>).
/// </summary>
public partial class AutostartView
{
    public AutostartView()
    {
        InitializeComponent();
    }

    /// <summary>Fokus i zaznaczenie tekstu wyszukiwarki strony (Ctrl+F na stronie „Autostart”).</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Panel szczegółów ma szerokość jak na stronie kategorii: 1/3 treści w granicach z <see cref="GuiLayout"/>.</summary>
    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && !double.IsNaN(e.NewSize.Width))
        {
            DetailColumn.Width = new GridLength(GuiLayout.DetailColumnWidth(e.NewSize.Width));
        }
    }

    /// <summary>
    /// Wiersz z panelu zawsze widoczny: po zmianie grupy, wyszukiwania albo odczycie lista nie zostaje przewinięta w połowie wiersza
    /// z poprzedniej zawartości (linia o kontach pod listą skraca ją o jeden wiersz tekstu).
    /// </summary>
    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutostartList.SelectedItem is { } item)
        {
            AutostartList.ScrollIntoView(item);
        }
    }

    /// <summary>Dwuklik wiersza (poza polem wyboru, które przełącza się samo) zaznacza albo odznacza pozycję.</summary>
    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source || IsInsideCheckBox(source))
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(AutostartList, source) is ListBoxItem { DataContext: AutostartRowViewModel row } && row.IsSelectable)
        {
            row.IsSelected = !row.IsSelected;
            e.Handled = true;
        }
    }

    /// <summary>Spacja na wierszu przełącza zaznaczenie (jak na stronach kategorii).</summary>
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.OriginalSource is ListBoxItem { DataContext: AutostartRowViewModel row } && row.IsSelectable)
        {
            row.IsSelected = !row.IsSelected;
            e.Handled = true;
        }
    }

    private static bool IsInsideCheckBox(DependencyObject source)
    {
        for (var current = source; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is CheckBox)
            {
                return true;
            }

            if (current is ListBoxItem)
            {
                return false;
            }
        }

        return false;
    }
}
