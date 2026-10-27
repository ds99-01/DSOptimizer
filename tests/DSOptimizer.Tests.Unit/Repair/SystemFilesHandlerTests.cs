using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Repair;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>
/// Handler <c>system-files</c> wyłącznie na fake'ach: DISM i SFC odtwarzają próbki z VM (Samples/), CBS.log to tekst w pamięci,
/// który fałszywe narzędzie dopisuje w trakcie „przebiegu”. Nic nie uruchamia prawdziwego DISM ani SFC.
/// </summary>
public class SystemFilesHandlerTests
{
    private const string Win = @"C:\Windows";
    private const string Cbs = @"C:\Windows\Logs\CBS\CBS.log";
    private const string RebootKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string OldCbs = "2026-10-01 Info CSI 00000001 [SR] Cannot repair member file stary.dll (poprzedni przebieg)\r\n";

    public enum DismCase { Healthy, Repaired, HealthyOrRepaired, RebootRequired, SourceMissing, Failed }

    public enum SfcCase { NoViolations, Repaired, RepairPendingReboot, NotAllRepaired, Unknown }

    private static readonly string DismHealthyCbs = RepairSamples.Text("dism-restorehealth-healthy-home.cbs.log");
    private static readonly string SfcHealthyCbs = RepairSamples.Text("sfc-scannow-noviolations-pro.cbs.log");
    private static readonly IReadOnlyList<string> DismLines = RepairSamples.Lines("dism-restorehealth-healthy-home.bin");
    private static readonly IReadOnlyList<string> SfcLines = RepairSamples.Lines("sfc-scannow-noviolations-home.bin");

    private sealed class Fixture
    {
        public Fixture()
        {
            Files.AddFile(Cbs, OldCbs);
            Dism = _ => (0, DismHealthyCbs, DismLines);
            Sfc = _ => (0, SfcHealthyCbs, SfcLines);
            LongTools.Handler = request =>
            {
                Events.Add("run:" + request.FileName + " " + request.Arguments);
                var (code, cbs, lines) = request.FileName == "dism.exe" ? Dism(this) : Sfc(this);
                if (cbs is not null)
                {
                    Files.AddFile(Cbs, (Files.FileText(Cbs) ?? string.Empty) + cbs);
                }

                return code is { } value
                    ? new ServicingToolRun(value, false, lines.ToArray())
                    : new ServicingToolRun(null, true, lines.ToArray());
            };
        }

        public FakeServiceManager Services { get; } = new();

        public FakeRegistry Registry { get; } = new();

        public FakeToolRunner Tools { get; } = new();

        public FakeRepairFileSystem Files { get; } = new();

        public FakeServicingToolRunner LongTools { get; } = new();

        public FakeServicingActivity Servicing { get; } = new();

        public FakeClock Clock { get; } = new();

        public ListLog Log { get; } = new();

        public ListProgress Output { get; } = new();

        public ListProgress<ActionProgress> Progress { get; } = new();

        public List<string> Events { get; } = new();

        /// <summary>Wynik DISM: kod (null = limit czasu), dopisek do CBS.log, linie wyjścia.</summary>
        public Func<Fixture, (int? Code, string? Cbs, IReadOnlyList<string> Lines)> Dism { get; set; }

        public Func<Fixture, (int? Code, string? Cbs, IReadOnlyList<string> Lines)> Sfc { get; set; }

        public SystemFilesHandler Handler() => new(new RepairServices
        {
            Services = Services,
            Registry = Registry,
            Tools = Tools,
            Files = Files,
            Domain = new FakeDomainInfo(),
            Clock = Clock,
            Log = Log,
            LongTools = LongTools,
            DiskSpace = new FakeDiskSpace(),
            Servicing = Servicing,
            Scripts = new FakeScriptRunner(),
            User = new FakeUserContext(),
            TimeZone = TimeZoneInfo.Utc,
            Paths = new RepairPaths { SystemRoot = Win, EnsureBackupDirectory = () => throw new InvalidOperationException("system-files nie robi kopii") },
        });

        public TweakDefinition Tweak { get; set; } = TestTweaks.Action() with { Id = "repair.system-files", Script = null, Handler = ActionHandlerNames.SystemFiles, Timeout = TimeSpan.FromSeconds(2700) };

