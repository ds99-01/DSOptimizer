using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Views;

public partial class HistoryView
{
    private readonly ListFocusKeeper _entryFocus;
    private Guid? _detailEntryId;

    public HistoryView()
    {
        InitializeComponent();
        // Odświeżenie historii to nowa lista wierszy: fokus klawiatury wraca na wpis o tym samym EntryId.
        // „Cofnij” nieaktywne albo schowane (operacja, wpis już cofnięty) oddaje fokus zaznaczonemu wierszowi.
        _entryFocus = new ListFocusKeeper(EntryList, item => item is HistoryRowViewModel row ? row.EntryId : null);
        _entryFocus.ReturnFocusFrom(UndoButton);
    }

    /// <summary>
    /// Enter na wierszu: fokus na „Cofnij”, gdy jest dostępne dla tego wpisu; inaczej nic. Autopowtórzenie Entera nie wciska
    /// „Cofnij” – <see cref="OnDetailPanelPreviewKeyDown"/>.
    /// </summary>
    private void OnEntryListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not ListBoxItem { DataContext: HistoryRowViewModel row })
        {
            return;
        }

        EntryList.SelectedItem = row;
        UndoButton.UpdateLayout();
        if (UndoButton.IsVisible && UndoButton.IsEnabled)
        {
            e.Handled = UndoButton.Focus();
        }
    }

    /// <summary>
    /// Przytrzymany Enter: pierwszy KeyDown na wierszu przenosi fokus na „Cofnij”, a autopowtórzenia (<see cref="KeyEventArgs.IsRepeat"/>)
    /// trafiałyby już w przycisk (ButtonBase klika przy KeyDown Entera) – wpis bezpieczny cofnąłby się w systemie bez okna potwierdzenia.
    /// Powtórzenia Entera w panelu są więc pochłaniane; świadomy Enter na „Cofnij” (bez powtórzenia) działa jak dotąd.
    /// </summary>
    private void OnDetailPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.IsRepeat)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Zmiana zaznaczonego wpisu (wg <see cref="HistoryRowViewModel.EntryId"/>) przewija szczegóły na górę. Odświeżenie historii
    /// tworzy nowe wiersze tego samego wpisu – wtedy przewinięcie zostaje; chwilowy brak wpisu (null) też go nie zeruje.
    /// </summary>
    private void OnDetailPanelDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not HistoryRowViewModel row || row.EntryId == _detailEntryId)
        {
            return;
        }

        _detailEntryId = row.EntryId;
        EntryDetailScroll.ScrollToTop();
    }

    /// <summary>Panel szczegółów wpisu ma szerokość jak na stronie kategorii: 1/3 treści w granicach z <see cref="GuiLayout"/>.</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && !double.IsNaN(e.NewSize.Width))
        {
            DetailColumn.Width = new GridLength(GuiLayout.DetailColumnWidth(e.NewSize.Width));
        }
    }
}
