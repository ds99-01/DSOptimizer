using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using Xunit;

namespace DSOptimizer.Tests.Windows.Localization;

/// <summary>
/// Okno główne, wspólne elementy i start programu po angielsku: dokładne teksty z <c>app.json</c> (i kluczy już przetłumaczonych
/// w innych plikach). Model okna składany jak w <see cref="MainViewModelTests"/> (katalog tymczasowy, wątek STA, bez detekcji,
/// fałszywy restart); żadne okno nie jest pokazywane.
/// </summary>
[Trait("Category", "Windows")]
public class AppEnglishTests
{
    [Fact]
    public void MainViewModel_ApplyCount_English()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using (L.Use(UiLanguage.English))
            {
                var vm = MainViewModelTests.Create(temp);
                Assert.Equal("Apply (0)", vm.ApplySelectedText);
                Assert.Equal("Undo (0)", vm.UndoSelectedText);
                Assert.Equal("Status not checked yet", vm.StatusText);
            }
        });
    }

    [Fact]
    public void MainViewModel_LastCheck_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Last check: 14:05", MainViewModel.LastCheckText(new DateTimeOffset(2026, 10, 28, 14, 5, 0, TimeSpan.FromHours(1))));
        }
    }

    [Fact]
    public void MainViewModel_OperationTexts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Canceled: Apply – Turn off telemetry.", MainViewModel.CanceledText(GuiOperation.Apply, "Turn off telemetry"));
            Assert.Equal("Undo: selected (3)…", MainViewModel.SelectedOperationText(GuiOperation.Undo, 3));
            Assert.Equal("Remove: selected apps (2)…", MainViewModel.SelectedAppsOperationText(GuiOperation.RemoveApps, 2));
            Assert.Equal("Checking status…", MainViewModel.CheckingText);
            Assert.Equal("Restarting the computer…", MainViewModel.RestartingText);
        }
    }

    [Fact]
    public void ItemViewModel_RiskText_English()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using (L.Use(UiLanguage.English))
            {
                var items = MainViewModelTests.Create(temp).Categories.SelectMany(category => category.Items).ToList();
                Assert.Equal("Safe", items.First(item => item.Risk == RiskLevel.Safe).DetailRiskText);
                Assert.Equal("Caution", items.First(item => item.Risk == RiskLevel.Caution).DetailRiskText);
            }
        });
    }

    [Fact]
    public void ItemViewModel_DetailTexts_English()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            using (L.Use(UiLanguage.English))
            {
                var vm = MainViewModelTests.Create(temp);
                var items = vm.Categories.SelectMany(category => category.Items).ToList();
                var tweak = items.First(item => item.IsTweak && item.Definition.RequiresReboot && item.Definition.Reversible);
                Assert.Equal("Settings", tweak.SectionName);
                Assert.Equal("Checking…", tweak.StateText);
                Assert.Equal("Required", tweak.DetailRebootText);
                Assert.Equal("Available", tweak.DetailUndoText);
                Assert.Equal("Not required", items.First(item => item.IsTweak && !item.Definition.RequiresReboot).DetailRebootText);

                // Wbudowany katalog nie ma nieodwracalnego tweaka: pozycja spoza katalogu, tylko do odczytu tekstów.
                var irreversible = new ItemViewModel(
                    new TweakDefinition
                    {
                        Id = "test.irreversible",
                        Category = "privacy",
                        Kind = TweakKind.Tweak,
                        Title = "Test",
                        Risk = RiskLevel.Safe,
                        Scope = TweakScope.Machine,
                        Reversible = false,
                    },
                    vm);
                Assert.Equal("Not available", irreversible.DetailUndoText);

                var action = items.First(item => item.Definition.Kind == TweakKind.Action);
                Assert.Equal("Actions", action.SectionName);
                Assert.Equal("This operation can't be undone", action.RowSubtitle);
                Assert.Equal("Not applicable", action.DetailUndoText);
                Assert.Equal("Opens a system window", items.First(item => item.Definition.Kind == TweakKind.Panel).RowSubtitle);
            }
        });
    }

    [Fact]
    public void SeriesWindow_Buttons_English()
    {
        var xaml = File.ReadAllText(RepoFile("src", "DSOptimizer.App", "Views", "SeriesWindow.xaml"));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Zamknij"] = "Close",
            ["Anuluj"] = "Cancel",
            ["Uruchom ponownie teraz"] = "Restart now",
            ["Kopiuj wynik"] = "Copy result",
        };
        using (L.Use(UiLanguage.English))
        {
            foreach (var pair in expected)
            {
                Assert.Contains("{l:T '" + pair.Key + "'}", xaml, StringComparison.Ordinal);
                Assert.Equal(pair.Value, new TExtension(pair.Key).ProvideValue(null!));
            }
        }
    }

    [Fact]
    public void SeriesWindowViewModel_RestartAndDetails_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var restart = SeriesWindowViewModel.ForRestart(new FixedClock());
            Assert.Equal("Restart", restart.HeaderTitle);
            Assert.Equal("Restart", restart.PrimaryText);
            Assert.Equal("Stop", SeriesWindowViewModel.StopText);
            Assert.Equal("Stopping…", SeriesWindowViewModel.StoppingText);
            Assert.Equal("Stopping finishes the current step and doesn't start the next ones.", SeriesWindowViewModel.StopNote);

            var item = new SeriesItemViewModel(new SeriesItem(
                "test.a", "Test", RiskLevel.Safe, "Summary", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SeriesFact>()));
            Assert.Equal("Technical details", item.ToggleText);
            item.IsExpanded = true;
            Assert.Equal("Hide technical details", item.ToggleText);
        }
    }

    [Fact]
    public void LogView_OpenLocationFailed_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Couldn't open the file location.", LogView.OpenLocationFailedMessage);
        }
    }

    [Fact]
    public void Theme_Texts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var resources = new ResourceDictionary();
            ThemeTexts.Apply(resources);
            Assert.Equal("Run", resources[ThemeTexts.RunToolTipKey]);
            Assert.Equal("Clear (Esc)", resources[ThemeTexts.SearchClearToolTipKey]);
            Assert.Equal("Clear search", resources[ThemeTexts.SearchClearNameKey]);
            Assert.Equal("Ctrl+F", resources[ThemeTexts.SearchShortcutKey]);
        }
    }

    [Fact]
    public void MachineSummary_WithUser_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("PC-1, Windows 11 Pro 26100.1, user Dawid", MachineSummary.WithUser("PC-1, Windows 11 Pro 26100.1", "Dawid"));
        }
    }

    [Fact]
    public async Task SeriesDialogs_Texts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Batch", SeriesDialogs.Component);
            Assert.Equal("Result copied to the clipboard.", SeriesDialogs.CopiedMessage);
            Assert.Equal("Couldn't copy the result to the clipboard: busy", SeriesDialogs.CopyFailedMessage("busy"));
            Assert.Equal("The restore point wasn't created (no space). Answer: Yes, continue.", SeriesDialogs.RestorePointAnsweredMessage("no space", "Yes, continue"));
            Assert.Equal("Continue without a restore point? Answer: No.", SeriesDialogs.ContinueAnsweredMessage("No"));

            // Bez trwającego okna serii: ostrzeżenie w logu i „Nie” (żadne okno się nie pokazuje).
            var log = new CapturingLog();
            var dialogs = new SeriesDialogs(log, (_, _, _) => { }, new FixedClock());
            Assert.False(await dialogs.ConfirmContinueWithoutRestorePointAsync("no space"));
            Assert.Equal(
                "Batch: The restore point wasn't created (no space) and the batch window isn't open – \"No\" was assumed.",
                Assert.Single(log.Lines));
        }
    }

    [Fact]
    public void Startup_Messages_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var nl = Environment.NewLine;
            Assert.Equal("DSOptimizer – warning", StartupText.WarningTitle);
            Assert.Equal("DSOptimizer – error", StartupText.ErrorTitle);
            Assert.Equal("The built-in catalog is invalid:" + nl + "a" + nl + "b", StartupText.CatalogInvalid(new[] { "a", "b" }));
            Assert.Equal("Can't prepare the DSOptimizer working folder: denied", StartupText.WorkingFolderFailed("denied"));
            Assert.Equal("Can't read the settings: denied Using default settings.", StartupText.SettingsReadFailed("denied"));
            Assert.Equal("The built-in app list is invalid:" + nl + "bad", StartupText.AppsListInvalid("bad"));
            Assert.Equal("The built-in install list is invalid:" + nl + "bad", StartupText.InstallListInvalid("bad"));
            Assert.Equal("Unhandled exception in the window.", StartupText.WindowExceptionLog);
            Assert.Equal("Unhandled exception – the program will close.", StartupText.FatalExceptionLog(true));
            Assert.Equal("Unhandled exception in the background.", StartupText.FatalExceptionLog(false));
            Assert.Equal(
                "An unexpected window error occurred:" + nl + "boom" + nl + nl + "Details were saved in the log file. The window may not work correctly – "
                + "close the program after the current operation finishes and start it again.",
                StartupText.WindowError("boom"));
            Assert.Equal(
                "Unexpected error – the program will close:" + nl + "boom" + nl + nl + "Details were saved in the log file. "
                + "Check the status of changed items after you start it again.",
                StartupText.FatalError("boom"));
        }
    }

    /// <summary>Plik repozytorium: katalog z <c>DSOptimizer.sln</c> nad katalogiem wyjściowym testów.</summary>
    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DSOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }

    private sealed class CapturingLog : ILog
    {
        public List<string> Lines { get; } = new();

        public void Info(string component, string message) => Lines.Add(component + ": " + message);

        public void Warn(string component, string message) => Lines.Add(component + ": " + message);

        public void Fail(string component, string message, Exception? exception = null) => Lines.Add(component + ": " + message);
    }
}