        public ActionHandlerResult Run(CancellationToken cancellationToken = default)
            => Handler().Run(Tweak, Output, Progress, cancellationToken);

        public List<string> ToolCalls => LongTools.Calls.Select(c => c.Request.FileName).ToList();
    }

    private static string[] Lines(ActionHandlerResult result) => (result.Message ?? string.Empty).Split(new[] { Environment.NewLine }, StringSplitOptions.None);

    private static string DismSummary(int detected, int repaired)
        => "Info CBS    Checking System Update Readiness.\r\nInfo CBS    Summary:\r\n"
           + $"Info CBS    Total Detected Corruption:\t{detected}\r\nInfo CBS    Total Repaired Corruption:\t{repaired}\r\n";

    private static (int? Code, string? Cbs, IReadOnlyList<string> Lines) DismResult(DismCase dism) => dism switch
    {
        DismCase.Healthy => (0, DismHealthyCbs, DismLines),
        DismCase.Repaired => (0, DismSummary(3, 3), DismLines),
        DismCase.HealthyOrRepaired => (0, "Info CBS    Exec: Processing complete\r\n", DismLines),
        DismCase.RebootRequired => (3010, null, DismLines),
        DismCase.SourceMissing => (unchecked((int)0x800F081F), DismSummary(2, 0), new[] { "Error: 0x800f081f" }),
        _ => (unchecked((int)0x800F0950), null, new[] { "Error: 0x800f0950" }),
    };

    private static (int? Code, string? Cbs, IReadOnlyList<string> Lines) SfcResult(Fixture f, SfcCase sfc)
    {
        const string P = "Info CSI    0000003a ";
        switch (sfc)
        {
            case SfcCase.NoViolations:
                return (0, SfcHealthyCbs, SfcLines);
            case SfcCase.Repaired:
                return (0, P + "[SR] Repairing corrupted file \\??\\C:\\Windows\\System32\\x.dll from store\r\n" + P + "[SR] Verify complete\r\n", SfcLines);
            case SfcCase.RepairPendingReboot:
                f.Registry.CreateKey(RebootKey);
                return (0, P + "[SR] Verify complete\r\n" + P + "[SR] Repairing 1 components\r\n", SfcLines);
            case SfcCase.NotAllRepaired:
                return (0, P + "[SR] Cannot repair member file y.dll\r\n" + P + "[SR] Verify complete\r\n", SfcLines);
            default:
                return (0, "Info CBS    TiWorker: Client requests SFP repair object.\r\n", SfcLines);
        }
    }

    // ---- nazwa, strażnik ----

    [Fact]
    public void Name_IsSystemFiles() => Assert.Equal(ActionHandlerNames.SystemFiles, new Fixture().Handler().Name);

    [Fact]
    public void GuardActive_NothingStarted()
    {
        var f = new Fixture();
        f.Servicing.Active = "dism";

        var result = f.Run();

        Assert.Equal(TweakOutcome.Refused, result.Outcome);
        Assert.Equal(ServicingGuard.RefusalMessage, result.Message);
        Assert.Empty(f.LongTools.Calls);
        Assert.Empty(f.Files.SharedReads);
        Assert.Empty(f.Progress.Items);
        Assert.Equal(1, f.Servicing.Calls);
    }

    // ---- ścieżka zdrowa (zmierzona na VM) ----

    [Fact]
    public void HealthyVm_RealSamples_IsExecuted_TwoStepLinesAndLogPaths()
    {
        var f = new Fixture();

        var result = f.Run();

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal(
            new[]
            {
                "DISM: magazyn składników jest zdrowy – nie wykryto uszkodzeń.",
                "SFC: nie znaleziono naruszeń integralności plików systemowych.",
                @"Logi: %SystemRoot%\Logs\DISM\dism.log, %SystemRoot%\Logs\CBS\CBS.log.",
            },
            Lines(result));
    }

    [Fact]
    public void CallOrderAndArguments_DismThenSfc_FromSystem32Names_WithCatalogTimeout()
    {
        var f = new Fixture();

        f.Run();

        Assert.Equal(new[] { "run:dism.exe /Online /Cleanup-Image /RestoreHealth", "run:sfc.exe /scannow" }, f.Events);
        Assert.All(f.LongTools.Calls, c => Assert.Equal(TimeSpan.FromSeconds(2700), c.WaitLimit));
    }

