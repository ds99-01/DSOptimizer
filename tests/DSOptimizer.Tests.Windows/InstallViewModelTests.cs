using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Tests.Windows.Support;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Model strony „Instalacja” (plan RustDesk, Task 5): lista także przy niedziałającym winget (komunikat nad listą, decyzja planu 1),
/// RustDesk z GitHuba do zaznaczenia, „Wszystkie (N)” (D1). Bez okna WPF i bez winget: sesja fałszywa (<see cref="FakeSession"/>),
/// seria to delegat jak w <see cref="SeriesWindowViewModelInstallTests"/>.
/// </summary>
[Trait("Category", "Windows")]
public class InstallViewModelTests
{
    private const string NotWorkingText = "winget nie działa (winget --version: kod 0x00000001). Użyj „Naprawy → Napraw winget”.";

    private static readonly AppsInstallCatalog Catalog = AppsInstallCatalog.Load("""
        { "entries": [
          { "id": "install.firefox", "wingetId": "Mozilla.Firefox", "title": "Mozilla Firefox", "summary": "Przeglądarka.",
            "category": "browsers", "match": { "displayName": "Mozilla Firefox*" }, "source": "https://example.com/firefox" },
          { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiwizator.",
            "category": "tools", "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" },
          { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Zdalny pulpit.",
            "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
            "source": "https://github.com/rustdesk/rustdesk/releases" },
          { "id": "install.anydesk", "wingetId": "AnyDesk.AnyDesk", "title": "AnyDesk", "summary": "Zdalny pulpit.",
            "category": "remote-help", "match": { "displayName": "AnyDesk" }, "source": "https://example.com/anydesk" }
        ] }
        """);

    /// <summary>Lista z Discordem (wynik winget będący pozycją listy, spec 3) – osobno, żeby liczniki pozostałych testów zostały.</summary>
    private static readonly AppsInstallCatalog DiscordCatalog = AppsInstallCatalog.Load("""
        { "entries": [
          { "id": "install.discord", "wingetId": "Discord.Discord", "title": "Discord", "summary": "Komunikator tekstowy i głosowy.",
            "category": "messengers", "match": { "displayName": "Discord" }, "source": "https://example.com/discord" },
          { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiwizator.",
            "category": "tools", "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" },
          { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Zdalny pulpit.",
            "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
            "source": "https://github.com/rustdesk/rustdesk/releases" }
        ] }
        """);

    private static readonly WingetStatus Ready = new(WingetState.Ready, "v1.11.510", "winget działa (wersja v1.11.510).");

    private sealed class FakeSession : IInstallPageSession
    {
        public FakeSession(InstallPageState state, AppsInstallCatalog catalog)
        {
            State = state;
            Catalog = catalog;
        }

        public InstallPageState State { get; set; }

        public List<string> Searches { get; } = new();

        public AppsInstallCatalog Catalog { get; }

        public InstallPageState Load() => State;

        /// <summary>Wynik wyszukiwania; domyślnie „nic nie znaleziono”.</summary>
        public InstallSearchResult SearchResult { get; set; } = new(Array.Empty<InstallRow>(), InstallText.SearchNoResults, Array.Empty<string>());

        /// <summary>Gdy ustawiona, <see cref="Search"/> czeka na nią (najwyżej 10 s) – wyszukiwanie kończy się po zdarzeniu z testu.</summary>
        public ManualResetEventSlim? SearchGate { get; set; }

        public InstallSearchResult Search(string query)
        {
            Searches.Add(query);
            SearchGate?.Wait(TimeSpan.FromSeconds(10));
            return SearchResult;
        }
    }

    private sealed class Harness
    {
        public Harness(InstallPageState state, AppsInstallCatalog? catalog = null)
        {
            Session = new FakeSession(state, catalog ?? Catalog);
            ViewModel = new InstallViewModel(
                Session,
                targets =>
                {
                    Installed.Add(targets);
                    return Task.CompletedTask;
                },
                () => Repairs++,
                (level, text) => Log.Add((level, text)),
                new FixedClock());
        }

        public FakeSession Session { get; }

        public InstallViewModel ViewModel { get; }

        public List<IReadOnlyList<InstallTarget>> Installed { get; } = new();

        public int Repairs { get; private set; }

        public List<(GuiLogLevel Level, string Text)> Log { get; } = new();

        public InstallRowViewModel Row(string id) => ViewModel.VisibleRows.Single(row => row.Target.Id == id);
    }

    private static InstallPageState State(WingetStatus winget, bool listRead = true, IReadOnlyList<InstalledApp>? raw = null, AppsInstallCatalog? catalog = null)
        => new(InstallPage.CatalogRows(catalog ?? Catalog, raw ?? Array.Empty<InstalledApp>(), winget, listRead), winget, Array.Empty<string>())
        {
            ListReadFailed = !listRead,
        };

    private static async Task<Harness> Loaded(InstallPageState state, AppsInstallCatalog? catalog = null)
    {
        var harness = new Harness(state, catalog);
        await harness.ViewModel.EnterAsync();
        return harness;
    }

