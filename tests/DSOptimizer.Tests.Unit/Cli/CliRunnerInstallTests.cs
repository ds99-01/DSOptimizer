using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using DSOptimizer.Tests.Unit.Install;
using DSOptimizer.Tests.Unit.Support;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

/// <summary>
/// <c>--list-install</c>/<c>--install-apps</c>/<c>--search-apps</c> na fake'ach (nigdy prawdziwy winget): raport pozycji z kodem
/// szesnastkowo i logiem, kody 0/1/4/2, odmowa konta bez żadnego wywołania winget.
/// </summary>
public class CliRunnerInstallTests
{
    private const string UserSid = WingetAvailabilityTests.UserSid;
    private const string AdminSid = WingetAvailabilityTests.OtherSid;
    private const uint NoNetwork = 0x8A150107;

    private static readonly AppsInstallCatalog Catalog = InstallOrderTests.Catalog(
        ("install.7zip", "7zip.7zip", "tools", "7-Zip*"),
        ("install.vlc", "VideoLAN.VLC", "multimedia", "VLC media player"),
        ("install.vcredist", "Microsoft.VCRedist.2015+.x64", "runtimes", "Microsoft Visual C++*"));

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

    /// <summary>Lista z RustDeskiem z GitHuba (plan RustDesk, Task 4) i jedną pozycją winget.</summary>
    private static readonly AppsInstallCatalog RustDeskCatalog = AppsInstallCatalog.Load("""
        { "entries": [
          { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Zdalny pulpit.",
            "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
            "source": "https://github.com/rustdesk/rustdesk/releases" },
          { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiwizator.",
            "category": "tools", "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" }
        ] }
        """);

    private const string RustDeskHash = "624fe792ab1d76b35134dab84bff97a6bcd14043ffd090a2c6a2800f77580495";
    private const string RustDeskMsiUrl = "https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi";
    private const string RustDeskMsiLog = @"C:\dso-logs\winget\rustdesk-msi-1.log";

    private static string RustDeskRelease()
        => new JObject
        {
            ["tag_name"] = "1.5.0",
            ["prerelease"] = false,
            ["draft"] = false,
            ["assets"] = new JArray(new JObject
            {
                ["name"] = "rustdesk-1.5.0-x86_64.msi",
                ["size"] = 1000,
                ["digest"] = "sha256:" + RustDeskHash,
                ["browser_download_url"] = RustDeskMsiUrl,
            }),
        }.ToString(Newtonsoft.Json.Formatting.None);

    private sealed class Fixture
    {
        public Fixture(string processSid = UserSid, AppsInstallCatalog? catalog = null, WingetRun? version = null)
        {
            Engine.User.Sid = UserSid;
            Reader.Apps.Add(WingetAvailabilityTests.AppInstaller());
            Runner.Enqueue(version ?? FakeWingetRunner.Exit(0, "v1.11.510"));
            Web.ByteSteps.AddRange(new long[] { 0, 500, 1000 });
            Files.Hash = RustDeskHash;
            Msi.OnInstall = () => Reader.Apps.Add(Program("RustDesk") with { Publisher = "Purslane Tech Pte. Ltd." });
            Install = new CliInstallServices
            {
                Runner = Runner,
                Logs = Logs,
                Reader = Reader,
                Catalog = catalog ?? Catalog,
                ProcessSid = processSid,
                AccountName = sid => sid == UserSid ? "dso" : null,
                Sleep = Sleep,
                GitHub = new GitHubInstallServices { Web = Web, Signatures = Signatures, Msi = Msi, Files = Files },
            };
        }

        /// <summary>HTTP w pamięci: bez <see cref="FakeWebDownloader.EnqueueText"/> każde zapytanie = brak sieci.</summary>
        public FakeWebDownloader Web { get; } = new();

        public FakeSignatureVerifier Signatures { get; } = new();

        public FakeMsiInstaller Msi { get; } = new();

        public FakeDownloadFiles Files { get; } = new();

        public EngineFixture Engine { get; } = new();

        /// <summary>Odstępy fałszywego sleep odczytu zwrotnego (R16); prawdziwy <c>Thread.Sleep</c> przy stałym FakeClock = pętla bez końca.</summary>
        public List<TimeSpan> Sleeps { get; } = new();

        private void Sleep(TimeSpan span)
        {
            Sleeps.Add(span);
            Engine.Clock.UtcNow += span;
        }

        public FakeWingetRunner Runner { get; } = new();

        public FakeWingetLogs Logs { get; } = new();

        public FakeInstalledAppsReader Reader { get; } = new();

        public CliInstallServices Install { get; }

        /// <summary>Instalacja, po której program pojawia się na liście zainstalowanych.</summary>
        public Func<string, WingetRun> Installs(string displayName, int code = 0)
            => _ =>
            {
                Reader.Apps.Add(Program(displayName));
                return FakeWingetRunner.Exit(code);
            };

        public List<string> CallArguments => Runner.Calls.Select(c => c.Arguments).ToList();

        public CliReport Run(CliCommand command, IReadOnlyList<string>? ids = null, string? query = null)
            => Run(new CliOptions
            {
                Command = command,
                TweakId = string.Empty,
                OutPath = @"C:\o.json",
                InstallIds = ids ?? Array.Empty<string>(),
                SearchQuery = query,
            });

