using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class InstallSessionTests
{
    private const string UserSid = WingetAvailabilityTests.UserSid;
    private const string AdminSid = WingetAvailabilityTests.OtherSid;
    private const string Busy = "Trwa inna operacja – nic nie zmieniono.";
    private const string SearchBusy = "Trwa inna operacja – spróbuj za chwilę.";

    private const string SeriesWarning =
        "Aplikacje zostaną pobrane z oficjalnego źródła winget (Microsoft) i zainstalowane bez okien instalatorów. Wymaga internetu. "
        + "Instalacja może potrwać kilka–kilkanaście minut.";

    private const string CatalogJson = """
        {
          "entries": [
            { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiwizer.", "category": "tools",
              "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" },
            { "id": "install.vcredist", "wingetId": "Microsoft.VCRedist.2015+.x64", "title": "Visual C++", "summary": "Biblioteki.",
              "category": "runtimes", "match": { "displayName": "Microsoft Visual C++*" }, "source": "https://example.com/vc" },
            { "id": "install.firefox", "wingetId": "Mozilla.Firefox", "title": "Firefox", "summary": "Przeglądarka.", "category": "browsers",
              "match": { "displayName": "Mozilla Firefox*" }, "warning": "Ustaw ją jako domyślną w Ustawieniach.",
              "source": "https://example.com/firefox" },
            { "id": "install.vlc", "wingetId": "VideoLAN.VLC", "title": "VLC", "summary": "Odtwarzacz.", "category": "multimedia",
              "match": { "displayName": "VLC media player" }, "source": "https://example.com/vlc" }
          ]
        }
        """;

    private static readonly AppsInstallCatalog Catalog = AppsInstallCatalog.Load(CatalogJson);

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["7zip.7zip"] = "7-Zip 26.05 (x64)",
        ["Microsoft.VCRedist.2015+.x64"] = "Microsoft Visual C++ 2015-2022 Redistributable (x64)",
        ["Mozilla.Firefox"] = "Mozilla Firefox (x64 pl)",
        ["VideoLAN.VLC"] = "VLC media player",
    };

    private static InstallTarget Entry(string id) => InstallTarget.FromEntry(Catalog.Find(id)!);

    private static InstalledApp Program(string name, string publisher = "Vendor")
        => new(
            @"win32:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + name,
            AppKind.Program,
            name,
            publisher,
            "1.0",
            AppScope.Machine,
            null,
            null,
            new Win32Info(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + name, @"C:\u.exe", null, false, null, null, false));

    private static string IdOf(string installArguments) => installArguments.Split(' ')[2];

    private sealed class Fixture
    {
        public Fixture(string processSid = UserSid, AppsInstallCatalog? catalog = null)
        {
            Engine.User.Sid = UserSid;
            Names.Names[UserSid] = "dso";
            Reader.Apps.Add(WingetAvailabilityTests.AppInstaller());
            Runner.Default = Dispatch;
            Gui = new GuiSession(new LoadedCatalog(new[] { TestTweaks.Scripted() }, "sha256:test"), Engine.CreateServices());

            // Pozycja z GitHuba (RustDesk) wyłącznie na fake'ach: wydanie z API, pobranie, skrót, podpis i msiexec w pamięci.
            Web.EnqueueText(FakeWebDownloader.Ok(AppInstallEngineGitHubTests.Release()));
            Web.ByteSteps.AddRange(new long[] { 0, 500, 1000 });
            Files.Hash = AppInstallEngineGitHubTests.Hash;
            Msi.OnInstall = () => Reader.Apps.Add(Program("RustDesk", "Purslane Tech Pte. Ltd."));
            var gitHub = new GitHubInstallServices { Web = Web, Signatures = Signatures, Msi = Msi, Files = Files };
            var engine = new AppInstallEngine(
                Runner, Logs, Reader, Engine.Journal, Engine.User, processSid, Names.NameOf, Engine.Log, Engine.Clock, _ => { }, gitHub);
            Session = new InstallSession(Gui, engine, catalog ?? Catalog, Reader, Runner, Engine.User, processSid, Names, Engine.Clock);
        }

        public FakeWebDownloader Web { get; } = new();

        public FakeSignatureVerifier Signatures { get; } = new();

        public FakeMsiInstaller Msi { get; } = new();

        public FakeDownloadFiles Files { get; } = new();

        /// <summary>Wynik <c>winget --version</c>; domyślnie kod 0 z numerem wersji (winget działa).</summary>
        public WingetRun VersionResult { get; set; } = FakeWingetRunner.Exit(0, "v1.11.510");

        public EngineFixture Engine { get; } = new();

        public FakeWingetRunner Runner { get; } = new();

        public FakeWingetLogs Logs { get; } = new();

        public FakeInstalledAppsReader Reader { get; } = new();

        public FakeAccountNames Names { get; } = new();

        public GuiSession Gui { get; }

        public InstallSession Session { get; }

        /// <summary>Wynik <c>winget search</c>; domyślnie kod 0 bez tabeli.</summary>
        public Func<string, WingetRun> SearchResult { get; set; } = _ => FakeWingetRunner.Exit(0);

        /// <summary>Wynik <c>winget install</c>; domyślnie program pojawia się na liście i kod 0.</summary>
        public Func<string, WingetRun>? InstallResult { get; set; }

        public List<string> CallsStarting(string prefix)
            => Runner.Calls.Where(c => c.Arguments.StartsWith(prefix, StringComparison.Ordinal)).Select(c => c.Arguments).ToList();

        public WingetRun AddsProgram(string arguments, int code = 0)
        {
            if (DisplayNames.TryGetValue(IdOf(arguments), out var name))
            {
                Reader.Apps.Add(Program(name));
            }

            return FakeWingetRunner.Exit(code);
        }

        private WingetRun Dispatch(string arguments)
        {
            if (arguments == "--version")
            {
                return VersionResult;
            }

            if (arguments.StartsWith("search ", StringComparison.Ordinal))
            {
                return SearchResult(arguments);
            }

            if (arguments.StartsWith("install ", StringComparison.Ordinal))
            {
                return (InstallResult ?? (args => AddsProgram(args)))(arguments);
            }

            return FakeWingetRunner.Exit(0);
        }
    }

    [Fact]
    public void Catalog_IsListOfPage()
    {
        Assert.Same(Catalog, new Fixture().Session.Catalog);
    }

    // --- Load ---

    [Fact]
    public void Load_PassesReaderWarnings()
    {
        var f = new Fixture();
        f.Reader.Warnings.Add("Nie udało się odczytać aplikacji ze Sklepu. test");
        f.Reader.Apps.Add(Program("VLC media player"));

        var state = f.Session.Load();

        Assert.Equal(new[] { "Nie udało się odczytać aplikacji ze Sklepu. test" }, state.Warnings);
        Assert.Equal(WingetState.Ready, state.Winget.State);
        Assert.False(state.ListReadFailed);
        Assert.Equal(new[] { "install.firefox", "install.vlc", "install.7zip", "install.vcredist" }, state.Rows.Select(row => row.Target.Id));
        Assert.True(state.Rows.Single(row => row.Target.Id == "install.vlc").IsInstalled);
        Assert.Equal(new[] { UserSid }, f.Reader.ReadSids);
        Assert.Equal(new[] { "--version" }, f.Runner.Calls.Select(c => c.Arguments));
    }

    [Fact]
    public void Load_OtherAccount_NoCheckboxes_NoWingetCall()
    {
        var f = new Fixture(processSid: AdminSid);

        var state = f.Session.Load();

        Assert.Equal(WingetState.OtherAccount, state.Winget.State);
        Assert.Equal(InstallText.OtherAccount("dso"), state.Winget.Text);
        Assert.All(state.Rows, row => Assert.False(row.CanSelect));
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void Load_ReadFails_NotWorkingWithReaderText_NoWingetCall()
    {
        // Kontrakt Tasku 5: błąd odczytu listy = NotWorking z tekstem odczytu, a nie „winget nie działa”; strona go zachowuje.
        var f = new Fixture();
        f.Reader.ReadThrows = new InvalidOperationException("brak dostępu");

        var state = f.Session.Load();

        Assert.Equal(WingetState.NotWorking, state.Winget.State);
        Assert.Equal(InstallText.ReadFailed("brak dostępu"), state.Winget.Text);
        Assert.True(state.ListReadFailed);
        Assert.Empty(state.Warnings);
        Assert.Equal(4, state.Rows.Count);
        Assert.All(state.Rows, row => Assert.False(row.CanSelect));
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void Load_ReadFails_OtherAccountWins()
    {
        var f = new Fixture(processSid: AdminSid);
        f.Reader.ReadThrows = new InvalidOperationException("brak dostępu");

        var state = f.Session.Load();

        Assert.Equal(WingetState.OtherAccount, state.Winget.State);
        Assert.True(state.ListReadFailed);
    }

    // --- Search ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("vlc\"")]
    [InlineData("a\tb")]
    [InlineData("a\u0001b")]
    [InlineData("--source msstore")]
    [InlineData(" -e")]
    public void Search_InvalidQuery_NoWingetCall(string query)
    {
        var f = new Fixture();

        var result = f.Session.Search(query);

        Assert.Empty(result.Rows);
        WingetArguments.TryValidateQuery(query, out var error);
        Assert.Equal(error, result.Message);
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void Search_TooLongQuery_NoWingetCall()
    {
        var f = new Fixture();

        var result = f.Session.Search(new string('a', WingetArguments.MaxQueryLength + 1));

        Assert.Equal("Zapytanie może mieć najwyżej 100 znaków.", result.Message);
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void Search_Found_RowsFromParser_CatalogDuplicateSkipped()
    {
        var f = new Fixture();
        f.Session.Load();
        f.SearchResult = _ => FakeWingetRunner.Exit(0, InstallSamples.Lines("spike-net-search-vlc.bin").ToArray());

        var result = f.Session.Search("  vlc ");

        Assert.Null(result.Message);
        Assert.DoesNotContain(result.Rows, row => string.Equals(row.Target.WingetId, "VideoLAN.VLC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Rows, row => row.Target.Id == "winget:VideoLAN.VLC.Nightly" && row.CanSelect);
        Assert.Equal(9, result.Rows.Count);
        var call = Assert.Single(f.Runner.Calls, c => c.Arguments.StartsWith("search ", StringComparison.Ordinal));
        Assert.Equal(WingetArguments.Search("vlc"), call.Arguments);
        Assert.Equal(InstallTimeouts.Search, call.WaitLimit);
    }

    [Fact]
    public void Search_AllFoundAlreadyOnList_Message()
    {
        var f = new Fixture();
        f.Session.Load();
        f.SearchResult = _ => FakeWingetRunner.Exit(0, "Name  Id            Version", "--------------------------", "VLC   VideoLAN.VLC  3.0");

        var result = f.Session.Search("vlc");

        Assert.Empty(result.Rows);
        Assert.Equal("Znalezione aplikacje są na liście – wyżej.", result.Message);
        Assert.Equal(new[] { "install.vlc" }, result.OnListIds);
    }

    [Fact]
    public void Search_NoResults_Message()
    {
        var f = new Fixture();
        f.Session.Load();
        f.SearchResult = _ => FakeWingetRunner.Exit(0x8A150014u, "No package found matching input criteria.");

        var result = f.Session.Search("nieistniejacy-program");

        Assert.Empty(result.Rows);
        Assert.Equal("Nic nie znaleziono w winget.", result.Message);
    }

    [Fact]
    public void Search_CodeZeroWithoutTable_NoResultsMessage()
    {
        var f = new Fixture();
        f.Session.Load();

        var result = f.Session.Search("cokolwiek");

        Assert.Empty(result.Rows);
        Assert.Equal("Nic nie znaleziono w winget.", result.Message);
    }

    [Fact]
    public void Search_Network_Message()
    {
        var f = new Fixture();
        f.Session.Load();
        f.SearchResult = _ => FakeWingetRunner.Exit(0x8A15000Fu, InstallSamples.Lines("spike-off-search-vlc.bin").ToArray());

        var result = f.Session.Search("vlc");

        Assert.Empty(result.Rows);
        Assert.Equal("Brak połączenia z internetem albo ze źródłem winget.", result.Message);
    }

    [Fact]
    public void Search_OtherCode_MessageWithHex()
    {
        var f = new Fixture();
        f.Session.Load();
        f.SearchResult = _ => FakeWingetRunner.Exit(0x8A150002u);

        var result = f.Session.Search("vlc");

        Assert.Equal("Wyszukiwanie nie powiodło się (kod 0x8A150002).", result.Message);
    }

    [Fact]
    public void Search_Timeout_Message()
    {
        var f = new Fixture();
        f.Session.Load();
        f.SearchResult = _ => FakeWingetRunner.TimedOut();

        var result = f.Session.Search("vlc");

        Assert.Equal("winget nie odpowiedział w 2 min.", result.Message);
    }

    [Fact]
    public void Search_StartThrows_Message()
    {
        var f = new Fixture();
        f.Session.Load();
        f.SearchResult = _ => throw new InvalidOperationException("brak pliku");

        var result = f.Session.Search("vlc");

        Assert.Empty(result.Rows);
        Assert.Equal("Nie udało się uruchomić winget: brak pliku", result.Message);
    }

    [Fact]
    public void Search_NotReady_MessageWithoutWinget()
    {
        var f = new Fixture(processSid: AdminSid);
        f.Session.Load();

        var result = f.Session.Search("vlc");

        Assert.Empty(result.Rows);
        Assert.Equal(InstallText.OtherAccount("dso"), result.Message);
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void Search_BeforeLoad_ChecksWingetFirst()
    {
        var f = new Fixture();
        f.SearchResult = _ => FakeWingetRunner.Exit(0, InstallSamples.Lines("spike-net-search-vlc.bin").ToArray());

        var result = f.Session.Search("vlc");

        Assert.NotEmpty(result.Rows);
        Assert.Equal(new[] { "--version", WingetArguments.Search("vlc") }, f.Runner.Calls.Select(c => c.Arguments));
    }

    [Fact]
    public async Task Search_Busy_Message()
    {
        var f = new Fixture();
        f.Session.Load();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => f.Gui.TryRunExclusive(
            () =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
                return 1;
            },
            out _));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));

        var result = f.Session.Search("vlc");

        release.Set();
        Assert.True(await holder);
        Assert.Empty(result.Rows);
        Assert.Equal(SearchBusy, result.Message);
        Assert.Empty(f.CallsStarting("search "));
    }

    // --- Potwierdzenie ---

    [Fact]
    public void Confirmation_ExactWarningAndOrder()
    {
        var f = new Fixture();
        var search = InstallTarget.FromSearch("Notepad++.Notepad++", "Notepad++");

        var confirmation = f.Session.ConfirmationFor(new[] { search, Entry("install.firefox"), Entry("install.vcredist"), Entry("install.7zip") });

        Assert.Equal(GuiOperation.InstallApps, confirmation.Operation);
        Assert.Equal("Zainstaluj zaznaczone", confirmation.Title);
        Assert.Equal("4 aplikacje", confirmation.Subtitle);
        Assert.Equal("Zainstaluj (4)", confirmation.PrimaryText);
        Assert.False(confirmation.CreatesRestorePoint);
        Assert.False(confirmation.RestorePointRequired);
        Assert.Equal(new[] { SeriesWarning }, confirmation.Warnings);
        Assert.Equal(
            new[]
            {
                new SeriesFact("Źródło", "winget (Microsoft)"),
                new SeriesFact("Punkt przywracania", "nie – instalację cofa się usunięciem na stronie „Aplikacje”"),
                new SeriesFact("Restart", "tylko gdy instalator go zgłosi"),
            },
            confirmation.Facts);
        Assert.Equal(
            new[] { "install.vcredist", "install.7zip", "install.firefox", "winget:Notepad++.Notepad++" },
            confirmation.Items.Select(item => item.Id));
        var firefox = confirmation.Items.Single(item => item.Id == "install.firefox");
        Assert.Equal("Firefox", firefox.Title);
        Assert.Equal("Przeglądarka.", firefox.Summary);
        Assert.Equal(new[] { "Ustaw ją jako domyślną w Ustawieniach." }, firefox.Warnings);
        Assert.Equal(new[] { new SeriesFact("Identyfikator winget", "Mozilla.Firefox") }, firefox.Technical);
        var notepad = confirmation.Items.Single(item => item.Id == "winget:Notepad++.Notepad++");
        Assert.Equal("Notepad++", notepad.Title);
        Assert.Equal("Wynik wyszukiwania w winget (spoza listy).", notepad.Summary);
        Assert.Empty(notepad.Warnings);
        Assert.All(confirmation.Items, item => Assert.Empty(item.Notes));
    }

    [Fact]
    public void Confirmation_EmptyOrInvalid_Throws()
    {
        var f = new Fixture();
        var truncated = new InstallTarget("winget:Vendor.Lo…", "Vendor.Lo…", "Lo", null, null, null);

        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(Array.Empty<InstallTarget>()));
        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(null!));
        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(new[] { truncated }));
    }

    // --- Instalacja ---

    [Fact]
    public void Install_SortedSeries_LinesRebootAndSucceededIds()
    {
        var f = new Fixture();
        f.InstallResult = args => IdOf(args) switch
        {
            "Mozilla.Firefox" => f.AddsProgram(args, unchecked((int)0x8A150109)),
            "VideoLAN.VLC" => FakeWingetRunner.Exit(0x8A150011u),
            _ => f.AddsProgram(args),
        };
        var steps = new ListProgress<BatchStepEvent>();
        var notepad = InstallTarget.FromSearch("Notepad++.Notepad++", "Notepad++");

        var report = f.Session.Install(new[] { notepad, Entry("install.vlc"), Entry("install.firefox"), Entry("install.vcredist") }, steps, null);

        Assert.Equal(GuiOperation.InstallApps, report.Operation);
        Assert.Equal("instalacja", report.TweakId);
        Assert.NotNull(report.Batch);
        Assert.Equal(
            new[] { "Microsoft.VCRedist.2015+.x64", "Mozilla.Firefox", "VideoLAN.VLC", "Notepad++.Notepad++" },
            f.CallsStarting("install ").Select(IdOf));
        Assert.All(f.CallsStarting("install "), args => Assert.DoesNotContain("--locale", args, StringComparison.Ordinal));
        Assert.Equal(
            new[] { "install.vcredist", "install.firefox", "winget:Notepad++.Notepad++" },
            report.SucceededIds);
        Assert.True(report.RequiresReboot);
        Assert.Contains(steps.Items, e => e.Phase == BatchStepPhase.Started && e.TweakId == "install.vcredist");
        Assert.Equal(
            new[]
            {
                (GuiLogLevel.Info, "Visual C++: Zainstalowano"),
                (GuiLogLevel.Info, "Firefox: Zainstalowano"),
                (GuiLogLevel.Error, "VLC: Nie wykonano – plik instalatora nie zgadza się z manifestem, nie zainstalowano. Kod 0x8A150011. Log winget: C:\\dso-logs\\winget\\VideoLAN.VLC-3.log"),
                (GuiLogLevel.Warning, "Notepad++: Zainstalowano (zgłoszone przez winget)"),
            },
            report.Lines.Select(line => (line.Level, line.Text)));
    }

    [Fact]
    public void Install_ReaderWarningsAndItemWarnings_InLines()
    {
        var f = new Fixture();
        f.Reader.Warnings.Add("Ostrzeżenie odczytu.");
        f.Engine.Journal.LoadWarning = "Journal był uszkodzony – odłożono go.";

        var report = f.Session.Install(new[] { Entry("install.7zip") }, null, null);

        Assert.Equal(
            new[]
            {
                (GuiLogLevel.Warning, "Ostrzeżenie odczytu."),
                (GuiLogLevel.Info, "7-Zip: Zainstalowano"),
                (GuiLogLevel.Warning, "Journal był uszkodzony – odłożono go."),
            },
            report.Lines.Select(line => (line.Level, line.Text)));
    }

    [Fact]
    public void Install_NotReady_RefusedLines()
    {
        var f = new Fixture(processSid: AdminSid);

        var report = f.Session.Install(new[] { Entry("install.7zip") }, null, null);

        Assert.Equal(TweakOutcome.Refused, Assert.Single(report.Batch!.Results).Outcome);
        Assert.Equal((GuiLogLevel.Warning, "7-Zip: Nie wykonano – " + InstallText.OtherAccount("dso")), (report.Lines.Single().Level, report.Lines.Single().Text));
        Assert.Empty(report.SucceededIds);
        Assert.False(report.RequiresReboot);
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void Install_StopBetweenItems_SkippedLine()
    {
        var f = new Fixture();
        var stop = new BatchStop();
        f.InstallResult = args =>
        {
            stop.Request();
            return f.AddsProgram(args);
        };

        var report = f.Session.Install(new[] { Entry("install.7zip"), Entry("install.vlc") }, null, stop);

        Assert.True(report.Batch!.Stopped);
        Assert.Equal((GuiLogLevel.Warning, "VLC: Nie wykonano – " + TweakEngine.StoppedMessage), (report.Lines[1].Level, report.Lines[1].Text));
    }

    [Fact]
    public void Install_InvalidTarget_RefusedNothingRun()
    {
        var f = new Fixture();
        var truncated = new InstallTarget("winget:Vendor.Lo…", "Vendor.Lo…", "Lo", null, null, null);

        var report = f.Session.Install(new[] { Entry("install.7zip"), truncated }, null, null);

        Assert.Null(report.Batch);
        Assert.Equal("Pozycji ze skróconą nazwą pakietu nie da się zainstalować – nic nie zmieniono.", Assert.Single(report.Lines).Text);
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void Install_Empty_Throws()
    {
        var f = new Fixture();

        Assert.Throws<ArgumentException>(() => f.Session.Install(Array.Empty<InstallTarget>(), null, null));
        Assert.Throws<ArgumentException>(() => f.Session.Install(null!, null, null));
    }

    [Fact]
    public async Task Install_HoldsLock_OtherOperationRefused()
    {
        var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.InstallResult = args =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
            return f.AddsProgram(args);
        };

        var install = Task.Run(() => f.Session.Install(new[] { Entry("install.7zip") }, null, null));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
        var tweaks = f.Gui.RunBatch(new[] { "test.script" }, GuiOperation.Apply, confirmed: true);
        var search = f.Session.Search("vlc");
        var second = f.Session.Install(new[] { Entry("install.vlc") }, null, null);
        release.Set();
        var report = await install;

        Assert.Equal(Busy, Assert.Single(tweaks.Lines).Text);
        Assert.Equal(SearchBusy, search.Message);
        Assert.Null(second.Batch);
        Assert.Equal(Busy, Assert.Single(second.Lines).Text);
        Assert.Equal(TweakOutcome.Installed, Assert.Single(report.Batch!.Results).Outcome);
        Assert.Equal(new[] { "7zip.7zip" }, f.CallsStarting("install ").Select(IdOf));
        Assert.Empty(f.CallsStarting("search "));
    }

    [Fact]
    public void Install_Timeout_LockReleased()
    {
        // Review Focus 4: limit minął, instalator dalej działa – pozycja „Nie potwierdzono”, reszta nieuruchomiona, blokada wolna.
        var f = new Fixture();
        f.InstallResult = _ => FakeWingetRunner.TimedOut();

        var report = f.Session.Install(new[] { Entry("install.7zip"), Entry("install.vlc") }, null, null);

        var results = report.Batch!.Results;
        Assert.Equal(TweakOutcome.NotConfirmed, results[0].Outcome);
        Assert.StartsWith("Nie potwierdzono – instalacja nadal trwa w tle", results[0].Message, StringComparison.Ordinal);
        Assert.Equal(TweakOutcome.Skipped, results[1].Outcome);
        Assert.Equal(InstallText.SkippedStillRunning, results[1].Message);
        Assert.Single(f.CallsStarting("install "));
        Assert.Equal(
            new[]
            {
                (GuiLogLevel.Warning, "7-Zip: Nie potwierdzono – instalacja nadal trwa w tle (program przestał czekać po 30 min)."),
                (GuiLogLevel.Warning, "VLC: Nie wykonano – poprzednia instalacja nadal trwa"),
            },
            report.Lines.Select(line => (line.Level, line.Text)));
        Assert.Empty(report.SucceededIds);
        Assert.True(f.Gui.TryRunExclusive(() => 1, out _));
        var tweaks = f.Gui.RunBatch(new[] { "test.script" }, GuiOperation.Apply, confirmed: true);
        Assert.DoesNotContain(tweaks.Lines, line => line.Text == Busy);
    }

    // --- RustDesk z GitHuba (plan RustDesk, Task 5) ---

    private const string GitHubWarning =
        "RustDesk zostanie pobrany i zainstalowany bez okna instalatora dopiero po sprawdzeniu skrótu SHA256 i podpisu wydawcy.";

    /// <summary>Ostrzeżenie pozycji RustDesk ze spec 4 (dosłownie, jak wpis Tasku 6).</summary>
    private const string RustDeskItemWarning =
        "Instaluje usługę RustDesk (działa w tle przy starcie Windows), dodaje reguły zapory, skrót na pulpicie i ikonę w Autostarcie wszystkich kont. "
        + "Instalator pobierany z GitHuba RustDesk i sprawdzany podpisem wydawcy (PURSLANE).";

    private static int Occurrences(IEnumerable<string> texts, string word)
        => texts.Sum(text => (text.Length - text.Replace(word, string.Empty).Length) / word.Length);

    /// <summary>Ostrzeżenia całego okna potwierdzenia: serii i pozycji.</summary>
    private static List<string> AllWarnings(SeriesConfirmation confirmation)
        => confirmation.Warnings.Concat(confirmation.Items.SelectMany(item => item.Warnings)).ToList();

    private static readonly AppsInstallCatalog GitHubCatalog = AppsInstallCatalog.Load("""
        { "entries": [
          { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiwizer.", "category": "tools",
            "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" },
          { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Zdalny pulpit.",
            "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
            "warning": "Instaluje usługę RustDesk (działa w tle przy starcie Windows), dodaje reguły zapory, skrót na pulpicie i ikonę w Autostarcie wszystkich kont. Instalator pobierany z GitHuba RustDesk i sprawdzany podpisem wydawcy (PURSLANE).",
            "source": "https://github.com/rustdesk/rustdesk/releases" }
        ] }
        """);

    private static InstallTarget GitHubEntry(string id) => InstallTarget.FromEntry(GitHubCatalog.Find(id)!);

    [Fact]
    public void Confirmation_RustDeskOnly_GitHubWarningOnly_RepositoryFact()
    {
        var f = new Fixture(catalog: GitHubCatalog);

        var confirmation = f.Session.ConfirmationFor(new[] { GitHubEntry("install.rustdesk") });

        Assert.Equal(new[] { GitHubWarning + " Wymaga internetu." }, confirmation.Warnings);
        Assert.Equal(new SeriesFact("Źródło", "GitHub RustDesk (rustdesk/rustdesk), podpis PURSLANE"), confirmation.Facts[0]);
        Assert.Equal(1, Occurrences(AllWarnings(confirmation), "Wymaga internetu"));
        Assert.Equal(1, Occurrences(AllWarnings(confirmation), "PURSLANE"));
        Assert.Equal(1, Occurrences(AllWarnings(confirmation), "GitHub"));
        Assert.Equal("1 aplikacja", confirmation.Subtitle);
        var item = Assert.Single(confirmation.Items);
        Assert.Equal(("install.rustdesk", "RustDesk", "Zdalny pulpit."), (item.Id, item.Title, item.Summary));
        Assert.Equal(new[] { RustDeskItemWarning }, item.Warnings);
        Assert.Equal(new[] { new SeriesFact("Repozytorium GitHub", "rustdesk/rustdesk") }, item.Technical);
    }

    [Fact]
    public void Confirmation_Mixed_BothWarningsBothSources()
    {
        var f = new Fixture(catalog: GitHubCatalog);

        var confirmation = f.Session.ConfirmationFor(new[] { GitHubEntry("install.rustdesk"), GitHubEntry("install.7zip") });

        Assert.Equal(new[] { SeriesWarning, GitHubWarning }, confirmation.Warnings);

        // M4: „Wymaga internetu.” raz (ostrzeżenie winget), GitHub i PURSLANE raz – w ostrzeżeniu pozycji RustDesk.
        Assert.Equal(1, Occurrences(AllWarnings(confirmation), "Wymaga internetu"));
        Assert.Equal(1, Occurrences(AllWarnings(confirmation), "PURSLANE"));
        Assert.Equal(1, Occurrences(AllWarnings(confirmation), "GitHub"));
        Assert.Equal(
            new SeriesFact("Źródło", "winget (Microsoft); GitHub RustDesk (rustdesk/rustdesk), podpis PURSLANE"),
            confirmation.Facts[0]);
        Assert.Equal(new[] { "install.7zip", "install.rustdesk" }, confirmation.Items.Select(item => item.Id));
        Assert.Equal(new[] { new SeriesFact("Identyfikator winget", "7zip.7zip") }, confirmation.Items[0].Technical);
        Assert.Equal(new[] { new SeriesFact("Repozytorium GitHub", "rustdesk/rustdesk") }, confirmation.Items[1].Technical);
    }

    [Fact]
    public void Confirmation_GitHubTargetOutsideAllowedList_Throws()
    {
        var f = new Fixture(catalog: GitHubCatalog);
        var forged = GitHubEntry("install.rustdesk") with { Id = "install.other" };
        var otherRepo = GitHubEntry("install.rustdesk") with { GitHub = "evil/rustdesk" };

        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(new[] { forged }));
        Assert.Throws<ArgumentException>(() => f.Session.ConfirmationFor(new[] { otherRepo }));
        Assert.Null(f.Session.Install(new[] { otherRepo }, null, null).Batch);
        Assert.Empty(f.Web.Calls);
    }

    [Fact]
    public void Load_ReadFails_GitHubNotSelectable()
    {
        var f = new Fixture(catalog: GitHubCatalog);
        f.Reader.ReadThrows = new InvalidOperationException("brak dostępu");

        var state = f.Session.Load();

        Assert.True(state.ListReadFailed);
        Assert.All(state.Rows, row => Assert.False(row.CanSelect));
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void Load_WingetNotWorking_OnlyRustDeskSelectable()
    {
        var f = new Fixture(catalog: GitHubCatalog) { VersionResult = FakeWingetRunner.Exit(1) };

        var state = f.Session.Load();

        Assert.Equal(WingetState.NotWorking, state.Winget.State);
        Assert.False(state.ListReadFailed);
        Assert.Equal(new[] { "install.rustdesk" }, state.Rows.Where(row => row.CanSelect).Select(row => row.Target.Id));
    }

    [Fact]
    public void Install_WingetNotWorking_RustDeskRuns()
    {
        var f = new Fixture(catalog: GitHubCatalog) { VersionResult = FakeWingetRunner.Exit(1) };

        var report = f.Session.Install(new[] { GitHubEntry("install.rustdesk"), GitHubEntry("install.7zip") }, null, null);

        var results = report.Batch!.Results;
        Assert.Equal(new[] { "install.7zip", "install.rustdesk" }, results.Select(result => result.TweakId));
        Assert.Equal(TweakOutcome.Refused, results[0].Outcome);
        Assert.Equal(TweakOutcome.Installed, results[1].Outcome);
        Assert.Equal(new[] { "install.rustdesk" }, report.SucceededIds);
        Assert.Empty(f.CallsStarting("install "));
        Assert.Single(f.Msi.Calls);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Info && line.Text == "RustDesk: Zainstalowano");
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var f = new Fixture();
        var engine = new AppInstallEngine(f.Runner, f.Logs, f.Reader, f.Engine.Journal, f.Engine.User, UserSid, f.Names.NameOf, f.Engine.Log, f.Engine.Clock);

        Assert.IsAssignableFrom<IInstallPageSession>(f.Session);
        Assert.Throws<ArgumentNullException>(() => new InstallSession(null!, engine, Catalog, f.Reader, f.Runner, f.Engine.User, UserSid, f.Names, f.Engine.Clock));
        Assert.Throws<ArgumentNullException>(() => new InstallSession(f.Gui, null!, Catalog, f.Reader, f.Runner, f.Engine.User, UserSid, f.Names, f.Engine.Clock));
        Assert.Throws<ArgumentNullException>(() => new InstallSession(f.Gui, engine, null!, f.Reader, f.Runner, f.Engine.User, UserSid, f.Names, f.Engine.Clock));
        Assert.Throws<ArgumentNullException>(() => new InstallSession(f.Gui, engine, Catalog, null!, f.Runner, f.Engine.User, UserSid, f.Names, f.Engine.Clock));
        Assert.Throws<ArgumentNullException>(() => new InstallSession(f.Gui, engine, Catalog, f.Reader, null!, f.Engine.User, UserSid, f.Names, f.Engine.Clock));
        Assert.Throws<ArgumentNullException>(() => new InstallSession(f.Gui, engine, Catalog, f.Reader, f.Runner, null!, UserSid, f.Names, f.Engine.Clock));
        Assert.Throws<ArgumentNullException>(() => new InstallSession(f.Gui, engine, Catalog, f.Reader, f.Runner, f.Engine.User, null!, f.Names, f.Engine.Clock));
        Assert.Throws<ArgumentNullException>(() => new InstallSession(f.Gui, engine, Catalog, f.Reader, f.Runner, f.Engine.User, UserSid, null!, f.Engine.Clock));
        Assert.Throws<ArgumentNullException>(() => new InstallSession(f.Gui, engine, Catalog, f.Reader, f.Runner, f.Engine.User, UserSid, f.Names, null!));
    }
}