    /// <summary>Teksty linii listy (nagłówki sekcji i kategorii, notatki, komunikaty) w kolejności widoku.</summary>
    private static string[] Lines(InstallViewModel vm)
        => vm.VisibleItems.OfType<InstallListLine>().Select(line => line.Text).ToArray();

    private static InstallSearchResult WingetResult(IReadOnlyList<WingetSearchRow> found, AppsInstallCatalog catalog)
    {
        var rows = InstallPage.SearchRows(found, catalog, Ready);
        var onList = InstallPage.CatalogIdsIn(found, catalog);
        return new InstallSearchResult(rows, rows.Count == 0 && onList.Count > 0 ? InstallText.SearchAllOnList : null, onList);
    }

    private static readonly WingetSearchRow[] DiscordFound =
    {
        new("Discord", "Discord.Discord", "1.0.9", true),
        new("Discord PTB", "Discord.Discord.PTB", "1.0.1", true),
        new("Discord Canary", "Discord.Discord.Canary", "1.0.5", true),
    };

    [Fact]
    public async Task WingetNotWorking_ListVisible_NoticeAboveList_RepairButton()
    {
        var h = await Loaded(State(new WingetStatus(WingetState.NotWorking, null, NotWorkingText)));
        var vm = h.ViewModel;

        Assert.True(vm.HasList);
        Assert.False(vm.ShowsProblem);
        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.True(vm.HasWingetNotice);
        Assert.Equal(NotWorkingText, vm.WingetNotice);
        Assert.Equal("Winget nie działa na tym komputerze", vm.WingetNoticeTitle);
        Assert.Equal("RustDesk (z GitHuba) można zainstalować także bez winget.", vm.GitHubStillAvailable);
        Assert.True(vm.HasGitHubStillAvailable);
        Assert.True(vm.ShowRepairWinget);
        Assert.Equal("Źródła: GitHub (RustDesk) · winget (Microsoft) niedostępny · 4 aplikacje · zainstalowane: 0", vm.Subtitle);
        Assert.False(vm.HasAccountNotice);

        vm.RepairWingetCommand.Execute(null);
        Assert.Equal(1, h.Repairs);

        // Wyszukiwanie w winget nieaktywne, gdy winget nie działa.
        vm.SearchText = "vlc";
        Assert.False(vm.SearchCommand.CanExecute(null));
    }

    [Fact]
    public async Task WingetNotWorking_RustDeskSelected_CanInstall()
    {
        var h = await Loaded(State(new WingetStatus(WingetState.NotRegistered, null, InstallText.NotRegistered)));
        var vm = h.ViewModel;

        Assert.False(h.Row("install.7zip").IsSelectable);
        Assert.False(h.Row("install.anydesk").IsSelectable);
        var rustDesk = h.Row("install.rustdesk");
        Assert.True(rustDesk.IsSelectable);
        Assert.False(vm.CanInstall);

        rustDesk.IsSelected = true;

        Assert.Equal(1, vm.SelectedCount);
        Assert.True(vm.CanInstall);
        Assert.Equal("Zainstaluj (1)", vm.InstallText);
        Assert.Equal("Z 1 kategorii, pobierane z GitHuba RustDesk. Wymaga internetu.", vm.SelectionBarDetails);
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Equal("install.rustdesk", Assert.Single(Assert.Single(h.Installed)).Id);
    }

    [Fact]
    public async Task WingetReady_MixedSelection_DetailsBothSources_NoNotice()
    {
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;

        h.Row("install.rustdesk").IsSelected = true;
        h.Row("install.7zip").IsSelected = true;

        Assert.False(vm.HasWingetNotice);
        Assert.False(vm.ShowRepairWinget);
        Assert.Null(vm.GitHubStillAvailable);
        Assert.True(vm.CanInstall);
        Assert.Equal("Źródła: winget (Microsoft) i GitHub (RustDesk) · 4 aplikacje · zainstalowane: 0", vm.Subtitle);
        Assert.Equal("Z 2 kategorii, pobierane z winget (Microsoft) i z GitHuba RustDesk. Wymaga internetu.", vm.SelectionBarDetails);
    }

    [Fact]
    public async Task ListReadFailed_ProblemInsteadOfList()
    {
        var failed = new WingetStatus(WingetState.NotWorking, null, InstallText.ReadFailed("brak dostępu"));
        var h = await Loaded(State(failed, listRead: false));
        var vm = h.ViewModel;

        Assert.True(vm.ShowsProblem);
        Assert.False(vm.HasList);
        Assert.Equal(InstallText.ReadFailed("brak dostępu"), vm.WingetMessage);
        Assert.False(vm.HasWingetNotice);
        Assert.False(vm.ShowRepairWinget);
        Assert.Equal("Źródło: winget (Microsoft) · niedostępne", vm.Subtitle);
        Assert.All(vm.VisibleRows, row => Assert.False(row.IsSelectable));
        Assert.False(vm.CanInstall);
    }

