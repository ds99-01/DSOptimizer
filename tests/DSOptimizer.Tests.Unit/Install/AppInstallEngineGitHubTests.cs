using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Journal;
using DSOptimizer.Tests.Unit.Fakes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

/// <summary>Seria instalacji z pozycją RustDesk z GitHuba (plan RustDesk, Task 2) – wyłącznie fake'i: bez sieci, msiexec i winget.</summary>
public class AppInstallEngineGitHubTests
{
    private const string UserSid = WingetAvailabilityTests.UserSid;
    private const long Size = 1000;
    internal const string Hash = "624fe792ab1d76b35134dab84bff97a6bcd14043ffd090a2c6a2800f77580495";
    private const string MsiName = "rustdesk-1.5.0-x86_64.msi";
    private const string MsiUrl = "https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi";
    private const string Path1 = @"C:\dso-downloads\1.msi";
    private const string MsiLog = @"C:\dso-logs\winget\rustdesk-msi-1.log";
    private const string NoGitHub = "Nie wykonano – brak połączenia z GitHubem";
    private const string NoInternet = "Nie wykonano – brak połączenia z internetem";
    private const string CheckFailed = "Nie wykonano – pobrany plik nie przeszedł sprawdzenia (";
    private const string ReleaseRefusedPrefix = "Nie wykonano – wydanie RustDesk na GitHubie nie przeszło sprawdzenia (";

    private static readonly AppsInstallCatalog Catalog = AppsInstallCatalog.Load("""
        { "entries": [
          { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Zdalny pulpit.",
            "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
            "source": "https://github.com/rustdesk/rustdesk/releases" },
          { "id": "install.steam", "wingetId": "Valve.Steam", "title": "Steam", "summary": "Gry.",
            "category": "games", "match": { "displayName": "Steam" }, "source": "https://example.com/steam" },
          { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiwizator.",
            "category": "tools", "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" }
        ] }
        """);

    private static InstallTarget RustDesk => InstallTarget.FromEntry(Catalog.Find("install.rustdesk")!);

    private static InstallTarget Steam => InstallTarget.FromEntry(Catalog.Find("install.steam")!);

    private static InstallTarget Zip => InstallTarget.FromEntry(Catalog.Find("install.7zip")!);

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

    private static InstalledApp RustDeskApp => Program("RustDesk", "Purslane Tech Pte. Ltd.");

    private static JObject Asset(string name, long size, string? digest, string url)
    {
        var asset = new JObject { ["name"] = name, ["size"] = size, ["browser_download_url"] = url };
        if (digest is not null)
        {
            asset["digest"] = digest;
        }

        return asset;
    }

    internal static string Release(
        bool prerelease = false,
        bool draft = false,
        string? digest = "sha256:" + Hash,
        long size = Size,
        string url = MsiUrl,
        bool withMsi = true)
    {
        var assets = new JArray(Asset("rustdesk-1.5.0-x86_64.exe", 900, "sha256:" + new string('1', 64), MsiUrl.Replace(".msi", ".exe")));
        if (withMsi)
        {
            assets.Add(Asset(MsiName, size, digest, url));
        }

        return new JObject { ["tag_name"] = "1.5.0", ["prerelease"] = prerelease, ["draft"] = draft, ["assets"] = assets }
            .ToString(Formatting.None);
    }

    private sealed class Fixture
    {
        public Fixture(WingetRun? version = null)
        {
            Reader.Apps.Add(WingetAvailabilityTests.AppInstaller());
            Runner.Enqueue(version ?? FakeWingetRunner.Exit(0, "v1.11.510"));
            Web.EnqueueText(FakeWebDownloader.Ok(Release()));
            Web.ByteSteps.AddRange(new long[] { 0, 250, 500, 750, 1000 });
            Files.Hash = Hash;
            Msi.OnInstall = () => Reader.Apps.Add(RustDeskApp);
            Web.Trace = Trace;
            Signatures.Trace = Trace;
            Msi.Trace = Trace;
            Files.Trace = Trace;
        }

        public List<string> Trace { get; } = new();

        public FakeWingetRunner Runner { get; } = new();

        public FakeWingetLogs Logs { get; } = new();

        public FakeInstalledAppsReader Reader { get; } = new();

        public FakeJournalStore Journal { get; } = new();

        public FakeUserContext User { get; } = new() { Sid = UserSid };

        public string ProcessSid { get; set; } = UserSid;

        public ListLog Log { get; } = new();

        public FakeClock Clock { get; } = new();

        public FakeWebDownloader Web { get; } = new();

        public FakeSignatureVerifier Signatures { get; } = new();

        public FakeMsiInstaller Msi { get; } = new();

        public FakeDownloadFiles Files { get; } = new();

        public bool WithGitHub { get; set; } = true;

        public List<TimeSpan> Sleeps { get; } = new();

        public ListProgress<BatchStepEvent> Steps { get; } = new();

        public BatchStop Stop { get; } = new();

        public void Sleep(TimeSpan span)
        {
            Sleeps.Add(span);
            Clock.UtcNow += span;
        }

        public Func<string, WingetRun> Installs(string displayName)
            => _ =>
            {
                Reader.Apps.Add(Program(displayName));
                return FakeWingetRunner.Exit(0);
            };

