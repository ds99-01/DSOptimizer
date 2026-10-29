using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Prep;
using DSOptimizer.Core.Settings;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows.Localization;

/// <summary>
/// Strony okna (Instalacja, Aplikacje, Historia zmian, Przygotowanie, Specyfikacja, Ustawienia) po angielsku: dokładne teksty
/// z <c>app-pages.json</c> i kluczy już przetłumaczonych w innych plikach. Teksty XAML sprawdzane przez klucz <c>{l:T '…'}</c>
/// w pliku widoku i <see cref="TExtension"/>; modele stron bez okna, bez winget i bez sieci (sesje fałszywe, wątek STA tam,
/// gdzie model potrzebuje dyspozytora).
/// </summary>
[Trait("Category", "Windows")]
public class AppPagesEnglishTests
{
    private static readonly AppsInstallCatalog InstallCatalog = AppsInstallCatalog.Load("""
        { "entries": [
          { "id": "install.firefox", "wingetId": "Mozilla.Firefox", "title": "Mozilla Firefox", "summary": "Browser.",
            "category": "browsers", "match": { "displayName": "Mozilla Firefox*" }, "source": "https://example.com/firefox" },
          { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiver.",
            "category": "tools", "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" },
          { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Remote desktop.",
            "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
            "source": "https://github.com/rustdesk/rustdesk/releases" },
          { "id": "install.anydesk", "wingetId": "AnyDesk.AnyDesk", "title": "AnyDesk", "summary": "Remote desktop.",
            "category": "remote-help", "match": { "displayName": "AnyDesk" }, "source": "https://example.com/anydesk" }
        ] }
        """);

    [Fact]
    public async Task Install_SearchPlaceholder_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var session = new FakeInstallSession(InstallState(new WingetStatus(WingetState.Ready, "v1.11.510", "winget works")));
            var vm = NewInstall(session);
            await vm.EnterAsync();
            Assert.Equal("Search apps, e.g. VLC", vm.SearchPlaceholder);

            session.State = InstallState(NotWorking());
            await vm.RefreshAsync();
            Assert.Equal("Search apps on the list, e.g. VLC", vm.SearchPlaceholder);

