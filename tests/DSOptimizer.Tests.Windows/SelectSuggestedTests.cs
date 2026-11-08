using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Przycisk „Zaznacz sugerowane” i zasada „nic nie zaznacza się samo”. Model okna jak w <see cref="MainViewModelTests"/>:
/// stany ustawiane prosto na pozycjach, detekcja ani żadna operacja nie jest uruchamiana.
/// </summary>
[Trait("Category", "Windows")]
public class SelectSuggestedTests
{
    /// <summary>Liczba pozycji privacy z sugestią oraz tych z nich o ryzyku Safe; jedyne miejsce z tymi liczbami.</summary>
    private const int SuggestedPrivacy = 15;
    private const int SuggestedPrivacySafe = 12;

    private const string UserSid = "S-1-5-21-1000";
    private const string MicrosoftDn = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";

    private sealed class FakeReader : IInstalledAppsReader
    {
        public List<InstalledApp> Apps { get; } = new();

        public InstalledAppsReading Read(string userSid) => new(Apps.ToList(), Array.Empty<string>());

        public AppPresence ReadPresence(InstalledApp app, string userSid, AppPresenceScope scope) => AppPresence.Unknown;
    }

    private sealed class FixedUser : IUserContext
    {
        public string Sid => UserSid;

        public bool IsCertain => true;
    }

    private sealed class NoNames : IAccountNames
    {
        public string? NameOf(string sid) => null;
    }

    private static CategoryViewModel Category(MainViewModel vm, string id) => vm.Categories.First(category => category.Id == id);

    private static void SetAllStates(CategoryViewModel category, DetectionState state)
    {
        foreach (var item in category.Items.Where(item => item.IsTweak))
        {
            item.SetState(state);
        }
    }

    private static void SetBusy(MainViewModel vm, bool value)
        => typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.GetSetMethod(nonPublic: true)!.Invoke(vm, new object[] { value });