        public InstallBatch Install(params InstallTarget[] targets)
        {
            var gitHub = WithGitHub
                ? new GitHubInstallServices { Web = Web, Signatures = Signatures, Msi = Msi, Files = Files }
                : null;
            var engine = new AppInstallEngine(Runner, Logs, Reader, Journal, User, ProcessSid, sid => sid == UserSid ? "dso" : null, Log, Clock, Sleep, gitHub);
            return engine.Install(targets, new InstallOptions { Steps = Steps, Stop = Stop }, CancellationToken.None);
        }

        public List<string> InstallCalls => Runner.Calls.Where(c => c.Arguments.StartsWith("install ", StringComparison.Ordinal)).Select(c => c.Arguments).ToList();

        public List<(string? Stage, int? Percent)> Progress(string id)
            => Steps.Items.Where(s => s.Phase == BatchStepPhase.Progress && s.TweakId == id).Select(s => (s.Stage, s.Percent)).ToList();
    }

    private static void AssertGitHubEntry(JournalEntry entry, JournalEntryStatus status, string outcome, string? message = null)
    {
        Assert.Equal(JournalEntry.AppInstallKind, entry.Kind);
        Assert.Equal("install.rustdesk", entry.TweakId);
        Assert.Equal("RustDesk", entry.Title);
        Assert.Equal(UserSid, entry.UserSid);
        Assert.Equal(status, entry.Status);
        Assert.Equal(new[] { "Started", outcome }, entry.Events.Select(e => e.Outcome));
        Assert.All(entry.Events, e => Assert.Equal(AppInstallEngine.JournalAction, e.Action));
        Assert.Equal("github: rustdesk/rustdesk", entry.Events[0].Message);
        if (message is not null)
        {
            Assert.Equal(message, entry.Events[1].Message);
        }
    }

    private static void AssertNothingDownloaded(Fixture f)
    {
        Assert.Empty(f.Web.Calls);
        Assert.Empty(f.Files.Paths);
        Assert.Empty(f.Msi.Calls);
        Assert.Empty(f.Signatures.Calls);
    }

