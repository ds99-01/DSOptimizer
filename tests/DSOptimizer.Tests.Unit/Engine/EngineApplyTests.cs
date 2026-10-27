using System;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Engine;

public class EngineApplyTests
{
    [Fact]
    public void Apply_NotApplied_WritesValuesAndJournalsBaseline()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(DetectionState.Applied, result.FinalState);
        Assert.True(result.IsSuccess);
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(TestTweaks.Path, "A"));
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(TestTweaks.Path, "B"));
        var entry = Assert.Single(f.Journal.Current.Entries);
        Assert.Equal(JournalEntryStatus.Active, entry.Status);
        Assert.Equal("sha256:test", entry.CatalogHash);
        Assert.Equal(f.User.Sid, entry.UserSid);
        Assert.Equal(f.Clock.UtcNow, entry.AppliedAt);
        Assert.Equal(RegValue.FromDWord(0), entry.Baseline[0].Value);
        Assert.Equal(RegValue.Absent, entry.Baseline[1].Value);
    }

    [Fact]
    public void Apply_AlreadyApplied_ChangesNothing()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(1));
        f.Registry.Set(TestTweaks.Path, "B", RegValue.FromDWord(1));

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.AlreadyApplied, result.Outcome);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Fact]
    public void Apply_BuildBelowMin_IsNotSupported()
    {
        var f = new EngineFixture();
        f.SystemInfo.Build = 19045;

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues() with { MinBuild = 22000 });

        Assert.Equal(TweakOutcome.NotSupported, result.Outcome);
        Assert.Empty(f.Registry.WriteLog);
    }

    [Fact]
    public void Apply_CaptureDenied_FailsBeforeAnyChange()
    {
        var f = new EngineFixture();
        f.Registry.DenyRead(TestTweaks.Path, "B");

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Fact]
    public void Apply_JournalSaveFails_FailsBeforeAnyChange()
    {
        var f = new EngineFixture();
        f.Journal.FailSave = true;

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("journal", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.Registry.WriteLog);
    }

    [Fact]
    public void Apply_SecondWriteFails_RollsBackFirstAndAbortsEntry()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        f.Registry.FailWrite(TestTweaks.Path, "B");

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Contains("B", result.Message, StringComparison.Ordinal);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(TestTweaks.Path, "A"));
        Assert.Equal(RegValue.Absent, f.Registry.Get(TestTweaks.Path, "B"));
        Assert.Equal(JournalEntryStatus.Aborted, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void Apply_FailureRollback_DeletesCreatedKeys()
    {
        var f = new EngineFixture();
        f.Registry.CreateKey(@"HKLM\SOFTWARE\DSO");
        f.Registry.FailWrite(TestTweaks.NewKeyPath, "B");

        var result = f.CreateEngine().Apply(TestTweaks.TwoValuesInNewKey());

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.False(f.Registry.HasKey(@"HKLM\SOFTWARE\DSO\Nowy"));
        Assert.True(f.Registry.HasKey(@"HKLM\SOFTWARE\DSO"));
    }

    [Fact]
    public void Apply_RollbackFails_ReportsIncompleteAndKeepsEntryActive()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        f.Registry.FailWrite(TestTweaks.Path, "B");
        f.Registry.WriteInterceptor = (_, name, value) => name == "A" && value.Equals(RegValue.FromDWord(0))
            ? new OperationException(OperationErrorKind.AccessDenied, "restore denied")
            : null;

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.FailedRollbackIncomplete, result.Outcome);
        Assert.Contains(@"\A", Assert.Single(result.NotRestored), StringComparison.Ordinal);
        Assert.Equal(JournalEntryStatus.Active, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void Apply_PostDetectMismatch_RollsBackWithCapturedSnapshot()
    {
        var f = new EngineFixture();
        var script = new ToggleScript { ApplyWithoutEffect = true, CaptureValue = "2" };
        script.Register(f.Scripts);

        var result = f.CreateEngine().Apply(TestTweaks.Scripted());

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Contains("Detekcja po zmianie", result.Message, StringComparison.Ordinal);
        Assert.Equal("2", script.LastUndoSnapshot);
    }

    [Fact]
    public void Apply_RequiresReboot_IsPendingUntilDetectSeesIt()
    {
        var f = new EngineFixture();
        var script = new ToggleScript { ApplyWithoutEffect = true };
        script.Register(f.Scripts);
        var tweak = TestTweaks.Scripted() with { RequiresReboot = true };
        var engine = f.CreateEngine();

        var result = engine.Apply(tweak);

        Assert.Equal(TweakOutcome.PendingReboot, result.Outcome);
        Assert.True(result.RequiresReboot);
        Assert.Equal(JournalEntryStatus.PendingReboot, Assert.Single(f.Journal.Current.Entries).Status);

        script.Applied = true;
        Assert.Equal(DetectionState.Applied, engine.Detect(tweak));
        Assert.Equal(JournalEntryStatus.Active, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void Apply_RequiresReboot_ReportsRebootEvenWhenDetectSeesValueImmediately()
    {
        var f = new EngineFixture();

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues() with { RequiresReboot = true });

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.True(result.RequiresReboot);
    }

    [Fact]
    public void Apply_WithoutRequiresReboot_DoesNotReportReboot()
    {
        var f = new EngineFixture();

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.False(result.RequiresReboot);
    }

    [Fact]
    public void Apply_Twice_KeepsFirstBaseline()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.TwoValues());
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(7));

        var result = engine.Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        var entry = Assert.Single(f.Journal.Current.Entries);
        Assert.Equal(RegValue.FromDWord(0), entry.Baseline[0].Value);
        Assert.Equal(2, entry.Events.Count(e => e.Action == "apply" && e.Outcome == "Applied"));
    }

    [Fact]
    public void Apply_UserScope_WritesToInteractiveUserHive()
    {
        var f = new EngineFixture();
        f.User.Sid = "S-1-5-21-42";

        var result = f.CreateEngine().Apply(TestTweaks.UserMenuDelay());

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Empty(result.Warnings);
        Assert.Equal(RegValue.FromString("10"), f.Registry.Get(@"HKU\S-1-5-21-42\Control Panel\Desktop", "MenuShowDelay"));
    }

    [Fact]
    public void Apply_UserScopeWithUncertainUser_Warns()
    {
        var f = new EngineFixture();
        f.User.IsCertain = false;

        var result = f.CreateEngine().Apply(TestTweaks.UserMenuDelay());

        Assert.Contains(f.User.Sid, Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_Service_DisablesStopsAndRemembersRunningState()
    {
        var f = new EngineFixture();
        f.Services.Add("SysMain", ServiceStartType.Automatic, running: true);

        var result = f.CreateEngine().Apply(TestTweaks.DisableService());

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Equal(ServiceStartType.Disabled, f.Services.StartTypeOf("SysMain"));
        Assert.False(f.Services.IsRunning("SysMain"));
        var baseline = Assert.Single(Assert.Single(f.Journal.Current.Entries).Baseline);
        Assert.Equal(ServiceStartType.Automatic, baseline.StartType);
        Assert.True(baseline.WasRunning);
    }

    [Fact]
    public void Apply_ServiceStopTimeout_RollsBackStartType()
    {
        var f = new EngineFixture();
        f.Services.Add("SysMain", ServiceStartType.Automatic, running: true);
        f.Services.FailStop("SysMain");

        var result = f.CreateEngine().Apply(TestTweaks.DisableService());

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Equal(ServiceStartType.Automatic, f.Services.StartTypeOf("SysMain"));
        Assert.True(f.Services.IsRunning("SysMain"));
    }

    [Fact]
    public void Apply_ScriptFails_RunsUndoForPartialEffects()
    {
        var f = new EngineFixture();
        var script = new ToggleScript { ApplyFails = true };
        script.Register(f.Scripts);

        var result = f.CreateEngine().Apply(TestTweaks.Scripted());

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Contains(f.Scripts.Calls, call => call.Body == "UNDO");
    }

    [Fact]
    public void Apply_ScriptThrows_UndoSucceeds_IsRolledBackAndEntryAborted()
    {
        // Review C1 (Review Focus pkt 4): runner rzuca przy APPLY (np. zerwany proces), UNDO zwraca 0 - rollback udany.
        var f = new EngineFixture();
        var script = new ToggleScript();
        script.Register(f.Scripts);
        f.Scripts.On("APPLY", _ => throw new InvalidOperationException("apply boom"));

        var result = f.CreateEngine().Apply(TestTweaks.Scripted());

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Contains("apply boom", result.Message, StringComparison.Ordinal);
        Assert.Contains(f.Scripts.Calls, call => call.Body == "UNDO");
        Assert.Equal("2", script.LastUndoSnapshot);
        Assert.Equal(JournalEntryStatus.Aborted, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void Apply_CancelledToken_ChangesNothing()
    {
        var f = new EngineFixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues(), cancellationToken: cts.Token);

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Contains("Anulowano", result.Message, StringComparison.Ordinal);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Equal(JournalEntryStatus.Aborted, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void Apply_UnexpectedException_IsRolledBack()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        f.Registry.WriteInterceptor = (_, name, _) => name == "B" ? new InvalidOperationException("boom") : null;

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Contains("boom", result.Message, StringComparison.Ordinal);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(TestTweaks.Path, "A"));
    }

    [Fact]
    public void Apply_OnAction_IsRefused()
        => Assert.Equal(TweakOutcome.Refused, new EngineFixture().CreateEngine().Apply(TestTweaks.Action()).Outcome);

    [Fact]
    public void Apply_MissingServiceAlongsideRegistry_IsIgnored()
    {
        var f = new EngineFixture();
        var tweak = TestTweaks.TwoValues() with
        {
            Services = new[] { new ServiceEntry { Name = "Nope", StartType = ServiceStartType.Disabled, Original = ServiceStartType.Manual } },
        };

        Assert.Equal(TweakOutcome.Applied, f.CreateEngine().Apply(tweak).Outcome);
    }

    [Fact]
    public void Apply_JournalLoadWarning_IsReported()
    {
        var f = new EngineFixture();
        f.Journal.LoadWarning = "Journal był uszkodzony.";

        Assert.Contains("Journal był uszkodzony.", f.CreateEngine().Apply(TestTweaks.TwoValues()).Warnings);
    }

    [Fact]
    public void Apply_JournalLoadWarning_IsAlsoLogged()
    {
        var f = new EngineFixture();
        f.Journal.LoadWarning = "Journal był uszkodzony.";

        f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Contains(f.Log.Lines, line => line.StartsWith("WARN", StringComparison.Ordinal) && line.IndexOf("Journal był uszkodzony.", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Apply_UnexpectedException_IsLoggedAndDoesNotThrow()
    {
        var f = new EngineFixture();
        f.Registry.WriteInterceptor = (_, name, _) => name == "A" ? new InvalidOperationException("boom") : null;

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal) && line.IndexOf("boom", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Apply_RollbackThrowsUnexpectedException_IsReportedNotThrown()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        f.Registry.FailWrite(TestTweaks.Path, "B");
        f.Registry.WriteInterceptor = (_, name, value) => name == "A" && value.Equals(RegValue.FromDWord(0))
            ? new InvalidOperationException("restore boom")
            : null;

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.FailedRollbackIncomplete, result.Outcome);
        Assert.Contains("restore boom", Assert.Single(result.NotRestored), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DetectOnlyRegistryEntry_IsNeverWritten()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "Marker", RegValue.FromDWord(5));
        var tweak = TestTweaks.TwoValues() with
        {
            Registry = TestTweaks.TwoValues().Registry.Append(new RegistryEntry
            {
                Path = TestTweaks.Path,
                Name = "Marker",
                Value = RegValue.FromDWord(1),
                Original = RegValue.FromDWord(0),
                DetectOnly = true,
            }).ToArray(),
        };

        f.CreateEngine().Apply(tweak);

        Assert.Equal(RegValue.FromDWord(5), f.Registry.Get(TestTweaks.Path, "Marker"));
        Assert.DoesNotContain(f.Registry.WriteLog, line => line.IndexOf("Marker", StringComparison.Ordinal) >= 0);
        Assert.Equal(2, Assert.Single(f.Journal.Current.Entries).Baseline.Count);
    }

    [Fact]
    public void Detect_PendingRebootPromotionLoadWarning_IsLogged()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(1));
        f.Registry.Set(TestTweaks.Path, "B", RegValue.FromDWord(1));
        f.Journal.LoadWarning = "Journal był uszkodzony.";

        f.CreateEngine().Detect(TestTweaks.TwoValues());

        Assert.Contains(f.Log.Lines, line => line.StartsWith("WARN", StringComparison.Ordinal) && line.IndexOf("Journal był uszkodzony.", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Apply_JournalSaveThrowsUnexpectedAfterChange_ReturnsResultWithWarning()
    {
        var f = new EngineFixture();
        f.Journal.SaveException = new InvalidOperationException("save boom");
        f.Journal.ThrowOnSaveFrom = 1;

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        Assert.Contains("save boom", Assert.Single(result.Warnings), StringComparison.Ordinal);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal) && line.IndexOf("save boom", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Apply_JournalSaveThrowsUnexpectedBeforeChange_FailsWithoutChanges()
    {
        var f = new EngineFixture();
        f.Journal.SaveException = new InvalidOperationException("save boom");

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Empty(f.Registry.WriteLog);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal) && line.IndexOf("save boom", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Apply_JournalLoadThrowsUnexpected_FailsWithoutChanges()
    {
        var f = new EngineFixture();
        f.Journal.LoadException = new InvalidOperationException("load boom");

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Empty(f.Registry.WriteLog);
    }

    [Fact]
    public void Apply_UserScopeDifferentSid_CreatesSeparateEntryWithOwnHive()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        f.User.Sid = "S-1-5-21-1";
        Assert.Equal(TweakOutcome.Applied, engine.Apply(TestTweaks.UserMenuDelay()).Outcome);
        f.User.Sid = "S-1-5-21-2";

        var result = engine.Apply(TestTweaks.UserMenuDelay());

        Assert.Equal(TweakOutcome.Applied, result.Outcome);
        var entries = f.Journal.Current.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal("S-1-5-21-2", entries[1].UserSid);
        Assert.Contains("S-1-5-21-2", entries[1].Baseline[0].Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ServiceStartType.Boot)]
    [InlineData(ServiceStartType.System)]
    public void Apply_ServiceIsBootOrSystemDriver_FailsBeforeAnyChange(ServiceStartType driverStartType)
    {
        var f = new EngineFixture();
        f.Services.Add("SysMain", driverStartType, running: true);

        var result = f.CreateEngine().Apply(TestTweaks.DisableService());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("sterownik", result.Message, StringComparison.Ordinal);
        Assert.Empty(f.Services.MutationCalls);
        Assert.Equal(driverStartType, f.Services.StartTypeOf("SysMain"));
        Assert.Empty(f.Journal.Current.Entries);
    }

    [Fact]
    public void Apply_IrreversibleScriptWithoutUndoFailsDetection_ReportsRollbackIncomplete()
    {
        var f = new EngineFixture();
        var script = new ToggleScript { ApplyWithoutEffect = true };
        script.Register(f.Scripts);
        var tweak = TestTweaks.Scripted() with
        {
            Reversible = false,
            Script = new ScriptSet { Apply = "APPLY", Detect = "DETECT", Capture = "CAPTURE" },
        };

        var result = f.CreateEngine().Apply(tweak);

        Assert.Equal(TweakOutcome.FailedRollbackIncomplete, result.Outcome);
        Assert.Contains("undo", Assert.Single(result.NotRestored), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JournalEntryStatus.Active, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void Apply_CancelledAfterFirstOperation_RollsBackFirstAndNeverTouchesSecond()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        using var cts = new CancellationTokenSource();
        f.Registry.WriteInterceptor = (_, name, value) =>
        {
            if (name == "A" && value.Equals(RegValue.FromDWord(1)))
            {
                cts.Cancel();
            }

            return null;
        };

        var result = f.CreateEngine().Apply(TestTweaks.TwoValues(), cancellationToken: cts.Token);

        Assert.Equal(TweakOutcome.FailedRolledBack, result.Outcome);
        Assert.Contains("Anulowano", result.Message, StringComparison.Ordinal);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(TestTweaks.Path, "A"));
        Assert.Equal(RegValue.Absent, f.Registry.Get(TestTweaks.Path, "B"));
        Assert.DoesNotContain(f.Registry.WriteLog, line => line.StartsWith(TestTweaks.Path + @"\B=", StringComparison.Ordinal));
        Assert.Equal(JournalEntryStatus.Aborted, Assert.Single(f.Journal.Current.Entries).Status);
    }
}
