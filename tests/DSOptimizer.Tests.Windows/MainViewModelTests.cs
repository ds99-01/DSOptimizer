using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
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
    public void NavItems_AutostartBetweenAppsAndInstall()
    {
        RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = Create(temp);

            var apps = vm.NavItems.ToList().IndexOf(vm.Apps);
            Assert.Same(vm.Autostart, vm.NavItems[apps + 1]);
            Assert.Same(vm.Install, vm.NavItems[apps + 2]);
            Assert.Same(vm.History, vm.NavItems[vm.NavItems.Count - 1]);
            Assert.False(vm.IsAutostartSelected);
        });
    }

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
    internal static MainViewModel Create(TempDirectory temp, FakeAutostartReader? autostartReader = null, ISeriesDialogs? seriesDialogs = null)
    {
        var composition = AppComposition.Create(temp.AppRoot, null);
        var services = composition.Services;
        var log = composition.Log;
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        var session = new GuiSession(new LoadedCatalog(catalog.Definitions, "sha256:test"), services);
        var sink = new UiLogSink(Dispatcher.CurrentDispatcher, services.Clock);
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
}