    private static void Run(Action<MainViewModel> body)
        => MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            body(MainViewModelTests.Create(temp));
        });

    [Fact]
    public void EnteringCategory_WithKnownStates_SelectsNothing()
        => Run(vm =>
        {
            var privacy = Category(vm, "privacy");
            vm.SelectedPage = privacy;
            SetAllStates(privacy, DetectionState.NotApplied);
            vm.SelectedPage = Category(vm, "ux");
            vm.SelectedPage = privacy;

            Assert.Equal(0, vm.SelectedCount);
            Assert.DoesNotContain(privacy.Items, item => item.IsSelected);
        });

    [Fact]
    public void SelectSuggested_SelectsAllSuggestedPrivacyItems_AndChangesNoState()
        => Run(vm =>
        {
            var privacy = Category(vm, "privacy");
            vm.SelectedPage = privacy;
            SetAllStates(privacy, DetectionState.NotApplied);

            Assert.True(vm.SelectSuggestedCommand.CanExecute(null));
            vm.SelectSuggestedCommand.Execute(null);

            Assert.Equal(SuggestedPrivacy, vm.SelectedCount);
            Assert.All(privacy.Items.Where(item => item.IsSelected), item => Assert.True(item.IsSuggested));
            Assert.All(privacy.Items.Where(item => item.IsTweak), item => Assert.Equal(DetectionState.NotApplied, item.State));
            Assert.False(vm.IsBusy);
        });

    [Fact]
    public void SelectSuggested_SkipsItemsHiddenByRiskFilter()
        => Run(vm =>
        {
            var privacy = Category(vm, "privacy");
            vm.SelectedPage = privacy;
            SetAllStates(privacy, DetectionState.NotApplied);
            vm.RiskFilter = RiskFilter.SafeOnly;

            vm.SelectSuggestedCommand.Execute(null);

            Assert.False(privacy.Items.First(item => item.Id == "privacy.diagtrack").IsSelected);
            Assert.True(privacy.Items.First(item => item.Id == "privacy.widgets").IsSelected);
            Assert.Equal(SuggestedPrivacySafe, vm.SelectedCount);
        });

    [Fact]
    public void SelectSuggested_InSearch_TakesOnlyVisibleResults()
        => Run(vm =>
        {
            var privacy = Category(vm, "privacy");
            vm.SelectedPage = privacy;
            SetAllStates(privacy, DetectionState.NotApplied);
            vm.SearchText = "diagtrack";

            Assert.True(vm.SelectSuggestedCommand.CanExecute(null));
            vm.SelectSuggestedCommand.Execute(null);

            Assert.True(privacy.Items.First(item => item.Id == "privacy.diagtrack").IsSelected);
            Assert.False(privacy.Items.First(item => item.Id == "privacy.widgets").IsSelected);
            Assert.True(vm.SelectedCount < SuggestedPrivacy);
        });

    [Fact]
    public void SelectSuggested_Twice_DoesNotToggle_AndKeepsManualSelection()
        => Run(vm =>
        {
            var privacy = Category(vm, "privacy");
            vm.SelectedPage = privacy;
            SetAllStates(privacy, DetectionState.NotApplied);
            var manual = privacy.Items.First(item => item.IsTweak && !item.IsSuggested);
            manual.IsSelected = true;

            vm.SelectSuggestedCommand.Execute(null);
            vm.SelectSuggestedCommand.Execute(null);

            Assert.True(manual.IsSelected);
            Assert.Equal(SuggestedPrivacy + 1, vm.SelectedCount);
        });

    [Fact]
    public void SelectSuggested_Button_HiddenOnCategoryWithoutSuggestions()
        => Run(vm =>
        {
            var changes = 0;
            vm.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(MainViewModel.IsSelectSuggestedVisible) ? 1 : 0;

            vm.SelectedPage = Category(vm, "ux");
            Assert.False(vm.IsSelectSuggestedVisible);
            vm.SelectedPage = Category(vm, "privacy");
            Assert.True(vm.IsSelectSuggestedVisible);
            vm.SelectedPage = vm.Apps;
            Assert.False(vm.IsSelectSuggestedVisible);
            Assert.True(changes >= 3);
        });

    [Fact]
    public void Execute_WhileBusy_DoesNothing()
        => Run(vm =>
        {
            var privacy = Category(vm, "privacy");
            vm.SelectedPage = privacy;
            SetAllStates(privacy, DetectionState.NotApplied);
            SetBusy(vm, true);

            Assert.False(vm.SelectSuggestedCommand.CanExecute(null));
            vm.SelectSuggestedCommand.Execute(null);

            Assert.Equal(0, vm.SelectedCount);
        });

    private static InstalledApp Store(string package, string name) => new(
        $"appx:{package}_8wekyb3d8bbwe", AppKind.Store, name, MicrosoftDn, "1.0.0.0", AppScope.User, UserSid,
        new AppxInfo($"{package}_1.0.0.0_x64__8wekyb3d8bbwe", $"{package}_8wekyb3d8bbwe", package, false, false, false, "Store", true),
        null);

    private static void WithApps(Action<AppsViewModel> body)
        => MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var composition = AppComposition.Create(temp.AppRoot, null);
            var services = composition.Services;
            var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
            var gui = new GuiSession(new LoadedCatalog(catalog.Definitions, "sha256:test"), services);
            var reader = new FakeReader();
            reader.Apps.Add(Store("Microsoft.BingNews", "Microsoft News"));
            reader.Apps.Add(Store("Microsoft.BingWeather", "MSN Weather"));
            reader.Apps.Add(Store("Contoso.Notes", "Contoso Notes"));
            var user = new FixedUser();
            var session = new AppsSession(
                gui,
                reader,
                new AppRemovalEngine(reader, new DSOptimizer.Windows.Apps.AppxRemover(services.Scripts), services.Journal, services.RestorePoints, user, UserSid, composition.Log, services.Clock),
                AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json),
                new DSOptimizer.Windows.Apps.SystemProtectionReader(),
                new NoNames(),
                user,
                UserSid,
                _ => false,
                Environment.SystemDirectory,
                services.Clock);
            body(new AppsViewModel(session, new NoNames(), _ => Task.CompletedTask, () => false, (_, _) => { }, services.Clock));
        });

    private static void Refresh(AppsViewModel apps) => Task.Run(apps.RefreshAsync).GetAwaiter().GetResult();

    [Fact]
    public void Apps_Refresh_SelectsNothing_ButtonSelectsVisibleOnly()
        => WithApps(apps =>
        {
            Refresh(apps);
            Assert.Equal(0, apps.SelectedCount);
            Assert.Equal(2, apps.VisibleRows.Count);

            apps.SelectSuggestedCommand.Execute(null);
            Assert.Equal(2, apps.SelectedCount);

            apps.ClearSelectionCommand.Execute(null);
            apps.SearchText = "news";
            apps.SelectSuggestedCommand.Execute(null);

            Assert.Equal(1, apps.SelectedCount);
            Assert.True(apps.VisibleRows.Single().IsSelected);
        });

    [Fact]
    public void Apps_Button_HiddenOutsideSuggestedGroup()
        => WithApps(apps =>
        {
            Refresh(apps);
            Assert.True(apps.IsSelectSuggestedVisible);

            apps.SelectedGroup = AppGroup.Other;

            Assert.False(apps.IsSelectSuggestedVisible);
            apps.SelectSuggestedCommand.Execute(null);
            Assert.Equal(0, apps.SelectedCount);
        });

    [Fact]
    public void Apps_Refresh_KeepsSelectionFromButton()
        => WithApps(apps =>
        {
            Refresh(apps);
            apps.SelectSuggestedCommand.Execute(null);
            var selected = apps.VisibleRows.Where(row => row.IsSelected).Select(row => row.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

            Refresh(apps);

            Assert.Equal(2, apps.SelectedCount);
            Assert.Equal(selected, apps.VisibleRows.Where(row => row.IsSelected).Select(row => row.Id).OrderBy(id => id, StringComparer.Ordinal).ToList());
        });
}