    [Fact]
    public void HappyPath_DownloadVerifyInstall_Installed()
    {
        var f = new Fixture();
        var hop = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1?sig=x");
        f.Web.RedirectHops.Add(hop);
        (int Entries, JournalEntryStatus Status, string Outcome, string? Message)? atHttp = null;
        f.Web.OnGetText = () =>
        {
            var entry = f.Journal.Current.Entries.Last();
            atHttp = (f.Journal.Current.Entries.Count, entry.Status, entry.Events.Last().Outcome, entry.Events.Last().Message);
        };

        var batch = f.Install(RustDesk);

        Assert.Equal((1, JournalEntryStatus.Aborted, "Started", (string?)"github: rustdesk/rustdesk"), atHttp);
        Assert.Equal(
            new[]
            {
                "GetText " + RustDeskSource.ReleasesLatestUrl,
                "Download " + MsiUrl + " " + RustDeskSource.MaxBytes,
                "Sha256 " + Path1,
                "Verify " + Path1,
                "Msi " + Path1,
                "Delete " + Path1,
            },
            f.Trace);
        Assert.Equal((RustDeskSource.ReleasesLatestUrl, RustDeskSource.ApiAccept, RustDeskSource.ApiTimeout), Assert.Single(f.Web.TextCalls));
        Assert.Equal((new Uri(MsiUrl), RustDeskSource.MaxBytes, Path1, RustDeskSource.DownloadTimeout), Assert.Single(f.Web.DownloadCalls));
        Assert.Equal(new[] { (hop, true) }, f.Web.RedirectChecks);
        Assert.Equal((Path1, MsiLog, InstallTimeouts.PerApp), Assert.Single(f.Msi.Calls));
        Assert.Equal(RustDeskSource.MsiLogKey, Assert.Single(f.Logs.Calls).WingetId);
        Assert.Equal(new[] { WingetArguments.Version() }, f.Runner.Calls.Select(c => c.Arguments));

        Assert.Equal(WingetState.Ready, batch.Winget.State);
        var item = Assert.Single(batch.Items);
        Assert.Equal(("install.rustdesk", (string?)null, "RustDesk"), (item.Id, item.WingetId, item.Title));
        Assert.Equal(TweakOutcome.Installed, item.Outcome);
        Assert.Equal("Zainstalowano i sprawdzono.", item.Message);
        Assert.Equal(InstallOrigin.GitHub, item.Origin);
        Assert.Equal("rustdesk/rustdesk", item.GitHub);
        Assert.Equal(0, item.MsiExitCode);
        Assert.Null(item.WingetExitCode);
        Assert.Equal("1.5.0", item.Version);
        Assert.Equal(MsiLog, item.LogPath);
        Assert.False(item.RequiresReboot);
        Assert.True(batch.Batch.Results[0].IsSuccess);
        Assert.Empty(batch.Batch.Results[0].Warnings);

        AssertGitHubEntry(
            Assert.Single(f.Journal.Current.Entries),
            JournalEntryStatus.Active,
            "Installed",
            "github: rustdesk/rustdesk, wersja 1.5.0, log: " + MsiLog);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void HashUppercase_Accepted()
    {
        var f = new Fixture();
        f.Files.Hash = Hash.ToUpperInvariant();

        Assert.Equal(TweakOutcome.Installed, Assert.Single(f.Install(RustDesk).Items).Outcome);
    }

    [Fact]
    public void ProgressStages_InOrderPercentOnlyOnChange()
    {
        var f = new Fixture();
        f.Web.ByteSteps.Clear();
        f.Web.ByteSteps.AddRange(new long[] { 0, 0, 100, 101, 250, 250, 999, 1000 });

        f.Install(RustDesk);

        const string download = "Pobieranie (z GitHuba)";
        Assert.Equal(
            new (string?, int?)[]
            {
                (download, null),
                (download, 0),
                (download, 10),
                (download, 25),
                (download, 99),
                (download, 100),
                ("Sprawdzanie podpisu", null),
                ("Instalowanie", null),
            },
            f.Progress("install.rustdesk"));
    }

    [Fact]
    public void MatchedBeforeRun_AlreadyInstalled_NoHttp()
    {
        var f = new Fixture();
        f.Reader.Apps.Add(Program("RustDesk", "Purslane Tech Pte. Ltd.") with { Version = "1.4.9" });

        var batch = f.Install(RustDesk);

        var item = Assert.Single(batch.Items);
        Assert.Equal(TweakOutcome.AlreadyInstalled, item.Outcome);
        Assert.Equal("Była już zainstalowana – nic nie uruchomiono.", item.Message);
        Assert.Equal(InstallOrigin.GitHub, item.Origin);
        Assert.Null(item.MsiExitCode);
        Assert.Null(item.LogPath);
        AssertNothingDownloaded(f);
        Assert.Empty(f.Journal.Current.Entries);
        Assert.Empty(f.Logs.Calls);
    }

    [Theory]
    [InlineData(WebFailure.Network)]
    [InlineData(WebFailure.TimedOut)]
    public void ApiNetwork_FailedNoConnection_SeriesEnds(WebFailure failure)
    {
        var f = NewWithApi(FakeWebDownloader.Failure(failure));

        var batch = f.Install(RustDesk, Steam);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal(NoGitHub, batch.Items[0].Message);
        Assert.Equal(NoGitHub, Assert.Single(f.Journal.Current.Entries).Events.Last().Reason);
        Assert.Equal(TweakOutcome.Skipped, batch.Items[1].Outcome);
        Assert.Equal(NoInternet, batch.Items[1].Message);
        Assert.Empty(f.InstallCalls);
        Assert.Empty(f.Files.Paths);
        AssertGitHubEntry(Assert.Single(f.Journal.Current.Entries), JournalEntryStatus.Aborted, "Failed", "github: rustdesk/rustdesk");
    }

    [Fact]
    public void ApiTimedOut_SameAsNetwork()
    {
        var f = NewWithApi(FakeWebDownloader.Failure(WebFailure.TimedOut));

        var batch = f.Install(RustDesk, Zip);

        Assert.Equal((TweakOutcome.Failed, NoGitHub), (batch.Items[0].Outcome, batch.Items[0].Message));
        Assert.Equal((TweakOutcome.Skipped, NoInternet), (batch.Items[1].Outcome, batch.Items[1].Message));
        Assert.Empty(f.InstallCalls);
        Assert.Empty(f.Files.Paths);
    }

    private static Fixture NewWithApi(WebText api, WingetRun? version = null)
    {
        var f = new Fixture(version);
        f.Web.DefaultText = api;
        f.Web.ClearTexts();
        return f;
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public void ApiRateLimited_Failed_SeriesContinues(int status)
    {
        var f = NewWithApi(FakeWebDownloader.Failure(WebFailure.HttpStatus, status));
        f.Runner.Enqueue(f.Installs("Steam"));

        var batch = f.Install(RustDesk, Steam);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal("Nie wykonano – GitHub chwilowo odmawia (limit zapytań), spróbuj za godzinę", batch.Items[0].Message);
        Assert.Equal(TweakOutcome.Installed, batch.Items[1].Outcome);
        Assert.Single(f.InstallCalls);
        Assert.Empty(f.Files.Paths);
    }

    [Fact]
    public void ApiOtherStatus_FailedWithCode()
    {
        var f = NewWithApi(FakeWebDownloader.Failure(WebFailure.HttpStatus, 500));
        f.Runner.Enqueue(f.Installs("Steam"));

        var batch = f.Install(RustDesk, Steam);

        Assert.Equal("Nie wykonano – GitHub odpowiedział kodem 500", batch.Items[0].Message);
        Assert.Contains("kodem 500", batch.Items[0].Message, StringComparison.Ordinal);
        Assert.Equal(TweakOutcome.Installed, batch.Items[1].Outcome);
    }

    [Theory]
    [InlineData(WebFailure.TooLarge)]
    [InlineData(WebFailure.RedirectRefused)]
    public void ApiOtherFailure_BadResponse(WebFailure failure)
    {
        var f = NewWithApi(FakeWebDownloader.Failure(failure, 200));

        var batch = f.Install(RustDesk);

        Assert.Equal(ReleaseRefusedPrefix + "błędna odpowiedź GitHuba)", batch.Items[0].Message);
        Assert.Empty(f.Files.Paths);
    }

    public static TheoryData<string, string> RefusedReleases => new()
    {
        { Release(prerelease: true), "wydanie testowe (prerelease)" },
        { Release(draft: true), "szkic wydania" },
        { Release(digest: null), "brak skrótu SHA256" },
        { Release(size: RustDeskSource.MaxBytes + 1), "plik większy niż 200 MB" },
        { Release(url: "https://evil.example.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi"), "adres pobrania spoza GitHuba RustDesk" },
        { Release(url: "http://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi"), "adres pobrania spoza GitHuba RustDesk" },
        { "{", "błędna odpowiedź GitHuba" },
        { string.Empty, "błędna odpowiedź GitHuba" },
    };

    [Theory]
    [MemberData(nameof(RefusedReleases))]
    public void ReleaseRefused_DownloadNotCalled(string json, string reasonFragment)
    {
        var f = NewWithApi(FakeWebDownloader.Ok(json));

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal(ReleaseRefusedPrefix + reasonFragment + ")", batch.Items[0].Message);
        Assert.Empty(f.Web.DownloadCalls);
        Assert.Empty(f.Files.Paths);
        Assert.Empty(f.Msi.Calls);
        AssertGitHubEntry(Assert.Single(f.Journal.Current.Entries), JournalEntryStatus.Aborted, "Failed");
    }

    [Fact]
    public void NoMsiAsset_GitHubNoMsiText()
    {
        var f = NewWithApi(FakeWebDownloader.Ok(Release(withMsi: false)));

        var batch = f.Install(RustDesk);

        Assert.Equal("Nie wykonano – na GitHubie RustDesk nie ma instalatora MSI dla tej wersji", batch.Items[0].Message);
        Assert.Empty(f.Web.DownloadCalls);
    }

    [Fact]
    public void DownloadRedirectRefused_CheckFailed_FileDeleted()
    {
        var f = new Fixture();
        var good = new Uri("https://release-assets.githubusercontent.com/a");
        var evil = new Uri("https://evil.example.com/a");
        f.Web.RedirectHops.AddRange(new[] { good, evil });

        var batch = f.Install(RustDesk);

        Assert.Equal(new[] { (good, true), (evil, false) }, f.Web.RedirectChecks);
        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal(CheckFailed + "przekierowanie na nieznany serwer evil.example.com)", batch.Items[0].Message);
        Assert.Empty(f.Files.HashCalls);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void DownloadTooLarge_CheckFailed_FileDeleted()
    {
        var f = new Fixture { };
        f.Web.FileResult = new WebFile(WebFailure.TooLarge, 200, RustDeskSource.MaxBytes + 1, null, "za duży");

        var batch = f.Install(RustDesk);

        Assert.Equal(CheckFailed + "plik większy niż 200 MB)", batch.Items[0].Message);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void DownloadHttpStatus_FailedWithCode_FileDeleted()
    {
        var f = new Fixture();
        f.Web.FileResult = new WebFile(WebFailure.HttpStatus, 404, 0, null, "404");

        var batch = f.Install(RustDesk);

        Assert.Equal("Nie wykonano – GitHub odpowiedział kodem 404", batch.Items[0].Message);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void DownloadSizeMismatch_CheckFailed_FileDeleted()
    {
        var f = new Fixture();
        f.Web.ByteSteps.Clear();
        f.Web.ByteSteps.AddRange(new long[] { 0, 999 });

        var batch = f.Install(RustDesk);

        Assert.Equal(CheckFailed + "rozmiar inny niż w GitHubie)", batch.Items[0].Message);
        Assert.Empty(f.Files.HashCalls);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void DownloadNetworkMidway_NoConnection_FileDeleted()
    {
        var f = new Fixture();
        f.Web.FileResult = new WebFile(WebFailure.Network, 200, 500, null, "zerwane");

        var batch = f.Install(RustDesk, Steam);

        Assert.Equal((TweakOutcome.Failed, NoGitHub), (batch.Items[0].Outcome, batch.Items[0].Message));
        Assert.Equal((TweakOutcome.Skipped, NoInternet), (batch.Items[1].Outcome, batch.Items[1].Message));
        Assert.Empty(f.InstallCalls);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void DownloadTimedOut_LimitText_SeriesEnds_FileDeleted()
    {
        // M2 final review: limit 10 min pobierania ma własny tekst (wolne łącze), ale nadal kończy serię jak brak sieci (decyzja planu 4).
        var f = new Fixture();
        f.Web.FileResult = new WebFile(WebFailure.TimedOut, 200, 500, null, "limit czasu");

        var batch = f.Install(RustDesk, Steam);

        Assert.Equal(
            (TweakOutcome.Failed, "Nie wykonano – pobieranie z GitHuba przekroczyło limit 10 min (wolne lub zerwane łącze)"),
            (batch.Items[0].Outcome, batch.Items[0].Message));
        Assert.Equal(InstallText.GitHubDownloadTimedOut, batch.Items[0].Message);
        Assert.Equal((TweakOutcome.Skipped, NoInternet), (batch.Items[1].Outcome, batch.Items[1].Message));
        Assert.Empty(f.InstallCalls);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void Installed_LogsFileHashAndSigner_BeforeMsi()
    {
        // M3 final review: przy sukcesie log mówi, CO zostało pobrane i zweryfikowane (plik, bajty, SHA256, podmiot podpisu).
        var f = new Fixture();
        int? linesAtMsi = null;
        f.Msi.OnInstall = () =>
        {
            linesAtMsi = f.Log.Lines.Count;
            f.Reader.Apps.Add(RustDeskApp);
        };

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Installed, Assert.Single(batch.Items).Outcome);
        var index = f.Log.Lines.FindIndex(line => line.StartsWith("INFO", StringComparison.Ordinal)
            && line.Contains(MsiName)
            && line.Contains("1000 B")
            && line.Contains(Hash)
            && line.Contains(FakeSignatureVerifier.PurslaneSubject));
        Assert.True(index >= 0, string.Join(Environment.NewLine, f.Log.Lines));
        Assert.True(index < linesAtMsi, "linia logu ma powstać przed msiexec");
    }

    [Fact]
    public void CheckFailed_NoVerifiedLogLine()
    {
        var f = new Fixture();
        f.Files.Hash = new string('0', 64);

        f.Install(RustDesk);

        Assert.DoesNotContain(f.Log.Lines, line => line.StartsWith("INFO", StringComparison.Ordinal) && line.Contains(FakeSignatureVerifier.PurslaneSubject));
    }

    [Fact]
    public void DownloadThrows_FailedFileDeleted_JournalClosed()
    {
        var f = new Fixture();
        f.Web.OnDownload = () => throw new InvalidOperationException("Bezpiecznik testów (fake)");

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal("Nieoczekiwany błąd: Bezpiecznik testów (fake)", batch.Items[0].Message);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
        AssertGitHubEntry(Assert.Single(f.Journal.Current.Entries), JournalEntryStatus.Aborted, "Failed", "github: rustdesk/rustdesk, wersja 1.5.0");
    }

    [Fact]
    public void DownloadWriteFails_Failed_SeriesContinues_FileDeleted()
    {
        // Ruling (Task 3): IOException zapisu z downloadera = porażka pozycji, nie brak sieci.
        var f = new Fixture();
        f.Web.OnDownload = () => throw new IOException("Za mało miejsca na dysku.");
        f.Runner.Enqueue(f.Installs("Steam"));

        var batch = f.Install(RustDesk, Steam);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal("Nie wykonano – nie udało się zapisać pobranego pliku (Za mało miejsca na dysku.)", batch.Items[0].Message);
        Assert.Equal(TweakOutcome.Installed, batch.Items[1].Outcome);
        Assert.Single(f.InstallCalls);
        Assert.Empty(f.Files.HashCalls);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
        AssertGitHubEntry(f.Journal.Current.Entries[0], JournalEntryStatus.Aborted, "Failed");
    }

    [Fact]
    public void HashMismatch_CheckFailed_MsiNotStarted_FileDeleted()
    {
        var f = new Fixture();
        f.Files.Hash = new string('0', 64);

        var batch = f.Install(RustDesk);

        Assert.Equal(CheckFailed + "skrót SHA256 inny niż w GitHubie)", batch.Items[0].Message);
        Assert.Empty(f.Signatures.Calls);
        Assert.Empty(f.Msi.Calls);
        Assert.Empty(f.Logs.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void HashThrows_CheckFailed_FileDeleted()
    {
        var f = new Fixture();
        f.Files.HashThrows = new IOException("plik zablokowany");

        var batch = f.Install(RustDesk);

        Assert.Equal(CheckFailed + "plik zablokowany)", batch.Items[0].Message);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Theory]
    [InlineData(SignatureStatus.NotSigned, null, null, "brak podpisu wydawcy")]
    [InlineData(SignatureStatus.Invalid, null, "0x80096010", "podpis nieważny (0x80096010)")]
    [InlineData(SignatureStatus.Invalid, null, null, "podpis nieważny")]
    [InlineData(SignatureStatus.Valid, "CN=Zlatin Stamatov, O=Zlatin Stamatov, C=BG", null, "podpis innego wydawcy: CN=Zlatin Stamatov, O=Zlatin Stamatov, C=BG")]
    [InlineData(SignatureStatus.Valid, "CN=Microsoft Corporation, O=Microsoft Corporation, C=US", null, "podpis innego wydawcy: CN=Microsoft Corporation, O=Microsoft Corporation, C=US")]
    [InlineData(SignatureStatus.Valid, null, null, "podpis innego wydawcy: (brak podmiotu)")]
    public void SignatureRejected_MsiNotStarted_FileDeleted(SignatureStatus status, string? subject, string? detail, string reason)
    {
        var f = new Fixture();
        f.Signatures.Result = new SignatureCheck(status, subject, detail);

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal(CheckFailed + reason + ")", batch.Items[0].Message);
        Assert.Equal(new[] { Path1 }, f.Signatures.Calls);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void VerifierThrows_CheckFailed_FileDeleted()
    {
        var f = new Fixture();
        f.Signatures.Throws = new InvalidOperationException("WinVerifyTrust padł");

        var batch = f.Install(RustDesk);

        Assert.Equal(CheckFailed + "WinVerifyTrust padł)", batch.Items[0].Message);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    [InlineData(1641)]
    public void MsiSuccessCodes_Installed(int code)
    {
        var f = new Fixture();
        f.Msi.Result = new MsiRun(code, false);

        var batch = f.Install(RustDesk);

        var item = batch.Items[0];
        var reboot = code != 0;
        Assert.Equal(TweakOutcome.Installed, item.Outcome);
        Assert.Equal(code, item.MsiExitCode);
        Assert.Equal(reboot, item.RequiresReboot);
        Assert.Equal(reboot, batch.Batch.Results[0].RequiresReboot);
        AssertGitHubEntry(
            Assert.Single(f.Journal.Current.Entries),
            reboot ? JournalEntryStatus.PendingReboot : JournalEntryStatus.Active,
            "Installed");
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Theory]
    [InlineData(1618)]
    [InlineData(1603)]
    public void MsiFailureCodes_Failed(int code)
    {
        var f = new Fixture();
        f.Msi.Result = new MsiRun(code, false);
        var readsBefore = 0;
        f.Msi.OnInstall = () => readsBefore = f.Reader.ReadSids.Count;

        var batch = f.Install(RustDesk);

        var item = batch.Items[0];
        Assert.Equal(TweakOutcome.Failed, item.Outcome);
        Assert.Equal($"Nie wykonano – instalator MSI zakończył się kodem {code}. Log MSI: {MsiLog}", item.Message);
        Assert.Contains($"kodem {code}", item.Message, StringComparison.Ordinal);
        Assert.Contains("Log MSI: ", item.Message, StringComparison.Ordinal);
        Assert.Equal(code, item.MsiExitCode);
        Assert.Equal(MsiLog, item.LogPath);
        Assert.Equal(readsBefore, f.Reader.ReadSids.Count);
        AssertGitHubEntry(Assert.Single(f.Journal.Current.Entries), JournalEntryStatus.Aborted, "Failed", "github: rustdesk/rustdesk, wersja 1.5.0, log: " + MsiLog);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void MsiTimedOut_NotConfirmed_SeriesEnds_FileKept()
    {
        var f = new Fixture();
        f.Msi.Result = new MsiRun(null, true);

        var batch = f.Install(RustDesk, Steam);

        Assert.Equal(TweakOutcome.NotConfirmed, batch.Items[0].Outcome);
        Assert.Equal("Nie potwierdzono – instalacja nadal trwa w tle (program przestał czekać po 30 min).", batch.Items[0].Message);
        Assert.Null(batch.Items[0].MsiExitCode);
        Assert.Equal((TweakOutcome.Skipped, "Nie wykonano – poprzednia instalacja nadal trwa"), (batch.Items[1].Outcome, batch.Items[1].Message));
        Assert.Empty(f.InstallCalls);
        Assert.Empty(f.Files.Deleted);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("WARN", StringComparison.Ordinal) && line.Contains(Path1));
        AssertGitHubEntry(Assert.Single(f.Journal.Current.Entries), JournalEntryStatus.Aborted, "NotConfirmed");
    }

    [Fact]
    public void MsiStartThrows_Failed_FileDeleted()
    {
        var f = new Fixture();
        f.Msi.Throws = new InvalidOperationException("CreateProcessW: kod 2");

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal("Nie wykonano – nie udało się uruchomić instalatora MSI: CreateProcessW: kod 2", batch.Items[0].Message);
        Assert.Null(batch.Items[0].MsiExitCode);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void LogPathFails_Failed_MsiNotStarted_FileDeleted()
    {
        var f = new Fixture();
        f.Logs.Throws = new IOException("junction");

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal("Nie wykonano – nie udało się przygotować pliku logu MSI: junction", batch.Items[0].Message);
        Assert.Null(batch.Items[0].LogPath);
        Assert.Empty(f.Msi.Calls);
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void MsiSuccessNotVisible_NotConfirmed()
    {
        var f = new Fixture();
        f.Msi.OnInstall = null;

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.NotConfirmed, batch.Items[0].Outcome);
        Assert.Equal(
            "instalator MSI zgłosił sukces, ale programu nie widać na liście zainstalowanych. Log MSI: " + MsiLog,
            batch.Items[0].Message);
        Assert.Equal(InstallTimeouts.ReadbackAttempts - 1, f.Sleeps.Count);
        Assert.All(f.Sleeps, s => Assert.Equal(InstallTimeouts.ReadbackInterval, s));
        Assert.Equal(1, f.Progress("install.rustdesk").Count(p => p.Stage == "Sprawdzanie instalacji"));
        AssertGitHubEntry(Assert.Single(f.Journal.Current.Entries), JournalEntryStatus.Aborted, "NotConfirmed");
        Assert.Equal(new[] { Path1 }, f.Files.Deleted);
    }

    [Fact]
    public void MsiSuccessAppearsAfterDelay_Installed()
    {
        var f = new Fixture();
        f.Msi.OnInstall = null;
        f.Steps.OnReport = e =>
        {
            if (e.Stage == "Sprawdzanie instalacji")
            {
                f.Reader.Apps.Add(RustDeskApp);
            }
        };

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
        Assert.Equal("Zainstalowano i sprawdzono.", batch.Items[0].Message);
        Assert.Single(f.Sleeps);
    }

    [Fact]
    public void MsiSuccessReadFails_NotConfirmedWithError()
    {
        var f = new Fixture();
        f.Msi.OnInstall = () => f.Reader.ReadThrows = new InvalidOperationException("odczyt padł");

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.NotConfirmed, batch.Items[0].Outcome);
        Assert.Equal(
            "instalator MSI zgłosił sukces, ale nie udało się odczytać listy zainstalowanych: odczyt padł. Log MSI: " + MsiLog,
            batch.Items[0].Message);
    }

    [Fact]
    public void WingetNotWorking_GitHubRuns_WingetItemRefused()
    {
        var f = new Fixture(FakeWingetRunner.Exit(5));

        var batch = f.Install(Zip, RustDesk);

        Assert.Equal(WingetState.NotWorking, batch.Winget.State);
        Assert.Equal(new[] { WingetArguments.Version() }, f.Runner.Calls.Select(c => c.Arguments));
        Assert.Equal(TweakOutcome.Refused, batch.Items[0].Outcome);
        Assert.Equal(batch.Winget.Text, batch.Items[0].Message);
        Assert.Equal(InstallOrigin.Winget, batch.Items[0].Origin);
        Assert.Equal(TweakOutcome.Installed, batch.Items[1].Outcome);
        Assert.Equal("install.rustdesk", Assert.Single(f.Journal.Current.Entries).TweakId);
        var zipFinished = Assert.Single(f.Steps.Items, s => s.TweakId == "install.7zip");
        Assert.Equal(BatchStepPhase.Finished, zipFinished.Phase);
        Assert.Equal(TweakOutcome.Refused, zipFinished.Result!.Outcome);
    }

    [Fact]
    public void WingetNotRegistered_RustDeskOnly_Installed()
    {
        var f = new Fixture();
        f.Reader.Apps.Clear();

        var batch = f.Install(RustDesk);

        Assert.Equal(WingetState.NotRegistered, batch.Winget.State);
        Assert.Empty(f.Runner.Calls);
        Assert.Equal(TweakOutcome.Installed, Assert.Single(batch.Items).Outcome);
        Assert.DoesNotContain(f.Log.Lines, line => line.Contains("Pozycje winget nie rozpoczęte"));
    }

    [Fact]
    public void WingetNotRegistered_MixedSeries_WingetItemsLogged()
    {
        var f = new Fixture();
        f.Reader.Apps.Clear();

        f.Install(RustDesk, Zip);

        Assert.Single(f.Log.Lines, line => line.StartsWith("WARN", StringComparison.Ordinal) && line.Contains("Pozycje winget nie rozpoczęte"));
    }

    [Fact]
    public void StopAfterGitHub_RemainingUnavailableItemsSkipped_SeriesStopped()
    {
        // Review Tasku 2, M2: po „Zatrzymaj” pozycja winget przy niedziałającym winget też jest pominięta, a seria – zatrzymana.
        var f = new Fixture(FakeWingetRunner.Exit(5));
        f.Steps.OnReport = e =>
        {
            if (e.TweakId == "install.rustdesk" && e.Phase == BatchStepPhase.Finished)
            {
                f.Stop.Request();
            }
        };

        var batch = f.Install(RustDesk, Zip);

        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
        Assert.Equal((TweakOutcome.Skipped, TweakEngine.StoppedMessage), (batch.Items[1].Outcome, batch.Items[1].Message));
        Assert.True(batch.Batch.Stopped);
    }

    [Fact]
    public void TimeoutOnWinget_GitHubWithoutServicesSkipped()
    {
        // Review Tasku 2, M2: po limicie czasu reszta to „poprzednia instalacja nadal trwa”, także pozycja z GitHuba bez usług.
        var f = new Fixture { WithGitHub = false };
        f.Runner.Enqueue(FakeWingetRunner.TimedOut());

        var batch = f.Install(Zip, RustDesk);

        Assert.Equal(TweakOutcome.NotConfirmed, batch.Items[0].Outcome);
        Assert.Equal((TweakOutcome.Skipped, "Nie wykonano – poprzednia instalacja nadal trwa"), (batch.Items[1].Outcome, batch.Items[1].Message));
    }

    [Fact]
    public void WingetNotRegistered_OnlyWingetItems_AllRefusedNoEvents()
    {
        var f = new Fixture();
        f.Reader.Apps.Clear();

        var batch = f.Install(Zip, Steam);

        Assert.All(batch.Items, item => Assert.Equal(TweakOutcome.Refused, item.Outcome));
        Assert.Empty(f.Steps.Items);
        AssertNothingDownloaded(f);
    }

    [Fact]
    public void OtherAccount_GitHubRefused_NothingDownloaded()
    {
        var f = new Fixture { ProcessSid = WingetAvailabilityTests.OtherSid };

        var batch = f.Install(RustDesk, Zip);

        Assert.Equal(WingetState.OtherAccount, batch.Winget.State);
        Assert.All(batch.Items, item =>
        {
            Assert.Equal(TweakOutcome.Refused, item.Outcome);
            Assert.Equal(batch.Winget.Text, item.Message);
        });
        Assert.Equal(InstallOrigin.GitHub, batch.Items[0].Origin);
        Assert.Equal("rustdesk/rustdesk", batch.Items[0].GitHub);
        AssertNothingDownloaded(f);
        Assert.Empty(f.Runner.Calls);
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Fact]
    public void UserUncertain_GitHubRefused_NothingDownloaded()
    {
        var f = new Fixture();
        f.User.IsCertain = false;

        var batch = f.Install(RustDesk);

        Assert.Equal(WingetState.UserUncertain, batch.Winget.State);
        Assert.Equal(TweakOutcome.Refused, Assert.Single(batch.Items).Outcome);
        AssertNothingDownloaded(f);
    }

    [Fact]
    public void ReadFails_GitHubRefused_NothingDownloaded()
    {
        var f = new Fixture();
        f.Reader.ReadThrows = new InvalidOperationException("rejestr niedostępny");

        var batch = f.Install(RustDesk, Zip);

        Assert.All(batch.Items, item =>
        {
            Assert.Equal(TweakOutcome.Refused, item.Outcome);
            Assert.Contains("rejestr niedostępny", item.Message, StringComparison.Ordinal);
        });
        AssertNothingDownloaded(f);
        Assert.Empty(f.Runner.Calls);
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Fact]
    public void GitHubServicesMissing_Failed()
    {
        // Review Tasku 1, M5: pozycja bez WingetId (np. z --apps-catalog) nigdy nie idzie do winget.
        var f = new Fixture { WithGitHub = false };
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)"));

        var batch = f.Install(RustDesk, Zip);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal("Nie wykonano – instalacja z GitHuba nie jest dostępna w tym uruchomieniu", batch.Items[0].Message);
        Assert.Equal(InstallOrigin.GitHub, batch.Items[0].Origin);
        Assert.Equal(TweakOutcome.Installed, batch.Items[1].Outcome);
        Assert.Equal(new[] { "7zip.7zip" }, f.Logs.Calls.Select(c => c.WingetId));
        Assert.Single(f.InstallCalls);
        Assert.Equal("install.7zip", Assert.Single(f.Journal.Current.Entries).TweakId);
        Assert.DoesNotContain(f.Journal.Current.Entries.SelectMany(e => e.Events), e => e.Message == "winget: ");
    }

    [Fact]
    public void DuplicatePackageKey_Throws()
    {
        var f = new Fixture();

        var ex = Assert.Throws<ArgumentException>(() => f.Install(RustDesk, RustDesk));

        Assert.Contains("github:rustdesk/rustdesk", ex.Message, StringComparison.Ordinal);
        AssertNothingDownloaded(f);
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void GitHubTargetNotAllowed_Throws()
    {
        var f = new Fixture();

        Assert.Throws<ArgumentException>(() => f.Install(new InstallTarget("install.other", null, "Inny", null, null, null, "rustdesk/rustdesk")));
        Assert.Throws<ArgumentException>(() => f.Install(RustDesk with { GitHub = "evil/rustdesk" }));
        Assert.Throws<ArgumentException>(() => f.Install(RustDesk with { GitHub = null }));
        Assert.Throws<ArgumentException>(() => f.Install(RustDesk with { Match = null }));
        AssertNothingDownloaded(f);
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void JournalSaveFails_Failed_NoHttp()
    {
        var f = new Fixture();
        f.Journal.FailSave = true;

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal("Nie udało się zapisać journala – instalacja nie została uruchomiona.", batch.Items[0].Message);
        AssertNothingDownloaded(f);
    }

    [Fact]
    public void DeleteFails_WarningInResult()
    {
        var f = new Fixture();
        f.Files.DeleteResult = false;

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
        const string warning = @"Nie udało się usunąć pobranego pliku C:\dso-downloads\1.msi";
        Assert.Contains(warning, batch.Batch.Results[0].Warnings);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("WARN", StringComparison.Ordinal) && line.Contains(warning));
    }

    [Fact]
    public void DeleteThrows_ResultKeptWithWarning()
    {
        var f = new Fixture();
        f.Files.DeleteThrows = new IOException("dostęp zabroniony");

        var batch = f.Install(RustDesk);

        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
        Assert.Contains(@"Nie udało się usunąć pobranego pliku C:\dso-downloads\1.msi", batch.Batch.Results[0].Warnings);
    }

    [Fact]
    public void AllowsGitHub_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => WingetAvailability.AllowsGitHub(null!));

    [Fact]
    public void NetworkOnGitHubAfterWingetRan_SeriesContinues()
    {
        var f = NewWithApi(FakeWebDownloader.Failure(WebFailure.Network));
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)")).Enqueue(f.Installs("Steam"));

        var batch = f.Install(Zip, RustDesk, Steam);

        Assert.Equal(new[] { TweakOutcome.Installed, TweakOutcome.Failed, TweakOutcome.Installed }, batch.Items.Select(i => i.Outcome));
        Assert.Equal(NoGitHub, batch.Items[1].Message);
        Assert.Equal(2, f.InstallCalls.Count);
    }

    [Theory]
    [InlineData(WingetState.Ready, true)]
    [InlineData(WingetState.NotRegistered, true)]
    [InlineData(WingetState.NotWorking, true)]
    [InlineData(WingetState.OtherAccount, false)]
    [InlineData(WingetState.UserUncertain, false)]
    public void AllowsGitHub_States(WingetState state, bool expected)
        => Assert.Equal(expected, WingetAvailability.AllowsGitHub(new WingetStatus(state, null, "x")));

    [Fact]
    public void Texts_FromPlan()
    {
        Assert.Equal("Pobieranie (z GitHuba)", InstallText.StageDownloadingGitHub);
        Assert.Equal("Sprawdzanie podpisu", InstallText.StageCheckingSignature);
        Assert.Equal("github: rustdesk/rustdesk", InstallText.GitHubJournalMessage("rustdesk/rustdesk", null, null));
        Assert.Equal("github: rustdesk/rustdesk, wersja 1.5.0, log: C:\\l.log", InstallText.GitHubJournalMessage("rustdesk/rustdesk", "1.5.0", "C:\\l.log"));
        Assert.Equal("Nie wykonano – instalator MSI zakończył się kodem 1603.", InstallText.MsiFailed(1603, null));
        Assert.Equal("a. Log MSI: C:\\l.log", InstallText.WithMsiLog("a. ", "C:\\l.log"));
        Assert.Equal("a.", InstallText.WithMsiLog("a.", null));
    }
}
