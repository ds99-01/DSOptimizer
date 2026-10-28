using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Strona „Historia zmian” (ostatnia pozycja menu): lista wpisów journala z filtrem, panel szczegółów zaznaczonego wpisu
/// z „Cofnij” i licznik aktywnych zmian w menu. Logika (stany, filtr, blokady cofania, teksty) jest w Core
/// (<see cref="ChangeHistory"/>, <see cref="HistoryText"/>); tu tylko ładowanie, filtr widoku i zaznaczenie.
/// </summary>
public sealed class HistoryViewModel : ObservableObject
{
    private readonly Func<Task<HistorySnapshot>> _load;
    private readonly Func<string, Task> _undo;
    private readonly Action _showApps;
    private readonly Action<GuiLogLevel, string> _log;
    private IReadOnlyList<HistoryRowViewModel> _all = Array.Empty<HistoryRowViewModel>();
    private IReadOnlyList<HistoryRowViewModel> _entries = Array.Empty<HistoryRowViewModel>();
    private HistoryFilter _filter = HistoryFilter.All;
    private HistoryRowViewModel? _selectedEntry;
    private int _activeCount;
    private string _errorText = string.Empty;
    private bool _isLoading;
    private bool _isBusy;
    private bool _loaded;
    private bool _warningLogged;
    private int _version;

    /// <param name="load">Wczytanie historii; samo idzie poza wątek UI (journal pod blokadą sesji, nazwy kont przez LSA).</param>
    /// <param name="undo">„Cofnij” dla id pozycji (operacja głównego okna z potwierdzeniem).</param>
    /// <param name="showApps">Przejście na stronę „Aplikacje” (link w szczegółach wpisu instalacji).</param>
    /// <param name="log">Linia do dziennika w oknie.</param>
    public HistoryViewModel(Func<Task<HistorySnapshot>> load, Func<string, Task> undo, Action showApps, Action<GuiLogLevel, string> log)
    {
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
        _showApps = showApps ?? throw new ArgumentNullException(nameof(showApps));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Filters = new[] { HistoryFilter.All, HistoryFilter.Active, HistoryFilter.PendingReboot, HistoryFilter.Inactive }
            .Select(filter => new HistoryFilterOption(filter, HistoryText.FilterName(filter)))
            .ToList();
        UndoCommand = new AsyncRelayCommand(UndoAsync, CanUndo);
        ShowAppsCommand = new RelayCommand(_showApps);
    }

    /// <summary>Link „Przejdź do: Aplikacje” w szczegółach wpisu instalacji.</summary>
    public IRelayCommand ShowAppsCommand { get; }

    public string Name { get; } = HistoryText.PageTitle;

    /// <summary>Rodzaj ikony w menu (klucz z <c>Themes/Icons.xaml</c>).</summary>
    public string IconKind { get; } = GuiIcons.History;

    /// <summary>Liczba aktywnych zmian (<see cref="ChangeHistory.ActiveCount"/>), niezależna od filtra.</summary>
    public int ActiveCount
    {
        get => _activeCount;
        private set
        {
            if (SetProperty(ref _activeCount, value))
            {
                OnPropertyChanged(nameof(CounterText));
                OnPropertyChanged(nameof(CounterDescription));
                OnPropertyChanged(nameof(NavToolTip));
            }
        }
    }

    /// <summary>Licznik w menu: liczba aktywnych zmian, także „0”.</summary>
    public string CounterText => ActiveCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>Pełny opis licznika (podpowiedź licznika, czytnik ekranu): „3 aktywne zmiany”.</summary>
    public string CounterDescription => GuiText.HistoryCounterDescription(ActiveCount);

    /// <summary>Podpowiedź w zwiniętym menu: „Historia zmian – N”.</summary>
    public string NavToolTip => GuiText.CategoryNavToolTip(Name, CounterText);

    public IReadOnlyList<HistoryFilterOption> Filters { get; }

    /// <summary>Filtr listy (start „Wszystkie wpisy”); zmiana filtruje już wczytane wpisy, bez ponownego odczytu.</summary>
    public HistoryFilter Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Wpisy pasujące do <see cref="Filter"/>, od najnowszego.</summary>
    public IReadOnlyList<HistoryRowViewModel> Entries
    {
        get => _entries;
        private set => SetProperty(ref _entries, value);
    }

    /// <summary>Wpis w panelu szczegółów; null = panel zwinięty (pusta lista).</summary>
    public HistoryRowViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                UndoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Linia w miejscu pustej listy; pusta, gdy są wpisy, przed pierwszym odczytem i gdy odczyt się nie udał (mówi o tym <see cref="ErrorText"/>).</summary>
    public string EmptyText
    {
        get
        {
            if (!_loaded || _entries.Count > 0)
            {
                return string.Empty;
            }

            if (_all.Count > 0)
            {
                return HistoryText.EmptyFiltered;
            }

            return HasError ? string.Empty : HistoryText.Empty;
        }
    }