    [Fact]
    public void NullTimeout_UsesMeasuredDefaultLimit()
    {
        var f = new Fixture();
        f.Tweak = f.Tweak with { Timeout = null };

        f.Run();

        Assert.All(f.LongTools.Calls, c => Assert.Equal(SystemFilesHandler.DefaultWaitLimit, c.WaitLimit));
        Assert.Equal(TimeSpan.FromSeconds(2700), SystemFilesHandler.DefaultWaitLimit);
    }

    [Fact]
    public void Progress_PercentsGoToProgressWithStepIndexZeroAndOne_NextStageSfcDuringDism()
    {
        var f = new Fixture();

        f.Run();

        var dism = f.Progress.Items.Where(p => p.Stage == "DISM").ToList();
        var sfc = f.Progress.Items.Where(p => p.Stage == "SFC").ToList();
        Assert.Equal(f.Progress.Items, dism.Concat(sfc));
        Assert.All(dism, p => Assert.Equal((0, 2, "SFC"), (p.StepIndex, p.StepCount, p.NextStage)));
        Assert.All(sfc, p => Assert.Equal((1, 2, (string?)null), (p.StepIndex, p.StepCount, p.NextStage)));
        Assert.Null(dism[0].Percent);
        Assert.Null(sfc[0].Percent);
        Assert.Equal(100, dism[dism.Count - 1].Percent);
        Assert.Equal(100, sfc[sfc.Count - 1].Percent);
        Assert.Contains(dism, p => p.Percent == 45);
        Assert.Contains(sfc, p => p.Percent == 70);
        // SFC wypisuje każdy procent zwykle dwa razy: do okna trafia tylko zmiana.
        Assert.Equal(sfc.Count, sfc.Select(p => p.Percent).Distinct().Count());
        Assert.Equal(102, sfc.Count);
    }

    [Fact]
    public void ToolLines_GoToOutput_ProgressLinesStayOutOfLog()
    {
        var f = new Fixture();

        f.Run();

        Assert.Equal(DismLines.Concat(SfcLines), f.Output.Items);
        Assert.Contains(f.Log.Lines, l => l.Contains("DISM: The restore operation completed successfully."));
        Assert.Contains(f.Log.Lines, l => l.Contains("SFC: Windows Resource Protection did not find any integrity violations."));
        Assert.DoesNotContain(f.Log.Lines, l => l.Contains("Verification 50% complete."));
        Assert.DoesNotContain(f.Log.Lines, l => l.Contains("%   "));
    }

