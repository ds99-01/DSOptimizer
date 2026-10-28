using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using DSOptimizer.Tests.Unit.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization.English;

/// <summary>Strona „Instalacja” i seria instalacji (Core/Install, winget i RustDesk z GitHuba) po angielsku: całe zdania, liczebniki, kody.</summary>
public class InstallEnglishTests
{
    private const string UserSid = WingetAvailabilityTests.UserSid;
    private const string VlcLog = @"C:\dso-logs\winget\VideoLAN.VLC-1.log";
    private const string MsiLog = @"C:\dso-logs\winget\rustdesk-msi-1.log";

    private static readonly AppsInstallCatalog Catalog = AppsInstallCatalog.Load("""
        { "entries": [
          { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Remote desktop.",
            "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
            "source": "https://github.com/rustdesk/rustdesk/releases" },
          { "id": "install.vlc", "wingetId": "VideoLAN.VLC", "title": "VLC", "summary": "Media player.",
            "category": "multimedia", "match": { "displayName": "VLC media player" }, "source": "https://example.com/vlc" },
          { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiver.",
            "category": "tools", "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" }
        ] }
        """);

    private static InstallTarget RustDesk => InstallTarget.FromEntry(Catalog.Find("install.rustdesk")!);

    private static InstallTarget Vlc => InstallTarget.FromEntry(Catalog.Find("install.vlc")!);

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

    /// <summary>Silnik instalacji wyłącznie na fake'ach (winget, GitHub, msiexec, journal), winget gotowy.</summary>
    private sealed class Fixture
    {
        public Fixture()
        {
            Reader.Apps.Add(WingetAvailabilityTests.AppInstaller());
            Runner.Enqueue(FakeWingetRunner.Exit(0, "v1.11.510"));
            Web.EnqueueText(FakeWebDownloader.Ok(AppInstallEngineGitHubTests.Release()));
            Web.ByteSteps.AddRange(new long[] { 0, 500, 1000 });
            Files.Hash = AppInstallEngineGitHubTests.Hash;
            Msi.OnInstall = () => Reader.Apps.Add(Program("RustDesk", "Purslane Tech Pte. Ltd."));
        }

        public FakeWingetRunner Runner { get; } = new();

        public FakeWingetLogs Logs { get; } = new();

        public FakeInstalledAppsReader Reader { get; } = new();

        public FakeJournalStore Journal { get; } = new();

        public FakeUserContext User { get; } = new() { Sid = UserSid };

        public ListLog Log { get; } = new();

        public FakeClock Clock { get; } = new();

        public FakeWebDownloader Web { get; } = new();

        public FakeSignatureVerifier Signatures { get; } = new();

        public FakeMsiInstaller Msi { get; } = new();

        public FakeDownloadFiles Files { get; } = new();

        public ListProgress<BatchStepEvent> Steps { get; } = new();

        public AppInstallEngine Engine()
            => new(
                Runner,
                Logs,
                Reader,
                Journal,
                User,
                UserSid,
                sid => sid == UserSid ? "dso" : null,
                Log,
                Clock,
                _ => { },
                new GitHubInstallServices { Web = Web, Signatures = Signatures, Msi = Msi, Files = Files });

        public InstallBatch Install(params InstallTarget[] targets)
            => Engine().Install(targets, new InstallOptions { Steps = Steps }, CancellationToken.None);
    }

