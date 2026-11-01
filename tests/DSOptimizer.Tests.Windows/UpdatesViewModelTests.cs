using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Settings;
using DSOptimizer.Core.Updates;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Komunikat o nowej wersji i „Sprawdź aktualizacje”: bez sieci (atrapa <see cref="IWebDownloader"/>), bez okna WPF.
/// Zadania asynchroniczne idą przez <c>Task.Run</c>, żeby wątek testu (STA) nie czekał na własny dyspozytor.
/// </summary>
[Trait("Category", "Windows")]
public class UpdatesViewModelTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private sealed class FakeStore : ISettingsStore
    {
        public List<AppSettings> Saved { get; } = new();

        public SettingsLoadResult Load() => new(AppSettings.Default, null);

        public void Save(AppSettings settings) => Saved.Add(settings);
    }

    private sealed class NoRelaunch : IProgramRelauncher
    {
        public void Relaunch() => throw new InvalidOperationException("Test nie uruchamia programu ponownie.");
    }

    [Fact]
    public void CheckAtStart_NewerVersion_ShowsNotice()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = NewUpdates(temp, new StubWebDownloader(StubWebDownloader.Release("v4.0.9")));

            Run(vm.CheckAtStartAsync);

            Assert.True(vm.IsNoticeVisible);
            Assert.Contains("4.0.9", vm.NoticeText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CheckAtStart_OlderOrEqualVersion_StaysSilent()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = NewUpdates(temp, new StubWebDownloader(StubWebDownloader.Release("v4.0.2")));

            Run(vm.CheckAtStartAsync);

            Assert.False(vm.IsNoticeVisible);
            Assert.Null(vm.CheckResultText);
        });
    }

    [Fact]
    public void CheckAtStart_Failure_StaysSilent()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var failed = new WebText(WebFailure.Network, null, null, "brak sieci");
            var vm = NewUpdates(temp, new StubWebDownloader(failed));

            Run(vm.CheckAtStartAsync);

            Assert.False(vm.IsNoticeVisible);
            Assert.Null(vm.CheckResultText);
        });
    }

    [Fact]
    public void CheckAtStart_HangingNetwork_ReturnsImmediately()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using var gate = new ManualResetEventSlim(false);
            var web = new StubWebDownloader(StubWebDownloader.Release("v4.0.9"), gate);
            var vm = NewUpdates(temp, web);

            // Wywołanie wraca od razu (zadanie niezakończone), a sieć pracuje poza wątkiem wywołującego.
            var started = vm.CheckAtStartAsync();
            Assert.True(web.Entered.Wait(Patience));
            Assert.False(started.IsCompleted);
            Assert.False(vm.IsNoticeVisible);

            gate.Set();
            Assert.True(started.Wait(Patience));
            Assert.True(vm.IsNoticeVisible);
        });
    }

    [Fact]
    public void HideNotice_ThenLateResult_DoesNotShowAgain()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using var gate = new ManualResetEventSlim(false);
            var web = new StubWebDownloader(StubWebDownloader.Release("v4.0.9"), gate);
            var vm = NewUpdates(temp, web);

            var started = vm.CheckAtStartAsync();
            Assert.True(web.Entered.Wait(Patience));
            vm.HideNoticeCommand.Execute(null);
            gate.Set();
            Assert.True(started.Wait(Patience));

            Assert.False(vm.IsNoticeVisible);
        });
    }

    [Fact]
    public void CheckNow_ShowsResultInSettings_EvenWhenNoticeHidden()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = NewUpdates(temp, new StubWebDownloader(StubWebDownloader.Release("v4.0.9")));
            vm.HideNoticeCommand.Execute(null);

            Run(() => vm.CheckNowCommand.ExecuteAsync(null));

            Assert.True(vm.HasNewerFromCheck);
            Assert.False(vm.IsNoticeVisible);
            Assert.Equal("Dostępna wersja 4.0.9", vm.CheckResultText);
        });
    }

    [Fact]
    public void CheckNow_Texts()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var cases = new (WebText Response, string Expected, bool Newer)[]
            {
                (StubWebDownloader.Release("v4.0.2"), "Masz najnowszą wersję", false),
                (StubWebDownloader.Release("v4.0.9"), "Dostępna wersja 4.0.9", true),
                (StubWebDownloader.Release("v4.0.10"), "Dostępna wersja 4.0.10", true),
                (new WebText(WebFailure.Network, null, null, "brak sieci"), "Nie udało się sprawdzić", false),
                (new WebText(WebFailure.None, 200, "<html>portal</html>", null), "Nie udało się sprawdzić", false),
            };

            foreach (var (response, expected, newer) in cases)
            {
                var vm = NewUpdates(temp, new StubWebDownloader(response));

                Run(() => vm.CheckNowCommand.ExecuteAsync(null));

                Assert.Equal(expected, vm.CheckResultText);
                Assert.Equal(newer, vm.HasNewerFromCheck);
            }
        });
    }

    [Fact]
    public void CheckNow_Disabled_WhileRunning()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using var gate = new ManualResetEventSlim(false);
            var web = new StubWebDownloader(StubWebDownloader.Release("v4.0.9"), gate);
            var vm = NewUpdates(temp, web);
            Assert.True(vm.CheckNowCommand.CanExecute(null));

            var running = Task.Run(() => vm.CheckNowCommand.ExecuteAsync(null));
            Assert.True(web.Entered.Wait(Patience));

            Assert.False(vm.CheckNowCommand.CanExecute(null));
            Assert.Equal(ItemViewModel.CheckingText, vm.CheckResultText);

            gate.Set();
            Assert.True(running.Wait(Patience));
            Assert.True(vm.CheckNowCommand.CanExecute(null));
        });
    }

    [Fact]
    public void SwitchOff_MakesNoRequest()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var web = new StubWebDownloader(StubWebDownloader.Release("v4.0.9"));
            var settings = NewSettings(temp, web, new FakeStore(), AppSettings.Default with { CheckUpdatesAtStart = false });

            Run(settings.CheckUpdatesAtStartAsync);

            Assert.Equal(0, web.Calls);
            Assert.False(settings.Updates.IsNoticeVisible);

            Run(() => settings.Updates.CheckNowCommand.ExecuteAsync(null));

            Assert.Equal(1, web.Calls);
            Assert.True(settings.Updates.HasNewerFromCheck);
        });
    }

    [Fact]
    public void SwitchOn_ChecksAtStart()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var web = new StubWebDownloader(StubWebDownloader.Release("v4.0.9"));
            var settings = NewSettings(temp, web, new FakeStore(), AppSettings.Default);

            Run(settings.CheckUpdatesAtStartAsync);

            Assert.Equal(1, web.Calls);
            Assert.True(settings.Updates.IsNoticeVisible);
        });
    }

    [Fact]
    public void RealDownloaderUnderGuard_IsFailed_NotThrown()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            Assert.Equal("1", Environment.GetEnvironmentVariable(WebDownloader.BlockRealNetworkVariable));
            var vm = NewUpdates(temp, new WebDownloader(RustDeskSource.UserAgent));

            Run(vm.CheckAtStartAsync);
            Run(() => vm.CheckNowCommand.ExecuteAsync(null));

            Assert.False(vm.IsNoticeVisible);
            Assert.Equal("Nie udało się sprawdzić", vm.CheckResultText);
        });
    }

    [Fact]
    public void SwitchChange_IsSaved()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var store = new FakeStore();
            var settings = NewSettings(temp, new StubWebDownloader(StubWebDownloader.Release("v4.0.2")), store, AppSettings.Default);
            Assert.True(settings.CheckUpdatesAtStart);

            settings.CheckUpdatesAtStart = false;

            Assert.False(settings.CheckUpdatesAtStart);
            Assert.False(Assert.Single(store.Saved).CheckUpdatesAtStart);
            Assert.True(store.Saved[0].CreateRestorePoint);
        });
    }

    [Fact]
    public void Layout_UpdateAndRestartBanners_ShareOneStackPanel()
    {
        var window = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "DSOptimizer.App", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var reboot = window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "RebootBanner");
        var update = window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "UpdateBanner");

        Assert.Same(reboot.Parent, update.Parent);
        Assert.Equal("StackPanel", update.Parent!.Name.LocalName);
        Assert.Equal("1", (string?)update.Parent.Attribute("Grid.Row"));
        Assert.Null(reboot.Attribute("Grid.Row"));
        Assert.Null(update.Attribute("Grid.Row"));
    }

    [Fact]
    public void CliHasNoUpdateCheck()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src", "DSOptimizer.Core", "Cli"), "*.cs")
            .Append(Path.Combine(root, "src", "DSOptimizer.Windows", "CliHost.cs"))
            .ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.DoesNotContain("UpdateChecker", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    private static UpdatesViewModel NewUpdates(TempDirectory temp, IWebDownloader web) =>
        new(new UpdateChecker(web, "4.0.2", new NullLog()), NewShell(temp));

    private static SettingsViewModel NewSettings(TempDirectory temp, IWebDownloader web, ISettingsStore store, AppSettings initial)
    {
        var composition = AppComposition.Create(temp.AppRoot, null);
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        var session = new GuiSession(new LoadedCatalog(catalog.Definitions, "sha256:test"), composition.Services);
        var log = new NullLog();
        return new SettingsViewModel(
            new SettingsLoadResult(initial, null),
            store,
            session,
            new ThemeController(log),
            NewShell(temp),
            log,
            UiLanguage.Polish,
            new NoRelaunch(),
            () => { },
            new UpdatesViewModel(new UpdateChecker(web, "4.0.2", log), NewShell(temp)));
    }

    private static ShellActions NewShell(TempDirectory temp) =>
        new(
            new ShellFileRevealer(new WindowsExplorerProcessQuery(), temp.AppRoot.FullPath),
            new ShellUrlOpener(new WindowsExplorerProcessQuery()),
            new NullLog());

    /// <summary>Uruchamia metodę asynchroniczną na wątku roboczym (bez kontekstu synchronizacji) i czeka na jej koniec.</summary>
    private static void Run(Func<Task> action) => Assert.True(Task.Run(action).Wait(Patience));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DSOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
