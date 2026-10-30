using System;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class BatchRunTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    private static SeriesConfirmation Confirmation(GuiOperation op, bool restorePoint, int items = 3)
        => new(
            op,
            "t",
            "s",
            Array.Empty<SeriesFact>(),
            Enumerable.Range(1, items)
                .Select(i => new SeriesItem("id" + i, "Pozycja " + i, RiskLevel.Safe, "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SeriesFact>()))
                .ToList(),
            "Zastosuj",
            restorePoint);

    private static TweakResult Result(string id, TweakOutcome outcome, string? message = null, bool reboot = false,
        string[]? notRestored = null, string[]? warnings = null)
        => new()
        {
            TweakId = id,
            Outcome = outcome,
            Message = message,
            RequiresReboot = reboot,
            NotRestored = notRestored ?? Array.Empty<string>(),
            Warnings = warnings ?? Array.Empty<string>(),
        };

    private static GuiOperationReport Report(BatchResult batch)
        => new() { TweakId = "series", Operation = GuiOperation.Apply, Batch = batch };

    private static BatchRun CompleteOne(TweakResult result, GuiOperation op = GuiOperation.Apply)
    {
        var run = new BatchRun(Confirmation(op, false, 1));
        run.Complete(Report(new BatchResult { Results = new[] { result } }));
        return run;
    }

    [Fact]
    public void Complete_RefusalWithoutBatch_FailsWithReportLine()
    {
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, true, 1));
        var refusal = new GuiOperationReport
        {
            TweakId = "usuwanie",
            Operation = GuiOperation.RemoveApps,
            Lines = new[] { new GuiLogLine(T0, GuiLogLevel.Warning, "Trwa inna operacja – nic nie zmieniono.") },
        };

        run.Complete(refusal);

        Assert.True(run.IsFinished);
        Assert.All(run.Steps, step => Assert.Equal(new[] { "Trwa inna operacja – nic nie zmieniono." }, step.Details));
    }

    [Fact]
    public void Complete_NoBatchNoLines_FailsWithGenericText()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 1));

        run.Complete(new GuiOperationReport { TweakId = "series", Operation = GuiOperation.Apply });

        Assert.Equal(new[] { "Brak wyniku serii." }, run.Steps[0].Details);
    }

    [Fact]
    public void New_ApplyWithRestorePoint_StepsWaiting()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, true));

        Assert.Equal(new[] { "Punkt przywracania", "Pozycja 1", "Pozycja 2", "Pozycja 3" }, run.Steps.Select(s => s.Title));
        Assert.All(run.Steps, s => Assert.Equal(BatchStepState.Waiting, s.State));
        Assert.All(run.Steps, s => Assert.Equal("Czeka", s.StatusText));
        Assert.Null(run.Steps[0].TweakId);
        Assert.Equal("id1", run.Steps[1].TweakId);
        Assert.Equal("Zastosowywanie zmian", run.Title);
        Assert.Equal("krok 1 z 4", run.Subtitle);
        Assert.Equal(0, run.Progress);
        Assert.False(run.IsFinished);
    }

    [Fact]
    public void New_Undo_HasNoRestorePointAndUndoTitle()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Undo, false));

        Assert.Equal(3, run.Steps.Count);
        Assert.Equal("Cofanie zmian", run.Title);
        Assert.Equal("krok 1 z 3", run.Subtitle);
    }

    [Fact]
    public void OnStep_StartedThenFinished_UpdatesStateAndCounter()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, true));
        var changes = 0;
        run.Changed += (_, _) => changes++;

        run.OnStep(new BatchStepEvent(0, 4, BatchStepKind.RestorePoint, null, BatchStepPhase.Started, T0));
        Assert.Equal(BatchStepState.Running, run.Steps[0].State);
        Assert.Equal("W toku", run.Steps[0].StatusText);

        run.OnStep(new BatchStepEvent(0, 4, BatchStepKind.RestorePoint, null, BatchStepPhase.Finished, T0.AddSeconds(3),
            RestorePoint: new RestorePointResult(RestorePointStatus.Created, null)));
        Assert.Equal(BatchStepState.Done, run.Steps[0].State);
        Assert.Equal("Utworzony, 3 s", run.Steps[0].StatusText);
        Assert.Equal(0.25, run.Progress, 3);

        run.OnStep(new BatchStepEvent(1, 4, BatchStepKind.Item, "id1", BatchStepPhase.Started, T0));
        Assert.Equal("krok 2 z 4", run.Subtitle);
        Assert.Equal(BatchStepState.Running, run.Steps[1].State);

        run.OnStep(new BatchStepEvent(1, 4, BatchStepKind.Item, "id1", BatchStepPhase.Finished, T0,
            Result: Result("id1", TweakOutcome.Applied)));
        Assert.Equal(BatchStepState.Done, run.Steps[1].State);
        Assert.Equal("Zastosowane i sprawdzone", run.Steps[1].StatusText);
        Assert.Equal(0.5, run.Progress, 3);
        Assert.Equal(4, changes);
    }

    [Theory]
    [InlineData(TweakOutcome.Applied, BatchStepState.Done, "Zastosowane i sprawdzone")]
    [InlineData(TweakOutcome.PendingReboot, BatchStepState.Done, "Zastosowane, po restarcie")]
    [InlineData(TweakOutcome.AlreadyApplied, BatchStepState.Done, "Było już zastosowane")]
    [InlineData(TweakOutcome.Undone, BatchStepState.Done, "Cofnięte i sprawdzone")]
    [InlineData(TweakOutcome.UndoneFromDefaults, BatchStepState.Done, "Cofnięte do wartości domyślnych")]
    [InlineData(TweakOutcome.NotSupported, BatchStepState.Warning, "Nieobsługiwane w tym systemie")]
    [InlineData(TweakOutcome.Refused, BatchStepState.Warning, "Odmowa")]
    [InlineData(TweakOutcome.Skipped, BatchStepState.Skipped, "Pominięte")]
    [InlineData(TweakOutcome.Failed, BatchStepState.Failed, "Błąd")]
    [InlineData(TweakOutcome.FailedRolledBack, BatchStepState.Failed, "Błąd")]
    [InlineData(TweakOutcome.FailedRollbackIncomplete, BatchStepState.Failed, "Błąd")]
    [InlineData(TweakOutcome.UndoIncomplete, BatchStepState.Failed, "Błąd")]
    public void Complete_OutcomeTexts(TweakOutcome outcome, BatchStepState state, string text)
    {
        var run = CompleteOne(Result("id1", outcome, "komunikat"));

        Assert.Equal(state, run.Steps[0].State);
        Assert.Equal(text, run.Steps[0].StatusText);
    }

    [Fact]
    public void Complete_SkippedByStop_UsesStoppedText()
    {
        var run = CompleteOne(Result("id1", TweakOutcome.Skipped, TweakEngine.StoppedMessage));

        Assert.Equal(BatchStepState.Skipped, run.Steps[0].State);
        Assert.Equal("Pominięte – zatrzymano", run.Steps[0].StatusText);
    }

    [Fact]
    public void Complete_Refused_DetailsIsMessage()
    {
        var run = CompleteOne(Result("id1", TweakOutcome.Refused, "Wymaga konta administratora."));

        Assert.Equal(new[] { "Wymaga konta administratora." }, run.Steps[0].Details);
    }

    [Fact]
    public void Complete_RolledBack_ExplainsRollback()
    {
        var run = CompleteOne(Result("id1", TweakOutcome.FailedRolledBack, "Weryfikacja nie przeszła."));

        Assert.Equal(new[] { "Weryfikacja nie przeszła.", "Zmiana została wycofana, stan jak przed operacją." }, run.Steps[0].Details);
    }

    [Fact]
    public void Complete_RollbackIncomplete_ListsNotRestored()
    {
        var run = CompleteOne(Result("id1", TweakOutcome.FailedRollbackIncomplete, "Błąd.", notRestored: new[] { "HKLM\\A", "Usługa B" }));

        Assert.Equal(new[] { "Błąd.", "Nie wszystko udało się wycofać:", "HKLM\\A", "Usługa B" }, run.Steps[0].Details);
        Assert.DoesNotContain(run.Steps[0].Details, d => d.Contains("została wycofana"));
    }

    [Fact]
    public void Complete_UndoIncomplete_ListsNotRestored()
    {
        var run = CompleteOne(Result("id1", TweakOutcome.UndoIncomplete, null, notRestored: new[] { "X" }), GuiOperation.Undo);

        Assert.Equal(new[] { "Nie wszystko udało się przywrócić:", "X" }, run.Steps[0].Details);
    }

    [Fact]
    public void Complete_Failed_DoesNotClaimRollback()
    {
        var run = CompleteOne(Result("id1", TweakOutcome.Failed, "Coś padło."));

        Assert.Equal(new[] { "Coś padło." }, run.Steps[0].Details);
        Assert.DoesNotContain("wycofana", run.ResultText());
    }

    [Fact]
    public void Complete_ResultWarnings_AppendedToDetails()
    {
        var run = CompleteOne(Result("id1", TweakOutcome.Failed, "M", warnings: new[] { "W1" }));

        Assert.Equal(new[] { "M", "W1" }, run.Steps[0].Details);
    }

    [Fact]
    public void Complete_OverridesEventStates()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 1));
        run.OnStep(new BatchStepEvent(0, 1, BatchStepKind.Item, "id1", BatchStepPhase.Started, T0));

        run.Complete(Report(new BatchResult { Results = new[] { Result("id1", TweakOutcome.Failed, "x") } }));

        Assert.Equal(BatchStepState.Failed, run.Steps[0].State);
        Assert.True(run.IsFinished);
        Assert.Equal(1.0, run.Progress);
    }

    [Fact]
    public void Complete_RestorePointDuration()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, true, 1));
        run.OnStep(new BatchStepEvent(0, 2, BatchStepKind.RestorePoint, null, BatchStepPhase.Started, T0));
        run.OnStep(new BatchStepEvent(0, 2, BatchStepKind.RestorePoint, null, BatchStepPhase.Finished, T0.AddSeconds(4.2)));

        run.Complete(Report(new BatchResult
        {
            RestorePoint = new RestorePointResult(RestorePointStatus.Created, null),
            Results = new[] { Result("id1", TweakOutcome.Applied) },
        }));

        Assert.Equal("Utworzony, 5 s", run.Steps[0].StatusText);
        Assert.Equal(BatchStepState.Done, run.Steps[0].State);
    }

    [Fact]
    public void Complete_RestorePointNoTimes_PlainCreated()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, true, 1));

        run.Complete(Report(new BatchResult
        {
            RestorePoint = new RestorePointResult(RestorePointStatus.Created, null),
            Results = new[] { Result("id1", TweakOutcome.Applied) },
        }));

        Assert.Equal("Utworzony", run.Steps[0].StatusText);
    }

    [Fact]
    public void Complete_RestorePointFailed_WarningAndListed()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, true, 1));

        run.Complete(Report(new BatchResult
        {
            RestorePoint = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona systemu wyłączona."),
            Warnings = new[] { "Ochrona systemu wyłączona." },
            Results = new[] { Result("id1", TweakOutcome.Applied) },
        }));

        Assert.Equal("Nie powstał", run.Steps[0].StatusText);
        Assert.Equal(BatchStepState.Warning, run.Steps[0].State);
        Assert.Equal(new[] { "Ochrona systemu wyłączona." }, run.Warnings);
    }

    [Fact]
    public void Complete_RemoveApps_WithProgram_RestorePointFailed_IsFailureStep()
    {
        // Seria z programem pyta „Kontynuować bez punktu?” – czerwony krok jak w renderze pytania.
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, true, 1) with { RestorePointRequired = true });

        run.Complete(Report(new BatchResult
        {
            RestorePoint = new RestorePointResult(RestorePointStatus.ProtectionDisabled, "Ochrona systemu na dysku C: jest wyłączona."),
            Results = new[] { Result("id1", TweakOutcome.Removed) },
        }));

        Assert.Equal("Nie powstał", run.Steps[0].StatusText);
        Assert.Equal(BatchStepState.Failed, run.Steps[0].State);
    }

    [Fact]
    public void Complete_RemoveApps_StoreOnly_RestorePointFailed_IsWarningStep()
    {
        // R20: seria samych aplikacji ze Sklepu idzie dalej bez pytania – krok żółty (Warning) jak w tweakach.
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, true, 1));

        run.Complete(Report(new BatchResult
        {
            RestorePoint = new RestorePointResult(RestorePointStatus.Failed, "Błąd WMI."),
            Warnings = new[] { "Punkt przywracania nie powstał: Błąd WMI." },
            Results = new[] { Result("id1", TweakOutcome.Removed) },
        }));

        Assert.Equal("Nie powstał", run.Steps[0].StatusText);
        Assert.Equal(BatchStepState.Warning, run.Steps[0].State);
        Assert.Equal(new[] { "Błąd WMI." }, run.Steps[0].Details);
    }

    [Fact]
    public void Complete_RemoveApps_FailedIsNieUdaloSie_WithoutRepeatedPrefix()
    {
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, 1));

        run.Complete(Report(new BatchResult { Results = new[] { Result("id1", TweakOutcome.Failed, AppsText.StillPresent + " Deinstalator zakończył się kodem 2.") } }));

        Assert.Equal("Nie udało się", run.Steps[0].StatusText);
        Assert.Equal(BatchStepState.Failed, run.Steps[0].State);
        Assert.Equal(new[] { "Aplikacja nadal jest na komputerze. Deinstalator zakończył się kodem 2." }, run.Steps[0].Details);
    }

    [Fact]
    public void Fail_RemoveApps_StepsSayNieUdaloSie()
    {
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, 1));

        run.Fail("Nieoczekiwany błąd: x");

        Assert.Equal("Nie udało się", run.Steps[0].StatusText);
    }

    [Theory]
    [InlineData(1, "1 aplikacja dokończy usuwanie po ponownym uruchomieniu komputera.")]
    [InlineData(3, "3 aplikacje dokończą usuwanie po ponownym uruchomieniu komputera.")]
    [InlineData(5, "5 aplikacji dokończy usuwanie po ponownym uruchomieniu komputera.")]
    public void Complete_RemoveApps_RebootTextForRemoval(int count, string expected)
    {
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, count));

        run.Complete(Report(new BatchResult
        {
            Results = Enumerable.Range(1, count).Select(i => Result("id" + i, i % 2 == 0 ? TweakOutcome.NotConfirmed : TweakOutcome.Removed, reboot: true)).ToList(),
        }));

        Assert.Equal(expected, run.RebootText);
        Assert.EndsWith("Wymagany restart. " + expected, run.ResultText());
    }

    [Fact]
    public void Complete_Apply_RebootTextAndFailureUnchanged()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 2));

        run.Complete(Report(new BatchResult
        {
            Results = new[] { Result("id1", TweakOutcome.PendingReboot, reboot: true), Result("id2", TweakOutcome.Failed, "x") },
        }));

        Assert.Equal("1 zmiana zadziała dopiero po ponownym uruchomieniu komputera.", run.RebootText);
        Assert.Equal("Błąd", run.Steps[1].StatusText);
    }

    [Fact]
    public void Complete_Stopped_TitleStopped()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 2));

        run.Complete(Report(new BatchResult
        {
            Stopped = true,
            Results = new[] { Result("id1", TweakOutcome.Applied), Result("id2", TweakOutcome.Skipped, TweakEngine.StoppedMessage) },
        }));

        Assert.Equal("Zatrzymano", run.Title);
        Assert.Equal("1 z 2 zastosowane", run.Subtitle);
    }

    [Fact]
    public void Complete_StopOnLastStep_TitleFinished()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Undo, false, 2));

        run.Complete(Report(new BatchResult
        {
            Stopped = false,
            Results = new[] { Result("id1", TweakOutcome.Undone), Result("id2", TweakOutcome.Undone) },
        }));

        Assert.Equal("Zakończono", run.Title);
        Assert.Equal("2 z 2 cofnięte", run.Subtitle);
    }

    [Fact]
    public void Complete_Warnings_MergedWithAbortReasonWithoutDuplicates()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 1));

        run.Complete(Report(new BatchResult
        {
            Aborted = true,
            AbortReason = "Przerwano.",
            Warnings = new[] { "A", "A", "Przerwano." },
            Results = new[] { Result("id1", TweakOutcome.Applied) },
        }));

        Assert.Equal(new[] { "A", "Przerwano." }, run.Warnings);
    }

    [Fact]
    public void Complete_MissingResult_StepSkipped()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 2));

        run.Complete(Report(new BatchResult { Results = new[] { Result("id1", TweakOutcome.Applied) } }));

        Assert.Equal(BatchStepState.Skipped, run.Steps[1].State);
        Assert.Equal("Pominięte", run.Steps[1].StatusText);
    }

    [Fact]
    public void Complete_NoBatch_FailsHonestly()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 1));

        run.Complete(new GuiOperationReport { TweakId = "x", Operation = GuiOperation.Apply });

        Assert.Equal("Zakończono z błędem", run.Title);
        Assert.Equal(BatchStepState.Failed, run.Steps[0].State);
    }

    [Fact]
    public void Complete_Reboot_CountsOnlySuccessful()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 3));

        run.Complete(Report(new BatchResult
        {
            Results = new[]
            {
                Result("id1", TweakOutcome.PendingReboot, reboot: true),
                Result("id2", TweakOutcome.Failed, reboot: true),
                Result("id3", TweakOutcome.Applied),
            },
        }));

        Assert.Equal(1, run.RebootCount);
        Assert.Equal("1 zmiana zadziała dopiero po ponownym uruchomieniu komputera.", run.RebootText);
    }

    [Fact]
    public void Complete_NoReboot_TextNull()
    {
        var run = CompleteOne(Result("id1", TweakOutcome.Applied));

        Assert.Equal(0, run.RebootCount);
        Assert.Null(run.RebootText);
    }

    [Theory]
    [InlineData(1, "1 zmiana zadziała dopiero po ponownym uruchomieniu komputera.")]
    [InlineData(2, "2 zmiany zadziałają dopiero po ponownym uruchomieniu komputera.")]
    [InlineData(5, "5 zmian zadziała dopiero po ponownym uruchomieniu komputera.")]
    [InlineData(12, "12 zmian zadziała dopiero po ponownym uruchomieniu komputera.")]
    [InlineData(22, "22 zmiany zadziałają dopiero po ponownym uruchomieniu komputera.")]
    public void RebootText_Plural(int count, string expected)
        => Assert.Equal(expected, GuiText.RebootBannerText(count));

    [Fact]
    public void Fail_MarksUnfinishedStepsAsFailed()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 3));
        run.OnStep(new BatchStepEvent(0, 3, BatchStepKind.Item, "id1", BatchStepPhase.Started, T0));
        run.OnStep(new BatchStepEvent(0, 3, BatchStepKind.Item, "id1", BatchStepPhase.Finished, T0, Result: Result("id1", TweakOutcome.Applied)));
        run.OnStep(new BatchStepEvent(1, 3, BatchStepKind.Item, "id2", BatchStepPhase.Started, T0));

        run.Fail("Wyjątek serii.");

        Assert.Equal(BatchStepState.Done, run.Steps[0].State);
        Assert.Equal(BatchStepState.Failed, run.Steps[1].State);
        Assert.Equal(BatchStepState.Failed, run.Steps[2].State);
        Assert.Equal("Błąd", run.Steps[2].StatusText);
        Assert.Equal(new[] { "Wyjątek serii." }, run.Steps[2].Details);
        Assert.Equal("Zakończono z błędem", run.Title);
        Assert.Equal("1 z 3 zastosowane", run.Subtitle);
        Assert.True(run.IsFinished);
    }

    [Fact]
    public void ResultText_ContainsAllSections()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 2));
        run.Complete(Report(new BatchResult
        {
            Warnings = new[] { "Ostrzeżenie serii" },
            Results = new[]
            {
                Result("id1", TweakOutcome.PendingReboot, reboot: true),
                Result("id2", TweakOutcome.FailedRolledBack, "Padło."),
            },
        }));

        var expected = string.Join(
            "\r\n",
            "Zakończono: 1 z 2 zastosowane",
            "Pozycja 1 – Zastosowane, po restarcie",
            "Pozycja 2 – Błąd",
            "  Padło.",
            "  Zmiana została wycofana, stan jak przed operacją.",
            "Ostrzeżenie serii",
            "Wymagany restart. 1 zmiana zadziała dopiero po ponownym uruchomieniu komputera.");
        Assert.Equal(expected, run.ResultText());
    }

    [Fact]
    public void Complete_IgnoresLateEvents()
    {
        var run = CompleteOne(Result("id1", TweakOutcome.Applied));
        var changes = 0;
        run.Changed += (_, _) => changes++;

        run.OnStep(new BatchStepEvent(0, 1, BatchStepKind.Item, "id1", BatchStepPhase.Started, T0));

        Assert.Equal(BatchStepState.Done, run.Steps[0].State);
        Assert.Equal("Zastosowane i sprawdzone", run.Steps[0].StatusText);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void New_RemoveApps_HasRemovalTitle_AndOptionalRestorePoint()
    {
        var withPoint = new BatchRun(Confirmation(GuiOperation.RemoveApps, true, 2));
        var withoutPoint = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, 2));

        Assert.Equal("Usuwanie aplikacji", withPoint.Title);
        Assert.Equal(new[] { "Punkt przywracania", "Pozycja 1", "Pozycja 2" }, withPoint.Steps.Select(s => s.Title));
        Assert.Equal(2, withoutPoint.Steps.Count);
        Assert.Equal("Usuwanie aplikacji", withoutPoint.Title);
    }

    [Fact]
    public void RemoveApps_StepTexts_RemovedAlreadyGoneNotConfirmedFailed()
    {
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, 4));
        run.Complete(Report(new BatchResult
        {
            Results = new[]
            {
                Result("id1", TweakOutcome.Removed, warnings: new[] { AppsText.CleanupHint("https://example.invalid") }),
                Result("id2", TweakOutcome.Removed, AppsText.AlreadyGone),
                Result("id3", TweakOutcome.NotConfirmed, AppsText.NotConfirmed),
                Result("id4", TweakOutcome.Failed, AppsText.StillPresent),
            },
        }));

        Assert.Equal(BatchStepState.Done, run.Steps[0].State);
        Assert.Equal("Usunięte i sprawdzone", run.Steps[0].StatusText);
        Assert.Equal(new[] { AppsText.CleanupHint("https://example.invalid") }, run.Steps[0].Details);
        Assert.Equal(BatchStepState.Done, run.Steps[1].State);
        Assert.Equal("Nie było już na komputerze", run.Steps[1].StatusText);
        Assert.Empty(run.Steps[1].Details);
        Assert.Equal(BatchStepState.Warning, run.Steps[2].State);
        Assert.Equal(AppsText.NotConfirmed, run.Steps[2].StatusText);
        Assert.Empty(run.Steps[2].Details);
        Assert.Equal(BatchStepState.Failed, run.Steps[3].State);
        Assert.Equal("Nie udało się", run.Steps[3].StatusText);
        Assert.Equal(new[] { "Aplikacja nadal jest na komputerze." }, run.Steps[3].Details);
        Assert.Equal("Zakończono", run.Title);
        Assert.Equal("2 z 4 usunięte", run.Subtitle);
    }

    [Fact]
    public void RemoveApps_NotConfirmedWithOwnReason_ShowsReasonInDetails()
    {
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, 1));
        run.Complete(Report(new BatchResult { Results = new[] { Result("id1", TweakOutcome.NotConfirmed, AppsText.NotConfirmedUntilReboot, reboot: true) } }));

        Assert.Equal(BatchStepState.Warning, run.Steps[0].State);
        Assert.Equal("Nie potwierdzono", run.Steps[0].StatusText);
        Assert.Equal(new[] { AppsText.NotConfirmedUntilReboot }, run.Steps[0].Details);
    }

    [Fact]
    public void RemoveApps_RemovedWithReboot_CountsForRebootBanner()
    {
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, 1));
        run.Complete(Report(new BatchResult { Results = new[] { Result("id1", TweakOutcome.Removed, reboot: true) } }));

        Assert.Equal(1, run.RebootCount);
    }

    [Fact]
    public void RemoveApps_NotConfirmedWithReboot_CountsForRebootBanner()
    {
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, 1));
        run.Complete(Report(new BatchResult { Results = new[] { Result("id1", TweakOutcome.NotConfirmed, AppsText.NotConfirmedUntilReboot, reboot: true) } }));

        Assert.Equal(1, run.RebootCount);
    }

    [Fact]
    public void RemoveApps_NotConfirmedWithoutReboot_DoesNotCountForRebootBanner()
    {
        var run = new BatchRun(Confirmation(GuiOperation.RemoveApps, false, 1));
        run.Complete(Report(new BatchResult { Results = new[] { Result("id1", TweakOutcome.NotConfirmed, AppsText.NotConfirmed) } }));

        Assert.Equal(0, run.RebootCount);
    }

    [Fact]
    public void BatchResult_NotConfirmed_CountsAsFailed()
    {
        var batch = new BatchResult { Results = new[] { Result("id1", TweakOutcome.NotConfirmed), Result("id2", TweakOutcome.Removed) } };

        Assert.Equal(1, batch.FailedCount);
        Assert.Equal(1, batch.SucceededCount);
    }

    [Theory]
    [InlineData(TweakOutcome.Installed, BatchStepState.Done, "Zainstalowane i sprawdzone")]
    [InlineData(TweakOutcome.AlreadyInstalled, BatchStepState.Done, "Była już zainstalowana")]
    [InlineData(TweakOutcome.InstalledUnverified, BatchStepState.Warning, "Zainstalowane (zgłoszone przez winget)")]
    public void Complete_InstallOutcomes(TweakOutcome outcome, BatchStepState state, string status)
    {
        var run = CompleteOne(Result("id1", outcome, "komunikat"));

        Assert.Equal(state, run.Steps[0].State);
        Assert.Equal(status, run.Steps[0].StatusText);
        Assert.Equal(new[] { "komunikat" }, run.Steps[0].Details);
    }

    [Fact]
    public void OnStep_Progress_DoesNotChangeStepOrRaise()
    {
        var run = new BatchRun(Confirmation(GuiOperation.Apply, false, 1));
        run.OnStep(new BatchStepEvent(0, 1, BatchStepKind.Item, "id1", BatchStepPhase.Started, T0));
        var changes = 0;
        run.Changed += (_, _) => changes++;

        run.OnStep(new BatchStepEvent(0, 1, BatchStepKind.Item, "id1", BatchStepPhase.Progress, T0, Stage: "Pobieranie", Percent: 40));

        Assert.Equal(BatchStepState.Running, run.Steps[0].State);
        Assert.Equal("W toku", run.Steps[0].StatusText);
        Assert.Equal(0, changes);
    }

    // --- Tryb instalacji (GuiOperation.InstallApps) ---

    private static BatchRun Install(int items = 3) => new(Confirmation(GuiOperation.InstallApps, false, items));

    private static BatchStepEvent Started(string id, int index, int count = 3)
        => new(index, count, BatchStepKind.Item, id, BatchStepPhase.Started, T0);

    private static BatchStepEvent Progress(string id, string? stage, int? percent)
        => new(0, 3, BatchStepKind.Item, id, BatchStepPhase.Progress, T0, Stage: stage, Percent: percent);

    [Fact]
    public void Install_TitleAndSubtitleWithNext()
    {
        var run = Install();

        Assert.Equal("Instalowanie aplikacji", run.Title);
        Assert.Equal("Aplikacja 1 z 3 · Dalej: Pozycja 2", run.Subtitle);
        Assert.Equal(3, run.Steps.Count);

        run.OnStep(Started("id2", 1));
        Assert.Equal("Aplikacja 2 z 3 · Dalej: Pozycja 3", run.Subtitle);

        run.OnStep(Started("id3", 2));
        Assert.Equal("Aplikacja 3 z 3", run.Subtitle);
    }

    [Fact]
    public void Install_NextSkipsItemFinishedBeforeStart()
    {
        // M6 z review Tasku 2: pozycja odrzucona przed startem (wczesne Finished bez Started) nie jest „Dalej”.
        var run = Install(4);
        run.OnStep(Started("id1", 0, 4));
        Assert.Equal("Aplikacja 1 z 4 · Dalej: Pozycja 2", run.Subtitle);

        run.OnStep(new BatchStepEvent(1, 4, BatchStepKind.Item, "id2", BatchStepPhase.Finished, T0, Result("id2", TweakOutcome.Refused, "Nie wykonano – winget nie działa")));
        Assert.Equal("Aplikacja 1 z 4 · Dalej: Pozycja 3", run.Subtitle);

        run.OnStep(new BatchStepEvent(2, 4, BatchStepKind.Item, "id3", BatchStepPhase.Finished, T0, Result("id3", TweakOutcome.Refused, "Nie wykonano – winget nie działa")));
        Assert.Equal("Aplikacja 1 z 4 · Dalej: Pozycja 4", run.Subtitle);

        run.OnStep(new BatchStepEvent(3, 4, BatchStepKind.Item, "id4", BatchStepPhase.Finished, T0, Result("id4", TweakOutcome.Refused, "Nie wykonano – winget nie działa")));
        Assert.Equal("Aplikacja 1 z 4", run.Subtitle);
    }

    [Fact]
    public void Install_EarlyFinishedBeforeFirstStart_NextIsWaitingItem()
    {
        var run = Install(3);
        run.OnStep(new BatchStepEvent(0, 3, BatchStepKind.Item, "id1", BatchStepPhase.Finished, T0, Result("id1", TweakOutcome.Refused, "Nie wykonano – winget nie działa")));
        run.OnStep(new BatchStepEvent(1, 3, BatchStepKind.Item, "id2", BatchStepPhase.Finished, T0, Result("id2", TweakOutcome.Refused, "Nie wykonano – winget nie działa")));

        run.OnStep(Started("id3", 2));

        Assert.Equal("Aplikacja 3 z 3", run.Subtitle);
    }

    [Fact]
    public void Install_SingleItem_SubtitleWithoutNext()
        => Assert.Equal("Aplikacja 1 z 1", Install(1).Subtitle);

    [Theory]
    [InlineData("Pobieranie", 45, "Pobieranie 45%")]
    [InlineData("Pobieranie", null, "Pobieranie")]
    [InlineData("Instalowanie", null, "Instalowanie")]
    [InlineData(null, 30, "W toku 30%")]
    [InlineData(null, null, "W toku")]
    public void Install_Progress_SetsStatusText(string? stage, int? percent, string expected)
    {
        var run = Install();
        run.OnStep(Started("id1", 0));
        var changes = 0;
        run.Changed += (_, _) => changes++;

        run.OnStep(Progress("id1", stage, percent));

        Assert.Equal(BatchStepState.Running, run.Steps[0].State);
        Assert.Equal(expected, run.Steps[0].StatusText);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Install_ProgressAfterResult_Ignored()
    {
        var run = Install();
        run.OnStep(Started("id1", 0));
        run.OnStep(new BatchStepEvent(0, 3, BatchStepKind.Item, "id1", BatchStepPhase.Finished, T0, Result("id1", TweakOutcome.Installed, InstallText.Installed)));

        run.OnStep(Progress("id1", "Instalowanie", null));

        Assert.Equal("Zainstalowano", run.Steps[0].StatusText);
    }

    [Theory]
    [InlineData(TweakOutcome.Installed, "Zainstalowano i sprawdzono.", BatchStepState.Done, "Zainstalowano", new string[0])]
    [InlineData(TweakOutcome.Installed, "Zainstalowano i sprawdzono – zainstalowano w wersji domyślnej (brak wersji pl-PL).", BatchStepState.Done, "Zainstalowano",
        new[] { "Zainstalowano i sprawdzono – zainstalowano w wersji domyślnej (brak wersji pl-PL)." })]
    [InlineData(TweakOutcome.AlreadyInstalled, "Była już zainstalowana – nic nie uruchomiono.", BatchStepState.Done, "Już zainstalowane",
        new[] { "Była już zainstalowana – nic nie uruchomiono." })]
    [InlineData(TweakOutcome.InstalledUnverified, "Zainstalowano (zgłoszone przez winget).", BatchStepState.Warning, "Zainstalowano (zgłoszone przez winget)", new string[0])]
    [InlineData(TweakOutcome.NotConfirmed, "Nie potwierdzono – instalacja nadal trwa w tle (program przestał czekać po 30 min).", BatchStepState.Warning,
        "Nie potwierdzono", new[] { "Instalacja nadal trwa w tle (program przestał czekać po 30 min)." })]
    [InlineData(TweakOutcome.NotConfirmed, "winget zgłosił sukces, ale programu nie widać na liście zainstalowanych. Log winget: C:\\l.log", BatchStepState.Warning,
        "Nie potwierdzono", new[] { "winget zgłosił sukces, ale programu nie widać na liście zainstalowanych. Log winget: C:\\l.log" })]
    [InlineData(TweakOutcome.Failed, "Nie wykonano – brak połączenia z internetem albo ze źródłem winget.", BatchStepState.Failed, "Nie wykonano",
        new[] { "Brak połączenia z internetem albo ze źródłem winget." })]
    [InlineData(TweakOutcome.Skipped, "Nie wykonano – poprzednia instalacja nadal trwa", BatchStepState.Skipped, "Nie wykonano",
        new[] { "Poprzednia instalacja nadal trwa" })]
    [InlineData(TweakOutcome.Refused, "Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie dso.", BatchStepState.Warning,
        "Nie wykonano", new[] { "Instalacja działa tylko na koncie zalogowanego użytkownika: uruchom DSOptimizer na koncie dso." })]
    public void Install_Statuses(TweakOutcome outcome, string message, BatchStepState state, string status, string[] details)
    {
        var run = new BatchRun(Confirmation(GuiOperation.InstallApps, false, 1));
        run.Complete(Report(new BatchResult { Results = new[] { Result("id1", outcome, message, warnings: new[] { "ostrzeżenie" }) } }));

        Assert.Equal(state, run.Steps[0].State);
        Assert.Equal(status, run.Steps[0].StatusText);
        Assert.Equal(details.Concat(new[] { "ostrzeżenie" }), run.Steps[0].Details);
    }

    [Fact]
    public void Install_SummaryAndRebootText()
    {
        var run = Install(4);
        run.Complete(Report(new BatchResult
        {
            Results = new[]
            {
                Result("id1", TweakOutcome.Installed, InstallText.Installed, reboot: true),
                Result("id2", TweakOutcome.InstalledUnverified, InstallText.InstalledUnverified),
                Result("id3", TweakOutcome.Failed, "Nie wykonano – wymaga restartu przed instalacją. Kod 0x8A15010A.", reboot: true),
                Result("id4", TweakOutcome.AlreadyInstalled, InstallText.AlreadyInstalledByWinget),
            },
        }));

        Assert.Equal("Zakończono", run.Title);
        Assert.Equal("3 z 4 zainstalowane", run.Subtitle);
        Assert.Equal(2, run.RebootCount);
        Assert.Equal(InstallText.RebootText(2), run.RebootText);
    }

    [Fact]
    public void Install_Fail_NotDoneStatus()
    {
        var run = Install(2);
        run.OnStep(Started("id1", 0, 2));
        run.OnStep(new BatchStepEvent(0, 2, BatchStepKind.Item, "id1", BatchStepPhase.Finished, T0, Result("id1", TweakOutcome.InstalledUnverified, InstallText.InstalledUnverified)));

        run.Fail("Nieoczekiwany błąd.");

        Assert.Equal("Nie wykonano", run.Steps[1].StatusText);
        Assert.Equal(new[] { "Nieoczekiwany błąd." }, run.Steps[1].Details);
        Assert.Equal("1 z 2 zainstalowane", run.Subtitle);
    }

    [Theory]
    [InlineData(1, "1 aplikacja potrzebuje ponownego uruchomienia komputera.")]
    [InlineData(2, "2 aplikacje potrzebują ponownego uruchomienia komputera.")]
    [InlineData(5, "5 aplikacji potrzebuje ponownego uruchomienia komputera.")]
    public void InstallText_RebootText(int count, string expected)
        => Assert.Equal(expected, InstallText.RebootText(count));
}
