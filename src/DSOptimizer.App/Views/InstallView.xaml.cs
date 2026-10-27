using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DSOptimizer.App.ViewModels;

namespace DSOptimizer.App.Views;

/// <summary>
/// Strona „Instalacja”. Pojedynczy klik tylko podświetla wiersz; dwuklik wiersza, pole wyboru i Spacja przełączają zaznaczenie
/// do instalacji (tylko wiersz z polem wyboru), jak na stronie „Aplikacje”. Linie listy (nagłówki, notatki) mają kontener bez fokusu
/// (<see cref="InstallListContainerSelector"/>), więc klik i strzałki trafiają tylko w wiersze. Ctrl+F z okna głównego trafia w pole
/// wyszukiwania (<see cref="FocusSearch"/>); pisanie filtruje listę, a winget rusza dopiero po Enterze albo „Szukaj też w winget”.
/// </summary>
public partial class InstallView
{
    public InstallView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is InstallViewModel old)
        {
            old.RowRevealRequested -= OnRowRevealRequested;
        }

        if (e.NewValue is InstallViewModel current)
        {
            current.RowRevealRequested += OnRowRevealRequested;
        }
    }

    /// <summary>„Pokaż RustDesk”: po przebudowie listy (nowy widok kategorii) przewija do wiersza i ustawia na nim fokus klawiatury.</summary>
    private void OnRowRevealRequested(object? sender, InstallRowViewModel row)
        => Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new System.Action(() =>
            {
                InstallList.ScrollIntoView(row);
                InstallList.UpdateLayout();
                if (InstallList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item)
                {
                    item.Focus();
                }
            }));

    /// <summary>Fokus i zaznaczenie tekstu pola wyszukiwania (Ctrl+F na stronie „Instalacja”).</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Wybrana kategoria zawsze widoczna na liście kategorii (w niskim oknie lista się przewija).</summary>
    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is { } selected)
        {
            CategoryList.ScrollIntoView(selected);
        }
    }

    /// <summary>Dwuklik wiersza (poza polem wyboru, które przełącza się samo) zaznacza albo odznacza aplikację do instalacji.</summary>
    private void OnInstallListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source || IsInsideCheckBox(source))
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(InstallList, source) is ListBoxItem { DataContext: InstallRowViewModel row } && row.IsSelectable)
        {
            row.IsSelected = !row.IsSelected;
            e.Handled = true;
        }
    }

    /// <summary>Spacja na wierszu przełącza zaznaczenie (jak na stronach kategorii).</summary>
    private void OnInstallListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.OriginalSource is ListBoxItem { DataContext: InstallRowViewModel row } && row.IsSelectable)
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
