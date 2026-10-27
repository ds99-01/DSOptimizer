using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Fokus klawiatury na wierszu listy mimo pełnego odświeżenia jej elementów (Refresh widoku przy filtrze albo wyszukiwaniu,
/// nowa lista wpisów historii). Reset kolekcji usuwa kontenery wierszy, a WPF przenosi wtedy fokus z usuniętego wiersza
/// na listę albo nigdzie. Wiersz rozpoznaje klucz (<c>key</c>), bo po odświeżeniu historii wiersze to nowe obiekty.
/// Przywraca tylko wtedy, gdy przed odświeżeniem fokus był na wierszu (albo w nim) i użytkownik go stamtąd nie przeniósł;
/// gdy wiersza już nie ma, fokus idzie na zaznaczony wiersz, żeby nie zniknął z okna.
/// </summary>
internal sealed class ListFocusKeeper
{
    private readonly ListBox _list;
    private readonly Func<object?, object?> _key;
    private object? _rowKey;
    private bool _restorePending;

    public ListFocusKeeper(ListBox list, Func<object?, object?> key)
    {
        _list = list ?? throw new ArgumentNullException(nameof(list));
        _key = key ?? throw new ArgumentNullException(nameof(key));
        list.GotKeyboardFocus += OnGotKeyboardFocus;
        list.LostKeyboardFocus += OnLostKeyboardFocus;
        ((INotifyCollectionChanged)list.Items).CollectionChanged += OnItemsChanged;
    }

    /// <summary>
    /// Przycisk panelu szczegółów (np. „Uruchom”, „Cofnij”), który w trakcie operacji staje się nieaktywny albo znika z fokusem
    /// (także logicznym – pod oknem potwierdzenia): fokus wraca na zaznaczony wiersz, a nie przepada.
    /// </summary>
    public void ReturnFocusFrom(UIElement element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        element.IsEnabledChanged += (_, e) => OnTargetLost(element, (bool)e.NewValue);
        element.IsVisibleChanged += (_, e) => OnTargetLost(element, (bool)e.NewValue);
    }

    /// <summary>Fokus klawiatury na zaznaczonym wierszu (po przewinięciu do niego); false, gdy lista jest pusta.</summary>
    public bool FocusSelectedRow() => FocusRow(_list.SelectedItem);

    private void OnTargetLost(UIElement element, bool stillAvailable)
    {
        if (stillAvailable)
        {
            return;
        }

        var scope = FocusManager.GetFocusScope(element);
        if (element.IsKeyboardFocused || (scope is not null && ReferenceEquals(FocusManager.GetFocusedElement(scope), element)))
        {
            FocusSelectedRow();
        }
    }

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Fokus na samej liście (po usunięciu wiersza) nie zmienia zapamiętanego wiersza.
        if (e.NewFocus is DependencyObject focus && RowOf(focus) is { } row)
        {
            _rowKey = _key(row.DataContext);
        }
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Użytkownik przeniósł fokus na inny element okna: po odświeżeniu nic nie wraca na listę.
        if (e.NewFocus is DependencyObject focus && !IsInList(focus) && focus is not Window)
        {
            _rowKey = null;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Reset || _rowKey is null || _restorePending)
        {
            return;
        }

        // Po ułożeniu nowych wierszy i po przeniesieniu fokusu przez WPF (Input) – priorytet Background jest niższy od obu.
        _restorePending = true;
        _list.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Restore));
    }

    private void Restore()
    {
        _restorePending = false;
        if (_rowKey is null)
        {
            return;
        }

        if (Keyboard.FocusedElement is DependencyObject focused)
        {
            if (RowOf(focused) is not null)
            {
                return;
            }

            if (IsConnected(focused) && !IsInList(focused) && focused is not Window)
            {
                _rowKey = null;
                return;
            }
        }

        foreach (var item in _list.Items)
        {
            if (Equals(_key(item), _rowKey))
            {
                FocusRow(item);
                return;
            }
        }

        FocusSelectedRow();
    }

    private bool FocusRow(object? item)
    {
        if (item is null || !_list.IsVisible)
        {
            return false;
        }

        _list.ScrollIntoView(item);
        _list.UpdateLayout();
        return _list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container && container.Focus();
    }

    private ListBoxItem? RowOf(DependencyObject element)
        => IsInList(element) ? ItemsControl.ContainerFromElement(_list, element) as ListBoxItem : null;

    private bool IsInList(DependencyObject element)
    {
        for (var current = element; current is not null; current = Parent(current))
        {
            if (ReferenceEquals(current, _list))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Element w oknie; element treści (np. link w dzienniku) liczy się jako podłączony – fokusu nie odbieramy.</summary>
    private static bool IsConnected(DependencyObject element)
        => element is not Visual visual || PresentationSource.FromVisual(visual) is not null;

    private static DependencyObject? Parent(DependencyObject element)
        => element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
}
