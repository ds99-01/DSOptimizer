using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Tools;
using DSOptimizer.Tests.Unit.Fakes;
using DSOptimizer.Tests.Unit.Support;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Tools;

/// <summary>
/// Handler <c>winutil</c> wyłącznie na fake'ach: żadnej sieci, żadnego pliku na dysku i żadnego okna PowerShell na hoście.
/// Odpowiedź API = próbka <c>winutil-release-26.10.07.json</c> (wzorowana na prawdziwej z 2026-10-26).
/// </summary>
public class WinUtilHandlerTests
{
    private const string Hash = "eeae69922fbe6354ea4e2a37f705eaa17bdaf45cc7889618a313178a513b13ee";
    private const string Url = "https://github.com/ChrisTitusTech/winutil/releases/download/26.10.07/winutil.ps1";
    private const string Path1 = @"C:\dso-downloads\1.ps1";

    private sealed class Rig
    {
        public List<string> Trace { get; } = new();

        public FakeWebDownloader Web { get; } = new();

        public FakeDownloadFiles Files { get; } = new() { Hash = Hash };

        public FakeProcessLauncher Launcher { get; } = new();

        public ListLog Log { get; } = new();

        public Rig()
        {
            Web.Trace = Trace;
            Files.Trace = Trace;
            Launcher.Trace = Trace;
            Web.ByteSteps.Add(927798);
        }

        public WinUtilHandler Handler()
            => new(new WinUtilServices { Web = Web, Files = Files, Launcher = Launcher, Log = Log });

        public ActionHandlerResult Run(IProgress<string>? output = null)
            => Handler().Run(
                new TweakDefinition { Id = "tools.winutil", Category = "tools", Kind = TweakKind.Action, Title = "t", Risk = RiskLevel.Caution, Scope = TweakScope.Machine, Summary = "s" },
                output,
                null,
                CancellationToken.None);

        public Rig Api(JObject root)
        {
            Web.EnqueueText(FakeWebDownloader.Ok(root.ToString()));
            return this;
        }
    }

    private static JObject Sample()
        => JObject.Parse(File.ReadAllText(RepoRoot.Combine("tests", "DSOptimizer.Tests.Unit", "Tools", "Samples", "winutil-release-26.10.07.json")));

    private static JObject FirstAsset(JObject root) => (JObject)root["assets"]![0]!;

    private static Rig Ready() => new Rig().Api(Sample());