    [Fact]
    public void Install_FailedWithCode_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new Fixture();
            f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A150010u, "Found VLC media player [VideoLAN.VLC] Version 3.0.21"));

            var result = f.Install(Vlc).Batch.Results.Single();

            const string Text = "Not done – the package isn't in winget or doesn't suit this computer. Code 0x8A150010.";
            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Equal(Text + " winget log: " + VlcLog, result.Message);
            Assert.Equal("Not done", InstallText.OutcomeName(result.Outcome));
            Assert.Equal(
                "The package isn't in winget or doesn't suit this computer. Code 0x8A150010. winget log: " + VlcLog,
                InstallText.ResultDetail(result));
            var close = f.Journal.Current.Entries.Single().Events[1];
            Assert.Equal(Text, close.Reason);
            Assert.Equal("winget: VideoLAN.VLC, version 3.0.21, log: " + VlcLog, close.Message);
        }
    }

    [Fact]
    public void WithoutLog_English_CutsLog()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Not done – x. winget log: " + VlcLog, InstallText.WithLog("Not done – x. ", VlcLog));
            Assert.Equal("Not done – x. MSI log: " + MsiLog, InstallText.WithMsiLog("Not done – x.", MsiLog));
            Assert.Equal("Not done – x.", InstallText.WithoutLog("Not done – x. winget log: " + VlcLog));
            Assert.Equal("Not done – x.", InstallText.WithoutLog("Not done – x. MSI log: " + MsiLog));
            Assert.Equal("A", InstallText.WithoutLog("A MSI log: x winget log: y"));
            Assert.Equal("Not done – x.", InstallText.WithoutLog("Not done – x."));
        }
    }

    [Fact]
    public void WithoutLog_PolishTextInEnglishRun_CutsLog()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                "Nie wykonano – za mało miejsca na dysku. Kod 0x8A150105.",
                InstallText.WithoutLog("Nie wykonano – za mało miejsca na dysku. Kod 0x8A150105. Log winget: " + VlcLog));
            Assert.Equal(
                "Nie wykonano – instalator MSI zakończył się kodem 1603.",
                InstallText.WithoutLog("Nie wykonano – instalator MSI zakończył się kodem 1603. Log MSI: " + MsiLog));
            Assert.Equal("A", InstallText.WithoutLog("A Log MSI: x winget log: y"));
        }
    }

    [Fact]
    public void WithoutLog_EnglishTextInPolishRun_CutsLog()
    {
        using (L.Use(UiLanguage.Polish))
        {
            Assert.Equal("Not done – x.", InstallText.WithoutLog("Not done – x. winget log: " + VlcLog));
            Assert.Equal("Not done – x.", InstallText.WithoutLog("Not done – x. MSI log: " + MsiLog));
            Assert.Equal("A", InstallText.WithoutLog("A MSI log: x Log winget: y"));
        }
    }

    [Fact]
    public void Install_NoNetwork_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new Fixture();
            f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A15000Fu));

            var results = f.Install(Vlc, Zip).Batch.Results;

            Assert.Equal(
                "Not done – no connection to the internet or to the winget source. Code 0x8A15000F. winget log: " + VlcLog,
                results[0].Message);
            Assert.Equal(TweakOutcome.Skipped, results[1].Outcome);
            Assert.Equal("Not done – no connection to the internet or to the winget source", results[1].Message);
            Assert.Equal("No connection to the internet or to the winget source", InstallText.ResultDetail(results[1]));
            Assert.Equal("no connection to the internet or to the winget source", InstallText.ResultDetail(results[1], capitalize: false));
        }
    }

    [Fact]
    public void Install_GitHubLimit_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var limited = new Fixture();
            limited.Web.ClearTexts();
            limited.Web.EnqueueText(FakeWebDownloader.Failure(WebFailure.HttpStatus, 403));
            Assert.Equal(
                "Not done – GitHub is temporarily refusing requests (rate limit), try again in an hour",
                limited.Install(RustDesk).Batch.Results.Single().Message);

            var slow = new Fixture { };
            slow.Web.FileResult = new WebFile(WebFailure.TimedOut, null, 500, null, "timeout");
            var results = slow.Install(RustDesk, Zip).Batch.Results;
            Assert.Equal("Not done – the download from GitHub went over the 10 min limit (slow or broken connection)", results[0].Message);
            Assert.Equal("Not done – no internet connection", results[1].Message);
        }
    }

    [Fact]
    public void Install_GitHubOtherFailures_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var offline = new Fixture();
            offline.Web.ClearTexts();
            offline.Web.EnqueueText(FakeWebDownloader.Failure(WebFailure.Network));
            Assert.Equal("Not done – no connection to GitHub", offline.Install(RustDesk).Batch.Results.Single().Message);

            var http = new Fixture();
            http.Web.ClearTexts();
            http.Web.EnqueueText(FakeWebDownloader.Failure(WebFailure.HttpStatus, 500));
            Assert.Equal("Not done – GitHub responded with code 500", http.Install(RustDesk).Batch.Results.Single().Message);

            var prerelease = new Fixture();
            prerelease.Web.ClearTexts();
            prerelease.Web.EnqueueText(FakeWebDownloader.Ok(AppInstallEngineGitHubTests.Release(prerelease: true)));
            Assert.Equal(
                "Not done – the RustDesk release on GitHub failed the check (test release (prerelease))",
                prerelease.Install(RustDesk).Batch.Results.Single().Message);

            var noMsi = new Fixture();
            noMsi.Web.ClearTexts();
            noMsi.Web.EnqueueText(FakeWebDownloader.Ok(AppInstallEngineGitHubTests.Release(withMsi: false)));
            Assert.Equal(
                "Not done – RustDesk on GitHub has no MSI installer for this version",
                noMsi.Install(RustDesk).Batch.Results.Single().Message);

            var hash = new Fixture();
            hash.Files.Hash = new string('0', 64);
            Assert.Equal(
                "Not done – the downloaded file failed the check (SHA256 hash doesn't match GitHub)",
                hash.Install(RustDesk).Batch.Results.Single().Message);

            var msi = new Fixture();
            msi.Msi.Result = new MsiRun(1603, false);
            var failed = msi.Install(RustDesk).Batch.Results.Single();
            Assert.Equal("Not done – the MSI installer ended with code 1603. MSI log: " + MsiLog, failed.Message);
            Assert.Equal("Not done – the MSI installer ended with code 1603.", msi.Journal.Current.Entries.Single().Events[1].Reason);
        }
    }

    [Fact]
    public void RustDesk_RedirectRefused_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var withHost = new Fixture();
            withHost.Web.RedirectHops.Add(new Uri("https://evil.example/rustdesk.msi"));
            Assert.Equal(
                "Not done – the downloaded file failed the check (redirect to an unknown server evil.example)",
                withHost.Install(RustDesk).Batch.Results.Single().Message);

            var withoutHost = new Fixture();
            withoutHost.Web.FileResult = new WebFile(WebFailure.RedirectRefused, 302, 0, null, "redirect refused");
            Assert.Equal(
                "Not done – the downloaded file failed the check (redirect to an unknown server)",
                withoutHost.Install(RustDesk).Batch.Results.Single().Message);

            Assert.Equal("redirect to an unknown server", InstallText.ReasonRedirect(null));
            Assert.Equal("redirect to an unknown server", InstallText.ReasonRedirect(string.Empty));
            Assert.Equal("redirect to an unknown server evil.example", InstallText.ReasonRedirect("evil.example"));
        }
    }

    [Theory]
    [InlineData(SignatureStatus.Invalid, null, "0x80096010", "invalid signature (0x80096010)")]
    [InlineData(SignatureStatus.Invalid, null, null, "invalid signature")]
    [InlineData(SignatureStatus.NotSigned, null, null, "no publisher signature")]
    [InlineData(SignatureStatus.Valid, "CN=Other, O=Other, C=US", null, "signature of another publisher: CN=Other, O=Other, C=US")]
    [InlineData(SignatureStatus.Valid, null, null, "signature of another publisher: (no subject)")]
    public void RustDesk_SignatureFailed_English(SignatureStatus status, string? subject, string? detail, string reason)
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new Fixture();
            f.Signatures.Result = new SignatureCheck(status, subject, detail);

            var result = f.Install(RustDesk).Batch.Results.Single();

            var expected = "Not done – the downloaded file failed the check (" + reason + ")";
            Assert.Equal(TweakOutcome.Failed, result.Outcome);
            Assert.Equal(expected, result.Message);
            Assert.Equal(expected, f.Journal.Current.Entries.Single().Events[1].Reason);
            Assert.Empty(f.Msi.Calls);
        }
    }

    [Fact]
    public void RustDesk_StagesAndSuccess_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new Fixture();

            var result = f.Install(RustDesk).Batch.Results.Single();

            Assert.Equal(TweakOutcome.Installed, result.Outcome);
            Assert.Equal("Installed and verified.", result.Message);
            Assert.Null(InstallText.ResultDetail(result));
            var stages = f.Steps.Items.Where(s => s.Phase == BatchStepPhase.Progress).Select(s => s.Stage).Distinct().ToList();
            Assert.Equal(new[] { "Downloading (from GitHub)", "Checking the signature", "Installing" }, stages);
            Assert.Contains(f.Log.Lines, line => line.Contains("Downloaded and verified rustdesk-1.5.0-x86_64.msi (1.5.0): 1000 B, SHA256 "));
        }
    }

    [Fact]
    public void Winget_StagesAndReadback_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new Fixture();
            f.Runner.Enqueue(FakeWingetRunner.Exit(0, "Downloading https://example.com/vlc.exe", "Starting package install..."));

            var result = f.Install(Vlc).Batch.Results.Single();

            Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
            Assert.Equal("winget reported success, but the program isn't on the list of installed apps. winget log: " + VlcLog, result.Message);
            var stages = f.Steps.Items.Where(s => s.Phase == BatchStepPhase.Progress).Select(s => s.Stage).Distinct().ToList();
            Assert.Equal(new[] { "Downloading", "Installing", "Verifying the installation" }, stages);
        }
    }

    [Fact]
    public void Install_AlreadyInstalledAndTimedOut_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var already = new Fixture();
            already.Reader.Apps.Add(Program("VLC media player"));
            Assert.Equal("It was already installed – nothing was run.", already.Install(Vlc).Batch.Results.Single().Message);

            var slow = new Fixture();
            slow.Runner.Enqueue(FakeWingetRunner.TimedOut());
            var results = slow.Engine().Install(
                new[] { Vlc, Zip },
                new InstallOptions { PerAppLimit = TimeSpan.FromMinutes(20) },
                CancellationToken.None).Batch.Results;
            Assert.Equal(
                "Not confirmed – the installation is still running in the background (the program stopped waiting after 20 min).",
                results[0].Message);
            Assert.Equal("Not done – the previous installation is still running", results[1].Message);
        }
    }

    [Fact]
    public void Install_FailedButVisible_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                "Not confirmed – winget ended with code 0x80004004, but the program is on the list of installed apps; check the winget log."
                    + " winget log: " + VlcLog,
                InstallText.FailedButVisible(unchecked((int)0x80004004), VlcLog));
            Assert.Equal(
                "Not done – winget ended with code 0x00000001.",
                InstallText.FailedWithCode(1, null));
            Assert.Equal(
                "Not done – the installer was interrupted (code 0x800704C7). If Windows showed a message about a virus or unwanted software,"
                    + " the antivirus blocked it.",
                InstallText.FailedWithCode(unchecked((int)0x800704C7), null));
        }
    }

    [Fact]
    public void Install_SourcesSubtitle_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Source: winget (Microsoft) · 1 app · installed: 0", InstallText.PageSummary(1, 0, false));
            Assert.Equal("Source: winget (Microsoft) · 2 apps · installed: 2", InstallText.PageSummary(2, 2, false));
            Assert.Equal("Source: winget (Microsoft) · 33 apps · installed: 3", InstallText.PageSummary(33, 3, false));
            Assert.Equal("Sources: winget (Microsoft) and GitHub (RustDesk) · 1 app · installed: 1", InstallText.PageSummary(1, 1, true));
            Assert.Equal("Sources: winget (Microsoft) and GitHub (RustDesk) · 34 apps · installed: 0", InstallText.PageSummary(34, 0, true));
            Assert.Equal(
                "Sources: GitHub (RustDesk) · winget (Microsoft) unavailable · 1 app · installed: 0",
                InstallText.PageWingetUnavailable(1, 0));
            Assert.Equal(
                "Sources: GitHub (RustDesk) · winget (Microsoft) unavailable · 4 apps · installed: 1",
                InstallText.PageWingetUnavailable(4, 1));
            Assert.Equal("Source: winget (Microsoft) · unavailable", InstallText.PageUnavailable);
        }
    }

    [Fact]
    public void Install_SelectedEmpty_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Nothing is selected. Select apps on the list or in the winget results.", InstallText.SelectedEmpty);
            Assert.Equal("Selected", InstallText.SelectedGroupName);
            Assert.Equal("Selected", InstallGroup.Selected.Name);
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x8A150109u)]
    [InlineData(0x8A150061u)]
    [InlineData(0x8A15010Du)]
    [InlineData(0x8A15002Bu)]
    [InlineData(0x8A15010Au)]
    [InlineData(0x8A15000Fu)]
    [InlineData(0x8A150107u)]
    [InlineData(0x8A150008u)]
    [InlineData(0x80072EE7u)]
    [InlineData(0x8A150056u)]
    [InlineData(0x8A150102u)]
    [InlineData(0x8A150014u)]
    [InlineData(0x8A150010u)]
    [InlineData(0x8A150011u)]
    [InlineData(0x8A150105u)]
    [InlineData(0x8A15003Au)]
    [InlineData(0x8A150041u)]
    [InlineData(0x8A150046u)]
    [InlineData(0x800704C7u)]
    [InlineData(0x800700E1u)]
    [InlineData(0x800700E2u)]
    [InlineData(0x80004004u)]
    public void WingetExitCodes_Messages_English(uint code)
    {
        var exitCode = unchecked((int)code);
        string polish;
        using (L.Use(UiLanguage.Polish))
        {
            polish = WingetExitCodes.Describe(exitCode);
        }

        using (L.Use(UiLanguage.English))
        {
            var english = WingetExitCodes.Describe(exitCode);
            Assert.False(string.IsNullOrWhiteSpace(english));
            Assert.NotEqual(polish, english);
            if (WingetExitCodes.Classify(exitCode) is not (WingetVerdict.Success or WingetVerdict.SuccessRebootRequired or WingetVerdict.AlreadyInstalled))
            {
                Assert.StartsWith("Not done – ", english, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void WingetExitCodes_ExactTexts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("winget reported success.", WingetExitCodes.Describe(0));
            Assert.Equal("winget reported success, a restart is needed.", WingetExitCodes.Describe(unchecked((int)0x8A150109)));
            Assert.Equal("Already installed.", WingetExitCodes.Describe(unchecked((int)0x8A150061)));
            Assert.Equal("Not done – a restart is required before installing.", WingetExitCodes.Describe(unchecked((int)0x8A15010A)));
            Assert.Equal(
                "Not done – this installer doesn't work with administrator rights; install the app manually.",
                WingetExitCodes.Describe(unchecked((int)0x8A150056)));
            Assert.Equal("Not done – not enough disk space.", WingetExitCodes.Describe(unchecked((int)0x8A150105)));
            Assert.Equal("Not done – the antivirus blocked the installer as a threat.", WingetExitCodes.Describe(unchecked((int)0x800700E1)));
        }
    }

    [Fact]
    public void WingetStatus_Texts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal(
                "Installing only works on the signed-in user's account: run DSOptimizer on account dso.",
                InstallText.OtherAccount("dso", "Administrator"));
            Assert.Equal(
                "Installing only works on the signed-in user's account: run DSOptimizer on account dso"
                    + " (Administrator protection: the elevated program runs on hidden account ADMIN_dso).",
                InstallText.OtherAccount("dso", "ADMIN_dso"));
            Assert.Equal(
                "winget (App Installer) isn't registered for this account. Use \"Repairs → Repair winget\".",
                InstallText.NotRegistered);
            Assert.Equal("winget works (version v1.11.510).", InstallText.WingetReady("v1.11.510"));
            Assert.Equal(
                "winget doesn't work (winget --version: code 0x80070005). Use \"Repairs → Repair winget\".",
                InstallText.WingetNotWorking(InstallText.VersionExitCode(unchecked((int)0x80070005))));
            Assert.Equal(
                "winget doesn't work (winget --version didn't respond within 15 s). Use \"Repairs → Repair winget\".",
                InstallText.WingetNotWorking(InstallText.VersionTimedOut(TimeSpan.FromSeconds(15))));
            Assert.Equal("winget --version: code 0, but there's no version number in the output", InstallText.VersionWithoutNumber);
            Assert.Equal("couldn't start winget: access denied", InstallText.VersionStartFailed("access denied"));
            Assert.Equal("Couldn't read the list of installed apps: x", InstallText.ReadFailed("x"));
        }
    }

    [Fact]
    public void WingetAvailability_NotWorking_WholeSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var runner = new FakeWingetRunner().Enqueue(FakeWingetRunner.Exit(0, "no version here"));
            var status = WingetAvailability.Check(
                new FakeUserContext { Sid = UserSid },
                UserSid,
                new[] { WingetAvailabilityTests.AppInstaller() },
                runner,
                _ => "dso");

            Assert.Equal(WingetState.NotWorking, status.State);
            Assert.Equal(
                "winget doesn't work (winget --version: code 0, but there's no version number in the output). Use \"Repairs → Repair winget\".",
                status.Text);
        }
    }

    [Fact]
    public void SelectionBar_CountsAndSources_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Double-click a row or checkbox to select it for installation.", InstallText.SelectionBarTitle(0));
            Assert.Equal("1 app selected", InstallText.SelectionBarTitle(1));
            Assert.Equal("2 apps selected", InstallText.SelectionBarTitle(2));
            Assert.Equal("22 apps selected", InstallText.SelectionBarTitle(22));
            Assert.Equal(string.Empty, InstallText.SelectionBarDetails(0, false, false));
            Assert.Equal(
                "From 1 category, downloaded from winget (Microsoft). Requires an internet connection.",
                InstallText.SelectionBarDetails(1, true, false));
            Assert.Equal(
                "From 1 category, downloaded from RustDesk's GitHub. Requires an internet connection.",
                InstallText.SelectionBarDetails(1, false, true));
            Assert.Equal(
                "From 1 category, downloaded from winget (Microsoft) and from RustDesk's GitHub. Requires an internet connection.",
                InstallText.SelectionBarDetails(1, true, true));
            Assert.Equal(
                "From 2 categories, downloaded from winget (Microsoft). Requires an internet connection.",
                InstallText.SelectionBarDetails(2, true, false));
            Assert.Equal(
                "From 5 categories, downloaded from RustDesk's GitHub. Requires an internet connection.",
                InstallText.SelectionBarDetails(5, false, true));
            Assert.Equal(
                "From 2 categories, downloaded from winget (Microsoft) and from RustDesk's GitHub. Requires an internet connection.",
                InstallText.SelectionBarDetails(2, true, true));
            Assert.Equal("Install (1)", InstallText.InstallButton(1));
            Assert.Equal("Install (12)", InstallText.InstallButton(12));
        }
    }

    [Fact]
    public void BatchResult_RebootAndSummary_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("1 app needs the computer to restart.", InstallText.RebootText(1));
            Assert.Equal("2 apps need the computer to restart.", InstallText.RebootText(2));
            Assert.Equal("5 apps need the computer to restart.", InstallText.RebootText(5));
            Assert.Equal("0 of 3 installed", InstallText.BatchSummary(0, 3));
            Assert.Equal("2 of 2 installed", InstallText.BatchSummary(2, 2));
            Assert.Equal("Installed", InstallText.OutcomeName(TweakOutcome.Installed));
            Assert.Equal("Already installed", InstallText.OutcomeName(TweakOutcome.AlreadyInstalled));
            Assert.Equal("Installed (reported by winget)", InstallText.OutcomeName(TweakOutcome.InstalledUnverified));
            Assert.Equal("Not confirmed", InstallText.OutcomeName(TweakOutcome.NotConfirmed));
            Assert.Equal("Not done", InstallText.OutcomeName(TweakOutcome.Skipped));
        }
    }

    [Fact]
    public void PageTexts_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Install", InstallText.PageTitle);
            Assert.Equal("Refresh", InstallText.RefreshButton);
            Assert.Equal("Search apps, e.g. VLC", InstallText.SearchPlaceholder);
            Assert.Equal("Search apps on the list, e.g. VLC", InstallText.SearchPlaceholderListOnly);
            Assert.Equal("Also search winget", InstallText.SearchButton);
            Assert.Equal("From the list (3)", InstallText.CatalogSection(3));
            Assert.Equal("In winget (12)", InstallText.WingetSection(12));
            Assert.Equal("Not on the list. Press Enter to search winget.", InstallText.NoCatalogMatch);
            Assert.Equal("Not on the list. Winget doesn't work – searching winget isn't available.", InstallText.NoCatalogMatchWingetDown);
            Assert.Equal("Not on the list.", InstallText.NoCatalogMatchPlain);
            Assert.Equal("RustDesk is installed from the list: use install.rustdesk.", InstallText.CliGitHubAlias("RustDesk", "install.rustdesk"));
            Assert.Equal("VLC is on the list – above", InstallText.OnListNote("VLC"));
            Assert.Equal("RustDesk is on the list – above (installed from GitHub)", InstallText.OnListNoteGitHub("RustDesk"));
            Assert.Equal("Go to: Repair winget", InstallText.RepairWingetButton);
            Assert.Equal("Stop after this app", InstallText.StopText);
            Assert.Equal("Stopping after the current app…", InstallText.StoppingText);
            Assert.Equal("The current installation always finishes.", InstallText.StopNote);
            Assert.Equal("Remote support", InstallText.CategoryName(InstallCategory.RemoteHelp));
            Assert.Equal("Messaging", InstallText.CategoryName(InstallCategory.Messengers));
            Assert.Equal("Runtimes", InstallText.CategoryName(InstallCategory.Runtimes));
            Assert.Equal("All", InstallGroup.All.Name);
            Assert.Equal("Search results", InstallGroup.Search.Name);
        }
    }

    [Fact]
    public void Search_Messages_English()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.True(WingetArguments.TryValidateQuery("vlc", out _));
            Assert.False(WingetArguments.TryValidateQuery(" ", out var empty));
            Assert.Equal("Type what to search for.", empty);
            Assert.False(WingetArguments.TryValidateQuery(new string('a', 101), out var tooLong));
            Assert.Equal("The query can have at most 100 characters.", tooLong);
            Assert.False(WingetArguments.TryValidateQuery("a\"b", out var quote));
            Assert.Equal("The query can't contain quotation marks or control characters.", quote);
            Assert.False(WingetArguments.TryValidateQuery("--source", out var dash));
            Assert.Equal("The query can't start with \"-\".", dash);
            Assert.Equal("The search failed (code 0x8A150001).", InstallText.SearchFailed(unchecked((int)0x8A150001)));
            Assert.Equal("winget didn't respond within 2 min.", InstallText.SearchTimedOut(TimeSpan.FromSeconds(90)));
            Assert.Equal("Couldn't start winget: x", InstallText.SearchStartFailed("x"));
            Assert.Equal("package name shortened – narrow the search", InstallText.TruncatedIdNote);
        }
    }

    [Fact]
    public void Confirmation_SubtitleFactsAndWarnings_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new SessionFixture();

            var one = f.Session.ConfirmationFor(new[] { Vlc });
            Assert.Equal("Install selected", one.Title);
            Assert.Equal("1 app", one.Subtitle);
            Assert.Equal("Install (1)", one.PrimaryText);
            Assert.Equal(
                new[]
                {
                    "The apps will be downloaded from the official winget source (Microsoft) and installed without installer windows."
                        + " Requires an internet connection. Installation can take from a few to over ten minutes.",
                },
                one.Warnings);
            Assert.Equal(
                new[]
                {
                    new SeriesFact("Source", "winget (Microsoft)"),
                    new SeriesFact("Restore point", "no – to undo an installation, remove the app on the \"Apps\" page"),
                    new SeriesFact("Restart", "only if the installer asks for it"),
                },
                one.Facts);
            Assert.Equal(new SeriesFact("winget ID", "VideoLAN.VLC"), one.Items.Single().Technical.Single());

            var two = f.Session.ConfirmationFor(new[] { RustDesk, Vlc });
            Assert.Equal("2 apps", two.Subtitle);
            Assert.Equal("Install (2)", two.PrimaryText);
            Assert.Equal("winget (Microsoft); GitHub RustDesk (rustdesk/rustdesk), PURSLANE signature", two.Facts[0].Value);
            Assert.Equal(
                "RustDesk will be downloaded and installed without an installer window only after its SHA256 hash and publisher signature are checked.",
                two.Warnings[1]);

            var rustDesk = f.Session.ConfirmationFor(new[] { RustDesk });
            Assert.Equal(
                "RustDesk will be downloaded and installed without an installer window only after its SHA256 hash and publisher signature are checked."
                    + " Requires an internet connection.",
                rustDesk.Warnings.Single());
            Assert.Equal(new SeriesFact("GitHub repository", "rustdesk/rustdesk"), rustDesk.Items.Single().Technical.Single());

            var search = f.Session.ConfirmationFor(new[] { InstallTarget.FromSearch("Contoso.App", "Contoso") });
            Assert.Equal("winget search result (not on the list).", search.Items.Single().Summary);
        }
    }

    [Fact]
    public void Session_StoppedBeforeStart_OneSentenceEnglish()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new SessionFixture();
            var stop = new BatchStop();
            stop.Request();

            var report = f.Session.Install(new[] { Vlc, Zip }, null, stop);

            Assert.Equal(
                new[] { "VLC: Not done – skipped because the batch was stopped.", "7-Zip: Not done – skipped because the batch was stopped." },
                report.Lines.Select(line => line.Text));
            Assert.All(report.Batch!.Results, result => Assert.Equal(TweakEngine.StoppedMessage, result.Message));
        }
    }

    [Fact]
    public void Session_ResultLines_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new SessionFixture { Result = FakeWingetRunner.Exit(0x8A150105u) };

            var report = f.Session.Install(new[] { Vlc }, null, null);

            Assert.Equal(
                "VLC: Not done – not enough disk space. Code 0x8A150105. winget log: " + VlcLog,
                report.Lines.Single().Text);
        }
    }

    [Fact]
    public void Session_SearchBusyAndResults_English()
    {
        using (L.Use(UiLanguage.English))
        {
            var f = new SessionFixture { Result = FakeWingetRunner.Exit(0x8A150014u) };
            Assert.Equal("Nothing found in winget.", f.Session.Search("vlc").Message);

            f.Result = FakeWingetRunner.Exit(0x8A15000Fu);
            Assert.Equal("No connection to the internet or to the winget source.", f.Session.Search("vlc").Message);

            f.Result = FakeWingetRunner.Exit(0x8A150001u);
            Assert.Equal("The search failed (code 0x8A150001).", f.Session.Search("vlc").Message);
            Assert.Equal("Type what to search for.", f.Session.Search(" ").Message);
            Assert.Equal("Another operation is in progress – try again in a moment.", InstallText.SearchBusy);
            Assert.Equal("Another operation is in progress – nothing was changed.", InstallText.InstallBusy);
        }
    }

    [Fact]
    public void Catalog_ValidationMessages_English()
    {
        using (L.Use(UiLanguage.English))
        {
            const string Valid = "\"title\": \"T\", \"summary\": \"S\", \"category\": \"tools\"";
            Assert.Equal("apps: (file), field entries: missing array \"entries\"", LoadError("{}"));
            Assert.Equal("apps: (entry 1), field entries: the element must be an object", LoadError("{ \"entries\": [1] }"));
            Assert.Equal("apps: (entry 1), field id: id is required", LoadError("{ \"entries\": [ {} ] }"));
            Assert.Equal(
                "apps: vlc, field id: id must have the form install.<name> (lower-case letters, digits, \"-\")",
                LoadError("{ \"entries\": [ { \"id\": \"vlc\" } ] }"));
            Assert.Equal(
                "apps: install.x, field github: give exactly one of the fields wingetId or github",
                LoadError("{ \"entries\": [ { \"id\": \"install.x\", \"wingetId\": \"A.B\", \"github\": \"a/b\" } ] }"));
            Assert.Equal(
                "apps: install.x, field github: the github field is allowed only for items listed in the code (install.rustdesk → rustdesk/rustdesk)",
                LoadError("{ \"entries\": [ { \"id\": \"install.x\", \"github\": \"a/b\" } ] }"));
            Assert.Equal(
                "apps: install.x, field wingetId: the field is required and can't be empty (or github for items listed in the code)",
                LoadError("{ \"entries\": [ { \"id\": \"install.x\" } ] }"));
            Assert.Equal(
                "apps: install.x, field wingetId: invalid winget ID",
                LoadError("{ \"entries\": [ { \"id\": \"install.x\", \"wingetId\": \"A B\" } ] }"));
            Assert.Equal(
                "apps: install.y, field wingetId: the winget ID appears more than once",
                LoadError("{ \"entries\": [ { \"id\": \"install.x\", \"wingetId\": \"A.B\", " + Valid
                    + ", \"match\": { \"displayName\": \"A\" }, \"source\": \"https://a\" }, { \"id\": \"install.y\", \"wingetId\": \"a.b\" } ] }"));
            Assert.Equal(
                "apps: install.x, field category: allowed: browsers, messengers, multimedia, documents, tools, remote-help, games, runtimes",
                LoadError("{ \"entries\": [ { \"id\": \"install.x\", \"wingetId\": \"A.B\", \"title\": \"T\", \"summary\": \"S\", \"category\": \"x\" } ] }"));
            Assert.Equal(
                "apps: install.x, field title: at most 60 characters",
                LoadError("{ \"entries\": [ { \"id\": \"install.x\", \"wingetId\": \"A.B\", \"title\": \"" + new string('t', 61) + "\" } ] }"));
            Assert.Equal(
                "apps: install.x, field match: an object with a match rule is required",
                LoadError("{ \"entries\": [ { \"id\": \"install.x\", \"wingetId\": \"A.B\", " + Valid + " } ] }"));
            Assert.Equal(
                "apps: install.x, field source: the address must be an absolute https URL",
                LoadError("{ \"entries\": [ { \"id\": \"install.x\", \"wingetId\": \"A.B\", " + Valid
                    + ", \"match\": { \"displayName\": \"A\" }, \"source\": \"http://a\" } ] }"));
        }
    }

    private static string LoadError(string json) => Assert.Throws<InstallCatalogException>(() => AppsInstallCatalog.Load(json)).Message;

    /// <summary>Sesja strony „Instalacja” na fake'ach (wzorzec <c>InstallSessionTests</c>): winget gotowy, program pojawia się po instalacji.</summary>
    private sealed class SessionFixture
    {
        public SessionFixture()
        {
            Engine.User.Sid = UserSid;
            Names.Names[UserSid] = "dso";
            Reader.Apps.Add(WingetAvailabilityTests.AppInstaller());
            Runner.Default = arguments => arguments == "--version" ? FakeWingetRunner.Exit(0, "v1.11.510") : Result;
            Gui = new GuiSession(new LoadedCatalog(new[] { TestTweaks.Scripted() }, "sha256:test"), Engine.CreateServices());
            var engine = new AppInstallEngine(Runner, Logs, Reader, Engine.Journal, Engine.User, UserSid, Names.NameOf, Engine.Log, Engine.Clock, _ => { });
            Session = new InstallSession(Gui, engine, Catalog, Reader, Runner, Engine.User, UserSid, Names, Engine.Clock);
        }

        /// <summary>Wynik każdego uruchomienia winget poza <c>--version</c> (install, search, list).</summary>
        public WingetRun Result { get; set; } = FakeWingetRunner.Exit(0);

        public EngineFixture Engine { get; } = new();

        public FakeWingetRunner Runner { get; } = new();

        public FakeWingetLogs Logs { get; } = new();

        public FakeInstalledAppsReader Reader { get; } = new();

        public FakeAccountNames Names { get; } = new();

        public GuiSession Gui { get; }

        public InstallSession Session { get; }
    }
}
