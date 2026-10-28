using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Tests.Unit.Catalog;
using DSOptimizer.Tests.Unit.Support;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class AppsInstallCatalogTests
{
    private const string Good7Zip = """
        { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiwizator.",
          "category": "tools", "match": { "displayName": "7-Zip*", "publisher": "Igor Pavlov" },
          "source": "https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip" }
        """;

    private const string GoodVlc = """
        { "id": "install.vlc", "wingetId": "VideoLAN.VLC", "title": "VLC", "summary": "Odtwarzacz.",
          "category": "multimedia", "match": { "displayName": "VLC media player", "publisher": "VideoLAN" },
          "warning": "Uwaga.", "source": "https://www.videolan.org/vlc/" }
        """;

    private const string GoodRustDesk = """
        { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Zdalny pulpit.",
          "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
          "source": "https://github.com/rustdesk/rustdesk/releases" }
        """;

    private const string GitHubNotAllowedText =
        "pole github jest dozwolone tylko dla pozycji z listy w kodzie (install.rustdesk → rustdesk/rustdesk)";

    private static string Wrap(params string[] entries) => "{ \"entries\": [" + string.Join(",", entries) + "] }";

    [Fact]
    public void GitHubEntry_Loaded()
    {
        var catalog = AppsInstallCatalog.Load(Wrap(Good7Zip, GoodRustDesk));

        var rustDesk = catalog.Entries[1];
        Assert.Equal("install.rustdesk", rustDesk.Id);
        Assert.Null(rustDesk.WingetId);
        Assert.Equal("rustdesk/rustdesk", rustDesk.GitHub);
        Assert.Equal(InstallOrigin.GitHub, rustDesk.Origin);
        Assert.Equal("github:rustdesk/rustdesk", rustDesk.PackageKey);
        Assert.Equal(InstallCategory.RemoteHelp, rustDesk.Category);
        Assert.Equal("Purslane Tech*", rustDesk.Match.Publisher);

        var zip = catalog.Entries[0];
        Assert.Null(zip.GitHub);
        Assert.Equal(InstallOrigin.Winget, zip.Origin);
        Assert.Equal("7zip.7zip", zip.PackageKey);

        // Wpis bez wingetId nie jest wynikiem wyszukiwania po identyfikatorze winget (także pustym).
        Assert.Null(catalog.FindByWingetId(null!));
        Assert.Same(rustDesk, catalog.Find("install.rustdesk"));
    }

    [Fact]
    public void BothWingetIdAndGitHub_Throws()
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(
            Wrap(GoodRustDesk.Replace("\"github\": \"rustdesk/rustdesk\",", "\"github\": \"rustdesk/rustdesk\", \"wingetId\": \"RustDesk.RustDesk\","))));

        Assert.Equal("install.rustdesk", ex.EntryId);
        Assert.Equal("github", ex.Field);
    }

    [Fact]
    public void NeitherWingetIdNorGitHub_Throws()
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(
            Wrap(GoodRustDesk.Replace("\"github\": \"rustdesk/rustdesk\",", ""))));

        Assert.Equal("install.rustdesk", ex.EntryId);
        Assert.Equal("wingetId", ex.Field);
    }

    [Fact]
    public void GitHubForOtherId_Throws()
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(
            Wrap(GoodRustDesk.Replace("\"install.rustdesk\"", "\"install.anydesk\""))));

        Assert.Equal("install.anydesk", ex.EntryId);
        Assert.Equal("github", ex.Field);
        Assert.EndsWith(GitHubNotAllowedText, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("evil/rustdesk")]
    [InlineData("RustDesk/rustdesk")]
    [InlineData("rustdesk/rustdesk-server")]
    [InlineData("rustdesk/rustdesk/")]
    [InlineData("https://github.com/rustdesk/rustdesk")]
    [InlineData("3")]
    public void GitHubWrongRepository_Throws(string repository)
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(
            Wrap(GoodRustDesk.Replace("\"rustdesk/rustdesk\"", "\"" + repository + "\""))));

        Assert.Equal("install.rustdesk", ex.EntryId);
        Assert.Equal("github", ex.Field);
        Assert.EndsWith(GitHubNotAllowedText, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GitHubNotText_Throws()
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(
            Wrap(GoodRustDesk.Replace("\"rustdesk/rustdesk\"", "[1]"))));

        Assert.Equal("github", ex.Field);
    }

    [Fact]
    public void FromEntry_GitHub_CarriesRepositoryAndKey()
    {
        var entry = AppsInstallCatalog.Load(Wrap(GoodRustDesk)).Find("install.rustdesk")!;

        var target = InstallTarget.FromEntry(entry);

        Assert.Equal("install.rustdesk", target.Id);
        Assert.Null(target.WingetId);
        Assert.Equal("rustdesk/rustdesk", target.GitHub);
        Assert.Equal(InstallOrigin.GitHub, target.Origin);
        Assert.Equal("github:rustdesk/rustdesk", target.PackageKey);
        Assert.Same(entry.Match, target.Match);

        var search = InstallTarget.FromSearch("7zip.7zip", "7-Zip");
        Assert.Null(search.GitHub);
        Assert.Equal(InstallOrigin.Winget, search.Origin);
        Assert.Equal("7zip.7zip", search.PackageKey);
    }

    [Theory]
    [InlineData("install.rustdesk", "rustdesk/rustdesk", true)]
    [InlineData("install.rustdesk", "rustdesk/RustDesk", false)]
    [InlineData("install.RustDesk", "rustdesk/rustdesk", false)]
    [InlineData("install.anydesk", "rustdesk/rustdesk", false)]
    [InlineData(null, "rustdesk/rustdesk", false)]
    [InlineData("install.rustdesk", null, false)]
    public void GitHubSources_IsAllowed(string? id, string? repository, bool expected)
    {
        Assert.Equal(expected, GitHubSources.IsAllowed(id!, repository!));
        Assert.Equal("github:", GitHubSources.KeyPrefix);
    }

    [Fact]
    public void InstallItemReport_DefaultsToWinget()
    {
        var report = new InstallItemReport("install.7zip", "7zip.7zip", "7-Zip", Core.Engine.TweakOutcome.Installed, 0, "ok", null, false, null);

        Assert.Equal(InstallOrigin.Winget, report.Origin);
        Assert.Null(report.GitHub);
        Assert.Null(report.MsiExitCode);
    }

    [Fact]
    public void Load_ValidFile_EntriesInOrderWithAllFields()
    {
        var catalog = AppsInstallCatalog.Load(Wrap(Good7Zip, GoodVlc));

        Assert.Equal(new[] { "install.7zip", "install.vlc" }, catalog.Entries.Select(e => e.Id));
        var zip = catalog.Entries[0];
        Assert.Equal("7zip.7zip", zip.WingetId);
        Assert.Equal("7-Zip", zip.Title);
        Assert.Equal("Archiwizator.", zip.Summary);
        Assert.Equal(InstallCategory.Tools, zip.Category);
        Assert.Equal("7-Zip*", zip.Match.DisplayName);
        Assert.Equal("Igor Pavlov", zip.Match.Publisher);
        Assert.Null(zip.Match.PackageName);
        Assert.Null(zip.Warning);
        Assert.Equal("https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip", zip.Source);
        var vlc = catalog.Entries[1];
        Assert.Equal(InstallCategory.Multimedia, vlc.Category);
        Assert.Equal("Uwaga.", vlc.Warning);
    }

    [Theory]
    [InlineData("browsers", InstallCategory.Browsers)]
    [InlineData("messengers", InstallCategory.Messengers)]
    [InlineData("multimedia", InstallCategory.Multimedia)]
    [InlineData("documents", InstallCategory.Documents)]
    [InlineData("tools", InstallCategory.Tools)]
    [InlineData("remote-help", InstallCategory.RemoteHelp)]
    [InlineData("games", InstallCategory.Games)]
    [InlineData("runtimes", InstallCategory.Runtimes)]
    public void Load_EveryCategoryName_MapsToEnum(string json, InstallCategory expected)
    {
        var catalog = AppsInstallCatalog.Load(Wrap(Good7Zip.Replace("\"tools\"", "\"" + json + "\"")));

        Assert.Equal(expected, Assert.Single(catalog.Entries).Category);
    }

    [Fact]
    public void Load_AppxMatch_ReadsPackageName()
    {
        var catalog = AppsInstallCatalog.Load(Wrap(Good7Zip.Replace(
            "{ \"displayName\": \"7-Zip*\", \"publisher\": \"Igor Pavlov\" }", "{ \"packageName\": \"Foo.Bar\" }")));

        var match = Assert.Single(catalog.Entries).Match;
        Assert.Equal("Foo.Bar", match.PackageName);
        Assert.Null(match.DisplayName);
    }

    [Fact]
    public void Find_ByIdAndByWingetIdCaseInsensitive()
    {
        var catalog = AppsInstallCatalog.Load(Wrap(Good7Zip, GoodVlc));

        Assert.Equal("VideoLAN.VLC", catalog.Find("install.vlc")!.WingetId);
        Assert.Null(catalog.Find("install.nic"));
        Assert.Equal("install.vlc", catalog.FindByWingetId("videolan.vlc")!.Id);
        Assert.Null(catalog.FindByWingetId("Foo.Bar"));
        Assert.Null(catalog.Find(null!));
        Assert.Null(catalog.FindByWingetId(null!));
    }

    public static IEnumerable<object?[]> InvalidFiles()
    {
        // json, oczekiwane id w komunikacie, oczekiwane pole
        yield return new object?[] { Wrap(Good7Zip.Replace("\"install.7zip\"", "\"7zip\"")), "7zip", "id" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"install.7zip\"", "\"install.7Zip\"")), "install.7Zip", "id" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"id\": \"install.7zip\", ", "")), "(wpis 1)", "id" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"install.7zip\"", "5")), "(wpis 1)", "id" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"7zip.7zip\"", "\"7zip 7zip\"")), "install.7zip", "wingetId" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"7zip.7zip\"", "\"Foo.Bar…\"")), "install.7zip", "wingetId" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"wingetId\": \"7zip.7zip\", ", "")), "install.7zip", "wingetId" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"title\": \"7-Zip\", ", "")), "install.7zip", "title" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"7-Zip\"", "\"" + new string('t', 61) + "\"")), "install.7zip", "title" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"Archiwizator.\"", "\"" + new string('s', 161) + "\"")), "install.7zip", "summary" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"summary\": \"Archiwizator.\",", "")), "install.7zip", "summary" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"Archiwizator.\"", "7")), "install.7zip", "summary" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"category\": \"tools\", ", "")), "install.7zip", "category" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"match\": { \"displayName\": \"7-Zip*\", \"publisher\": \"Igor Pavlov\" },", "")), "install.7zip", "match" };
        yield return new object?[] { Wrap(Good7Zip.Replace("{ \"displayName\": \"7-Zip*\", \"publisher\": \"Igor Pavlov\" }", "{ }")), "install.7zip", "match" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"Igor Pavlov\"", "[1]")), "install.7zip", "publisher" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"source\"", "\"warning\": 3, \"source\"")), "install.7zip", "warning" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip\"", "\"\"")), "install.7zip", "source" };
        yield return new object?[] { Wrap(Good7Zip.Replace("\"https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip\"", "\"winget-pkgs\"")), "install.7zip", "source" };
        yield return new object?[] { "[1]", "(plik)", "json" };
        yield return new object?[] { "{ \"entries\": [ ", "(plik)", "json" };
        yield return new object?[] { "{ }", "(plik)", "entries" };
        yield return new object?[] { "{ \"entries\": [ 5 ] }", "(wpis 1)", "entries" };
    }

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void Load_InvalidFile_ThrowsWithIdAndField(string json, string expectedId, string expectedField)
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(json));

        Assert.Equal(expectedId, ex.EntryId);
        Assert.Equal(expectedField, ex.Field);
        Assert.StartsWith("apps: " + expectedId + ", pole " + expectedField + ": ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateId_Throws()
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(
            Wrap(Good7Zip, Good7Zip.Replace("\"7zip.7zip\"", "\"Other.Id\""))));

        Assert.Equal("install.7zip", ex.EntryId);
        Assert.Equal("id", ex.Field);
    }

    [Fact]
    public void DuplicateWingetIdCaseInsensitive_Throws()
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(
            Wrap(Good7Zip, GoodVlc.Replace("\"VideoLAN.VLC\"", "\"7ZIP.7Zip\""))));

        Assert.Equal("install.vlc", ex.EntryId);
        Assert.Equal("wingetId", ex.Field);
    }

    [Fact]
    public void UnknownCategory_Throws()
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(
            Wrap(Good7Zip.Replace("\"tools\"", "\"Tools\""))));

        Assert.Equal("category", ex.Field);
        Assert.Contains("remote-help", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://www.7-zip.org/")]
    [InlineData("ftp://www.7-zip.org/")]
    [InlineData("/manifests/7/7zip")]
    public void SourceNotHttps_Throws(string source)
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(Wrap(Good7Zip.Replace(
            "\"https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip\"", "\"" + source + "\""))));

        Assert.Equal("install.7zip", ex.EntryId);
        Assert.Equal("source", ex.Field);
    }

    [Fact]
    public void MatchWithPackageAndDisplayName_Throws()
    {
        var ex = Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(Wrap(Good7Zip.Replace(
            "\"displayName\": \"7-Zip*\"", "\"packageName\": \"Foo\", \"displayName\": \"7-Zip*\""))));

        Assert.Equal("install.7zip", ex.EntryId);
        Assert.Equal("match", ex.Field);
        Assert.Contains("packageName", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>M7: jedna reguła Id pozycji listy dla <c>apps.json</c> i <c>CliParser</c>; <c>\z</c>, więc bez znaku nowej linii na końcu.</summary>
    [Theory]
    [InlineData("install.7zip", true)]
    [InlineData("install.k-lite", true)]
    [InlineData("install.vcredist-x64", true)]
    [InlineData("install.7zip\n", false)]
    [InlineData("install.", false)]
    [InlineData("install.Chrome", false)]
    [InlineData("install.a.b", false)]
    [InlineData("winget:7zip.7zip", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidId(string? id, bool expected)
        => Assert.Equal(expected, AppsInstallCatalog.IsValidId(id));

    [Fact]
    public void Load_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AppsInstallCatalog.Load(null!));
    }

    [Fact]
    public void CategoryName_PolishNamesInUiOrder()
    {
        Assert.Equal(
            new[] { "Przeglądarki", "Komunikatory", "Multimedia", "Dokumenty", "Narzędzia", "Zdalna pomoc", "Gry", "Środowiska uruchomieniowe" },
            Enum.GetValues(typeof(InstallCategory)).Cast<InstallCategory>().Select(InstallText.CategoryName));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallText.CategoryName((InstallCategory)99));
    }

    [Fact]
    public void Load_EmptyEntries_EmptyCatalog()
    {
        Assert.Empty(AppsInstallCatalog.Load("{ \"entries\": [] }").Entries);
    }

    // ---- Lista wbudowana (Task 9): pozycje z dowodem z VM, docs/apps/install-README.md ----

    /// <summary>Klucze pakietów listy wbudowanej (<see cref="InstallEntry.PackageKey"/>: wingetId albo <c>github:&lt;repo&gt;</c>) w kolejności pliku.</summary>
    private static readonly string[] BuiltInPackageKeysInOrder =
    {
        "Google.Chrome", "Mozilla.Firefox", "Brave.Brave", "Opera.Opera",
        "Discord.Discord", "Telegram.TelegramDesktop", "OpenWhisperSystems.Signal", "Zoom.Zoom",
        "VideoLAN.VLC", "CodecGuide.K-LiteCodecPack.Standard", "PeterPawlowski.foobar2000",
        "Foxit.FoxitReader", "TheDocumentFoundation.LibreOffice", "Adobe.Acrobat.Reader.64-bit", "SumatraPDF.SumatraPDF",
        "7zip.7zip", "Notepad++.Notepad++", "voidtools.Everything", "Microsoft.PowerToys", "RARLab.WinRAR", "ShareX.ShareX",
        "CPUID.CPU-Z", "CPUID.HWMonitor", "TechPowerUp.GPU-Z", "REALiX.HWiNFO",
        "github:rustdesk/rustdesk", "AnyDesk.AnyDesk", "TeamViewer.TeamViewer",
        "Valve.Steam", "EpicGames.EpicGamesLauncher", "GOG.Galaxy",
        "Microsoft.VCRedist.2015+.x64", "Microsoft.VCRedist.2015+.x86", "Microsoft.DotNet.DesktopRuntime.8", "Microsoft.DotNet.DesktopRuntime.9",
    };

    /// <summary>Pozycje z wpisem Uninstall w gałęzi HKU (instalacja per użytkownik, pomiar Pro/Home 2026-10-24).</summary>
    private static readonly string[] PerUserIds =
    {
        "install.brave", "install.opera", "install.discord", "install.telegram", "install.signal", "install.sumatrapdf", "install.powertoys",
    };

    private static readonly string[] RejectedWingetIds = { "Spotify.Spotify", "Microsoft.DirectX", "RustDesk.RustDesk", "Microsoft.Teams" };

    /// <summary>Ostrzeżenie pozycji RustDesk – spec 4, dosłownie (pomiar spike'a VM 2026-10-24).</summary>
    private const string RustDeskWarning =
        "Instaluje usługę RustDesk (działa w tle przy starcie Windows), dodaje reguły zapory, skrót na pulpicie i ikonę w Autostarcie "
        + "wszystkich kont. Instalator pobierany z GitHuba RustDesk i sprawdzany podpisem wydawcy (PURSLANE).";

    private static AppsInstallCatalog BuiltIn() => AppsInstallCatalog.Load(BuiltInCatalog.AppsInstallSource.Json);

    private const string PolishReadme = "docs/apps/install-README.md";
    private const string EnglishReadme = "docs/en/apps/install-README.md";

    private static string Readme(string path) => File.ReadAllText(RepoRoot.Combine(path.Split('/')), Encoding.UTF8);

    private static string Section(string text, string heading, string path)
    {
        var start = text.IndexOf("\n## " + heading, StringComparison.Ordinal);
        Assert.True(start >= 0, "Brak sekcji „" + heading + "” w " + path);
        var end = text.IndexOf("\n## ", start + 4, StringComparison.Ordinal);
        return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
    }

    [Fact]
    public void BuiltIn_AllMeasuredEntriesInCategoryOrder()
    {
        var entries = BuiltIn().Entries;

        Assert.Equal(BuiltInPackageKeysInOrder, entries.Select(e => e.PackageKey));
        Assert.Equal(entries.Select(e => e.Category).OrderBy(c => c), entries.Select(e => e.Category));
        Assert.Equal(Enum.GetValues(typeof(InstallCategory)).Cast<InstallCategory>(), entries.Select(e => e.Category).Distinct());
    }

    [Theory]
    [InlineData(PolishReadme, "Lista – pozycje z dowodem")]
    [InlineData(EnglishReadme, "List – entries with evidence")]
    public void BuiltIn_EveryEntryHasReadmeRow(string path, string listHeading)
    {
        // Każde id i wingetId (albo repozytorium github) ma wiersz tabeli z dowodem, z tą samą regułą match i tym samym źródłem co apps.json.
        var section = Section(Readme(path), listHeading, path);
        var table = section
            .Split('\n').Where(l => l.StartsWith("| `install.", StringComparison.Ordinal)).ToList();
        var entries = BuiltIn().Entries;

        Assert.Equal(35, entries.Count);
        Assert.Equal(entries.Count, table.Count);
        Assert.Contains("(" + entries.Count + ")", section.Split('\n')[1], StringComparison.Ordinal);
        foreach (var entry in entries)
        {
            var row = Assert.Single(table, l => l.StartsWith("| `" + entry.Id + "` |", StringComparison.Ordinal));
            Assert.Contains("`" + (entry.WingetId ?? entry.GitHub) + "`", row, StringComparison.Ordinal);
            Assert.Contains(entry.Source, row, StringComparison.Ordinal);
            if (entry.Match.DisplayName is not null)
            {
                Assert.Contains("displayName: \"" + entry.Match.DisplayName + "\"", row, StringComparison.Ordinal);
            }

            if (entry.Match.Publisher is not null)
            {
                Assert.Contains("publisher: \"" + entry.Match.Publisher + "\"", row, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(PolishReadme, "Odrzucone", "zamiast tego `install.rustdesk` z GitHuba")]
    [InlineData(EnglishReadme, "Rejected", "use `install.rustdesk` from GitHub instead")]
    public void BuiltIn_RejectedNotInList(string path, string rejectedHeading, string rustDeskPhrase)
    {
        var catalog = BuiltIn();
        var rejected = Section(Readme(path), rejectedHeading, path);

        foreach (var id in RejectedWingetIds)
        {
            Assert.Null(catalog.FindByWingetId(id));
            Assert.Contains("| `" + id + "` |", rejected, StringComparison.Ordinal);
        }

        // RustDesk nie ma w winget, ale jest na liście z GitHuba: wiersz odrzuconego pakietu winget odsyła do pozycji listy.
        var rustDeskRow = rejected.Split('\n').Single(l => l.StartsWith("| `RustDesk.RustDesk` |", StringComparison.Ordinal));
        Assert.Contains(rustDeskPhrase, rustDeskRow, StringComparison.Ordinal);
        Assert.Equal(InstallOrigin.GitHub, catalog.Find("install.rustdesk")!.Origin);
    }

    [Fact]
    public void BuiltIn_RuntimesCategoryHasVcRedistAndDotNet()
    {
        Assert.Equal(
            new[] { "Microsoft.VCRedist.2015+.x64", "Microsoft.VCRedist.2015+.x86", "Microsoft.DotNet.DesktopRuntime.8", "Microsoft.DotNet.DesktopRuntime.9" },
            BuiltIn().Entries.Where(e => e.Category == InstallCategory.Runtimes).Select(e => e.WingetId));
    }

    [Fact]
    public void BuiltIn_NoEntryPreselected()
    {
        // Format nie ma pola zaznaczenia ani presetów (decyzja Dawida 2026-10-15): wbudowany plik nie ma żadnych nieznanych pól.
        var allowed = new[] { "id", "wingetId", "github", "title", "summary", "category", "match", "warning", "source" };
        var root = JObject.Parse(BuiltInCatalog.AppsInstallSource.Json.TrimStart('\uFEFF'));

        Assert.Equal(new[] { "entries" }, root.Properties().Select(p => p.Name));
        foreach (var item in (JArray)root["entries"]!)
        {
            var obj = (JObject)item;
            Assert.All(obj.Properties(), p => Assert.Contains(p.Name, allowed));
            Assert.All(((JObject)obj["match"]!).Properties(), p => Assert.Contains(p.Name, new[] { "displayName", "publisher" }));
        }
    }

    [Fact]
    public void BuiltIn_SourceIsWingetPkgsManifest()
    {
        // Ścieżka manifestu w microsoft/winget-pkgs: manifests/<pierwsza litera małą>/<Id z „.” zamienionymi na „/”>
        // (sprawdzone przez GitHub API 2026-10-24 dla każdej pozycji). Tylko pozycje winget; źródło pozycji z GitHuba sprawdza test niżej.
        var winget = BuiltIn().Entries.Where(e => e.Origin == InstallOrigin.Winget).ToList();

        Assert.Equal(BuiltIn().Entries.Count - 1, winget.Count);
        foreach (var entry in winget)
        {
            var wingetId = entry.WingetId!;
            var expected = "https://github.com/microsoft/winget-pkgs/tree/master/manifests/"
                + char.ToLowerInvariant(wingetId[0]) + "/" + wingetId.Replace('.', '/');
            Assert.Equal(expected, entry.Source);
        }
    }

    [Fact]
    public void BuiltIn_RustDeskSourceIsGitHubReleases()
    {
        // Jedyna pozycja z GitHuba (review Tasku 1): źródło = strona wydań repozytorium, z którego program pobiera MSI.
        var gitHub = Assert.Single(BuiltIn().Entries, e => e.Origin == InstallOrigin.GitHub);

        Assert.Equal("install.rustdesk", gitHub.Id);
        Assert.Equal("rustdesk/rustdesk", gitHub.GitHub);
        Assert.Null(gitHub.WingetId);
        Assert.Equal("https://github.com/rustdesk/rustdesk/releases", gitHub.Source);
        Assert.Equal("https://github.com/" + RustDeskSource.Repository + "/releases", gitHub.Source);
        Assert.Equal("RustDesk", gitHub.Title);
        Assert.Equal("Zdalny pulpit open source: pomoc na odległość i dostęp do tego komputera z innego.", gitHub.Summary);
        Assert.Equal(InstallCategory.RemoteHelp, gitHub.Category);
        Assert.Equal(new AppMatch(null, "RustDesk", "Purslane Tech*"), gitHub.Match);
    }

    [Fact]
    public void BuiltIn_WarningsFromMeasurement()
    {
        var entries = BuiltIn().Entries;

        Assert.Contains("program płatny po okresie próbnym", entries.Single(e => e.Id == "install.winrar").Warning, StringComparison.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var perUser = entry.Warning?.IndexOf("tylko dla bieżącego konta", StringComparison.Ordinal) >= 0;
            Assert.True(perUser == PerUserIds.Contains(entry.Id), entry.Id + ": ostrzeżenie o instalacji dla bieżącego konta = gałąź HKU z pomiaru");
        }

        Assert.Contains("Microsoft Visual C++", entries.Single(e => e.Id == "install.libreoffice").Warning, StringComparison.Ordinal);
        Assert.Contains("Microsoft GameInput", entries.Single(e => e.Id == "install.epic-games").Warning, StringComparison.Ordinal);
        Assert.Contains("Mozilla Maintenance Service", entries.Single(e => e.Id == "install.firefox").Warning, StringComparison.Ordinal);
        Assert.Contains("Foxit PDF Reader Update Service", entries.Single(e => e.Id == "install.foxit-reader").Warning, StringComparison.Ordinal);
        Assert.Equal(RustDeskWarning, entries.Single(e => e.Id == "install.rustdesk").Warning);
    }

    private static InstalledApp Win32(string name, string? publisher, bool systemComponent = false)
        => new(
            "win32:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + name,
            AppKind.Program,
            name,
            publisher,
            "1.0",
            AppScope.Machine,
            null,
            null,
            new Win32Info("HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + name, "C:\\u.exe", null, false, null, null, systemComponent));

    private static InstalledApp Appx(string package)
        => new(
            "appx:" + package + "_x",
            AppKind.Store,
            package,
            "CN=X",
            "1.0",
            AppScope.User,
            "S-1-5-21-1",
            new AppxInfo(package + "_1.0_x64__x", package + "_x", package, false, false, false, "Developer", false),
            null);

    public static IEnumerable<object?[]> MeasuredUninstallEntries()
    {
        // Prawdziwe wpisy z pomiaru VM 2026-10-24 (Pro: winget v1.11 → v1.29, Home: v1.11; spike Pro) → jedyna pasująca pozycja albo null.
        yield return new object?[] { "Google Chrome", "Google LLC", false, "install.chrome" };
        yield return new object?[] { "Mozilla Firefox (x64 pl)", "Mozilla", false, "install.firefox" };
        yield return new object?[] { "Mozilla Firefox (x64 en-US)", "Mozilla", false, "install.firefox" };
        yield return new object?[] { "Brave", "Autorzy Brave", false, "install.brave" };
        yield return new object?[] { "Opera Stable 137.0.6036.39", "Opera Software", false, "install.opera" };
        yield return new object?[] { "Opera Stable 137.0.6036.63", "Opera Software", false, "install.opera" };
        yield return new object?[] { "Discord", "Discord Inc.", false, "install.discord" };
        yield return new object?[] { "Telegram Desktop", "Telegram FZ-LLC", false, "install.telegram" };
        yield return new object?[] { "Signal 8.32.0", "Signal Messenger, LLC", false, "install.signal" };
        yield return new object?[] { "Zoom Workplace (64-bit)", "Zoom", false, "install.zoom" };
        yield return new object?[] { "VLC media player", "VideoLAN", false, "install.vlc" };
        yield return new object?[] { "K-Lite Codec Pack 20.1.0 Standard", "KLCP", false, "install.k-lite" };
        yield return new object?[] { "foobar2000 v2.26.1 (x64)", "Peter Pawlowski", false, "install.foobar2000" };
        yield return new object?[] { "Foxit PDF Reader", "Foxit Software Inc.", false, "install.foxit-reader" };
        yield return new object?[] { "LibreOffice 26.8.1.1", "The Document Foundation", false, "install.libreoffice" };
        yield return new object?[] { "Adobe Acrobat (64-bit)", "Adobe", false, "install.acrobat-reader" };
        yield return new object?[] { "SumatraPDF", "Krzysztof Kowalczyk", false, "install.sumatrapdf" };
        yield return new object?[] { "7-Zip 26.05 (x64)", "Igor Pavlov", false, "install.7zip" };
        yield return new object?[] { "Notepad++ (x64)", "Notepad++ team (MSI installer)", false, "install.notepadplusplus" };
        yield return new object?[] { "Notepad++ (64-bit x64)", "Notepad++ Team", false, "install.notepadplusplus" };
        yield return new object?[] { "Everything 1.4.1.1032 (x64)", "voidtools", false, "install.everything" };
        yield return new object?[] { "PowerToys (Preview) x64", "Microsoft Corporation", false, "install.powertoys" };
        yield return new object?[] { "PowerToys (Preview)", "Microsoft Corporation", true, "install.powertoys" };
        yield return new object?[] { "WinRAR 7.23 (64-bitowy)", "win.rar GmbH", false, "install.winrar" };
        yield return new object?[] { "ShareX", "ShareX Team", false, "install.sharex" };
        yield return new object?[] { "CPUID CPU-Z 3.01", "CPUID, Inc.", false, "install.cpu-z" };
        yield return new object?[] { "CPUID HWMonitor 1.68", "CPUID, Inc.", false, "install.hwmonitor" };
        yield return new object?[] { "TechPowerUp GPU-Z", "TechPowerUp", false, "install.gpu-z" };
        // Pomiar VM Pro 2026-10-26 (winget 8.54): HKLM 64 „HWiNFO® 64_is1”, DisplayName ze znakiem U+00AE.
        yield return new object?[] { "HWiNFO® 64", "Martin Malik, REALiX s.r.o.", false, "install.hwinfo" };

        // Spike RustDesk 2026-10-24 (Home, MSI 1.5.0): HKLM 64 {9D5F8A39-0D7B-4BC4-851D-DA8501D03DE7}, DisplayVersion 1.5.0.29846474.
        yield return new object?[] { "RustDesk", "Purslane Tech Pte. Ltd.", false, "install.rustdesk" };
        yield return new object?[] { "AnyDesk", "AnyDesk Software GmbH", false, "install.anydesk" };
        yield return new object?[] { "TeamViewer", "TeamViewer", false, "install.teamviewer" };
        yield return new object?[] { "Steam", "Valve Corporation", false, "install.steam" };
        yield return new object?[] { "Epic Games Launcher", "Epic Games, Inc.", false, "install.epic-games" };
        yield return new object?[] { "GOG GALAXY", "GOG.com", false, "install.gog-galaxy" };
        yield return new object?[] { "Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247", "Microsoft Corporation", false, "install.vcredist-x64" };
        yield return new object?[] { "Microsoft Visual C++ v14 Redistributable (x86) - 14.51.36247", "Microsoft Corporation", false, "install.vcredist-x86" };

        // Starsza nazwa wpisu tej samej rodziny 14.x (T11 Home: doinstalowana przez inny instalator serii) – M4 final review.
        yield return new object?[] { "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.51.36231", "Microsoft Corporation", false, "install.vcredist-x64" };
        yield return new object?[] { "Microsoft Visual C++ 2015-2022 Redistributable (x86) - 14.51.36231", "Microsoft Corporation", false, "install.vcredist-x86" };
        yield return new object?[] { "Microsoft Windows Desktop Runtime - 8.0.32 (x64)", "Microsoft Corporation", false, "install.dotnet-desktop-8" };
        yield return new object?[] { "Microsoft Windows Desktop Runtime - 8.0.32 (x64)", "Microsoft Corporation", true, "install.dotnet-desktop-8" };
        yield return new object?[] { "Microsoft Windows Desktop Runtime - 9.0.21 (x64)", "Microsoft Corporation", false, "install.dotnet-desktop-9" };

        // Świeży 26H2 (baseline Home i Pro) i składniki doinstalowane obok: żadna reguła nie pasuje.
        yield return new object?[] { "Microsoft Edge", "Microsoft Corporation", false, null };
        yield return new object?[] { "Microsoft Edge Update", null, false, null };
        yield return new object?[] { "Microsoft Edge WebView2 Runtime", null, false, null };
        yield return new object?[] { "Środowisko uruchomieniowe Microsoft Edge WebView2", "Microsoft Corporation", false, null };
        yield return new object?[] { "Mozilla Maintenance Service", "Mozilla", false, null };
        yield return new object?[] { "Microsoft GameInput", "Microsoft Corporation", false, null };
        yield return new object?[] { "Microsoft Windows Application Compatibility Fix Database", null, false, null };
        yield return new object?[] { "Microsoft Visual C++ 2022 X64 Minimum Runtime - 14.51.36247", "Microsoft Corporation", true, null };
        yield return new object?[] { "Microsoft Visual C++ 2022 X86 Additional Runtime - 14.51.36247", "Microsoft Corporation", true, null };
        yield return new object?[] { "Microsoft .NET Runtime - 8.0.32 (x64)", "Microsoft Corporation", true, null };
        yield return new object?[] { "Microsoft .NET Host FX Resolver - 9.0.21 (x64)", "Microsoft Corporation", true, null };
        yield return new object?[] { "PuTTY release 0.85 (64-bit)", "Simon Tatham", false, null };

        // Środowisko x86 nie może oznaczać pozycji x64 jako zainstalowanej (wpis zbudowany jak zmierzony x64).
        yield return new object?[] { "Microsoft Windows Desktop Runtime - 8.0.32 (x86)", "Microsoft Corporation", false, null };
        yield return new object?[] { "Microsoft Windows Desktop Runtime - 9.0.21 (x86)", "Microsoft Corporation", false, null };
    }

    /// <summary>
    /// Wpisy SYNTETYCZNE (nazwy na wzór pomiarów z README, nie z VM), które nie mogą dostać statusu „Zainstalowana”: fałszywe
    /// dopasowanie chowa pole wyboru i aplikacji nie da się zainstalować (M4, M12 final review).
    /// </summary>
    public static IEnumerable<object?[]> SyntheticNonMatchingEntries()
    {
        // Visual C++ spoza rodziny 14.x i składniki Minimum/Additional Runtime (M4).
        yield return new object?[] { "Microsoft Visual C++ 2013 Redistributable (x64) - 12.0.40664", "Microsoft Corporation" };
        yield return new object?[] { "Microsoft Visual C++ 2012 Redistributable (x64) - 11.0.61030", "Microsoft Corporation" };
        yield return new object?[] { "Microsoft Visual C++ 2010  x64 Redistributable - 10.0.40219", "Microsoft Corporation" };
        yield return new object?[] { "Microsoft Visual C++ 2022 X64 Minimum Runtime - 14.51.36231", "Microsoft Corporation" };
        yield return new object?[] { "Microsoft Visual C++ 2022 X86 Additional Runtime - 14.51.36231", "Microsoft Corporation" };

        // Warianty innych produktów o podobnej nazwie (M12).
        yield return new object?[] { "Opera GX Stable 117.0.5408.197", "Opera Software" };
        yield return new object?[] { "Mozilla Firefox ESR (x64 en-US)", "Mozilla" };
        yield return new object?[] { "Zoom Rooms", "Zoom" };
        yield return new object?[] { "K-Lite Codec Pack 18.6.0 Full", "KLCP" };

        // Płatny edytor tego samego wydawcy (winget: Foxit.PhantomPDF) nie może dać statusu Foxit PDF Reader.
        yield return new object?[] { "Foxit PDF Editor", "Foxit Software Inc." };
        yield return new object?[] { "Foxit PDF Editor Pro", "Foxit Software Inc." };

        // RustDesk: reguła DisplayName dokładnie „RustDesk” – podobna nazwa tego samego wydawcy nie daje statusu.
        yield return new object?[] { "RustDesk Client", "Purslane Tech Pte. Ltd." };
    }

    [Theory]
    [MemberData(nameof(SyntheticNonMatchingEntries))]
    public void BuiltIn_SyntheticSimilarEntry_MatchesNothing(string name, string publisher)
    {
        var app = Win32(name, publisher);

        Assert.DoesNotContain(BuiltIn().Entries, e => InstallStatus.IsInstalled(e, new[] { app }));
    }

    [Fact]
    public void BuiltIn_VcRedistX64RuleDoesNotMatchX86()
    {
        var x64 = BuiltIn().Entries.Single(e => e.Id == "install.vcredist-x64");

        Assert.False(InstallStatus.IsInstalled(x64, new[] { Win32("Microsoft Visual C++ 2015-2022 Redistributable (x86) - 14.51.36231", "Microsoft Corporation") }));
        Assert.False(InstallStatus.IsInstalled(x64, new[] { Win32("Microsoft Visual C++ v14 Redistributable (x86) - 14.51.36247", "Microsoft Corporation") }));
    }

    [Theory]
    [MemberData(nameof(MeasuredUninstallEntries))]
    public void BuiltIn_MatchFromMeasuredUninstallEntry(string name, string? publisher, bool systemComponent, string? expectedId)
    {
        var app = Win32(name, publisher, systemComponent);

        var matching = BuiltIn().Entries.Where(e => InstallStatus.IsInstalled(e, new[] { app })).Select(e => e.Id).ToList();

        Assert.Equal(expectedId is null ? Array.Empty<string>() : new[] { expectedId }, matching);
    }

    [Theory]
    [InlineData("MSTeams")]
    [InlineData("NotepadPlusPlus")]
    [InlineData("WinRAR.ShellExtension")]
    [InlineData("AdobeAcrobatReaderCoreApp")]
    [InlineData("Microsoft.PowerToys.SparseApp")]
    public void BuiltIn_AppxPackagesFromMeasurement_MatchNothing(string package)
    {
        // Reguły listy są tylko Win32 (widoczny wpis Uninstall); pakiety AppX doinstalowane obok nie dają statusu.
        Assert.DoesNotContain(BuiltIn().Entries, e => InstallStatus.IsInstalled(e, new[] { Appx(package) }));
    }

    [Fact]
    public void Load_English_TranslatesSummaryWarning_TitleUnchanged()
    {
        var table = CatalogTranslationTests.DataTable(
            ("Odtwarzacz.", "Player."),
            ("Uwaga.", "Note."),
            ("VLC", "VLC player"));
        using (L.UseTable(table))
        using (L.Use(UiLanguage.English))
        {
            var vlc = Assert.Single(AppsInstallCatalog.Load(Wrap(GoodVlc)).Entries);

            Assert.Equal("Player.", vlc.Summary);
            Assert.Equal("Note.", vlc.Warning);
            Assert.Equal("VLC", vlc.Title);
            Assert.Equal("VideoLAN.VLC", vlc.WingetId);
            Assert.Equal("https://www.videolan.org/vlc/", vlc.Source);
        }
    }

    [Fact]
    public void Load_EnglishTooLongTranslation_DoesNotFailLoad()
    {
        var longEnglish = new string('e', 200);
        using (L.UseTable(CatalogTranslationTests.DataTable(("Archiwizator.", longEnglish))))
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(longEnglish, AppsInstallCatalog.Load(Wrap(Good7Zip)).Entries[0].Summary);
        }
    }
}
