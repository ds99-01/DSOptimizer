using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App;

public partial class MainWindow
{
    /// <summary>Wysokość paska marki = obszar podpisu okna (przeciąganie, dwuklik, przyciąganie do krawędzi).</summary>
    private const double BrandBarHeight = 44;

    private static readonly DependencyPropertyKey IsNavCompactPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsNavCompact), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    /// <summary>Menu zwinięte do ikon (<see cref="GuiLayout.IsNavCompact"/>); szablony pozycji menu chowają nazwę i licznik.</summary>
    public static readonly DependencyProperty IsNavCompactProperty = IsNavCompactPropertyKey.DependencyProperty;

    private readonly MainViewModel _viewModel;
    private readonly ILog _log;
    private readonly ThemeController _theme;
    private readonly LogView _logView;
    private readonly ListFocusKeeper _itemFocus;
    private string? _detailItemId;

    /// <summary>Wiersz, na którym wciśnięto lewy przycisk; reakcja (dwuklik, ▶) następuje tylko, gdy puszczono go na tym samym wierszu.</summary>
    private ListBoxItem? _pressedRow;
    private int _pressedClickCount;

    /// <summary>Wiersz i cel pierwszego kliku bieżącej serii (clickCount 1): drugi klik w ten sam wiersz po ▶ nie uruchamia akcji ponownie.</summary>
    private ListBoxItem? _firstClickRow;
    private RowClickTarget? _firstClickTarget;
    private const string RunIconName = "RunIcon";

    /// <summary>
    /// <paramref name="shell"/> pokazuje plik z linku w dzienniku (poza wątkiem UI); ścieżki w <paramref name="protectedRoot"/>
    /// (katalog programu, <c>%ProgramData%\DSOptimizer</c>) dziennik tylko kopiuje, nie otwiera. <paramref name="theme"/> po
    /// <c>Loaded</c> zaczyna śledzić motyw Windows (gdy ustawienie to „Systemowy”).
    /// </summary>
    public MainWindow(MainViewModel viewModel, ILog log, ShellActions shell, ThemeController theme, string protectedRoot)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        DataContext = viewModel;
        InitializeComponent();
        Title = $"{ProductInfo.Name} {ProductInfo.Version}";
        _logView = new LogView(LogBox, viewModel.LogLines, shell, protectedRoot, log);
        // Fokus klawiatury zostaje na wierszu po Refresh widoku (detekcja, „Odśwież stan” przy filtrze albo wyszukiwaniu)
        // i wraca na wiersz, gdy „Uruchom” robi się nieaktywne w trakcie operacji.
        _itemFocus = new ListFocusKeeper(ItemList, item => item is ItemViewModel row ? row.Id : null);
        _itemFocus.ReturnFocusFrom(RunButton);
        SizeChanged += OnWindowSizeChanged;
        StateChanged += OnWindowStateChanged;
        Loaded += OnLoaded;
    }

    public bool IsNavCompact
    {
        get => (bool)GetValue(IsNavCompactProperty);
        private set => SetValue(IsNavCompactPropertyKey, value);
    }

    /// <summary>
    /// FluentWindow ustawia WindowChrome w OnSourceInitialized (CaptionHeight = 0, bo zwykle podpis robi ui:TitleBar).
    /// Własny pasek marki staje się obszarem podpisu; jego przyciski mają WindowChrome.IsHitTestVisibleInChrome.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyInitialBounds();
        ApplyInitialLogHeight();
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is null)
        {
            return;
        }

        if (chrome.IsFrozen)
        {
            chrome = (WindowChrome)chrome.Clone();
        }

        chrome.CaptionHeight = BrandBarHeight;
        WindowChrome.SetWindowChrome(this, chrome);
        UpdateCaptionHeight();
    }

    /// <summary>
    /// Po zmianie stanu okna przelicza obszar podpisu. Układ po maksymalizacji ustala się dopiero po zdarzeniu,
    /// więc pomiar idzie z priorytetem Loaded, gdy układ jest już policzony.
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateCaptionHeight();
        Dispatcher.BeginInvoke(new Action(UpdateCaptionHeight), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Obszar podpisu (<c>CaptionHeight</c>) leży przy górnej krawędzi okna. Zmaksymalizowane okno wystaje poza ekran (niewidoczna ramka),
    /// a treść jest o tę ramkę przesunięta w dół, więc przy stałej 44 dolna część paska marki nie przeciągałaby okna.
    /// Margines mierzony w czasie działania: górny róg <see cref="BrandBar"/> na ekranie minus górna krawędź okna
    /// (<c>GetWindowRect</c>), z pikseli urządzenia na DIP. W stanie normalnym margines = 0.
    /// WindowChrome liczy obszar podpisu od dolnej krawędzi <c>ResizeBorderThickness.Top</c>, nie od górnej krawędzi okna,
    /// więc tę grubość odejmujemy: bez tego podpis sięgał 4 px pod pasek (VM 26H2: HTCAPTION 40..83 przy pasku 36..79,
    /// a po maksymalizacji −4..47 przy pasku 0..43).
    /// </summary>
    private void UpdateCaptionHeight()
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is null || chrome.IsFrozen)
        {
            return;
        }

        var offset = WindowState == WindowState.Maximized ? MeasureBrandBarTopOffset() : 0;
        var height = Math.Max(0, BrandBarHeight + offset - chrome.ResizeBorderThickness.Top);
        if (Math.Abs(chrome.CaptionHeight - height) > 0.01)
        {
            chrome.CaptionHeight = height;
        }
    }

    /// <summary>Odległość (DIP, nie mniej niż 0) między górną krawędzią okna a górą paska marki; 0, gdy nie da się zmierzyć.</summary>
    private double MeasureBrandBarTopOffset()
    {
        var source = PresentationSource.FromVisual(this);
        var handle = new WindowInteropHelper(this).Handle;
        if (source?.CompositionTarget is null || handle == IntPtr.Zero || !BrandBar.IsLoaded
            || !NativeMethods.GetWindowRect(handle, out var windowRect))
        {
            return 0;
        }

        var barTopPx = BrandBar.PointToScreen(new Point(0, 0)).Y;
        var offsetPx = barTopPx - windowRect.Top;
        var offsetDip = source.CompositionTarget.TransformFromDevice.Transform(new Point(0, offsetPx)).Y;
        return Math.Max(0, offsetDip);
    }

    /// <summary>
    /// Startowy rozmiar i położenie z obszaru roboczego monitora, na którym leży okno (90%, wyśrodkowane, nigdy Maximized).
    /// Gdy odczyt monitora zawiedzie, służy <see cref="SystemParameters.WorkArea"/> (monitor główny), a powód trafia do logu.
    /// </summary>
    private void ApplyInitialBounds()
    {
        WindowState = WindowState.Normal;
        var workArea = TryReadMonitorWorkArea() ?? FallbackWorkArea();
        if (workArea.Width <= 0 || workArea.Height <= 0 || double.IsNaN(workArea.Width) || double.IsNaN(workArea.Height))
        {
            _log.Warn("Gui", "Obszar roboczy ma niepoprawny rozmiar – zostają domyślne wymiary okna.");
            return;
        }

        var bounds = GuiLayout.InitialBounds(workArea);
        MinWidth = Math.Min(GuiLayout.MinWidth, workArea.Width);
        MinHeight = Math.Min(GuiLayout.MinHeight, workArea.Height);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        ApplyNavWidth(bounds.Width);
    }

    /// <summary>Dziennik startuje z 20% wysokości okna (146–240); górna granica = połowa okna. Wysokość nie jest zapamiętywana.</summary>
    private void ApplyInitialLogHeight()
    {
        var height = double.IsNaN(Height) ? ActualHeight : Height;
        if (height > 0)
        {
            LogRow.Height = new GridLength(GuiLayout.InitialLogHeight(height));
            LogRow.MaxHeight = GuiLayout.MaxLogHeight(height);
        }
    }

    /// <summary>
    /// Uchwyt dziennika: górna granica podąża za wysokością okna (np. po maksymalizacji). Menu zwija się do ikon albo
    /// rozwija na żywo wg szerokości okna (bez animacji); kolumny treści przelicza potem <see cref="OnContentSizeChanged"/>.
    /// </summary>
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Height > 0)
        {
            LogRow.MaxHeight = GuiLayout.MaxLogHeight(e.NewSize.Height);
        }

        if (e.WidthChanged)
        {
            ApplyNavWidth(e.NewSize.Width);
        }
    }

    /// <summary>Szerokość kolumny menu i stan zwinięcia z <see cref="GuiLayout.NavWidth"/>.</summary>
    private void ApplyNavWidth(double windowWidth)
    {
        if (windowWidth > 0 && !double.IsNaN(windowWidth))
        {
            NavColumn.Width = new GridLength(GuiLayout.NavWidth(windowWidth));
            IsNavCompact = GuiLayout.IsNavCompact(windowWidth);
        }
    }

    /// <summary>Obszar roboczy monitora okna w DIP; null, gdy P/Invoke albo przeliczenie zawiodło (powód w logu).</summary>
    private LayoutRect? TryReadMonitorWorkArea()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
            var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                _log.Warn("Gui", $"Nie udało się odczytać obszaru roboczego monitora (błąd Win32 {Marshal.GetLastWin32Error()}) – używam obszaru monitora głównego.");
                return null;
            }

            var target = PresentationSource.FromVisual(this)?.CompositionTarget;
            if (target is null)
            {
                _log.Warn("Gui", "Brak źródła prezentacji do przeliczenia pikseli na DIP – używam obszaru monitora głównego.");
                return null;
            }

            var topLeft = target.TransformFromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
            var bottomRight = target.TransformFromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
            return new LayoutRect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.Warn("Gui", $"Odczyt obszaru roboczego monitora nie powiódł się ({ex.GetType().Name}) – używam obszaru monitora głównego.");
            return null;
        }
    }

    private static LayoutRect FallbackWorkArea()
    {
        var area = SystemParameters.WorkArea;
        return new LayoutRect(area.Left, area.Top, area.Width, area.Height);
    }

    /// <summary>Kolumna szczegółów: 1/3 szerokości treści w granicach z <see cref="GuiLayout"/>.</summary>
    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && !double.IsNaN(e.NewSize.Width))
        {
            DetailColumn.Width = new GridLength(GuiLayout.DetailColumnWidth(e.NewSize.Width));
        }
    }

    // InitializeAsync i LoadUserNameAsync łapią własne wyjątki, loader specyfikacji nie rzuca, a ThemeController.Attach łapie i loguje błąd śledzenia motywu, więc async void nie wycieka.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Po Loaded: Watch na załadowanym oknie nie woła ApplySystemTheme (to włączyłoby Mica na MainWindow).
        _theme.Attach(this);

        // Odczyt specyfikacji równolegle z detekcją; LoadAsync tworzy Progress na wątku UI i nigdy nie rzuca.
        var specification = _viewModel.Specification.LoadAsync();
        var userName = _viewModel.LoadUserNameAsync();
        await _viewModel.InitializeAsync();
        await userName;
        await specification;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.CanClose())
        {
            e.Cancel = true;
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    /// <summary>
    /// Ctrl+F: fokus i zaznaczenie tekstu pola wyszukiwania. Pole jest tylko na stronie kategorii, więc ze Specyfikacji
    /// i z Ustawień najpierw otwiera ostatnio otwartą kategorię (na starcie pierwszą) [decyzja projektowa].
    /// </summary>
    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        // Strona „Aplikacje” ma własną wyszukiwarkę (nazwa i wydawca); globalne szukanie kategorii jej nie obejmuje.
        if (_viewModel.IsAppsSelected)
        {
            AppsPage.FocusSearch();
            return;
        }

        // Strona „Instalacja”: pole filtra listy (Enter albo „Szukaj też w winget” = także winget).
        if (_viewModel.IsInstallSelected)
        {
            InstallPage.FocusSearch();
            return;
        }

        if (!_viewModel.IsCategorySelected && _viewModel.LastCategory is { } category)
        {
            _viewModel.SelectedPage = category;
            UpdateLayout();
        }

        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>
    /// Klik w pozycję menu czyści wyszukiwanie także wtedy, gdy to już zaznaczona kategoria (zmiana zaznaczenia
    /// czyści je w <see cref="MainViewModel.SelectedPage"/>, ale ponowny klik w tę samą pozycję jej nie zmienia).
    /// </summary>
    private void OnNavClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(NavList, source) is ListBoxItem)
        {
            _viewModel.ClearSearchCommand.Execute(null);
        }
    }

    /// <summary>
    /// Pojedynczy klik tylko pokazuje pozycję w panelu (robi to <c>IsSynchronizedWithCurrentItem</c> przy zaznaczeniu wiersza).
    /// Dwuklik wiersza przełącza serię (tweak) albo uruchamia (akcja/panel), klik w ▶ uruchamia akcję – <see cref="GuiRules.RowClick"/>.
    /// Pole wyboru przełącza się samo. Reakcja przy puszczeniu przycisku na tym samym wierszu, w którym go wciśnięto.
    /// </summary>
    private void OnItemListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedRow = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(ItemList, source) as ListBoxItem
            : null;
        _pressedClickCount = e.ClickCount;
        if (e.ClickCount == 1)
        {
            _firstClickRow = null;
            _firstClickTarget = null;
        }
    }

    private void OnItemListMouseUp(object sender, MouseButtonEventArgs e)
    {
        var pressed = _pressedRow;
        _pressedRow = null;
        if (pressed is null
            || e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(ItemList, source) is not ListBoxItem container
            || !ReferenceEquals(container, pressed)
            || container.DataContext is not ItemViewModel item)
        {
            return;
        }

        var target = ClassifyClickTarget(source, container);
        if (_pressedClickCount == 1)
        {
            _firstClickRow = container;
            _firstClickTarget = target;
        }

        var firstTarget = ReferenceEquals(_firstClickRow, container) ? _firstClickTarget : null;
        switch (GuiRules.RowClick(item.IsTweak, target, _pressedClickCount, firstTarget))
        {
            case RowClickAction.ToggleSeries:
                item.IsSelected = !item.IsSelected;
                break;
            case RowClickAction.Run:
                // Ta sama ścieżka co przycisk „Uruchom”: ExecuteCommand (potwierdzenie i IsBusy są w RunAsync/CanExecute).
                if (item.ExecuteCommand.CanExecute(null))
                {
                    item.ExecuteCommand.Execute(null);
                }

                break;
        }
    }

    /// <summary>
    /// Na wierszu (fokus na samym wierszu, nie na polu wyboru): Spacja przełącza zaznaczenie tweaka do serii,
    /// Enter pokazuje wiersz w panelu szczegółów i przenosi fokus na „Uruchom”, gdy jest widoczne i aktywne; inaczej nic nie robi
    /// (treść panelu jest osiągalna Tabem). Autopowtórzenie Entera nie wciska „Uruchom” – <see cref="OnDetailPanelPreviewKeyDown"/>.
    /// </summary>
    private void OnItemListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is not ListBoxItem { DataContext: ItemViewModel item })
        {
            return;
        }

        if (e.Key == Key.Space && item.IsTweak)
        {
            item.IsSelected = !item.IsSelected;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ItemList.SelectedItem = item;
            DetailPanel.UpdateLayout();
            if (RunButton.IsVisible && RunButton.IsEnabled)
            {
                e.Handled = RunButton.Focus();
            }
        }
    }

    /// <summary>
    /// Przytrzymany Enter: pierwszy KeyDown na wierszu przenosi fokus na „Uruchom”, a autopowtórzenia (<see cref="KeyEventArgs.IsRepeat"/>)
    /// trafiałyby już w przycisk (ButtonBase klika przy KeyDown Entera) – pozycja bezpieczna uruchomiłaby się bez okna potwierdzenia.
    /// Powtórzenia Entera w panelu są więc pochłaniane; świadomy Enter na przycisku (bez powtórzenia) działa jak dotąd.
    /// </summary>
    private void OnDetailPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.IsRepeat)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Zmiana pozycji w panelu (wg <see cref="ItemViewModel.Id"/>) przewija treść na górę. Odświeżenie tej samej pozycji (F5, zmiana
    /// stanu) nie rusza przewinięcia; chwilowy brak pozycji (null) też nie – po powrocie do tej samej zostaje jak było.
    /// </summary>
    private void OnDetailPanelDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not ItemViewModel item || item.Id == _detailItemId)
        {
            return;
        }

        _detailItemId = item.Id;
        DetailScroll.ScrollToTop();
    }

    /// <summary>Margines (DIP) dookoła elementu przewijanego do widoku w panelu: pierścień fokusu <c>BrandFocusVisual</c> wystaje 3 px poza element.</summary>
    private const double DetailBringIntoViewMargin = 4;

    /// <summary>
    /// Fokus klawiatury przewija panel tylko do granic elementu, więc krawędź panelu ucinała pierścień fokusu (3 px na zewnątrz).
    /// Żądanie bez prostokąta (fokus, <see cref="FrameworkElement.BringIntoView()"/>) od elementu w treści panelu zamieniamy na żądanie
    /// z prostokątem powiększonym o <see cref="DetailBringIntoViewMargin"/>; żądanie z prostokątem przechodzi bez zmian (bez pętli).
    /// Przystanki Tab bez zmian.
    /// </summary>
    private void OnDetailContentRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (!e.TargetRect.IsEmpty || ReferenceEquals(e.TargetObject, sender) || e.TargetObject is not FrameworkElement target
            || target.ActualWidth <= 0 || target.ActualHeight <= 0)
        {
            return;
        }

        e.Handled = true;
        target.BringIntoView(new Rect(-DetailBringIntoViewMargin, -DetailBringIntoViewMargin,
            target.ActualWidth + (2 * DetailBringIntoViewMargin), target.ActualHeight + (2 * DetailBringIntoViewMargin)));
    }

    /// <summary>
    /// Rozwinięcie „Szczegółów technicznych” (klik, Enter, Spacja) przy dolnej krawędzi panelu zmieniało tylko strzałkę, a treść zostawała
    /// pod krawędzią. Po ułożeniu treści panel przewija się minimalnie tak, żeby widać było nagłówek z treścią; gdy całość się nie mieści,
    /// nagłówek staje u góry panelu (reguła minimalnego przewinięcia ScrollViewera). Zmiana pozycji nadal przewija panel na górę.
    /// </summary>
    private void OnTechnicalDetailsExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander || !ReferenceEquals(e.OriginalSource, expander))
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (expander.IsExpanded && expander.IsVisible && expander.ActualHeight > 0)
            {
                expander.BringIntoView(new Rect(0, -DetailBringIntoViewMargin, expander.ActualWidth, expander.ActualHeight + (2 * DetailBringIntoViewMargin)));
            }
        }));
    }

    /// <summary>Cel kliknięcia z drzewa wizualnego, od klikniętego elementu w górę do wiersza: przodek CheckBox, znacznik ▶ (element szablonu o nazwie <c>RunIcon</c>) albo reszta wiersza.</summary>
    private static RowClickTarget ClassifyClickTarget(DependencyObject source, ListBoxItem container)
    {
        DependencyObject? current = source;
        while (current is not null && !ReferenceEquals(current, container))
        {
            if (current is CheckBox)
            {
                return RowClickTarget.CheckBox;
            }

            if (current is FrameworkElement { Name: RunIconName })
            {
                return RowClickTarget.RunIcon;
            }

            current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return RowClickTarget.Row;
    }

    /// <summary>„Kopiuj wszystko” w nagłówku dziennika: wszystkie linie (godzina, poziom, tekst) do schowka.</summary>
    private void OnCopyLogClick(object sender, RoutedEventArgs e) => _logView.CopyAll();
}