        public CliReport Run(CliOptions options)
            => CliRunner.Run(
                options,
                new CliCatalogResult(new LoadedCatalog(Array.Empty<TweakDefinition>(), "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>()),
                Engine.CreateServices(),
                Array.Empty<string>(),
                null,
                Install);
    }

    // --- --install-apps ---

    [Fact]
    public void InstallApps_NoNetwork_ItemsWithHexCodeAndLog_Exit1()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(NoNetwork));

        var report = f.Run(CliCommand.InstallApps, new[] { "install.7zip", "install.vlc" });

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(CliCommand.InstallApps, report.Command);
        Assert.Null(report.TweakId);
        Assert.Null(report.Outcome);
        Assert.Equal(WingetState.Ready, report.Winget!.State);
        Assert.Equal("v1.11.510", report.Winget.Version);
        Assert.Equal(InstallText.WingetReady("v1.11.510"), report.Winget.Message);
        Assert.Null(report.InstallRows);
        Assert.Null(report.SearchRows);
        var items = report.InstallItems!;
        Assert.Equal(2, items.Count);
        var zip = items[0];
        Assert.Equal(("install.7zip", "7zip.7zip", "Tytuł install.7zip"), (zip.Id, zip.WingetId, zip.Title));
        Assert.Equal(TweakOutcome.Failed, zip.Outcome);
        Assert.Equal("0x8A150107", zip.WingetCode);
        Assert.Equal(@"C:\dso-logs\winget\7zip.7zip-1.log", zip.LogPath);
        Assert.Contains("brak połączenia", zip.Message, StringComparison.Ordinal);
        Assert.False(zip.RequiresReboot);
        var vlc = items[1];
        Assert.Equal(TweakOutcome.Skipped, vlc.Outcome);
        Assert.Equal(InstallText.SkippedNoNetwork, vlc.Message);
        Assert.Null(vlc.WingetCode);
        Assert.Null(vlc.LogPath);
        Assert.Equal(2, f.Runner.Calls.Count); // --version + jedna instalacja; VLC nie startuje
        Assert.Single(f.Engine.Journal.Current.Entries);
        Assert.Equal(JournalEntry.AppInstallKind, f.Engine.Journal.Current.Entries[0].Kind);
        Assert.Empty(f.Engine.RestorePoints.Descriptions);

        var json = JObject.Parse(CliReportSerializer.Serialize(report));
        var first = json["installItems"]![0]!;
        Assert.Equal(
            new[] { "id", "source", "wingetId", "gitHub", "title", "outcome", "wingetCode", "msiCode", "message", "logPath", "requiresReboot", "version" },
            ((JObject)first).Properties().Select(p => p.Name));
        Assert.Equal("0x8A150107", (string)first["wingetCode"]!);
        Assert.Equal("Failed", (string)first["outcome"]!);
        Assert.Equal(JTokenType.Null, json["installItems"]![1]!["wingetCode"]!.Type);
        Assert.Equal("Ready", (string)json["winget"]!["state"]!);
        Assert.Equal(new[] { "state", "version", "message" }, ((JObject)json["winget"]!).Properties().Select(p => p.Name));
        Assert.Null(json["installRows"]);
        Assert.Null(json["searchRows"]);
    }

    [Fact]
    public void InstallApps_AllInstalledOrAlready_Exit0_RuntimesFirst()
    {
        var f = new Fixture();
        f.Reader.Apps.Add(Program("7-Zip 26.05 (x64)"));
        f.Runner
            .Enqueue(f.Installs("Microsoft Visual C++ 2015-2022 Redistributable (x64)"))
            .Enqueue(f.Installs("VLC media player"));

        var report = f.Run(CliCommand.InstallApps, new[] { "install.vlc", "install.7zip", "install.vcredist" });

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(new[] { "install.vcredist", "install.7zip", "install.vlc" }, report.InstallItems!.Select(i => i.Id));
        Assert.Equal(
            new[] { TweakOutcome.Installed, TweakOutcome.AlreadyInstalled, TweakOutcome.Installed },
            report.InstallItems!.Select(i => i.Outcome));
        Assert.Equal("0x00000000", report.InstallItems![0].WingetCode);
        Assert.Null(report.InstallItems[1].WingetCode); // dopasowane przed startem – winget nie ruszył (Review Focus 3)
        Assert.Equal(
            new[]
            {
                WingetArguments.Version(),
                WingetArguments.Install("Microsoft.VCRedist.2015+.x64", @"C:\dso-logs\winget\Microsoft.VCRedist.2015+.x64-1.log"),
                WingetArguments.Install("VideoLAN.VLC", @"C:\dso-logs\winget\VideoLAN.VLC-2.log"),
            },
            f.CallArguments);
        Assert.False(report.RequiresReboot);
        Assert.Null(report.Message);
    }

    [Fact]
    public void InstallApps_RebootRequiredToFinish_ReportRequiresReboot()
    {
        var f = new Fixture();
        f.Runner.Enqueue(f.Installs("VLC media player", unchecked((int)0x8A150109)));

        var report = f.Run(CliCommand.InstallApps, new[] { "install.vlc" });

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.True(report.InstallItems![0].RequiresReboot);
        Assert.True(report.RequiresReboot);
        Assert.Equal("0x8A150109", report.InstallItems[0].WingetCode);
    }

