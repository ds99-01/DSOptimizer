using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using TextFolding = DSOptimizer.Core.Gui.SearchText;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Strona „Autostart” (wariant A: liczba, pod nią pasek). Cienka warstwa nad <see cref="AutostartSession"/>: odczyt przy każdym wejściu,
/// F5, „Odśwież listę” i po serii (starszy odczyt odrzucany), nazwa konta poza wątkiem UI, własne zaznaczenie i pasek serii
/// „Wyłącz (N)”/„Włącz (M)”, który zawsze idzie przez okno serii (<c>MainViewModel</c>). Nic nie zaznacza się samo: sugerowane zaznacza
/// dopiero „Zaznacz sugerowane” (<see cref="SuggestedPick.Autostart"/>) – ze wszystkich grup, w granicach wyszukiwania.
/// </summary>
public sealed class AutostartViewModel : ObservableObject
{
    private static readonly char[] WordSeparators = { ' ' };

    private readonly AutostartSession _session;
    private readonly IAccountNames _names;
    private readonly Func<IReadOnlyList<AutostartRow>, AutostartState, Task> _run;
    private readonly IClipboardText _clipboard;
    private readonly Action<GuiLogLevel, string> _log;
    private readonly IClock _clock;
    private readonly HashSet<string> _loggedWarnings = new(StringComparer.Ordinal);
    private IReadOnlyList<AutostartRowViewModel> _rows = Array.Empty<AutostartRowViewModel>();
    private IReadOnlyList<AutostartRowViewModel> _matching = Array.Empty<AutostartRowViewModel>();
    private IReadOnlyList<AutostartRowViewModel> _visibleRows = Array.Empty<AutostartRowViewModel>();
    private AutostartRowViewModel? _selectedRow;
    private AutostartGroup _selectedGroup = AutostartGroup.Enabled;
    private string _searchText = string.Empty;
    private DateTimeOffset? _readAt;
    private DateTimeOffset? _measuredAt;
    private string? _readError;
    private bool _isLoading;
    private bool _isBusy;
    private bool _rebuilding;
    private int _toDisable;
    private int _toEnable;
    private int _selectedCount;
    private int _version;