    /// <summary>Ostrzeżenie z ostatniego odczytu (np. uszkodzony journal); pusty, gdy odczyt był czysty.</summary>
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorText.Length > 0;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                UndoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Operacja w głównym oknie (ustawia <see cref="MainViewModel"/>): „Cofnij” nieaktywne.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                UndoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>„Cofnij” zaznaczonego wpisu: tylko aktywny wpis bez <see cref="HistoryEntry.UndoBlockedReason"/>, poza operacją.</summary>
    public IAsyncRelayCommand UndoCommand { get; }

    /// <summary>
    /// Wczytuje historię od nowa. Wynik starszego odczytu, który skończył się później, jest pomijany (jak baner restartu).
    /// Pierwsze ostrzeżenie w sesji trafia do dziennika, każde do <see cref="ErrorText"/>. Zaznaczenie wraca po
    /// <see cref="HistoryEntry.EntryId"/>; gdy wpisu już nie ma w widoku – pierwszy wpis. Nigdy nie rzuca: każdy błąd
    /// (odczyt, budowa wierszy, filtr, dziennik) = <see cref="ErrorText"/> i jedna linia w dzienniku, a <see cref="IsLoading"/>
    /// jest zerowane dla bieżącego odczytu.
    /// </summary>
    public async Task RefreshAsync()
    {
        var version = ++_version;
        try
        {
            IsLoading = true;
            HistorySnapshot snapshot;
            try
            {
                snapshot = await _load().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                snapshot = new HistorySnapshot(Array.Empty<HistoryEntry>(), HistoryText.LoadFailed(ex.Message));
            }

            if (snapshot.Warning is { } warning && !_warningLogged)
            {
                _warningLogged = true;
                _log(GuiLogLevel.Warning, warning);
            }

            if (version != _version)
            {
                return;
            }

            ErrorText = snapshot.Warning ?? string.Empty;
            _all = snapshot.Entries.Select(entry => new HistoryRowViewModel(entry)).ToList();
            _loaded = true;
            ActiveCount = ChangeHistory.ActiveCount(snapshot.Entries);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ReportRefreshFailure(version, ex);
        }
        finally
        {
            ClearLoading(version);
        }
    }

    /// <summary>Koniec bieżącego odczytu; wyjątek z obsługi PropertyChanged nie może wyjść z <see cref="RefreshAsync"/>.</summary>
    private void ClearLoading(int version)
    {
        if (version != _version)
        {
            return;
        }

        try
        {
            IsLoading = false;
        }
        catch (Exception ex)
        {
            _isLoading = false;
            ReportRefreshFailure(version, ex);
        }
    }

    /// <summary>Filtr „Czekające na restart” i świeży odczyt (link „Pokaż które” na banerze restartu).</summary>
    public Task ShowPendingRebootAsync()
    {
        Filter = HistoryFilter.PendingReboot;
        return RefreshAsync();
    }

    /// <summary>
    /// Nieoczekiwany błąd odświeżenia (np. wiersz, filtr, dziennik): komunikat w <see cref="ErrorText"/> (tylko bieżący odczyt)
    /// i jedna linia w dzienniku. Sam też nie rzuca – <see cref="RefreshAsync"/> bywa wołane bez await.
    /// </summary>
    private void ReportRefreshFailure(int version, Exception ex)
    {
        try
        {
            if (version == _version)
            {
                ErrorText = HistoryText.LoadFailed(ex.Message);
                OnPropertyChanged(nameof(EmptyText));
            }
        }
        catch (Exception)
        {
            // Powiadomienie widoku zawiodło – zostaje linia w dzienniku niżej.
        }

        try
        {
            _log(GuiLogLevel.Error, L.F("Nieoczekiwany błąd: {0}: {1}", ex.GetType().Name, ex.Message));
        }
        catch (Exception)
        {
            // Dziennik niedostępny – nic więcej nie da się zrobić bez rzucania.
        }
    }

    private void ApplyFilter()
    {
        var keep = _selectedEntry?.EntryId;
        var visible = _all.Where(row => ChangeHistory.Matches(row.State, _filter)).ToList();
        Entries = visible;
        SelectedEntry = visible.FirstOrDefault(row => row.EntryId == keep) ?? visible.FirstOrDefault();
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>
    /// Także nie w trakcie odczytu: po operacji IsBusy wraca na false, zanim historia się przeładuje (tłumaczenie SID-ów
    /// bywa sekundami), a nieaktualny wiersz „Aktywna” po cofnięciu dałby drugie Undo bez aktywnego wpisu (wartości z katalogu).
    /// </summary>
    private bool CanUndo() => !IsBusy && !IsLoading && SelectedEntry is { IsActive: true, UndoBlockedReason: null };

    private async Task UndoAsync()
    {
        if (SelectedEntry is not { } row || !CanUndo())
        {
            return;
        }

        try
        {
            await _undo(row.TweakId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log(GuiLogLevel.Error, L.F("Nieoczekiwany błąd: {0}: {1}", ex.GetType().Name, ex.Message));
        }
    }
}
