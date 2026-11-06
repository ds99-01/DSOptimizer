using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using DSOptimizer.App.ViewModels;
using DSOptimizer.App.Views;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Filtr „Pokaż” (stan pozycji) obok „Ryzyka” i widok „Zaznaczone” (zaznaczone ze wszystkich kategorii na mechanizmie wyników wyszukiwania).
/// Nic nie znika po cichu: start „Wszystkie”, ukryte liczy „Ukryte przez filtr”, a strona „Naprawy i narzędzia” bez listy „Pokaż” jej nie stosuje.
/// </summary>
[Trait("Category", "Windows")]
public class ShowFilterTests
{
    [Fact]
    public void ShowFilter_DefaultAll_FourOptionsInOrder()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);

            Assert.Equal(ShowFilter.All, vm.ShowFilter);
            Assert.False(vm.IsSelectedView);
            Assert.Equal(
                new[] { ShowFilter.All, ShowFilter.NotApplied, ShowFilter.Applied, ShowFilter.Selected },
                vm.ShowFilterOptions.Select(option => option.Value));
            Assert.Equal(
                new[] { "Wszystkie", "Niezastosowane", "Zastosowane", "Zaznaczone" },
                vm.ShowFilterOptions.Select(option => option.ToString()));
        });
    }

    [Fact]
    public void ShowNotApplied_HidesAppliedAndCountsHidden()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            var ux = Cat(vm, "ux");
            vm.SelectedPage = ux;
            vm.ShowFilter = ShowFilter.NotApplied;
            Assert.Null(vm.FilterNoteText);

            Item(vm, "ux.menu-delay").SetState(DetectionState.Applied);

            Assert.DoesNotContain(Item(vm, "ux.menu-delay"), Rows(ux.ItemsView));
            Assert.Equal(ux.Items.Count - 1, Rows(ux.ItemsView).Count);
            Assert.Equal("Ukryte przez filtr: 1", vm.FilterNoteText);

            vm.ShowFilter = ShowFilter.Applied;
            Assert.Equal(new[] { "ux.menu-delay" }, Rows(ux.ItemsView).Select(item => item.Id));
            Assert.Equal($"Ukryte przez filtr: {ux.Items.Count - 1}", vm.FilterNoteText);
        });
    }

    /// <summary>„Odśwież stan” (Sprawdzanie…) zostawia ostatni znany stan, więc filtr niczego na chwilę nie chowa ani nie pokazuje.</summary>
    [Fact]
    public void ShowNotApplied_RefreshKeepsRows()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            var ux = Cat(vm, "ux");
            vm.SelectedPage = ux;
            Item(vm, "ux.menu-delay").SetState(DetectionState.Applied);
            Item(vm, "ux.game-dvr-policy").SetState(DetectionState.NotApplied);
            vm.ShowFilter = ShowFilter.NotApplied;
            var before = Rows(ux.ItemsView).Select(item => item.Id).ToList();
            var resets = 0;
            ((INotifyCollectionChanged)ux.ItemsView).CollectionChanged += (_, _) => resets++;

            foreach (var item in vm.Categories.SelectMany(category => category.Items))
            {
                item.SetChecking();
            }

            Assert.Equal(before, Rows(ux.ItemsView).Select(item => item.Id));
            Assert.Equal(0, resets);
            Assert.Equal("Ukryte przez filtr: 1", vm.FilterNoteText);
        });
    }

    [Fact]
    public void ShowFilter_PersistsAcrossPages_ExceptSelected()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "ux");
            vm.ShowFilter = ShowFilter.NotApplied;

            vm.SelectedPage = Cat(vm, "perf");
            Assert.Equal(ShowFilter.NotApplied, vm.ShowFilter);

            vm.ShowFilter = ShowFilter.Selected;
            vm.SelectedPage = Cat(vm, "ux");
            Assert.Equal(ShowFilter.All, vm.ShowFilter);
            Assert.False(vm.IsSelectedView);
        });
    }

    [Fact]
    public void ServicePage_IgnoresShowFilter()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "ux");
            Assert.True(vm.IsShowFilterVisible);
            vm.ShowFilter = ShowFilter.Applied;

            var service = vm.Categories.Single(category => category.IsService);
            vm.SelectedPage = service;

            Assert.Equal(service.Items, Rows(service.ItemsView));
            Assert.False(vm.IsShowFilterVisible);
            Assert.Null(vm.FilterNoteText);
            Assert.Equal(ShowFilter.Applied, vm.ShowFilter);
        });
    }

    [Fact]
    public void Search_AppliesShowFilter_AndShowsCombo()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = vm.Categories.Single(category => category.IsService);
            vm.SearchText = "dns";
            Assert.True(vm.IsShowFilterVisible);
            var found = Rows(vm.SearchResultsView);
            Assert.Contains(Item(vm, "repair.dns-flush"), found);

            var hidden = found.Count(item => !ItemFilter.PassesShow(item.Definition, item.KnownState, ShowFilter.NotApplied));
            Assert.True(hidden > 0);

            vm.ShowFilter = ShowFilter.NotApplied;

            Assert.DoesNotContain(Item(vm, "repair.dns-flush"), Rows(vm.SearchResultsView));
            Assert.Equal(found.Count - hidden, Rows(vm.SearchResultsView).Count);
            Assert.Equal($"Ukryte przez filtr: {hidden}", vm.FilterNoteText);
            Assert.True(vm.IsShowFilterVisible);
        });
    }

    /// <summary>Widok „Zaznaczone” na stronie „Naprawy i narzędzia” bez frazy: wyniki stosują filtr „Pokaż”, więc lista „Pokaż” jest widoczna.</summary>
    [Fact]
    public void ServicePage_SelectedView_ShowsCombo()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = vm.Categories.Single(category => category.IsService);
            Item(vm, "ux.menu-delay").IsSelected = true;
            Assert.False(vm.IsShowFilterVisible);

            vm.ShowSelectedCommand.Execute(null);

            Assert.False(vm.IsSearching);
            Assert.True(vm.IsSelectedView);
            Assert.True(vm.IsShowFilterVisible);

            vm.EscapeCommand.Execute(null);
            Assert.False(vm.IsShowFilterVisible);
        });
    }

    [Fact]
    public void SelectedView_ShowsSelectedFromAllCategories()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");
            Item(vm, "privacy.widgets").IsSelected = true;
            Item(vm, "ux.menu-delay").IsSelected = true;

            vm.ShowFilter = ShowFilter.Selected;

            Assert.True(vm.IsSelectedView);
            Assert.True(vm.UsesResultsView);
            Assert.False(vm.IsSearching);
            Assert.True(vm.IsListGrouped);
            Assert.Equal(new[] { "privacy.widgets", "ux.menu-delay" }, Rows(vm.SearchResultsView).Select(item => item.Id));
            Assert.Equal(
                new[] { Item(vm, "privacy.widgets").ResultsHeader, Item(vm, "ux.menu-delay").ResultsHeader },
                vm.SearchResultsView.Groups.Cast<CollectionViewGroup>().Select(group => (string)group.Name));
            Assert.Equal("Zaznaczone", vm.PageTitle);
            Assert.Equal("2 pozycje w 2 kategoriach", vm.PageSubtitle);
            Assert.Equal("privacy.widgets", vm.DetailItem?.Id);
            Assert.Equal(string.Empty, vm.EmptyText);
            Assert.Same(Cat(vm, "perf"), vm.SelectedNavItem);
        });
    }

    [Fact]
    public void SelectedView_UnselectedRowStaysUntilLeaving()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");
            Item(vm, "privacy.widgets").IsSelected = true;
            Item(vm, "ux.menu-delay").IsSelected = true;
            vm.ShowFilter = ShowFilter.Selected;

            Item(vm, "ux.menu-delay").IsSelected = false;
            Assert.Equal(new[] { "privacy.widgets", "ux.menu-delay" }, Rows(vm.SearchResultsView).Select(item => item.Id));

            vm.ShowFilter = ShowFilter.All;
            vm.ShowFilter = ShowFilter.Selected;
            Assert.Equal(new[] { "privacy.widgets" }, Rows(vm.SearchResultsView).Select(item => item.Id));
        });
    }

    [Fact]
    public void SelectedView_NewlySelectedJoins()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");
            Item(vm, "ux.menu-delay").IsSelected = true;
            vm.ShowFilter = ShowFilter.Selected;

            Item(vm, "privacy.widgets").IsSelected = true;

            Assert.Equal(new[] { "privacy.widgets", "ux.menu-delay" }, Rows(vm.SearchResultsView).Select(item => item.Id));
        });
    }

    [Fact]
    public void SelectedView_ClearSelection_KeepsRows()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");
            Item(vm, "privacy.widgets").IsSelected = true;
            Item(vm, "ux.menu-delay").IsSelected = true;
            vm.ShowFilter = ShowFilter.Selected;

            vm.ClearSelectionCommand.Execute(null);

            var rows = Rows(vm.SearchResultsView);
            Assert.Equal(new[] { "privacy.widgets", "ux.menu-delay" }, rows.Select(item => item.Id));
            Assert.All(rows, item => Assert.False(item.IsSelected));
            Assert.True(vm.IsSelectedView);
        });
    }

    [Fact]
    public void SelectedView_SearchNarrows()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");
            Item(vm, "privacy.widgets").IsSelected = true;
            Item(vm, "ux.menu-delay").IsSelected = true;
            vm.ShowFilter = ShowFilter.Selected;

            vm.SearchText = "widzet";

            Assert.Equal(new[] { "privacy.widgets" }, Rows(vm.SearchResultsView).Select(item => item.Id));
            Assert.Equal("Zaznaczone", vm.PageTitle);
            Assert.Equal("1 pozycja w 1 kategorii", vm.PageSubtitle);
            Assert.True(vm.IsSelectedView);
        });
    }

    [Fact]
    public void SelectedView_NavClickReturnsToAll()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");
            Item(vm, "privacy.widgets").IsSelected = true;
            vm.ShowFilter = ShowFilter.Selected;
            vm.SearchText = "widzet";

            vm.ResetListViewCommand.Execute(null);

            Assert.Equal(ShowFilter.All, vm.ShowFilter);
            Assert.False(vm.IsSelectedView);
            Assert.False(vm.UsesResultsView);
            Assert.Equal(string.Empty, vm.SearchText);
            Assert.Equal("Wydajność", vm.PageTitle);
        });
    }

    [Fact]
    public void Escape_ClearsSearchThenLeavesSelectedView()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");
            Assert.False(vm.EscapeCommand.CanExecute(null));
            Item(vm, "privacy.widgets").IsSelected = true;
            vm.ShowFilter = ShowFilter.Selected;
            vm.SearchText = "widzet";

            vm.EscapeCommand.Execute(null);
            Assert.Equal(string.Empty, vm.SearchText);
            Assert.True(vm.IsSelectedView);
            Assert.True(vm.EscapeCommand.CanExecute(null));

            vm.EscapeCommand.Execute(null);
            Assert.False(vm.IsSelectedView);
            Assert.Equal(ShowFilter.All, vm.ShowFilter);
            Assert.False(vm.EscapeCommand.CanExecute(null));
        });
    }

    [Fact]
    public void SelectedView_Empty_ShowsEmptyText()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");

            vm.ShowFilter = ShowFilter.Selected;

            Assert.True(vm.SearchResultsView.IsEmpty);
            Assert.Equal("Nic nie jest zaznaczone.", vm.EmptyText);
            Assert.Null(vm.DetailItem);
            Assert.Null(vm.FilterNoteText);
        });
    }

    [Fact]
    public void ShowSelectedCommand_EntersView()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");
            Assert.False(vm.ShowSelectedCommand.CanExecute(null));
            var changed = 0;
            vm.ShowSelectedCommand.CanExecuteChanged += (_, _) => changed++;

            Item(vm, "ux.menu-delay").IsSelected = true;
            Assert.True(changed > 0);
            Assert.True(vm.ShowSelectedCommand.CanExecute(null));

            vm.ShowSelectedCommand.Execute(null);

            Assert.True(vm.IsSelectedView);
            Assert.Equal(ShowFilter.Selected, vm.ShowFilter);
            Assert.Equal(new[] { "ux.menu-delay" }, Rows(vm.SearchResultsView).Select(item => item.Id));
        });
    }

    [Fact]
    public void ShowAll_ResetsRiskAndState_KeepsSelectedView()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            vm.SelectedPage = Cat(vm, "perf");
            vm.RiskFilter = RiskFilter.SafeOnly;
            vm.ShowFilter = ShowFilter.Applied;
            var refreshes = 0;
            ((INotifyCollectionChanged)vm.SearchResultsView).CollectionChanged += (_, _) => refreshes++;

            vm.ShowAllCommand.Execute(null);
            Assert.Equal(RiskFilter.All, vm.RiskFilter);
            Assert.Equal(ShowFilter.All, vm.ShowFilter);
            Assert.Equal(1, refreshes);
            Assert.Equal(Cat(vm, "perf").Items, Rows(Cat(vm, "perf").ItemsView));

            Item(vm, "ux.menu-delay").IsSelected = true;
            vm.ShowFilter = ShowFilter.Selected;
            vm.RiskFilter = RiskFilter.SafeOnly;
            vm.ShowAllCommand.Execute(null);
            Assert.Equal(RiskFilter.All, vm.RiskFilter);
            Assert.Equal(ShowFilter.Selected, vm.ShowFilter);
            Assert.True(vm.IsSelectedView);
        });
    }

    /// <summary>
    /// Wariant P1 (b) w MainWindow.xaml: wiersz szukania to <see cref="SearchFilterRow"/> z wyszukiwarką i listami „Pokaż”, „Ryzyko” (każda z etykietą),
    /// w tej kolejności nazw dla czytnika ekranu i klawisza Tab; opcje list w kolejności z modelu okna.
    /// </summary>
    [Fact]
    public void FilterRow_UiaNamesAndOptionsInOrder()
    {
        var window = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "DSOptimizer.App", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var row = window.Descendants().Single(e => e.Name.LocalName == nameof(SearchFilterRow));
        var search = row.Elements().First();
        var combos = row.Descendants().Where(e => e.Name.LocalName == "ComboBox").ToList();

        Assert.Equal("SearchBox", (string?)search.Attribute(x + "Name"));
        Assert.Equal("1", (string?)search.Attribute("KeyboardNavigation.TabIndex"));
        Assert.Equal(
            new[] { "{x:Static gui:GuiText.ShowLabel}", "{l:T 'Ryzyko'}" },
            combos.Select(combo => (string?)combo.Attribute("AutomationProperties.Name")));
        Assert.Equal(new[] { "{Binding ShowFilterOptions}", "{Binding RiskFilterOptions}" }, combos.Select(combo => (string?)combo.Attribute("ItemsSource")));
        Assert.Equal(new[] { "{Binding ShowFilter}", "{Binding RiskFilter}" }, combos.Select(combo => (string?)combo.Attribute("SelectedValue")));
        Assert.Equal(new[] { "2", "3" }, combos.Select(combo => (string?)combo.Attribute("KeyboardNavigation.TabIndex")));
        Assert.All(combos, combo => Assert.Equal(
            "True",
            (string?)combo.Attributes().SingleOrDefault(a => a.Name.LocalName == nameof(ComboBoxSizing) + ".FitWidestOption")));
        var escape = search.Descendants().Single(e => e.Name.LocalName == "KeyBinding");
        Assert.Equal("Escape", (string?)escape.Attribute("Key"));
        Assert.Equal("{Binding EscapeCommand}", (string?)escape.Attribute("Command"));
        Assert.Equal(
            new[] { "{x:Static gui:GuiText.ShowLabel}", "{l:T 'Ryzyko'}" },
            row.Descendants().Where(e => e.Name.LocalName == "TextBlock").Select(label => (string?)label.Attribute("Text")));
        // Grupy list to osobne dzieci wiersza; „Pokaż” chowa się przez SearchFilterRow.IsShown (rezerwuje szerokość), nie przez Visibility.
        var groups = row.Elements().Skip(1).ToList();
        Assert.Equal(2, groups.Count);
        Assert.Equal(
            new[] { "{Binding IsShowFilterVisible}", null },
            groups.Select(group => (string?)group.Attributes().SingleOrDefault(a => a.Name.LocalName == nameof(SearchFilterRow) + ".IsShown")));
        Assert.All(groups, group => Assert.Null(group.Attribute("Visibility")));

        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            Assert.Equal(new[] { "Wszystkie", "Niezastosowane", "Zastosowane", "Zaznaczone" }, vm.ShowFilterOptions.Select(option => option.Name));
            Assert.Equal(new[] { "Wszystkie", "Bez eksperymentalnych", "Tylko bezpieczne" }, vm.RiskFilterOptions.Select(option => option.Name));
        });
    }

    /// <summary>
    /// Pierwszy nagłówek listy (SectionHeaderTemplate) zwija się tylko poza wynikami i poza stroną „Naprawy i narzędzia”: tam lista ma grupy z kategorii
    /// (<see cref="CategoryListGrouping.Categories"/>, tylko przy <see cref="CategoryViewModel.IsService"/>), których stały wiersz nie niesie, więc nagłówek
    /// „Naprawy” nigdy nie znika.
    /// </summary>
    [Fact]
    public void SectionHeader_CollapsesOnlyOutsideResultsAndServicePage()
    {
        var window = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "DSOptimizer.App", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var template = window.Descendants().Single(e => e.Name.LocalName == "DataTemplate" && (string?)e.Attribute(x + "Key") == "SectionHeaderTemplate");
        var trigger = template.Descendants().Single(e => e.Name.LocalName == "MultiDataTrigger");
        var conditions = trigger.Descendants().Where(e => e.Name.LocalName == "Condition")
            .Select(c => (Path: ((string?)c.Attribute("Binding"))?.Split(',')[0], Value: (string?)c.Attribute("Value")))
            .ToList();

        Assert.Equal(
            new (string?, string?)[]
            {
                ("{Binding (ItemsControl.AlternationIndex)", "0"),
                ("{Binding DataContext.IsSelectSuggestedVisible", "True"),
                ("{Binding DataContext.UsesResultsView", "False"),
                ("{Binding DataContext.SelectedCategory.IsService", "False"),
            },
            conditions.Select(c => (c.Path, c.Value)));
        Assert.Equal("Collapsed", (string?)trigger.Descendants().Single(e => e.Name.LocalName == "Setter").Attribute("Value"));

        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);
            Assert.All(vm.Categories, category => Assert.Equal(category.IsService, category.Grouping == CategoryListGrouping.Categories));
        });
    }

    /// <summary>Przy wyszukiwarce węższej niż <see cref="GuiLayout.SearchMinWidth"/> obok list obie listy schodzą do drugiego wiersza pod nią.</summary>
    [Fact]
    public void FilterRow_WrapsBelowSearchMinWidth()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            const double filters = 300;
            var search = new Border { Height = 30 };
            var lists = new Border { Width = filters, Height = 30 };
            var row = new SearchFilterRow();
            row.Children.Add(search);
            row.Children.Add(lists);
            var oneRow = GuiLayout.SearchMinWidth + GuiLayout.SearchFiltersGap + filters;

            Layout(row, oneRow);
            Assert.False(row.IsWrapped);
            Assert.Equal(GuiLayout.SearchMinWidth, search.ActualWidth, 3);
            Assert.Equal(30, row.DesiredSize.Height, 3);
            Assert.Equal(oneRow - filters, lists.TranslatePoint(new Point(0, 0), row).X, 3);

            Layout(row, oneRow - 1);
            Assert.True(row.IsWrapped);
            Assert.Equal(oneRow - 1, search.ActualWidth, 3);
            Assert.Equal(new Point(0, 30 + GuiLayout.SearchFiltersRowGap), lists.TranslatePoint(new Point(0, 0), row));
            Assert.Equal(30 + GuiLayout.SearchFiltersRowGap + 30, row.DesiredSize.Height, 3);

            Layout(row, oneRow + 200);
            Assert.False(row.IsWrapped);
            Assert.Equal(GuiLayout.SearchMinWidth + 200, search.ActualWidth, 3);
        });
    }

    /// <summary>
    /// Grupy list jako osobne dzieci wiersza: „Pokaż” ukryte (<see cref="SearchFilterRow.IsShownProperty"/> = false, strona „Naprawy i narzędzia”
    /// poza wynikami) rezerwuje szerokość w decyzji o drugim wierszu, więc przy stałej szerokości wiersza <see cref="SearchFilterRow.IsWrapped"/> i
    /// wysokość są takie same jak przy widocznym „Pokaż”. W rzędzie stoi wtedy samo „Ryzyko”, bez pustej luki (po prawej albo od lewej pod wyszukiwarką).
    /// </summary>
    [Fact]
    public void FilterRow_HiddenShowGroup_KeepsWrapDecision()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            const double showWidth = 200;
            const double riskWidth = 150;
            var search = new Border { Height = 30 };
            var show = new Border { Width = showWidth, Height = 30 };
            var risk = new Border { Width = riskWidth, Height = 30 };
            var row = new SearchFilterRow();
            row.Children.Add(search);
            row.Children.Add(show);
            row.Children.Add(risk);
            var oneRow = GuiLayout.SearchMinWidth + GuiLayout.SearchFiltersGap + showWidth + riskWidth;

            foreach (var width in new[] { oneRow - showWidth - 1, oneRow - showWidth, oneRow - 1, oneRow, oneRow + 100 })
            {
                SearchFilterRow.SetIsShown(show, true);
                Layout(row, width);
                var wrapped = row.IsWrapped;
                var height = row.DesiredSize.Height;
                Assert.Equal(width < oneRow, wrapped);
                Assert.Equal(Visibility.Visible, show.Visibility);

                SearchFilterRow.SetIsShown(show, false);
                Layout(row, width);

                Assert.Equal(wrapped, row.IsWrapped);
                Assert.Equal(height, row.DesiredSize.Height, 3);
                Assert.Equal(Visibility.Hidden, show.Visibility);
                var riskAt = risk.TranslatePoint(new Point(0, 0), row);
                if (wrapped)
                {
                    Assert.Equal(new Point(0, 30 + GuiLayout.SearchFiltersRowGap), riskAt);
                    Assert.Equal(width, search.ActualWidth, 3);
                }
                else
                {
                    Assert.Equal(width - riskWidth, riskAt.X, 3);
                    Assert.Equal(width - GuiLayout.SearchFiltersGap - riskWidth, search.ActualWidth, 3);
                }
            }
        });
    }

    /// <summary>
    /// Listy z prawdziwym stylem <c>BrandComboBox</c> i <see cref="ComboBoxSizing.FitWidestOption"/>: szerokość pola nie zależy od wybranej opcji, więc
    /// przy stałej szerokości wiersza wybór opcji nie przestawia układu ani nie zmienia szerokości wyszukiwarki. Bez flagi pole idzie za wybraną opcją
    /// (sprawdzenie, że test w ogóle widzi różnicę).
    /// </summary>
    [Fact]
    public void FilterRow_FixedComboWidths_SelectionDoesNotChangeLayout()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            var tokens = (ResourceDictionary)Application.LoadComponent(new System.Uri("/DSOptimizer;component/Themes/Tokens.xaml", System.UriKind.Relative));
            var showOptions = new[] { ShowFilter.All, ShowFilter.NotApplied, ShowFilter.Applied, ShowFilter.Selected }
                .Select(filter => new ShowFilterOption(filter, GuiText.ShowFilterName(filter))).ToList();
            var riskOptions = new[] { RiskFilter.All, RiskFilter.NoExperimental, RiskFilter.SafeOnly }
                .Select(filter => new RiskFilterOption(filter, GuiText.RiskFilterName(filter))).ToList();
            ComboBox Combo(System.Collections.IEnumerable items, bool fit)
            {
                var combo = new ComboBox { Style = (Style)tokens["BrandComboBox"], ItemsSource = items, SelectedIndex = 0 };
                ComboBoxSizing.SetFitWidestOption(combo, fit);
                return combo;
            }

            var show = Combo(showOptions, true);
            var risk = Combo(riskOptions, true);
            var lists = new StackPanel { Orientation = Orientation.Horizontal };
            lists.Children.Add(show);
            lists.Children.Add(risk);
            var search = new Border { Height = 30 };
            var row = new SearchFilterRow();
            row.Children.Add(search);
            row.Children.Add(lists);
            Layout(row, 2000);
            var showWidth = show.ActualWidth;
            var riskWidth = risk.ActualWidth;
            var width = GuiLayout.SearchMinWidth + GuiLayout.SearchFiltersGap + lists.ActualWidth + 0.5;

            for (var s = 0; s < showOptions.Count; s++)
            {
                for (var r = 0; r < riskOptions.Count; r++)
                {
                    show.SelectedIndex = s;
                    risk.SelectedIndex = r;
                    Layout(row, width);
                    Assert.Equal(showWidth, show.ActualWidth, 3);
                    Assert.Equal(riskWidth, risk.ActualWidth, 3);
                    Assert.False(row.IsWrapped);
                    Assert.Equal(GuiLayout.SearchMinWidth + 0.5, search.ActualWidth, 3);
                }
            }

            var plain = Combo(riskOptions, false);
            Layout(plain, 2000);
            var first = plain.DesiredSize.Width;
            plain.SelectedIndex = 1;
            Layout(plain, 2000);
            Assert.True(plain.DesiredSize.Width > first + 1, $"Bez flagi pole ma iść za wybraną opcją ({first:0.#} → {plain.DesiredSize.Width:0.#}).");
            Assert.True(riskWidth >= plain.DesiredSize.Width - 0.01, "Stała szerokość mieści najdłuższą opcję „Ryzyka”.");
        });
    }

    private static void Layout(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
        element.UpdateLayout();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(System.AppDomain.CurrentDomain.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DSOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static System.Collections.Generic.List<ItemViewModel> Rows(System.ComponentModel.ICollectionView view) => view.Cast<ItemViewModel>().ToList();

    private static CategoryViewModel Cat(MainViewModel vm, string id) => vm.Categories.Single(category => category.Id == id);

    private static ItemViewModel Item(MainViewModel vm, string id) => vm.Categories.SelectMany(category => category.Items).Single(item => item.Id == id);
}