    [Fact]
    public void InstallApps_NotVisibleAfterSuccess_Exit4()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0));

        var report = f.Run(CliCommand.InstallApps, new[] { "install.vlc" });

        Assert.Equal(CliExitCode.Incomplete, report.Result);
        Assert.Equal(4, report.ExitCode);
        Assert.Equal(TweakOutcome.NotConfirmed, report.InstallItems![0].Outcome);
        Assert.Equal(60, f.Sleeps.Count); // okno odczytu zwrotnego 60 s co 1 s (R16) przez sleep wstrzyknięty w CLI
        Assert.All(f.Sleeps, s => Assert.Equal(TimeSpan.FromSeconds(1), s));
    }

    /// <summary>Ruling R21: nieznany kod ≠ 0 przy programie widocznym na liście = NotConfirmed i kod 4 (jak „sukces bez dowodu”).</summary>
    [Fact]
    public void InstallApps_UnknownCodeButVisible_NotConfirmed_Exit4()
    {
        var f = new Fixture();
        f.Runner.Enqueue(f.Installs("VLC media player", unchecked((int)0x80004004)));

        var report = f.Run(CliCommand.InstallApps, new[] { "install.vlc" });

        Assert.Equal(CliExitCode.Incomplete, report.Result);
        Assert.Equal(4, report.ExitCode);
        var item = Assert.Single(report.InstallItems!);
        Assert.Equal(TweakOutcome.NotConfirmed, item.Outcome);
        Assert.Equal("0x80004004", item.WingetCode);
        Assert.Contains("ale program jest widoczny na liście zainstalowanych", item.Message, StringComparison.Ordinal);
        Assert.Empty(f.Sleeps);
    }

    [Fact]
    public void InstallApps_SearchTargetUnverified_Exit4()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0)).Enqueue(FakeWingetRunner.Exit(1));

        var report = f.Run(CliCommand.InstallApps, new[] { "winget:Vendor.Tool" });

        Assert.Equal(CliExitCode.Incomplete, report.Result);
        var item = Assert.Single(report.InstallItems!);
        Assert.Equal(("winget:Vendor.Tool", "Vendor.Tool", "Vendor.Tool"), (item.Id, item.WingetId, item.Title));
        Assert.Equal(TweakOutcome.InstalledUnverified, item.Outcome);
        Assert.Equal(WingetArguments.ListExact("Vendor.Tool"), f.CallArguments.Last());
    }

    [Fact]
    public void InstallApps_FailedWinsOverNotConfirmed_Exit1()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0)).Enqueue(FakeWingetRunner.Exit(0x8A150011));

        var report = f.Run(CliCommand.InstallApps, new[] { "install.7zip", "install.vlc" });

        Assert.Equal(new[] { TweakOutcome.NotConfirmed, TweakOutcome.Failed }, report.InstallItems!.Select(i => i.Outcome));
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
    }

    /// <summary>Review Focus 5: inne konto niż zalogowane = każda pozycja Refused z kodem 1, a fake runnera nie dostał żadnego wywołania (także --version).</summary>
    [Fact]
    public void InstallApps_OtherAccount_AllRefused_NoWingetCall_Exit1()
    {
        var f = new Fixture(processSid: AdminSid);

        var report = f.Run(CliCommand.InstallApps, new[] { "install.7zip", "winget:Vendor.Tool" });

        Assert.Empty(f.Runner.Calls);
        Assert.Empty(f.Logs.Calls);
        Assert.Empty(f.Engine.Journal.Current.Entries);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(WingetState.OtherAccount, report.Winget!.State);
        Assert.Null(report.Winget.Version);
        Assert.All(report.InstallItems!, item =>
        {
            Assert.Equal(TweakOutcome.Refused, item.Outcome);
            Assert.Contains("uruchom DSOptimizer na koncie dso", item.Message, StringComparison.Ordinal);
        });
        Assert.Contains("uruchom DSOptimizer na koncie dso", report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallApps_UnknownInstallId_UsageError2_NothingRuns()
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.InstallApps, new[] { "install.7zip", "install.nie-ma" });

        Assert.Equal(CliExitCode.UsageError, report.Result);
        Assert.Equal(2, report.ExitCode);
        Assert.Contains("install.nie-ma", Assert.Single(report.Errors), StringComparison.Ordinal);
        Assert.Null(report.InstallItems);
        Assert.Empty(f.Runner.Calls);
        Assert.Empty(f.Reader.ReadSids);
        Assert.Empty(f.Engine.Journal.Current.Entries);
    }

    [Fact]
    public void InstallApps_WingetIdOfCatalogEntry_UsesCatalogEntryWithMatch()
    {
        var f = new Fixture();
        f.Reader.Apps.Add(Program("7-Zip 26.05 (x64)"));

        var report = f.Run(CliCommand.InstallApps, new[] { "winget:7ZIP.7zip" });

        Assert.Equal(CliExitCode.Success, report.Result);
        var item = Assert.Single(report.InstallItems!);
        Assert.Equal("install.7zip", item.Id);
        Assert.Equal(TweakOutcome.AlreadyInstalled, item.Outcome);
        Assert.Equal(new[] { WingetArguments.Version() }, f.CallArguments);
    }

    [Fact]
    public void InstallApps_SamePackageTwiceViaListAndWinget_UsageError2()
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.InstallApps, new[] { "install.7zip", "winget:7zip.7zip" });

        Assert.Equal(CliExitCode.UsageError, report.Result);
        Assert.Contains("7zip.7zip", Assert.Single(report.Errors), StringComparison.Ordinal);
        Assert.Empty(f.Runner.Calls);
    }

    /// <summary>Obrona w głąb Review Focus 1: Id z „…” podane z pominięciem parsera też nie uruchamia winget.</summary>
    [Theory]
    [InlineData("winget:Microsoft.VisualStudioCode.Ins…")]
    [InlineData("Microsoft.VisualStudioCode")]
    public void InstallApps_InvalidIdBypassingParser_UsageError2(string id)
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.InstallApps, new[] { id });

        Assert.Equal(CliExitCode.UsageError, report.Result);
        Assert.Empty(f.Runner.Calls);
        Assert.Empty(f.Reader.ReadSids);
    }

    [Fact]
    public void InstallApps_EmptyIdListBypassingParser_UsageError2()
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.InstallApps, Array.Empty<string>());

        Assert.Equal(CliExitCode.UsageError, report.Result);
        Assert.Empty(f.Runner.Calls);
    }

    // --- RustDesk z GitHuba (plan RustDesk, Task 4) ---

    [Fact]
    public void InstallApps_RustDesk_ItemWithMsiCodeNoWingetCode()
    {
        var f = new Fixture(catalog: RustDeskCatalog);
        f.Web.EnqueueText(FakeWebDownloader.Ok(RustDeskRelease()));

        var report = f.Run(CliCommand.InstallApps, new[] { "install.rustdesk" });

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(0, report.ExitCode);
        var item = Assert.Single(report.InstallItems!);
        Assert.Equal(("install.rustdesk", "github", (string?)null, (string?)"rustdesk/rustdesk"), (item.Id, item.Source, item.WingetId, item.GitHub));
        Assert.Equal(TweakOutcome.Installed, item.Outcome);
        Assert.Equal(0, item.MsiCode);
        Assert.Null(item.WingetCode);
        Assert.Equal(RustDeskMsiLog, item.LogPath);
        Assert.Equal("1.5.0", item.Version);
        Assert.Equal("Zainstalowano i sprawdzono.", item.Message);
        Assert.Equal(new[] { WingetArguments.Version() }, f.CallArguments);
        Assert.Equal(new[] { @"C:\dso-downloads\1.msi" }, f.Files.Deleted);

        var json = JObject.Parse(CliReportSerializer.Serialize(report));
        var first = (JObject)json["installItems"]![0]!;
        Assert.Equal(
            new[] { "id", "source", "gitHub", "title", "outcome", "wingetCode", "msiCode", "message", "logPath", "requiresReboot", "version" },
            first.Properties().Select(p => p.Name));
        Assert.Equal("github", (string)first["source"]!);
        Assert.Equal("rustdesk/rustdesk", (string)first["gitHub"]!);
        Assert.Equal(0, (int)first["msiCode"]!);
        Assert.Equal(JTokenType.Null, first["wingetCode"]!.Type);
    }

    /// <summary>Kod msiexec dziesiętnie (decyzja planu 10); 3010 = restart w raporcie pozycji i całej serii.</summary>
    [Fact]
    public void InstallApps_RustDeskMsi3010_MsiCodeDecimal_RequiresReboot()
    {
        var f = new Fixture(catalog: RustDeskCatalog);
        f.Web.EnqueueText(FakeWebDownloader.Ok(RustDeskRelease()));
        f.Msi.Result = new MsiRun(3010, false);

        var report = f.Run(CliCommand.InstallApps, new[] { "install.rustdesk" });

        var item = Assert.Single(report.InstallItems!);
        Assert.Equal(TweakOutcome.Installed, item.Outcome);
        Assert.Equal(3010, item.MsiCode);
        Assert.True(item.RequiresReboot);
        Assert.True(report.RequiresReboot);
        Assert.Equal(3010, (int)JObject.Parse(CliReportSerializer.Serialize(report))["installItems"]![0]!["msiCode"]!);
    }

    /// <summary>msiexec ≠ 0/3010/1641 (M6 final review): Failed, <c>msiCode</c> dziesiętnie, bez <c>wingetCode</c>, tekst z kodem i logiem MSI, kod wyjścia 1.</summary>
    [Fact]
    public void InstallApps_RustDeskMsi1603_FailedExit1MsiCode()
    {
        var f = new Fixture(catalog: RustDeskCatalog);
        f.Web.EnqueueText(FakeWebDownloader.Ok(RustDeskRelease()));
        f.Msi.Result = new MsiRun(1603, false);
        f.Msi.OnInstall = null;

        var report = f.Run(CliCommand.InstallApps, new[] { "install.rustdesk" });

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        var item = Assert.Single(report.InstallItems!);
        Assert.Equal(("github", TweakOutcome.Failed), (item.Source, item.Outcome));
        Assert.Equal(1603, item.MsiCode);
        Assert.Null(item.WingetCode);
        Assert.Contains("kodem 1603", item.Message, StringComparison.Ordinal);
        Assert.Contains("Log MSI:", item.Message, StringComparison.Ordinal);
        Assert.Equal(RustDeskMsiLog, item.LogPath);
        Assert.False(item.RequiresReboot);

        var first = (JObject)JObject.Parse(CliReportSerializer.Serialize(report))["installItems"]![0]!;
        Assert.Equal("Failed", (string)first["outcome"]!);
        Assert.Equal(1603, (int)first["msiCode"]!);
        Assert.Equal(JTokenType.Null, first["wingetCode"]!.Type);
    }

    /// <summary>Duplikat po <c>PackageKey</c> (<c>github:rustdesk/rustdesk</c>) z pominięciem parsera: kod 2, nic nie startuje.</summary>
    [Fact]
    public void InstallApps_RustDeskTwice_UsageError()
    {
        var f = new Fixture(catalog: RustDeskCatalog);

        var report = f.Run(CliCommand.InstallApps, new[] { "install.rustdesk", "install.rustdesk" });

        Assert.Equal(CliExitCode.UsageError, report.Result);
        Assert.Equal(2, report.ExitCode);
        Assert.Contains("github:rustdesk/rustdesk", Assert.Single(report.Errors), StringComparison.Ordinal);
        Assert.Null(report.InstallItems);
        Assert.Empty(f.Runner.Calls);
        Assert.Empty(f.Reader.ReadSids);
        Assert.Empty(f.Web.Calls);
        Assert.Empty(f.Msi.Calls);
        Assert.Empty(f.Engine.Journal.Current.Entries);
    }

    /// <summary>
    /// Fala poprawek F13 (spec 11.2, Ruling R5): alias winget RustDeska (<see cref="GitHubSources.WingetAliases"/>) to nie druga droga
    /// instalacji – błędne argumenty (kod 2), nic nie startuje; także z <c>install.rustdesk</c> obok i w innej wielkości liter.
    /// </summary>
    [Theory]
    [InlineData("winget:RustDesk.RustDesk")]
    [InlineData("winget:rustdesk.RUSTDESK")]
    public void InstallApps_RustDeskWingetAlias_UsageError2_NothingRuns(string id)
    {
        foreach (var ids in new[] { new[] { id }, new[] { "install.rustdesk", id } })
        {
            var f = new Fixture(catalog: RustDeskCatalog);

            var report = f.Run(CliCommand.InstallApps, ids);

            Assert.Equal(CliExitCode.UsageError, report.Result);
            Assert.Equal(2, report.ExitCode);
            Assert.Equal("RustDesk instaluje się z listy: użyj install.rustdesk.", Assert.Single(report.Errors));
            Assert.Null(report.InstallItems);
            Assert.Empty(f.Runner.Calls);
            Assert.Empty(f.Reader.ReadSids);
            Assert.Empty(f.Web.Calls);
            Assert.Empty(f.Msi.Calls);
            Assert.Empty(f.Engine.Journal.Current.Entries);
        }
    }

    /// <summary>Zepsuty winget (decyzja planu 1/3): RustDesk instaluje się z GitHuba, 7-Zip = Refused, winget dostał tylko --version.</summary>
    [Fact]
    public void InstallApps_WingetBroken_RustDeskInstalled_7zipRefused()
    {
        var f = new Fixture(catalog: RustDeskCatalog, version: FakeWingetRunner.Exit(5));
        f.Web.EnqueueText(FakeWebDownloader.Ok(RustDeskRelease()));

        var report = f.Run(CliCommand.InstallApps, new[] { "install.7zip", "install.rustdesk" });

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(WingetState.NotWorking, report.Winget!.State);
        Assert.Equal(report.Winget.Message, report.Message);
        Assert.DoesNotContain(f.CallArguments, a => a.StartsWith("install", StringComparison.Ordinal));
        Assert.Equal(new[] { WingetArguments.Version() }, f.CallArguments);
        var items = report.InstallItems!;
        Assert.Equal(new[] { "install.rustdesk", "install.7zip" }, items.Select(i => i.Id));
        Assert.Equal((TweakOutcome.Installed, "github", (int?)0), (items[0].Outcome, items[0].Source, items[0].MsiCode));
        Assert.Equal((TweakOutcome.Refused, "winget", (string?)"7zip.7zip"), (items[1].Outcome, items[1].Source, items[1].WingetId));
        Assert.Null(items[1].GitHub);
        Assert.Null(items[1].MsiCode);
        Assert.Null(items[1].WingetCode);
        Assert.Equal(report.Winget.Message, items[1].Message);

        var json = JObject.Parse(CliReportSerializer.Serialize(report));
        var zip = (JObject)json["installItems"]![1]!;
        Assert.Equal(
            new[] { "id", "source", "wingetId", "gitHub", "title", "outcome", "wingetCode", "msiCode", "message", "logPath", "requiresReboot", "version" },
            zip.Properties().Select(p => p.Name));
        Assert.Equal("winget", (string)zip["source"]!);
        Assert.Equal(JTokenType.Null, zip["gitHub"]!.Type);
        Assert.Equal(JTokenType.Null, zip["msiCode"]!.Type);
    }

    /// <summary>Brak sieci przy API GitHuba (spec 3.7): „brak połączenia z GitHubem”, seria się kończy, nic nie pobrane, kod 1.</summary>
    [Fact]
    public void InstallApps_RustDeskNoNetwork_Exit1_MessageNoGitHub()
    {
        var f = new Fixture(catalog: RustDeskCatalog);

        var report = f.Run(CliCommand.InstallApps, new[] { "install.7zip", "install.rustdesk" });

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(1, report.ExitCode);
        var items = report.InstallItems!;
        Assert.Equal(new[] { "install.rustdesk", "install.7zip" }, items.Select(i => i.Id));
        Assert.Equal((TweakOutcome.Failed, InstallText.GitHubNoConnection), (items[0].Outcome, items[0].Message));
        Assert.Equal("Nie wykonano – brak połączenia z GitHubem", items[0].Message); // spec 3.7, dosłownie
        Assert.Equal(("github", (int?)null, (string?)null), (items[0].Source, items[0].MsiCode, items[0].WingetCode));
        Assert.Equal((TweakOutcome.Skipped, InstallText.SkippedNoInternet), (items[1].Outcome, items[1].Message));
        Assert.Equal(new[] { "GetText " + RustDeskSource.ReleasesLatestUrl }, f.Web.Calls);
        Assert.Empty(f.Files.Paths);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { WingetArguments.Version() }, f.CallArguments);
    }

    /// <summary>Host bez usług GitHuba (null): pozycja RustDesk = Failed „niedostępna w tym uruchomieniu”, bez HTTP.</summary>
    [Fact]
    public void InstallApps_RustDeskWithoutGitHubServices_FailedUnavailable()
    {
        var f = new Fixture(catalog: RustDeskCatalog);
        var services = new CliInstallServices
        {
            Runner = f.Install.Runner,
            Logs = f.Install.Logs,
            Reader = f.Install.Reader,
            Catalog = f.Install.Catalog,
            ProcessSid = f.Install.ProcessSid,
            AccountName = f.Install.AccountName,
            Sleep = f.Install.Sleep,
        };

        var report = CliRunner.Run(
            new CliOptions { Command = CliCommand.InstallApps, TweakId = string.Empty, OutPath = @"C:\o.json", InstallIds = new[] { "install.rustdesk" } },
            new CliCatalogResult(new LoadedCatalog(Array.Empty<TweakDefinition>(), "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>()),
            f.Engine.CreateServices(),
            Array.Empty<string>(),
            null,
            services);

        Assert.Null(services.GitHub);
        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal((TweakOutcome.Failed, InstallText.GitHubUnavailable), (report.InstallItems![0].Outcome, report.InstallItems[0].Message));
        Assert.Empty(f.Web.Calls);
    }

    [Fact]
    public void ListInstall_RustDeskRow_SourceGitHub_NoWingetId()
    {
        var f = new Fixture(catalog: RustDeskCatalog);
        f.Reader.Apps.Add(Program("RustDesk") with { Publisher = "Purslane Tech Pte. Ltd." });

        var report = f.Run(CliCommand.ListInstall);

        Assert.Equal(CliExitCode.Success, report.Result);
        var rows = report.InstallRows!;
        Assert.Equal(
            ("install.rustdesk", "github", (string?)null, (string?)"rustdesk/rustdesk", true),
            (rows[0].Id, rows[0].Source, rows[0].WingetId, rows[0].GitHub, rows[0].Installed));
        Assert.Equal(InstallCategory.RemoteHelp, rows[0].Category);
        Assert.Equal(
            ("install.7zip", "winget", (string?)"7zip.7zip", (string?)null, false),
            (rows[1].Id, rows[1].Source, rows[1].WingetId, rows[1].GitHub, rows[1].Installed));
        Assert.Empty(f.Web.Calls);

        var json = JObject.Parse(CliReportSerializer.Serialize(report));
        var rustDesk = (JObject)json["installRows"]![0]!;
        Assert.Equal(new[] { "id", "source", "gitHub", "title", "category", "installed" }, rustDesk.Properties().Select(p => p.Name));
        Assert.Equal("github", (string)rustDesk["source"]!);
        Assert.Equal("rustdesk/rustdesk", (string)rustDesk["gitHub"]!);
        Assert.Null(rustDesk["wingetId"]);
        var zip = (JObject)json["installRows"]![1]!;
        Assert.Equal(new[] { "id", "source", "wingetId", "gitHub", "title", "category", "installed" }, zip.Properties().Select(p => p.Name));
        Assert.Equal("winget", (string)zip["source"]!);
        Assert.Equal(JTokenType.Null, zip["gitHub"]!.Type);
    }

    // --- --list-install ---

    [Fact]
    public void ListInstall_InstalledFlags_ReadyWinget_Exit0()
    {
        var f = new Fixture();
        f.Reader.Apps.Add(Program("7-Zip 26.05 (x64)"));
        f.Reader.Warnings.Add("Nie udało się odczytać programów z rejestru. x");

        var report = f.Run(CliCommand.ListInstall);

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(CliCommand.ListInstall, report.Command);
        Assert.Equal(new[] { WingetArguments.Version() }, f.CallArguments);
        Assert.Equal(UserSid, Assert.Single(f.Reader.ReadSids));
        Assert.Equal(WingetState.Ready, report.Winget!.State);
        Assert.Contains("Nie udało się odczytać programów z rejestru. x", report.Warnings);
        Assert.Null(report.InstallItems);
        var rows = report.InstallRows!;
        Assert.Equal(new[] { "install.7zip", "install.vlc", "install.vcredist" }, rows.Select(r => r.Id));
        var zip = rows[0];
        Assert.Equal(("7zip.7zip", "Tytuł install.7zip", InstallCategory.Tools, true), (zip.WingetId, zip.Title, zip.Category, zip.Installed));
        Assert.False(rows[1].Installed);
        Assert.Equal(InstallCategory.Runtimes, rows[2].Category);
        Assert.Empty(f.Engine.Journal.Current.Entries);

        var json = JObject.Parse(CliReportSerializer.Serialize(report));
        var row = (JObject)json["installRows"]![0]!;
        Assert.Equal(new[] { "id", "source", "wingetId", "gitHub", "title", "category", "installed" }, row.Properties().Select(p => p.Name));
        Assert.Equal("winget", (string)row["source"]!);
        Assert.Equal("Tools", (string)row["category"]!);
        Assert.True((bool)row["installed"]!);
    }

    [Fact]
    public void ListInstall_OtherAccount_StateInReport_NoWingetCall_Exit0()
    {
        var f = new Fixture(processSid: AdminSid);
        f.Reader.Apps.Add(Program("VLC media player"));

        var report = f.Run(CliCommand.ListInstall);

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Empty(f.Runner.Calls);
        Assert.Equal(WingetState.OtherAccount, report.Winget!.State);
        Assert.Equal(InstallText.OtherAccount("dso"), report.Winget.Message);
        Assert.Equal(new[] { false, true, false }, report.InstallRows!.Select(r => r.Installed));
    }

    [Fact]
    public void ListInstall_UserUncertain_StateInReport_NoWingetCall()
    {
        var f = new Fixture();
        f.Engine.User.IsCertain = false;

        var report = f.Run(CliCommand.ListInstall);

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Empty(f.Runner.Calls);
        Assert.Equal(WingetState.UserUncertain, report.Winget!.State);
    }

    [Fact]
    public void ListInstall_ReadFails_Exit1_WithReason()
    {
        var f = new Fixture();
        f.Reader.ReadThrows = new InvalidOperationException("rejestr niedostępny");

        var report = f.Run(CliCommand.ListInstall);

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Contains("rejestr niedostępny", Assert.Single(report.Errors), StringComparison.Ordinal);
        Assert.Equal(WingetState.NotWorking, report.Winget!.State);
        Assert.Null(report.InstallRows);
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void ListInstall_ReadFailsOnOtherAccount_AccountRefusalWins()
    {
        var f = new Fixture(processSid: AdminSid);
        f.Reader.ReadThrows = new InvalidOperationException("rejestr niedostępny");

        var report = f.Run(CliCommand.ListInstall);

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(WingetState.OtherAccount, report.Winget!.State);
    }

    // --- --search-apps ---

    [Fact]
    public void SearchApps_Rows_WithInstallable_Exit0()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0, InstallSamples.Lines("synthetic-search-truncated.txt").ToArray()));

        var report = f.Run(CliCommand.SearchApps, query: "visual studio");

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(CliCommand.SearchApps, report.Command);
        Assert.Equal(new[] { WingetArguments.Version(), WingetArguments.Search("visual studio") }, f.CallArguments);
        Assert.Equal(InstallTimeouts.Search, f.Runner.Calls[1].WaitLimit);
        var rows = report.SearchRows!;
        Assert.Equal(4, rows.Count);
        Assert.Equal(("Visual Studio Code", "Microsoft.VisualStudioCode", "1.95.0", true), (rows[0].Name, rows[0].WingetId, rows[0].Version, rows[0].Installable));
        Assert.Equal("Microsoft.VisualStudioCode.Ins…", rows[1].WingetId);
        Assert.False(rows[1].Installable);
        Assert.False(rows[3].Installable);
        Assert.Null(report.Message);
        Assert.Empty(f.Engine.Journal.Current.Entries);

        var json = JObject.Parse(CliReportSerializer.Serialize(report));
        var row = (JObject)json["searchRows"]![1]!;
        Assert.Equal(new[] { "name", "wingetId", "version", "installable" }, row.Properties().Select(p => p.Name));
        Assert.False((bool)row["installable"]!);
    }

    [Fact]
    public void SearchApps_RealSample_Exit0()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0, InstallSamples.Lines("spike-net-search-vlc.bin").ToArray()));

        var report = f.Run(CliCommand.SearchApps, query: "vlc");

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Equal(("VLC media player", "VideoLAN.VLC", true), (report.SearchRows![0].Name, report.SearchRows[0].WingetId, report.SearchRows[0].Installable));
    }

    [Fact]
    public void SearchApps_NoResultsCode_Exit0_EmptyRowsWithMessage()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A150014));

        var report = f.Run(CliCommand.SearchApps, query: "nic-takiego");

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Empty(report.SearchRows!);
        Assert.Equal(InstallText.SearchNoResults, report.Message);
    }

    [Fact]
    public void SearchApps_ZeroWithoutTable_Exit0_NoResultsMessage()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0, "Nie znaleziono pakietu."));

        var report = f.Run(CliCommand.SearchApps, query: "x");

        Assert.Equal(CliExitCode.Success, report.Result);
        Assert.Empty(report.SearchRows!);
        Assert.Equal(InstallText.SearchNoResults, report.Message);
    }

    [Fact]
    public void SearchApps_NoNetwork_Exit1()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A15000F, InstallSamples.Lines("spike-off-search-vlc.bin").ToArray()));

        var report = f.Run(CliCommand.SearchApps, query: "vlc");

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(InstallText.SearchNoNetwork, report.Message);
        Assert.Null(report.SearchRows);
    }

    [Fact]
    public void SearchApps_OtherCode_Exit1_WithHex()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A150002));

        var report = f.Run(CliCommand.SearchApps, query: "vlc");

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(InstallText.SearchFailed(unchecked((int)0x8A150002)), report.Message);
    }

    [Fact]
    public void SearchApps_Timeout_Exit1()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.TimedOut());

        var report = f.Run(CliCommand.SearchApps, query: "vlc");

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(InstallText.SearchTimedOut(InstallTimeouts.Search), report.Message);
    }

    [Fact]
    public void SearchApps_StartFails_Exit1()
    {
        var f = new Fixture();
        f.Runner.Enqueue(_ => throw new InvalidOperationException("brak aliasu"));

        var report = f.Run(CliCommand.SearchApps, query: "vlc");

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Equal(InstallText.SearchStartFailed("brak aliasu"), report.Message);
    }

    [Fact]
    public void SearchApps_OtherAccount_Refused_NoWingetCall_Exit1()
    {
        var f = new Fixture(processSid: AdminSid);

        var report = f.Run(CliCommand.SearchApps, query: "vlc");

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Empty(f.Runner.Calls);
        Assert.Equal(WingetState.OtherAccount, report.Winget!.State);
        Assert.Contains("uruchom DSOptimizer na koncie dso", report.Message, StringComparison.Ordinal);
        Assert.Null(report.SearchRows);
    }

    [Fact]
    public void SearchApps_ReadFails_Exit1_NoWingetCall()
    {
        var f = new Fixture();
        f.Reader.ReadThrows = new InvalidOperationException("rejestr niedostępny");

        var report = f.Run(CliCommand.SearchApps, query: "vlc");

        Assert.Equal(CliExitCode.OperationFailed, report.Result);
        Assert.Empty(f.Runner.Calls);
        Assert.Contains("rejestr niedostępny", report.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("vlc \"x\"")]
    [InlineData(null)]
    public void SearchApps_InvalidQueryBypassingParser_UsageError2_NoWingetCall(string? query)
    {
        var f = new Fixture();

        var report = f.Run(CliCommand.SearchApps, query: query);

        Assert.Equal(CliExitCode.UsageError, report.Result);
        Assert.Empty(f.Runner.Calls);
        Assert.Empty(f.Reader.ReadSids);
    }

    // --- wspólne ---

    [Fact]
    public void InstallCommandWithoutServices_Throws()
    {
        var engine = new EngineFixture();
        Assert.Throws<ArgumentNullException>(() => CliRunner.Run(
            new CliOptions { Command = CliCommand.ListInstall, TweakId = string.Empty, OutPath = "o.json" },
            new CliCatalogResult(new LoadedCatalog(Array.Empty<TweakDefinition>(), "sha256:abc"), CliCatalog.BuiltInSource, Array.Empty<string>()),
            engine.CreateServices(),
            Array.Empty<string>()));
    }

    [Fact]
    public void OtherCommandReport_HasNoInstallKeys()
    {
        var json = JObject.Parse(CliReportSerializer.Serialize(new CliReport { Command = CliCommand.Detect }));

        foreach (var key in new[] { "winget", "installItems", "installRows", "searchRows" })
        {
            Assert.Null(json[key]);
        }
    }

    /// <summary>Lista testowa przypadków VM (<c>--apps-catalog</c>) przechodzi walidację <c>apps.json</c>.</summary>
    [Fact]
    public void VmTestAppsFixture_LoadsAsAppsCatalog()
    {
        var list = AppsInstallCatalog.Load(File.ReadAllText(RepoRoot.Combine("tests", "vm", "fixtures", "vm-test-apps.json")));

        Assert.Equal(new[] { "install.dso-test-msi", "install.dso-test-absent" }, list.Entries.Select(e => e.Id));
        Assert.Equal(new[] { "Dso.TestMsi", "Dso.TestAbsent" }, list.Entries.Select(e => e.WingetId));
        Assert.All(list.Entries, e => Assert.Equal(InstallCategory.Tools, e.Category));
        Assert.True(InstallStatus.IsInstalled(list.Entries[0], new[] { Program("DSO Test MSI") }));
        Assert.False(InstallStatus.IsInstalled(list.Entries[1], new[] { Program("DSO Test MSI") }));
    }

    // --- CliExitCodes.ForInstall ---

    private static InstallItemReport Item(TweakOutcome outcome)
        => new("install.x", "X.Y", "X", outcome, null, "m", null, false, null);

    [Theory]
    [InlineData(new[] { TweakOutcome.Installed }, CliExitCode.Success)]
    [InlineData(new[] { TweakOutcome.Installed, TweakOutcome.AlreadyInstalled }, CliExitCode.Success)]
    [InlineData(new[] { TweakOutcome.Installed, TweakOutcome.NotConfirmed }, CliExitCode.Incomplete)]
    [InlineData(new[] { TweakOutcome.InstalledUnverified }, CliExitCode.Incomplete)]
    [InlineData(new[] { TweakOutcome.NotConfirmed, TweakOutcome.Skipped }, CliExitCode.OperationFailed)]
    [InlineData(new[] { TweakOutcome.Installed, TweakOutcome.Failed }, CliExitCode.OperationFailed)]
    [InlineData(new[] { TweakOutcome.Refused, TweakOutcome.Refused }, CliExitCode.OperationFailed)]
    [InlineData(new[] { TweakOutcome.InstalledUnverified, TweakOutcome.Skipped }, CliExitCode.OperationFailed)]
    public void ForInstall_MapsSeries(TweakOutcome[] outcomes, CliExitCode expected)
        => Assert.Equal(expected, CliExitCodes.ForInstall(outcomes.Select(Item).ToList()));

    [Fact]
    public void ForInstall_EmptySeries_Throws()
        => Assert.Throws<ArgumentException>(() => CliExitCodes.ForInstall(Array.Empty<InstallItemReport>()));

    [Fact]
    public void ForInstall_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => CliExitCodes.ForInstall(null!));

    [Theory]
    [InlineData(TweakOutcome.Applied)]
    [InlineData(TweakOutcome.Removed)]
    public void ForInstall_NonInstallOutcome_Throws(TweakOutcome outcome)
        => Assert.Throws<ArgumentOutOfRangeException>(() => CliExitCodes.ForInstall(new[] { Item(outcome) }));
}
