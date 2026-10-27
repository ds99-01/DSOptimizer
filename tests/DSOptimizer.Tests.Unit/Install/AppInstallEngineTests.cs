using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Journal;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class AppInstallEngineTests
{
    private const string UserSid = WingetAvailabilityTests.UserSid;
    private const string NoNetwork = "Nie wykonano – brak połączenia z internetem albo ze źródłem winget";
    private const string StillRunning = "Nie wykonano – poprzednia instalacja nadal trwa";

    private static readonly AppsInstallCatalog Catalog = InstallOrderTests.Catalog(
        ("install.7zip", "7zip.7zip", "tools", "7-Zip*"),
        ("install.vlc", "VideoLAN.VLC", "multimedia", "VLC media player"),
        ("install.firefox", "Mozilla.Firefox", "browsers", "Mozilla Firefox*"));

    private static InstallTarget Zip => InstallTarget.FromEntry(Catalog.Find("install.7zip")!);

    private static InstallTarget Vlc => InstallTarget.FromEntry(Catalog.Find("install.vlc")!);

    private static InstallTarget Firefox => InstallTarget.FromEntry(Catalog.Find("install.firefox")!);

    private static string Log(string wingetId, int n) => @"C:\dso-logs\winget\" + wingetId + "-" + n + ".log";

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

    private sealed class Fixture
    {
        public Fixture()
        {
            Reader.Apps.Add(WingetAvailabilityTests.AppInstaller());
            Runner.Enqueue(FakeWingetRunner.Exit(0, "v1.11.510"));
        }

        public FakeWingetRunner Runner { get; } = new();

        public FakeWingetLogs Logs { get; } = new();

        public FakeInstalledAppsReader Reader { get; } = new();

        public FakeJournalStore Journal { get; } = new();

        public FakeUserContext User { get; } = new() { Sid = UserSid };

        public string ProcessSid { get; set; } = UserSid;

        public ListLog Log { get; } = new();

        public FakeClock Clock { get; } = new();

        /// <summary>Fałszywy sleep: przesuwa zegar zamiast czekać; zapisuje odstępy.</summary>
        public List<TimeSpan> Sleeps { get; } = new();

        /// <summary>Wywoływane przy każdym sleep (np. dodanie programu na listę po N sekundach).</summary>
        public Action<int>? OnSleep { get; set; }

        public void Sleep(TimeSpan span)
        {
            Sleeps.Add(span);
            Clock.UtcNow += span;
            OnSleep?.Invoke(Sleeps.Count);
        }

        public ListProgress<BatchStepEvent> Steps { get; } = new();

        public BatchStop Stop { get; } = new();

        public TimeSpan? Limit { get; set; }

        /// <summary>Instalacja, po której program <paramref name="displayName"/> pojawia się na liście zainstalowanych.</summary>
        public Func<string, WingetRun> Installs(string displayName, int code = 0, params string[] lines)
            => _ =>
            {
                Reader.Apps.Add(Program(displayName));
                return FakeWingetRunner.Exit(code, lines);
            };

        public InstallBatch Install(params InstallTarget[] targets) => Install(CancellationToken.None, targets);

        public InstallBatch Install(CancellationToken ct, params InstallTarget[] targets)
        {
            var engine = new AppInstallEngine(Runner, Logs, Reader, Journal, User, ProcessSid, sid => sid == UserSid ? "dso" : null, Log, Clock, Sleep);
            var options = new InstallOptions { Steps = Steps, Stop = Stop };
            if (Limit is { } limit)
            {
                options = new InstallOptions { Steps = Steps, Stop = Stop, PerAppLimit = limit };
            }

            return engine.Install(targets, options, ct);
        }

        public List<string> InstallCalls => Runner.Calls.Where(c => c.Arguments.StartsWith("install ", StringComparison.Ordinal)).Select(c => c.Arguments).ToList();
    }

    private static void AssertEntry(JournalEntry entry, InstallTarget target, JournalEntryStatus status, string outcome, string? message = null)
    {
        Assert.Equal(JournalEntry.AppInstallKind, entry.Kind);
        Assert.Equal(target.Id, entry.TweakId);
        Assert.Equal(target.Title, entry.Title);
        Assert.Equal(UserSid, entry.UserSid);
        Assert.Equal(status, entry.Status);
        Assert.Equal(new[] { "Started", outcome }, entry.Events.Select(e => e.Outcome));
        Assert.All(entry.Events, e => Assert.Equal(AppInstallEngine.JournalAction, e.Action));
        Assert.Equal("winget: " + target.WingetId, entry.Events[0].Message);
        if (message is not null)
        {
            Assert.Equal(message, entry.Events[1].Message);
        }
    }

    [Fact]
    public void HappyPath_TwoItems_InstalledAndJournalled()
    {
        var f = new Fixture();
        f.Runner
            .Enqueue(f.Installs("7-Zip 26.05 (x64)", 0, InstallSamples.Lines("spike-net-install-7zip.bin").ToArray()))
            .Enqueue(f.Installs("VLC media player", 0, InstallSamples.Lines("spike-net-install-vlc.bin").ToArray()));
        var atStart = new List<(int Entries, JournalEntryStatus Status, string LastOutcome)>();
        f.Runner.OnRun = args =>
        {
            if (args.StartsWith("install ", StringComparison.Ordinal))
            {
                var entry = f.Journal.Current.Entries.Last();
                atStart.Add((f.Journal.Current.Entries.Count, entry.Status, entry.Events.Last().Outcome));
            }
        };

        var batch = f.Install(Zip, Vlc);

        Assert.Equal(
            new[] { WingetArguments.Version(), WingetArguments.Install("7zip.7zip", Log("7zip.7zip", 1)), WingetArguments.Install("VideoLAN.VLC", Log("VideoLAN.VLC", 2)) },
            f.Runner.Calls.Select(c => c.Arguments));
        Assert.Equal(InstallTimeouts.PerApp, f.Runner.Calls[1].WaitLimit);
        Assert.True(f.Runner.Calls[1].HasOnLine);
        Assert.Equal(new[] { (1, JournalEntryStatus.Aborted, "Started"), (2, JournalEntryStatus.Aborted, "Started") }, atStart);

        Assert.Equal(WingetState.Ready, batch.Winget.State);
        Assert.Equal(new[] { TweakOutcome.Installed, TweakOutcome.Installed }, batch.Items.Select(i => i.Outcome));
        Assert.Equal(new[] { "install.7zip", "install.vlc" }, batch.Batch.Results.Select(r => r.TweakId));
        Assert.All(batch.Batch.Results, r => Assert.True(r.IsSuccess));
        var zip = batch.Items[0];
        Assert.Equal(("install.7zip", "7zip.7zip", "Tytuł install.7zip"), (zip.Id, zip.WingetId, zip.Title));
        Assert.Equal(0, zip.WingetExitCode);
        Assert.Equal("26.05", zip.Version);
        Assert.Equal(Log("7zip.7zip", 1), zip.LogPath);
        Assert.False(zip.RequiresReboot);
        Assert.Equal("Zainstalowano i sprawdzono.", zip.Message);
        Assert.Equal(zip.Message, batch.Batch.Results[0].Message);
        Assert.Equal("3.0.24", batch.Items[1].Version);

        var entries = f.Journal.Current.Entries;
        Assert.Equal(2, entries.Count);
        AssertEntry(entries[0], Zip, JournalEntryStatus.Active, "Installed", "winget: 7zip.7zip, wersja 26.05, log: " + Log("7zip.7zip", 1));
        AssertEntry(entries[1], Vlc, JournalEntryStatus.Active, "Installed", "winget: VideoLAN.VLC, wersja 3.0.24, log: " + Log("VideoLAN.VLC", 2));
        Assert.Equal(f.Clock.UtcNow, entries[0].AppliedAt);

        var phases = f.Steps.Items.Where(s => s.Phase != BatchStepPhase.Progress).Select(s => (s.Index, s.Count, s.Kind, s.TweakId, s.Phase)).ToList();
        Assert.Equal(
            new[]
            {
                (0, 2, BatchStepKind.Item, (string?)"install.7zip", BatchStepPhase.Started),
                (0, 2, BatchStepKind.Item, (string?)"install.7zip", BatchStepPhase.Finished),
                (1, 2, BatchStepKind.Item, (string?)"install.vlc", BatchStepPhase.Started),
                (1, 2, BatchStepKind.Item, (string?)"install.vlc", BatchStepPhase.Finished),
            },
            phases);
        Assert.Equal(TweakOutcome.Installed, f.Steps.Items.Last().Result!.Outcome);
        Assert.Equal(new[] { UserSid, UserSid, UserSid, UserSid, UserSid }, f.Reader.ReadSids);
    }

    [Fact]
    public void OtherAccount_AllRefused_NothingRun()
    {
        var f = new Fixture { ProcessSid = "S-1-5-21-500" };

        var batch = f.Install(Zip, Vlc);

        Assert.Empty(f.Runner.Calls);
        Assert.Empty(f.Logs.Calls);
        Assert.Empty(f.Journal.Current.Entries);
        Assert.Empty(f.Steps.Items);
        Assert.Equal(WingetState.OtherAccount, batch.Winget.State);
        const string text = "Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie dso.";
        Assert.All(batch.Items, item =>
        {
            Assert.Equal(TweakOutcome.Refused, item.Outcome);
            Assert.Equal(text, item.Message);
            Assert.Null(item.WingetExitCode);
            Assert.Null(item.LogPath);
            Assert.Equal(CliExitCode.OperationFailed, CliExitCodes.FromOutcome(item.Outcome));
        });
        Assert.All(batch.Batch.Results, r => Assert.Equal(TweakOutcome.Refused, r.Outcome));
        Assert.Equal(2, batch.Batch.Results.Count);
    }

    [Fact]
    public void UserUncertain_Refused()
    {
        var f = new Fixture();
        f.User.IsCertain = false;

        var batch = f.Install(Zip);

        Assert.Empty(f.Runner.Calls);
        Assert.Equal(WingetState.UserUncertain, batch.Winget.State);
        Assert.Equal("Nie udało się ustalić zalogowanego użytkownika.", Assert.Single(batch.Items).Message);
        Assert.Equal(TweakOutcome.Refused, batch.Items[0].Outcome);
    }

    [Fact]
    public void NotRegistered_NoVersionCall()
    {
        var f = new Fixture();
        f.Reader.Apps.Clear();

        var batch = f.Install(Zip);

        Assert.Empty(f.Runner.Calls);
        Assert.Equal(WingetState.NotRegistered, batch.Winget.State);
        Assert.Equal(TweakOutcome.Refused, Assert.Single(batch.Items).Outcome);
        Assert.Equal(batch.Winget.Text, batch.Items[0].Message);
    }

    [Fact]
    public void VersionFails_NotWorking_NothingInstalled()
    {
        var f = new Fixture();
        f.Runner.Calls.Clear();
        var runner = new FakeWingetRunner().Enqueue(FakeWingetRunner.Exit(5));
        var engine = new AppInstallEngine(runner, f.Logs, f.Reader, f.Journal, f.User, UserSid, _ => null, f.Log, f.Clock);

        var batch = engine.Install(new[] { Zip }, new InstallOptions(), CancellationToken.None);

        Assert.Equal(WingetState.NotWorking, batch.Winget.State);
        Assert.Single(runner.Calls);
        Assert.Equal(TweakOutcome.Refused, Assert.Single(batch.Items).Outcome);
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Fact]
    public void ReaderThrows_RefusedWithReason_NoWingetCall()
    {
        var f = new Fixture();
        f.Reader.ReadThrows = new InvalidOperationException("rejestr niedostępny");

        var batch = f.Install(Zip);

        Assert.Empty(f.Runner.Calls);
        Assert.Equal(WingetState.NotWorking, batch.Winget.State);
        Assert.Equal(TweakOutcome.Refused, Assert.Single(batch.Items).Outcome);
        Assert.Contains("rejestr niedostępny", batch.Items[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderThrows_OtherAccountStillWins()
    {
        var f = new Fixture { ProcessSid = "S-1-5-21-500" };
        f.Reader.ReadThrows = new InvalidOperationException("rejestr niedostępny");

        Assert.Equal(WingetState.OtherAccount, f.Install(Zip).Winget.State);
        Assert.Empty(f.Runner.Calls);
    }

    [Fact]
    public void ReaderWarnings_InBatchWarnings()
    {
        var f = new Fixture();
        f.Reader.Warnings.Add("AppX: błąd odczytu");
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)"));

        var batch = f.Install(Zip);

        Assert.Contains("AppX: błąd odczytu", batch.Batch.Warnings);
    }

    [Fact]
    public void MatchedBeforeRun_AlreadyInstalled_WingetNotStarted()
    {
        var f = new Fixture();
        var runtime = Program("7-Zip 24.09 (x64)") with
        {
            Win32 = Program("x").Win32! with { SystemComponent = true },
        };
        f.Reader.Apps.Add(runtime);
        f.Runner.Enqueue(f.Installs("VLC media player"));

        var batch = f.Install(Zip, Vlc);

        Assert.Equal(new[] { WingetArguments.Install("VideoLAN.VLC", Log("VideoLAN.VLC", 1)) }, f.InstallCalls);
        var zip = batch.Items[0];
        Assert.Equal(TweakOutcome.AlreadyInstalled, zip.Outcome);
        Assert.Equal("Była już zainstalowana – nic nie uruchomiono.", zip.Message);
        Assert.Null(zip.WingetExitCode);
        Assert.Null(zip.LogPath);
        Assert.True(batch.Batch.Results[0].IsSuccess);
        Assert.Equal(CliExitCode.Success, CliExitCodes.FromOutcome(zip.Outcome));
        Assert.Equal("install.vlc", Assert.Single(f.Journal.Current.Entries).TweakId);
        Assert.Equal(new[] { "VideoLAN.VLC" }, f.Logs.Calls.Select(c => c.WingetId));
    }

    [Fact]
    public void NetworkOnFirst_RestSkippedNotRun()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A15000Fu, InstallSamples.Lines("spike-off-install-7zip.bin").ToArray()));

        var batch = f.Install(Zip, Vlc, Firefox);

        Assert.Single(f.InstallCalls);
        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.StartsWith(NoNetwork, batch.Items[0].Message, StringComparison.Ordinal);
        Assert.Equal(unchecked((int)0x8A15000F), batch.Items[0].WingetExitCode);
        foreach (var item in batch.Items.Skip(1))
        {
            Assert.Equal(TweakOutcome.Skipped, item.Outcome);
            Assert.Equal(NoNetwork, item.Message);
            Assert.Null(item.LogPath);
        }

        AssertEntry(Assert.Single(f.Journal.Current.Entries), Zip, JournalEntryStatus.Aborted, "Failed", "winget: 7zip.7zip, log: " + Log("7zip.7zip", 1));
        Assert.Equal(2, f.Steps.Items.Count(s => s.Phase != BatchStepPhase.Progress));
    }

    [Fact]
    public void NetworkOnFirstRun_AfterMatchedItem_EndsSeries()
    {
        var f = new Fixture();
        f.Reader.Apps.Add(Program("7-Zip 26.05 (x64)"));
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A150107u));

        var batch = f.Install(Zip, Vlc, Firefox);

        Assert.Single(f.InstallCalls);
        Assert.Equal(new[] { TweakOutcome.AlreadyInstalled, TweakOutcome.Failed, TweakOutcome.Skipped }, batch.Items.Select(i => i.Outcome));
        Assert.Equal(NoNetwork, batch.Items[2].Message);
    }

    [Fact]
    public void NetworkLostAfterFirstSuccess_EachRemainingReportsNoNetwork()
    {
        var f = new Fixture();
        f.Runner
            .Enqueue(f.Installs("7-Zip 26.05 (x64)"))
            .Enqueue(FakeWingetRunner.Exit(0x8A150107u))
            .Enqueue(FakeWingetRunner.Exit(0x8A150008u));

        var batch = f.Install(Zip, Vlc, Firefox);

        Assert.Equal(3, f.InstallCalls.Count);
        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
        foreach (var item in batch.Items.Skip(1))
        {
            Assert.Equal(TweakOutcome.Failed, item.Outcome);
            Assert.StartsWith(NoNetwork, item.Message, StringComparison.Ordinal);
            Assert.NotNull(item.LogPath);
            Assert.Contains(item.LogPath!, item.Message, StringComparison.Ordinal);
            Assert.Equal(CliExitCode.OperationFailed, CliExitCodes.FromOutcome(item.Outcome));
        }

        Assert.DoesNotContain(batch.Items.Skip(1), i => i.Outcome is TweakOutcome.Installed or TweakOutcome.InstalledUnverified);
        var entries = f.Journal.Current.Entries;
        Assert.Equal(3, entries.Count);
        Assert.Equal(JournalEntryStatus.Active, entries[0].Status);
        AssertEntry(entries[1], Vlc, JournalEntryStatus.Aborted, "Failed");
        AssertEntry(entries[2], Firefox, JournalEntryStatus.Aborted, "Failed");
    }

    [Fact]
    public void Timeout_NotConfirmed_SeriesEnds_RestNotStarted()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.TimedOut("Found 7-Zip [7zip.7zip] Version 26.05"));

        var batch = f.Install(Zip, Vlc, Firefox);

        Assert.Single(f.InstallCalls);
        var zip = batch.Items[0];
        Assert.Equal(TweakOutcome.NotConfirmed, zip.Outcome);
        Assert.Equal("Nie potwierdzono – instalacja nadal trwa w tle (program przestał czekać po 30 min).", zip.Message);
        Assert.Null(zip.WingetExitCode);
        Assert.Equal(Log("7zip.7zip", 1), zip.LogPath);
        foreach (var item in batch.Items.Skip(1))
        {
            Assert.Equal(TweakOutcome.Skipped, item.Outcome);
            Assert.Equal(StillRunning, item.Message);
        }

        AssertEntry(
            Assert.Single(f.Journal.Current.Entries),
            Zip,
            JournalEntryStatus.Aborted,
            "NotConfirmed",
            "winget: 7zip.7zip, wersja 26.05, log: " + Log("7zip.7zip", 1));
        Assert.Equal(CliExitCode.Incomplete, CliExitCodes.FromOutcome(zip.Outcome));
    }

    [Fact]
    public void Timeout_CustomLimit_PassedToRunnerAndText()
    {
        var f = new Fixture { Limit = TimeSpan.FromMinutes(7) };
        f.Runner.Enqueue(FakeWingetRunner.TimedOut());

        var batch = f.Install(Zip);

        Assert.Equal(TimeSpan.FromMinutes(7), f.Runner.Calls[1].WaitLimit);
        Assert.Contains("po 7 min", batch.Items[0].Message, StringComparison.Ordinal);
    }

    /// <summary>Brak instalatora (<c>0x8A150010</c>) = jedno uruchomienie, bez ponowienia (ścieżka <c>--locale</c> usunięta, R14).</summary>
    [Fact]
    public void NoApplicableInstaller_SingleAttempt_Failed()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A150010u));

        var batch = f.Install(Firefox);

        var call = Assert.Single(f.InstallCalls);
        Assert.DoesNotContain("--locale", call, StringComparison.Ordinal);
        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal(Log("Mozilla.Firefox", 1), batch.Items[0].LogPath);
    }

    [Fact]
    public void SuccessButNotVisible_NotConfirmed()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0));

        var batch = f.Install(Zip);

        var item = Assert.Single(batch.Items);
        Assert.Equal(TweakOutcome.NotConfirmed, item.Outcome);
        Assert.StartsWith("winget zgłosił sukces, ale programu nie widać na liście zainstalowanych", item.Message, StringComparison.Ordinal);
        Assert.Contains(Log("7zip.7zip", 1), item.Message, StringComparison.Ordinal);
        AssertEntry(Assert.Single(f.Journal.Current.Entries), Zip, JournalEntryStatus.Aborted, "NotConfirmed");
    }

    [Fact]
    public void MatchAppearsAfterDelay_Installed()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0));
        f.OnSleep = n =>
        {
            if (n == 5)
            {
                f.Reader.Apps.Add(Program("7-Zip 26.05 (x64)"));
            }
        };

        var batch = f.Install(Zip);

        Assert.Equal(TweakOutcome.Installed, Assert.Single(batch.Items).Outcome);
        Assert.Equal(5, f.Sleeps.Count);
        Assert.All(f.Sleeps, s => Assert.Equal(TimeSpan.FromSeconds(1), s));
        AssertEntry(Assert.Single(f.Journal.Current.Entries), Zip, JournalEntryStatus.Active, "Installed");
    }

    [Fact]
    public void MatchNeverAppears_NotConfirmedAfter60s()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0));
        var start = f.Clock.UtcNow;

        var batch = f.Install(Zip);

        var item = Assert.Single(batch.Items);
        Assert.Equal(TweakOutcome.NotConfirmed, item.Outcome);
        Assert.StartsWith("winget zgłosił sukces, ale programu nie widać na liście zainstalowanych", item.Message, StringComparison.Ordinal);
        Assert.Equal(60, f.Sleeps.Count);
        Assert.Equal(TimeSpan.FromSeconds(60), f.Clock.UtcNow - start);
    }

    [Fact]
    public void ReaderThrowsDuringRetry_ThenFound_Installed()
    {
        var f = new Fixture();
        f.Runner.Enqueue(_ =>
        {
            f.Reader.ReadThrows = new InvalidOperationException("odczyt padł");
            return FakeWingetRunner.Exit(0);
        });
        f.OnSleep = n =>
        {
            if (n == 2)
            {
                f.Reader.ReadThrows = null;
                f.Reader.Apps.Add(Program("7-Zip 26.05 (x64)"));
            }
        };

        var batch = f.Install(Zip);

        Assert.Equal(TweakOutcome.Installed, Assert.Single(batch.Items).Outcome);
        Assert.Equal(2, f.Sleeps.Count);
    }

    [Fact]
    public void SuccessButReadbackThrows_NotConfirmed()
    {
        var f = new Fixture();
        f.Runner.Enqueue(_ =>
        {
            f.Reader.ReadThrows = new InvalidOperationException("odczyt padł");
            return FakeWingetRunner.Exit(0);
        });

        var batch = f.Install(Zip);

        Assert.Equal(TweakOutcome.NotConfirmed, batch.Items[0].Outcome);
        Assert.Contains("odczyt padł", batch.Items[0].Message, StringComparison.Ordinal);

        // M3: 61 nieudanych odczytów w oknie = jeden wpis ERROR ze stosem, ten sam tekst błędu nie wraca jako WARN.
        Assert.Equal(61, f.Reader.ReadSids.Count - 2);
        Assert.Single(f.Log.Lines, l => l.StartsWith("ERROR [Install] Odczyt listy zainstalowanych", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Log.Lines, l => l.StartsWith("WARN [Install] Odczyt listy zainstalowanych", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadbackErrorsWithChangingText_OneFailThenWarnPerChange()
    {
        var f = new Fixture();
        f.Runner.Enqueue(_ =>
        {
            f.Reader.ReadThrows = new InvalidOperationException("błąd A");
            return FakeWingetRunner.Exit(0);
        });
        f.OnSleep = n =>
        {
            if (n == 3)
            {
                f.Reader.ReadThrows = new InvalidOperationException("błąd B");
            }
        };

        f.Install(Zip);

        var readLines = f.Log.Lines.Where(l => l.Contains("Odczyt listy zainstalowanych")).ToList();
        Assert.Equal(2, readLines.Count);
        Assert.StartsWith("ERROR [Install]", readLines[0], StringComparison.Ordinal);
        Assert.Contains("błąd A", readLines[0], StringComparison.Ordinal);
        Assert.StartsWith("WARN [Install]", readLines[1], StringComparison.Ordinal);
        Assert.EndsWith("błąd B", readLines[1], StringComparison.Ordinal);
    }

    /// <summary>M3: zamrożony (albo cofnięty) zegar systemowy nie zawiesza serii – najwyżej 61 odczytów (60 s / 1 s + 1).</summary>
    [Fact]
    public void FrozenClock_ReadbackEndsAfter61Attempts()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0));
        var sleeps = 0;
        var engine = new AppInstallEngine(f.Runner, f.Logs, f.Reader, f.Journal, f.User, UserSid, _ => null, f.Log, f.Clock, _ => sleeps++);

        var batch = engine.Install(new[] { Zip }, new InstallOptions(), CancellationToken.None);

        Assert.Equal(TweakOutcome.NotConfirmed, batch.Items[0].Outcome);
        Assert.Equal(60, sleeps);
        Assert.Equal(61, f.Reader.ReadSids.Count - 2);
        Assert.Equal(61, InstallTimeouts.ReadbackAttempts);
    }

    /// <summary>M3: gdy pierwszy odczyt nie pasuje, okno serii dostaje etap „Sprawdzanie instalacji” (raz).</summary>
    [Fact]
    public void ReadbackWaiting_ReportsVerifyingStageOnce()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0, InstallSamples.Lines("spike-net-install-vlc.bin").ToArray()));
        f.OnSleep = n =>
        {
            if (n == 3)
            {
                f.Reader.Apps.Add(Program("VLC media player"));
            }
        };

        var batch = f.Install(Vlc);

        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
        var stages = f.Steps.Items.Where(s => s.Phase == BatchStepPhase.Progress).Select(s => (s.Stage, s.Percent)).ToList();
        Assert.Equal(("Sprawdzanie instalacji", (int?)null), stages.Last());
        Assert.Single(stages, s => s.Stage == "Sprawdzanie instalacji");
        Assert.Equal("Sprawdzanie instalacji", InstallText.StageVerifying);
    }

    [Fact]
    public void ReadbackMatchedAtOnce_NoVerifyingStage()
    {
        var f = new Fixture();
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)"));

        f.Install(Zip);

        Assert.DoesNotContain(f.Steps.Items, s => s.Stage == InstallText.StageVerifying);
    }

    [Fact]
    public void PreRunReadThrows_InstallStillAttempted()
    {
        var f = new Fixture();
        var reads = 0;
        var reader = new ThrowingOnSecondRead(f.Reader, () => ++reads == 2);
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)"));
        var engine = new AppInstallEngine(f.Runner, f.Logs, reader, f.Journal, f.User, UserSid, _ => null, f.Log, f.Clock);

        var batch = engine.Install(new[] { Zip }, new InstallOptions(), CancellationToken.None);

        Assert.Single(f.InstallCalls);
        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
    }

    private sealed class ThrowingOnSecondRead : DSOptimizer.Core.Abstractions.IInstalledAppsReader
    {
        private readonly FakeInstalledAppsReader _inner;
        private readonly Func<bool> _throw;

        public ThrowingOnSecondRead(FakeInstalledAppsReader inner, Func<bool> shouldThrow)
        {
            _inner = inner;
            _throw = shouldThrow;
        }

        public DSOptimizer.Core.Abstractions.InstalledAppsReading Read(string userSid)
            => _throw() ? throw new InvalidOperationException("chwilowy błąd") : _inner.Read(userSid);

        public AppPresence ReadPresence(InstalledApp app, string userSid, AppPresenceScope scope) => _inner.ReadPresence(app, userSid, scope);
    }

    [Fact]
    public void SearchItem_ListConfirms_Installed()
    {
        var f = new Fixture();
        var target = InstallTarget.FromSearch("7zip.7zip", "7-Zip");
        f.Runner
            .Enqueue(FakeWingetRunner.Exit(0))
            .Enqueue(FakeWingetRunner.Exit(0, InstallSamples.Lines("spike-net-list-7zip-after.bin").ToArray()));

        var batch = f.Install(target);

        Assert.Equal(WingetArguments.ListExact("7zip.7zip"), f.Runner.Calls[2].Arguments);
        Assert.Equal(InstallTimeouts.List, f.Runner.Calls[2].WaitLimit);
        var item = Assert.Single(batch.Items);
        Assert.Equal(TweakOutcome.Installed, item.Outcome);
        Assert.Equal("winget:7zip.7zip", item.Id);
        AssertEntry(Assert.Single(f.Journal.Current.Entries), target, JournalEntryStatus.Active, "Installed");
    }

    [Fact]
    public void SearchItem_ListIdCaseInsensitive_Installed()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0)).Enqueue(FakeWingetRunner.Exit(0, "Notepad++  NOTEPAD++.NOTEPAD++  8.8"));

        Assert.Equal(TweakOutcome.Installed, f.Install(InstallTarget.FromSearch("Notepad++.Notepad++", "Notepad++")).Items[0].Outcome);
    }

    public static IEnumerable<object[]> ListFailures()
    {
        yield return new object[] { FakeWingetRunner.Exit(0x8A150014u, "No installed package found matching input criteria.") };
        yield return new object[] { FakeWingetRunner.Exit(0, "Name Id Version", "Inna.Aplikacja") };
        yield return new object[] { FakeWingetRunner.TimedOut("7zip.7zip") };
    }

    [Theory]
    [MemberData(nameof(ListFailures))]
    public void SearchItem_ListFails_InstalledUnverified(WingetRun list)
    {
        var f = new Fixture();
        var target = InstallTarget.FromSearch("7zip.7zip", "7-Zip");
        f.Runner.Enqueue(FakeWingetRunner.Exit(0)).Enqueue(list);

        var batch = f.Install(target);

        var item = Assert.Single(batch.Items);
        Assert.Equal(TweakOutcome.InstalledUnverified, item.Outcome);
        Assert.Equal("Zainstalowano (zgłoszone przez winget).", item.Message);
        Assert.False(batch.Batch.Results[0].IsSuccess);
        Assert.Equal(CliExitCode.Incomplete, CliExitCodes.FromOutcome(item.Outcome));
        AssertEntry(Assert.Single(f.Journal.Current.Entries), target, JournalEntryStatus.Active, "InstalledUnverified");
    }

    [Fact]
    public void SearchItem_ListThrows_InstalledUnverified()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0)).Enqueue(_ => throw new OperationException(OperationErrorKind.Unexpected, "list padł"));

        Assert.Equal(TweakOutcome.InstalledUnverified, f.Install(InstallTarget.FromSearch("7zip.7zip", "7-Zip")).Items[0].Outcome);
    }

    [Fact]
    public void RebootToFinish_InstalledWithRebootAndPendingJournal()
    {
        var f = new Fixture();
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)", unchecked((int)0x8A150109u)));

        var batch = f.Install(Zip);

        var item = Assert.Single(batch.Items);
        Assert.Equal(TweakOutcome.Installed, item.Outcome);
        Assert.True(item.RequiresReboot);
        Assert.True(batch.Batch.Results[0].RequiresReboot);
        Assert.True(batch.Batch.RequiresReboot);
        var entry = Assert.Single(f.Journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.PendingReboot, entry.Status);
        Assert.Equal("Tytuł install.7zip", Assert.Single(RebootPending.ComputeAppInstalls(f.Journal.Current, f.Clock.UtcNow.AddMinutes(-1))).Title);
    }

    [Fact]
    public void RebootToFinish_SearchUnverified_PendingReboot()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A150109u)).Enqueue(FakeWingetRunner.Exit(1));

        var batch = f.Install(InstallTarget.FromSearch("Vendor.App", "App"));

        Assert.Equal(TweakOutcome.InstalledUnverified, batch.Items[0].Outcome);
        Assert.True(batch.Items[0].RequiresReboot);
        Assert.Equal(JournalEntryStatus.PendingReboot, f.Journal.Current.Entries[0].Status);
    }

    [Fact]
    public void RebootToFinish_NotVisible_NotConfirmedAbortedButReboot()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A150109u));

        var batch = f.Install(Zip);

        Assert.Equal(TweakOutcome.NotConfirmed, batch.Items[0].Outcome);
        Assert.True(batch.Items[0].RequiresReboot);
        Assert.Equal(JournalEntryStatus.Aborted, f.Journal.Current.Entries[0].Status);
    }

    [Fact]
    public void RebootBeforeInstall_FailedWithReboot()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x8A15010Au));

        var batch = f.Install(Zip, Vlc);

        var item = batch.Items[0];
        Assert.Equal(TweakOutcome.Failed, item.Outcome);
        Assert.True(item.RequiresReboot);
        Assert.StartsWith("Nie wykonano – wymaga restartu przed instalacją.", item.Message, StringComparison.Ordinal);
        Assert.Equal(JournalEntryStatus.Aborted, f.Journal.Current.Entries[0].Status);
        Assert.Equal(2, f.InstallCalls.Count);
    }

    [Theory]
    [InlineData(0x8A150061u, TweakOutcome.AlreadyInstalled, "Była już zainstalowana")]
    [InlineData(0x8A15010Du, TweakOutcome.AlreadyInstalled, "Była już zainstalowana")]
    [InlineData(0x8A15002Bu, TweakOutcome.AlreadyInstalled, "Była już zainstalowana")]
    [InlineData(0x8A15010Au, TweakOutcome.Failed, "Nie wykonano – wymaga restartu przed instalacją.")]
    [InlineData(0x8A15000Fu, TweakOutcome.Failed, "Nie wykonano – brak połączenia z internetem albo ze źródłem winget.")]
    [InlineData(0x8A150107u, TweakOutcome.Failed, "Nie wykonano – brak połączenia z internetem albo ze źródłem winget.")]
    [InlineData(0x8A150008u, TweakOutcome.Failed, "Nie wykonano – brak połączenia z internetem albo ze źródłem winget.")]
    [InlineData(0x8A150056u, TweakOutcome.Failed, "Nie wykonano – ten instalator nie działa z uprawnieniami administratora; zainstaluj aplikację ręcznie.")]
    [InlineData(0x8A150102u, TweakOutcome.Failed, "Nie wykonano – trwa inna instalacja, spróbuj za chwilę.")]
    [InlineData(0x8A150014u, TweakOutcome.Failed, "Nie wykonano – pakietu nie ma w winget albo nie pasuje do tego komputera.")]
    [InlineData(0x8A150010u, TweakOutcome.Failed, "Nie wykonano – pakietu nie ma w winget albo nie pasuje do tego komputera.")]
    [InlineData(0x8A150011u, TweakOutcome.Failed, "Nie wykonano – plik instalatora nie zgadza się z manifestem, nie zainstalowano.")]
    [InlineData(0x8A150105u, TweakOutcome.Failed, "Nie wykonano – za mało miejsca na dysku.")]
    [InlineData(0x8A15003Au, TweakOutcome.Failed, "Nie wykonano – instalację zablokowały zasady tego komputera.")]
    [InlineData(0x8A150041u, TweakOutcome.Failed, "Nie wykonano – nie udało się zaakceptować umowy pakietu albo źródła.")]
    [InlineData(0x8A150046u, TweakOutcome.Failed, "Nie wykonano – nie udało się zaakceptować umowy pakietu albo źródła.")]
    [InlineData(0x8A159999u, TweakOutcome.Failed, "Nie wykonano – winget zakończył się kodem 0x8A159999.")]
    [InlineData(1u, TweakOutcome.Failed, "Nie wykonano – winget zakończył się kodem 0x00000001.")]
    public void ExitCode_MapsToOutcome(uint code, TweakOutcome expected, string messageStart)
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(code));

        var item = Assert.Single(f.Install(Zip).Items);

        Assert.Equal(expected, item.Outcome);
        Assert.StartsWith(messageStart, item.Message, StringComparison.Ordinal);
        Assert.Equal(unchecked((int)code), item.WingetExitCode);
        Assert.Equal(Log("7zip.7zip", 1), item.LogPath);
        if (expected == TweakOutcome.Failed)
        {
            Assert.Contains(WingetExitCodes.Hex(unchecked((int)code)), item.Message, StringComparison.Ordinal);
            Assert.Contains(Log("7zip.7zip", 1), item.Message, StringComparison.Ordinal);
            Assert.Equal(1, CountOf(item.Message, WingetExitCodes.Hex(unchecked((int)code))));
        }

        var entry = Assert.Single(f.Journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Aborted, entry.Status);
        Assert.Equal(expected.ToString(), entry.Events.Last().Outcome);
    }

    /// <summary>
    /// Ruling R21 (HWMonitor na Pro, T11): nieznany kod ≠ 0, a program pozycji listy jest widoczny w jednym świeżym odczycie =
    /// <see cref="TweakOutcome.NotConfirmed"/>, nigdy <see cref="TweakOutcome.Installed"/>; bez okna 60 s.
    /// </summary>
    [Fact]
    public void UnknownCode_ProgramVisible_NotConfirmed()
    {
        var f = new Fixture();
        f.Runner
            .Enqueue(f.Installs("7-Zip 26.05 (x64)", unchecked((int)0x80004004u)))
            .Enqueue(f.Installs("VLC media player"));

        var batch = f.Install(Zip, Vlc);

        var item = batch.Items[0];
        Assert.Equal(TweakOutcome.NotConfirmed, item.Outcome);
        Assert.Equal(
            "Nie potwierdzono – winget zakończył się kodem 0x80004004, ale program jest widoczny na liście zainstalowanych; "
                + "sprawdź log winget. Log winget: " + Log("7zip.7zip", 1),
            item.Message);
        Assert.Equal(unchecked((int)0x80004004u), item.WingetExitCode);
        Assert.False(item.RequiresReboot);
        Assert.False(batch.Batch.Results[0].RequiresReboot);
        Assert.Empty(f.Sleeps);
        Assert.Equal(CliExitCode.Incomplete, CliExitCodes.FromOutcome(item.Outcome));
        AssertEntry(f.Journal.Current.Entries[0], Zip, JournalEntryStatus.Aborted, "NotConfirmed");

        // Nieznany kod nie jest kodem sieci, więc seria idzie dalej.
        Assert.Equal(2, f.InstallCalls.Count);
    }

    [Fact]
    public void CancelledCode_NotVisible_FailedWithReasonWithoutLog()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x800704C7u));

        var item = Assert.Single(f.Install(Zip).Items);

        Assert.Equal(TweakOutcome.Failed, item.Outcome);
        Assert.Equal(WingetExitCodes.Describe(unchecked((int)0x800704C7)) + " Log winget: " + Log("7zip.7zip", 1), item.Message);
        var last = Assert.Single(f.Journal.Current.Entries).Events.Last();
        Assert.Equal("Failed", last.Outcome);
        Assert.Equal(WingetExitCodes.Describe(unchecked((int)0x800704C7)), last.Reason);
        Assert.DoesNotContain("Log winget:", last.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x800704C7u)]
    [InlineData(0x800700E1u)]
    public void CancelledOrAntivirusCode_ProgramVisible_NotConfirmed(uint raw)
    {
        var f = new Fixture();
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)", unchecked((int)raw)));

        var item = Assert.Single(f.Install(Zip).Items);

        Assert.Equal(TweakOutcome.NotConfirmed, item.Outcome);
        Assert.Contains("program jest widoczny na liście zainstalowanych", item.Message, StringComparison.Ordinal);
        var last = Assert.Single(f.Journal.Current.Entries).Events.Last();
        Assert.StartsWith("Nie potwierdzono – winget zakończył się kodem", last.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Log winget:", last.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Success_JournalEventHasNoReason()
    {
        var f = new Fixture();
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)", 0, InstallSamples.Lines("spike-net-install-7zip.bin").ToArray()));

        f.Install(Zip);

        Assert.Null(Assert.Single(f.Journal.Current.Entries).Events.Last().Reason);
    }

    [Fact]
    public void UnknownCode_NotVisible_Failed()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x80004004u));

        var item = Assert.Single(f.Install(Zip).Items);

        Assert.Equal(TweakOutcome.Failed, item.Outcome);
        Assert.Equal("Nie wykonano – winget zakończył się kodem 0x80004004. Log winget: " + Log("7zip.7zip", 1), item.Message);
        Assert.Empty(f.Sleeps);

        // Dostępność winget + dopasowanie przed próbą + jeden odczyt po nieznanym kodzie.
        Assert.Equal(3, f.Reader.ReadSids.Count);
        AssertEntry(Assert.Single(f.Journal.Current.Entries), Zip, JournalEntryStatus.Aborted, "Failed");
    }

    [Fact]
    public void UnknownCode_ReadFails_Failed()
    {
        var f = new Fixture();
        f.Runner.Enqueue(_ =>
        {
            f.Reader.ReadThrows = new InvalidOperationException("odczyt padł");
            return FakeWingetRunner.Exit(0x80004004u);
        });

        var item = Assert.Single(f.Install(Zip).Items);

        Assert.Equal(TweakOutcome.Failed, item.Outcome);
        Assert.StartsWith("Nie wykonano – winget zakończył się kodem 0x80004004.", item.Message, StringComparison.Ordinal);
        Assert.Empty(f.Sleeps);
    }

    [Fact]
    public void UnknownCode_SearchItem_Failed_NoReadback()
    {
        var f = new Fixture();
        f.Runner.Enqueue(FakeWingetRunner.Exit(0x80004004u));

        var item = Assert.Single(f.Install(InstallTarget.FromSearch("Vendor.App", "App")).Items);

        Assert.Equal(TweakOutcome.Failed, item.Outcome);
        Assert.DoesNotContain(f.Runner.Calls, c => c.Arguments.StartsWith("list ", StringComparison.Ordinal));
        Assert.Equal(2, f.Runner.Calls.Count);

        // Tylko odczyt dostępności winget – pozycja z wyszukiwania nie ma reguły match.
        Assert.Single(f.Reader.ReadSids);
    }

    /// <summary>Znany kod porażki: winget jednoznacznie mówi, że nie instalował – bez odczytu, nawet gdy program jest widoczny.</summary>
    [Fact]
    public void KnownFailureCode_NoReadback()
    {
        var f = new Fixture();
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)", unchecked((int)0x8A150011u)));

        var item = Assert.Single(f.Install(Zip).Items);

        Assert.Equal(TweakOutcome.Failed, item.Outcome);
        Assert.StartsWith("Nie wykonano – plik instalatora nie zgadza się z manifestem", item.Message, StringComparison.Ordinal);
        Assert.Equal(2, f.Reader.ReadSids.Count);
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void StopBetweenItems_RestSkipped()
    {
        var f = new Fixture();
        f.Runner.Enqueue(_ =>
        {
            f.Stop.Request();
            f.Reader.Apps.Add(Program("7-Zip 26.05 (x64)"));
            return FakeWingetRunner.Exit(0);
        });

        var batch = f.Install(Zip, Vlc, Firefox);

        Assert.Single(f.InstallCalls);
        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
        Assert.All(batch.Items.Skip(1), item =>
        {
            Assert.Equal(TweakOutcome.Skipped, item.Outcome);
            Assert.Equal(TweakEngine.StoppedMessage, item.Message);
        });
        Assert.True(batch.Batch.Stopped);
        Assert.Single(f.Journal.Current.Entries);
    }

    [Fact]
    public void CancelledBeforeStart_AllSkipped_NothingInstalled()
    {
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var batch = f.Install(cts.Token, Zip, Vlc);

        Assert.Empty(f.InstallCalls);
        Assert.All(batch.Items, item => Assert.Equal(TweakOutcome.Skipped, item.Outcome));
        Assert.All(batch.Items, item => Assert.Equal(TweakEngine.StoppedMessage, item.Message));
        Assert.True(batch.Batch.Stopped);
    }

    [Fact]
    public void ProgressLinesNotLogged_StageAndPercentReported()
    {
        var f = new Fixture();
        f.Runner.Enqueue(f.Installs("VLC media player", 0, InstallSamples.Lines("spike-net-install-vlc.bin").ToArray()));

        f.Install(Vlc);

        var progress = f.Steps.Items.Where(s => s.Phase == BatchStepPhase.Progress).ToList();
        Assert.Equal(
            new (string?, int?)[] { ("Pobieranie", null), ("Pobieranie", 2), ("Pobieranie", 31), ("Pobieranie", 77), ("Pobieranie", 100), ("Instalowanie", null) },
            progress.Select(p => (p.Stage, p.Percent)));
        Assert.All(progress, p =>
        {
            Assert.Equal(("install.vlc", 0, 1, BatchStepKind.Item), (p.TweakId, p.Index, p.Count, p.Kind));
            Assert.Null(p.Result);
        });

        var installLines = f.Log.Lines.Where(l => l.StartsWith("INFO [Install] VideoLAN.VLC: ", StringComparison.Ordinal)).ToList();
        Assert.Contains("INFO [Install] VideoLAN.VLC: Found VLC media player [VideoLAN.VLC] Version 3.0.24", installLines);
        Assert.Contains("INFO [Install] VideoLAN.VLC: Successfully installed", installLines);
        Assert.Contains("INFO [Install] VideoLAN.VLC: Starting package install...", installLines);
        Assert.DoesNotContain(f.Log.Lines, l => l.Contains('█') || l.Contains('▒') || l.Contains(" MB / "));
        Assert.DoesNotContain(installLines, l => l.Substring("INFO [Install] VideoLAN.VLC: ".Length).Trim().Length <= 1);
    }

    [Fact]
    public void ProgressReceiverThrows_SeriesContinues()
    {
        var f = new Fixture();
        f.Steps.OnReport = _ => throw new InvalidOperationException("UI padło");
        f.Runner.Enqueue(f.Installs("VLC media player", 0, InstallSamples.Lines("spike-net-install-vlc.bin").ToArray()));

        Assert.Equal(TweakOutcome.Installed, f.Install(Vlc).Items[0].Outcome);
    }

    [Fact]
    public void JournalSaveFails_FailedNothingRun()
    {
        var f = new Fixture();
        f.Journal.FailSave = true;

        var batch = f.Install(Zip, Vlc);

        Assert.Empty(f.InstallCalls);
        Assert.Empty(f.Logs.Calls);
        Assert.All(batch.Items, item =>
        {
            Assert.Equal(TweakOutcome.Failed, item.Outcome);
            Assert.Equal("Nie udało się zapisać journala – instalacja nie została uruchomiona.", item.Message);
            Assert.Null(item.LogPath);
        });
    }

    [Fact]
    public void FinalJournalSaveFails_ResultKeptWithWarning()
    {
        var f = new Fixture();
        f.Journal.SaveException = new JournalStoreException("dysk pełny");
        f.Journal.ThrowOnSaveFrom = 1;
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)"));

        var batch = f.Install(Zip);

        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
        Assert.Contains(batch.Batch.Results[0].Warnings, w => w.Contains("dysk pełny"));
    }

    [Fact]
    public void RunnerThrows_Failed()
    {
        var f = new Fixture();
        f.Runner.Enqueue(_ => throw new OperationException(OperationErrorKind.Unexpected, "brak pliku winget.exe"));

        var batch = f.Install(Zip, Vlc);

        Assert.Equal(TweakOutcome.Failed, batch.Items[0].Outcome);
        Assert.Equal("Nie wykonano – nie udało się uruchomić winget: brak pliku winget.exe", batch.Items[0].Message);
        Assert.Equal(JournalEntryStatus.Aborted, f.Journal.Current.Entries[0].Status);
        Assert.Equal(2, f.InstallCalls.Count);
    }

    public static IEnumerable<object[]> LogPathFailures()
    {
        yield return new object[] { new System.IO.IOException("logs\\winget to junction") };
        yield return new object[] { new ArgumentException("nieprawidłowy id") };
    }

    [Theory]
    [MemberData(nameof(LogPathFailures))]
    public void LogPathFails_FailedWingetNotStarted(Exception failure)
    {
        var f = new Fixture();
        f.Logs.Throws = failure;
        f.Runner.Enqueue(f.Installs("VLC media player"));

        var batch = f.Install(Zip, Vlc);

        Assert.Empty(f.InstallCalls);
        var item = batch.Items[0];
        Assert.Equal(TweakOutcome.Failed, item.Outcome);
        Assert.Equal("Nie wykonano – nie udało się przygotować pliku logu winget: " + failure.Message, item.Message);
        Assert.Null(item.LogPath);
        Assert.Null(item.WingetExitCode);
        AssertEntry(f.Journal.Current.Entries[0], Zip, JournalEntryStatus.Aborted, "Failed", "winget: 7zip.7zip");
        Assert.Equal(TweakOutcome.Failed, batch.Items[1].Outcome);
    }

    [Fact]
    public void DuplicateWingetId_Throws()
    {
        var f = new Fixture();
        f.Runner.Calls.Clear();

        Assert.Throws<ArgumentException>(() => f.Install(Zip, InstallTarget.FromSearch("7ZIP.7zip", "7-Zip")));
        Assert.Empty(f.Runner.Calls);
        Assert.Empty(f.Reader.ReadSids);
    }

    [Fact]
    public void NullTarget_Throws()
        => Assert.Throws<ArgumentException>(() => new Fixture().Install(Zip, null!));

    [Fact]
    public void Empty_NoItems()
    {
        var f = new Fixture();

        var batch = f.Install();

        Assert.Empty(batch.Items);
        Assert.Empty(batch.Batch.Results);
    }

    [Fact]
    public void InputNotSorted_RunsInGivenOrder()
    {
        var f = new Fixture();
        f.Runner.Enqueue(f.Installs("VLC media player")).Enqueue(f.Installs("7-Zip 26.05 (x64)"));

        var batch = f.Install(Vlc, Zip);

        Assert.Equal(new[] { "install.vlc", "install.7zip" }, batch.Items.Select(i => i.Id));
    }

    [Fact]
    public void Constructor_And_Install_NullArguments_Throw()
    {
        var f = new Fixture();
        Func<string, string?> names = _ => null;
        Assert.Throws<ArgumentNullException>(() => new AppInstallEngine(null!, f.Logs, f.Reader, f.Journal, f.User, UserSid, names, f.Log, f.Clock));
        Assert.Throws<ArgumentNullException>(() => new AppInstallEngine(f.Runner, null!, f.Reader, f.Journal, f.User, UserSid, names, f.Log, f.Clock));
        Assert.Throws<ArgumentNullException>(() => new AppInstallEngine(f.Runner, f.Logs, null!, f.Journal, f.User, UserSid, names, f.Log, f.Clock));
        Assert.Throws<ArgumentNullException>(() => new AppInstallEngine(f.Runner, f.Logs, f.Reader, null!, f.User, UserSid, names, f.Log, f.Clock));
        Assert.Throws<ArgumentNullException>(() => new AppInstallEngine(f.Runner, f.Logs, f.Reader, f.Journal, null!, UserSid, names, f.Log, f.Clock));
        Assert.Throws<ArgumentNullException>(() => new AppInstallEngine(f.Runner, f.Logs, f.Reader, f.Journal, f.User, null!, names, f.Log, f.Clock));
        Assert.Throws<ArgumentNullException>(() => new AppInstallEngine(f.Runner, f.Logs, f.Reader, f.Journal, f.User, UserSid, null!, f.Log, f.Clock));
        Assert.Throws<ArgumentNullException>(() => new AppInstallEngine(f.Runner, f.Logs, f.Reader, f.Journal, f.User, UserSid, names, null!, f.Clock));
        Assert.Throws<ArgumentNullException>(() => new AppInstallEngine(f.Runner, f.Logs, f.Reader, f.Journal, f.User, UserSid, names, f.Log, null!));
        var engine = new AppInstallEngine(f.Runner, f.Logs, f.Reader, f.Journal, f.User, UserSid, names, f.Log, f.Clock);
        Assert.Throws<ArgumentNullException>(() => engine.Install(null!, new InstallOptions(), CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => engine.Install(Array.Empty<InstallTarget>(), null!, CancellationToken.None));
    }

    [Fact]
    public void Timeouts_FromSpec()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), InstallTimeouts.PerApp);
        Assert.Equal(TimeSpan.FromSeconds(30), InstallTimeouts.Version);
        Assert.Equal(TimeSpan.FromMinutes(2), InstallTimeouts.Search);
        Assert.Equal(TimeSpan.FromMinutes(2), InstallTimeouts.List);
        Assert.Equal(InstallTimeouts.PerApp, new InstallOptions().PerAppLimit);
        Assert.Equal("install-app", AppInstallEngine.JournalAction);
        Assert.Equal("app-install", JournalEntry.AppInstallKind);
    }

    [Fact]
    public void JournalLoadWarning_InResultWarnings()
    {
        var f = new Fixture();
        f.Journal.LoadWarning = "journal uszkodzony – odłożony";
        f.Runner.Enqueue(f.Installs("7-Zip 26.05 (x64)"));

        var batch = f.Install(Zip);

        Assert.Equal(TweakOutcome.Installed, batch.Items[0].Outcome);
        Assert.Contains("journal uszkodzony – odłożony", batch.Batch.Results[0].Warnings);
    }

    [Fact]
    public void JournalLoadUnexpectedException_FailedNothingRun()
    {
        var f = new Fixture();
        f.Journal.LoadException = new InvalidOperationException("nieoczekiwany");

        var batch = f.Install(Zip);

        Assert.Empty(f.InstallCalls);
        Assert.Equal("Nie udało się zapisać journala – instalacja nie została uruchomiona.", batch.Items[0].Message);
        Assert.Contains(f.Log.Lines, l => l.StartsWith("ERROR [Install]", StringComparison.Ordinal) && l.Contains("nieoczekiwany"));
    }
}