    private static void AssertFailed(ActionHandlerResult result, string message)
    {
        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public void Name_IsWinUtil() => Assert.Equal(ActionHandlerNames.WinUtil, new Rig().Handler().Name);

    [Fact]
    public void ActionHandlerNames_WinUtilInAllNotLongRunning()
    {
        Assert.Equal("winutil", ActionHandlerNames.WinUtil);
        Assert.Contains(ActionHandlerNames.WinUtil, ActionHandlerNames.All);
        Assert.DoesNotContain(ActionHandlerNames.WinUtil, ActionHandlerNames.LongRunning);
    }

    [Fact]
    public void Run_HappyPath_LaunchesPowerShellWithFileAndReturnsExecuted()
    {
        var rig = Ready();

        var result = rig.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Otwarto okno WinUtil 26.10.07 (plik z wydania GitHuba, SHA256 zgodny).", result.Message);
        Assert.Empty(result.Warnings);
        var api = Assert.Single(rig.Web.TextCalls);
        Assert.Equal(WinUtilSource.ReleasesLatestUrl, api.Url);
        Assert.Equal(RustDeskSource.ApiAccept, api.Accept);
        Assert.Equal(WinUtilSource.ApiTimeout, api.Timeout);
        Assert.Equal(new[] { Path1 }, rig.Files.Paths);
        var download = Assert.Single(rig.Web.DownloadCalls);
        Assert.Equal(new Uri(Url), download.Url);
        Assert.Equal(WinUtilSource.MaxBytes, download.MaxBytes);
        Assert.Equal(Path1, download.TargetPath);
        Assert.Equal(WinUtilSource.DownloadTimeout, download.Timeout);
        var launch = Assert.Single(rig.Launcher.Calls);
        Assert.Equal("powershell.exe", launch.File);
        Assert.Equal(WinUtilSource.LaunchArguments(Path1), launch.Arguments);
        Assert.Empty(rig.Files.Deleted); // K4: plik zostaje, PowerShell czyta go po starcie
        Assert.Contains(rig.Log.Lines, l => l.StartsWith("INFO ", StringComparison.Ordinal)
                                             && l.Contains("26.10.07", StringComparison.Ordinal)
                                             && l.Contains(Path1, StringComparison.Ordinal)
                                             && l.Contains("927798", StringComparison.Ordinal)
                                             && l.Contains(Hash, StringComparison.Ordinal));
    }

    [Fact]
    public void Run_HashComputedAfterDownloadAndBeforeLaunch()
    {
        var rig = Ready();

        rig.Run();

        Assert.Equal(
            new[] { "GetText " + WinUtilSource.ReleasesLatestUrl, "Download " + Url + " 5242880", "Sha256 " + Path1, "Launch powershell.exe" },
            rig.Trace);
    }

    [Fact]
    public void Run_ReportsOneProgressLineBeforeDownload()
    {
        var rig = Ready();
        var output = new ListProgress();
        rig.Web.OnDownload = () => rig.Trace.Add("output=" + output.Items.Count);

        rig.Run(output);

        Assert.Equal(new[] { "Pobieranie WinUtil 26.10.07 z GitHuba…" }, output.Items);
        Assert.Contains("output=1", rig.Trace);
    }

    [Fact]
    public void Run_ApiFailure_NoProgressLine()
    {
        var rig = new Rig();
        rig.Web.EnqueueText(FakeWebDownloader.Failure(WebFailure.Network));
        var output = new ListProgress();

        rig.Run(output);

        Assert.Empty(output.Items);
    }

    [Fact]
    public void Run_UppercaseHashFromFile_StillMatches()
    {
        var rig = Ready();
        rig.Files.Hash = Hash.ToUpperInvariant();

        Assert.Equal(TweakOutcome.Executed, rig.Run().Outcome);
    }

    [Theory]
    [InlineData(WebFailure.Network)]
    [InlineData(WebFailure.TimedOut)]
    public void Run_ApiNetworkFailure_FailedNoDownloadNoLaunch(WebFailure failure)
    {
        var rig = new Rig();
        rig.Web.EnqueueText(FakeWebDownloader.Failure(failure));

        var result = rig.Run();

        AssertFailed(result, "Nie wykonano – brak połączenia z GitHubem.");
        Assert.Empty(rig.Files.Paths);
        Assert.Empty(rig.Web.DownloadCalls);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public void Run_Api403_FailedRateLimitText(int status)
    {
        var rig = new Rig();
        rig.Web.EnqueueText(FakeWebDownloader.Failure(WebFailure.HttpStatus, status));

        AssertFailed(rig.Run(), "Nie wykonano – GitHub chwilowo odmawia (limit zapytań).");
        Assert.Empty(rig.Files.Paths);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Theory]
    [InlineData(WebFailure.HttpStatus, 500)]
    [InlineData(WebFailure.HttpStatus, 302)]
    [InlineData(WebFailure.TooLarge, 200)]
    public void Run_ApiOtherFailure_FailedUnexpectedResponse(WebFailure failure, int status)
    {
        var rig = new Rig();
        rig.Web.EnqueueText(FakeWebDownloader.Failure(failure, status));

        AssertFailed(rig.Run(), "Nie wykonano – GitHub zwrócił nieoczekiwaną odpowiedź.");
        Assert.Empty(rig.Files.Paths);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_ReleaseWithoutDigest_FailedNoDownload()
    {
        var root = Sample();
        FirstAsset(root)["digest"] = JValue.CreateNull();
        var rig = new Rig().Api(root);

        AssertFailed(rig.Run(), "Nie wykonano – brak skrótu SHA256.");
        Assert.Empty(rig.Files.Paths);
        Assert.Empty(rig.Web.DownloadCalls);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_Prerelease_FailedWithRefusal()
    {
        var root = Sample();
        root["prerelease"] = true;
        var rig = new Rig().Api(root);

        AssertFailed(rig.Run(), "Nie wykonano – wydanie testowe (prerelease).");
        Assert.Empty(rig.Web.DownloadCalls);
    }

    [Fact]
    public void Run_NoAsset_FailedText()
    {
        var root = Sample();
        FirstAsset(root)["name"] = "winutil-renamed.ps1";
        var rig = new Rig().Api(root);

        AssertFailed(rig.Run(), "Nie wykonano – najnowsze wydanie WinUtil nie ma pliku winutil.ps1.");
        Assert.Empty(rig.Files.Paths);
        Assert.Empty(rig.Web.DownloadCalls);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_DownloadUrlOtherRepo_FailedNoDownload()
    {
        var root = Sample();
        FirstAsset(root)["browser_download_url"] = "https://github.com/someone/winutil/releases/download/26.10.07/winutil.ps1";
        var rig = new Rig().Api(root);

        // Nic nie pobrano, więc tekst o odpowiedzi GitHuba, nie o „pobranym pliku” (review końcowe, uwaga 7).
        AssertFailed(rig.Run(), "Nie wykonano – GitHub zwrócił nieoczekiwaną odpowiedź.");
        Assert.Empty(rig.Files.Paths);
        Assert.Empty(rig.Web.DownloadCalls);
        Assert.Empty(rig.Launcher.Calls);
        Assert.Contains(rig.Log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains("someone", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(WebFailure.TimedOut)]
    [InlineData(WebFailure.Network)]
    public void Run_DownloadTimedOut_FailedAndFileDeleted(WebFailure failure)
    {
        var rig = Ready();
        rig.Web.FileResult = new WebFile(failure, null, 1000, null, "limit (fake)");

        AssertFailed(rig.Run(), "Nie wykonano – brak połączenia z GitHubem.");
        Assert.Equal(new[] { Path1 }, rig.Files.Deleted);
        Assert.Empty(rig.Files.HashCalls);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_RedirectRefused_FailedAndFileDeleted()
    {
        var rig = Ready();
        rig.Web.RedirectHops.Add(new Uri("https://evil.example.com/winutil.ps1"));

        AssertFailed(rig.Run(), "Nie wykonano – GitHub zwrócił nieoczekiwaną odpowiedź.");
        Assert.Equal(new[] { (new Uri("https://evil.example.com/winutil.ps1"), false) }, rig.Web.RedirectChecks);
        Assert.Equal(new[] { Path1 }, rig.Files.Deleted);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_RedirectToGitHubAssets_IsAllowed()
    {
        var rig = Ready();
        rig.Web.RedirectHops.Add(new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1/x?sig=1"));

        Assert.Equal(TweakOutcome.Executed, rig.Run().Outcome);
        Assert.True(Assert.Single(rig.Web.RedirectChecks).Allowed);
    }

    [Theory]
    [InlineData(403, "Nie wykonano – GitHub chwilowo odmawia (limit zapytań).")]
    [InlineData(404, "Nie wykonano – GitHub zwrócił nieoczekiwaną odpowiedź.")]
    public void Run_DownloadHttpStatus_FailedAndFileDeleted(int status, string message)
    {
        var rig = Ready();
        rig.Web.FileResult = new WebFile(WebFailure.HttpStatus, status, 0, null, "kod (fake)");

        AssertFailed(rig.Run(), message);
        Assert.Equal(new[] { Path1 }, rig.Files.Deleted);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_DownloadTooLarge_FailedCheckAndFileDeleted()
    {
        var rig = Ready();
        rig.Web.FileResult = new WebFile(WebFailure.TooLarge, 200, WinUtilSource.MaxBytes + 1, null, "za duży (fake)");

        AssertFailed(rig.Run(), "Nie wykonano – pobrany plik nie przeszedł sprawdzenia.");
        Assert.Equal(new[] { Path1 }, rig.Files.Deleted);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_DownloadWriteError_FailedAndFileDeleted()
    {
        var rig = Ready();
        rig.Web.OnDownload = () => throw new IOException("dysk pełny");

        AssertFailed(rig.Run(), "Nie wykonano – nie udało się zapisać pobranego pliku (dysk pełny).");
        Assert.Equal(new[] { Path1 }, rig.Files.Deleted);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_DownloadFolderFails_FailedNothingDownloaded()
    {
        var rig = Ready();
        var files = new ThrowingPathFiles();

        var result = new WinUtilHandler(new WinUtilServices { Web = rig.Web, Files = files, Launcher = rig.Launcher, Log = rig.Log })
            .Run(new TweakDefinition { Id = "tools.winutil", Category = "tools", Kind = TweakKind.Action, Title = "t", Risk = RiskLevel.Caution, Scope = TweakScope.Machine, Summary = "s" }, null, null, CancellationToken.None);

        AssertFailed(result, "Nie wykonano – nie udało się przygotować folderu pobrań (junction).");
        Assert.Empty(rig.Web.DownloadCalls);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_HashMismatch_FailedFileDeletedNoLaunch()
    {
        var rig = Ready();
        rig.Files.Hash = new string('0', 64);

        AssertFailed(rig.Run(), "Nie wykonano – pobrany plik nie przeszedł sprawdzenia.");
        Assert.Equal(new[] { Path1 }, rig.Files.Deleted);
        Assert.Empty(rig.Launcher.Calls);
        Assert.Contains(rig.Log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains(Hash, StringComparison.Ordinal));
    }

    [Fact]
    public void Run_HashThrows_FailedFileDeletedNoLaunch()
    {
        var rig = Ready();
        rig.Files.HashThrows = new IOException("plik zajęty");

        AssertFailed(rig.Run(), "Nie wykonano – pobrany plik nie przeszedł sprawdzenia.");
        Assert.Equal(new[] { Path1 }, rig.Files.Deleted);
        Assert.Empty(rig.Launcher.Calls);
    }

    [Fact]
    public void Run_LaunchThrows_FailedAndFileDeleted()
    {
        var rig = Ready();
        rig.Launcher.Failure = new OperationException(OperationErrorKind.NotFound, "Brak pliku powershell.exe.");

        AssertFailed(rig.Run(), "Nie wykonano – nie udało się otworzyć okna PowerShell: Brak pliku powershell.exe.");
        Assert.Equal(new[] { Path1 }, rig.Files.Deleted);
    }

    [Fact]
    public void Run_UnexpectedLaunchException_PropagatesButFileDeleted()
    {
        var rig = Ready();
        rig.Launcher.Failure = new InvalidOperationException("błąd programisty");

        Assert.Throws<InvalidOperationException>(() => rig.Run());
        Assert.Equal(new[] { Path1 }, rig.Files.Deleted);
    }

    [Fact]
    public void Run_FailedDeleteAfterFailure_AddsWarning()
    {
        var rig = Ready();
        rig.Files.Hash = new string('0', 64);
        rig.Files.DeleteResult = false;

        var result = rig.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(new[] { "Nie udało się usunąć pobranego pliku " + Path1 }, result.Warnings);
        Assert.Contains(rig.Log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains("Nie udało się usunąć", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_DeleteThrows_AddsWarningInsteadOfThrowing()
    {
        var rig = Ready();
        rig.Files.Hash = new string('0', 64);
        rig.Files.DeleteThrows = new IOException("odmowa");

        var result = rig.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Constructor_NullServices_Throws()
        => Assert.Throws<ArgumentNullException>(() => new WinUtilHandler(null!));

    private sealed class ThrowingPathFiles : IDownloadFiles
    {
        public string NewDownloadPath(string extension) => throw new IOException("junction");

        public string Sha256(string path) => throw new InvalidOperationException();

        public bool TryDelete(string path) => throw new InvalidOperationException();
    }
}
