using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Settings;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Pozycja „Język” w Ustawieniach: wybór zapisuje się od razu, a nowy język obowiązuje po ponownym uruchomieniu programu.
/// Relauncher zawsze fałszywy – test nigdy nie startuje nowej instancji programu.
/// </summary>
[Trait("Category", "Windows")]
public class SettingsViewModelLanguageTests
{
    private sealed class FakeStore : ISettingsStore
    {
        public List<AppSettings> Saved { get; } = new();

        public bool Fail { get; init; }

        public SettingsLoadResult Load() => new(AppSettings.Default, null);

        public void Save(AppSettings settings)
        {
            if (Fail)
            {
                throw new SettingsStoreException("Brak dostępu.");
            }

            Saved.Add(settings);
        }
    }

    private sealed class FakeRelauncher : IProgramRelauncher
    {
        public int Calls { get; private set; }

        public bool Fail { get; init; }

        public void Relaunch()
        {
            Calls++;
            if (Fail)
            {
                throw new OperationException(OperationErrorKind.Unexpected, "Nie udało się ponownie uruchomić programu: test");
            }
        }
    }

    private sealed class RecordingLog : ILog
    {
        public List<string> Lines { get; } = new();

        public void Info(string component, string message) => Lines.Add("INFO " + message);

        public void Warn(string component, string message) => Lines.Add("WARN " + message);

        public void Fail(string component, string message, Exception? exception = null) => Lines.Add("FAIL " + message);
    }

    private sealed class Fixture
    {
        public FakeStore Store { get; init; } = new();

        public FakeRelauncher Relauncher { get; init; } = new();

        public RecordingLog Log { get; } = new();

        public int Shutdowns { get; set; }
    }

    [Fact]
    public void SelectedLanguage_InitialIsRunning()
    {
        RunOnSta(temp =>
        {
            var fixture = new Fixture();
            var vm = Create(temp, fixture, UiLanguage.English);

            Assert.Equal(UiLanguage.English, vm.SelectedLanguage);
            Assert.False(vm.RelaunchNeeded);
            Assert.False(vm.RelaunchCommand.CanExecute(null));
            Assert.Empty(fixture.Store.Saved);
        });
    }

    [Fact]
    public void SelectEnglish_SavesAndNeedsRelaunch()
    {
        RunOnSta(temp =>
        {
            var fixture = new Fixture();
            var vm = Create(temp, fixture, UiLanguage.Polish);
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            vm.SelectedLanguage = UiLanguage.English;

            Assert.Equal(UiLanguage.English, Assert.Single(fixture.Store.Saved).Language);
            Assert.True(vm.RelaunchNeeded);
            Assert.True(vm.RelaunchCommand.CanExecute(null));
            Assert.Contains(nameof(SettingsViewModel.SelectedLanguage), changed);
            Assert.Contains(nameof(SettingsViewModel.RelaunchNeeded), changed);
        });
    }

    [Fact]
    public void SelectBackRunning_NoRelaunchNeeded()
    {
        RunOnSta(temp =>
        {
            var fixture = new Fixture();
            var vm = Create(temp, fixture, UiLanguage.Polish);

            vm.SelectedLanguage = UiLanguage.English;
            vm.SelectedLanguage = UiLanguage.Polish;

            Assert.Equal(new UiLanguage?[] { UiLanguage.English, UiLanguage.Polish }, fixture.Store.Saved.Select(s => s.Language).ToArray());
            Assert.False(vm.RelaunchNeeded);
            Assert.False(vm.RelaunchCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Relaunch_StartsAndShutsDown()
    {
        RunOnSta(temp =>
        {
            var fixture = new Fixture();
            var vm = Create(temp, fixture, UiLanguage.Polish);
            vm.SelectedLanguage = UiLanguage.English;

            vm.RelaunchCommand.Execute(null);

            Assert.Equal(1, fixture.Relauncher.Calls);
            Assert.Equal(1, fixture.Shutdowns);
        });
    }

    [Fact]
    public void Relaunch_StartFails_StaysOpenAndLogs()
    {
        RunOnSta(temp =>
        {
            var fixture = new Fixture { Relauncher = new FakeRelauncher { Fail = true } };
            var vm = Create(temp, fixture, UiLanguage.Polish);
            vm.SelectedLanguage = UiLanguage.English;

            vm.RelaunchCommand.Execute(null);

            Assert.Equal(1, fixture.Relauncher.Calls);
            Assert.Equal(0, fixture.Shutdowns);
            Assert.Contains("FAIL Nie udało się ponownie uruchomić programu: test", fixture.Log.Lines);
            Assert.True(vm.RelaunchCommand.CanExecute(null));
        });
    }

    [Fact]
    public void RelaunchCommand_DisabledWhileBusy()
    {
        RunOnSta(temp =>
        {
            var fixture = new Fixture();
            var vm = Create(temp, fixture, UiLanguage.Polish);
            vm.SelectedLanguage = UiLanguage.English;
            var raised = 0;
            vm.RelaunchCommand.CanExecuteChanged += (_, _) => raised++;

            vm.IsEditable = false;

            Assert.False(vm.RelaunchCommand.CanExecute(null));
            Assert.True(raised > 0);
            vm.RelaunchCommand.Execute(null);
            Assert.Equal(0, fixture.Relauncher.Calls);
            Assert.Equal(0, fixture.Shutdowns);

            vm.IsEditable = true;
            Assert.True(vm.RelaunchCommand.CanExecute(null));
        });
    }

    [Fact]
    public void SaveFails_LanguageStillSelected()
    {
        RunOnSta(temp =>
        {
            var fixture = new Fixture { Store = new FakeStore { Fail = true } };
            var vm = Create(temp, fixture, UiLanguage.Polish);

            vm.SelectedLanguage = UiLanguage.English;

            Assert.Equal(UiLanguage.English, vm.SelectedLanguage);
            Assert.True(vm.RelaunchNeeded);
            Assert.Contains(fixture.Log.Lines, l => l.StartsWith("WARN Nie udało się zapisać ustawień: Brak dostępu.", StringComparison.Ordinal));
        });
    }

    private static SettingsViewModel Create(TempDirectory temp, Fixture fixture, UiLanguage running)
    {
        var composition = AppComposition.Create(temp.AppRoot, null);
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        var session = new GuiSession(new LoadedCatalog(catalog.Definitions, "sha256:test"), composition.Services);
        var shell = new ShellActions(
            new ShellFileRevealer(new WindowsExplorerProcessQuery(), temp.AppRoot.FullPath),
            new ShellUrlOpener(new WindowsExplorerProcessQuery()),
            fixture.Log);
        var loaded = new SettingsLoadResult(AppSettings.Default with { Language = running }, null);
        return new SettingsViewModel(
            loaded, fixture.Store, session, new ThemeController(fixture.Log), shell, fixture.Log, running, fixture.Relauncher, () => fixture.Shutdowns++);
    }

    private static void RunOnSta(Action<TempDirectory> body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var temp = new TempDirectory();
                body(temp);
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
