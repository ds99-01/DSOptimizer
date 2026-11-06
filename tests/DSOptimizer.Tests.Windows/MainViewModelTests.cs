using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.App.Views;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Prep;
using DSOptimizer.Core.Settings;
using DSOptimizer.Core.Updates;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using DSOptimizer.Windows.Apps;
using DSOptimizer.Windows.Hardware;
using DSOptimizer.Windows.Prep;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Model głównego okna składany jak w <c>Program</c>, ale w katalogu tymczasowym i bez okna WPF. Nic nie jest uruchamiane:
/// konstruktory usług niczego nie czytają ani nie zmieniają (odczyt dopiero w <c>InitializeAsync</c>, którego test nie woła).
/// Wątek STA z własnym dispatcherem – widoki kolekcji i dziennik panelu należą do wątku, który je utworzył.
/// </summary>
[Trait("Category", "Windows")]
public class MainViewModelTests
{
    // Restart i czas startu systemu zawsze fałszywe: test nigdy nie tworzy prawdziwego ShutdownRestarter (zasada repo).
    private sealed class NoRestart : IRestarter
    {
        public void Restart() => throw new InvalidOperationException("Test nie restartuje komputera.");
    }

    private sealed class NoRelaunch : IProgramRelauncher
    {
        public void Relaunch() => throw new InvalidOperationException("Test nie uruchamia programu ponownie.");
    }

    private sealed class NoLastBoot : ILastBootTime
    {
        public DateTimeOffset? Read() => null;
    }

