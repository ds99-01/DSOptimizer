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
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
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
    public const string CheckingText = "Sprawdzanie stanu…";
    public const string NotCheckedText = "Stan nie został jeszcze sprawdzony";
    public const string RestartingText = "Uruchamianie ponownie komputera…";

    /// <summary>Cel „Przejdź do: Napraw winget” (strona „Instalacja”): kategoria „Naprawy” i pozycja naprawy winget.</summary>
    private const string RepairCategoryId = "repair";
    private const string RepairWingetId = "repair.winget";

    private readonly GuiSession _session;
    private readonly AppsSession _apps;
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
    private readonly SuggestedSelection _suggestedSelection = new();
    private string _searchText = string.Empty;
    private RiskFilter _riskFilter = RiskFilter.All;
    private bool _isBusy;
    private bool _isConfirming;
    private Task<DateTimeOffset?>? _lastBoot;
    private int _rebootVersion;
    private bool _rebootWarningShown;
    private IReadOnlyList<string> _pendingRebootTitles = Array.Empty<string>();
    private string? _operationText;
    private DateTimeOffset? _lastCheck;
    private string _logSummary = GuiText.LogSummary(0, 0);
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
        InstallSession install,
        SpecificationViewModel specification,
        PrepViewModel prep,
        SettingsViewModel settings)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _apps = apps ?? throw new ArgumentNullException(nameof(apps));
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
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy && SelectedCount > 0);
        ShowAllCommand = new RelayCommand(() => RiskFilter = RiskFilter.All);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        RestartCommand = new AsyncRelayCommand(RestartFromBannerAsync, () => !IsBusy && !IsConfirming);
        ShowPendingCommand = new RelayCommand(ShowPendingReboot);

        Categories = session.Categories.Select(category => new CategoryViewModel(category, this)).ToList();
        _allItems = Categories.SelectMany(category => category.Items).ToList();
        foreach (var item in _allItems)
        {
            _items[item.Id] = item;
        }

        foreach (var category in Categories)
        {
            category.PropertyChanged += OnCategoryChanged;
        }

        // Wyniki wyszukiwania: wszystkie pozycje w kolejności kategorii (w kategorii najpierw tweaki), nagłówek = kategoria.
        _searchView = new ListCollectionView(_allItems.ToList()) { Filter = item => item is ItemViewModel vm && IsVisible(vm) };
        _searchView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ItemViewModel.CategoryName)));
        _searchView.CurrentChanged += (_, _) => OnPropertyChanged(nameof(DetailItem));
        RiskFilterOptions = new[] { RiskFilter.All, RiskFilter.NoExperimental, RiskFilter.SafeOnly }
            .Select(filter => new RiskFilterOption(filter, GuiText.RiskFilterName(filter)))
            .ToList();

        Specification = specification ?? throw new ArgumentNullException(nameof(specification));
        Prep = prep ?? throw new ArgumentNullException(nameof(prep));
        // Historia: odczyt journala i nazw kont poza wątkiem UI, z tym samym czasem startu systemu co baner restartu.
        // „Cofnij” idzie tą samą ścieżką co z listy (potwierdzenie, raport, baner, odświeżenie historii).
        History = new HistoryViewModel(
            async () =>
            {
                var lastBoot = await LastBootAsync().ConfigureAwait(true);
                return await Task.Run(() => _session.LoadHistory(lastBoot, _sessionStart, accountNames)).ConfigureAwait(true);
            },
            id => RunAsync(id, GuiOperation.Undo),
            () => SelectedPage = Apps,
            (level, text) => _sink.Add(level, text));
        // Aplikacje: własne zaznaczenie i „Usuń (N)” zawsze przez okno serii (RemoveAppsAsync), osobno od „Zastosuj/Cofnij”.
        Apps = new AppsViewModel(apps, accountNames, RemoveAppsAsync, () => _session.CreateRestorePoint, (level, text) => _sink.Add(level, text), _clock);
        // Instalacja: własne zaznaczenie i „Zainstaluj (N)” zawsze przez okno serii (InstallAppsAsync); „Napraw winget” = Naprawy.
        Install = new InstallViewModel(install, InstallAppsAsync, ShowRepairWinget, (level, text) => _sink.Add(level, text), _clock);
        NavItems = new object[] { specification, prep }.Concat(Categories).Concat(new object[] { Apps, Install, History }).ToList();
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

    /// <summary>Strona „Aplikacje” – między kategoriami a „Instalacją”.</summary>
    public AppsViewModel Apps { get; }

    /// <summary>Strona „Instalacja” – tuż pod „Aplikacjami” (spec 6), przed „Historią zmian”.</summary>
    public InstallViewModel Install { get; }

    /// <summary>Strona „Historia zmian” – ostatnia pozycja górnej listy menu.</summary>
    public HistoryViewModel History { get; }

    /// <summary>
    /// Menu w lewej kolumnie: Specyfikacja, Przygotowanie, kategorie w kolejności <see cref="GuiSession.Categories"/>, Aplikacje,
    /// Instalacja, na końcu Historia zmian.
    /// </summary>
    public IReadOnlyList<object> NavItems { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Pozycje przypięte na dole menu (za linią): tylko „Ustawienia”.</summary>
    public IReadOnlyList<object> PinnedNavItems { get; }

    /// <summary>
    /// Strona z lewej kolumny (start = Specyfikacja). Null (np. chwilowo z ListBoxa) jest ignorowany.
    /// Wybór pozycji w menu czyści wyszukiwanie [decyzja projektowa]. Wejście na „Historię zmian” zaczyna od filtra „Wszystkie wpisy”
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
            OnPropertyChanged(nameof(IsCategorySelected));
            OnPropertyChanged(nameof(IsSpecificationSelected));
            OnPropertyChanged(nameof(IsPrepSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsHistorySelected));
            OnPropertyChanged(nameof(IsAppsSelected));
            OnPropertyChanged(nameof(IsInstallSelected));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(DetailItem));
            OnHiddenChanged();

            // Historia, Aplikacje, Instalacja i Przygotowanie wczytują się przy każdym wejściu na stronę (RefreshAsync nigdy nie rzuca;
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
            else if (value is InstallViewModel install)
            {
                // Każde wejście na stronę = „Wszystkie” (D1) i świeży odczyt.
                _ = install.EnterAsync();
            }
        }

        SearchText = string.Empty;

        // Pierwsze wejście na kategorię w tym uruchomieniu zaznacza jej sugestie; po wyczyszczeniu frazy, żeby guard IsSearching nie blokował wejścia z wyszukiwania.
        ApplySuggestionsForCurrentPage();
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
                RefreshSearch();

                // Fraza wyczyszczona na kategorii z odłożonymi sugestiami (odświeżenie skończyło się w trakcie wyszukiwania).
                ApplySuggestionsForCurrentPage();
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
                foreach (var category in Categories)
                {
                    category.ApplyFilter(value);
                }

                RefreshSearch();
            }
        }
    }

    public IReadOnlyList<RiskFilterOption> RiskFilterOptions { get; }

    /// <summary>Wyszukiwanie trwa, gdy zapytanie po normalizacji nie jest puste (same spacje = brak wyszukiwania).</summary>
    public bool IsSearching => TextFolding.Fold(_searchText).Length > 0;

    /// <summary>Wyniki wyszukiwania pogrupowane nagłówkami kategorii, z filtrem zapytania i ryzyka.</summary>
    public ICollectionView SearchResultsView => _searchView;

    /// <summary>Pozycja panelu szczegółów: bieżący wynik wyszukiwania albo pozycja kategorii; null = panel zwinięty.</summary>
    public ItemViewModel? DetailItem => IsSearching ? _searchView.CurrentItem as ItemViewModel : SelectedCategory?.SelectedItem;

    /// <summary>
    /// Rozwinięte „Szczegóły techniczne” w panelu szczegółów (spec opisów 2026-10-26): domyślnie zwinięte, wybór innej pozycji
    /// ich nie zwija – stan trwa do zamknięcia programu, nie jest zapisywany w ustawieniach.
    /// </summary>
    public bool TechnicalDetailsExpanded
    {
        get => _technicalDetailsExpanded;
        set => SetProperty(ref _technicalDetailsExpanded, value);
    }

    public string PageTitle => IsSearching ? GuiText.SearchTitle : SelectedCategory?.Name ?? string.Empty;

    /// <summary>Pod tytułem: przy wyszukiwaniu liczba wyników (w trakcie operacji jej postęp), inaczej <see cref="StatusText"/>.</summary>
    public string PageSubtitle
    {
        get
        {
            if (!IsSearching || (IsBusy && _operationText is not null))
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

    /// <summary>Linia w miejscu pustej listy wyników; pusty tekst, gdy nie dotyczy (kategoria albo są wyniki).</summary>
    public string EmptyText => IsSearching && _searchView.IsEmpty ? GuiText.EmptySearchText(_searchText, HiddenCount) : string.Empty;

    public IRelayCommand ShowAllCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    private int HiddenCount
    {
        get
        {
            if (IsSearching)
            {
                return ItemFilter.CountHidden(
                    _allItems.Select(item => new KeyValuePair<TweakDefinition, DetectionState?>(item.Definition, item.KnownState)),
                    _searchText,
                    _riskFilter);
            }

            return SelectedCategory?.HiddenCount ?? 0;
        }
    }

    private int ShownDespiteFilterCount
    {
        get
        {
            if (IsSearching)
            {
                return ItemFilter.CountShownDespiteFilter(
                    _allItems.Select(item => new KeyValuePair<TweakDefinition, DetectionState?>(item.Definition, item.KnownState)),
                    _searchText,
                    _riskFilter);
            }

            return SelectedCategory?.ShownDespiteFilterCount ?? 0;
        }
    }

    /// <summary>Zaznaczona kategoria; filtruje środek. Null, gdy otwarta jest Specyfikacja.</summary>
    public CategoryViewModel? SelectedCategory => SelectedPage as CategoryViewModel;

    public bool IsCategorySelected => SelectedPage is CategoryViewModel;

    public bool IsSpecificationSelected => SelectedPage is SpecificationViewModel;

    public bool IsPrepSelected => SelectedPage is PrepViewModel;

    public bool IsSettingsSelected => SelectedPage is SettingsViewModel;

    public bool IsHistorySelected => SelectedPage is HistoryViewModel;

    public bool IsAppsSelected => SelectedPage is AppsViewModel;

    public bool IsInstallSelected => SelectedPage is InstallViewModel;

    public ObservableCollection<GuiLogLine> LogLines => _sink.Lines;

    /// <summary>„N błędów, M ostrzeżeń” nad dziennikiem.</summary>
    public string LogSummary
    {
        get => _logSummary;
        private set => SetProperty(ref _logSummary, value);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// F5: odświeżenie otwartej strony – kategoria = <see cref="RefreshCommand"/>, Specyfikacja i Przygotowanie = ich ponowny odczyt,
    /// Historia = <see cref="HistoryViewModel.RefreshAsync"/>, Aplikacje = <see cref="AppsViewModel.RefreshAsync"/>,
    /// Instalacja = <see cref="InstallViewModel.RefreshAsync"/>, Ustawienia = nic.
    /// W trakcie operacji nieaktywne.
    /// </summary>
    public IAsyncRelayCommand RefreshCurrentPageCommand { get; }

    public IAsyncRelayCommand ApplySelectedCommand { get; }

    public IAsyncRelayCommand UndoSelectedCommand { get; }

    public IRelayCommand ClearSelectionCommand { get; }

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
                Settings.IsEditable = !value;
                History.IsBusy = value;
                Apps.IsBusy = value;
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

            return _lastCheck is { } at
                ? "Ostatnie sprawdzenie: " + at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
                : NotCheckedText;
        }
    }

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
    public string SelectionBarTitle => GuiText.SelectionBarTitle(SelectedCount, ApplicableCount, UndoableCount);

    /// <summary>Druga linia paska: restarty i punkt przywracania; pusta przy 0 zaznaczonych.</summary>
    public string SelectionBarDetails => _selectionBarDetails;

    public string ApplySelectedText => $"Zastosuj ({ApplicableCount})";

    public string UndoSelectedText => $"Cofnij ({UndoableCount})";

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
            _operationText = null;
            IsBusy = false;
        }

        // Licznik „Historia zmian” w menu po detekcji – inaczej niż baner już po zwolnieniu IsBusy (RefreshHistory): historia
        // trzyma blokadę sesji tylko na Journal.Load (milisekundy), a kolizja z klikiem to nieszkodliwe „Trwa inna operacja –
        // nic nie zmieniono”; tłumaczenie SID-ów poza blokadą może trwać sekundy i nie może wydłużać IsBusy.
        RefreshHistory();
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
            _ => $"Anulowano: {GuiText.OperationName(operation)} – {item.Title}.").ConfigureAwait(true);
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
                _sink.Add(GuiLogLevel.Info, $"Anulowano: {GuiText.OperationName(GuiOperation.Execute)} – {item.Title}.");
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
    /// Linie raportu do dziennika, nowe stany pozycji, czas sprawdzenia, potem baner restartu z journala i odświeżenie historii
    /// (licznik w menu, stan wpisów; bez czekania – <see cref="RefreshHistory"/>).
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

        _sink.Add(GuiLogLevel.Warning, "Trwa operacja – zamknij okno po jej zakończeniu.");
        return false;
    }

    /// <summary>Przelicza liczniki paska zaznaczenia (wołane przez pozycję przy zmianie zaznaczenia albo stanu).</summary>
    internal void RefreshSelection()
    {
        var selected = _items.Values.Where(item => item.IsSelected).ToList();
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

        await RunOperationAsync($"{GuiText.OperationName(operation)}: zaznaczone ({ids.Count})…", async () =>
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

            var operationText = $"{GuiText.OperationName(operation)}: zaznaczone ({ids.Count})…";
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
            var operationText = $"{GuiText.OperationName(GuiOperation.RemoveApps)}: zaznaczone aplikacje ({rows.Count})…";
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
        RefreshHistory();
        _ = Apps.RefreshAsync();
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
            var operationText = $"{GuiText.OperationName(GuiOperation.InstallApps)}: zaznaczone aplikacje ({targets.Count})…";
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
        RefreshHistory();
        _ = Install.RefreshAsync();
    }

    /// <summary>
    /// „Przejdź do: Napraw winget” ze strony „Instalacja”: kategoria „Naprawy” z pozycją <c>repair.winget</c> w panelu szczegółów
    /// (filtr ryzyka wraca do „Wszystkie”, gdy ją chowa). Nic nie uruchamia – naprawę startuje użytkownik.
    /// </summary>
    private void ShowRepairWinget()
    {
        var category = Categories.FirstOrDefault(candidate => string.Equals(candidate.Id, RepairCategoryId, StringComparison.Ordinal));
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
    /// tu wyniki wyszukiwania i „Ukryte przez filtr” – stan zmienia widoczność tylko przy filtrze innym niż „Wszystkie”.
    /// </summary>
    internal void OnItemStateChanged()
    {
        if (_riskFilter != RiskFilter.All)
        {
            RefreshSearch();
        }
    }

    private bool IsVisible(ItemViewModel item) => ItemFilter.IsVisible(item.Definition, item.KnownState, _searchText, _riskFilter);

    /// <summary>Nowy filtr wyników; bieżący wynik, który wypadł z widoku, przechodzi na pierwszy widoczny.</summary>
    private void RefreshSearch()
    {
        _searchView.Refresh();
        if (_searchView.CurrentItem is not ItemViewModel current || !IsVisible(current))
        {
            _searchView.MoveCurrentToFirst();
        }

        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(DetailItem));
        OnHiddenChanged();
    }

    private void OnHiddenChanged()
    {
        OnPropertyChanged(nameof(FilterNoteText));
        OnPropertyChanged(nameof(HasFilterNote));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void OnCategoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender != SelectedCategory || IsSearching)
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
    }

    private void ApplyStates(IReadOnlyDictionary<string, DetectionState> states)
    {
        foreach (var pair in states)
        {
            ApplyState(pair.Key, pair.Value);
        }

        // Odświeżenie stanów skończone: kategoria otwarta w trakcie detekcji dostaje sugestie dopiero teraz.
        ApplySuggestionsForCurrentPage();
    }

    /// <summary>
    /// Zaznacza do serii sugerowane, niezastosowane pozycje bieżącej kategorii - raz na kategorię i uruchomienie
    /// (<see cref="SuggestedSelection"/>); czeka, aż wszystkie sugerowane pozycje mają znany stan. Poza kategorią nic nie robi.
    /// </summary>
    private void ApplySuggestionsForCurrentPage()
    {
        // Wyniki wyszukiwania nie wyzwalają sugestii; kategoria pod spodem dostanie je po wyczyszczeniu frazy.
        if (_selectedPage is not CategoryViewModel category || IsSearching)
        {
            return;
        }

        var candidates = category.Items
            .Where(item => item.IsTweak)
            .Select(item => new SuggestionCandidate(
                item.Id,
                // Sugestia ukryta przez filtr ryzyka nie jest zaznaczana (zaznaczenie niewidoczne dla użytkownika); kategoria i tak liczy się jako obsłużona.
                item.IsSuggested && ItemFilter.PassesRisk(item.Definition, item.KnownState, _riskFilter),
                item.KnownState))
            .ToList();
        foreach (var id in _suggestedSelection.TakeFor(category.Id, candidates))
        {
            _items[id].IsSelected = true;
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
        => _sink.Add(GuiLogLevel.Error, $"Nieoczekiwany błąd: {ex.GetType().Name}: {ex.Message}");

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
