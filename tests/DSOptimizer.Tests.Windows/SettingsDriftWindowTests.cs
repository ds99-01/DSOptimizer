using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Baner strażnika ustawień w oknie i „Zaznacz i pokaż”. Model okna jak w <see cref="MainViewModelTests"/>: wpisy journala w katalogu
/// tymczasowym, stany ustawiane prosto na pozycjach (<see cref="ItemViewModel.SetState"/>), detekcja ani żadna operacja nie jest uruchamiana.
/// </summary>
[Trait("Category", "Windows")]
public class SettingsDriftWindowTests
{
    // Pozycje maszynowe: SysMain (experimental) i DiagTrack (caution).
    private const string SysMain = "perf.sysmain";
    private const string DiagTrack = "privacy.diagtrack";
    private const string MenuDelay = "ux.menu-delay";

    [Fact]
    public void Refresh_TwoActiveEntriesNotApplied_BannerVisible()
        => Run((temp, vm) =>
        {
            SeedActive(temp, SysMain, DiagTrack);
            SetAllTweakStates(vm, DetectionState.NotApplied);

            MainViewModelTests.Wait(vm.RefreshDriftAsync());

            Assert.True(vm.DriftBanner.IsVisible);
            Assert.Equal("2 pozycje nie są już w pełni zastosowane.", vm.DriftBanner.Text);
            Assert.Equal(new[] { SysMain, DiagTrack }, vm.DriftBanner.Ids.OrderBy(id => id, StringComparer.Ordinal));
        });

    [Fact]
    public void Refresh_NoJournal_NoBanner()
        => Run((_, vm) =>
        {
            SetAllTweakStates(vm, DetectionState.NotApplied);

            MainViewModelTests.Wait(vm.RefreshDriftAsync());

            Assert.False(vm.DriftBanner.IsVisible);
        });

    [Fact]
    public void Refresh_ItemsWithoutState_NoBanner()
        => Run((temp, vm) =>
        {
            SeedActive(temp, SysMain, DiagTrack);

            MainViewModelTests.Wait(vm.RefreshDriftAsync());

            Assert.False(vm.DriftBanner.IsVisible);
        });

    [Fact]
    public void Refresh_AfterStatesApplied_BannerDisappears()
        => Run((temp, vm) =>
        {
            SeedActive(temp, SysMain, DiagTrack);
            SetAllTweakStates(vm, DetectionState.NotApplied);
            MainViewModelTests.Wait(vm.RefreshDriftAsync());
            Assert.True(vm.DriftBanner.IsVisible);

            Item(vm, SysMain).SetState(DetectionState.Applied);
            Item(vm, DiagTrack).SetState(DetectionState.Applied);
            MainViewModelTests.Wait(vm.RefreshDriftAsync());

            Assert.False(vm.DriftBanner.IsVisible);
        });

    [Fact]
    public void SelectAndShow_FromStartPage_AddsSelectionAndEntersSelectedView()
        => Run((temp, vm) =>
        {
            SeedActive(temp, SysMain, DiagTrack);
            SetAllTweakStates(vm, DetectionState.NotApplied);
            var entriesBefore = JournalCount(temp);
            MainViewModelTests.Wait(vm.RefreshDriftAsync());
            Item(vm, MenuDelay).IsSelected = true;
            var expected = vm.LastCategory;
            var statesBefore = States(vm);
            Assert.IsType<SpecificationViewModel>(vm.SelectedPage);

            Assert.True(vm.DriftBanner.SelectAndShowCommand.CanExecute(null));
            vm.DriftBanner.SelectAndShowCommand.Execute(null);

            Assert.Same(expected, vm.SelectedPage);
            Assert.True(vm.IsSelectedView);
            Assert.Equal(3, vm.SelectedCount);
            Assert.True(Item(vm, MenuDelay).IsSelected);
            Assert.True(Item(vm, SysMain).IsSelected);
            Assert.True(Item(vm, DiagTrack).IsSelected);
            Assert.Equal(statesBefore, States(vm));
            Assert.Empty(vm.LogLines);
            Assert.Equal(entriesBefore, JournalCount(temp));
            Assert.False(vm.IsBusy);
        });

    [Fact]
    public void SelectAndShow_RiskFilterKept_HiddenCounted()
        => Run((temp, vm) =>
        {
            SeedActive(temp, SysMain, DiagTrack);
            SetAllTweakStates(vm, DetectionState.NotApplied);
            MainViewModelTests.Wait(vm.RefreshDriftAsync());
            vm.RiskFilter = RiskFilter.SafeOnly;

            vm.DriftBanner.SelectAndShowCommand.Execute(null);

            Assert.Equal(RiskFilter.SafeOnly, vm.RiskFilter);
            Assert.True(vm.IsSelectedView);
            Assert.Equal("Ukryte przez filtr: 2", vm.FilterNoteText);
            Assert.Equal(2, vm.ApplicableCount);
        });

