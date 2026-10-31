using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using InstallStrings = DSOptimizer.Core.Install.InstallText;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Strona „Instalacja” (wariant A, ruling R5): kategorie z licznikami po lewej – na górze „Wszystkie (N)”, wybrane przy każdym wejściu
/// na stronę (D1, Ruling P3), pod nimi „Zaznaczone (N)” – i jedno pole wyszukiwania w górnym pasku strony (spec poprawek 2026-10-25,
/// pkt 3–4). Każdy znak od razu filtruje listę (<see cref="InstallSearch"/>, bez winget i bez sieci): niepuste znormalizowane zapytanie
/// = tryb wyszukiwania – sekcja „Z listy (N)” z nagłówkami kategorii, a po Enterze albo „Szukaj też w winget” pod nią sekcja
/// „W winget (N)” z wynikami <see cref="InstallSession.Search"/> dla bieżącego tekstu (wynik dla innego tekstu jest odrzucany).
/// Niedziałający winget (brak App Installera, <c>--version</c>) = komunikat nad listą, filtr działa dalej, a RustDesk z GitHuba jest
/// do zaznaczenia (decyzja planu 1); tylko błąd odczytu listy zainstalowanych zastępuje listę komunikatem. Odczyt
/// (<see cref="InstallSession.Load"/>) i wyszukiwanie w winget poza wątkiem UI. Własne zaznaczenie (ze wszystkich kategorii i wyników
/// winget; przetrwa zmianę kategorii, zmianę tekstu, nowe wyszukiwanie i odświeżenie) i pasek „Zainstaluj (N)”, który zawsze idzie
/// przez okno serii (<c>MainViewModel</c>). Nic nie zaznacza się samo (bez presetów, decyzja autora 2026-10-15).
/// Load biegnie poza blokadą <see cref="GuiSession"/>, więc w trakcie operacji (<see cref="IsBusy"/>) odświeżenie czeka na jej koniec.
/// </summary>
public sealed class InstallViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> NoIds = Array.Empty<string>();

    private readonly IInstallPageSession _session;
    private readonly Func<IReadOnlyList<InstallTarget>, Task> _install;
    private readonly Action _showRepairWinget;
    private readonly Action<GuiLogLevel, string> _log;
    private readonly IClock _clock;
    private readonly HashSet<string> _loggedWarnings = new(StringComparer.Ordinal);

    /// <summary>Grupa wierszy winget (licznik grup w pasku serii); nie ma jej na liście kategorii.</summary>
    private readonly InstallCategoryOption _wingetGroup = new(InstallGroup.Search);
    private IReadOnlyList<InstallRowViewModel> _catalogRows = Array.Empty<InstallRowViewModel>();

    /// <summary>Wyniki winget bieżącego tekstu (sekcja „W winget”); zmiana tekstu je czyści.</summary>
    private IReadOnlyList<InstallRowViewModel> _searchRows = Array.Empty<InstallRowViewModel>();

    /// <summary>Id pozycji listy trafionych przez winget dla bieżącego tekstu (<see cref="InstallSearchResult.OnListIds"/>).</summary>
    private IReadOnlyList<string> _onListIds = NoIds;

    /// <summary>Zaznaczone wyniki wcześniejszych wyszukiwań, których nie ma w bieżących wynikach (po <c>PackageKey</c>, spec 11.5).</summary>
    private Dictionary<string, InstallRowViewModel> _heldSearch = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<InstallRowViewModel> _visibleRows = Array.Empty<InstallRowViewModel>();
    private IReadOnlyList<object> _visibleItems = Array.Empty<object>();

    /// <summary>Nagłówki widoku „Zaznaczone” z wierszami pod nimi: licznik idzie za zaznaczeniem, wiersze zostają (F9).</summary>
    private readonly List<(InstallListLine Line, IReadOnlyList<InstallRowViewModel> Rows, bool IsWinget)> _selectionHeaders = new();
    private InstallRowViewModel? _selectedRow;
    private InstallCategoryOption _selectedGroup;
    private InstallPageState? _state;
    private DateTimeOffset? _readAt;
    private string _searchText = string.Empty;
    private string? _searchMessage;
    private bool _hasWingetSection;

    /// <summary>Grupa wybrana przy niepustym polu wygrywa z filtrem aż do kolejnej zmiany tekstu (spec 11.1).</summary>
    private bool _groupOverride;
    private bool _isLoading;
    private bool _isBusy;
    private bool _isSearching;
    private bool _refreshPending;
    private bool _rebuilding;
    private int _selectedCount;
    private int _version;

    /// <summary>Numer tekstu pola: każda zmiana tekstu go zwiększa, a wynik winget dla starszego numeru jest odrzucany.</summary>
    private int _textVersion;
    private int _searchingVersion = -1;

    /// <param name="session">Odczyt, wyszukiwanie i seria instalacji (wołane tylko przez Task.Run).</param>
    /// <param name="install">„Zainstaluj (N)”: okno serii, potwierdzenie, postęp i wynik (MainViewModel).</param>
    /// <param name="showRepairWinget">„Przejdź do: Napraw winget”: kategoria „Naprawy”, pozycja <c>repair.winget</c> w panelu.</param>
    /// <param name="log">Linia do dziennika w oknie.</param>
    /// <param name="clock">Czas odczytu listy (licznik w menu dopiero po pierwszym odczycie).</param>
    public InstallViewModel(
        IInstallPageSession session,
        Func<IReadOnlyList<InstallTarget>, Task> install,
        Action showRepairWinget,
        Action<GuiLogLevel, string> log,
        IClock clock)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _install = install ?? throw new ArgumentNullException(nameof(install));
        _showRepairWinget = showRepairWinget ?? throw new ArgumentNullException(nameof(showRepairWinget));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Categories = InstallPage.Groups(session.Catalog).Select(group => new InstallCategoryOption(group)).ToList();
        AllGroup = Categories[0];
        SelectedItemsGroup = Categories.First(option => option.IsSelection);
        SelectedItemsGroup.Count = 0;
        _selectedGroup = AllGroup;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && !IsLoading && !IsSearching);
        SearchCommand = new AsyncRelayCommand(SearchAsync, CanSearch);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => CanInstall);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy && SelectedCount > 0);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        RepairWingetCommand = new RelayCommand(_showRepairWinget);
        ShowRustDeskCommand = new RelayCommand(ShowRustDesk, () => CanShowRustDesk);
        ShowSelectedCommand = new RelayCommand(ShowSelected);
    }

    public string Name { get; } = InstallStrings.PageTitle;

    /// <summary>Rodzaj ikony w menu (I2, klucz z <c>Themes/Icons.xaml</c>).</summary>
    public string IconKind { get; } = GuiIcons.Install;

    /// <summary>Licznik w menu: liczba pozycji listy; pusty przed pierwszym odczytem.</summary>
    public string CounterText => _readAt is null ? string.Empty : _catalogRows.Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Pełny opis licznika (podpowiedź licznika, czytnik ekranu): „35 aplikacji na liście”; pusty przed pierwszym odczytem.</summary>
    public string CounterDescription => _readAt is null ? string.Empty : GuiText.InstallCounterDescription(_catalogRows.Count);

    public string NavToolTip => _readAt is null ? Name : GuiText.CategoryNavToolTip(Name, CounterText);

    /// <summary>
    /// Tekst zastępczy pola wyszukiwania (pole aktywne zawsze przy odczytanej liście): przy działającym winget (i przed pierwszym
    /// odczytem) <see cref="InstallText.SearchPlaceholder"/>, inaczej <see cref="InstallText.SearchPlaceholderListOnly"/>.
    /// </summary>
    public string SearchPlaceholder => _state is null || IsWingetReady ? InstallStrings.SearchPlaceholder : InstallStrings.SearchPlaceholderListOnly;

    /// <summary>
    /// Podtytuł: odczyt w toku, źródła z liczbą pozycji i zainstalowanych (z GitHubem, gdy lista ma RustDesk), przy niedziałającym
    /// winget – GitHub dostępny, winget nie (M6), a „niedostępne” tylko przy błędzie odczytu albo bez pozycji z GitHuba.
    /// </summary>
    public string Subtitle
    {
        get
        {
            if (IsLoading)
            {
                return InstallStrings.Reading;
            }

            if (_state is null)
            {
                return InstallStrings.NotRead;
            }

            var withGitHub = _catalogRows.Any(row => row.Target.Origin == InstallOrigin.GitHub);
            var total = _catalogRows.Count;
            var installed = _catalogRows.Count(row => row.IsInstalled);
            if (ShowsProblem || (HasWingetNotice && !withGitHub))
            {
                return InstallStrings.PageUnavailable;
            }

            return HasWingetNotice
                ? InstallStrings.PageWingetUnavailable(total, installed)
                : InstallStrings.PageSummary(total, installed, withGitHub);
        }
    }

    /// <summary>
    /// Lista kategorii (<see cref="InstallPage.Groups"/>): „Wszystkie”, „Zaznaczone”, potem kategorie w kolejności UI
    /// (<see cref="InstallCategory"/>), tylko te, które mają pozycje.
    /// </summary>
    public IReadOnlyList<InstallCategoryOption> Categories { get; }

    /// <summary>„Wszystkie” – pierwsza pozycja <see cref="Categories"/>, wybierana przy każdym wejściu na stronę (<see cref="EnterAsync"/>).</summary>
    public InstallCategoryOption AllGroup { get; }

    /// <summary>„Zaznaczone (N)” – druga pozycja <see cref="Categories"/>; licznik = <see cref="SelectedCount"/>, także 0 (spec 4).</summary>
    public InstallCategoryOption SelectedItemsGroup { get; }

    /// <summary>
    /// Wybrana grupa (start i każde wejście na stronę: „Wszystkie”). Wybór przy niepustym polu: „Zaznaczone” pokazuje zaznaczone (pole
    /// zostaje, aż do kolejnej zmiany tekstu – spec 11.1), inna grupa czyści pole (Ruling R3); po wyczyszczeniu pola lista wraca do tej grupy.
    /// </summary>
    public InstallCategoryOption SelectedGroup
    {
        get => _selectedGroup;
        set => SelectGroup(value, explicitChoice: true);
    }

    /// <summary>Zaznaczenie listy kategorii: <see cref="SelectedGroup"/>, a w trybie wyszukiwania nic (lista nie pokazuje wtedy grupy).</summary>
    public InstallCategoryOption? SelectedCategory
    {
        get => IsSearchView ? null : _selectedGroup;
        set
        {
            if (value is not null)
            {
                SelectGroup(value, explicitChoice: true);
            }
        }
    }

    /// <summary>Tryb wyszukiwania: zapytanie po normalizacji niepuste i grupa nie wybrana po wpisaniu tekstu.</summary>
    public bool IsSearchView => !_groupOverride && HasQuery;

    /// <summary>
    /// Lista ma własne nagłówki (sekcje wyszukiwania, kategorie w „Wszystkie” i „Zaznaczone”), więc nagłówek grupy nad listą znika.
    /// </summary>
    public bool ShowsCategoryHeaders => IsSearchView || _selectedGroup.Group.ShowsCategoryHeaders;

    /// <summary>
    /// Wiersze aplikacji w kolejności widoku: wyniki filtra (sekcja „Z listy”, potem „W winget”), „Wszystkie” w kolejności kategorii,
    /// kategoria w kolejności pliku albo zaznaczone (lista, potem winget).
    /// </summary>
    public IReadOnlyList<InstallRowViewModel> VisibleRows
    {
        get => _visibleRows;
        private set => SetProperty(ref _visibleRows, value);
    }

    /// <summary>
    /// To, co pokazuje lista: <see cref="VisibleRows"/> przeplecione liniami <see cref="InstallListLine"/> (nagłówki sekcji i kategorii,
    /// notatki, komunikat winget). Płaska lista zamiast grup widoku kolekcji – sekcja „W winget” bywa bez wierszy (sam komunikat).
    /// </summary>
    public IReadOnlyList<object> VisibleItems
    {
        get => _visibleItems;
        private set => SetProperty(ref _visibleItems, value);
    }

    /// <summary>Wyniki winget bieżącego tekstu (sekcja „W winget”); puste przed wyszukiwaniem i po każdej zmianie tekstu.</summary>
    public IReadOnlyList<InstallRowViewModel> WingetResults => _searchRows;

    /// <summary>Wiersz podświetlony na liście (bez panelu szczegółów).</summary>
    public InstallRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    /// <summary>
    /// Linia w miejscu pustej listy: w trybie wyszukiwania bez dopasowań <see cref="InstallText.NoCatalogMatch"/> (winget gotowy),
    /// <see cref="InstallText.NoCatalogMatchWingetDown"/> (winget nie działa) albo <see cref="InstallText.NoCatalogMatchPlain"/>
    /// (odmowa konta – winget działa, tylko nie na tym koncie; Ruling R4), pusta grupa „Zaznaczone” – <see cref="InstallText.SelectedEmpty"/>.
    /// </summary>
    public string EmptyText
    {
        get
        {
            if (_visibleItems.Count > 0 || _state is null || ShowsProblem)
            {
                return string.Empty;
            }

            if (IsSearchView)
            {
                return IsWingetReady ? InstallStrings.NoCatalogMatch
                    : HasWingetNotice ? InstallStrings.NoCatalogMatchWingetDown
                    : InstallStrings.NoCatalogMatchPlain;
            }

            return _selectedGroup.IsSelection ? InstallStrings.SelectedEmpty : string.Empty;
        }
    }

    /// <summary>
    /// Tekst pola: każda zmiana od razu filtruje listę, czyści sekcję „W winget” (zaznaczone wyniki zostają w serii) i wraca do
    /// wyników filtra; winget rusza dopiero po Enterze albo „Szukaj też w winget”.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            _textVersion++;
            DropWingetSection();
            _groupOverride = false;
            OnViewModeChanged();
            ApplyView();
            SearchCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Komunikat wyszukiwania winget w jego sekcji (brak sieci, nic nie znaleziono, błąd, niepoprawne zapytanie); null = brak.</summary>
    public string? SearchMessage
    {
        get => _searchMessage;
        private set
        {
            if (SetProperty(ref _searchMessage, value))
            {
                OnPropertyChanged(nameof(HasSearchMessage));
                OnPropertyChanged(nameof(SearchMessageIsWarning));
            }
        }
    }

    public bool HasSearchMessage => !string.IsNullOrEmpty(_searchMessage);

    /// <summary>Trójkąt ostrzeżenia przy komunikacie – poza „nic nie znaleziono” i „wszystko jest już na liście”.</summary>
    public bool SearchMessageIsWarning => HasSearchMessage
        && !string.Equals(_searchMessage, InstallStrings.SearchNoResults, StringComparison.Ordinal)
        && !string.Equals(_searchMessage, InstallStrings.SearchAllOnList, StringComparison.Ordinal);

    /// <summary>
    /// Nie udało się odczytać listy zainstalowanych: komunikat zamiast listy (bez odczytu nie ma statusu „Zainstalowana” ani pól
    /// wyboru). Niedziałający winget listy nie zastępuje – <see cref="HasWingetNotice"/> (decyzja planu 1).
    /// </summary>
    public bool ShowsProblem => _state is { ListReadFailed: true };

    /// <summary>Lista (kategorie, wyszukiwanie, wiersze, pasek serii) – zawsze poza <see cref="ShowsProblem"/>.</summary>
    public bool HasList => !ShowsProblem;

    /// <summary>Tekst błędu odczytu listy (Core) w miejscu listy; poza <see cref="ShowsProblem"/> = null.</summary>
    public string? WingetMessage => ShowsProblem ? _state!.Winget.Text : null;

    /// <summary>Winget nie działa (brak App Installera, <c>--version</c>) przy odczytanej liście: komunikat nad listą (wzór <see cref="AccountNotice"/>).</summary>
    public bool HasWingetNotice => _state is { ListReadFailed: false } state
        && state.Winget.State is WingetState.NotRegistered or WingetState.NotWorking;

    /// <summary>Nagłówek komunikatu nad listą.</summary>
    public string WingetNoticeTitle { get; } = InstallStrings.WingetProblemTitle;

    /// <summary>Tekst stanu winget (Core) w komunikacie nad listą; null bez komunikatu.</summary>
    public string? WingetNotice => HasWingetNotice ? _state!.Winget.Text : null;

    /// <summary>
    /// Dopisek komunikatu, gdy lista ma pozycję z GitHuba (RustDesk) – ta rusza bez winget; tylko gdy RustDesk da się zaznaczyć
    /// (nie gdy już zainstalowany – M2).
    /// </summary>
    public string? GitHubStillAvailable => HasWingetNotice && RustDeskRow() is not null
        ? InstallStrings.GitHubStillAvailable
        : null;

    public bool HasGitHubStillAvailable => GitHubStillAvailable is not null;

    /// <summary>Link „Pokaż RustDesk” w komunikacie nad listą (M5): tylko gdy winget nie działa, a RustDesk da się zaznaczyć.</summary>
    public bool CanShowRustDesk => HasWingetNotice && RustDeskRow() is not null;

    public string ShowRustDeskText { get; } = InstallStrings.ShowRustDeskLink;

    /// <summary>Prośba do widoku: przewiń do wiersza i ustaw na nim fokus (po „Pokaż RustDesk”).</summary>
    public event EventHandler<InstallRowViewModel>? RowRevealRequested;

    /// <summary>„Przejdź do: Napraw winget” w komunikacie nad listą (NotRegistered/NotWorking; błąd odczytu listy to nie usterka winget).</summary>
    public bool ShowRepairWinget => HasWingetNotice;

    /// <summary>Odmowa konta nad listą (inne konto, Administrator Protection, niepewny użytkownik); lista bez pól wyboru.</summary>
    public string? AccountNotice => _state is { ListReadFailed: false } state
        && state.Winget.State is WingetState.OtherAccount or WingetState.UserUncertain
            ? state.Winget.Text
            : null;

    public bool HasAccountNotice => AccountNotice is not null;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(Subtitle));
                OnCommandsChanged();
            }
        }
    }

    /// <summary>
    /// Operacja w głównym oknie (ustawia <c>MainViewModel</c>): odświeżanie, wyszukiwanie i „Zainstaluj” nieaktywne. Odświeżenie
    /// zlecone w trakcie (wejście na stronę, koniec serii) rusza po jej końcu.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnCommandsChanged();
                if (!value && _refreshPending)
                {
                    _refreshPending = false;
                    _ = RefreshAsync();
                }
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                OnPropertyChanged(nameof(EmptyText));
                OnCommandsChanged();
            }
        }
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set => SetProperty(ref _selectedCount, value);
    }

    /// <summary>Coś jest zaznaczone: napis paska serii „Zaznaczono N …” jest wtedy linkiem do „Zaznaczone” (przy 0 – podpowiedź).</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>Nic nie jest zaznaczone: zamiast linku zwykła podpowiedź <see cref="InstallText.SelectionBarTitle"/>(0).</summary>
    public bool ShowsSelectionHint => !HasSelection;

    /// <summary>Przycisk serii „Zainstaluj (N)”; N ze wszystkich kategorii i wyników winget (także spoza bieżącego tekstu).</summary>
    public string InstallText => InstallStrings.InstallButton(SelectedCount);

    public string SelectionBarTitle => InstallStrings.SelectionBarTitle(SelectedCount);

    public string SelectionBarDetails
    {
        get
        {
            var selected = SelectedRows().ToList();
            return InstallStrings.SelectionBarDetails(
                selected.Select(row => row.Group).Distinct().Count(),
                selected.Any(row => row.Target.Origin == InstallOrigin.Winget),
                selected.Any(row => row.Target.Origin == InstallOrigin.GitHub));
        }
    }

    /// <summary>
    /// „Zainstaluj (N)” aktywne: coś zaznaczone, nic nie trwa, lista odczytana i konto procesu w porządku (odmowa konta blokuje także
    /// RustDesk, decyzja planu 2). Niedziałający winget nie blokuje – zaznaczyć da się wtedy tylko pozycję z GitHuba (Core), a wyniki wyszukiwania wypadają z serii w odczycie (Rebuild).
    /// </summary>
    public bool CanInstall => !IsBusy && !IsLoading && !IsSearching && SelectedCount > 0 && IsAccountUsable;

    private bool IsAccountUsable => _state is { ListReadFailed: false } state
        && state.Winget.State is not (WingetState.OtherAccount or WingetState.UserUncertain);

    private bool IsWingetReady => _state is { ListReadFailed: false } state && state.Winget.State == WingetState.Ready;

    /// <summary>Pole niepuste po normalizacji (same spacje, myślniki, kropki i podkreślenia to brak zapytania – Review Focus 1).</summary>
    private bool HasQuery => InstallSearch.Normalize(_searchText).Length > 0;

    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>„Szukaj też w winget” i Enter: winget gotowy, pole niepuste, nic nie trwa.</summary>
    public IAsyncRelayCommand SearchCommand { get; }

    public IAsyncRelayCommand InstallCommand { get; }

    public IRelayCommand ClearSelectionCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    public IRelayCommand RepairWingetCommand { get; }

    /// <summary>„Pokaż RustDesk”: czyści pole, wybiera kategorię pozycji z GitHuba, podświetla jej wiersz i <see cref="RowRevealRequested"/>; nic nie zaznacza.</summary>
    public IRelayCommand ShowRustDeskCommand { get; }

    /// <summary>
    /// Link „Zaznaczono N …” w pasku serii: wybiera „Zaznaczone” (także przy niepustym polu – spec 11.1); gdy „Zaznaczone” już jest
    /// pokazane, odświeża widok (odznaczone wiersze znikają).
    /// </summary>
    public IRelayCommand ShowSelectedCommand { get; }

    /// <summary>
    /// Świeży odczyt listy i stanu winget (wejście na stronę, F5, „Odśwież”, po serii). W trakcie operacji tylko zapamiętuje prośbę
    /// (Load biegnie poza blokadą sesji). Wynik starszego odczytu jest pomijany, zaznaczenie wraca po Id pozycji. Ostrzeżenia odczytu
    /// trafiają do dziennika (każdy tekst raz na sesję). Nigdy nie rzuca: błąd = jedna linia Error, poprzednia lista zostaje.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            _refreshPending = true;
            return;
        }

        var version = ++_version;
        try
        {
            IsLoading = true;
            var state = await Task.Run(_session.Load).ConfigureAwait(true);
            foreach (var warning in state.Warnings.Where(_loggedWarnings.Add))
            {
                _log(GuiLogLevel.Warning, warning);
            }

            if (version != _version)
            {
                return;
            }

            Rebuild(state);
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

    /// <summary>
    /// Wejście na stronę (menu): wybrane „Wszystkie” (D1) i świeży odczyt (<see cref="RefreshAsync"/>). Zaznaczenie i tekst pola
    /// zostają (niepuste pole = dalej wyniki filtra).
    /// </summary>
    public Task EnterAsync()
    {
        SelectGroup(AllGroup, explicitChoice: false);
        return RefreshAsync();
    }

    /// <summary>Po serii: odznacza pozycje, które są już na komputerze (Id z <see cref="GuiOperationReport.SucceededIds"/>).</summary>
    public void OnInstalled(IReadOnlyCollection<string> ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return;
        }

        var done = new HashSet<string>(ids, StringComparer.Ordinal);
        _rebuilding = true;
        try
        {
            foreach (var row in SelectedRows().Where(row => done.Contains(row.Target.Id)).ToList())
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

    /// <summary>Przelicza licznik, „Zaznaczone (N)” i pasek serii (zmiana zaznaczenia). Lista się nie przebudowuje.</summary>
    public void RefreshSelection()
    {
        if (_rebuilding)
        {
            return;
        }

        SelectedCount = SelectedRows().Count();
        SelectedItemsGroup.Count = SelectedCount;
        UpdateSelectionHeaders();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowsSelectionHint));
        OnPropertyChanged(nameof(InstallText));
        OnPropertyChanged(nameof(SelectionBarTitle));
        OnPropertyChanged(nameof(SelectionBarDetails));
        OnCommandsChanged();
    }

    private IEnumerable<InstallRowViewModel> SelectedRows()
        => _catalogRows.Where(row => row.IsSelected).Concat(SelectedWingetRows());

    /// <summary>Zaznaczone wyniki winget: bieżące, potem trzymane z wcześniejszych tekstów (<see cref="_heldSearch"/>).</summary>
    private IEnumerable<InstallRowViewModel> SelectedWingetRows()
        => _searchRows.Concat(_heldSearch.Values).Where(row => row.IsSelected);

    private void Rebuild(InstallPageState state)
    {
        var selected = new HashSet<string>(_catalogRows.Where(row => row.IsSelected).Select(row => row.Target.Id), StringComparer.Ordinal);
        var catalog = _session.Catalog;
        _rebuilding = true;
        try
        {
            _catalogRows = state.Rows
                .Select(row => new InstallRowViewModel(row, catalog.Find(row.Target.Id)?.Summary, GroupOf(row), RefreshSelection))
                .ToList();
            foreach (var row in _catalogRows)
            {
                // Setter pomija wiersz bez pola wyboru (zainstalowana, winget niegotowy, inne konto).
                row.IsSelected = selected.Contains(row.Target.Id);
            }

            if (state.ListReadFailed || state.Winget.State != WingetState.Ready)
            {
                // I1: wyniki wyszukiwania to pozycje winget – przy winget, który nie jest gotowy, wypadają z serii i tracą pole wyboru
                // (zaznaczenie i „Zainstaluj (N)” tylko z pozycji, które mogą ruszyć).
                _searchRows = _searchRows
                    .Select(row => row.IsSelectable ? new InstallRowViewModel(row.Row with { CanSelect = false }, row.Summary, row.Group, RefreshSelection) : row)
                    .ToList();
                _heldSearch.Clear();
            }
        }
        finally
        {
            _rebuilding = false;
        }

        _state = state;
        _readAt = _clock.UtcNow;
        foreach (var option in Categories.Where(option => !option.IsSelection))
        {
            option.Count = _catalogRows.Count(row => option.Group.Contains(row.Row));
        }

        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(CounterDescription));
        OnPropertyChanged(nameof(NavToolTip));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ShowsProblem));
        OnPropertyChanged(nameof(HasList));
        OnPropertyChanged(nameof(WingetMessage));
        OnPropertyChanged(nameof(HasWingetNotice));
        OnPropertyChanged(nameof(WingetNotice));
        OnPropertyChanged(nameof(GitHubStillAvailable));
        OnPropertyChanged(nameof(HasGitHubStillAvailable));
        OnPropertyChanged(nameof(ShowRepairWinget));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(CanShowRustDesk));
        ShowRustDeskCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(AccountNotice));
        OnPropertyChanged(nameof(HasAccountNotice));
        ApplyView();
        RefreshSelection();
    }

    /// <summary>Wiersz pozycji z GitHuba do pokazania (<see cref="InstallPage.GitHubRowToShow"/>) albo null.</summary>
    private InstallRowViewModel? RustDeskRow()
        => InstallPage.GitHubRowToShow(_catalogRows.Select(row => row.Row).ToList()) is { } shown
            ? _catalogRows.First(row => ReferenceEquals(row.Row, shown))
            : null;

    private void ShowRustDesk()
    {
        if (RustDeskRow() is not { } row)
        {
            return;
        }

        SearchText = string.Empty;
        SelectGroup(row.Group, explicitChoice: true);
        SelectedRow = row;
        RowRevealRequested?.Invoke(this, row);
    }

    /// <summary>Link paska serii: „Zaznaczone”, a gdy już są pokazane – świeży widok bez odznaczonych wierszy (F9).</summary>
    private void ShowSelected()
    {
        var alreadyShown = _selectedGroup.IsSelection && !IsSearchView;
        SelectGroup(SelectedItemsGroup, explicitChoice: true);
        if (alreadyShown)
        {
            ApplyView();
        }
    }

    /// <summary>Grupa wiersza (licznik grup w pasku serii): jego kategoria, nigdy „Wszystkie” ani „Zaznaczone”; bez kategorii – grupa winget.</summary>
    private InstallCategoryOption GroupOf(InstallRow row)
        => row.Target.Category is { } category
            ? Categories.FirstOrDefault(option => option.Category == category) ?? _wingetGroup
            : _wingetGroup;

    /// <summary>
    /// Wybór grupy. <paramref name="explicitChoice"/> (klik, link, „Pokaż RustDesk”) przy niepustym polu (Ruling R3, spec 3 + 11.1):
    /// „Zaznaczone” pokazuje wszystkie zaznaczone, a pole zostaje (kolejny znak wraca do wyników filtra); każda inna grupa (także
    /// „Wszystkie”) czyści pole i pokazuje tę grupę – nigdy tekst w polu, który nie filtruje. Wejście na stronę nie jest wyborem:
    /// niepuste pole dalej filtruje.
    /// </summary>
    private void SelectGroup(InstallCategoryOption? value, bool explicitChoice)
    {
        if (value is null)
        {
            return;
        }

        if (explicitChoice && HasQuery && !value.IsSelection)
        {
            // Przeglądanie kategorii = koniec wyszukiwania (zaznaczone wyniki winget zostają w serii – SearchText → _heldSearch).
            SearchText = string.Empty;
        }

        var groupOverride = explicitChoice && HasQuery;
        if (ReferenceEquals(_selectedGroup, value) && _groupOverride == groupOverride)
        {
            return;
        }

        _selectedGroup = value;
        _groupOverride = groupOverride;
        OnViewModeChanged();
        ApplyView();
    }

    private void OnViewModeChanged()
    {
        OnPropertyChanged(nameof(SelectedGroup));
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(IsSearchView));
        OnPropertyChanged(nameof(ShowsCategoryHeaders));
    }

    /// <summary>Zmiana tekstu: sekcja „W winget” znika, a zaznaczone wyniki przechodzą do <see cref="_heldSearch"/> (spec 11.5).</summary>
    private void DropWingetSection()
    {
        foreach (var row in _searchRows.Where(row => row.IsSelected))
        {
            _heldSearch[row.Target.PackageKey] = row;
        }

        _searchRows = Array.Empty<InstallRowViewModel>();
        _onListIds = NoIds;
        _hasWingetSection = false;
        SearchMessage = null;
    }

    /// <summary>Przebudowa listy (tryb, grupa, odczyt, wynik winget). Zmiana zaznaczenia jej nie woła – wiersz zostaje na miejscu.</summary>
    private void ApplyView()
    {
        var keep = _selectedRow;
        var items = new List<object>();
        var rows = new List<InstallRowViewModel>();
        _selectionHeaders.Clear();
        if (IsSearchView)
        {
            BuildSearchView(items, rows);
        }
        else if (_selectedGroup.IsSelection)
        {
            BuildSelectionView(items, rows);
        }
        else if (_selectedGroup.Group.ShowsCategoryHeaders)
        {
            AddByCategory(items, rows, _catalogRows.Where(row => _selectedGroup.Group.Contains(row.Row)), InstallListLineKind.Header);
        }
        else
        {
            AddRows(items, rows, _catalogRows.Where(row => _selectedGroup.Group.Contains(row.Row)));
        }

        VisibleRows = rows;
        VisibleItems = items;
        SelectedRow = keep is not null && rows.Contains(keep) ? keep : null;
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(WingetResults));
    }

    /// <summary>
    /// Tryb wyszukiwania: „Z listy (N)” – pozycje pasujące do filtra albo trafione przez winget (spec 3) z nagłówkami kategorii; pod
    /// nią, po wyszukiwaniu winget dla bieżącego tekstu, „W winget (N)” ze zdaniem o pakietach spoza listy, notatkami o pozycjach listy
    /// i komunikatem. Bez dopasowań i bez sekcji winget lista jest pusta (<see cref="EmptyText"/>).
    /// </summary>
    private void BuildSearchView(List<object> items, List<InstallRowViewModel> rows)
    {
        var onList = new HashSet<string>(_onListIds, StringComparer.Ordinal);
        var catalog = _catalogRows.Where(row => onList.Contains(row.Target.Id) || InstallSearch.Matches(row.Target, row.Summary, _searchText)).ToList();
        var searching = IsSearching && _searchingVersion == _textVersion;
        if (catalog.Count == 0 && !_hasWingetSection && !searching)
        {
            return;
        }

        items.Add(new InstallListLine(InstallListLineKind.Header, InstallStrings.CatalogSection(catalog.Count), isFirst: true));
        AddByCategory(items, rows, catalog, InstallListLineKind.SubHeader);
        if (_hasWingetSection)
        {
            items.Add(new InstallListLine(InstallListLineKind.Header, InstallStrings.WingetSection(_searchRows.Count)));
            if (_searchRows.Count > 0)
            {
                items.Add(new InstallListLine(InstallListLineKind.Note, InstallStrings.WingetSectionNote));
            }

            var notes = 0;
            foreach (var id in _onListIds)
            {
                if (_catalogRows.FirstOrDefault(row => string.Equals(row.Target.Id, id, StringComparison.Ordinal)) is { } listed)
                {
                    items.Add(new InstallListLine(
                        InstallListLineKind.Note,
                        listed.Target.Origin == InstallOrigin.GitHub ? InstallStrings.OnListNoteGitHub(listed.Title) : InstallStrings.OnListNote(listed.Title)));
                    notes++;
                }
            }

            // „Znalezione aplikacje są na liście – wyżej” powtarzałoby notatki o pozycjach listy.
            if (HasSearchMessage && !(notes > 0 && string.Equals(_searchMessage, InstallStrings.SearchAllOnList, StringComparison.Ordinal)))
            {
                items.Add(new InstallListLine(InstallListLineKind.Message, _searchMessage!, isWarning: SearchMessageIsWarning));
            }

            AddRows(items, rows, _searchRows);
        }

        if (searching)
        {
            items.Add(new InstallListLine(InstallListLineKind.Note, InstallStrings.Searching));
        }
    }

    /// <summary>
    /// „Zaznaczone”: zaznaczone pozycje listy z nagłówkami kategorii, potem „W winget (N)” z zaznaczonymi wynikami winget. Nagłówki
    /// trafiają do <see cref="_selectionHeaders"/>: po odznaczeniu wiersz zostaje (można się rozmyślić), a licznik nagłówka spada
    /// (<see cref="UpdateSelectionHeaders"/>, F9).
    /// </summary>
    private void BuildSelectionView(List<object> items, List<InstallRowViewModel> rows)
    {
        foreach (var category in _catalogRows.Where(row => row.IsSelected).GroupBy(row => row.CategoryHeader, StringComparer.Ordinal))
        {
            var members = category.ToList();
            var line = new InstallListLine(InstallListLineKind.Header, category.Key, members.Count.ToString(CultureInfo.InvariantCulture), isFirst: items.Count == 0);
            items.Add(line);
            AddRows(items, rows, members);
            _selectionHeaders.Add((line, members, false));
        }

        var winget = SelectedWingetRows().Distinct().ToList();
        if (winget.Count > 0)
        {
            var line = new InstallListLine(InstallListLineKind.Header, InstallStrings.WingetSection(winget.Count), isFirst: items.Count == 0);
            items.Add(line);
            AddRows(items, rows, winget);
            _selectionHeaders.Add((line, winget, true));
        }
    }

    /// <summary>Liczniki nagłówków „Zaznaczone” = zaznaczone wiersze pod nagłówkiem (lista się nie przebudowuje).</summary>
    private void UpdateSelectionHeaders()
    {
        foreach (var (line, members, isWinget) in _selectionHeaders)
        {
            var count = members.Count(row => row.IsSelected);
            if (isWinget)
            {
                line.SetText(InstallStrings.WingetSection(count), null);
            }
            else
            {
                line.SetText(line.Text, count.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>Wiersze z nagłówkiem nad każdą kategorią (nazwa i liczba pozycji), w kolejności wierszy.</summary>
    private static void AddByCategory(List<object> items, List<InstallRowViewModel> rows, IEnumerable<InstallRowViewModel> source, InstallListLineKind kind)
    {
        foreach (var category in source.GroupBy(row => row.CategoryHeader, StringComparer.Ordinal))
        {
            var members = category.ToList();
            items.Add(new InstallListLine(kind, category.Key, members.Count.ToString(CultureInfo.InvariantCulture), isFirst: items.Count == 0));
            AddRows(items, rows, members);
        }
    }

    private static void AddRows(List<object> items, List<InstallRowViewModel> rows, IEnumerable<InstallRowViewModel> source)
    {
        foreach (var row in source)
        {
            items.Add(row);
            rows.Add(row);
        }
    }

    /// <summary>Winget gotowy, nic nie trwa i zapytanie po normalizacji niepuste (same separatory nie uruchamiają winget).</summary>
    private bool CanSearch() => !IsBusy && !IsSearching && IsWingetReady && HasQuery;

    /// <summary>
    /// Wyszukiwanie w winget dla bieżącego tekstu (Enter, „Szukaj też w winget”): widok wraca do wyników filtra, pod sekcją „Z listy”
    /// notatka „Szukanie w winget”. Wynik, który przyszedł po zmianie tekstu, jest odrzucany (Review Focus 2).
    /// </summary>
    private async Task SearchAsync()
    {
        if (!CanSearch())
        {
            return;
        }

        var query = _searchText.Trim();
        var version = _textVersion;
        _searchingVersion = version;
        IsSearching = true;
        if (_groupOverride)
        {
            _groupOverride = false;
            OnViewModeChanged();
        }

        ApplyView();
        try
        {
            var result = await Task.Run(() => _session.Search(query)).ConfigureAwait(true);
            if (version != _textVersion)
            {
                return;
            }

            IsSearching = false;
            ApplySearch(result);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
        finally
        {
            if (IsSearching)
            {
                IsSearching = false;
                if (version == _textVersion)
                {
                    ApplyView();
                }
            }
        }
    }

    /// <summary>
    /// Nowe wyniki: zaznaczony wcześniej wynik (po <c>PackageKey</c>) wraca zaznaczony, a zaznaczone spoza nowych wyników zostają
    /// w serii (<see cref="_heldSearch"/>), choć ich nie widać w sekcji „W winget” (są w „Zaznaczone”). Gdy w trakcie wyszukiwania
    /// odczyt przestawił winget z <c>Ready</c> (wejście na stronę nie czeka na <see cref="IsSearching"/> – M1 final review), wyniki
    /// przychodzą bez pola wyboru, jak w <see cref="Rebuild"/>, a seria nie trzyma żadnego wyniku wyszukiwania.
    /// </summary>
    private void ApplySearch(InstallSearchResult result)
    {
        var held = new Dictionary<string, InstallRowViewModel>(StringComparer.OrdinalIgnoreCase);
        var ready = IsWingetReady;
        if (ready)
        {
            foreach (var row in _searchRows.Concat(_heldSearch.Values).Where(row => row.IsSelected))
            {
                held[row.Target.PackageKey] = row;
            }
        }

        var rows = new List<InstallRowViewModel>();
        foreach (var source in result.Rows)
        {
            var row = ready || !source.CanSelect ? source : source with { CanSelect = false };
            if (row.CanSelect && held.TryGetValue(row.Target.PackageKey, out var kept))
            {
                held.Remove(row.Target.PackageKey);
                rows.Add(kept);
            }
            else
            {
                rows.Add(new InstallRowViewModel(row, null, _wingetGroup, RefreshSelection));
            }
        }

        _searchRows = rows;
        _heldSearch = held;
        _onListIds = result.OnListIds;
        _hasWingetSection = true;
        SearchMessage = result.Message;
        if (_groupOverride)
        {
            // F10: „Zaznaczone” wybrane w trakcie wyszukiwania (tekst bez zmian) – wynik ma być widać od razu, nie dopiero po kolejnym Enterze.
            _groupOverride = false;
            OnViewModeChanged();
        }

        ApplyView();
        RefreshSelection();
    }

    private async Task InstallAsync()
    {
        var targets = SelectedRows().Select(row => row.Target).ToList();
        if (targets.Count == 0 || !CanInstall)
        {
            return;
        }

        try
        {
            await _install(targets).ConfigureAwait(true);
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
            foreach (var row in SelectedRows().ToList())
            {
                row.IsSelected = false;
            }

            _heldSearch.Clear();
        }
        finally
        {
            _rebuilding = false;
        }

        RefreshSelection();
        if (_selectedGroup.IsSelection && !IsSearchView)
        {
            // „Odznacz wszystko” w „Zaznaczone”: lista od razu pusta (pojedyncze odznaczenie zostawia wiersz do rozmyślenia się).
            ApplyView();
        }
    }

    private void OnCommandsChanged()
    {
        OnPropertyChanged(nameof(CanInstall));
        RefreshCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
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