    [Fact]
    public void TechnicalDetailsExpanded_DefaultsToCollapsed_AndSurvivesSelectionChange()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);
            Assert.False(vm.TechnicalDetailsExpanded);

            var category = vm.Categories.First(c => c.Items.Count >= 2);
            vm.SelectedPage = category;
            category.ItemsView.MoveCurrentTo(category.Items[0]);
            Assert.Same(category.Items[0], vm.DetailItem);

            vm.TechnicalDetailsExpanded = true;
            category.ItemsView.MoveCurrentTo(category.Items[1]);

            Assert.Same(category.Items[1], vm.DetailItem);
            Assert.True(vm.TechnicalDetailsExpanded);
        });
    }

    [Fact]
    public void NavItems_FiveGroupsSeparatedByLines()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);

            Assert.Equal(
                "Specification,Prep,|,privacy,perf,disk,net,sec,ux,|,Apps,Autostart,Install,|,service,|,History",
                string.Join(",", vm.NavItems.Select(Kind)));
            Assert.False(vm.IsAutostartSelected);
        });
    }

    [Fact]
    public void ServicePage_SectionsRepairToolsPanels_NoCounter()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);

            var service = vm.Categories.Single(c => c.IsService);
            Assert.Equal("Naprawy i narzędzia", service.Name);
            Assert.Equal(
                new[] { "Naprawy", "Narzędzia", "Panele systemowe" },
                service.ItemsView.Groups.Cast<CollectionViewGroup>().Select(g => (string)g.Name));
            Assert.Equal("repair.system-protection", service.Items[0].Id);
            Assert.False(service.HasCounter);
            Assert.Equal(GuiIcons.Repair, service.IconKind);
        });
    }

    [Fact]
    public void ServicePage_ZeroSelected_ShowsRunHint()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);

            vm.SelectedPage = vm.Categories.Single(c => c.IsService);

            Assert.Equal(GuiText.ServiceSelectionHint, vm.SelectionBarTitle);
            Assert.Equal(GuiText.ServiceSelectionHintDetails, vm.SelectionBarDetails);
        });
    }

    [Fact]
    public void ServicePage_WithSelection_ShowsCount()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);
            vm.SelectedPage = vm.Categories.Single(c => c.IsService);

            vm.Categories.SelectMany(c => c.Items).Single(i => i.Id == "privacy.widgets").IsSelected = true;

            Assert.Equal("Zaznaczono 1 pozycję", vm.SelectionBarTitle.Split(':')[0]);
            Assert.NotEqual(GuiText.ServiceSelectionHintDetails, vm.SelectionBarDetails);
        });
    }

    /// <summary>Wyniki wyszukiwania i widok „Zaznaczone” na stronie „Naprawy i narzędzia”: dwuklik dodaje tam do zmiany, więc pasek ma zwykłą podpowiedź.</summary>
    [Fact]
    public void ServicePage_SearchAndSelectedView_NoServiceHint()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);
            vm.SelectedPage = vm.Categories.Single(c => c.IsService);
            Assert.Equal(GuiText.ServiceSelectionHint, vm.SelectionBarTitle);

            vm.SearchText = "dns";
            Assert.Equal("Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do zmiany.", vm.SelectionBarTitle);
            Assert.Equal(string.Empty, vm.SelectionBarDetails);

            vm.SearchText = string.Empty;
            Assert.Equal(GuiText.ServiceSelectionHint, vm.SelectionBarTitle);

            vm.ShowFilter = ShowFilter.Selected;
            Assert.True(vm.IsSelectedView);
            Assert.Equal("Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do zmiany.", vm.SelectionBarTitle);
            Assert.Equal(string.Empty, vm.SelectionBarDetails);

            vm.EscapeCommand.Execute(null);
            Assert.Equal(GuiText.ServiceSelectionHint, vm.SelectionBarTitle);
            Assert.Equal(GuiText.ServiceSelectionHintDetails, vm.SelectionBarDetails);
        });
    }

    [Fact]
    public void OtherPage_ZeroSelected_KeepsDoubleClickHint()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);

            vm.SelectedPage = vm.Categories.Single(c => c.Id == "ux");

            Assert.Equal("Kliknij dwukrotnie wiersz albo pole wyboru, żeby dodać do zmiany.", vm.SelectionBarTitle);
            Assert.Equal(string.Empty, vm.SelectionBarDetails);
        });
    }

    [Fact]
    public void SelectingSeparator_KeepsCurrentPage()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);
            var privacy = vm.Categories.First(c => c.Id == "privacy");
            vm.SelectedPage = privacy;
            vm.SearchText = "diag";
            var notified = 0;
            vm.PropertyChanged += (_, e) => notified += e.PropertyName == nameof(MainViewModel.SelectedNavItem) ? 1 : 0;

            vm.SelectedNavItem = vm.NavItems.OfType<NavSeparatorViewModel>().First();

            Assert.Same(privacy, vm.SelectedPage);
            Assert.Same(privacy, vm.SelectedNavItem);
            Assert.Equal("diag", vm.SearchText);
            Assert.True(notified > 0, "Odrzucenie linii ma przywrócić podświetlenie bieżącej strony (PropertyChanged SelectedNavItem).");
        });
    }

    /// <summary>
    /// Lista menu ze stylem z Themes/Tokens.xaml: czytnik ekranu widzi 13 stron („pozycja N z 13”), bez linii grup i bez nazw typów CLR.
    /// Zaznaczenie linii przez listę (jak Select z automatyzacji UI) wraca do bieżącej strony.
    /// </summary>
    [Fact]
    public void NavMenu_AutomationPeer_ListsPagesOnly_AndSeparatorSelectionReverts()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);
            var tokens = (ResourceDictionary)Application.LoadComponent(new Uri("/DSOptimizer;component/Themes/Tokens.xaml", UriKind.Relative));
            var list = new NavMenuListBox { Style = (Style)tokens["NavListBox"], DataContext = vm };
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(MainViewModel.NavItems)));
            list.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(MainViewModel.SelectedNavItem)) { Mode = BindingMode.TwoWay });
            list.Measure(new Size(304, 2000));
            list.Arrange(new Rect(0, 0, 304, 2000));

            var children = UIElementAutomationPeer.CreatePeerForElement(list).GetChildren();

            Assert.Equal(13, children.Count);
            Assert.All(children, child =>
            {
                Assert.False(string.IsNullOrEmpty(child.GetName()));
                Assert.DoesNotContain("ViewModel", child.GetName(), StringComparison.Ordinal);
                Assert.Equal(13, child.GetSizeOfSet());
            });
            Assert.Equal(Enumerable.Range(1, 13), children.Select(child => child.GetPositionInSet()));

            list.SelectedItem = vm.NavItems.OfType<NavSeparatorViewModel>().First();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            Assert.Same(vm.Specification, vm.SelectedPage);
            Assert.Same(vm.Specification, list.SelectedItem);
        });
    }

    [Fact]
    public void RepairWingetLink_OpensServicePageOnRepairWinget()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);

            vm.Install.RepairWingetCommand.Execute(null);

            Assert.True(((CategoryViewModel)vm.SelectedPage!).IsService);
            Assert.Equal("repair.winget", vm.DetailItem?.Id);
        });
    }

    /// <summary>Rodzaj pozycji menu: linia grupy = „|”, kategoria = jej id, inna strona = nazwa typu bez „ViewModel”.</summary>
    private static string Kind(object item) => item switch
    {
        NavSeparatorViewModel => "|",
        CategoryViewModel category => category.Id,
        _ => item.GetType().Name.Replace("ViewModel", string.Empty),
    };

    [Fact]
    public void EnteringAutostart_Refreshes()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var reader = new FakeAutostartReader();
            var vm = Create(temp, reader);
            Assert.Empty(reader.Contexts);

            vm.SelectedPage = vm.Autostart;

            Assert.True(vm.IsAutostartSelected);
            Assert.True(SpinWait.SpinUntil(() => reader.Contexts.Count == 1 && !vm.Autostart.IsLoading, TimeSpan.FromSeconds(30)));
        });
    }

    [Fact]
    public void F5_RefreshesAutostart()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var reader = new FakeAutostartReader();
            var vm = Create(temp, reader);
            vm.SelectedPage = vm.Autostart;
            Assert.True(SpinWait.SpinUntil(() => reader.Contexts.Count == 1 && !vm.Autostart.IsLoading, TimeSpan.FromSeconds(30)));

            Assert.True(vm.RefreshCurrentPageCommand.CanExecute(null));
            vm.RefreshCurrentPageCommand.Execute(null);

            Assert.True(SpinWait.SpinUntil(() => reader.Contexts.Count == 2 && !vm.Autostart.IsLoading, TimeSpan.FromSeconds(30)));
        });
    }

    /// <summary>
    /// Autostart na atrapie czytnika (bez wpisów, chyba że test poda własny): prawdziwy autostart hosta nie jest czytany, a rejestr Windows
    /// służy sesji tylko do sprawdzenia gałęzi konta (<see cref="IRegistry.KeyStatus"/>). Bez wierszy silnik nie ma czego zapisać; wiersze
    /// testu mają przełączniki w <see cref="RegistrySandbox"/>, a silnik pisze wyłącznie pod kluczem przełącznika wiersza. Konto autostartu
    /// stałe i pewne (<see cref="AutostartToggleTests.UserSid"/>), żeby seria nie zależała od sesji hosta. <paramref name="seriesDialogs"/> =
    /// atrapa okna serii (domyślnie prawdziwe, którego testy nie otwierają).
    /// </summary>
    internal static MainViewModel Create(TempDirectory temp, FakeAutostartReader? autostartReader = null, ISeriesDialogs? seriesDialogs = null, Action<UiLogSink>? sinkReceiver = null)
    {
        var composition = AppComposition.Create(temp.AppRoot, null);
        var services = composition.Services;
        var log = composition.Log;
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        var session = new GuiSession(new LoadedCatalog(catalog.Definitions, "sha256:test"), services);
        var sink = new UiLogSink(Dispatcher.CurrentDispatcher, services.Clock);
        sinkReceiver?.Invoke(sink);
        var accountNames = new CachedAccountNames(new WindowsAccountNames());
        var sid = services.User.Sid;
        var reader = new InstalledAppsReader(services.Scripts);
        var apps = new AppsSession(
            session,
            reader,
            new AppRemovalEngine(reader, new AppxRemover(services.Scripts), services.Journal, services.RestorePoints, services.User, sid, log, services.Clock),
            AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json),
            new SystemProtectionReader(),
            accountNames,
            services.User,
            sid,
            _ => false,
            Environment.SystemDirectory,
            services.Clock);
        var install = new InstallSession(
            session,
            new AppInstallEngine(composition.Winget, composition.WingetLogs, reader, services.Journal, services.User, sid, accountNames.NameOf, log, services.Clock,
                gitHub: composition.GitHub),
            AppsInstallCatalog.Load(BuiltInCatalog.AppsInstallSource.Json),
            reader,
            composition.Winget,
            services.User,
            sid,
            accountNames,
            services.Clock);
        var autostart = new AutostartSession(
            session,
            autostartReader ?? new FakeAutostartReader(),
            new AutostartEngine(services.Registry, services.Journal, log, services.Clock),
            AutostartKnownCatalog.Load("{ \"entries\": [] }"),
            new AutostartToggleTests.FixedUser(),
            AutostartToggleTests.UserSid,
            services.Registry,
            log,
            services.Clock);
        var shell = new ShellActions(
            new ShellFileRevealer(new WindowsExplorerProcessQuery(), temp.AppRoot.FullPath),
            new ShellUrlOpener(new WindowsExplorerProcessQuery()),
            log);
        var specification = new SpecificationViewModel(
            new SpecificationLoader(new WindowsHardwareReader(), log, SpecificationLoader.DefaultTimeout),
            new ReportFileWriter(temp.AppRoot, null),
            log,
            services.Clock,
            "TEST",
            sink.Add,
            shell);
        var prep = new PrepViewModel(
            new PrepLoader(new WindowsHardwareReader(), new WindowsPrepReader(), log, PrepLoader.DefaultTimeout), shell, "C:", (level, text) => sink.Add(level, text));
        var settingsStore = new JsonFileSettingsStore(temp.AppRoot, services.Clock);
        var settings = new SettingsViewModel(
            new SettingsLoadResult(AppSettings.Default, null), settingsStore, session, new ThemeController(log), shell, log, UiLanguage.Polish, new NoRelaunch(), () => { },
            new UpdatesViewModel(new UpdateChecker(composition.GitHub.Web, "4.0.2", log), shell));
        return new MainViewModel(
            session,
            sink,
            seriesDialogs ?? new SeriesDialogs(log, sink.Add, services.Clock),
            services.Clock,
            new NoLastBoot(),
            new NoRestart(),
            "TEST",
            null,
            accountNames,
            apps,
            autostart,
            install,
            specification,
            prep,
            settings);
    }

    internal static void RunOnSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>Czeka na koniec zadania, wykonując kolejkę dispatchera wątku testu (kontynuacje await na DispatcherSynchronizationContext).</summary>
    internal static void Wait(Task task)
    {
        PumpUntil(() => task.IsCompleted, "koniec operacji");
        task.GetAwaiter().GetResult();
    }

    /// <summary>Kolejka dispatchera wątku testu aż do spełnienia warunku (najwyżej 30 s).</summary>
    internal static void PumpUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Nie doczekano się: " + what);
            }

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }
}
