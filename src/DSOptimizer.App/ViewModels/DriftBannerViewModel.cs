using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Baner „strażnika ustawień”: wcześniej zastosowane pozycje, których świeża detekcja już nie daje „zastosowane”.
/// Model trzyma tylko identyfikatory i widoczność; zaznaczenie pozycji na liście robi wywołujący (<c>selectAndShow</c>).
/// Ukrycie obowiązuje do końca uruchomienia: późniejsze <see cref="Update"/> go nie przywraca.
/// </summary>
public sealed class DriftBannerViewModel : ObservableObject
{
    private readonly Action<IReadOnlyList<string>> _selectAndShow;
    private readonly Func<bool> _canSelect;
    private IReadOnlyList<string> _ids = Array.Empty<string>();
    private bool _hidden;

    public DriftBannerViewModel(Action<IReadOnlyList<string>> selectAndShow, Func<bool> canSelect)
    {
        _selectAndShow = selectAndShow ?? throw new ArgumentNullException(nameof(selectAndShow));
        _canSelect = canSelect ?? throw new ArgumentNullException(nameof(canSelect));
        SelectAndShowCommand = new RelayCommand(SelectAndShow, CanSelectAndShow);
        HideCommand = new RelayCommand(Hide);
    }

    public IReadOnlyList<string> Ids
    {
        get => _ids;
        private set => SetProperty(ref _ids, value);
    }

    public bool IsVisible => !_hidden && _ids.Count > 0;

    public string Text => GuiText.DriftBannerText(_ids.Count);

    public IRelayCommand SelectAndShowCommand { get; }

    public IRelayCommand HideCommand { get; }

    public void Update(IReadOnlyList<string> ids)
    {
        Ids = ids ?? throw new ArgumentNullException(nameof(ids));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(Text));
        SelectAndShowCommand.NotifyCanExecuteChanged();
    }

    public void RefreshCanSelect() => SelectAndShowCommand.NotifyCanExecuteChanged();

    private bool CanSelectAndShow() => _canSelect() && _ids.Count > 0;

    // RelayCommand.Execute nie sprawdza CanExecute, więc warunek jest powtórzony.
    private void SelectAndShow()
    {
        if (CanSelectAndShow())
        {
            _selectAndShow(_ids);
        }
    }

    private void Hide()
    {
        _hidden = true;
        OnPropertyChanged(nameof(IsVisible));
    }
}
