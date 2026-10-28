using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using TextFolding = DSOptimizer.Core.Gui.SearchText;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Strona „Aplikacje” (wariant B: przełącznik grup z licznikami nad listą). Odczyt i nazwy kont poza wątkiem UI
/// (<see cref="AppsSession.Load"/> + <see cref="IAccountNames"/> w Task.Run), własne zaznaczenie i pasek „Usuń (N)”, który
/// zawsze idzie przez okno serii (<c>MainViewModel</c>). Sugerowane (<see cref="AppGroup.Suggested"/> + usuwalne) zaznaczają się
/// raz na Id aplikacji w tym uruchomieniu (<see cref="AppSuggestionTracker"/>); F5 i ponowne wejście nie zaznaczają ich ponownie.
/// </summary>
public sealed class AppsViewModel : ObservableObject
{
    private static readonly char[] WordSeparators = { ' ' };
    private static StringComparer NameOrder => StringComparer.Create(L.Culture, true);

    private readonly AppsSession _session;
    private readonly IAccountNames _names;
    private readonly Func<IReadOnlyList<AppRow>, Task> _remove;
    private readonly Func<bool> _restorePointSetting;
    private readonly Action<GuiLogLevel, string> _log;
    private readonly IClock _clock;
    private readonly HashSet<string> _loggedWarnings = new(StringComparer.Ordinal);
    private IReadOnlyList<AppRowViewModel> _rows = Array.Empty<AppRowViewModel>();
    private IReadOnlyList<AppRowViewModel> _visibleRows = Array.Empty<AppRowViewModel>();
    private AppRowViewModel? _selectedRow;
    private AppGroup _selectedGroup = AppGroup.Suggested;
    private string _searchText = string.Empty;
    private DateTime? _readAt;
    private bool _isLoading;
    private bool _isBusy;
    private readonly AppSuggestionTracker _suggestions = new();
    private bool _rebuilding;
    private int _selectedCount;
    private int _version;