    [Theory]
    [InlineData(WingetState.OtherAccount)]
    [InlineData(WingetState.UserUncertain)]
    public async Task OtherAccount_CannotInstall(WingetState state)
    {
        var refusal = new WingetStatus(state, null, InstallText.OtherAccount("dso"));
        var h = await Loaded(State(refusal));
        var vm = h.ViewModel;

        // Pole wyboru i tak nie przyjmie zaznaczenia (reguła Core), a „Zainstaluj” nie rusza przy odmowie konta.
        h.Row("install.rustdesk").IsSelected = true;

        Assert.True(vm.HasAccountNotice);
        Assert.False(vm.HasWingetNotice);
        Assert.True(vm.HasList);
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.CanInstall);
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task OtherAccountAfterSelection_CannotInstall()
    {
        // Zaznaczenie z odczytu przy działającym koncie, potem odczyt z odmową konta: zaznaczenie znika, „Zainstaluj” nieaktywne.
        var h = await Loaded(State(Ready));
        h.Row("install.rustdesk").IsSelected = true;
        Assert.True(h.ViewModel.CanInstall);

        h.Session.State = State(new WingetStatus(WingetState.OtherAccount, null, InstallText.OtherAccount("dso")));
        await h.ViewModel.RefreshAsync();

        Assert.False(h.ViewModel.CanInstall);
    }

    [Fact]
    public async Task GitHubRow_SourceLabel()
    {
        var h = await Loaded(State(Ready));

        var rustDesk = h.Row("install.rustdesk");
        Assert.Equal("GitHub: rustdesk/rustdesk", rustDesk.SourceLabel);
        Assert.True(rustDesk.HasSourceLabel);
        var zip = h.Row("install.7zip");
        Assert.Equal("7zip.7zip", zip.SourceLabel);
        Assert.True(zip.HasSourceLabel);
    }

    [Fact]
    public async Task All_DefaultOnEveryEntry_GroupedRows_SelectionIndependentOfCategory()
    {
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;

        var all = vm.Categories[0];
        Assert.Equal("Wszystkie", all.Name);
        Assert.Equal("4", all.CountText);
        Assert.Same(all, vm.SelectedGroup);
        Assert.Same(all, vm.SelectedCategory);
        Assert.True(vm.ShowsCategoryHeaders);
        Assert.Equal(
            new[] { "install.firefox", "install.7zip", "install.rustdesk", "install.anydesk" },
            vm.VisibleRows.Select(row => row.Target.Id));
        Assert.Equal(
            new[] { "Przeglądarki", "Narzędzia", "Zdalna pomoc", "Zdalna pomoc" },
            vm.VisibleRows.Select(row => row.CategoryHeader));
        Assert.Equal(
            new[] { "Wszystkie", "Zaznaczone", "Przeglądarki", "Narzędzia", "Zdalna pomoc" },
            vm.Categories.Select(option => option.Name));

        h.Row("install.firefox").IsSelected = true;
        var remote = vm.Categories.Single(option => option.Name == "Zdalna pomoc");
        vm.SelectedCategory = remote;

        Assert.False(vm.ShowsCategoryHeaders);
        Assert.Equal(new[] { "install.rustdesk", "install.anydesk" }, vm.VisibleRows.Select(row => row.Target.Id));
        h.Row("install.rustdesk").IsSelected = true;
        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal("Zainstaluj (2)", vm.InstallText);

        // Ponowne wejście na stronę: znowu „Wszystkie”, zaznaczenie zostaje.
        await vm.EnterAsync();

        Assert.Same(all, vm.SelectedGroup);
        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.Equal(2, vm.SelectedCount);
    }