    [Fact]
    public void CbsSegmentReadFromOffset_OldRunMarkersIgnored()
    {
        var f = new Fixture();
        var dismStart = OldCbs.Length;
        var sfcStart = OldCbs.Length + DismHealthyCbs.Length;

        var result = f.Run();

        Assert.Equal(new[] { (Cbs, (long)dismStart), (Cbs, (long)sfcStart) }, f.Files.SharedReads);
        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.DoesNotContain("nie udało się naprawić", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CbsMissingBeforeRun_ReadsFromStartOfNewFile()
    {
        var f = new Fixture();
        f.Files.RemoveFile(Cbs);

        var result = f.Run();

        Assert.Equal(new[] { (Cbs, 0L), (Cbs, (long)DismHealthyCbs.Length) }, f.Files.SharedReads);
        Assert.Equal(TweakOutcome.Executed, result.Outcome);
    }

    // ---- każda kombinacja wyniku zbiorczego (pkt 6) ----

    [Theory]
    [InlineData(DismCase.Healthy, SfcCase.NoViolations, TweakOutcome.Executed, false)]
    [InlineData(DismCase.Healthy, SfcCase.Repaired, TweakOutcome.Executed, false)]
    [InlineData(DismCase.Healthy, SfcCase.RepairPendingReboot, TweakOutcome.Executed, true)]
    [InlineData(DismCase.Healthy, SfcCase.NotAllRepaired, TweakOutcome.Failed, false)]
    [InlineData(DismCase.Healthy, SfcCase.Unknown, TweakOutcome.NotConfirmed, false)]
    [InlineData(DismCase.Repaired, SfcCase.NoViolations, TweakOutcome.Executed, false)]
    [InlineData(DismCase.Repaired, SfcCase.Repaired, TweakOutcome.Executed, false)]
    [InlineData(DismCase.Repaired, SfcCase.RepairPendingReboot, TweakOutcome.Executed, true)]
    [InlineData(DismCase.Repaired, SfcCase.NotAllRepaired, TweakOutcome.Failed, false)]
    [InlineData(DismCase.Repaired, SfcCase.Unknown, TweakOutcome.NotConfirmed, false)]
    [InlineData(DismCase.HealthyOrRepaired, SfcCase.NoViolations, TweakOutcome.Executed, false)]
    [InlineData(DismCase.HealthyOrRepaired, SfcCase.Repaired, TweakOutcome.Executed, false)]
    [InlineData(DismCase.HealthyOrRepaired, SfcCase.RepairPendingReboot, TweakOutcome.Executed, true)]
    [InlineData(DismCase.HealthyOrRepaired, SfcCase.NotAllRepaired, TweakOutcome.Failed, false)]
    [InlineData(DismCase.HealthyOrRepaired, SfcCase.Unknown, TweakOutcome.NotConfirmed, false)]
    [InlineData(DismCase.RebootRequired, SfcCase.NoViolations, TweakOutcome.Executed, true)]
    [InlineData(DismCase.RebootRequired, SfcCase.Repaired, TweakOutcome.Executed, true)]
    [InlineData(DismCase.RebootRequired, SfcCase.RepairPendingReboot, TweakOutcome.Executed, true)]
    [InlineData(DismCase.RebootRequired, SfcCase.NotAllRepaired, TweakOutcome.Failed, false)]
    [InlineData(DismCase.RebootRequired, SfcCase.Unknown, TweakOutcome.NotConfirmed, false)]
    [InlineData(DismCase.SourceMissing, SfcCase.NoViolations, TweakOutcome.Failed, false)]
    [InlineData(DismCase.SourceMissing, SfcCase.Repaired, TweakOutcome.Failed, false)]
    [InlineData(DismCase.SourceMissing, SfcCase.RepairPendingReboot, TweakOutcome.Failed, false)]
    [InlineData(DismCase.SourceMissing, SfcCase.NotAllRepaired, TweakOutcome.Failed, false)]
    [InlineData(DismCase.SourceMissing, SfcCase.Unknown, TweakOutcome.Failed, false)]
    [InlineData(DismCase.Failed, SfcCase.NoViolations, TweakOutcome.Failed, false)]
    [InlineData(DismCase.Failed, SfcCase.Repaired, TweakOutcome.Failed, false)]
    [InlineData(DismCase.Failed, SfcCase.RepairPendingReboot, TweakOutcome.Failed, false)]
    [InlineData(DismCase.Failed, SfcCase.NotAllRepaired, TweakOutcome.Failed, false)]
    [InlineData(DismCase.Failed, SfcCase.Unknown, TweakOutcome.Failed, false)]
    public void Combination_GivesAggregateOutcomeAndReboot(DismCase dism, SfcCase sfc, TweakOutcome expected, bool reboot)
    {
        var f = new Fixture { Dism = _ => DismResult(dism) };
        f.Sfc = fx => SfcResult(fx, sfc);

        var result = f.Run();

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(reboot, result.RequiresReboot);
        Assert.Equal(new[] { "dism.exe", "sfc.exe" }, f.ToolCalls);
        var lines = Lines(result);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("DISM", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("SFC", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("Logi: ", lines[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DismCase.Healthy, "DISM: magazyn składników jest zdrowy – nie wykryto uszkodzeń.")]
    [InlineData(DismCase.Repaired, "DISM: naprawiono uszkodzenia magazynu składników (3 z 3).")]
    [InlineData(DismCase.HealthyOrRepaired, "DISM: zakończony bez błędu (kod 0); z CBS.log nie dało się odczytać, czy coś naprawiono.")]
    [InlineData(DismCase.RebootRequired, "DISM: naprawa dokończy się po ponownym uruchomieniu komputera (kod 3010).")]
    [InlineData(DismCase.SourceMissing, "DISM: nie znaleziono plików do naprawy – potrzebny internet (Windows Update) albo nośnik instalacyjny (kod 0x800F081F).")]
    [InlineData(DismCase.Failed, @"DISM: błąd 0x800F0950. Szczegóły: %SystemRoot%\Logs\DISM\dism.log.")]
    public void DismLine_PerVerdict(DismCase dism, string expected)
    {
        var f = new Fixture { Dism = _ => DismResult(dism) };

        Assert.Equal(expected, Lines(f.Run())[0]);
    }

    [Fact]
    public void DismDetectedButNotAllRepaired_IsFailedLineWithCounts()
    {
        var f = new Fixture { Dism = _ => (0, DismSummary(4, 1), DismLines) };

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(@"DISM: uszkodzenia magazynu składników: wykryto 4, naprawiono 1. Szczegóły: %SystemRoot%\Logs\DISM\dism.log.", Lines(result)[0]);
    }

    [Theory]
    [InlineData(SfcCase.NoViolations, "SFC: nie znaleziono naruszeń integralności plików systemowych.")]
    [InlineData(SfcCase.Repaired, "SFC: znaleziono uszkodzone pliki systemowe i je naprawiono.")]
    [InlineData(SfcCase.RepairPendingReboot, "SFC: naprawa plików systemowych dokończy się po ponownym uruchomieniu komputera.")]
    [InlineData(SfcCase.NotAllRepaired, @"SFC: nie udało się naprawić części plików systemowych – szczegóły (wpisy [SR]) w %SystemRoot%\Logs\CBS\CBS.log.")]
    [InlineData(SfcCase.Unknown, @"SFC: zakończony kodem 0, ale wyniku nie da się jednoznacznie odczytać – sprawdź wpisy [SR] w %SystemRoot%\Logs\CBS\CBS.log.")]
    public void SfcLine_PerVerdict(SfcCase sfc, string expected)
    {
        var f = new Fixture();
        f.Sfc = fx => SfcResult(fx, sfc);

        Assert.Equal(expected, Lines(f.Run())[1]);
    }

    [Fact]
    public void RebootPendingAlreadyBeforeSfc_IsNotCountedAsAppeared()
    {
        var f = new Fixture();
        f.Registry.CreateKey(RebootKey);
        f.Sfc = fx => SfcResult(fx, SfcCase.RepairPendingReboot);

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.False(result.RequiresReboot);
    }

    [Fact]
    public void DismFailed_SfcStillRuns()
    {
        var f = new Fixture { Dism = _ => DismResult(DismCase.Failed) };

        var result = f.Run();

        Assert.Equal(new[] { "dism.exe", "sfc.exe" }, f.ToolCalls);
        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("SFC: nie znaleziono naruszeń integralności plików systemowych.", Lines(result)[1]);
    }

    [Fact]
    public void DismCouldNotStart_SfcStillRuns_AndResultIsFailed()
    {
        var f = new Fixture();
        var inner = f.LongTools.Handler;
        f.LongTools.Handler = request => request.FileName == "dism.exe"
            ? throw new OperationException(OperationErrorKind.NotFound, @"Nie znaleziono pliku C:\Windows\System32\dism.exe.")
            : inner(request);

        var result = f.Run();

        Assert.Equal(new[] { "dism.exe", "sfc.exe" }, f.ToolCalls);
        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(@"DISM: nie udało się uruchomić – Nie znaleziono pliku C:\Windows\System32\dism.exe.", Lines(result)[0]);
    }

    [Fact]
    public void SfcCouldNotStart_IsFailed_WithDismLineKept()
    {
        var f = new Fixture();
        var inner = f.LongTools.Handler;
        f.LongTools.Handler = request => request.FileName == "sfc.exe"
            ? throw new OperationException(OperationErrorKind.NotFound, "brak sfc.exe")
            : inner(request);

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal("DISM: magazyn składników jest zdrowy – nie wykryto uszkodzeń.", Lines(result)[0]);
        Assert.Equal("SFC: nie udało się uruchomić – brak sfc.exe", Lines(result)[1]);
    }

    [Fact]
    public void UnexpectedExceptionFromRunner_IsNotSwallowed()
    {
        var f = new Fixture();
        f.LongTools.Handler = _ => throw new InvalidOperationException("błąd programisty");

        Assert.Throws<InvalidOperationException>(() => f.Run());
    }

    // ---- limit czasu (Review Focus 3) ----

    [Fact]
    public void DismTimeout_NotConfirmed_SfcNotStarted()
    {
        var f = new Fixture { Dism = _ => (null, null, DismLines.Take(10).ToList()) };

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal(new[] { "dism.exe" }, f.ToolCalls);
        Assert.Equal(@"DISM nadal działa w tle po 45 min – program przestał czekać. Wynik sprawdzisz w %SystemRoot%\Logs\DISM\dism.log.", Lines(result)[0]);
        Assert.Equal("SFC nie został uruchomiony (DISM jeszcze pracuje).", Lines(result)[1]);
        Assert.Empty(f.Files.SharedReads);
        Assert.DoesNotContain(f.Progress.Items, p => p.Stage == "SFC");
    }

    [Fact]
    public void DismTimeout_ShortLimit_IsWrittenInSeconds()
    {
        var f = new Fixture { Dism = _ => (null, null, Array.Empty<string>()) };
        f.Tweak = f.Tweak with { Timeout = TimeSpan.FromSeconds(90) };

        Assert.StartsWith("DISM nadal działa w tle po 90 s – program przestał czekać.", Lines(f.Run())[0], StringComparison.Ordinal);
    }

    [Fact]
    public void SfcTimeout_NotConfirmed()
    {
        var f = new Fixture { Sfc = _ => (null, null, SfcLines.Take(5).ToList()) };

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal("DISM: magazyn składników jest zdrowy – nie wykryto uszkodzeń.", Lines(result)[0]);
        Assert.Equal(@"SFC nadal działa w tle po 45 min – program przestał czekać. Wynik sprawdzisz w %SystemRoot%\Logs\CBS\CBS.log.", Lines(result)[1]);
        Assert.Single(f.Files.SharedReads);
    }

    [Fact]
    public void SfcTimeout_AfterDismFailure_IsFailed()
    {
        var f = new Fixture { Dism = _ => DismResult(DismCase.SourceMissing), Sfc = _ => (null, null, Array.Empty<string>()) };

        var result = f.Run();

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.StartsWith("SFC nadal działa w tle", Lines(result)[1], StringComparison.Ordinal);
    }

    // ---- „Zatrzymaj” między krokami (R10) ----

    [Fact]
    public void StopBetweenSteps_SfcSkipped()
    {
        var f = new Fixture();
        using var stop = new CancellationTokenSource();
        f.Dism = _ =>
        {
            stop.Cancel();
            return (0, DismHealthyCbs, DismLines);
        };

        var result = f.Run(stop.Token);

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.False(result.RequiresReboot);
        Assert.Equal(new[] { "dism.exe" }, f.ToolCalls);
        Assert.Equal("DISM: magazyn składników jest zdrowy – nie wykryto uszkodzeń.", Lines(result)[0]);
        Assert.Equal("SFC pominięty – zatrzymano.", Lines(result)[1]);
        Assert.DoesNotContain(f.Progress.Items, p => p.Stage == "SFC");
    }

    [Fact]
    public void StopBetweenSteps_AfterDismFailure_IsFailed()
    {
        var f = new Fixture();
        using var stop = new CancellationTokenSource();
        f.Dism = _ =>
        {
            stop.Cancel();
            return DismResult(DismCase.Failed);
        };

        var result = f.Run(stop.Token);

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Equal(new[] { "dism.exe" }, f.ToolCalls);
        Assert.Equal("SFC pominięty – zatrzymano.", Lines(result)[1]);
    }

    [Fact]
    public void StopRequestedBeforeStart_StillRunsDismBecauseStopActsOnlyBetweenSteps()
    {
        var f = new Fixture();
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var result = f.Run(stop.Token);

        Assert.Equal(new[] { "dism.exe" }, f.ToolCalls);
        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
    }

    // ---- odczyt CBS.log nieudany ----

    [Fact]
    public void CbsUnreadableAfterSfc_IsNotConfirmed_NeverExecuted()
    {
        var f = new Fixture();
        f.Sfc = fx =>
        {
            fx.Files.RemoveFile(Cbs);
            return (0, null, SfcLines);
        };

        var result = f.Run();

        Assert.Equal(TweakOutcome.NotConfirmed, result.Outcome);
        Assert.StartsWith("SFC: zakończony kodem 0, ale wyniku nie da się jednoznacznie odczytać", Lines(result)[1], StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutOutputAndProgress_DoesNotThrow()
    {
        var f = new Fixture();

        var result = f.Handler().Run(f.Tweak, null, null, CancellationToken.None);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
    }

    [Fact]
    public void NullTweak_Throws()
        => Assert.Throws<ArgumentNullException>(() => new Fixture().Handler().Run(null!, null, null, CancellationToken.None));
}
