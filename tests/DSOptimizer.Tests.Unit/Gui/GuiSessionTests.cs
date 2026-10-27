using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Engine;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class GuiSessionTests
{
    private static TweakDefinition Caution(string id = "test.caution")
        => TestTweaks.TwoValues(id) with { Risk = RiskLevel.Caution, Warnings = new[] { "Uwaga 1", "Uwaga 2" }, Description = "Opis" };

    private static GuiSession Session(EngineFixture f, params TweakDefinition[] definitions)
        => new(new LoadedCatalog(definitions, "sha256:test"), f.CreateServices());

    private static TweakDefinition InCategory(string category, string id)
        => TestTweaks.TwoValues(id) with { Category = category };

    [Fact]
    public void Categories_OrderedByCategoryOrder_UnknownLast()
    {
        var session = Session(new EngineFixture(), InCategory("ux", "a.1"), InCategory("zzz", "a.2"), InCategory("perf", "a.3"),
            InCategory("aaa", "a.4"), InCategory("ux", "a.5"));

        Assert.Equal(new[] { "perf", "ux", "aaa", "zzz" }, session.Categories.Select(c => c.Id));
        Assert.Equal("Wydajność", session.Categories[0].Name);
        Assert.Equal("aaa", session.Categories[2].Name);
        Assert.Equal(new[] { "a.1", "a.5" }, session.Categories[1].Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData(GuiOperation.Apply)]
    [InlineData(GuiOperation.Undo)]
    public void Run_CautionWithoutConfirmation_DoesNotTouchEngine(GuiOperation operation)
    {
        var f = new EngineFixture();
        var session = Session(f, Caution());

        var report = session.Run("test.caution", operation, confirmed: false);

        AssertUntouched(f, report, "Operacja wymaga potwierdzenia – nic nie zmieniono.");
    }

    [Fact]
    public void Run_SafeActionExecuteWithoutConfirmation_DoesNotTouchEngine()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.Action());

        var report = session.Run("test.action", GuiOperation.Execute, confirmed: false);

        AssertUntouched(f, report, "Operacja wymaga potwierdzenia – nic nie zmieniono.");
        Assert.Empty(f.Launcher.Calls);
    }

    [Fact]
    public void Run_SafePanelExecute_NeedsNoConfirmation()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.Panel());

        var report = session.Run("test.panel", GuiOperation.Execute, confirmed: false);

        Assert.Equal(TweakOutcome.Executed, report.Result!.Outcome);
        Assert.Single(f.Launcher.Calls);
    }

    private static void AssertUntouched(EngineFixture f, GuiOperationReport report, string line)
    {
        Assert.Null(report.Result);
        var logged = Assert.Single(report.Lines);
        Assert.Equal(GuiLogLevel.Warning, logged.Level);
        Assert.Equal(line, logged.Text);
        Assert.Empty(report.Refreshed);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Empty(f.Scripts.Calls);
        Assert.Empty(f.RestorePoints.Descriptions);
        Assert.Equal(0, f.Journal.SaveCount);
    }

    [Fact]
    public void Run_CautionApplyConfirmed_Applies()
    {
        var f = new EngineFixture();
        var session = Session(f, Caution());

        var report = session.Run("test.caution", GuiOperation.Apply, confirmed: true);

        Assert.Equal(TweakOutcome.Applied, report.Result!.Outcome);
        Assert.Equal(DetectionState.Applied, report.Refreshed["test.caution"]);
        var line = Assert.Single(report.Lines);
        Assert.Equal(GuiLogLevel.Info, line.Level);
        Assert.Equal("Dwie wartości: Zastosowano", line.Text);
        Assert.False(report.RequiresReboot);
    }

    [Fact]
    public void Run_Undo_RevertsAppliedTweak()
    {
        var f = new EngineFixture();
        var session = Session(f, Caution());
        session.Run("test.caution", GuiOperation.Apply, confirmed: true);

        var report = session.Run("test.caution", GuiOperation.Undo, confirmed: true);

        Assert.Equal(TweakOutcome.Undone, report.Result!.Outcome);
        Assert.Equal(DetectionState.NotApplied, report.Refreshed["test.caution"]);
    }

    [Fact]
    public void Run_Apply_UsesIfAvailableRestorePoint()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues());

        session.Run("test.two-values", GuiOperation.Apply, confirmed: false);

        Assert.Equal("DSOptimizer – przed test.two-values", Assert.Single(f.RestorePoints.Descriptions));
    }

    [Fact]
    public void Run_Apply_RestorePointFailure_ContinuesWithWarningLine()
    {
        var f = new EngineFixture();
        f.RestorePoints.Next = new RestorePointResult(RestorePointStatus.Failed, "brak");
        var session = Session(f, TestTweaks.TwoValues());

        var report = session.Run("test.two-values", GuiOperation.Apply, confirmed: false);

        Assert.Equal(TweakOutcome.Applied, report.Result!.Outcome);
        var warning = report.Lines[0];
        Assert.Equal(GuiLogLevel.Warning, warning.Level);
        Assert.Contains("Punkt przywracania nie powstał", warning.Text);
    }

    [Fact]
    public async Task Run_WhileAnotherRunInProgress_RefusesWithoutEngineCall()
    {
        var f = new EngineFixture();
        var toggle = new ToggleScript();
        toggle.Register(f.Scripts);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var applyCalls = 0;
        f.Scripts.On("APPLY", _ =>
        {
            Interlocked.Increment(ref applyCalls);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
            toggle.Applied = true;
            return FakeScriptRunner.Result(0);
        });
        var session = Session(f, TestTweaks.Scripted());

        var first = Task.Run(() => session.Run("test.script", GuiOperation.Apply, confirmed: true));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));

        var second = session.Run("test.script", GuiOperation.Apply, confirmed: true);
        var detecting = Task.Run(() => session.Detect("test.script"));
        Assert.NotSame(detecting, await Task.WhenAny(detecting, Task.Delay(300)));

        Assert.Null(second.Result);
        var refusal = Assert.Single(second.Lines);
        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", refusal.Text);
        Assert.Equal(GuiLogLevel.Warning, refusal.Level);

        release.Set();
        var firstReport = await first;
        Assert.Equal(1, applyCalls);
        Assert.Equal(TweakOutcome.Applied, firstReport.Result!.Outcome);
        Assert.Equal(DetectionState.Applied, await detecting);
    }

    [Fact]
    public void TryRunExclusive_Free_RunsWorkAndReturnsResult()
    {
        var session = Session(new EngineFixture(), TestTweaks.Scripted());

        var ran = session.TryRunExclusive(() => 42, out var result);

        Assert.True(ran);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task TryRunExclusive_WhileRunInProgress_RefusesWithoutWork()
    {
        var f = new EngineFixture();
        var toggle = new ToggleScript();
        toggle.Register(f.Scripts);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Scripts.On("APPLY", _ =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
            toggle.Applied = true;
            return FakeScriptRunner.Result(0);
        });
        var session = Session(f, TestTweaks.Scripted());

        var first = Task.Run(() => session.Run("test.script", GuiOperation.Apply, confirmed: true));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
        var worked = false;
        var ran = session.TryRunExclusive(() => worked = true, out var result);
        release.Set();
        await first;

        Assert.False(ran);
        Assert.False(result);
        Assert.False(worked);
    }

    [Fact]
    public async Task RunBatch_WhileTryRunExclusiveInProgress_Refuses()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues("t.other"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => session.TryRunExclusive(
            () =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
                return 0;
            },
            out _));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));

        var batch = session.RunBatch(new[] { "t.other" }, GuiOperation.Apply, confirmed: true);
        var single = session.Run("t.other", GuiOperation.Apply, confirmed: true);
        release.Set();

        Assert.True(await holder);
        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", Assert.Single(batch.Lines).Text);
        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", Assert.Single(single.Lines).Text);
        Assert.Empty(f.RestorePoints.Descriptions);
    }

    [Fact]
    public void TryRunExclusive_WorkThrows_ReleasesLock()
    {
        var session = Session(new EngineFixture(), TestTweaks.Scripted());

        Assert.Throws<InvalidOperationException>(() => session.TryRunExclusive<int>(() => throw new InvalidOperationException("x"), out _));

        Assert.True(session.TryRunExclusive(() => 1, out _));
    }

    [Fact]
    public void Run_Execute_RefreshesAllTweaks()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0);
        var session = Session(f, TestTweaks.Action(), TestTweaks.TwoValues("t.one"), TestTweaks.TwoValues("t.two"));

        var report = session.Run("test.action", GuiOperation.Execute, confirmed: true);

        Assert.Equal(TweakOutcome.Executed, report.Result!.Outcome);
        Assert.Equal(new[] { "t.one", "t.two" }, report.Refreshed.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Run_ActionMultiLineOutput_OneLogLineAndFileInfo()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0, "linia 1", "linia 2", "linia 3");
        var session = Session(f, TestTweaks.Action());

        var report = session.Run("test.action", GuiOperation.Execute, confirmed: true);

        var line = Assert.Single(report.Lines, l => l.Text.StartsWith("Akcja:", StringComparison.Ordinal));
        Assert.DoesNotContain("\n", line.Text);
        Assert.Contains("linia 1 | linia 2 | linia 3", line.Text);
        Assert.EndsWith(" (pełny wynik w pliku logu)", line.Text);
        Assert.Contains(f.Log.Lines, l => l.StartsWith("INFO [Gui]", StringComparison.Ordinal)
            && l.Contains("test.action: pełny wynik:") && l.Contains("linia 1") && l.Contains("linia 3"));
    }

    [Fact]
    public void Run_ActionSingleShortOutput_NoSuffix()
    {
        var f = new EngineFixture();
        f.Scripts.On("RUN", 0, "gotowe");
        var session = Session(f, TestTweaks.Action());

        var report = session.Run("test.action", GuiOperation.Execute, confirmed: true);

        var line = Assert.Single(report.Lines, l => l.Text.StartsWith("Akcja:", StringComparison.Ordinal));
        Assert.DoesNotContain("pełny wynik", line.Text);
        Assert.DoesNotContain(f.Log.Lines, l => l.Contains("pełny wynik"));
    }

    [Fact]
    public void Run_FailedOutcome_ReportsErrorLine()
    {
        var f = new EngineFixture();
        var toggle = new ToggleScript { ApplyFails = true };
        toggle.Register(f.Scripts);
        var session = Session(f, TestTweaks.Scripted());

        var report = session.Run("test.script", GuiOperation.Apply, confirmed: true);

        Assert.False(report.Result!.IsSuccess);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Error && line.Text.StartsWith("Skrypt: ", StringComparison.Ordinal));
        Assert.Single(report.Refreshed);
    }

    [Fact]
    public void Run_IncompleteRollback_ReportsNotRestoredAsError()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        f.Registry.WriteInterceptor = (_, name, value) =>
            name == "B" || (name == "A" && value.Equals(RegValue.FromDWord(0))) ? new InvalidOperationException("fake") : null;
        var session = Session(f, TestTweaks.TwoValues());

        var report = session.Run("test.two-values", GuiOperation.Apply, confirmed: false);

        Assert.NotEmpty(report.Result!.NotRestored);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Error && line.Text.StartsWith("Nie przywrócono: ", StringComparison.Ordinal));
        Assert.Equal(GuiLogLevel.Error, report.Lines.First(line => line.Text.StartsWith("Dwie wartości: ", StringComparison.Ordinal)).Level);
    }

    private const string OtherPath = @"HKLM\SOFTWARE\DSO\Inna";

    /// <summary>Pozycja już zastosowana (inna ścieżka rejestru): jej detekcja czyta journal (PromotePendingReboot).</summary>
    private static TweakDefinition AppliedOther(EngineFixture f)
    {
        f.Registry.Set(OtherPath, "X", RegValue.FromDWord(1));
        return TestTweaks.TwoValues("t.other") with
        {
            Title = "Inna pozycja",
            Registry = new[] { new RegistryEntry { Path = OtherPath, Name = "X", Value = RegValue.FromDWord(1), Original = RegValue.FromDWord(0) } },
        };
    }

    [Fact]
    public void Run_RefreshDetectionThrows_KeepsReportAndMarksItemUnknown()
    {
        var f = new EngineFixture();
        var other = AppliedOther(f);
        f.Scripts.On("RUN", _ =>
        {
            f.Journal.LoadException = new InvalidOperationException("journal padł");
            return FakeScriptRunner.Result(0);
        });
        var session = Session(f, TestTweaks.Action(), TestTweaks.TwoValues(), other);

        var report = session.Run("test.action", GuiOperation.Execute, confirmed: true);

        Assert.Equal(TweakOutcome.Executed, report.Result!.Outcome);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Info && line.Text == "Akcja: Wykonano");
        Assert.Equal(DetectionState.NotApplied, report.Refreshed["test.two-values"]);
        Assert.Equal(DetectionState.Unknown, report.Refreshed["t.other"]);
        var error = Assert.Single(report.Lines, line => line.Level == GuiLogLevel.Error);
        Assert.StartsWith("Inna pozycja: ", error.Text, StringComparison.Ordinal);
        Assert.Contains("journal padł", error.Text, StringComparison.Ordinal);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR [Gui]", StringComparison.Ordinal) && line.Contains("t.other"));
    }

    [Fact]
    public void Run_RefreshDetectionThrowsAfterIncompleteRollback_KeepsNotRestoredLines()
    {
        var f = new EngineFixture();
        var other = AppliedOther(f);
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        f.Registry.WriteInterceptor = (_, name, value) =>
        {
            if (name == "B")
            {
                // Silnik już nie czyta journala po zapisach – wyjątek trafi dopiero w detekcję odświeżającą.
                f.Journal.LoadException = new InvalidOperationException("journal padł");
                return new InvalidOperationException("fake");
            }

            return name == "A" && value.Equals(RegValue.FromDWord(0)) ? new InvalidOperationException("fake") : null;
        };
        var session = Session(f, TestTweaks.TwoValues(), other);

        var report = session.Run("test.two-values", GuiOperation.Apply, confirmed: false);

        Assert.NotEmpty(report.Result!.NotRestored);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Error && line.Text.StartsWith("Dwie wartości: ", StringComparison.Ordinal));
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Error && line.Text.StartsWith("Nie przywrócono: ", StringComparison.Ordinal));
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Error && line.Text.StartsWith("Inna pozycja: ", StringComparison.Ordinal));
        Assert.Equal(DetectionState.Unknown, report.Refreshed["t.other"]);
    }

    [Fact]
    public void Run_EngineThrows_ReportsFailureAndLogsFail()
    {
        var f = new EngineFixture();
        var log = new ThrowingInfoLog();
        var services = new EngineServices
        {
            Registry = f.Registry,
            Services = f.Services,
            Scripts = f.Scripts,
            Launcher = f.Launcher,
            Journal = f.Journal,
            RestorePoints = f.RestorePoints,
            SystemInfo = f.SystemInfo,
            User = f.User,
            Log = log,
            Clock = f.Clock,
        };
        var session = new GuiSession(new LoadedCatalog(new[] { TestTweaks.Panel() }, "sha256:test"), services);

        var report = session.Run("test.panel", GuiOperation.Execute, confirmed: false);

        Assert.Equal(TweakOutcome.Failed, report.Result!.Outcome);
        Assert.Equal("Panel: Błąd – Nieoczekiwany błąd: log padł", Assert.Single(report.Lines).Text);
        var fail = Assert.Single(log.Fails);
        Assert.StartsWith("[Gui] ", fail, StringComparison.Ordinal);
        Assert.Contains("test.panel", fail, StringComparison.Ordinal);
        Assert.Contains("log padł", fail, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_PendingReboot_AddsRebootWarning()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues() with { RequiresReboot = true });

        var report = session.Run("test.two-values", GuiOperation.Apply, confirmed: false);

        Assert.True(report.RequiresReboot);
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Warning && line.Text == "Wymagany restart, żeby zmiana zadziałała.");
        Assert.Contains(report.Lines, line => line.Level == GuiLogLevel.Info && line.Text == "Dwie wartości: Zastosowano");
    }

    [Fact]
    public void Run_NotSupportedBuild_IsWarningLine()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues() with { MinBuild = f.SystemInfo.Build + 1 });

        var report = session.Run("test.two-values", GuiOperation.Apply, confirmed: false);

        Assert.Equal(GuiLogLevel.Warning, report.Lines.First(line => line.Text.StartsWith("Dwie wartości: ", StringComparison.Ordinal)).Level);
    }

    [Fact]
    public void Run_ApplyOnPanel_Throws()
    {
        var session = Session(new EngineFixture(), TestTweaks.Panel());
        Assert.Throws<ArgumentException>(() => session.Run("test.panel", GuiOperation.Apply, confirmed: true));
        Assert.Throws<ArgumentException>(() => session.Run("test.panel", GuiOperation.Undo, confirmed: true));
    }

    [Fact]
    public void Run_ExecuteOnTweak_Throws()
    {
        var session = Session(new EngineFixture(), TestTweaks.TwoValues());
        Assert.Throws<ArgumentException>(() => session.Run("test.two-values", GuiOperation.Execute, confirmed: true));
    }

    [Fact]
    public void Run_UnknownId_Throws()
    {
        var session = Session(new EngineFixture(), TestTweaks.TwoValues());
        Assert.Throws<ArgumentException>(() => session.Run("nie.ma", GuiOperation.Apply, confirmed: true));
        Assert.Throws<ArgumentException>(() => session.Detect("nie.ma"));
        Assert.Throws<ArgumentException>(() => session.Run(null!, GuiOperation.Apply, confirmed: true));
    }

    [Fact]
    public void Detect_ReturnsStateAndRejectsNonTweak()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.TwoValues(), TestTweaks.Action());
        Assert.Equal(DetectionState.NotApplied, session.Detect("test.two-values"));
        Assert.Throws<ArgumentException>(() => session.Detect("test.action"));
    }

    [Fact]
    public void DetectAll_ReportsProgressPerTweak_SkipsActionsAndPanels()
    {
        var f = new EngineFixture();
        var session = Session(f, TestTweaks.Action(), TestTweaks.Panel(), TestTweaks.TwoValues("t.one"), TestTweaks.TwoValues("t.two"));
        var progress = new SyncProgress<KeyValuePair<string, DetectionState>>();

        var states = session.DetectAll(progress);

        Assert.Equal(new[] { "t.one", "t.two" }, progress.Items.Select(i => i.Key));
        Assert.Equal(2, states.Count);
        Assert.All(states.Values, state => Assert.Equal(DetectionState.NotApplied, state));
    }

    [Fact]
    public void DetectAll_CancelledToken_Throws()
    {
        var session = Session(new EngineFixture(), TestTweaks.TwoValues());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.DetectAll(null, cts.Token));
    }

    [Fact]
    public void Constructor_RejectsNulls()
    {
        var f = new EngineFixture();
        Assert.Throws<ArgumentNullException>(() => new GuiSession(null!, f.CreateServices()));
        Assert.Throws<ArgumentNullException>(() => new GuiSession(new LoadedCatalog(Array.Empty<TweakDefinition>(), "h"), null!));
    }

    private static TweakDefinition LongAction(string handler = ActionHandlerNames.SystemFiles)
        => TestTweaks.Action() with { Id = "test.long", Title = "Długa naprawa", Script = null, Handler = handler };

    private static GuiSession LongSession(EngineFixture f, FakeLongHandler handler, params TweakDefinition[] more)
    {
        f.Handlers = new ActionHandlerSet(new IActionHandler[] { handler });
        var definitions = new List<TweakDefinition> { LongAction(handler.Name) };
        definitions.AddRange(more);
        return Session(f, definitions.ToArray());
    }

    [Fact]
    public async Task RunAction_HoldsLock_OtherRunRefused()
    {
        var f = new EngineFixture();
        using var handler = new FakeLongHandler();
        var session = LongSession(f, handler, TestTweaks.TwoValues("t.other"));

        var running = Task.Run(() => session.RunAction("test.long", confirmed: true, null, CancellationToken.None));
        Assert.True(handler.FirstStepEntered.Wait(TimeSpan.FromSeconds(30)));

        var single = session.Run("t.other", GuiOperation.Apply, confirmed: true);
        var batch = session.RunBatch(new[] { "t.other" }, GuiOperation.Apply, confirmed: true);
        var second = session.RunAction("test.long", confirmed: true, null, CancellationToken.None);
        var exclusive = session.TryRunExclusive(() => 1, out _);
        handler.ReleaseFirstStep.Set();
        var report = await running;

        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", Assert.Single(single.Lines).Text);
        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", Assert.Single(batch.Lines).Text);
        Assert.Null(second.Result);
        Assert.Equal("Trwa inna operacja – nic nie zmieniono.", Assert.Single(second.Lines).Text);
        Assert.False(exclusive);
        Assert.Equal(1, handler.Runs);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Equal(TweakOutcome.Executed, report.Result!.Outcome);
        Assert.Equal("test.long", report.TweakId);
        Assert.Equal(GuiOperation.Execute, report.Operation);
        Assert.True(session.TryRunExclusive(() => 1, out _));
    }

    [Fact]
    public async Task RunAction_StopBetweenSteps_SecondStepNotStarted()
    {
        var f = new EngineFixture();
        using var handler = new FakeLongHandler();
        var session = LongSession(f, handler);
        using var stop = new CancellationTokenSource();

        var running = Task.Run(() => session.RunAction("test.long", confirmed: true, null, stop.Token));
        Assert.True(handler.FirstStepEntered.Wait(TimeSpan.FromSeconds(30)));
        stop.Cancel();
        handler.ReleaseFirstStep.Set();
        var report = await running;

        Assert.Equal(new[] { "Krok 1" }, handler.StartedSteps);
        Assert.Equal(TweakOutcome.NotConfirmed, report.Result!.Outcome);
        Assert.Equal("Długa naprawa: Nie potwierdzono – Zatrzymano po kroku 1.", report.Lines[0].Text);
    }

    [Fact]
    public async Task RunAction_ProgressReachesCaller()
    {
        var f = new EngineFixture();
        using var handler = new FakeLongHandler();
        handler.ReleaseFirstStep.Set();
        var session = LongSession(f, handler);
        var progress = new SyncProgress<ActionProgress>();

        var report = await Task.Run(() => session.RunAction("test.long", confirmed: true, progress, CancellationToken.None));

        Assert.Equal(TweakOutcome.Executed, report.Result!.Outcome);
        Assert.Equal(
            new[]
            {
                new ActionProgress("Krok 1", 0, 0, 2),
                new ActionProgress("Krok 1", 100, 0, 2),
                new ActionProgress("Krok 2", null, 1, 2),
                new ActionProgress("Krok 2", 100, 1, 2),
            },
            progress.Items);
        Assert.Equal(new[] { "Krok 1", "Krok 2" }, handler.StartedSteps);
    }

    [Fact]
    public void RunAction_WithoutConfirmation_RefusedWithoutHandler()
    {
        var f = new EngineFixture();
        using var handler = new FakeLongHandler();
        var session = LongSession(f, handler);

        var report = session.RunAction("test.long", confirmed: false, null, CancellationToken.None);

        Assert.Null(report.Result);
        Assert.Equal("Operacja wymaga potwierdzenia – nic nie zmieniono.", Assert.Single(report.Lines).Text);
        Assert.Equal(0, handler.Runs);
    }

    [Fact]
    public void RunAction_ItemWithoutProgressWindow_Throws()
    {
        var f = new EngineFixture();
        using var handler = new FakeLongHandler();
        var session = LongSession(f, handler, TestTweaks.Action(), TestTweaks.TwoValues(), LongAction(ActionHandlerNames.WuReset) with { Id = "test.wu" });

        Assert.Throws<ArgumentException>(() => session.RunAction("test.action", true, null, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => session.RunAction("test.two-values", true, null, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => session.RunAction("test.wu", true, null, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => session.RunAction("brak", true, null, CancellationToken.None));
    }

    /// <summary>
    /// Fałszywa długa naprawa: dwa kroki z postępem, krok 1 czeka na <see cref="ReleaseFirstStep"/>, „Zatrzymaj” (token)
    /// sprawdzany tylko między krokami – jak kontrakt <see cref="IActionHandler"/> dla długich handlerów.
    /// </summary>
    private sealed class FakeLongHandler : IActionHandler, IDisposable
    {
        public FakeLongHandler(string name = ActionHandlerNames.SystemFiles) => Name = name;

        public string Name { get; }

        public ManualResetEventSlim FirstStepEntered { get; } = new();

        public ManualResetEventSlim ReleaseFirstStep { get; } = new();

        public List<string> StartedSteps { get; } = new();

        public int Runs { get; private set; }

        public ActionHandlerResult Run(TweakDefinition tweak, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
        {
            Runs++;
            StartedSteps.Add("Krok 1");
            progress?.Report(new ActionProgress("Krok 1", 0, 0, 2));
            FirstStepEntered.Set();
            Assert.True(ReleaseFirstStep.Wait(TimeSpan.FromSeconds(30), CancellationToken.None));
            progress?.Report(new ActionProgress("Krok 1", 100, 0, 2));
            if (cancellationToken.IsCancellationRequested)
            {
                return new ActionHandlerResult { Outcome = TweakOutcome.NotConfirmed, Message = "Zatrzymano po kroku 1." };
            }

            StartedSteps.Add("Krok 2");
            progress?.Report(new ActionProgress("Krok 2", null, 1, 2));
            progress?.Report(new ActionProgress("Krok 2", 100, 1, 2));
            return new ActionHandlerResult { Outcome = TweakOutcome.Executed, Message = "Oba kroki bez błędów." };
        }

        public void Dispose()
        {
            FirstStepEntered.Dispose();
            ReleaseFirstStep.Dispose();
        }
    }

    /// <summary>Log, którego Info rzuca – jedyny sposób, by silnik wyrzucił wyjątek poza własne try (Execute: „start”/„launch”).</summary>
    private sealed class ThrowingInfoLog : ILog
    {
        public List<string> Fails { get; } = new();

        public void Info(string component, string message) => throw new InvalidOperationException("log padł");

        public void Warn(string component, string message)
        {
        }

        public void Fail(string component, string message, Exception? exception = null)
            => Fails.Add($"[{component}] {message} {exception?.Message}");
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        public List<T> Items { get; } = new();

        public void Report(T value) => Items.Add(value);
    }
}