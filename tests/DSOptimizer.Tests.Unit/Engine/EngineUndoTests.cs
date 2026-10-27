using System;
using System.Linq;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using Xunit;

namespace DSOptimizer.Tests.Unit.Engine;

public class EngineUndoTests
{
    [Fact]
    public void Undo_AfterApply_RestoresBaselineAndMarksUndone()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.TwoValues());

        var result = engine.Undo(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(DetectionState.NotApplied, result.FinalState);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(TestTweaks.Path, "A"));
        Assert.Equal(RegValue.Absent, f.Registry.Get(TestTweaks.Path, "B"));
        Assert.Equal(JournalEntryStatus.Undone, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void Undo_Service_RestoresStartTypeAndStartsIt()
    {
        var f = new EngineFixture();
        f.Services.Add("SysMain", ServiceStartType.Automatic, running: true);
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.DisableService());

        Assert.Equal(TweakOutcome.Undone, engine.Undo(TestTweaks.DisableService()).Outcome);
        Assert.Equal(ServiceStartType.Automatic, f.Services.StartTypeOf("SysMain"));
        Assert.True(f.Services.IsRunning("SysMain"));
    }

    [Fact]
    public void Undo_Script_PassesCapturedSnapshot()
    {
        var f = new EngineFixture();
        var script = new ToggleScript { CaptureValue = "2" };
        script.Register(f.Scripts);
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.Scripted());

        Assert.Equal(TweakOutcome.Undone, engine.Undo(TestTweaks.Scripted()).Outcome);
        Assert.Equal("2", script.LastUndoSnapshot);
    }

    [Fact]
    public void Undo_RestoreFails_ReportsIncompleteAndKeepsEntryActive()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.TwoValues());
        f.Registry.FailWrite(TestTweaks.Path, "A");

        var result = engine.Undo(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.UndoIncomplete, result.Outcome);
        Assert.Contains(result.NotRestored, item => item.Contains(@"\A"));
        Assert.Equal(JournalEntryStatus.Active, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void Undo_StateDiffersFromBaselineAfterRestore_ReportsIncomplete()
    {
        var f = new EngineFixture();
        var script = new ToggleScript { CaptureValue = "2" };
        script.Register(f.Scripts);
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.Scripted());
        script.CaptureValue = "3";

        var result = engine.Undo(TestTweaks.Scripted());

        Assert.Equal(TweakOutcome.UndoIncomplete, result.Outcome);
        Assert.Contains(result.NotRestored, item => item.Contains("różni się"));
    }

    [Fact]
    public void Undo_TwoValuesInCreatedKey_DeletesKeyOnce()
    {
        var f = new EngineFixture();
        f.Registry.CreateKey(@"HKLM\SOFTWARE\DSO");
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.TwoValuesInNewKey());
        Assert.True(f.Registry.HasKey(TestTweaks.NewKeyPath));

        var result = engine.Undo(TestTweaks.TwoValuesInNewKey());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Empty(result.Warnings);
        Assert.False(f.Registry.HasKey(@"HKLM\SOFTWARE\DSO\Nowy"));
        Assert.True(f.Registry.HasKey(@"HKLM\SOFTWARE\DSO"));
        Assert.Equal(new[] { TestTweaks.NewKeyPath, @"HKLM\SOFTWARE\DSO\Nowy" }, f.Registry.DeletedKeys);
    }

    [Fact]
    public void Undo_ForeignValueAddedLater_WarnsAndKeepsKey()
    {
        var f = new EngineFixture();
        f.Registry.CreateKey(@"HKLM\SOFTWARE\DSO");
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.TwoValuesInNewKey());
        f.Registry.Set(TestTweaks.NewKeyPath, "Obca", RegValue.FromDWord(5));

        var result = engine.Undo(TestTweaks.TwoValuesInNewKey());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        var warning = Assert.Single(result.Warnings, w => w.StartsWith("Klucz ", StringComparison.Ordinal));
        Assert.Equal(@"Klucz HKLM\SOFTWARE\DSO\Nowy pozostał po cofnięciu (nie jest pusty).", warning);
        Assert.True(f.Registry.HasKey(TestTweaks.NewKeyPath));
    }

    [Fact]
    public void Undo_LegacyJournalEntry_DoesNotDeleteKeys()
    {
        var f = new EngineFixture();
        f.Registry.CreateKey(@"HKLM\SOFTWARE\DSO");
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.TwoValuesInNewKey());
        var document = f.Journal.Current;
        var entry = Assert.Single(document.Entries);
        var legacy = entry.Baseline.Select(r => SnapshotRecord.ForRegistry(r.Path!, r.Name!, r.Value!)).ToList();
        entry.Baseline.Clear();
        entry.Baseline.AddRange(legacy);
        f.Journal.Save(document);

        var result = engine.Undo(TestTweaks.TwoValuesInNewKey());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Empty(result.Warnings);
        Assert.Empty(f.Registry.DeletedKeys);
        Assert.True(f.Registry.HasKey(TestTweaks.NewKeyPath));
    }

    [Fact]
    public void Undo_KeyDeleteDenied_UndoneWithWarning()
    {
        var f = new EngineFixture();
        f.Registry.CreateKey(@"HKLM\SOFTWARE\DSO");
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.TwoValuesInNewKey());
        f.Registry.DenyKey(TestTweaks.NewKeyPath);

        var result = engine.Undo(TestTweaks.TwoValuesInNewKey());

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Contains(result.Warnings, w => w.StartsWith("Klucz ", StringComparison.Ordinal));
        Assert.Equal(JournalEntryStatus.Undone, Assert.Single(f.Journal.Current.Entries).Status);
    }

    [Fact]
    public void Undo_WithoutJournal_RestoresCatalogDefaultsWithWarning()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(1));
        f.Registry.Set(TestTweaks.Path, "B", RegValue.FromDWord(1));

        var result = f.CreateEngine().Undo(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.UndoneFromDefaults, result.Outcome);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(TestTweaks.Path, "A"));
        Assert.Equal(RegValue.Absent, f.Registry.Get(TestTweaks.Path, "B"));
        Assert.Contains(result.Warnings, warning => warning.Contains("journal"));
    }

    [Fact]
    public void Undo_WithoutJournal_StopsAtFirstFailedOperationAndSkipsTheRest()
    {
        // Kolejność operacji: rejestr, potem skrypt; undo idzie od końca, więc skrypt jest pierwszy.
        // Jego błąd musi zatrzymać undo: wpisy rejestru nie mogą zostać zmienione na ślepo.
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(1));
        f.Registry.Set(TestTweaks.Path, "B", RegValue.FromDWord(1));
        f.Scripts.On("UNDO", 1);
        var tweak = TestTweaks.TwoValues() with { Script = new ScriptSet { Apply = "APPLY", Undo = "UNDO" } };
        var writesBefore = f.Registry.WriteLog.Count;

        var result = f.CreateEngine().Undo(tweak);

        Assert.Equal(TweakOutcome.UndoIncomplete, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(TestTweaks.Path, "A"));
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(TestTweaks.Path, "B"));
        Assert.Equal(writesBefore, f.Registry.WriteLog.Count);
        Assert.Contains(result.NotRestored, item => item.Contains("Skrypt PowerShell"));
        Assert.Single(f.Scripts.Calls);
    }

    [Fact]
    public void Undo_WithoutJournalOrDefaults_IsRefused()
    {
        var tweak = TestTweaks.TwoValues() with
        {
            Registry = new[] { new RegistryEntry { Path = TestTweaks.Path, Name = "A", Value = RegValue.FromDWord(1) } },
        };

        Assert.Equal(TweakOutcome.Refused, new EngineFixture().CreateEngine().Undo(tweak).Outcome);
    }

    [Fact]
    public void Undo_Irreversible_IsRefused()
        => Assert.Equal(TweakOutcome.Refused, new EngineFixture().CreateEngine().Undo(TestTweaks.TwoValues() with { Reversible = false }).Outcome);

    [Fact]
    public void Undo_CatalogLostOperation_RestoresRestAndReportsMissing()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.TwoValues());
        var shrunk = TestTweaks.TwoValues() with { Registry = new[] { TestTweaks.TwoValues().Registry[0] } };

        var result = engine.Undo(shrunk);

        Assert.Equal(TweakOutcome.UndoIncomplete, result.Outcome);
        Assert.Contains(result.NotRestored, item => item.Contains(@"\B") && item.Contains("brak odpowiadającej operacji"));
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(TestTweaks.Path, "A"));
    }

    [Fact]
    public void Undo_JournalUnreadable_Fails()
    {
        var f = new EngineFixture();
        f.Journal.FailLoad = true;

        Assert.Equal(TweakOutcome.Failed, f.CreateEngine().Undo(TestTweaks.TwoValues()).Outcome);
    }

    [Fact]
    public void Undo_JournalLoadThrowsUnexpectedException_FailsWithoutThrowing()
    {
        var f = new EngineFixture();
        f.Journal.LoadException = new InvalidOperationException("load boom");

        var result = f.CreateEngine().Undo(TestTweaks.TwoValues());

        Assert.Equal(TweakOutcome.Failed, result.Outcome);
        Assert.Contains("load boom", result.Message, StringComparison.Ordinal);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal) && line.Contains("load boom"));
    }

    [Fact]
    public void Undo_VerificationCaptureThrows_ReportsIncompleteAndKeepsEntryActive()
    {
        var f = new EngineFixture();
        var script = new ToggleScript { CaptureValue = "2" };
        script.Register(f.Scripts);
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.Scripted());
        f.Scripts.On("CAPTURE", _ => throw new InvalidOperationException("verify boom"));

        var result = engine.Undo(TestTweaks.Scripted());

        Assert.Equal(TweakOutcome.UndoIncomplete, result.Outcome);
        Assert.Contains(result.NotRestored, item => item.Contains("nie można zweryfikować"));
        Assert.Equal(JournalEntryStatus.Active, Assert.Single(f.Journal.Current.Entries).Status);
        Assert.Contains(f.Log.Lines, line => line.StartsWith("ERROR", StringComparison.Ordinal) && line.Contains("verify boom"));
    }

    [Fact]
    public void Undo_CatalogGainedOperationAfterFirstApply_RestoresNewOperationToo()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(0));
        f.Registry.Set(TestTweaks.Path, "B", RegValue.FromDWord(5));
        var full = TestTweaks.TwoValues();
        var v1 = full with { Registry = new[] { full.Registry[0] } };
        var engine = f.CreateEngine();
        Assert.Equal(TweakOutcome.Applied, engine.Apply(v1).Outcome);
        Assert.Equal(TweakOutcome.Applied, engine.Apply(full).Outcome);
        Assert.Equal(2, Assert.Single(f.Journal.Current.Entries).Baseline.Count);

        var result = engine.Undo(full);

        Assert.Equal(TweakOutcome.Undone, result.Outcome);
        Assert.Equal(RegValue.FromDWord(0), f.Registry.Get(TestTweaks.Path, "A"));
        Assert.Equal(RegValue.FromDWord(5), f.Registry.Get(TestTweaks.Path, "B"));
    }

    [Fact]
    public void Undo_OnAction_IsRefused()
        => Assert.Equal(TweakOutcome.Refused, new EngineFixture().CreateEngine().Undo(TestTweaks.Action()).Outcome);

    [Fact]
    public void Undo_JournalLoadWarning_IsInResultAndLog()
    {
        var f = new EngineFixture();
        var engine = f.CreateEngine();
        engine.Apply(TestTweaks.TwoValues());
        f.Journal.LoadWarning = "journal uszkodzony (test)";

        var result = engine.Undo(TestTweaks.TwoValues());

        Assert.Contains("journal uszkodzony (test)", result.Warnings);
        Assert.Contains(f.Log.Lines, line => line.Contains("journal uszkodzony (test)"));
    }

    [Fact]
    public void Undo_UserScope_OtherSidDoesNotTouchEntryOfFirstUser()
    {
        var f = new EngineFixture();
        f.User.Sid = "S-1-5-21-1";
        var engine = f.CreateEngine();
        Assert.Equal(TweakOutcome.Applied, engine.Apply(TestTweaks.UserMenuDelay()).Outcome);

        f.User.Sid = "S-1-5-21-2";
        var result = engine.Undo(TestTweaks.UserMenuDelay());

        Assert.Equal(TweakOutcome.UndoneFromDefaults, result.Outcome);
        var entry = Assert.Single(f.Journal.Current.Entries);
        Assert.Equal("S-1-5-21-1", entry.UserSid);
        Assert.Equal(JournalEntryStatus.Active, entry.Status);
    }

    private static TweakDefinition RegistryAndService() => new()
    {
        Id = "test.reg-and-service",
        Category = "test",
        Kind = TweakKind.Tweak,
        Title = "Rejestr i usługa",
        Risk = RiskLevel.Caution,
        Scope = TweakScope.Machine,
        Registry = new[]
        {
            new RegistryEntry { Path = TestTweaks.Path, Name = "A", Value = RegValue.FromDWord(1), Original = RegValue.FromDWord(0) },
        },
        Services = new[]
        {
            new ServiceEntry { Name = "SysMain", StartType = ServiceStartType.Disabled, Original = ServiceStartType.Automatic, Stop = true },
        },
    };

    [Fact]
    public void UndoWithoutJournal_WhenRestoreThrows_DoesNotClaimDefaultsWereRestored()
    {
        // N-2: ostrzeżenie "przywrócono wartości domyślne" dodawane przed pętlą było fałszywe, gdy break przerwał undo.
        var f = new EngineFixture();
        f.Services.Add("SysMain", ServiceStartType.Disabled, running: false);
        f.Services.FailSetStartType("SysMain");

        var result = f.CreateEngine().Undo(RegistryAndService());

        Assert.Equal(TweakOutcome.UndoIncomplete, result.Outcome);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("przywrócono"));
        Assert.Contains(result.Warnings, warning => warning.Contains("przerwana") && warning.Contains("stan nieznany"));
    }

    [Fact]
    public void UndoWithoutJournal_WhenRestoreThrows_ListsSkippedOperationsAsNotRestored()
    {
        // N-3: operacje pominięte po break (tu wpis rejestru, bo usługa jest cofana pierwsza) trafiają do NotRestored.
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(1));
        f.Services.Add("SysMain", ServiceStartType.Disabled, running: false);
        f.Services.FailSetStartType("SysMain");

        var result = f.CreateEngine().Undo(RegistryAndService());

        Assert.Equal(2, result.NotRestored.Count);
        Assert.Contains(result.NotRestored, entry => entry.Contains("SysMain") && !entry.Contains("pominięte"));
        Assert.Contains(result.NotRestored, entry => entry.Contains("pominięte (stan nieznany)"));
        Assert.Equal(RegValue.FromDWord(1), f.Registry.Get(TestTweaks.Path, "A"));
    }

    [Fact]
    public void UndoWithoutJournal_WhenAllRestored_KeepsRestoredWarning()
    {
        var f = new EngineFixture();
        f.Registry.Set(TestTweaks.Path, "A", RegValue.FromDWord(1));
        f.Services.Add("SysMain", ServiceStartType.Disabled, running: false);

        var result = f.CreateEngine().Undo(RegistryAndService());

        Assert.Equal(TweakOutcome.UndoneFromDefaults, result.Outcome);
        Assert.Contains(result.Warnings, warning => warning.Contains("przywrócono wartości domyślne"));
        Assert.Empty(result.NotRestored);
    }
}