    [Fact]
    public void SelectAndShow_ClearsSearch()
        => Run((temp, vm) =>
        {
            SeedActive(temp, SysMain, DiagTrack);
            SetAllTweakStates(vm, DetectionState.NotApplied);
            MainViewModelTests.Wait(vm.RefreshDriftAsync());
            var privacy = vm.Categories.First(category => category.Id == "privacy");
            vm.SelectedPage = privacy;
            vm.SearchText = "widzet";

            vm.DriftBanner.SelectAndShowCommand.Execute(null);

            Assert.Equal(string.Empty, vm.SearchText);
            Assert.True(vm.IsSelectedView);
            Assert.Same(privacy, vm.SelectedPage);
        });

    [Fact]
    public void SelectAndShow_WhileBusy_DoesNothing()
        => Run((temp, vm) =>
        {
            SeedActive(temp, SysMain, DiagTrack);
            SetAllTweakStates(vm, DetectionState.NotApplied);
            MainViewModelTests.Wait(vm.RefreshDriftAsync());
            Assert.True(vm.DriftBanner.IsVisible);
            SetBusy(vm, true);

            Assert.False(vm.DriftBanner.SelectAndShowCommand.CanExecute(null));
            vm.DriftBanner.SelectAndShowCommand.Execute(null);

            Assert.Equal(0, vm.SelectedCount);
            Assert.False(vm.IsSelectedView);
        });

    [Fact]
    public void Hide_StaysHiddenAfterRefresh()
        => Run((temp, vm) =>
        {
            SeedActive(temp, SysMain, DiagTrack);
            SetAllTweakStates(vm, DetectionState.NotApplied);
            MainViewModelTests.Wait(vm.RefreshDriftAsync());
            Assert.True(vm.DriftBanner.IsVisible);

            vm.DriftBanner.HideCommand.Execute(null);
            MainViewModelTests.Wait(vm.RefreshDriftAsync());

            Assert.False(vm.DriftBanner.IsVisible);
        });

    private static void Run(Action<TempDirectory, MainViewModel> body)
        => MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            // Kontynuacje await wracają na wątek testu (jak w oknie), a Wait/PumpUntil je wykonują.
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            body(temp, MainViewModelTests.Create(temp));
        });

    private static ItemViewModel Item(MainViewModel vm, string id)
        => vm.Categories.SelectMany(category => category.Items).First(item => item.Id == id);

    private static void SetAllTweakStates(MainViewModel vm, DetectionState state)
    {
        foreach (var item in vm.Categories.SelectMany(category => category.Items).Where(item => item.IsTweak))
        {
            item.SetState(state);
        }
    }

    private static Dictionary<string, DetectionState?> States(MainViewModel vm)
        => vm.Categories.SelectMany(category => category.Items).ToDictionary(item => item.Id, item => item.State, StringComparer.Ordinal);

    private static void SetBusy(MainViewModel vm, bool value)
        => typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.GetSetMethod(nonPublic: true)!.Invoke(vm, new object[] { value });

    private static IJournalStore Journal(TempDirectory temp) => AppComposition.Create(temp.AppRoot, null).Services.Journal;

    private static int JournalCount(TempDirectory temp) => Journal(temp).Load().Document.Entries.Count;

    /// <summary>Aktywne wpisy „zastosowano” pozycji katalogu (bez <c>Kind</c>) dla konta procesu, sprzed 5 minut.</summary>
    private static void SeedActive(TempDirectory temp, params string[] ids)
    {
        var services = AppComposition.Create(temp.AppRoot, null).Services;
        var journal = services.Journal;
        var document = journal.Load().Document;
        foreach (var id in ids)
        {
            var entry = new JournalEntry
            {
                EntryId = Guid.NewGuid(),
                Kind = null,
                TweakId = id,
                Title = id,
                UserSid = services.User.Sid,
                Status = JournalEntryStatus.Active,
                AppliedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            };
            entry.Events.Add(new JournalEvent { At = entry.AppliedAt, Action = "apply", Outcome = "Started" });
            entry.Events.Add(new JournalEvent { At = entry.AppliedAt, Action = "apply", Outcome = nameof(TweakOutcome.Applied) });
            document.Entries.Add(entry);
        }

        journal.Save(document);
    }
}