    [Fact]
    public async Task SearchResults_NotPartOfAll()
    {
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(new[] { new WingetSearchRow("Notepad++", "Notepad++.Notepad++", "8.7", true) }, Catalog);
        vm.SearchText = "notepad";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "notepad" }, h.Session.Searches);
        Assert.True(vm.IsSearchView);
        Assert.Null(vm.SelectedCategory);
        Assert.Equal("winget:Notepad++.Notepad++", Assert.Single(vm.WingetResults).Target.Id);

        // Ruling R3: kliknięcie „Wszystkie” w trakcie wyszukiwania czyści pole i pokazuje całą listę, bez wyników winget.
        vm.SelectedCategory = vm.Categories[0];

        Assert.False(vm.IsSearchView);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Same(vm.AllGroup, vm.SelectedCategory);
        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.All(vm.VisibleRows, row => Assert.NotNull(row.Target.Category));
        Assert.Empty(vm.WingetResults);
    }

    [Fact]
    public async Task CategoryClickWhileFiltering_ClearsSearchText_ShowsCategory_KeepsSelectedWingetResult()
    {
        // Ruling R3: inna grupa niż „Zaznaczone” przy niepustym polu = przeglądanie – pole czyszczone, nigdy tekst, który nie filtruje.
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(new[] { new WingetSearchRow("Notepad++", "Notepad++.Notepad++", "8.7", true) }, Catalog);
        vm.SearchText = "notepad";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Single(vm.WingetResults).IsSelected = true;
        var remote = vm.Categories.Single(option => option.Name == "Zdalna pomoc");

        vm.SelectedCategory = remote;

        Assert.Equal(string.Empty, vm.SearchText);
        Assert.False(vm.IsSearchView);
        Assert.Same(remote, vm.SelectedCategory);
        Assert.Same(remote, vm.SelectedGroup);
        Assert.Equal(new[] { "install.rustdesk", "install.anydesk" }, vm.VisibleRows.Select(row => row.Target.Id));
        Assert.Equal(1, vm.SelectedCount); // zaznaczony wynik winget zostaje w serii (spec 11.5)

        // Klik tej samej (już wybranej) kategorii przy niepustym polu też czyści pole.
        vm.SearchText = "7zip";
        Assert.Null(vm.SelectedCategory);
        vm.SelectedCategory = remote;
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal(2, vm.VisibleRows.Count);
    }

    // --- Poprawki po przeklikaniu (2026-10-25): filtr listy na żywo, sekcje, „Zaznaczone (N)” ---

    [Fact]
    public async Task Filter_7zip_OnlyCatalogMatch_NoWinget()
    {
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;

        vm.SearchText = "7zip";

        Assert.True(vm.IsSearchView);
        Assert.True(vm.ShowsCategoryHeaders);
        Assert.Null(vm.SelectedCategory);
        Assert.Equal("install.7zip", Assert.Single(vm.VisibleRows).Target.Id);
        Assert.Equal(new[] { "Z listy (1)", "Narzędzia" }, Lines(vm));
        Assert.Empty(vm.WingetResults);
        Assert.Empty(h.Session.Searches);
        Assert.Equal(string.Empty, vm.EmptyText);
        Assert.True(vm.SearchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Filter_SeparatorsOnly_ShowsSelectedGroup()
    {
        // Review Focus 1: zapytanie z samych separatorów = brak zapytania, widok „Wszystkie”, nie „Brak na liście”.
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;

        vm.SearchText = " - ";

        Assert.False(vm.IsSearchView);
        Assert.Same(vm.AllGroup, vm.SelectedCategory);
        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.Equal(string.Empty, vm.EmptyText);
        Assert.Equal(new[] { "Przeglądarki", "Narzędzia", "Zdalna pomoc" }, Lines(vm));
        Assert.False(vm.SearchCommand.CanExecute(null));

        foreach (var separators in new[] { "...", "_", " . _ - " })
        {
            vm.SearchText = separators;
            Assert.False(vm.SearchCommand.CanExecute(null));
            await vm.SearchCommand.ExecuteAsync(null);
        }

        Assert.Empty(h.Session.Searches);
    }

    [Fact]
    public async Task Filter_ClearedText_BackToSelectedCategory()
    {
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;
        var remote = vm.Categories.Single(option => option.Name == "Zdalna pomoc");
        vm.SelectedCategory = remote;

        vm.SearchText = "7zip";
        Assert.Equal("install.7zip", Assert.Single(vm.VisibleRows).Target.Id);

        vm.SearchText = string.Empty;

        Assert.Same(remote, vm.SelectedCategory);
        Assert.Equal(new[] { "install.rustdesk", "install.anydesk" }, vm.VisibleRows.Select(row => row.Target.Id));
    }

    [Fact]
    public async Task WingetNotWorking_FilterRust_RustDeskSelectable_NoWingetSearch()
    {
        // Review Focus 4: winget nie działa – filtr działa, RustDesk do zaznaczenia, Enter/przycisk nieaktywne.
        var h = await Loaded(State(new WingetStatus(WingetState.NotWorking, null, NotWorkingText)));
        var vm = h.ViewModel;

        vm.SearchText = "rust";

        var rustDesk = Assert.Single(vm.VisibleRows);
        Assert.Equal("install.rustdesk", rustDesk.Target.Id);
        Assert.True(rustDesk.IsSelectable);
        rustDesk.IsSelected = true;
        Assert.Equal(1, vm.SelectedCount);
        Assert.False(vm.SearchCommand.CanExecute(null));
        Assert.Equal(InstallText.SearchPlaceholderListOnly, vm.SearchPlaceholder);

        vm.SearchText = "qqq";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Empty(vm.VisibleItems);
        Assert.Equal(InstallText.NoCatalogMatchWingetDown, vm.EmptyText);
        Assert.Empty(h.Session.Searches);
    }

    [Fact]
    public async Task Filter_NoMatch_NoCatalogMatchMessage()
    {
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;

        vm.SearchText = "qqq";

        Assert.Empty(vm.VisibleItems);
        Assert.Empty(vm.VisibleRows);
        Assert.Equal(InstallText.NoCatalogMatch, vm.EmptyText);
    }

    [Fact]
    public async Task WingetSearch_Discord_SectionsWithOnListNote()
    {
        var h = await Loaded(State(Ready, catalog: DiscordCatalog), DiscordCatalog);
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(DiscordFound, DiscordCatalog);
        vm.SearchText = "discord";
        Assert.Equal(new[] { "Z listy (1)", "Komunikatory" }, Lines(vm));

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "discord" }, h.Session.Searches);
        Assert.Equal(
            new[] { "install.discord", "winget:Discord.Discord.PTB", "winget:Discord.Discord.Canary" },
            vm.VisibleRows.Select(row => row.Target.Id));
        Assert.Equal(
            new[] { "Z listy (1)", "Komunikatory", "W winget (2)", InstallText.WingetSectionNote, "Discord jest na liście – wyżej" },
            Lines(vm));
        Assert.Equal(new[] { "winget:Discord.Discord.PTB", "winget:Discord.Discord.Canary" }, vm.WingetResults.Select(row => row.Target.Id));
        Assert.All(vm.WingetResults, row => Assert.True(row.IsSelectable));
    }

    [Fact]
    public async Task WingetSearch_OnListItemNotMatchedByFilter_AddedToCatalogSection()
    {
        // Pozycja listy trafiona przez winget pojawia się w sekcji „Z listy”, choć filtr jej nie złapał (spec 3).
        var h = await Loaded(State(Ready, catalog: DiscordCatalog), DiscordCatalog);
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(new[] { new WingetSearchRow("Discord", "Discord.Discord", "1.0.9", true) }, DiscordCatalog);
        vm.SearchText = "dc";
        Assert.Empty(vm.VisibleRows);

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("install.discord", Assert.Single(vm.VisibleRows).Target.Id);
        Assert.Equal(new[] { "Z listy (1)", "Komunikatory", "W winget (0)", "Discord jest na liście – wyżej" }, Lines(vm));
    }

    [Fact]
    public async Task WingetSearch_RustDeskAlias_GitHubNote()
    {
        var h = await Loaded(State(Ready, catalog: DiscordCatalog), DiscordCatalog);
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(new[] { new WingetSearchRow("RustDesk", "RustDesk.RustDesk", "1.4.2", true) }, DiscordCatalog);
        vm.SearchText = "rustdesk";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("install.rustdesk", Assert.Single(vm.VisibleRows).Target.Id);
        Assert.Contains("RustDesk jest na liście – wyżej (instalowany z GitHuba)", Lines(vm));
        Assert.Empty(vm.WingetResults);
    }

    [Fact]
    public async Task WingetSearch_NoNetwork_MessageInWingetSection_FilterStillWorks()
    {
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;
        h.Session.SearchResult = new InstallSearchResult(Array.Empty<InstallRow>(), InstallText.SearchNoNetwork, Array.Empty<string>());
        vm.SearchText = "7zip";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("install.7zip", Assert.Single(vm.VisibleRows).Target.Id);
        Assert.Equal(new[] { "Z listy (1)", "Narzędzia", "W winget (0)", InstallText.SearchNoNetwork }, Lines(vm));
        var message = vm.VisibleItems.OfType<InstallListLine>().Last();
        Assert.Equal(InstallListLineKind.Message, message.Kind);
        Assert.True(message.IsWarning);
    }

    [Fact]
    public async Task WingetSearch_TextChangedWhileSearching_ResultDiscarded()
    {
        // Review Focus 2: wynik starego zapytania nie trafia do sekcji „W winget”.
        var h = await Loaded(State(Ready, catalog: DiscordCatalog), DiscordCatalog);
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(DiscordFound, DiscordCatalog);
        using var gate = new ManualResetEventSlim(false);
        h.Session.SearchGate = gate;
        vm.SearchText = "discord";
        var search = vm.SearchCommand.ExecuteAsync(null);
        Assert.Contains(InstallText.Searching, Lines(vm));

        vm.SearchText = "discord ptb";
        Assert.DoesNotContain(InstallText.Searching, Lines(vm));
        gate.Set();
        await search;

        Assert.False(vm.IsSearching);
        Assert.Empty(vm.WingetResults);
        Assert.Null(vm.SearchMessage);
        Assert.DoesNotContain(Lines(vm), line => line.StartsWith("W winget", StringComparison.Ordinal));
        Assert.True(vm.SearchCommand.CanExecute(null));
    }

    [Fact]
    public async Task WingetSearch_TextChanged_ClearsWingetSection_SelectedResultHeld()
    {
        // Review Focus 3 + spec 11.5: zaznaczony wynik winget po zmianie i wyczyszczeniu pola dalej w „Zaznaczone (N)” i w serii.
        var h = await Loaded(State(Ready, catalog: DiscordCatalog), DiscordCatalog);
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(DiscordFound, DiscordCatalog);
        vm.SearchText = "discord";
        await vm.SearchCommand.ExecuteAsync(null);
        vm.WingetResults.Single(row => row.Title == "Discord PTB").IsSelected = true;
        Assert.Equal(1, vm.SelectedCount);

        vm.SearchText = "discor";
        Assert.Empty(vm.WingetResults);
        Assert.DoesNotContain(Lines(vm), line => line.StartsWith("W winget", StringComparison.Ordinal));

        vm.SearchText = string.Empty;

        Assert.Equal(1, vm.SelectedCount);
        Assert.Equal("1", vm.SelectedItemsGroup.CountText);
        vm.SelectedGroup = vm.SelectedItemsGroup;
        var held = Assert.Single(vm.VisibleRows);
        Assert.Equal("winget:Discord.Discord.PTB", held.Target.Id);
        Assert.Equal(new[] { "W winget (1)" }, Lines(vm));
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "winget:Discord.Discord.PTB" }, Assert.Single(h.Installed).Select(target => target.Id));
    }

    [Fact]
    public async Task SelectedGroup_SecondCategory_CountTracksSelection_ShowsAllSelected()
    {
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;

        var selected = vm.Categories[1];
        Assert.Same(InstallGroup.Selected, selected.Group);
        Assert.Same(selected, vm.SelectedItemsGroup);
        Assert.Equal("Zaznaczone", selected.Name);
        Assert.Equal("0", selected.CountText);

        vm.SelectedGroup = selected;
        Assert.Empty(vm.VisibleItems);
        Assert.Equal(InstallText.SelectedEmpty, vm.EmptyText);

        vm.SelectedGroup = vm.AllGroup;
        h.Row("install.anydesk").IsSelected = true;
        h.Row("install.firefox").IsSelected = true;
        Assert.Equal("2", selected.CountText);

        vm.SelectedGroup = selected;
        Assert.True(vm.ShowsCategoryHeaders);
        Assert.Equal(new[] { "install.firefox", "install.anydesk" }, vm.VisibleRows.Select(row => row.Target.Id));
        Assert.Equal(new[] { "Przeglądarki", "Zdalna pomoc" }, Lines(vm));
        Assert.Equal(string.Empty, vm.EmptyText);

        // Odznaczenie w „Zaznaczone” działa (licznik od razu, wiersz zostaje do ponownego wejścia – można się rozmyślić).
        h.Row("install.firefox").IsSelected = false;
        Assert.Equal("1", selected.CountText);
        Assert.Equal(1, vm.SelectedCount);
        Assert.Equal(2, vm.VisibleRows.Count);

        // „Odznacz wszystko” w „Zaznaczone”: lista od razu pusta.
        vm.ClearSelectionCommand.Execute(null);
        Assert.Empty(vm.VisibleItems);
        Assert.Equal(InstallText.SelectedEmpty, vm.EmptyText);
        Assert.Equal("0", selected.CountText);
    }

    /// <summary>Fala poprawek F9: w „Zaznaczone” liczniki nagłówków idą za odznaczeniem, a ponowny klik linku paska odświeża widok.</summary>
    [Fact]
    public async Task SelectedGroup_UncheckRow_HeaderCountsUpdate_LinkAgainRefreshesView()
    {
        var h = await Loaded(State(Ready, catalog: DiscordCatalog), DiscordCatalog);
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(DiscordFound, DiscordCatalog);
        vm.SearchText = "discord";
        await vm.SearchCommand.ExecuteAsync(null);
        vm.WingetResults.Single(row => row.Title == "Discord PTB").IsSelected = true;
        vm.WingetResults.Single(row => row.Title == "Discord Canary").IsSelected = true;
        vm.SearchText = string.Empty;
        h.Row("install.7zip").IsSelected = true;
        h.Row("install.rustdesk").IsSelected = true;
        vm.ShowSelectedCommand.Execute(null);
        Assert.Equal(new[] { "Narzędzia", "Zdalna pomoc", "W winget (2)" }, Lines(vm));
        Assert.Equal(new[] { "1", "1", string.Empty }, vm.VisibleItems.OfType<InstallListLine>().Select(line => line.CountText));

        h.Row("install.7zip").IsSelected = false;
        h.Row("winget:Discord.Discord.Canary").IsSelected = false;

        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.Equal(new[] { "Narzędzia", "Zdalna pomoc", "W winget (1)" }, Lines(vm));
        Assert.Equal(new[] { "0", "1", string.Empty }, vm.VisibleItems.OfType<InstallListLine>().Select(line => line.CountText));

        vm.ShowSelectedCommand.Execute(null);

        Assert.Same(vm.SelectedItemsGroup, vm.SelectedGroup);
        Assert.Equal(new[] { "install.rustdesk", "winget:Discord.Discord.PTB" }, vm.VisibleRows.Select(row => row.Target.Id));
        Assert.Equal(new[] { "Zdalna pomoc", "W winget (1)" }, Lines(vm));
    }

    /// <summary>Fala poprawek F10: wynik winget, który przyszedł przy wybranym „Zaznaczone” (tekst bez zmian), od razu widać.</summary>
    [Fact]
    public async Task WingetResultArrivesWhileSelectedGroupShown_SwitchesToSearchView()
    {
        var h = await Loaded(State(Ready, catalog: DiscordCatalog), DiscordCatalog);
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(DiscordFound, DiscordCatalog);
        h.Row("install.7zip").IsSelected = true;
        using var gate = new ManualResetEventSlim(false);
        h.Session.SearchGate = gate;
        vm.SearchText = "discord";
        var search = vm.SearchCommand.ExecuteAsync(null);

        vm.ShowSelectedCommand.Execute(null);
        Assert.False(vm.IsSearchView);
        gate.Set();
        await search;

        Assert.True(vm.IsSearchView);
        Assert.Null(vm.SelectedCategory);
        Assert.Equal("discord", vm.SearchText);
        Assert.Equal(
            new[] { "install.discord", "winget:Discord.Discord.PTB", "winget:Discord.Discord.Canary" },
            vm.VisibleRows.Select(row => row.Target.Id));
        Assert.Contains("W winget (2)", Lines(vm));
    }

    /// <summary>Fala poprawek F12 (Ruling R4): przy odmowie konta brak dopasowań to sam „Brak na liście.” – winget działa, tylko nie na tym koncie.</summary>
    [Theory]
    [InlineData(WingetState.OtherAccount)]
    [InlineData(WingetState.UserUncertain)]
    public async Task Filter_NoMatch_AccountRefusal_PlainNoCatalogMatch(WingetState state)
    {
        var h = await Loaded(State(new WingetStatus(state, null, InstallText.OtherAccount("dso"))));
        var vm = h.ViewModel;

        vm.SearchText = "qqq";

        Assert.Empty(vm.VisibleItems);
        Assert.Equal(InstallText.NoCatalogMatchPlain, vm.EmptyText);
    }

    [Theory]
    [InlineData(WingetState.NotRegistered)]
    [InlineData(WingetState.NotWorking)]
    public async Task Filter_NoMatch_WingetDown_WingetDownText(WingetState state)
    {
        var h = await Loaded(State(new WingetStatus(state, null, NotWorkingText)));
        var vm = h.ViewModel;

        vm.SearchText = "qqq";

        Assert.Equal(InstallText.NoCatalogMatchWingetDown, vm.EmptyText);
    }

    [Fact]
    public async Task ShowSelectedCommand_SelectsSelectedGroup_EvenWhileFiltering()
    {
        // Spec 11.1: „Zaznaczone” pokazuje wszystkie zaznaczone, filtr pola go nie dotyczy; kolejny znak wraca do wyników.
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;
        h.Row("install.anydesk").IsSelected = true;
        vm.SearchText = "7zip";
        Assert.True(vm.HasSelection);

        vm.ShowSelectedCommand.Execute(null);

        Assert.Same(vm.SelectedItemsGroup, vm.SelectedGroup);
        Assert.Same(vm.SelectedItemsGroup, vm.SelectedCategory);
        Assert.False(vm.IsSearchView);
        Assert.Equal("7zip", vm.SearchText);
        Assert.Equal("install.anydesk", Assert.Single(vm.VisibleRows).Target.Id);

        vm.SearchText = "7zi";

        Assert.True(vm.IsSearchView);
        Assert.Equal("install.7zip", Assert.Single(vm.VisibleRows).Target.Id);

        vm.SearchText = string.Empty;

        Assert.Same(vm.SelectedItemsGroup, vm.SelectedCategory);
        Assert.Equal("install.anydesk", Assert.Single(vm.VisibleRows).Target.Id);
    }

    [Fact]
    public async Task WingetNotWorking_ShowRustDesk_ClearsSearchText()
    {
        var h = await Loaded(State(new WingetStatus(WingetState.NotWorking, null, NotWorkingText)));
        var vm = h.ViewModel;
        vm.SearchText = "7zip";

        vm.ShowRustDeskCommand.Execute(null);

        Assert.Equal(string.Empty, vm.SearchText);
        Assert.False(vm.IsSearchView);
        Assert.Equal("Zdalna pomoc", vm.SelectedGroup.Name);
        Assert.Equal("install.rustdesk", vm.SelectedRow?.Target.Id);
    }

    // --- Runda poprawek 1 (review Tasku 5) ---

    private static InstalledApp RustDeskInstalled() => new(
        @"win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RustDesk", AppKind.Program, "RustDesk", "Purslane Tech Pte. Ltd.", "1.4.2",
        AppScope.Machine, null, null,
        new Win32Info(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RustDesk", @"C:\u.exe", null, false, null, null, false));

    [Fact]
    public async Task WingetBrokeAfterSearchSelection_SearchRowDropped_OnlyRustDeskInSeries()
    {
        // I1: zaznaczony wynik wyszukiwania winget nie może przeżyć odczytu, w którym winget już nie działa.
        // RustDesk najpierw (widok „Wszystkie”), potem wynik winget – sekcja „W winget” zostaje bieżąca (klik kategorii czyściłby pole, R3).
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;
        h.Row("install.rustdesk").IsSelected = true;
        h.Session.SearchResult = WingetResult(new[] { new WingetSearchRow("Notepad++", "Notepad++.Notepad++", "8.7", true) }, Catalog);
        vm.SearchText = "notepad";
        await vm.SearchCommand.ExecuteAsync(null);
        var found = Assert.Single(vm.WingetResults);
        found.IsSelected = true;
        Assert.Equal(2, vm.SelectedCount);

        h.Session.State = State(new WingetStatus(WingetState.NotWorking, null, NotWorkingText));
        await vm.RefreshAsync();

        Assert.Equal(1, vm.SelectedCount);
        Assert.Equal("Zainstaluj (1)", vm.InstallText);
        Assert.Equal("Z 1 kategorii, pobierane z GitHuba RustDesk. Wymaga internetu.", vm.SelectionBarDetails);
        var dropped = Assert.Single(vm.WingetResults);
        Assert.False(dropped.IsSelected);
        Assert.False(dropped.IsSelectable);
        dropped.IsSelected = true;
        Assert.Equal(1, vm.SelectedCount);

        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "install.rustdesk" }, Assert.Single(h.Installed).Select(target => target.Id));
    }

    [Fact]
    public async Task WingetBrokeAfterSearchSelection_OnlySearchRow_CannotInstall()
    {
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(new[] { new WingetSearchRow("Notepad++", "Notepad++.Notepad++", "8.7", true) }, Catalog);
        vm.SearchText = "notepad";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Single(vm.WingetResults).IsSelected = true;
        vm.SearchText = "inne";
        h.Session.SearchResult = new InstallSearchResult(Array.Empty<InstallRow>(), InstallText.SearchNoResults, Array.Empty<string>());
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.SelectedCount); // zaznaczony wynik czeka poza bieżącymi wynikami (_heldSearch)

        h.Session.State = State(new WingetStatus(WingetState.NotRegistered, null, InstallText.NotRegistered));
        await vm.RefreshAsync();

        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.CanInstall);
    }

    [Fact]
    public async Task SearchFinishesAfterRefreshWithWingetNotWorking_ResultsNotSelectable_CannotInstall()
    {
        // M1 final review: wejście na stronę w trakcie wyszukiwania (Refresh nie czeka na IsSearching) przestawia winget na NotWorking,
        // a spóźnione wyniki nie mogą wrócić z polem wyboru ani z zaznaczeniem z wcześniejszego wyszukiwania.
        var h = await Loaded(State(Ready));
        var vm = h.ViewModel;
        h.Session.SearchResult = WingetResult(new[] { new WingetSearchRow("Notepad++", "Notepad++.Notepad++", "8.7", true) }, Catalog);
        vm.SearchText = "notepad";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Single(vm.WingetResults).IsSelected = true;
        Assert.Equal(1, vm.SelectedCount);

        using var gate = new ManualResetEventSlim(false);
        h.Session.SearchGate = gate;
        vm.SearchText = "notepad++";
        var search = vm.SearchCommand.ExecuteAsync(null);
        h.Session.State = State(new WingetStatus(WingetState.NotWorking, null, NotWorkingText));
        await vm.EnterAsync();
        gate.Set();
        await search;

        Assert.False(vm.IsSearching);
        var late = Assert.Single(vm.WingetResults);
        Assert.False(late.IsSelectable);
        Assert.False(late.IsSelected);
        late.IsSelected = true;
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.CanInstall);
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task WingetNotWorking_RustDeskInstalled_NoGitHubNote_NoShowLink()
    {
        // M2: dopisek i „Pokaż RustDesk” tylko, gdy RustDesk da się zaznaczyć.
        var h = await Loaded(State(new WingetStatus(WingetState.NotWorking, null, NotWorkingText), raw: new[] { RustDeskInstalled() }));
        var vm = h.ViewModel;

        Assert.True(vm.HasWingetNotice);
        Assert.Null(vm.GitHubStillAvailable);
        Assert.False(vm.HasGitHubStillAvailable);
        Assert.False(vm.CanShowRustDesk);
        Assert.False(vm.ShowRustDeskCommand.CanExecute(null));
    }

    [Fact]
    public async Task WingetNotWorking_ShowRustDesk_SelectsCategoryAndRow_RequestsReveal()
    {
        // M5: „Pokaż RustDesk” wybiera kategorię pozycji i jej wiersz; widok przewija i ustawia fokus (zdarzenie).
        var h = await Loaded(State(new WingetStatus(WingetState.NotWorking, null, NotWorkingText)));
        var vm = h.ViewModel;
        InstallRowViewModel? revealed = null;
        vm.RowRevealRequested += (_, row) => revealed = row;
        Assert.Same(vm.AllGroup, vm.SelectedGroup);
        Assert.True(vm.CanShowRustDesk);
        Assert.Equal("Pokaż RustDesk", vm.ShowRustDeskText);

        vm.ShowRustDeskCommand.Execute(null);

        Assert.Equal("Zdalna pomoc", vm.SelectedGroup.Name);
        Assert.Equal("install.rustdesk", vm.SelectedRow?.Target.Id);
        Assert.Same(vm.SelectedRow, revealed);
        Assert.False(vm.SelectedRow!.IsSelected); // pokazanie nie zaznacza do serii
    }

    [Fact]
    public async Task WingetReady_NoShowRustDeskLink()
    {
        var h = await Loaded(State(Ready));

        Assert.False(h.ViewModel.CanShowRustDesk);
    }

    [Fact]
    public async Task SearchPlaceholder_DependsOnWinget()
    {
        // Pole zawsze aktywne (filtr listy); przy niedziałającym winget podpowiedź mówi, że szuka tylko na liście.
        var h = await Loaded(State(Ready));
        Assert.Equal("Szukaj aplikacji, np. VLC", h.ViewModel.SearchPlaceholder);

        h.Session.State = State(new WingetStatus(WingetState.NotWorking, null, NotWorkingText));
        await h.ViewModel.RefreshAsync();

        Assert.Equal("Szukaj aplikacji na liście, np. VLC", h.ViewModel.SearchPlaceholder);
        h.ViewModel.SearchText = "vlc";
        Assert.False(h.ViewModel.SearchCommand.CanExecute(null));
    }
}