            AssertXamlTexts("InstallView.xaml", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Szukaj aplikacji"] = "Search apps",
                ["Kategorie"] = "Categories",
                ["Aplikacje do instalacji"] = "Apps to install",
                ["Odznacz wszystko"] = "Clear selection",
            });
        }
    }

    [Fact]
    public async Task Install_WingetNotWorking_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var vm = NewInstall(new FakeInstallSession(InstallState(NotWorking())));
            await vm.EnterAsync();

            Assert.True(vm.HasWingetNotice);
            Assert.Equal("Winget doesn't work on this computer", vm.WingetNoticeTitle);
            Assert.Equal("winget doesn't work (winget --version: code 0x00000001). Use \"Repairs → Repair winget\".", vm.WingetNotice);
            Assert.Equal("RustDesk (from GitHub) can be installed without winget too.", vm.GitHubStillAvailable);
            Assert.Equal("Show RustDesk", vm.ShowRustDeskText);
            Assert.Equal("Go to: Repair winget", InstallText.RepairWingetButton);
            Assert.Equal("Sources: GitHub (RustDesk) · winget (Microsoft) unavailable · 4 apps · installed: 0", vm.Subtitle);
            Assert.Equal("All", vm.Categories[0].Name);
            Assert.Equal("Selected", vm.Categories[1].Name);
            Assert.Contains("{x:Static install:InstallText.RepairWingetButton}", ReadView("InstallView.xaml"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Apps_GroupNames_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Suggested for removal", new AppGroupOption(AppGroup.Suggested).Name);
            Assert.Equal("Other", new AppGroupOption(AppGroup.Other).Name);
            Assert.Equal("Protected", new AppGroupOption(AppGroup.Protected).Name);
            AssertXamlTexts("AppsView.xaml", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Szukaj aplikacji"] = "Search apps",
                ["Grupy aplikacji"] = "App groups",
                ["Aplikacje"] = "Apps",
                ["Grupa"] = "Group",
                ["Rodzaj"] = "Type",
                ["Wydawca"] = "Publisher",
                ["Wersja"] = "Version",
                ["Konto"] = "Account",
                ["Usuwanie"] = "Removal",
                ["Cofanie"] = "Undo",
                ["Odznacz wszystko"] = "Clear selection",
            });
        }
    }

    [Fact]
    public void Apps_TechnicalDetails_English()
    {
        using (L.Use(UiLanguage.English))
        {
            const string Key = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Trial";
            var app = new InstalledApp(
                "win32:" + Key, AppKind.Program, "Trial", "Example", "1.0", AppScope.Machine, null, null,
                new Win32Info(Key, "uninstall.exe", "uninstall.exe /S", false, null, null, false));
            var row = new AppRow(
                app, AppGroup.Suggested, null, null, null, "https://example.com/cleanup", AppRemovability.Removable, null,
                new UninstallPlan(@"C:\Trial\uninstall.exe", "/S", UninstallMode.Quiet));

            var vm = new AppRowViewModel(row, null, null, StoreRemovalScope.CurrentUser, () => { });

            Assert.Equal(
                string.Join(
                    Environment.NewLine,
                    "Uninstall entry: " + Key,
                    "Mode: Manufacturer's silent uninstaller, no window",
                    "Command: \"C:\\Trial\\uninstall.exe\" /S",
                    "Manufacturer's cleanup tool: https://example.com/cleanup"),
                vm.TechnicalText);

            var store = new InstalledApp(
                "appx:Example.App_8wekyb3d8bbwe", AppKind.Store, "Example", null, "1.0", AppScope.User, null,
                new AppxInfo("Example.App_1.0.0.0_x64__8wekyb3d8bbwe", "Example.App_8wekyb3d8bbwe", "Example.App", false, false, false, "Store", false),
                null);
            var storeRow = new AppRow(store, AppGroup.Other, null, null, null, null, AppRemovability.Removable, null, null);
            Assert.Equal(
                "Package: Example.App_1.0.0.0_x64__8wekyb3d8bbwe",
                new AppRowViewModel(storeRow, null, null, StoreRemovalScope.CurrentUser, () => { }).TechnicalText);
        }
    }

    [Fact]
    public void Apps_RemoveCount_English()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using (L.Use(UiLanguage.English))
            {
                var vm = MainViewModelTests.Create(temp);
                Assert.Equal("Remove (0)", vm.Apps.RemoveText);
                Assert.Equal("Settings", vm.Settings.Name);
                Assert.Equal("Version " + DSOptimizer.Core.ProductInfo.Version, vm.Settings.VersionText);
                Assert.Equal("Specification", vm.Specification.Name);
            }
        });
    }

    [Fact]
    public void History_Filter_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var vm = new HistoryViewModel(
                () => Task.FromResult(new HistorySnapshot(Array.Empty<HistoryEntry>(), null)), _ => Task.CompletedTask, () => { }, (_, _) => { });

            Assert.Equal(
                new[] { "All entries", "Active", "Waiting for restart", "Undone and interrupted" },
                vm.Filters.Select(filter => filter.Name).ToArray());
        }
    }

    [Fact]
    public async Task History_UnexpectedError_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var lines = new List<(GuiLogLevel Level, string Text)>();
            var vm = new HistoryViewModel(
                () => Task.FromResult(new HistorySnapshot(Array.Empty<HistoryEntry>(), "warning")),
                _ => Task.CompletedTask,
                () => { },
                (level, text) =>
                {
                    if (level == GuiLogLevel.Warning)
                    {
                        throw new InvalidOperationException("boom");
                    }

                    lines.Add((level, text));
                });

            await vm.RefreshAsync();

            Assert.Contains((GuiLogLevel.Error, "Unexpected error: InvalidOperationException: boom"), lines);
        }
    }

    [Fact]
    public void Prep_RefreshButton_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Refresh status", PrepText.RefreshButton);
            Assert.Contains("{x:Static prep:PrepText.RefreshButton}", ReadView("PrepView.xaml"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Settings_LanguageHint_English()
    {
        using (L.Use(UiLanguage.English))
        {
            AssertXamlTexts("SettingsView.xaml", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Język"] = "Language",
                ["Język zmieni się po ponownym uruchomieniu programu."] = "The language changes after the program restarts.",
                ["Uruchom ponownie teraz"] = "Restart now",
            });
        }
    }

    [Fact]
    public void Theme_Names_English()
    {
        using (L.Use(UiLanguage.English))
        {
            AssertXamlTexts("SettingsView.xaml", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Motyw"] = "Theme",
                ["Systemowy"] = "System",
                ["Jasny"] = "Light",
                ["Ciemny"] = "Dark",
                ["Systemowy zmienia się razem z motywem Windows. Pasek marki i menu zostają grafitowe."] =
                    "\"System\" follows the Windows theme. The brand bar and the menu stay graphite.",
            });
        }
    }

    [Fact]
    public void Settings_Texts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            AssertXamlTexts("SettingsView.xaml", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Ustawienia"] = "Settings",
                ["Zmiany zapisują się od razu."] = "Changes are saved right away.",
                ["Zmiany w systemie"] = "System changes",
                ["Twórz punkt przywracania przed zmianami"] = "Create a restore point before changes",
                ["Przed pierwszą zmianą każdego „Zastosuj” (pojedynczego i w serii) program próbuje utworzyć punkt przywracania Windows. Gdy się nie uda, na przykład przy wyłączonej ochronie systemu, zmiany i tak zostaną wprowadzone, a dziennik pokaże ostrzeżenie. Nie dotyczy „Cofnij” ani akcji."] =
                    "Before the first change of every \"Apply\" (single or in a batch), the program tries to create a Windows restore point. If that fails, for example when System Protection is off, the changes are still made and the log shows a warning. This doesn't apply to \"Undo\" or to actions.",
                ["Wygląd i język"] = "Appearance and language",
                ["Autor"] = "Author",
                ["Licencja"] = "License",
                ["Otwórz github.com/ds99-01/DSOptimizer"] = "Open github.com/ds99-01/DSOptimizer",
                ["Licencje komponentów"] = "Component licenses",
            });
        }
    }

    [Fact]
    public void Settings_SaveFailed_English()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using (L.Use(UiLanguage.English))
            {
                var log = new CapturingLog();
                var vm = NewSettings(temp, new FailingStore(), log);

                vm.CreateRestorePoint = false;

                Assert.Contains("WARN Settings: Couldn't save the settings: Access denied. The change lasts until the program closes.", log.Lines);
            }
        });
    }

    [Fact]
    public void Specification_Save_English()
    {
        using (L.Use(UiLanguage.English))
        {
            AssertXamlTexts("SpecificationView.xaml", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Zapisz raport"] = "Save report",
                ["Specyfikacja"] = "Specification",
                ["Odśwież stan"] = "Refresh status",
                ["Kopiuj"] = "Copy",
                ["Otwórz github.com/ds99-01/DSOptimizer"] = "Open github.com/ds99-01/DSOptimizer",
            });
            Assert.Equal("Couldn't save the report: Access denied.", SpecificationViewModel.ReportNotSavedText("Access denied."));
        }
    }

    [Fact]
    public void LanguageChoice_StaysBilingual()
    {
        using (L.Use(UiLanguage.English))
        {
            var xaml = ReadView("LanguageChoiceWindow.xaml");
            Assert.DoesNotContain("{l:T", xaml, StringComparison.Ordinal);
            Assert.Contains("Text=\"Język / Language\"", xaml, StringComparison.Ordinal);
        }
    }

    private static WingetStatus NotWorking() =>
        new(WingetState.NotWorking, null, InstallText.WingetNotWorking(InstallText.VersionExitCode(1)));

    private static InstallPageState InstallState(WingetStatus winget) =>
        new(InstallPage.CatalogRows(InstallCatalog, Array.Empty<InstalledApp>(), winget, true), winget, Array.Empty<string>());

    private static InstallViewModel NewInstall(IInstallPageSession session) =>
        new(session, _ => Task.CompletedTask, () => { }, (_, _) => { }, new FixedClock());

    private static SettingsViewModel NewSettings(TempDirectory temp, ISettingsStore store, ILog log)
    {
        var composition = AppComposition.Create(temp.AppRoot, null);
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        var session = new GuiSession(new LoadedCatalog(catalog.Definitions, "sha256:test"), composition.Services);
        var shell = new ShellActions(
            new ShellFileRevealer(new WindowsExplorerProcessQuery(), temp.AppRoot.FullPath),
            new ShellUrlOpener(new WindowsExplorerProcessQuery()),
            log);
        return new SettingsViewModel(
            new SettingsLoadResult(AppSettings.Default, null), store, session, new ThemeController(log), shell, log, UiLanguage.English, new NoRelauncher(), () => { });
    }

    /// <summary>Każdy klucz stoi w widoku jako <c>{l:T '…'}</c> (albo Name/Text strony), a po angielsku daje dokładny tekst.</summary>
    private static void AssertXamlTexts(string view, IReadOnlyDictionary<string, string> expected)
    {
        var xaml = ReadView(view);
        foreach (var pair in expected)
        {
            Assert.Contains("{l:T '" + pair.Key + "'}", xaml, StringComparison.Ordinal);
            Assert.Equal(pair.Value, new TExtension(pair.Key).ProvideValue(null!));
        }
    }

    private static string ReadView(string name)
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DSOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "DSOptimizer.App", "Views", name));
    }

    private sealed class FakeInstallSession : IInstallPageSession
    {
        public FakeInstallSession(InstallPageState state) => State = state;

        public InstallPageState State { get; set; }

        public AppsInstallCatalog Catalog => InstallCatalog;

        public InstallPageState Load() => State;

        public InstallSearchResult Search(string query) => new(Array.Empty<InstallRow>(), InstallText.SearchNoResults, Array.Empty<string>());
    }

    private sealed class FailingStore : ISettingsStore
    {
        public SettingsLoadResult Load() => new(AppSettings.Default, null);

        public void Save(AppSettings settings) => throw new SettingsStoreException("Access denied.");
    }

    private sealed class NoRelauncher : IProgramRelauncher
    {
        public void Relaunch() => throw new InvalidOperationException("The test never restarts the program.");
    }

    private sealed class CapturingLog : ILog
    {
        public List<string> Lines { get; } = new();

        public void Info(string component, string message) => Lines.Add("INFO " + component + ": " + message);

        public void Warn(string component, string message) => Lines.Add("WARN " + component + ": " + message);

        public void Fail(string component, string message, Exception? exception = null) => Lines.Add("FAIL " + component + ": " + message);
    }
}