    /// <param name="session">Odczyt i treść serii (bez WPF).</param>
    /// <param name="names">SID → nazwa konta (LSA może blokować – tylko poza wątkiem UI).</param>
    /// <param name="run">„Wyłącz (N)”/„Włącz (M)”: okno serii, potwierdzenie, postęp i wynik (MainViewModel).</param>
    /// <param name="clipboard">Schowek dla „Kopiuj nazwę”.</param>
    /// <param name="log">Linia do dziennika w oknie.</param>
    /// <param name="clock">Chwila odczytu listy.</param>
    public AutostartViewModel(
        AutostartSession session,
        IAccountNames names,
        Func<IReadOnlyList<AutostartRow>, AutostartState, Task> run,
        IClipboardText clipboard,
        Action<GuiLogLevel, string> log,
        IClock clock)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _names = names ?? throw new ArgumentNullException(nameof(names));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Groups = new[] { AutostartGroup.Enabled, AutostartGroup.Disabled, AutostartGroup.MissingFile }
            .Select(group => new AutostartGroupOption(group))
            .ToList();
        Groups[0].IsSelected = true;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && !IsLoading);
        DisableCommand = new AsyncRelayCommand(() => RunAsync(AutostartState.Disabled), () => CanRun(_toDisable));
        EnableCommand = new AsyncRelayCommand(() => RunAsync(AutostartState.Enabled), () => CanRun(_toEnable));
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy && _selectedCount > 0);
        SelectSuggestedCommand = new RelayCommand(SelectSuggested, CanSelectSuggested);
        CopyNameCommand = new RelayCommand(CopyName, () => SelectedRow is not null);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        SelectGroupCommand = new RelayCommand<AutostartGroupOption>(option =>
        {
            if (option is not null)
            {
                SelectedGroup = option.Value;
            }
        });
    }

    public string Name { get; } = AutostartText.PageTitle;

    /// <summary>Rodzaj ikony w menu (symbol zasilania, klucz z <c>Themes/Icons.xaml</c>).</summary>
    public string IconKind { get; } = GuiIcons.Autostart;

    /// <summary>Licznik w menu: liczba wszystkich pozycji; pusty przed pierwszym odczytem.</summary>
    public string CounterText => _readAt is null ? string.Empty : _rows.Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Pełny opis licznika (podpowiedź, czytnik ekranu): „16 pozycji”; pusty przed pierwszym odczytem.</summary>
    public string CounterDescription => _readAt is null ? string.Empty : AutostartText.CounterDescription(_rows.Count);

    public string NavToolTip => _readAt is null ? Name : GuiText.CategoryNavToolTip(Name, CounterText);

    public string SearchPlaceholder { get; } = AutostartText.SearchPlaceholder;

    /// <summary>
    /// Podtytuł: odczyt w toku, błąd ostatniego odczytu (poprzednia lista zostaje), „11 włączonych · pomiar Windows 02.11, 08:14” albo
    /// „jeszcze nie odczytana”.
    /// </summary>
    public string Subtitle => IsLoading
        ? AutostartText.Reading
        : _readError ?? (_readAt is null ? AutostartText.NotRead : AutostartText.Subtitle(_rows.Count(row => row.Group == AutostartGroup.Enabled), _measuredAt));

    /// <summary>Stała linia pod listą.</summary>
    public string OtherAccountsNote { get; } = AutostartText.OtherAccountsNote;

    public IReadOnlyList<AutostartGroupOption> Groups { get; }

    /// <summary>Grupa pokazywana na liście (start: „Włączone”).</summary>
    public AutostartGroup SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                foreach (var option in Groups)
                {
                    option.IsSelected = option.Value == value;
                }

                ApplyFilter();
            }
        }
    }

    /// <summary>Fraza wyszukiwarki strony (nazwa, wydawca i polecenie, bez polskich znaków).</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Wiersze wybranej grupy pasujące do frazy, w kolejności listy (zmierzone malejąco, potem niezmierzone).</summary>
    public IReadOnlyList<AutostartRowViewModel> VisibleRows
    {
        get => _visibleRows;
        private set => SetProperty(ref _visibleRows, value);
    }

    /// <summary>Wiersz w panelu szczegółów; null = panel zwinięty.</summary>
    public AutostartRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                CopyNameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Linia w miejscu pustej listy (po odczycie): pusta grupa albo brak dopasowań.</summary>
    public string EmptyText => _readAt is null || _visibleRows.Count > 0
        ? string.Empty
        : TextFolding.Fold(_searchText).Length > 0 ? AutostartText.EmptySearch : AutostartText.EmptyGroup;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(Subtitle));
                NotifyCommands();
            }
        }
    }

    /// <summary>Operacja w głównym oknie (ustawia <c>MainViewModel</c>): odświeżanie i seria nieaktywne.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    public string SelectionBarTitle => AutostartText.SelectionBarTitle(_selectedCount);

    public string SelectionBarDetails => _selectedCount == 0 ? string.Empty : AutostartText.SelectionBarDetails;

    /// <summary>„Wyłącz (N)”: N = zaznaczone przełączalne i włączone.</summary>
    public string DisableText => AutostartText.DisableButton(_toDisable);

    /// <summary>„Włącz (M)”: M = zaznaczone przełączalne i wyłączone.</summary>
    public string EnableText => AutostartText.EnableButton(_toEnable);

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand DisableCommand { get; }

    public IAsyncRelayCommand EnableCommand { get; }

    public IRelayCommand ClearSelectionCommand { get; }

    /// <summary>„Zaznacz sugerowane”: dodaje do zaznaczenia sugerowane pozycje pasujące do wyszukiwania ze wszystkich grup; nic nie odznacza.</summary>
    public IRelayCommand SelectSuggestedCommand { get; }

    /// <summary>„Kopiuj nazwę”: nazwa, wydawca i plik wiersza z panelu, każde w osobnej linii.</summary>
    public IRelayCommand CopyNameCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    public IRelayCommand<AutostartGroupOption> SelectGroupCommand { get; }

    /// <summary>
    /// Świeży odczyt listy (wejście na stronę, F5, „Odśwież listę”, po serii). Wynik starszego odczytu jest pomijany. Zaznaczenie i wiersz
    /// panelu wracają po Id. Ostrzeżenia odczytu trafiają do dziennika (każdy tekst raz na sesję). Błąd odczytu = podtytuł z błędem,
    /// poprzednia lista zostaje. Nigdy nie rzuca.
    /// </summary>
    public async Task RefreshAsync()
    {
        var version = ++_version;
        try
        {
            IsLoading = true;
            var page = await _session.LoadAsync().ConfigureAwait(true);
            var userName = page.List is null ? null : await Task.Run(() => _names.NameOf(_session.UserSid)).ConfigureAwait(true);
            if (page.List is { } list)
            {
                foreach (var warning in list.Warnings.Where(_loggedWarnings.Add))
                {
                    _log(GuiLogLevel.Warning, warning);
                }
            }

            if (version != _version)
            {
                return;
            }

            if (page.List is { } read)
            {
                Rebuild(read, userName);
            }
            else
            {
                _readError = page.Error ?? AutostartText.ReadFailed;
            }
        }
        catch (Exception ex)
        {
            Report(ex);
        }
        finally
        {
            if (version == _version)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(Subtitle));
            }
        }
    }

    /// <summary>Przelicza liczniki „Wyłącz (N)”/„Włącz (M)” i pasek serii po zmianie zaznaczenia.</summary>
    public void RefreshSelection()
    {
        if (_rebuilding)
        {
            return;
        }

        var selected = _rows.Where(row => row.IsSelected).ToList();
        _selectedCount = selected.Count;
        _toDisable = selected.Count(row => Matches(row, AutostartState.Disabled));
        _toEnable = selected.Count(row => Matches(row, AutostartState.Enabled));
        OnPropertyChanged(nameof(SelectionBarTitle));
        OnPropertyChanged(nameof(SelectionBarDetails));
        OnPropertyChanged(nameof(DisableText));
        OnPropertyChanged(nameof(EnableText));
        NotifyCommands();
    }

    /// <summary>Wiersz trafia do serii w kierunku <paramref name="target"/>: przełączalny i w stanie przeciwnym (ta sama reguła co w sesji).</summary>
    private static bool Matches(AutostartRowViewModel row, AutostartState target)
        => row.IsSelected && row.Row.CanToggle && row.Row.State == (target == AutostartState.Disabled ? AutostartState.Enabled : AutostartState.Disabled);

    private void Rebuild(AutostartList list, string? userName)
    {
        var selected = new HashSet<string>(_rows.Where(row => row.IsSelected).Select(row => row.Id), StringComparer.Ordinal);
        var keep = _selectedRow?.Id;
        var maxCpu = list.Rows
            .Where(row => row.Entry.Measurement is not null && !row.MissingFile)
            .Select(row => row.Entry.Measurement!.CpuMicroseconds)
            .DefaultIfEmpty(0)
            .Max();

        _rebuilding = true;
        try
        {
            _rows = list.Rows.Select(row => new AutostartRowViewModel(row, maxCpu, userName, RefreshSelection)).ToList();
            foreach (var row in _rows)
            {
                row.IsSelected = selected.Contains(row.Id);
            }
        }
        finally
        {
            _rebuilding = false;
        }

        _readAt = _clock.UtcNow;
        _measuredAt = list.MeasuredAt;
        _readError = null;
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(CounterDescription));
        OnPropertyChanged(nameof(NavToolTip));
        OnPropertyChanged(nameof(Subtitle));
        ApplyFilter(keep);
        RefreshSelection();
    }

    private bool CanRun(int count) => !IsBusy && !IsLoading && count > 0;

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
        EnableCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        SelectSuggestedCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<string> SuggestedToSelect() => SuggestedPick.Autostart(_matching.Select(row => row.Row).ToList());

    private bool CanSelectSuggested() => !IsBusy && !IsLoading && SuggestedToSelect().Count > 0;

    // RelayCommand.Execute nie sprawdza CanExecute, więc warunek jest też tutaj.
    private void SelectSuggested()
    {
        if (IsBusy || IsLoading)
        {
            return;
        }

        var ids = new HashSet<string>(SuggestedToSelect(), StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return;
        }

        _rebuilding = true;
        try
        {
            foreach (var row in _rows.Where(row => ids.Contains(row.Id)))
            {
                row.IsSelected = true;
            }
        }
        finally
        {
            _rebuilding = false;
        }

        RefreshSelection();
    }

    private void ApplyFilter() => ApplyFilter(_selectedRow?.Id);

    private void ApplyFilter(string? keep)
    {
        var query = TextFolding.Fold(_searchText);
        var words = query.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        _matching = _rows.Where(row => words.All(word => row.SearchKey.IndexOf(word, StringComparison.Ordinal) >= 0)).ToList();
        foreach (var option in Groups)
        {
            option.Count = _matching.Count(row => row.Group == option.Value);
        }

        var visible = _matching.Where(row => row.Group == _selectedGroup).ToList();
        VisibleRows = visible;
        SelectedRow = visible.FirstOrDefault(row => row.Id == keep) ?? visible.FirstOrDefault();
        OnPropertyChanged(nameof(EmptyText));
        SelectSuggestedCommand.NotifyCanExecuteChanged();
    }

    private async Task RunAsync(AutostartState target)
    {
        var rows = _rows.Where(row => Matches(row, target)).Select(row => row.Row).ToList();
        if (rows.Count == 0 || IsBusy || IsLoading)
        {
            return;
        }

        try
        {
            await _run(rows, target).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private void ClearSelection()
    {
        _rebuilding = true;
        try
        {
            foreach (var row in _rows)
            {
                row.IsSelected = false;
            }
        }
        finally
        {
            _rebuilding = false;
        }

        RefreshSelection();
    }

    /// <summary>Schowek bywa zajęty przez inny proces (np. RustDesk): <see cref="ClipboardCopy"/> rozstrzyga odczytem zwrotnym.</summary>
    private void CopyName()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var result = ClipboardCopy.Copy(_clipboard, AutostartText.CopyNameText(row.Row));
        if (result.Copied)
        {
            _log(GuiLogLevel.Info, AutostartText.NameCopied);
        }
        else
        {
            _log(GuiLogLevel.Warning, AutostartText.NameCopyFailed(result.Error?.Message ?? string.Empty));
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            _log(GuiLogLevel.Error, L.F("Nieoczekiwany błąd: {0}: {1}", ex.GetType().Name, ex.Message));
        }
        catch (Exception)
        {
            // Dziennik niedostępny – RefreshAsync bywa wołane bez await i nie może rzucać.
        }
    }
}
