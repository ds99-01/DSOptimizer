using System;
using System.Linq;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class InstallPageTests
{
    private const string TruncatedNote = "nazwa pakietu skrócona – zawęź wyszukiwanie";

    private static readonly WingetStatus Ready = new(WingetState.Ready, "v1.11.510", "winget działa (wersja v1.11.510).");

    private static readonly AppsInstallCatalog Catalog = InstallOrderTests.Catalog(
        ("install.7zip", "7zip.7zip", "tools", "7-Zip*"),
        ("install.vcredist", "Microsoft.VCRedist.2015+.x64", "runtimes", "Microsoft Visual C++*"),
        ("install.firefox", "Mozilla.Firefox", "browsers", "Mozilla Firefox*"),
        ("install.vlc", "VideoLAN.VLC", "multimedia", "VLC media player"),
        ("install.chrome", "Google.Chrome", "browsers", "Google Chrome"));

    private static InstalledApp Program(string name)
        => new(
            @"win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + name,
            AppKind.Program,
            name,
            "Vendor",
            "1.0",
            AppScope.Machine,
            null,
            null,
            new Win32Info(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + name, @"C:\u.exe", null, false, null, null, false));

    [Fact]
    public void CatalogRows_CategoryOrderThenCatalogOrder()
    {
        var rows = InstallPage.CatalogRows(Catalog, Array.Empty<InstalledApp>(), Ready);

        Assert.Equal(
            new[] { "install.firefox", "install.chrome", "install.vlc", "install.7zip", "install.vcredist" },
            rows.Select(row => row.Target.Id));
        Assert.All(rows, row =>
        {
            Assert.False(row.IsInstalled);
            Assert.True(row.CanSelect);
            Assert.Null(row.Note);
        });
        Assert.Equal(InstallCategory.Browsers, rows[0].Target.Category);
        Assert.Equal("Mozilla.Firefox", rows[0].Target.WingetId);
    }

    [Fact]
    public void Installed_NoCheckbox()
    {
        var rows = InstallPage.CatalogRows(Catalog, new[] { Program("7-Zip 26.05 (x64)") }, Ready);

        var zip = rows.Single(row => row.Target.Id == "install.7zip");
        Assert.True(zip.IsInstalled);
        Assert.False(zip.CanSelect);
        Assert.Null(zip.Note);
        Assert.All(rows.Where(row => row.Target.Id != "install.7zip"), row => Assert.True(row.CanSelect));
    }

    [Theory]
    [InlineData(WingetState.OtherAccount)]
    [InlineData(WingetState.UserUncertain)]
    [InlineData(WingetState.NotRegistered)]
    [InlineData(WingetState.NotWorking)]
    public void OtherAccount_NoCheckboxes(WingetState state)
    {
        var winget = new WingetStatus(state, null, "Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie dso.");
        var found = new[] { new WingetSearchRow("Notepad++", "Notepad++.Notepad++", "8.7", true) };

        var rows = InstallPage.CatalogRows(Catalog, new[] { Program("VLC media player") }, winget);
        var search = InstallPage.SearchRows(found, Catalog, winget);

        Assert.All(rows, row => Assert.False(row.CanSelect));
        Assert.True(rows.Single(row => row.Target.Id == "install.vlc").IsInstalled);
        Assert.False(Assert.Single(search).CanSelect);
    }

    [Fact]
    public void SearchRow_Truncated_NoCheckboxWithNote()
    {
        var found = WingetSearchParser.Parse(InstallSamples.Lines("synthetic-search-truncated.txt"));

        var rows = InstallPage.SearchRows(found, Catalog, Ready);

        Assert.Equal(4, rows.Count);
        var full = rows.Single(row => row.Target.WingetId == "Microsoft.VisualStudioCode");
        Assert.True(full.CanSelect);
        Assert.Null(full.Note);
        Assert.Equal("winget:Microsoft.VisualStudioCode", full.Target.Id);
        Assert.Equal("Visual Studio Code", full.Target.Title);
        Assert.Null(full.Target.Category);
        Assert.Null(full.Target.Match);

        var truncated = rows.Single(row => row.Target.WingetId == "Microsoft.VisualStudioCode.Ins…");
        Assert.False(truncated.CanSelect);
        Assert.False(truncated.IsInstalled);
        Assert.Equal(TruncatedNote, truncated.Note);

        var empty = rows.Single(row => row.Target.WingetId!.Length == 0);
        Assert.False(empty.CanSelect);
        Assert.Equal(TruncatedNote, empty.Note);
        Assert.Equal("Krotka", empty.Target.Title);
    }

    [Fact]
    public void SearchRow_WideCharsBeforeId_NoCheckboxWithNote()
    {
        var found = WingetSearchParser.Parse(InstallSamples.Lines("synthetic-search-wide.txt"));
        Assert.Contains(found, row => !row.Installable);

        var rows = InstallPage.SearchRows(found, Catalog, Ready);

        Assert.All(rows.Where(row => !row.CanSelect), row => Assert.Equal(TruncatedNote, row.Note));
        Assert.Equal(found.Count(row => !row.Installable), rows.Count(row => !row.CanSelect));
    }

    [Fact]
    public void SearchDuplicateOfCatalog_ShownOnce()
    {
        var found = new[]
        {
            new WingetSearchRow("VLC media player", "videolan.vlc", "3.0.21", true),
            new WingetSearchRow("VLC Nightly", "VideoLAN.VLC.Nightly", "4.0", true),
            new WingetSearchRow("VLC Nightly duplikat", "videolan.VLC.nightly", "4.0", true),
        };

        var rows = InstallPage.SearchRows(found, Catalog, Ready);

        var row = Assert.Single(rows);
        Assert.Equal("winget:VideoLAN.VLC.Nightly", row.Target.Id);
        Assert.Equal("VLC Nightly", row.Target.Title);
    }

    [Fact]
    public void SearchRow_EmptyName_TitleFromId()
    {
        var rows = InstallPage.SearchRows(new[] { new WingetSearchRow(string.Empty, "Vendor.App", null, true) }, Catalog, Ready);

        Assert.Equal("Vendor.App", Assert.Single(rows).Target.Title);
    }

    // --- RustDesk z GitHuba (plan RustDesk, Task 5) ---

    private static readonly AppsInstallCatalog GitHubCatalog = AppsInstallCatalog.Load("""
        { "entries": [
          { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiwizator.",
            "category": "tools", "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" },
          { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Zdalny pulpit.",
            "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
            "source": "https://github.com/rustdesk/rustdesk/releases" },
          { "id": "install.anydesk", "wingetId": "AnyDesk.AnyDesk", "title": "AnyDesk", "summary": "Zdalny pulpit.",
            "category": "remote-help", "match": { "displayName": "AnyDesk" }, "source": "https://example.com/anydesk" }
        ] }
        """);

    private static WingetStatus Status(WingetState state) => new(state, null, "stan " + state);

    private static InstallRow RowOf(System.Collections.Generic.IReadOnlyList<InstallRow> rows, string id) => rows.Single(row => row.Target.Id == id);

    [Theory]
    [InlineData(WingetState.NotWorking)]
    [InlineData(WingetState.NotRegistered)]
    public void CatalogRows_GitHub_SelectableWhenWingetNotWorking(WingetState state)
    {
        var rows = InstallPage.CatalogRows(GitHubCatalog, Array.Empty<InstalledApp>(), Status(state));

        Assert.True(RowOf(rows, "install.rustdesk").CanSelect);
        Assert.False(RowOf(rows, "install.7zip").CanSelect);
        Assert.False(RowOf(rows, "install.anydesk").CanSelect);
    }

    [Fact]
    public void CatalogRows_GitHub_SelectableWhenWingetReady()
    {
        var rows = InstallPage.CatalogRows(GitHubCatalog, Array.Empty<InstalledApp>(), Ready);

        Assert.All(rows, row => Assert.True(row.CanSelect));
    }

    [Theory]
    [InlineData(WingetState.OtherAccount)]
    [InlineData(WingetState.UserUncertain)]
    public void CatalogRows_GitHub_NotSelectableOtherAccount(WingetState state)
    {
        var rows = InstallPage.CatalogRows(GitHubCatalog, Array.Empty<InstalledApp>(), Status(state));

        Assert.All(rows, row => Assert.False(row.CanSelect));
    }

    [Fact]
    public void CatalogRows_GitHubInstalled_NoCheckbox()
    {
        var rustDesk = new InstalledApp(
            @"win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RustDesk",
            AppKind.Program,
            "RustDesk",
            "Purslane Tech Pte. Ltd.",
            "1.4.2",
            AppScope.Machine,
            null,
            null,
            new Win32Info(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RustDesk", @"C:\u.exe", null, false, null, null, false));

        var rows = InstallPage.CatalogRows(GitHubCatalog, new[] { rustDesk }, Status(WingetState.NotWorking));

        var row = RowOf(rows, "install.rustdesk");
        Assert.True(row.IsInstalled);
        Assert.False(row.CanSelect);
    }

    [Theory]
    [InlineData(WingetState.Ready)]
    [InlineData(WingetState.NotWorking)]
    public void CatalogRows_ListNotRead_GitHubNotSelectable(WingetState state)
    {
        // Review Focus 2: bez odczytu listy zainstalowanych nie ma dopasowania ani odczytu zwrotnego – RustDesk bez pola wyboru.
        var rows = InstallPage.CatalogRows(GitHubCatalog, Array.Empty<InstalledApp>(), Status(state), listRead: false);

        Assert.All(rows, row => Assert.False(row.CanSelect));
    }

    // --- Zaznaczone, wyniki winget z pozycjami listy (poprawki po przeklikaniu, Task 1) ---

    [Fact]
    public void CatalogIdsIn_ByWingetIdAndAlias_NoDuplicates_WingetOrder()
    {
        var found = new[]
        {
            new WingetSearchRow("Discord", "Discord.Discord", "1", true),
            new WingetSearchRow("Discord PTB", "Discord.Discord.PTB", "1", true),
            new WingetSearchRow("RustDesk", "rustdesk.rustdesk", "1", true),
            new WingetSearchRow("Discord", "discord.discord", "1", true),
        };
        var catalog = AppsInstallCatalog.Load("""
            { "entries": [
              { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Zdalny pulpit.",
                "category": "remote-help", "match": { "displayName": "RustDesk" }, "source": "https://github.com/rustdesk/rustdesk/releases" },
              { "id": "install.discord", "wingetId": "Discord.Discord", "title": "Discord", "summary": "Czat.",
                "category": "messengers", "match": { "displayName": "Discord" }, "source": "https://example.com/d" }
            ] }
            """);

        Assert.Equal(new[] { "install.discord", "install.rustdesk" }, InstallPage.CatalogIdsIn(found, catalog));
        Assert.Throws<ArgumentNullException>(() => InstallPage.CatalogIdsIn(null!, catalog));
        Assert.Throws<ArgumentNullException>(() => InstallPage.CatalogIdsIn(found, null!));
    }

    [Fact]
    public void SearchRows_SkipsAliasOfCatalogEntry()
    {
        var found = new[]
        {
            new WingetSearchRow("RustDesk", "RustDesk.RustDesk", "1", true),
            new WingetSearchRow("Vendor App", "Vendor.App", "1", true),
        };

        var rows = InstallPage.SearchRows(found, GitHubCatalog, Ready);

        Assert.Equal("winget:Vendor.App", Assert.Single(rows).Target.Id);
    }

    [Fact]
    public void WingetAliases_CaseInsensitive()
        => Assert.Equal("install.rustdesk", GitHubSources.WingetAliases["rustdesk.RUSTDESK"]);

    [Fact]
    public void SelectedGroup_Properties()
    {
        Assert.Equal("Zaznaczone", InstallGroup.Selected.Name);
        Assert.Equal(InstallGroupKind.Selected, InstallGroup.Selected.Kind);
        Assert.Null(InstallGroup.Selected.Category);
    }

    // --- pozycja „Wszystkie” (D1, Ruling P3) ---

    [Fact]
    public void Groups_AllFirstThenCategoriesWithEntries()
    {
        var groups = InstallPage.Groups(Catalog);

        Assert.Equal(
            new[] { "Wszystkie", "Zaznaczone", "Przeglądarki", "Multimedia", "Narzędzia", "Środowiska uruchomieniowe" },
            groups.Select(group => group.Name));
        Assert.Equal(InstallGroupKind.All, groups[0].Kind);
        Assert.Same(InstallGroup.All, groups[0]);
        Assert.Equal(InstallGroupKind.Selected, groups[1].Kind);
        Assert.Same(InstallGroup.Selected, groups[1]);
        Assert.False(groups[1].Contains(InstallPage.CatalogRows(Catalog, Array.Empty<InstalledApp>(), Ready)[0]));
        Assert.True(groups[1].ShowsCategoryHeaders);
        Assert.All(groups.Skip(2), group => Assert.Equal(InstallGroupKind.Category, group.Kind));
        Assert.Equal(InstallCategory.Browsers, groups[2].Category);
    }

    [Fact]
    public void Group_HeadersOnlyForAll_SearchGroupName()
    {
        Assert.True(InstallGroup.All.ShowsCategoryHeaders);
        Assert.False(InstallGroup.Of(InstallCategory.Tools).ShowsCategoryHeaders);
        Assert.False(InstallGroup.Search.ShowsCategoryHeaders);
        Assert.Equal("Wyniki wyszukiwania", InstallGroup.Search.Name);
        Assert.Equal(InstallGroupKind.Search, InstallGroup.Search.Kind);
        Assert.Null(InstallGroup.Search.Category);
        Assert.Equal(InstallGroup.Of(InstallCategory.Tools), InstallGroup.Of(InstallCategory.Tools));
    }

    [Fact]
    public void Contains_All_EveryCatalogRow_CategoryOnlyItsRows_SearchOnlySearchRows()
    {
        var rows = InstallPage.CatalogRows(Catalog, Array.Empty<InstalledApp>(), Ready);
        var search = InstallPage.SearchRows(new[] { new WingetSearchRow("Notepad++", "Notepad++.Notepad++", "8.7", true) }, Catalog, Ready);
        var all = rows.Concat(search).ToList();

        Assert.Equal(rows.Select(row => row.Target.Id), all.Where(InstallGroup.All.Contains).Select(row => row.Target.Id));
        Assert.Equal(
            new[] { "install.firefox", "install.chrome" },
            all.Where(InstallGroup.Of(InstallCategory.Browsers).Contains).Select(row => row.Target.Id));
        Assert.Equal(new[] { "winget:Notepad++.Notepad++" }, all.Where(InstallGroup.Search.Contains).Select(row => row.Target.Id));
        Assert.Throws<ArgumentNullException>(() => InstallGroup.All.Contains(null!));
    }

    // --- „Pokaż RustDesk” w komunikacie winget (M5) ---

    [Theory]
    [InlineData(WingetState.NotWorking)]
    [InlineData(WingetState.Ready)]
    public void GitHubRowToShow_SelectableGitHubRow(WingetState state)
    {
        var rows = InstallPage.CatalogRows(GitHubCatalog, Array.Empty<InstalledApp>(), Status(state));

        var row = InstallPage.GitHubRowToShow(rows);

        Assert.Equal("install.rustdesk", row?.Target.Id);
    }

    [Fact]
    public void GitHubRowToShow_InstalledOrNotSelectable_Null()
    {
        var rustDesk = new InstalledApp(
            @"win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RustDesk",
            AppKind.Program,
            "RustDesk",
            "Purslane Tech Pte. Ltd.",
            "1.4.2",
            AppScope.Machine,
            null,
            null,
            new Win32Info(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RustDesk", @"C:\u.exe", null, false, null, null, false));

        Assert.Null(InstallPage.GitHubRowToShow(InstallPage.CatalogRows(GitHubCatalog, new[] { rustDesk }, Status(WingetState.NotWorking))));
        Assert.Null(InstallPage.GitHubRowToShow(InstallPage.CatalogRows(GitHubCatalog, Array.Empty<InstalledApp>(), Status(WingetState.OtherAccount))));
        Assert.Null(InstallPage.GitHubRowToShow(InstallPage.CatalogRows(Catalog, Array.Empty<InstalledApp>(), Ready)));
        Assert.Throws<ArgumentNullException>(() => InstallPage.GitHubRowToShow(null!));
    }

    [Fact]
    public void CategoryHeader_CategoryNameOrSearchGroup()
    {
        var rows = InstallPage.CatalogRows(Catalog, Array.Empty<InstalledApp>(), Ready);
        var search = InstallPage.SearchRows(new[] { new WingetSearchRow("Notepad++", "Notepad++.Notepad++", "8.7", true) }, Catalog, Ready);

        Assert.Equal("Przeglądarki", InstallPage.CategoryHeader(rows[0]));
        Assert.Equal("Wyniki wyszukiwania", InstallPage.CategoryHeader(Assert.Single(search)));
    }

    [Fact]
    public void GroupsAndCategoryHeader_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => InstallPage.Groups(null!));
        Assert.Throws<ArgumentNullException>(() => InstallPage.CategoryHeader(null!));
    }

    [Theory]
    [InlineData(TweakOutcome.Installed, true)]
    [InlineData(TweakOutcome.AlreadyInstalled, true)]
    [InlineData(TweakOutcome.InstalledUnverified, true)]
    [InlineData(TweakOutcome.NotConfirmed, false)]
    [InlineData(TweakOutcome.Failed, false)]
    [InlineData(TweakOutcome.Skipped, false)]
    [InlineData(TweakOutcome.Refused, false)]
    public void CountsAsInstalled(TweakOutcome outcome, bool expected)
        => Assert.Equal(expected, InstallPage.CountsAsInstalled(outcome));

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => InstallPage.CatalogRows(null!, Array.Empty<InstalledApp>(), Ready));
        Assert.Throws<ArgumentNullException>(() => InstallPage.CatalogRows(Catalog, null!, Ready));
        Assert.Throws<ArgumentNullException>(() => InstallPage.CatalogRows(Catalog, Array.Empty<InstalledApp>(), null!));
        Assert.Throws<ArgumentNullException>(() => InstallPage.SearchRows(null!, Catalog, Ready));
        Assert.Throws<ArgumentNullException>(() => InstallPage.SearchRows(Array.Empty<WingetSearchRow>(), null!, Ready));
        Assert.Throws<ArgumentNullException>(() => InstallPage.SearchRows(Array.Empty<WingetSearchRow>(), Catalog, null!));
    }
}

