using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSOptimizer.App.Gui;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using TextFolding = DSOptimizer.Core.Gui.SearchText;

namespace DSOptimizer.App.ViewModels;

/// <summary>
/// Cienka warstwa nad <see cref="GuiSession"/>. Zasada: Detect/DetectAll/Run wołane wyłącznie przez Task.Run
/// (blokują się na blokadzie sesji), a postęp detekcji to <see cref="Progress{T}"/> utworzony na wątku UI
/// (asynchroniczny Post – żadnego Dispatcher.Invoke w callbacku, bo Report działa pod blokadą sesji).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public static string CheckingText => L.T("Sprawdzanie stanu…");

    public static string NotCheckedText => L.T("Stan nie został jeszcze sprawdzony");

    public static string RestartingText => L.T("Uruchamianie ponownie komputera…");

    /// <summary>Cel „Przejdź do: Napraw winget” (strona „Instalacja”): pozycja naprawy winget na stronie „Naprawy i narzędzia”.</summary>
    private const string RepairWingetId = "repair.winget";

    private readonly GuiSession _session;
    private readonly AppsSession _apps;
    private readonly AutostartSession _autostart;
    private readonly InstallSession _install;
    private readonly UiLogSink _sink;
    private readonly ISeriesDialogs _seriesDialogs;
    private readonly IClock _clock;
    private readonly ILastBootTime _lastBootTime;
    private readonly IRestarter _restarter;
    private readonly DateTimeOffset _sessionStart;
    private readonly Func<string?>? _resolveUserName;
    private readonly Dictionary<string, ItemViewModel> _items = new(StringComparer.Ordinal);
    private readonly List<ItemViewModel> _allItems;
    private readonly ListCollectionView _searchView;
    private object? _selectedPage;
    private CategoryViewModel? _lastCategory;
    private string _searchText = string.Empty;
    private RiskFilter _riskFilter = RiskFilter.All;
    private ShowFilter _showFilter = ShowFilter.All;

    /// <summary>Id pozycji widoku „Zaznaczone” (zaznaczone przy wejściu i dodane w trakcie); null poza widokiem.</summary>
    private HashSet<string>? _selectedViewIds;
    private bool _isBusy;
    private bool _isConfirming;
    private Task<DateTimeOffset?>? _lastBoot;
    private int _rebootVersion;
    private int _driftVersion;
    private bool _rebootWarningShown;
    private IReadOnlyList<string> _pendingRebootTitles = Array.Empty<string>();
    private string? _operationText;
    private DateTimeOffset? _lastCheck;
    private string _logSummary = GuiText.LogSummary(0, 0);
    private bool _isLogExpanded;
    private string _lastLogText = string.Empty;
    private string _lastLogToolTip = string.Empty;
    private GuiLogLevel _logSummaryLevel = GuiLogLevel.Info;
    private int _selectedCount;
    private int _applicableCount;
    private int _undoableCount;
    private string _selectionBarDetails = string.Empty;
    private string _machineText;
    private string? _userName;
    private bool _technicalDetailsExpanded;

    /// <param name="seriesDialogs">Okno serii 2+ pozycji: potwierdzenie, postęp i wynik; potwierdzenie restartu.</param>
    /// <param name="lastBootTime">Czas ostatniego startu systemu (baner restartu); czytany raz, przez Task.Run.</param>
    /// <param name="restarter">„Uruchom ponownie” z banera i z okna wyniku serii – zawsze po potwierdzeniu.</param>
    /// <param name="resolveUserName">Nazwa użytkownika na pasek marki; wołana poza wątkiem UI w <see cref="LoadUserNameAsync"/>.</param>
    /// <param name="accountNames">SID → nazwa konta na stronie „Historia zmian”; wołane tylko w Task.Run (LSA może blokować).</param>
    /// <param name="apps">Strona „Aplikacje”: odczyt, potwierdzenie i seria usuwania (ta sama blokada co seria tweaków).</param>
    /// <param name="autostart">Strona „Autostart”: odczyt, potwierdzenie i seria „Wyłącz/Włącz” (ta sama blokada).</param>
    /// <param name="install">Strona „Instalacja”: odczyt, wyszukiwanie w winget, potwierdzenie i seria instalacji (ta sama blokada).</param>
    /// <param name="specification">Strona „Specyfikacja” – pierwsza pozycja menu i strona startowa.</param>
    /// <param name="prep">Strona „Przygotowanie” – druga pozycja menu, tuż pod „Specyfikacją”.</param>
    /// <param name="settings">Strona „Ustawienia” – pozycja przypięta na dole menu.</param>
    public MainViewModel(
        GuiSession session,
        UiLogSink sink,
        ISeriesDialogs seriesDialogs,
        IClock clock,
        ILastBootTime lastBootTime,
        IRestarter restarter,
        string machineText,
        Func<string?>? resolveUserName,
        IAccountNames accountNames,
        AppsSession apps,
        AutostartSession autostart,
        InstallSession install,
        SpecificationViewModel specification,
        PrepViewModel prep,
        SettingsViewModel settings)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _apps = apps ?? throw new ArgumentNullException(nameof(apps));
        _autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));
        _install = install ?? throw new ArgumentNullException(nameof(install));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _seriesDialogs = seriesDialogs ?? throw new ArgumentNullException(nameof(seriesDialogs));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _lastBootTime = lastBootTime ?? throw new ArgumentNullException(nameof(lastBootTime));
        _restarter = restarter ?? throw new ArgumentNullException(nameof(restarter));
        _sessionStart = _clock.UtcNow;
        _machineText = machineText ?? string.Empty;
        _resolveUserName = resolveUserName;
        if (accountNames is null)
        {
            throw new ArgumentNullException(nameof(accountNames));
        }

        RefreshCommand = new AsyncRelayCommand(InitializeAsync, () => !IsBusy);
        RefreshCurrentPageCommand = new AsyncRelayCommand(RefreshCurrentPageAsync, () => !IsBusy);
        ApplySelectedCommand = new AsyncRelayCommand(() => RunBatchAsync(GuiOperation.Apply), () => !IsBusy && ApplicableCount > 0);
        UndoSelectedCommand = new AsyncRelayCommand(() => RunBatchAsync(GuiOperation.Undo), () => !IsBusy && UndoableCount > 0);
        ToggleLogCommand = new RelayCommand(() => IsLogExpanded = !IsLogExpanded);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy && SelectedCount > 0);
        SelectSuggestedCommand = new RelayCommand(SelectSuggested, CanSelectSuggested);
        ShowAllCommand = new RelayCommand(ShowAll);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ShowSelectedCommand = new RelayCommand(() => ShowFilter = ShowFilter.Selected, () => SelectedCount > 0);
        EscapeCommand = new RelayCommand(Escape, () => _searchText.Length > 0 || IsSelectedView);
        ResetListViewCommand = new RelayCommand(ResetListView);
        RestartCommand = new AsyncRelayCommand(RestartFromBannerAsync, () => !IsBusy && !IsConfirming);
        ShowPendingCommand = new RelayCommand(ShowPendingReboot);
        // Strażnik ustawień: „Zaznacz i pokaż” tylko zaznacza i otwiera widok „Zaznaczone”, nieaktywne w trakcie operacji.
        DriftBanner = new DriftBannerViewModel(SelectAndShowDrifted, () => !IsBusy);

        // Strony kategorii w kolejności menu: kategorie ustawień, na końcu scalona strona „Naprawy i narzędzia”.
        Categories = GuiNav.CategoryPages(session.Categories).Select(page => new CategoryViewModel(page, this)).ToList();
        _allItems = Categories.SelectMany(category => category.Items).ToList();
        foreach (var item in _allItems)
        {
            _items[item.Id] = item;
        }

        foreach (var category in Categories)
        {
            category.PropertyChanged += OnCategoryChanged;
        }

        // Wyniki wyszukiwania: wszystkie pozycje w kolejności kategorii (w kategorii jak na jej liście), nagłówek = kategoria albo „Kategoria · grupa”.
        _searchView = new ListCollectionView(_allItems.ToList()) { Filter = item => item is ItemViewModel vm && IsVisible(vm) };
        _searchView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ItemViewModel.ResultsHeader)));
        _searchView.CurrentChanged += (_, _) => OnPropertyChanged(nameof(DetailItem));
        RiskFilterOptions = new[] { RiskFilter.All, RiskFilter.NoExperimental, RiskFilter.SafeOnly }
            .Select(filter => new RiskFilterOption(filter, GuiText.RiskFilterName(filter)))
            .ToList();
        ShowFilterOptions = new[] { ShowFilter.All, ShowFilter.NotApplied, ShowFilter.Applied, ShowFilter.Selected }
            .Select(filter => new ShowFilterOption(filter, GuiText.ShowFilterName(filter)))
            .ToList();

        Specification = specification ?? throw new ArgumentNullException(nameof(specification));
        Prep = prep ?? throw new ArgumentNullException(nameof(prep));
        // Historia: odczyt journala i nazw kont poza wątkiem UI, z tym samym czasem startu systemu co baner restartu.
        // „Cofnij” tweaka idzie tą samą ścieżką co z listy (potwierdzenie, raport, baner, odświeżenie historii); wpis autostartu – przez
        // okno serii i sesję autostartu (UndoFromHistoryAsync).
        History = new HistoryViewModel(
            async () =>
            {
                var lastBoot = await LastBootAsync().ConfigureAwait(true);
                return await Task.Run(() => _session.LoadHistory(lastBoot, _sessionStart, accountNames)).ConfigureAwait(true);
            },
            UndoFromHistoryAsync,
            () => SelectedPage = Apps,
            (level, text) => _sink.Add(level, text));
        // Aplikacje: własne zaznaczenie i „Usuń (N)” zawsze przez okno serii (RemoveAppsAsync), osobno od „Zastosuj/Cofnij”.
        Apps = new AppsViewModel(apps, accountNames, RemoveAppsAsync, () => _session.CreateRestorePoint, (level, text) => _sink.Add(level, text), _clock);
        // Autostart: własne zaznaczenie i „Wyłącz (N)”/„Włącz (M)” zawsze przez okno serii (ToggleAutostartAsync).
        Autostart = new AutostartViewModel(autostart, accountNames, ToggleAutostartAsync, WpfClipboardText.Instance, (level, text) => _sink.Add(level, text), _clock);
        // Instalacja: własne zaznaczenie i „Zainstaluj (N)” zawsze przez okno serii (InstallAppsAsync); „Napraw winget” = strona „Naprawy i narzędzia”.
        Install = new InstallViewModel(install, InstallAppsAsync, ShowRepairWinget, (level, text) => _sink.Add(level, text), _clock);
        NavItems = WithSeparators(
            new object[] { specification, prep },
            Categories.Where(category => !category.IsService).ToArray<object>(),
            new object[] { Apps, Autostart, Install },
            Categories.Where(category => category.IsService).ToArray<object>(),
            new object[] { History });
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        PinnedNavItems = new object[] { settings };
        // Zmiana punktu przywracania od razu w drugiej linii paska serii (GuiText.SelectionBarDetails).
        Settings.PropertyChanged += OnSettingsChanged;
        _selectedPage = specification;
        _sink.Lines.CollectionChanged += OnLogChanged;
    }

    /// <summary>Komputer, edycja, build, a po <see cref="LoadUserNameAsync"/> także (jeśli pewny) użytkownik – na pasek marki.</summary>
    public string MachineText
    {
        get => _machineText;
        private set => SetProperty(ref _machineText, value);
    }

    /// <summary>
    /// Dopisuje użytkownika do paska marki i zapamiętuje go dla „Dotyczy konta …” w oknie serii. Tłumaczenie SID → nazwa
    /// konta może blokować (domena bez kontrolera), więc idzie przez Task.Run; błąd albo brak nazwy = pasek bez użytkownika.
    /// </summary>
    public async Task LoadUserNameAsync()
    {
        if (_resolveUserName is null)
        {
            return;
        }

        try
        {
            var name = await Task.Run(_resolveUserName).ConfigureAwait(true);
            _userName = string.IsNullOrWhiteSpace(name) ? null : name;
            MachineText = MachineSummary.WithUser(MachineText, name);
        }
        catch (Exception)
        {
            // Nazwa użytkownika jest tylko ozdobą paska – bez niej okno działa tak samo.
        }
    }

    public IReadOnlyList<CategoryViewModel> Categories { get; }

    public SpecificationViewModel Specification { get; }

    /// <summary>Strona „Przygotowanie” – tuż pod „Specyfikacją”, przed kategoriami.</summary>
    public PrepViewModel Prep { get; }

    /// <summary>Strona „Aplikacje” – pierwsza w grupie za kategoriami ustawień (za linią).</summary>
    public AppsViewModel Apps { get; }

    /// <summary>Strona „Autostart” – tuż pod „Aplikacjami”, przed „Instalacją”.</summary>
    public AutostartViewModel Autostart { get; }

    /// <summary>Strona „Instalacja” – tuż pod „Autostartem”, ostatnia w swojej grupie (za nią linia i „Naprawy i narzędzia”).</summary>
    public InstallViewModel Install { get; }

    /// <summary>Strona „Historia zmian” – ostatnia pozycja górnej listy menu, sama w ostatniej grupie.</summary>
    public HistoryViewModel History { get; }

    /// <summary>
    /// Menu w lewej kolumnie, pięć grup rozdzielonych liniami (<see cref="NavSeparatorViewModel"/>): Specyfikacja i Przygotowanie |
    /// kategorie ustawień w kolejności <see cref="GuiSession.Categories"/> | Aplikacje, Autostart, Instalacja | Naprawy i narzędzia |
    /// Historia zmian. Pusta grupa (np. katalog bez napraw, narzędzi i paneli) nie dostaje linii.
    /// </summary>
    public IReadOnlyList<object> NavItems { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Pozycje przypięte na dole menu (za linią): tylko „Ustawienia”.</summary>
    public IReadOnlyList<object> PinnedNavItems { get; }

    /// <summary>
    /// Strona z lewej kolumny (start = Specyfikacja). Null (np. chwilowo z ListBoxa) jest ignorowany.
    /// Wybór pozycji w menu czyści wyszukiwanie [decyzja projektowa] i wychodzi z widoku „Zaznaczone”. Wejście na „Historię zmian” zaczyna od filtra „Wszystkie wpisy”
    /// (filtr „Czekające na restart” ustawia tylko <see cref="ShowPendingCommand"/>).
    /// </summary>
    public object? SelectedPage
    {
        get => _selectedPage;
        set => SelectPage(value, HistoryFilter.All);
    }

    /// <summary>
    /// Zmiana strony (<see cref="SelectedPage"/>). <paramref name="historyFilter"/> = filtr historii przy wejściu na nią z innej strony,
    /// ustawiany przed wejściem, bo samo wejście odświeża historię – jeden odczyt journala.
    /// </summary>
    private void SelectPage(object? value, HistoryFilter historyFilter)
    {
        if (value is null)
        {
            return;
        }

        // Linia grupy menu nie jest stroną: wybór jej (np. Select z automatyzacji UI) niczego nie zmienia, ani strony, ani wyszukiwania;
        // powiadomienie przywraca w liście podświetlenie bieżącej strony.
        if (value is NavSeparatorViewModel)
        {
            OnPropertyChanged(nameof(SelectedNavItem));
            return;
        }

        if (value is CategoryViewModel category)
        {
            _lastCategory = category;
        }

        if (value is HistoryViewModel entering && !ReferenceEquals(_selectedPage, entering))
        {
            entering.Filter = historyFilter;
        }

        if (SetProperty(ref _selectedPage, value, nameof(SelectedPage)))
        {
            OnPropertyChanged(nameof(SelectedNavItem));
            OnPropertyChanged(nameof(SelectedPinnedItem));
            OnPropertyChanged(nameof(SelectedCategory));
            OnPropertyChanged(nameof(IsListGrouped));
            OnPropertyChanged(nameof(IsCategorySelected));
            OnPropertyChanged(nameof(IsSpecificationSelected));
            OnPropertyChanged(nameof(IsPrepSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsHistorySelected));
            OnPropertyChanged(nameof(IsAppsSelected));
            OnPropertyChanged(nameof(IsAutostartSelected));
            OnPropertyChanged(nameof(IsInstallSelected));
            OnPropertyChanged(nameof(IsShowFilterVisible));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(SelectionBarTitle));
            OnPropertyChanged(nameof(SelectionBarDetails));
            OnPropertyChanged(nameof(DetailItem));
            OnHiddenChanged();

            // Zmiana strony wychodzi z widoku „Zaznaczone”; „Niezastosowane”/„Zastosowane” zostają.
            LeaveSelectedView();

            // Historia, Aplikacje, Autostart, Instalacja i Przygotowanie wczytują się przy każdym wejściu na stronę (RefreshAsync nigdy nie rzuca;
            // Instalacja w trakcie operacji odkłada odczyt na jej koniec).
            if (value is HistoryViewModel history)
            {
                _ = history.RefreshAsync();
            }
            else if (value is PrepViewModel prep)
            {
                _ = prep.RefreshAsync();
            }
            else if (value is AppsViewModel apps)
            {
                _ = apps.RefreshAsync();
            }
            else if (value is AutostartViewModel autostart)
            {
                _ = autostart.RefreshAsync();
            }
            else if (value is InstallViewModel install)
            {
                // Każde wejście na stronę = „Wszystkie” (D1) i świeży odczyt.
                _ = install.EnterAsync();
            }
        }

        SearchText = string.Empty;
        RefreshSelectSuggested();
    }

    /// <summary>
    /// Zaznaczenie górnej listy menu (<see cref="NavItems"/>): <see cref="SelectedPage"/>, gdy jest na tej liście, inaczej null.
    /// Dzięki temu w obu listach menu podświetlona jest dokładnie jedna pozycja, a klik w pozycję drugiej listy zawsze zmienia zaznaczenie.
    /// </summary>
    public object? SelectedNavItem
    {
        get => _selectedPage is { } page && NavItems.Contains(page) ? page : null;
        set => SelectedPage = value;
    }

    /// <summary>Zaznaczenie listy przypiętej na dole menu (<see cref="PinnedNavItems"/>), na tej samej zasadzie co <see cref="SelectedNavItem"/>.</summary>
    public object? SelectedPinnedItem
    {
        get => _selectedPage is { } page && PinnedNavItems.Contains(page) ? page : null;
        set => SelectedPage = value;
    }

    /// <summary>Ostatnio otwarta kategoria (na starcie pierwsza) – Ctrl+F ze Specyfikacji wraca do niej.</summary>
    public CategoryViewModel? LastCategory => _lastCategory ?? (Categories.Count > 0 ? Categories[0] : null);

    /// <summary>Zapytanie wyszukiwarki (zmiana od razu przy pisaniu); szuka we wszystkich kategoriach.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                EscapeCommand.NotifyCanExecuteChanged();
                RefreshSearch();
                OnPropertyChanged(nameof(SelectionBarTitle));
                OnPropertyChanged(nameof(SelectionBarDetails));
            }
        }
    }

    /// <summary>Filtr ryzyka listy (start „Wszystkie”, niezapamiętywany między uruchomieniami).</summary>
    public RiskFilter RiskFilter
    {
        get => _riskFilter;
        set
        {
            if (SetProperty(ref _riskFilter, value))
            {
                ApplyCategoryFilters();
                RefreshSearch();
            }
        }
    }

    public IReadOnlyList<RiskFilterOption> RiskFilterOptions { get; }

    /// <summary>
    /// Filtr „Pokaż” (start „Wszystkie”, niezapamiętywany między uruchomieniami). „Niezastosowane”/„Zastosowane” działają jak filtr ryzyka
    /// (globalnie, zostają po zmianie strony), a „Zaznaczone” to widok (<see cref="IsSelectedView"/>): wejście zapamiętuje zaznaczone pozycje
    /// ze wszystkich kategorii, wyjście (inna opcja, klik w menu, zmiana strony, Esc) ten zbiór czyści.
    /// </summary>
    public ShowFilter ShowFilter
    {
        get => _showFilter;
        set
        {
            if (!SetProperty(ref _showFilter, value))
            {
                return;
            }

            _selectedViewIds = value == ShowFilter.Selected
                ? new HashSet<string>(_allItems.Where(item => item.IsSelected).Select(item => item.Id), StringComparer.Ordinal)
                : null;
            OnPropertyChanged(nameof(IsSelectedView));
            OnPropertyChanged(nameof(UsesResultsView));
            OnPropertyChanged(nameof(IsShowFilterVisible));
            OnPropertyChanged(nameof(SelectionBarTitle));
            OnPropertyChanged(nameof(SelectionBarDetails));
            EscapeCommand.NotifyCanExecuteChanged();
            ApplyCategoryFilters();
            RefreshSearch();
        }
    }

    public IReadOnlyList<ShowFilterOption> ShowFilterOptions { get; }

    /// <summary>Widok „Zaznaczone”: zaznaczone ze wszystkich kategorii z nagłówkami „Kategoria · grupa”, na mechanizmie wyników wyszukiwania.</summary>
    public bool IsSelectedView => _showFilter == ShowFilter.Selected;

    /// <summary>Lista pokazuje wyniki (<see cref="SearchResultsView"/>), nie stronę kategorii: wyszukiwanie albo widok „Zaznaczone”.</summary>
    public bool UsesResultsView => IsSearching || IsSelectedView;

    /// <summary>
    /// Lista „Pokaż” jest widoczna na stronie kategorii, poza stroną „Naprawy i narzędzia” (tam filtr stanu nie działa). W wynikach (wyszukiwanie,
    /// „Zaznaczone”) jest widoczna zawsze, także na tej stronie, bo wyniki stosują oba filtry – nic nie znika bez kontrolki.
    /// </summary>
    public bool IsShowFilterVisible => SelectedCategory is { } category && (UsesResultsView || !category.IsService);

    /// <summary>Link „Pokaż zaznaczone” w pasku serii: widok „Zaznaczone”; aktywny, gdy coś jest zaznaczone.</summary>
    public IRelayCommand ShowSelectedCommand { get; }

    /// <summary>Esc w wyszukiwarce i na liście: najpierw czyści tekst wyszukiwania, a bez tekstu wychodzi z widoku „Zaznaczone”.</summary>
    public IRelayCommand EscapeCommand { get; }

    /// <summary>Klik w pozycję menu: czyści wyszukiwanie i wychodzi z widoku „Zaznaczone” (także przy kliku w bieżącą stronę).</summary>
    public IRelayCommand ResetListViewCommand { get; }

    /// <summary>Wyszukiwanie trwa, gdy zapytanie po normalizacji nie jest puste (same spacje = brak wyszukiwania).</summary>
    public bool IsSearching => TextFolding.Fold(_searchText).Length > 0;

    /// <summary>
    /// Lista nie dzieli się na „Ustawienia”/„Akcje”: wyniki wyszukiwania, kategoria z grupami tematycznymi albo strona „Naprawy i narzędzia”.
    /// Stały wiersz nad listą nie pokazuje wtedy etykiety „Ustawienia”. Przy grupach tematycznych (poza wyszukiwaniem) stoi tam nagłówek
    /// pierwszej widocznej grupy z licznikiem (<see cref="CategoryViewModel.FirstVisibleGroup"/>), a jego dubel w liście jest zwinięty;
    /// w wynikach (wyszukiwanie, widok „Zaznaczone”) wiersz ma sam przycisk „Zaznacz sugerowane”, a pierwszy nagłówek „Kategoria · grupa” zostaje w liście.
    /// </summary>
    public bool IsListGrouped => UsesResultsView || SelectedCategory is { Grouping: not CategoryListGrouping.Sections };

    /// <summary>
    /// Wyniki wyszukiwania i widok „Zaznaczone”, pogrupowane nagłówkami „Kategoria · grupa” (bez grupy: kategoria), z filtrem zapytania,
    /// ryzyka i „Pokaż”; w widoku „Zaznaczone” tylko pozycje jego zbioru.
    /// </summary>
    public ICollectionView SearchResultsView => _searchView;

    /// <summary>Pozycja panelu szczegółów: bieżący wynik (wyszukiwanie, „Zaznaczone”) albo pozycja kategorii; null = panel zwinięty.</summary>
    public ItemViewModel? DetailItem => UsesResultsView ? _searchView.CurrentItem as ItemViewModel : SelectedCategory?.SelectedItem;

    /// <summary>
    /// Rozwinięte „Szczegóły techniczne” w panelu szczegółów (spec opisów 2026-10-26): domyślnie zwinięte, wybór innej pozycji
    /// ich nie zwija – stan trwa do zamknięcia programu, nie jest zapisywany w ustawieniach.
    /// </summary>
    public bool TechnicalDetailsExpanded
    {
        get => _technicalDetailsExpanded;
        set => SetProperty(ref _technicalDetailsExpanded, value);
    }

    public string PageTitle => IsSelectedView
        ? GuiText.SelectedViewTitle
        : IsSearching ? GuiText.SearchTitle : SelectedCategory?.Name ?? string.Empty;

    /// <summary>Pod tytułem: w wynikach (wyszukiwanie, „Zaznaczone”) ich liczba (w trakcie operacji jej postęp), inaczej <see cref="StatusText"/>.</summary>
    public string PageSubtitle
    {
        get
        {
            if (!UsesResultsView || (IsBusy && _operationText is not null))
            {
                return StatusText;
            }

            var visible = _allItems.Where(IsVisible).ToList();
            var categories = visible.Select(item => item.Definition.Category).Distinct(StringComparer.Ordinal).Count();
            return GuiText.SearchSummary(visible.Count, categories);
        }
    }

    /// <summary>Linia pod listą: ukryte przez filtr i zastosowane widoczne mimo filtra; null, gdy nie ma o czym mówić.</summary>
    public string? FilterNoteText => GuiText.FilterNote(HiddenCount, ShownDespiteFilterCount);

    public bool HasFilterNote => FilterNoteText is not null;

    /// <summary>
    /// Linia w miejscu pustej listy wyników; pusty tekst, gdy nie dotyczy (kategoria albo są wyniki). Widok „Zaznaczone” bez zaznaczonych
    /// pozycji mówi „Nic nie jest zaznaczone.”, a nie pokazuje pustej listy.
    /// </summary>
    public string EmptyText
    {
        get
        {
            if (!UsesResultsView || !_searchView.IsEmpty)
            {
                return string.Empty;
            }

            return _selectedViewIds is { Count: 0 } ? GuiText.SelectedViewEmpty : GuiText.EmptySearchText(_searchText, HiddenCount);
        }
    }

    /// <summary>„Pokaż wszystkie” pod listą: filtr ryzyka i „Niezastosowane”/„Zastosowane” wracają do „Wszystkie”; widok „Zaznaczone” zostaje.</summary>
    public IRelayCommand ShowAllCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    private int HiddenCount
    {
        get
        {
            if (UsesResultsView)
            {
                return ItemFilter.CountHidden(ResultsPool(), _searchText, _riskFilter, _showFilter);
            }

            return SelectedCategory?.HiddenCount ?? 0;
        }
    }

    private int ShownDespiteFilterCount
    {
        get
        {
            if (UsesResultsView)
            {
                return ItemFilter.CountShownDespiteFilter(ResultsPool(), _searchText, _riskFilter, _showFilter);
            }

            return SelectedCategory?.ShownDespiteFilterCount ?? 0;
        }
    }

    /// <summary>Pozycje, z których wybierają wyniki: wszystkie, a w widoku „Zaznaczone” tylko jego zbiór (z ostatnim znanym stanem).</summary>
    private IEnumerable<KeyValuePair<TweakDefinition, DetectionState?>> ResultsPool()
        => _allItems
            .Where(item => _selectedViewIds is null || _selectedViewIds.Contains(item.Id))
            .Select(item => new KeyValuePair<TweakDefinition, DetectionState?>(item.Definition, item.KnownState));

    /// <summary>Zaznaczona kategoria; filtruje środek. Null, gdy otwarta jest Specyfikacja.</summary>
    public CategoryViewModel? SelectedCategory => SelectedPage as CategoryViewModel;

    public bool IsCategorySelected => SelectedPage is CategoryViewModel;

    public bool IsSpecificationSelected => SelectedPage is SpecificationViewModel;

    public bool IsPrepSelected => SelectedPage is PrepViewModel;

    public bool IsSettingsSelected => SelectedPage is SettingsViewModel;

    public bool IsHistorySelected => SelectedPage is HistoryViewModel;

    public bool IsAppsSelected => SelectedPage is AppsViewModel;

    public bool IsAutostartSelected => SelectedPage is AutostartViewModel;

    public bool IsInstallSelected => SelectedPage is InstallViewModel;

    public ObservableCollection<GuiLogLine> LogLines => _sink.Lines;

    /// <summary>„N błędów, M ostrzeżeń” nad dziennikiem.</summary>
    public string LogSummary
    {
        get => _logSummary;
        private set => SetProperty(ref _logSummary, value);
    }

    /// <summary>
    /// Dziennik rozwinięty (lista linii) albo zwinięty do jednego paska. Start zawsze zwinięty, stan nie jest zapamiętywany,
    /// a nowa linia (także błąd) niczego nie rozwija – rozwija wyłącznie użytkownik.
    /// </summary>
    public bool IsLogExpanded
    {
        get => _isLogExpanded;
        set
        {
            if (SetProperty(ref _isLogExpanded, value))
            {
                OnPropertyChanged(nameof(LogToggleText));
            }
        }
    }

    public IRelayCommand ToggleLogCommand { get; }

    /// <summary>Nazwa przycisku rozwinięcia: „Rozwiń dziennik” albo „Zwiń dziennik”.</summary>
    public string LogToggleText => IsLogExpanded ? GuiText.LogCollapseText : GuiText.LogExpandText;

    /// <summary>Ostatnia linia dziennika w jednej linii (godzina i tekst) na pasku zwiniętego dziennika; pusta, gdy dziennik jest pusty.</summary>
    public string LastLogText
    {
        get => _lastLogText;
        private set => SetProperty(ref _lastLogText, value);
    }

    /// <summary>Pełny tekst ostatniej linii dziennika (podpowiedź paska).</summary>
    public string LastLogToolTip
    {
        get => _lastLogToolTip;
        private set => SetProperty(ref _lastLogToolTip, value);
    }

    /// <summary>Poziom licznika: Error przy błędach, inaczej Warning przy ostrzeżeniach, inaczej Info (kolor licznika na pasku).</summary>
    public GuiLogLevel LogSummaryLevel
    {
        get => _logSummaryLevel;
        private set => SetProperty(ref _logSummaryLevel, value);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// F5: odświeżenie otwartej strony – kategoria = <see cref="RefreshCommand"/>, Specyfikacja i Przygotowanie = ich ponowny odczyt,
    /// Historia = <see cref="HistoryViewModel.RefreshAsync"/>, Aplikacje = <see cref="AppsViewModel.RefreshAsync"/>,
    /// Autostart = <see cref="AutostartViewModel.RefreshAsync"/>, Instalacja = <see cref="InstallViewModel.RefreshAsync"/>, Ustawienia = nic.
    /// W trakcie operacji nieaktywne.
    /// </summary>
    public IAsyncRelayCommand RefreshCurrentPageCommand { get; }

    public IAsyncRelayCommand ApplySelectedCommand { get; }

    public IAsyncRelayCommand UndoSelectedCommand { get; }

    public IRelayCommand ClearSelectionCommand { get; }

    /// <summary>„Zaznacz sugerowane”: dodaje do zaznaczenia widoczne, sugerowane i niezastosowane ustawienia; nic nie wykonuje i niczego nie odznacza.</summary>
    public IRelayCommand SelectSuggestedCommand { get; }

    /// <summary>Przycisk „Zaznacz sugerowane” jest widoczny na stronie kategorii, gdy w widoku (kategoria albo wyniki wyszukiwania) jest pozycja sugerowana.</summary>
    public bool IsSelectSuggestedVisible => IsCategorySelected && SuggestionViewItems().Any(item => item.IsSuggested);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                foreach (var item in _items.Values)
                {
                    item.RefreshCommands();
                }

                RefreshCommand.NotifyCanExecuteChanged();
                RefreshCurrentPageCommand.NotifyCanExecuteChanged();
                ApplySelectedCommand.NotifyCanExecuteChanged();
                UndoSelectedCommand.NotifyCanExecuteChanged();
                ClearSelectionCommand.NotifyCanExecuteChanged();
                RestartCommand.NotifyCanExecuteChanged();
                DriftBanner.RefreshCanSelect();
                RefreshSelectSuggested();
                Settings.IsEditable = !value;
                History.IsBusy = value;
                Apps.IsBusy = value;
                Autostart.IsBusy = value;
                Install.IsBusy = value;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(PageSubtitle));
            }
        }
    }

    /// <summary>
    /// Wiersz pod tytułem kategorii: w trakcie pracy co się dzieje, poza nią „Ostatnie sprawdzenie: HH:mm”
    /// (wymagany restart pokazuje baner pod paskiem marki).
    /// </summary>
    public string StatusText
    {
        get
        {
            if (IsBusy && _operationText is not null)
            {
                return _operationText;
            }

            return _lastCheck is { } at ? LastCheckText(at.ToLocalTime()) : NotCheckedText;
        }
    }

    /// <summary>„Ostatnie sprawdzenie: HH:mm” – godzina podana (czas lokalny), 24-godzinna w obu językach.</summary>
    public static string LastCheckText(DateTimeOffset localTime)
        => L.F("Ostatnie sprawdzenie: {0}", localTime.ToString("HH:mm", CultureInfo.InvariantCulture));

    /// <summary>Linia dziennika po anulowaniu potwierdzenia jednej pozycji: „Anulowano: Zastosuj – tytuł.”.</summary>
    public static string CanceledText(GuiOperation operation, string title)
        => L.F("Anulowano: {0} – {1}.", GuiText.OperationName(operation), title);

    /// <summary>Tekst pod tytułem w trakcie serii zaznaczonych pozycji: „Zastosuj: zaznaczone (3)…”.</summary>
    public static string SelectedOperationText(GuiOperation operation, int count)
        => L.F("{0}: zaznaczone ({1})…", GuiText.OperationName(operation), count);

    /// <summary>Tekst pod tytułem w trakcie serii aplikacji: „Usuń: zaznaczone aplikacje (2)…”.</summary>
    public static string SelectedAppsOperationText(GuiOperation operation, int count)
        => L.F("{0}: zaznaczone aplikacje ({1})…", GuiText.OperationName(operation), count);

    /// <summary>Baner „Wymagany restart” pod paskiem marki: widoczny, gdy coś z journala czeka na restart (<see cref="GuiSession.PendingReboot"/>).</summary>
    public bool IsRebootBannerVisible => _pendingRebootTitles.Count > 0;

    /// <summary>Tekst banera za pogrubionym <see cref="GuiText.RebootTitle"/>: ile zmian czeka na restart.</summary>
    public string RebootBannerText => GuiText.RebootBannerText(_pendingRebootTitles.Count);

    /// <summary>Nazwy pozycji czekających na restart (z <see cref="GuiSession.PendingReboot"/>); ich liczba steruje banerem.</summary>
    public IReadOnlyList<string> PendingRebootTitles
    {
        get => _pendingRebootTitles;
        private set
        {
            if (SetProperty(ref _pendingRebootTitles, value))
            {
                OnPropertyChanged(nameof(IsRebootBannerVisible));
                OnPropertyChanged(nameof(RebootBannerText));
            }
        }
    }

    /// <summary>„Pokaż które” na banerze: strona „Historia zmian” z filtrem „Czekające na restart” i świeżym odczytem.</summary>
    public IRelayCommand ShowPendingCommand { get; }

    /// <summary>
    /// Baner strażnika ustawień: zastosowane wcześniej pozycje, których świeża detekcja nie daje już Applied (<see cref="RefreshDriftAsync"/>).
    /// „Zaznacz i pokaż” = <see cref="SelectAndShowDrifted"/>; nic nie stosuje się samo.
    /// </summary>
    public DriftBannerViewModel DriftBanner { get; }

    /// <summary>
    /// Filtr ustawiany przy wejściu na stronę (<see cref="SelectPage"/>), bo samo wejście odświeża historię – jeden odczyt
    /// zamiast dwóch. Gdy historia jest już otwarta, wejścia nie ma, więc odświeża <see cref="HistoryViewModel.ShowPendingRebootAsync"/>.
    /// </summary>
    private void ShowPendingReboot()
    {
        if (ReferenceEquals(_selectedPage, History))
        {
            _ = History.ShowPendingRebootAsync();
            return;
        }

        SelectPage(History, HistoryFilter.PendingReboot);
    }

    /// <summary>
    /// Kontrakt <see cref="ILastBootTime.Read"/> mówi „nigdy nie rzuca”, ale zapamiętany Task z wyjątkiem psułby każde kolejne
    /// przeliczenie banera – wyjątek = czas nieznany (null), wtedy liczy się start sesji.
    /// </summary>
    private DateTimeOffset? ReadLastBoot()
    {
        try
        {
            return _lastBootTime.Read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>„Uruchom ponownie” na banerze: potwierdzenie, potem restart; nieaktywne w trakcie operacji i potwierdzenia.</summary>
    public IAsyncRelayCommand RestartCommand { get; }

    /// <summary>Okno potwierdzenia (albo przygotowanie jego treści) otwarte; razem z <see cref="IsBusy"/> blokuje „Uruchom ponownie”.</summary>
    private bool IsConfirming
    {
        get => _isConfirming;
        set
        {
            if (_isConfirming != value)
            {
                _isConfirming = value;
                RestartCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // Zaznaczenie do operacji zbiorczej (pola wyboru tweaków, przez wszystkie kategorie).
    public int SelectedCount
    {
        get => _selectedCount;
        private set => SetProperty(ref _selectedCount, value);
    }

    /// <summary>Zaznaczone, które można zastosować (<see cref="GuiRules.CanApply"/>).</summary>
    public int ApplicableCount
    {
        get => _applicableCount;
        private set => SetProperty(ref _applicableCount, value);
    }

    /// <summary>Zaznaczone, które można cofnąć (<see cref="GuiRules.CanUndo"/>).</summary>
    public int UndoableCount
    {
        get => _undoableCount;
        private set => SetProperty(ref _undoableCount, value);
    }

    public bool HasSelection => SelectedCount > 0;

    /// <summary>Tytuł stałego paska akcji (przy 0 zaznaczonych podpowiedź, jak zacząć).</summary>
    public string SelectionBarTitle => ShowsServiceHint
        ? GuiText.ServiceSelectionHint
        : GuiText.SelectionBarTitle(SelectedCount, ApplicableCount, UndoableCount);

    /// <summary>Druga linia paska: restarty i punkt przywracania; pusta przy 0 zaznaczonych.</summary>
    public string SelectionBarDetails => ShowsServiceHint ? GuiText.ServiceSelectionHintDetails : _selectionBarDetails;

    /// <summary>Strona „Naprawy i narzędzia” bez zaznaczenia i poza wynikami/„Zaznaczone”: dwuklik uruchamia pozycję, więc pasek mówi to zamiast „dodaj do zmiany”.</summary>
    private bool ShowsServiceHint => SelectedCount == 0 && !UsesResultsView && _selectedPage is CategoryViewModel { IsService: true };

    public string ApplySelectedText => L.F("Zastosuj ({0})", ApplicableCount);

    public string UndoSelectedText => L.F("Cofnij ({0})", UndoableCount);

    /// <summary>Odświeżenie strony pod F5 (<see cref="RefreshCurrentPageCommand"/>); poza operacją, ścieżkami istniejących odświeżeń.</summary>
    private Task RefreshCurrentPageAsync()
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        return _selectedPage switch
        {
            CategoryViewModel when RefreshCommand.CanExecute(null) => RefreshCommand.ExecuteAsync(null),
            SpecificationViewModel specification when specification.RefreshCommand.CanExecute(null) => specification.RefreshCommand.ExecuteAsync(null),
            PrepViewModel prep => prep.RefreshAsync(),
            HistoryViewModel history => history.RefreshAsync(),
            AppsViewModel apps => apps.RefreshAsync(),
            AutostartViewModel autostart => autostart.RefreshAsync(),
            InstallViewModel install => install.RefreshAsync(),
            _ => Task.CompletedTask,
        };
    }

    /// <summary>Detekcja startowa i „Odśwież stan”. IsBusy = true do końca; pozycje pokazują „Sprawdzanie…”.</summary>
    public async Task InitializeAsync()
    {
        if (IsBusy || _isConfirming)
        {
            return;
        }

        _operationText = CheckingText;
        IsBusy = true;
        try
        {
            foreach (var item in _items.Values)
            {
                item.SetChecking();
            }

            var progress = new Progress<KeyValuePair<string, DetectionState>>(pair => ApplyState(pair.Key, pair.Value));
            var states = await Task.Run(() => _session.DetectAll(progress)).ConfigureAwait(true);
            ApplyStates(states);
            _lastCheck = _clock.UtcNow;
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            foreach (var item in _items.Values)
            {
                item.SettleIfChecking();
            }

            // Detekcja mogła przenieść wpisy journala z PendingReboot do Active – baner dopiero po niej, ale jeszcze
            // pod IsBusy: PendingReboot bierze blokadę sesji, a klik „Zastosuj” w tej chwili dostałby „Trwa inna operacja”.
            // RefreshRebootAsync nigdy nie rzuca.
            await RefreshRebootAsync().ConfigureAwait(true);
            // Strażnik ustawień na stanach po detekcji, gdy ewentualny awans PendingReboot → Active jest już zapisany; też pod IsBusy
            // (journal pod blokadą sesji). RefreshDriftAsync nigdy nie rzuca.
            await RefreshDriftAsync().ConfigureAwait(true);
            _operationText = null;
            IsBusy = false;
        }

        // Licznik „Historia zmian” w menu po detekcji – inaczej niż baner już po zwolnieniu IsBusy (RefreshHistory): historia
        // trzyma blokadę sesji tylko na Journal.Load (milisekundy), a kolizja z klikiem to nieszkodliwe „Trwa inna operacja –
        // nic nie zmieniono”; tłumaczenie SID-ów poza blokadą może trwać sekundy i nie może wydłużać IsBusy.
        RefreshHistory();
    }

    /// <summary>
    /// „Cofnij” w Historii zmian: wpis autostartu = <see cref="UndoAutostartAsync"/> (okno serii, cofanie po
    /// <see cref="HistoryEntry.EntryId"/>); każdy inny wpis = <see cref="RunAsync(string, GuiOperation)"/> po id pozycji, jak dotąd.
    /// </summary>
    public Task UndoFromHistoryAsync(HistoryEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        return entry.Kind == HistoryKind.Autostart ? UndoAutostartAsync(entry) : RunAsync(entry.TweakId, GuiOperation.Undo);
    }

    /// <summary>
    /// „Cofnij” z historii: pozycja po id, dalej <see cref="RunAsync(ItemViewModel, GuiOperation)"/> (te same potwierdzenia i raport
    /// co z listy). Id spoza wczytanego katalogu = jedna linia ostrzeżenia w dzienniku i nic więcej.
    /// </summary>
    public Task RunAsync(string id, GuiOperation operation)
    {
        if (id is not null && _items.TryGetValue(id, out var item))
        {
            return RunAsync(item, operation);
        }

        _sink.Add(GuiLogLevel.Warning, GuiText.ItemNotFound(operation, id ?? string.Empty));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Wołane przez przyciski panelu szczegółów. Najpierw (bez IsBusy) okno potwierdzenia, potem operacja:
    /// cała ścieżka jest w try/catch/finally, IsBusy zawsze zwolnione.
    /// </summary>
    public async Task RunAsync(ItemViewModel item, GuiOperation operation)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (IsBusy || _isConfirming)
        {
            return;
        }

        // Długa naprawa: okno postępu (potwierdzenie → procent i etap → wynik), pozostałe akcje bez zmian.
        if (operation == GuiOperation.Execute && GuiRules.UsesProgressWindow(item.Definition))
        {
            await RunActionWindowAsync(item).ConfigureAwait(true);
            return;
        }

        var id = item.Id;
        var userName = _userName;
        var confirmed = await ConfirmAsync(
            () => _session.ConfirmationForItem(id, operation, userName),
            confirmation => _seriesDialogs.ConfirmAsync(confirmation),
            _ => CanceledText(operation, item.Title)).ConfigureAwait(true);
        if (confirmed is null)
        {
            return;
        }

        await RunOperationAsync($"{GuiText.OperationName(operation)}: {item.Title}…", async () =>
        {
            GuiOperationReport report;
            using (_sink.BeginOperation(new[] { id }))
            {
                report = await Task.Run(() => _session.Run(id, operation, confirmed.Value)).ConfigureAwait(true);
                _sink.CompleteOperation(report);
            }

            await ApplyReportAsync(report).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Długa naprawa w oknie postępu – ta sama ścieżka co <see cref="RunSeriesWindowAsync"/>: model potwierdzenia przez Task.Run,
    /// <see cref="_isConfirming"/> w potwierdzeniu, IsBusy od zgody, <see cref="UiLogSink.BeginOperation"/>, raport w dzienniku
    /// i stanach (<see cref="ApplyReportAsync"/>) zanim okno pokaże wynik, restart z wyniku po zamknięciu okna. Postęp idzie do
    /// okna i do dziennika (linie <see cref="ActionProgressLog"/>: zmiana etapu i co 10%, na wątku UI); „Zatrzymaj po tym
    /// kroku” anuluje token, który handler sprawdza tylko między krokami.
    /// </summary>
    private async Task RunActionWindowAsync(ItemViewModel item)
    {
        IsConfirming = true;
        var restartRequested = false;
        var id = item.Id;
        try
        {
            var userName = _userName;
            var confirmation = await Task.Run(() => _session.ConfirmationForItem(id, GuiOperation.Execute, userName)).ConfigureAwait(true);
            if (confirmation is null)
            {
                // Akcja zawsze wymaga potwierdzenia (GuiRules.RequiresConfirmation) – tu nie dochodzi.
                return;
            }

            var operationText = $"{GuiText.OperationName(GuiOperation.Execute)}: {item.Title}…";
            var progressLog = new ActionProgressLog(item.Title);
            var applied = false;
            var result = await _seriesDialogs.RunActionAsync(confirmation, async (progress, stop) =>
            {
                _operationText = operationText;
                IsBusy = true;
                IsConfirming = false;

                // Progress utworzony na wątku UI: linie dziennika dopisywane na wątku UI, w kolejności zdarzeń.
                var logged = new Progress<ActionProgress>(p =>
                {
                    foreach (var line in progressLog.OnProgress(p))
                    {
                        _sink.Add(GuiLogLevel.Info, line);
                    }
                });
                var both = new FanOutProgress<ActionProgress>(progress, logged);
                GuiOperationReport report;
                using (_sink.BeginOperation(new[] { id }))
                {
                    report = await Task.Run(() => _session.RunAction(id, confirmed: true, both, stop)).ConfigureAwait(true);
                    _sink.CompleteOperation(report);
                }

                // Wynik od razu w głównym oknie (za oknem wyniku); błąd odświeżenia tylko do dziennika.
                applied = true;
                try
                {
                    await ApplyReportAsync(report).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    ReportUnexpected(ex);
                }

                _operationText = null;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(PageSubtitle));
                return report;
            }).ConfigureAwait(true);

            restartRequested = result.RestartRequested;
            if (result.Report is null)
            {
                _sink.Add(GuiLogLevel.Info, CanceledText(GuiOperation.Execute, item.Title));
                return;
            }

            // Raport zastępczy po wyjątku (okno go zbudowało) – do dziennika dopiero teraz.
            if (!applied)
            {
                await ApplyReportAsync(result.Report).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            _operationText = null;
            IsConfirming = false;
            IsBusy = false;
        }

        if (restartRequested)
        {
            await RestartNowAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Okno potwierdzenia przed operacją, gdy pozycja go wymaga (model przez Task.Run – podgląd czyta rejestr i usługi). Zwraca: null = anulowano albo błąd (już w dzienniku),
    /// true = potwierdzono, false = potwierdzenie niepotrzebne. W tym czasie IsBusy jest false (pasek statusu nie udaje
    /// trwającej operacji), ale <see cref="_isConfirming"/> blokuje drugą operację i zamknięcie okna (<see cref="CanClose"/>).
    /// Przy zgodzie flaga zostaje do <see cref="RunOperationAsync"/> (zdejmuje ją dopiero po IsBusy = true), więc między
    /// zamknięciem okna potwierdzenia a startem operacji nie ma chwili bez blokady.
    /// </summary>
    private async Task<bool?> ConfirmAsync<T>(Func<T?> request, Func<T, Task<bool>> show, Func<T, string> cancelledText)
        where T : class
    {
        IsConfirming = true;
        bool? result = null;
        try
        {
            var pending = await Task.Run(request).ConfigureAwait(true);
            if (pending is null)
            {
                result = false;
            }
            else if (await show(pending).ConfigureAwait(true))
            {
                result = true;
            }
            else
            {
                _sink.Add(GuiLogLevel.Info, cancelledText(pending));
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            if (result is null)
            {
                IsConfirming = false;
            }
        }

        return result;
    }

    /// <summary>Wspólny szkielet operacji: IsBusy przed pracą, wyjątki do dziennika, zawsze zwolnienie IsBusy.</summary>
    private async Task RunOperationAsync(string operationText, Func<Task> body)
    {
        _operationText = operationText;
        IsBusy = true;
        IsConfirming = false;
        try
        {
            await body().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            _operationText = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Linie raportu do dziennika, nowe stany pozycji, czas sprawdzenia, potem baner restartu z journala, baner strażnika ustawień
    /// i odświeżenie historii (licznik w menu, stan wpisów; bez czekania – <see cref="RefreshHistory"/>).
    /// </summary>
    private async Task ApplyReportAsync(GuiOperationReport report)
    {
        foreach (var line in report.Lines)
        {
            _sink.Add(line);
        }

        ApplyStates(report.Refreshed);
        if (report.Refreshed.Count > 0)
        {
            _lastCheck = _clock.UtcNow;
        }

        await RefreshRebootAsync().ConfigureAwait(true);
        await RefreshDriftAsync().ConfigureAwait(true);
        RefreshHistory();
    }

    /// <summary>
    /// Odświeża historię bez czekania na wynik: odczyt tłumaczy SID-y kont (LSA, w domenie bez kontrolera nawet sekundy),
    /// więc nie może wydłużać IsBusy operacji ani detekcji. Journal czyta pod blokadą sesji tylko na czas odczytu pliku.
    /// <see cref="HistoryViewModel.RefreshAsync"/> nigdy nie rzuca, a starszy wynik po nowszym jest pomijany.
    /// </summary>
    private void RefreshHistory() => _ = History.RefreshAsync();

    /// <summary>
    /// Przelicza baner restartu: czas startu systemu czytany raz na sesję (Task.Run), <see cref="GuiSession.PendingReboot"/>
    /// też przez Task.Run (czyta journal pod blokadą sesji). Wynik starszego przeliczenia, które skończyło się później, jest pomijany.
    /// Ostrzeżenie (np. uszkodzony journal) trafia do dziennika raz na sesję. Nigdy nie rzuca.
    /// </summary>
    private async Task RefreshRebootAsync()
    {
        var version = ++_rebootVersion;
        try
        {
            var lastBoot = await LastBootAsync().ConfigureAwait(true);
            var status = await Task.Run(() => _session.PendingReboot(lastBoot, _sessionStart)).ConfigureAwait(true);
            if (status.Warning is { } warning && !_rebootWarningShown)
            {
                _rebootWarningShown = true;
                _sink.Add(GuiLogLevel.Warning, warning);
            }

            if (version == _rebootVersion)
            {
                PendingRebootTitles = status.Titles;
                foreach (var item in _items.Values)
                {
                    item.PendingReboot = !status.Ids.Contains(item.Id)
                        ? null
                        : status.UndoneIds.Contains(item.Id) ? RebootPendingKind.Undone : RebootPendingKind.Applied;
                }
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
    }

    /// <summary>
    /// Przelicza baner strażnika ustawień z ostatnich stanów detekcji (tylko tweaki ze znanym stanem; słownik budowany na wątku UI)
    /// i journala: <see cref="GuiSession.DriftedSettings"/> przez Task.Run (journal pod blokadą sesji), bez detekcji i bez zapisu.
    /// Wynik starszego przeliczenia, które skończyło się później, jest pomijany. Nigdy nie rzuca.
    /// Woła go tylko detekcja (<see cref="InitializeAsync"/>) i raport tweaków (<see cref="ApplyReportAsync"/>); raporty usuwania
    /// i instalacji go nie wołają.
    /// </summary>
    internal async Task RefreshDriftAsync()
    {
        var version = ++_driftVersion;
        try
        {
            var states = new Dictionary<string, DetectionState>(StringComparer.Ordinal);
            foreach (var item in _items.Values)
            {
                if (item.IsTweak && item.State is { } state)
                {
                    states[item.Id] = state;
                }
            }

            var lastBoot = await LastBootAsync().ConfigureAwait(true);
            var ids = await Task.Run(() => _session.DriftedSettings(states, lastBoot, _sessionStart)).ConfigureAwait(true);
            if (version == _driftVersion)
            {
                DriftBanner.Update(ids);
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
    }

    /// <summary>
    /// „Zaznacz i pokaż” na banerze strażnika: dokłada do zaznaczenia podane tweaki (niczego nie odznacza), poza stroną kategorii
    /// przechodzi na ostatnią kategorię, czyści wyszukiwanie i otwiera widok „Zaznaczone”. Filtra ryzyka nie zmienia (ukryte pokazuje
    /// linia „Ukryte przez filtr: N”) i niczego nie stosuje – dalej zwykłe „Zastosuj (N)”. W trakcie operacji nic nie robi.
    /// </summary>
    private void SelectAndShowDrifted(IReadOnlyList<string> ids)
    {
        if (IsBusy)
        {
            return;
        }

        foreach (var id in ids)
        {
            if (_items.TryGetValue(id, out var item) && item.IsTweak)
            {
                item.IsSelected = true;
            }
        }

        if (SelectedPage is not CategoryViewModel)
        {
            var category = LastCategory;
            if (category is null)
            {
                return;
            }

            SelectedPage = category;
        }

        SearchText = string.Empty;
        ShowFilter = ShowFilter.Selected;
    }

    /// <summary>Czas startu systemu dla banera i historii: czytany raz na sesję, przez Task.Run (wołać z wątku UI).</summary>
    private Task<DateTimeOffset?> LastBootAsync() => _lastBoot ??= Task.Run(ReadLastBoot);

    /// <summary>„Uruchom ponownie” na banerze: najpierw potwierdzenie w skórce, przy zgodzie <see cref="RestartNowAsync"/>.</summary>
    private async Task RestartFromBannerAsync()
    {
        if (IsBusy || IsConfirming)
        {
            return;
        }

        IsConfirming = true;
        bool confirmed;
        try
        {
            confirmed = await _seriesDialogs.ConfirmRestartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
            IsConfirming = false;
            return;
        }

        if (!confirmed)
        {
            IsConfirming = false;
            return;
        }

        // Przy zgodzie IsConfirming zdejmuje dopiero RunOperationAsync (po IsBusy = true) – bez chwili bez blokady.
        await RestartNowAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Restart po zgodzie (baner albo „Uruchom ponownie teraz” w wyniku serii): linia Info, potem <see cref="IRestarter.Restart"/>
    /// przez Task.Run. Każdy wyjątek = jedna linia Error w dzienniku, baner zostaje. Na czas zlecenia IsBusy (bez drugiej operacji).
    /// </summary>
    private Task RestartNowAsync() => RunOperationAsync(RestartingText, async () =>
    {
        _sink.Add(GuiLogLevel.Info, RestartingText);
        try
        {
            await Task.Run(() => _restarter.Restart()).ConfigureAwait(true);
        }
        catch (OperationException ex)
        {
            _sink.Add(GuiLogLevel.Error, ex.Message);
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
    });

    /// <summary>
    /// Zamknięcie okna jest dozwolone tylko poza operacją i poza oknem potwierdzenia (to drugie jest modalne,
    /// więc zwykle i tak blokuje okno, ale np. „Zamknij okno” z paska zadań nie może przejść); przy blokadzie
    /// dopisuje ostrzeżenie do logu.
    /// </summary>
    public bool CanClose()
    {
        if (!IsBusy && !_isConfirming)
        {
            return true;
        }

        _sink.Add(GuiLogLevel.Warning, L.T("Trwa operacja – zamknij okno po jej zakończeniu."));
        return false;
    }

    /// <summary>
    /// Przelicza liczniki paska zaznaczenia (wołane przez pozycję przy zmianie zaznaczenia albo stanu). W widoku „Zaznaczone” nowo zaznaczona
    /// pozycja dochodzi do widoku, a odznaczona w nim zostaje (bez skakania listy) do wyjścia z widoku.
    /// </summary>
    internal void RefreshSelection()
    {
        var selected = _items.Values.Where(item => item.IsSelected).ToList();
        if (_selectedViewIds is not null)
        {
            var joined = false;
            foreach (var item in selected)
            {
                joined |= _selectedViewIds.Add(item.Id);
            }

            if (joined)
            {
                RefreshSearch();
            }
        }

        SelectedCount = selected.Count;
        ApplicableCount = selected.Count(item => GuiRules.CanApply(item.Definition, item.State));
        UndoableCount = selected.Count(item => GuiRules.CanUndo(item.Definition, item.State, item.PendingReboot));
        OnPropertyChanged(nameof(HasSelection));
        _selectionBarDetails = selected.Count == 0
            ? string.Empty
            : GuiText.SelectionBarDetails(selected.Count(item => item.Definition.RequiresReboot), ApplicableCount > 0, restorePoint: _session.CreateRestorePoint);
        OnPropertyChanged(nameof(SelectionBarTitle));
        OnPropertyChanged(nameof(SelectionBarDetails));
        OnPropertyChanged(nameof(ApplySelectedText));
        OnPropertyChanged(nameof(UndoSelectedText));
        ApplySelectedCommand.NotifyCanExecuteChanged();
        UndoSelectedCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        ShowSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// „Zastosuj/Cofnij zaznaczone”: pozycje wg bieżącego stanu (CanApply/CanUndo), jedno potwierdzenie, jedno RunBatch poza wątkiem UI.
    /// Po serii odznacza tylko te pozycje, których własny wynik był udany – nieudane zostają zaznaczone.
    /// </summary>
    private async Task RunBatchAsync(GuiOperation operation)
    {
        if (IsBusy || _isConfirming)
        {
            return;
        }

        var ids = _items.Values
            .Where(item => item.IsSelected
                && (operation == GuiOperation.Apply
                    ? GuiRules.CanApply(item.Definition, item.State)
                    : GuiRules.CanUndo(item.Definition, item.State, item.PendingReboot)))
            .Select(item => item.Id)
            .ToList();
        if (ids.Count == 0)
        {
            return;
        }

        if (ids.Count > 1)
        {
            await RunSeriesWindowAsync(ids, operation).ConfigureAwait(true);
            return;
        }

        var userName = _userName;
        var confirmed = await ConfirmAsync(
            () => _session.ConfirmationForItem(ids[0], operation, userName),
            confirmation => _seriesDialogs.ConfirmAsync(confirmation),
            _ => GuiText.CancelledBatch(operation, ids.Select(id => _items[id].Title).ToList())).ConfigureAwait(true);
        if (confirmed is null)
        {
            return;
        }

        await RunOperationAsync(SelectedOperationText(operation, ids.Count), async () =>
        {
            GuiOperationReport report;
            using (_sink.BeginOperation(ids))
            {
                report = await Task.Run(() => _session.RunBatch(ids, operation, confirmed.Value)).ConfigureAwait(true);
                _sink.CompleteOperation(report);
            }

            await ApplyBatchReportAsync(report).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>Raport serii jak <see cref="ApplyReportAsync"/>, potem odznaczenie udanych pozycji.</summary>
    private async Task ApplyBatchReportAsync(GuiOperationReport report)
    {
        await ApplyReportAsync(report).ConfigureAwait(true);
        foreach (var id in report.SucceededIds)
        {
            if (_items.TryGetValue(id, out var done))
            {
                done.IsSelected = false;
            }
        }
    }

    /// <summary>
    /// Seria 2+ pozycji w oknie serii. Model potwierdzenia przez Task.Run (podgląd zmian czyta rejestr i usługi), w tym czasie
    /// i w trakcie potwierdzenia <see cref="_isConfirming"/> blokuje inne operacje i zamknięcie okna. IsBusy dopiero od zgody
    /// (start serii w oknie), więc pasek statusu nie udaje operacji podczas potwierdzenia. Linie raportu, stany i odznaczenie
    /// udanych już po serii, zanim okno pokaże wynik (IsBusy do zamknięcia okna); po wyjątku – raport zastępczy po oknie.
    /// Anulowanie = <see cref="GuiText.CancelledBatch"/>.
    /// </summary>
    private async Task RunSeriesWindowAsync(List<string> ids, GuiOperation operation)
    {
        IsConfirming = true;
        var restartRequested = false;
        try
        {
            var userName = _userName;
            var confirmation = await Task.Run(() => _session.ConfirmationForSeries(ids, operation, userName)).ConfigureAwait(true);
            if (confirmation is null)
            {
                // Seria 2+ pozycji zawsze wymaga potwierdzenia (GuiRules.SeriesRequiresConfirmation) – tu nie dochodzi.
                return;
            }

            var operationText = SelectedOperationText(operation, ids.Count);
            var applied = false;
            var result = await _seriesDialogs.RunSeriesAsync(confirmation, async (steps, stop) =>
            {
                _operationText = operationText;
                IsBusy = true;
                IsConfirming = false;
                GuiOperationReport report;
                using (_sink.BeginOperation(ids))
                {
                    report = await Task.Run(() => _session.RunBatch(ids, operation, confirmed: true, steps, stop)).ConfigureAwait(true);
                    _sink.CompleteOperation(report);
                }

                // Wynik od razu w głównym oknie (za oknem wyniku): linie dziennika, stany, baner, odznaczenie udanych.
                // Błąd odświeżenia tylko do dziennika – udana seria nie staje się w oknie „Zakończono z błędem”.
                applied = true;
                try
                {
                    await ApplyBatchReportAsync(report).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    ReportUnexpected(ex);
                }

                // IsBusy zostaje do zamknięcia okna wyniku; pasek statusu już nie udaje trwającej serii.
                _operationText = null;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(PageSubtitle));
                return report;
            }).ConfigureAwait(true);

            // Od razu: zgoda na restart padła w oknie i nie może przepaść przez wyjątek niżej.
            restartRequested = result.RestartRequested;
            if (result.Report is null)
            {
                _sink.Add(GuiLogLevel.Info, GuiText.CancelledBatch(operation, ids.Select(id => _items[id].Title).ToList()));
                return;
            }

            // Raport zastępczy po wyjątku w serii (okno go zbudowało) – do dziennika dopiero teraz.
            if (!applied)
            {
                await ApplyBatchReportAsync(result.Report).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            _operationText = null;
            IsConfirming = false;
            IsBusy = false;
        }

        // „Uruchom ponownie teraz” w wyniku: zgoda już padła w oknie (ConfirmRestartAsync), ta sama ścieżka co baner.
        if (restartRequested)
        {
            await RestartNowAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// „Usuń (N)” ze strony „Aplikacje” – zawsze w oknie serii, także dla jednej aplikacji. Model potwierdzenia przez Task.Run
    /// (ochrona systemu, nazwy kont); w tym czasie i w potwierdzeniu <see cref="_isConfirming"/> blokuje inne operacje.
    /// Seria biegnie poza wątkiem UI pod blokadą sesji; pytanie „Kontynuować bez punktu przywracania?” wraca z wątku silnika
    /// na wątek UI (<see cref="AskWithoutRestorePoint"/>). Po serii: dziennik, baner restartu, Historia i świeży odczyt listy.
    /// </summary>
    private async Task RemoveAppsAsync(IReadOnlyList<AppRow> rows)
    {
        if (IsBusy || _isConfirming || rows.Count == 0)
        {
            return;
        }

        IsConfirming = true;
        var restartRequested = false;
        var ids = rows.Select(row => row.App.Id).ToList();
        var restorePointSetting = _session.CreateRestorePoint;
        var ui = SynchronizationContext.Current;
        // Zamykanie programu w trakcie pytania o punkt przywracania = „Nie” (wątek silnika nie wisi z blokadą sesji).
        using var shutdown = new CancellationTokenSource();
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        EventHandler onShutdown = (_, _) => shutdown.Cancel();
        dispatcher.ShutdownStarted += onShutdown;
        try
        {
            var confirmation = await Task.Run(() => _apps.ConfirmationFor(rows, restorePointSetting)).ConfigureAwait(true);
            var operationText = SelectedAppsOperationText(GuiOperation.RemoveApps, rows.Count);
            var applied = false;
            var result = await _seriesDialogs.RunSeriesAsync(confirmation, async (steps, stop) =>
            {
                _operationText = operationText;
                IsBusy = true;
                IsConfirming = false;
                var options = new AppRemovalOptions
                {
                    RestorePointSetting = restorePointSetting,
                    Steps = steps,
                    Stop = stop,
                    ContinueWithoutRestorePoint = point => AskWithoutRestorePoint(ui, point, shutdown.Token),
                };
                GuiOperationReport report;
                using (_sink.BeginOperation(ids))
                {
                    report = await Task.Run(() => _apps.Remove(rows, options)).ConfigureAwait(true);
                    _sink.CompleteOperation(report);
                }

                applied = true;
                try
                {
                    await ApplyRemovalReportAsync(report).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    ReportUnexpected(ex);
                }

                _operationText = null;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(PageSubtitle));
                return report;
            }).ConfigureAwait(true);

            restartRequested = result.RestartRequested;
            if (result.Report is null)
            {
                _sink.Add(GuiLogLevel.Info, GuiText.CancelledBatch(GuiOperation.RemoveApps, rows.Select(row => row.App.Name).ToList()));
                return;
            }

            if (!applied)
            {
                await ApplyRemovalReportAsync(result.Report).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            dispatcher.ShutdownStarted -= onShutdown;
            _operationText = null;
            IsConfirming = false;
            IsBusy = false;
        }

        if (restartRequested)
        {
            await RestartNowAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Raport usuwania: linie do dziennika, baner restartu (journal), Historia i świeży odczyt listy aplikacji.</summary>
    private async Task ApplyRemovalReportAsync(GuiOperationReport report)
    {
        foreach (var line in report.Lines)
        {
            _sink.Add(line);
        }

        await RefreshRebootAsync().ConfigureAwait(true);
        // Bez RefreshDriftAsync: tu nie ma detekcji tweaków, a journal zmieniają tylko wpisy z Kind.
        RefreshHistory();
        _ = Apps.RefreshAsync();
    }

    /// <summary>
    /// „Wyłącz (N)”/„Włącz (M)” ze strony „Autostart” – zawsze w oknie serii, także dla jednej pozycji (wzorzec <see cref="RemoveAppsAsync"/>),
    /// bez pytania o punkt przywracania i bez banera restartu (zmiana działa od następnego logowania). Seria biegnie poza wątkiem UI pod
    /// blokadą sesji. Po serii: dziennik, Historia i świeży odczyt strony.
    /// </summary>
    private Task ToggleAutostartAsync(IReadOnlyList<AutostartRow> rows, AutostartState target)
    {
        if (rows.Count == 0)
        {
            return Task.CompletedTask;
        }

        var operation = target == AutostartState.Disabled ? GuiOperation.DisableAutostart : GuiOperation.EnableAutostart;
        return RunAutostartSeriesAsync(
            () => _autostart.ConfirmationFor(rows, target),
            rows.Select(row => row.Id).ToList(),
            SelectedOperationText(operation, rows.Count),
            options => _autostart.Set(rows, target, options),
            () => GuiText.CancelledBatch(operation, rows.Select(row => row.Entry.Name).ToList()));
    }

    /// <summary>
    /// „Cofnij” wpisu autostartu z Historii zmian – w oknie serii z jedną pozycją (<see cref="AutostartSession.ConfirmationForUndo"/>), ta sama
    /// ścieżka co <see cref="ToggleAutostartAsync"/>: cofanie po <see cref="HistoryEntry.EntryId"/> poza wątkiem UI pod blokadą sesji (silnik sam
    /// sprawdza jeszcze raz, czy wpis jest aktywny, najnowszy i tego konta), linie do dziennika, Historia i świeży odczyt strony „Autostart”.
    /// Katalog tweaków nie jest pytany o id pozycji.
    /// </summary>
    private Task UndoAutostartAsync(HistoryEntry entry)
        => RunAutostartSeriesAsync(
            () => _autostart.ConfirmationForUndo(entry),
            new[] { entry.TweakId },
            $"{GuiText.OperationName(GuiOperation.Undo)}: {entry.Title}…",
            options => _autostart.Undo(entry, options),
            () => CanceledText(GuiOperation.Undo, entry.Title));

    /// <summary>
    /// Wspólny przebieg serii autostartu w oknie serii (wzorzec <see cref="RemoveAppsAsync"/>): potwierdzenie, IsBusy od zgody, praca
    /// <paramref name="run"/> przez Task.Run, raport w dzienniku i Historia; anulowanie = linia <paramref name="cancelledText"/> i nic więcej.
    /// Odczyt strony „Autostart” po zwolnieniu IsBusy (w trakcie serii odświeżenie jest nieaktywne).
    /// </summary>
    private async Task RunAutostartSeriesAsync(
        Func<SeriesConfirmation> confirmationFor,
        IReadOnlyList<string> ids,
        string operationText,
        Func<AutostartRunOptions, GuiOperationReport> run,
        Func<string> cancelledText)
    {
        if (IsBusy || _isConfirming)
        {
            return;
        }

        IsConfirming = true;
        try
        {
            var confirmation = confirmationFor();
            var applied = false;
            var result = await _seriesDialogs.RunSeriesAsync(confirmation, async (steps, stop) =>
            {
                _operationText = operationText;
                IsBusy = true;
                IsConfirming = false;
                var options = new AutostartRunOptions { Steps = steps, Stop = stop };
                GuiOperationReport report;
                using (_sink.BeginOperation(ids))
                {
                    report = await Task.Run(() => run(options)).ConfigureAwait(true);
                    _sink.CompleteOperation(report);
                }

                applied = true;
                ApplyAutostartReport(report);
                _operationText = null;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(PageSubtitle));
                return report;
            }).ConfigureAwait(true);

            if (result.Report is null)
            {
                _sink.Add(GuiLogLevel.Info, cancelledText());
                return;
            }

            if (!applied)
            {
                ApplyAutostartReport(result.Report);
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            _operationText = null;
            IsConfirming = false;
            IsBusy = false;
        }

        // Odczyt strony po zwolnieniu IsBusy (w trakcie serii odświeżenie jest nieaktywne).
        _ = Autostart.RefreshAsync();
    }

    /// <summary>
    /// Raport serii autostartu: linie do dziennika, odznaczenie pozycji zmienionych z powodzeniem (jak w serii tweaków i instalacji –
    /// inaczej od razu uzbroiłyby przycisk w drugą stronę) i Historia (restart nie jest potrzebny).
    /// </summary>
    private void ApplyAutostartReport(GuiOperationReport report)
    {
        foreach (var line in report.Lines)
        {
            _sink.Add(line);
        }

        Autostart.Deselect(report.SucceededIds);
        RefreshHistory();
    }

    /// <summary>
    /// „Zainstaluj (N)” ze strony „Instalacja” – zawsze w oknie serii, także dla jednej aplikacji (wzorzec <see cref="RemoveAppsAsync"/>).
    /// Treść potwierdzenia przez Task.Run (<see cref="InstallSession.ConfirmationFor"/>), w potwierdzeniu <see cref="_isConfirming"/>
    /// blokuje inne operacje. Seria biegnie poza wątkiem UI pod blokadą sesji; postęp idzie do okna i do dziennika (linie
    /// <see cref="InstallProgressLog"/>: zmiana etapu pozycji, na wątku UI). Po serii: dziennik, odznaczenie zainstalowanych, baner
    /// restartu, Historia i świeży odczyt strony (po zwolnieniu IsBusy). „Uruchom ponownie teraz” jak w <see cref="RemoveAppsAsync"/>.
    /// </summary>
    private async Task InstallAppsAsync(IReadOnlyList<InstallTarget> targets)
    {
        if (IsBusy || _isConfirming || targets.Count == 0)
        {
            return;
        }

        IsConfirming = true;
        var restartRequested = false;
        try
        {
            var confirmation = await Task.Run(() => _install.ConfirmationFor(targets)).ConfigureAwait(true);
            var operationText = SelectedAppsOperationText(GuiOperation.InstallApps, targets.Count);
            var titles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in confirmation.Items)
            {
                titles[item.Id] = item.Title;
            }

            var progressLog = new InstallProgressLog(titles);
            var ids = confirmation.Items.Select(item => item.Id).ToList();
            var applied = false;
            var result = await _seriesDialogs.RunSeriesAsync(confirmation, async (steps, stop) =>
            {
                _operationText = operationText;
                IsBusy = true;
                IsConfirming = false;

                // Progress utworzony na wątku UI: linie etapu (Pobieranie / Instalowanie) dopisywane na wątku UI, w kolejności zdarzeń.
                var logged = new Progress<BatchStepEvent>(step =>
                {
                    if (progressLog.OnStep(step) is { } line)
                    {
                        _sink.Add(GuiLogLevel.Info, line);
                    }
                });
                var both = new FanOutProgress<BatchStepEvent>(steps, logged);
                GuiOperationReport report;
                using (_sink.BeginOperation(ids))
                {
                    report = await Task.Run(() => _install.Install(targets, both, stop)).ConfigureAwait(true);
                    _sink.CompleteOperation(report);
                }

                applied = true;
                try
                {
                    await ApplyInstallReportAsync(report).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    ReportUnexpected(ex);
                }

                _operationText = null;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(PageSubtitle));
                return report;
            }).ConfigureAwait(true);

            restartRequested = result.RestartRequested;
            if (result.Report is null)
            {
                _sink.Add(GuiLogLevel.Info, GuiText.CancelledBatch(GuiOperation.InstallApps, confirmation.Items.Select(item => item.Title).ToList()));
                return;
            }

            if (!applied)
            {
                await ApplyInstallReportAsync(result.Report).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            _operationText = null;
            IsConfirming = false;
            IsBusy = false;
        }

        if (restartRequested)
        {
            await RestartNowAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Raport instalacji: linie do dziennika, odznaczenie zainstalowanych, baner restartu (journal), Historia i odczyt strony
    /// „Instalacja” – ten ostatni rusza dopiero po zwolnieniu IsBusy (Load biegnie poza blokadą sesji).
    /// </summary>
    private async Task ApplyInstallReportAsync(GuiOperationReport report)
    {
        foreach (var line in report.Lines)
        {
            _sink.Add(line);
        }

        Install.OnInstalled(report.SucceededIds);
        await RefreshRebootAsync().ConfigureAwait(true);
        // Bez RefreshDriftAsync: tu nie ma detekcji tweaków, a journal zmieniają tylko wpisy z Kind.
        RefreshHistory();
        _ = Install.RefreshAsync();
    }

    /// <summary>
    /// „Przejdź do: Napraw winget” ze strony „Instalacja”: strona „Naprawy i narzędzia” z pozycją <c>repair.winget</c> w panelu
    /// szczegółów (filtr ryzyka wraca do „Wszystkie”, gdy ją chowa). Nic nie uruchamia – naprawę startuje użytkownik.
    /// </summary>
    private void ShowRepairWinget()
    {
        var category = Categories.FirstOrDefault(candidate => string.Equals(candidate.Id, GuiNav.ServicePageId, StringComparison.Ordinal));
        if (category is null)
        {
            return;
        }

        SelectedPage = category;
        if (_items.TryGetValue(RepairWingetId, out var item))
        {
            if (!category.ItemsView.Contains(item))
            {
                RiskFilter = RiskFilter.All;
            }

            category.ItemsView.MoveCurrentTo(item);
        }
    }

    /// <summary>Grupy menu po kolei, z linią (<see cref="NavSeparatorViewModel"/>) między niepustymi grupami; pusta grupa znika bez linii.</summary>
    private static List<object> WithSeparators(params IReadOnlyList<object>[] groups)
    {
        var items = new List<object>();
        foreach (var group in groups.Where(group => group.Count > 0))
        {
            if (items.Count > 0)
            {
                items.Add(new NavSeparatorViewModel());
            }

            items.AddRange(group);
        }

        return items;
    }

    /// <summary>
    /// Wołane z wątku silnika (pod blokadą sesji): pytanie w oknie serii na wątku UI, wątek silnika czeka na odpowiedź najwyżej
    /// <see cref="CrossThreadQuestion.DefaultTimeout"/>. Brak kontekstu UI, błąd okna, limit albo zamykanie programu = „Nie”
    /// (seria kończy się bez usuwania).
    /// </summary>
    private bool AskWithoutRestorePoint(SynchronizationContext? ui, RestorePointResult point, CancellationToken shutdown)
    {
        if (ui is null)
        {
            return false;
        }

        var reason = string.IsNullOrWhiteSpace(point.Message) ? point.Status.ToString() : point.Message!;
        return CrossThreadQuestion.Ask(
            work => ui.Post(_ => work(), null),
            () => _seriesDialogs.ConfirmContinueWithoutRestorePointAsync(reason),
            CrossThreadQuestion.DefaultTimeout,
            shutdown);
    }

    /// <summary>
    /// Wołane przez pozycję po zmianie stanu (detekcja, operacja). Licznik i widok kategorii odświeża sama kategoria;
    /// tu wyniki wyszukiwania i „Ukryte przez filtr” – stan zmienia widoczność tylko przy filtrze ryzyka innym niż „Wszystkie”
    /// albo przy „Niezastosowane”/„Zastosowane”.
    /// </summary>
    internal void OnItemStateChanged()
    {
        if (_riskFilter != RiskFilter.All || _showFilter is ShowFilter.NotApplied or ShowFilter.Applied)
        {
            RefreshSearch();
        }

        RefreshSelectSuggested();
    }

    /// <summary>Widoczność w wynikach: zbiór widoku „Zaznaczone” (poza nim wszystkie pozycje), zapytanie, ryzyko i „Pokaż”.</summary>
    private bool IsVisible(ItemViewModel item)
        => (_selectedViewIds is null || _selectedViewIds.Contains(item.Id))
            && ItemFilter.IsVisible(item.Definition, item.KnownState, _searchText, _riskFilter, _showFilter);

    private void ApplyCategoryFilters()
    {
        foreach (var category in Categories)
        {
            category.ApplyFilter(_riskFilter, _showFilter);
        }
    }

    /// <summary>
    /// Oba filtry naraz, z jednym odświeżeniem list. Zmiana „Niezastosowane”/„Zastosowane” → „Wszystkie” nie dotyczy widoku „Zaznaczone”,
    /// więc zbiór widoku, <see cref="IsSelectedView"/> i <see cref="UsesResultsView"/> się nie zmieniają.
    /// </summary>
    private void ShowAll()
    {
        var resetShow = _showFilter is ShowFilter.NotApplied or ShowFilter.Applied;
        if (_riskFilter == RiskFilter.All && !resetShow)
        {
            return;
        }

        SetProperty(ref _riskFilter, RiskFilter.All, nameof(RiskFilter));
        if (resetShow)
        {
            SetProperty(ref _showFilter, ShowFilter.All, nameof(ShowFilter));
        }

        ApplyCategoryFilters();
        RefreshSearch();
    }

    private void Escape()
    {
        if (_searchText.Length > 0)
        {
            SearchText = string.Empty;
        }
        else
        {
            LeaveSelectedView();
        }
    }

    private void ResetListView()
    {
        SearchText = string.Empty;
        LeaveSelectedView();
    }

    /// <summary>Wyjście z widoku „Zaznaczone” („Pokaż” wraca do „Wszystkie”); poza widokiem nic nie robi.</summary>
    private void LeaveSelectedView()
    {
        if (IsSelectedView)
        {
            ShowFilter = ShowFilter.All;
        }
    }

    /// <summary>Nowy filtr wyników; bieżący wynik, który wypadł z widoku, przechodzi na pierwszy widoczny.</summary>
    private void RefreshSearch()
    {
        _searchView.Refresh();
        if (_searchView.CurrentItem is not ItemViewModel current || !IsVisible(current))
        {
            _searchView.MoveCurrentToFirst();
        }

        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(UsesResultsView));
        OnPropertyChanged(nameof(IsShowFilterVisible));
        OnPropertyChanged(nameof(IsListGrouped));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(DetailItem));
        OnHiddenChanged();
        RefreshSelectSuggested();
    }

    /// <summary>Ustawienia widoczne teraz na liście: wyniki (wyszukiwanie, „Zaznaczone”) albo bieżąca kategoria, po jej filtrach.</summary>
    private IEnumerable<ItemViewModel> SuggestionViewItems()
    {
        if (UsesResultsView)
        {
            return _allItems.Where(item => item.IsTweak && IsVisible(item));
        }

        var category = SelectedCategory;
        return category is null
            ? Array.Empty<ItemViewModel>()
            : category.Items.Where(item => item.IsTweak && category.IsShown(item));
    }

    private List<ItemViewModel> SuggestedToSelect()
    {
        var view = SuggestionViewItems().ToList();
        var ids = new HashSet<string>(
            SuggestedPick.Settings(view.Select(item => new SuggestionCandidate(item.Id, item.IsSuggested, item.KnownState)).ToList()),
            StringComparer.Ordinal);
        return view.Where(item => ids.Contains(item.Id)).ToList();
    }

    private bool CanSelectSuggested() => !IsBusy && SuggestedToSelect().Count > 0;

    // RelayCommand.Execute nie sprawdza CanExecute, więc warunek jest też tutaj.
    private void SelectSuggested()
    {
        if (IsBusy)
        {
            return;
        }

        foreach (var item in SuggestedToSelect())
        {
            item.IsSelected = true;
        }
    }

    private void RefreshSelectSuggested()
    {
        OnPropertyChanged(nameof(IsSelectSuggestedVisible));
        SelectSuggestedCommand.NotifyCanExecuteChanged();
    }

    private void OnHiddenChanged()
    {
        OnPropertyChanged(nameof(FilterNoteText));
        OnPropertyChanged(nameof(HasFilterNote));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void OnCategoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender != SelectedCategory || UsesResultsView)
        {
            return;
        }

        if (e.PropertyName == nameof(CategoryViewModel.SelectedItem))
        {
            OnPropertyChanged(nameof(DetailItem));
        }
        else if (e.PropertyName is nameof(CategoryViewModel.HiddenCount) or nameof(CategoryViewModel.ShownDespiteFilterCount))
        {
            OnHiddenChanged();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.CreateRestorePoint))
        {
            RefreshSelection();
            Apps.RefreshSelection();
        }
    }

    /// <summary>„Odznacz wszystko”; w widoku „Zaznaczone” wiersze zostają z pustym polem wyboru (zbiór widoku się nie zmienia).</summary>
    private void ClearSelection()
    {
        foreach (var item in _items.Values)
        {
            item.IsSelected = false;
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var errors = 0;
        var warnings = 0;
        foreach (var line in _sink.Lines)
        {
            if (line.Level == GuiLogLevel.Error)
            {
                errors++;
            }
            else if (line.Level == GuiLogLevel.Warning)
            {
                warnings++;
            }
        }

        LogSummary = GuiText.LogSummary(errors, warnings);
        LogSummaryLevel = errors > 0 ? GuiLogLevel.Error : warnings > 0 ? GuiLogLevel.Warning : GuiLogLevel.Info;
        var last = _sink.Lines.Count > 0 ? _sink.Lines[_sink.Lines.Count - 1] : null;
        LastLogText = LogLineText.OneLineSummary(last);
        LastLogToolTip = last?.Text ?? string.Empty;
    }

    private void ApplyStates(IReadOnlyDictionary<string, DetectionState> states)
    {
        foreach (var pair in states)
        {
            ApplyState(pair.Key, pair.Value);
        }
    }

    private void ApplyState(string id, DetectionState state)
    {
        if (_items.TryGetValue(id, out var item))
        {
            item.SetState(state);
        }
    }

    private void ReportUnexpected(Exception ex)
        => _sink.Add(GuiLogLevel.Error, L.F("Nieoczekiwany błąd: {0}: {1}", ex.GetType().Name, ex.Message));

    /// <summary>Jeden postęp do kilku odbiorców (okno i dziennik); każdy sam przenosi zdarzenie na wątek UI.</summary>
    private sealed class FanOutProgress<T> : IProgress<T>
    {
        private readonly IProgress<T>[] _targets;

        public FanOutProgress(params IProgress<T>[] targets) => _targets = targets;

        public void Report(T value)
        {
            foreach (var target in _targets)
            {
                target.Report(value);
            }
        }
    }
}