    /// <param name="session">Odczyt, klasyfikacja i seria usuwania (wołane tylko przez Task.Run).</param>
    /// <param name="names">SID → nazwa konta (LSA może blokować – tylko poza wątkiem UI).</param>
    /// <param name="remove">„Usuń (N)”: okno serii, potwierdzenie, postęp i wynik (MainViewModel).</param>
    /// <param name="restorePointSetting">Przełącznik punktu przywracania z Ustawień (druga linia paska).</param>
    /// <param name="log">Linia do dziennika w oknie.</param>
    /// <param name="clock">Czas odczytu listy w podtytule.</param>
    public AppsViewModel(
        AppsSession session,
        IAccountNames names,
        Func<IReadOnlyList<AppRow>, Task> remove,
        Func<bool> restorePointSetting,
        Action<GuiLogLevel, string> log,
        IClock clock)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _names = names ?? throw new ArgumentNullException(nameof(names));
        _remove = remove ?? throw new ArgumentNullException(nameof(remove));
        _restorePointSetting = restorePointSetting ?? throw new ArgumentNullException(nameof(restorePointSetting));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Groups = new[] { AppGroup.Suggested, AppGroup.Other, AppGroup.Protected }.Select(group => new AppGroupOption(group)).ToList();
        Groups[0].IsSelected = true;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && !IsLoading);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => !IsBusy && !IsLoading && SelectedCount > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy && SelectedCount > 0);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        SelectGroupCommand = new RelayCommand<AppGroupOption>(option =>
        {
            if (option is not null)
            {
                SelectedGroup = option.Value;
            }
        });
    }

    public string Name { get; } = AppsPageText.PageTitle;

    /// <summary>Rodzaj ikony w menu (K1, klucz z <c>Themes/Icons.xaml</c>).</summary>
    public string IconKind { get; } = GuiIcons.Apps;

    /// <summary>Licznik w menu: liczba aplikacji na liście; pusty przed pierwszym odczytem.</summary>
    public string CounterText => _readAt is null ? string.Empty : _rows.Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Pełny opis licznika (podpowiedź licznika, czytnik ekranu): „34 aplikacje”; pusty przed pierwszym odczytem.</summary>
    public string CounterDescription => _readAt is null ? string.Empty : GuiText.AppsCounterDescription(_rows.Count);

    public string NavToolTip => _readAt is null ? Name : GuiText.CategoryNavToolTip(Name, CounterText);

    public string SearchPlaceholder { get; } = AppsPageText.SearchPlaceholder;

    /// <summary>Podtytuł strony: odczyt w toku, czas i liczba z ostatniego odczytu albo „jeszcze nie odczytana”.</summary>
    public string Subtitle => IsLoading
        ? AppsPageText.Reading
        : _readAt is { } at ? AppsPageText.ReadSummary(at, _rows.Count) : AppsPageText.NotRead;

    public IReadOnlyList<AppGroupOption> Groups { get; }

    /// <summary>Grupa pokazywana na liście (start: „Sugerowane do usunięcia”).</summary>
    public AppGroup SelectedGroup
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

    /// <summary>Fraza wyszukiwarki strony (nazwa i wydawca, bez polskich znaków); działa tylko na tej stronie.</summary>
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

    /// <summary>Wiersze wybranej grupy pasujące do frazy, alfabetycznie.</summary>
    public IReadOnlyList<AppRowViewModel> VisibleRows
    {
        get => _visibleRows;
        private set => SetProperty(ref _visibleRows, value);
    }

    /// <summary>Wiersz w panelu szczegółów; null = panel zwinięty.</summary>
    public AppRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    /// <summary>Linia w miejscu pustej listy (po odczycie): pusta grupa albo brak dopasowań.</summary>
    public string EmptyText => _readAt is null || _visibleRows.Count > 0
        ? string.Empty
        : TextFolding.Fold(_searchText).Length > 0 ? AppsPageText.EmptySearch : AppsPageText.EmptyGroup;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(Subtitle));
                RefreshCommand.NotifyCanExecuteChanged();
                RemoveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Operacja w głównym oknie (ustawia <c>MainViewModel</c>): odświeżanie i „Usuń” nieaktywne.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                RemoveCommand.NotifyCanExecuteChanged();
                ClearSelectionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set => SetProperty(ref _selectedCount, value);
    }

    public string SelectionBarTitle => AppsPageText.SelectionBarTitle(SelectedCount);

    public string SelectionBarDetails => SelectedCount == 0
        ? string.Empty
        : AppsPageText.SelectionBarDetails(_rows.Any(row => row.IsSelected && row.Row.App.Kind == AppKind.Program), _restorePointSetting());

    public string RemoveText => $"Usuń ({SelectedCount})";

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand RemoveCommand { get; }

    public IRelayCommand ClearSelectionCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    public IRelayCommand<AppGroupOption> SelectGroupCommand { get; }

    /// <summary>
    /// Świeży odczyt listy (wejście na stronę, F5, „Odśwież listę”, po serii). Wynik starszego odczytu jest pomijany.
    /// Zaznaczenie i wiersz panelu wracają po Id. Ostrzeżenia odczytu trafiają do dziennika (każdy tekst raz na sesję).
    /// Nigdy nie rzuca: błąd = jedna linia Error, poprzednia lista zostaje.
    /// </summary>
    public async Task RefreshAsync()
    {
        var version = ++_version;
        try
        {
            IsLoading = true;
            var loaded = await Task.Run(LoadWithNames).ConfigureAwait(true);
            foreach (var warning in loaded.Page.Warnings.Where(_loggedWarnings.Add))
            {
                _log(GuiLogLevel.Warning, warning);
            }

            if (version != _version)
            {
                return;
            }

            Rebuild(loaded);
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
            }
        }
    }

    /// <summary>Przelicza licznik i pasek serii (zmiana zaznaczenia albo ustawienia punktu przywracania).</summary>
    public void RefreshSelection()
    {
        if (_rebuilding)
        {
            return;
        }

        SelectedCount = _rows.Count(row => row.IsSelected);
        OnPropertyChanged(nameof(SelectionBarTitle));
        OnPropertyChanged(nameof(SelectionBarDetails));
        OnPropertyChanged(nameof(RemoveText));
        RemoveCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private Loaded LoadWithNames()
    {
        var page = _session.Load();
        var sids = page.Rows
            .Where(row => row.App.Scope == AppScope.User && row.App.OwnerSid is not null)
            .Select(row => row.App.OwnerSid!)
            .Append(_session.UserSid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in sids)
        {
            names[sid] = _names.NameOf(sid);
        }

        return new Loaded(page, names, _clock.UtcNow.ToLocalTime().DateTime);
    }

    private void Rebuild(Loaded loaded)
    {
        var selected = new HashSet<string>(_rows.Where(row => row.IsSelected).Select(row => row.Id), StringComparer.Ordinal);
        var keep = _selectedRow?.Id;
        var userName = loaded.Names.TryGetValue(_session.UserSid, out var user) ? user : null;
        var scope = _session.StoreScope;
        // Sugestie raz na Id aplikacji w tym uruchomieniu (R16); reszta zaznaczenia sprzed odświeżenia.
        var suggested = new HashSet<string>(_suggestions.TakeNew(loaded.Page.Rows), StringComparer.Ordinal);
        _rebuilding = true;
        try
        {
            _rows = loaded.Page.Rows
                .Select(row => new AppRowViewModel(
                    row,
                    row.App.OwnerSid is { } owner && loaded.Names.TryGetValue(owner, out var name) ? name : null,
                    userName,
                    scope,
                    RefreshSelection))
                .OrderBy(row => row.Name, NameOrder)
                .ToList();
            foreach (var row in _rows)
            {
                row.IsSelected = selected.Contains(row.Id) || suggested.Contains(row.Id);
            }
        }
        finally
        {
            _rebuilding = false;
        }

        _readAt = loaded.At;
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(CounterDescription));
        OnPropertyChanged(nameof(NavToolTip));
        OnPropertyChanged(nameof(Subtitle));
        ApplyFilter(keep);
        RefreshSelection();
    }

    private void ApplyFilter() => ApplyFilter(_selectedRow?.Id);

    private void ApplyFilter(string? keep)
    {
        var query = TextFolding.Fold(_searchText);
        var words = query.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        var matching = _rows.Where(row => words.All(word => row.SearchKey.IndexOf(word, StringComparison.Ordinal) >= 0)).ToList();
        foreach (var option in Groups)
        {
            option.Count = matching.Count(row => row.Group == option.Value);
        }

        var visible = matching.Where(row => row.Group == _selectedGroup).ToList();
        VisibleRows = visible;
        SelectedRow = visible.FirstOrDefault(row => row.Id == keep) ?? visible.FirstOrDefault();
        OnPropertyChanged(nameof(EmptyText));
    }

    private async Task RemoveAsync()
    {
        var rows = _rows.Where(row => row.IsSelected && row.IsSelectable).Select(row => row.Row).ToList();
        if (rows.Count == 0 || IsBusy || IsLoading)
        {
            return;
        }

        try
        {
            await _remove(rows).ConfigureAwait(true);
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

    private void Report(Exception ex)
    {
        try
        {
            _log(GuiLogLevel.Error, $"Nieoczekiwany błąd: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception)
        {
            // Dziennik niedostępny – RefreshAsync bywa wołane bez await i nie może rzucać.
        }
    }

    private sealed record Loaded(AppsPage Page, IReadOnlyDictionary<string, string?> Names, DateTime At);
}
