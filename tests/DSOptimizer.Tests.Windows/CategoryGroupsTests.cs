using System.Linq;
using System.Windows.Data;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Grupy tematyczne w listach: „Prywatność i AI” ma trzy grupy z licznikami w kolejności katalogu, inne kategorie zostają przy sekcjach
/// „Ustawienia”/„Akcje”, a wyniki wyszukiwania mają nagłówki „Kategoria · grupa”. Pozycja bez grupy cofa kategorię do sekcji.
/// </summary>
[Trait("Category", "Windows")]
public class CategoryGroupsTests
{
    [Fact]
    public void Privacy_ThreeGroupsInDefinedOrder()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            var privacy = Cat(vm, "privacy");

            Assert.Equal(CategoryListGrouping.CatalogGroups, privacy.Grouping);
            Assert.Equal(
                new[] { "Start, wyszukiwanie i Widżety", "Reklamy i sugestie", "Telemetria i diagnostyka" },
                privacy.Groups.Select(group => group.Title));
            Assert.Equal(
                new[] { "privacy.widgets", "privacy.search-web-results", "privacy.start-recommendations", "privacy.start-recommended-section" },
                privacy.Items.Take(4).Select(item => item.Id));
            Assert.Equal(
                privacy.Groups,
                privacy.ItemsView.Groups.Cast<CollectionViewGroup>().Select(group => group.Name));
        });
    }

    [Fact]
    public void GroupCounter_FollowsState()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            var start = Cat(vm, "privacy").Groups[0];
            Assert.Equal("0/4", start.CounterText);
            var notified = 0;
            start.PropertyChanged += (_, e) => notified += e.PropertyName == nameof(ListGroupViewModel.CounterText) ? 1 : 0;

            Item(vm, "privacy.widgets").SetState(DetectionState.Applied);

            Assert.Equal("1/4", start.CounterText);
            Assert.Equal("1 z 4 ustawień zastosowanych", start.CounterDescription);
            Assert.True(start.HasCounter);
            Assert.True(notified > 0, "Zmiana stanu pozycji ma odświeżyć licznik grupy (PropertyChanged CounterText).");
        });
    }

    [Fact]
    public void Perf_KeepsSections()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            var perf = Cat(vm, "perf");

            vm.SelectedPage = perf;

            Assert.Equal(CategoryListGrouping.Sections, perf.Grouping);
            Assert.Empty(perf.Groups);
            Assert.False(vm.IsListGrouped);

            vm.SelectedPage = Cat(vm, "privacy");
            Assert.True(vm.IsListGrouped);
        });
    }

    [Fact]
    public void ServicePage_GroupsByCategories()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            var service = vm.Categories.Single(c => c.IsService);

            Assert.Equal(CategoryListGrouping.Categories, service.Grouping);
            Assert.Empty(service.Groups);
        });
    }

    [Fact]
    public void SearchResults_HeaderIsCategoryAndGroup()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");

            vm.SearchText = "widzety";

            Assert.True(vm.IsListGrouped);
            Assert.Equal("Prywatność i AI · Start, wyszukiwanie i Widżety", ((CollectionViewGroup)vm.SearchResultsView.Groups[0]).Name);
            Assert.Equal("Wydajność", Item(vm, "perf.sysmain").ResultsHeader);
        });
    }

    [Fact]
    public void CategoryWithItemMissingGroup_FallsBackToSections()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            var withoutGroup = Item(vm, "privacy.widgets").Definition with { Group = null };
            var page = new NavCategoryPage("privacy", "x", new[] { new GuiCategory("privacy", "x", new[] { withoutGroup }) }, false);

            var category = new CategoryViewModel(page, vm);

            Assert.Equal(CategoryListGrouping.Sections, category.Grouping);
            Assert.Empty(category.Groups);
            Assert.Single(category.Items);
            Assert.Equal("Ustawienia", (string)((CollectionViewGroup)category.ItemsView.Groups[0]).Name);
        });
    }

    /// <summary>
    /// Nagłówek w stałym wierszu nad listą = pierwsza grupa widoku po filtrze. Wbudowane pozycje „start” są bezpieczne, więc kategoria testu ma je
    /// z ryzykiem „ostrożnie”: filtr „Tylko bezpieczne” chowa całą grupę i nagłówkiem staje się „Reklamy i sugestie”.
    /// </summary>
    [Fact]
    public void FirstVisibleGroup_FollowsFilter_NullWhenViewEmpty()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            var definitions = Cat(vm, "privacy").Items
                .Select(item => item.Definition.Group == "start" ? item.Definition with { Risk = RiskLevel.Caution } : item.Definition)
                .ToList();
            var category = new CategoryViewModel(new NavCategoryPage("privacy", "x", new[] { new GuiCategory("privacy", "x", definitions) }, false), vm);
            Assert.Equal("start", category.FirstVisibleGroup?.Key);
            var notified = 0;
            category.PropertyChanged += (_, e) => notified += e.PropertyName == nameof(CategoryViewModel.FirstVisibleGroup) ? 1 : 0;

            category.ApplyFilter(RiskFilter.SafeOnly, ShowFilter.All);
            Assert.Equal("Reklamy i sugestie", category.FirstVisibleGroup?.Title);
            Assert.Equal(1, notified);

            category.ApplyFilter(RiskFilter.All, ShowFilter.All);
            Assert.Equal("start", category.FirstVisibleGroup?.Key);
            Assert.Equal(2, notified);

            var onlyStart = new CategoryViewModel(
                new NavCategoryPage("privacy", "x", new[] { new GuiCategory("privacy", "x", definitions.Where(d => d.Group == "start").ToList()) }, false), vm);
            onlyStart.ApplyFilter(RiskFilter.SafeOnly, ShowFilter.All);
            Assert.True(onlyStart.ItemsView.IsEmpty);
            Assert.Null(onlyStart.FirstVisibleGroup);
        });
    }

    [Fact]
    public void FirstVisibleGroup_NullOutsideCatalogGroups()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);

            Assert.Null(Cat(vm, "perf").FirstVisibleGroup);
            Assert.Null(vm.Categories.Single(c => c.IsService).FirstVisibleGroup);
        });
    }

    private static CategoryViewModel Cat(MainViewModel vm, string id) => vm.Categories.Single(category => category.Id == id);

    private static ItemViewModel Item(MainViewModel vm, string id) => vm.Categories.SelectMany(category => category.Items).Single(item => item.Id == id);
}
