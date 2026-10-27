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
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Prep;
using DSOptimizer.Core.Settings;
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

    private static MainViewModel Create(TempDirectory temp)
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
        var settings = new SettingsViewModel(new SettingsLoadResult(AppSettings.Default, null), settingsStore, session, new ThemeController(log), shell, log);
        return new MainViewModel(
            session,
            sink,
            new SeriesDialogs(log, sink.Add, services.Clock),
            services.Clock,
            new NoLastBoot(),
            new NoRestart(),
            "TEST",
            null,
            accountNames,
            apps,
            install,
            specification,
            prep,
            settings);
    }

    private static void RunOnSta(Action body)
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
