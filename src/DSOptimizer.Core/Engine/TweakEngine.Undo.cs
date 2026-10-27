using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;

namespace DSOptimizer.Core.Engine;

public sealed partial class TweakEngine
{
    /// <summary>
    /// Przywraca stan sprzed pierwszej zmiany (baza z journala) i weryfikuje go ponownym capture.
    /// Bez wpisu w journalu — wartości domyślne z katalogu (z ostrzeżeniem).
    /// </summary>
    public TweakResult Undo(TweakDefinition tweak, IProgress<string>? output = null, CancellationToken cancellationToken = default)
    {
        if (tweak is null)
        {
            throw new ArgumentNullException(nameof(tweak));
        }

        if (tweak.Kind != TweakKind.Tweak)
        {
            return Finish(tweak, TweakOutcome.Refused, null, "Undo dotyczy tylko kind=tweak.");
        }

        if (!tweak.Reversible)
        {
            return Finish(tweak, TweakOutcome.Refused, null, "Tweak jest oznaczony jako nieodwracalny.");
        }

        _s.Log.Info(Component, $"Undo {tweak.Id}: start");
        var warnings = UserWarnings(tweak);
        List<IOperation> mutating;
        try
        {
            mutating = _factory.Create(tweak, output).Where(operation => !operation.IsDetectOnly).ToList();
        }
        catch (Exception ex)
        {
            _s.Log.Fail(Component, $"Nieoczekiwany błąd przy budowie operacji {tweak.Id}.", ex);
            return Finish(tweak, TweakOutcome.Failed, null, $"Nie udało się przygotować operacji: {ex.Message}", warnings);
        }

        JournalLoadResult load;
        try
        {
            load = _s.Journal.Load();
        }
        catch (Exception ex)
        {
            if (ex is not JournalStoreException)
            {
                _s.Log.Fail(Component, $"Nieoczekiwany błąd odczytu journala przy {tweak.Id}.", ex);
            }

            return Finish(tweak, TweakOutcome.Failed, null, $"Nie udało się odczytać journala: {ex.Message}", warnings);
        }

        if (load.Warning is not null)
        {
            AddLoadWarning(load.Warning, warnings);
        }

        var entry = load.Document.FindActive(tweak.Id, ScopedUserSid(tweak));
        if (entry is null)
        {
            // Bez własnego wpisu stan sprzed zmiany jest nieznany: gdy konfliktująca pozycja jest aktywna, domyślne z katalogu
            // nadpisałyby jej wartość i zgłosiły sukces, więc odmawiamy przed jakimkolwiek Restore.
            var conflict = FirstActiveConflict(tweak, load.Document);
            return conflict is not null
                ? Finish(tweak, TweakOutcome.Refused, null, ConflictMessage(conflict), warnings)
                : UndoFromDefaults(tweak, mutating, warnings, cancellationToken);
        }

        return UndoFromJournal(tweak, load.Document, entry, mutating, warnings, cancellationToken);
    }

    private TweakResult UndoFromJournal(
        TweakDefinition tweak,
        JournalDocument document,
        JournalEntry entry,
        IReadOnlyList<IOperation> mutating,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var pairs = new List<(IOperation Operation, SnapshotRecord Record)>();
        foreach (var record in entry.Baseline)
        {
            var operation = mutating.FirstOrDefault(candidate => candidate.Owns(record));
            if (operation is null)
            {
                problems.Add($"{record.Describe()}: brak odpowiadającej operacji w katalogu");
            }
            else
            {
                pairs.Add((operation, record));
            }
        }

        for (var i = pairs.Count - 1; i >= 0; i--)
        {
            try
            {
                pairs[i].Operation.Restore(pairs[i].Record, cancellationToken);
            }
            catch (Exception ex)
            {
                _s.Log.Fail(Component, $"Undo {pairs[i].Operation.Describe()} nie powiodło się.", ex);
                problems.Add($"{pairs[i].Operation.Describe()}: {ex.Message}");
            }
        }

        if (problems.Count == 0)
        {
            VerifyRestored(pairs, problems, warnings);
        }

        var outcome = problems.Count == 0 ? TweakOutcome.Undone : TweakOutcome.UndoIncomplete;
        if (outcome == TweakOutcome.Undone)
        {
            entry.Status = JournalEntryStatus.Undone;
        }

        AddEvent(entry, "undo", outcome.ToString(), problems.Count == 0 ? null : string.Join("; ", problems));
        TrySave(document, warnings);
        return Finish(tweak, outcome, SafeDetect(tweak), null, warnings, problems, requiresReboot: tweak.RequiresReboot && outcome == TweakOutcome.Undone);
    }

    private void VerifyRestored(IReadOnlyList<(IOperation Operation, SnapshotRecord Record)> pairs, List<string> problems, List<string> warnings)
    {
        foreach (var (operation, record) in pairs)
        {
            try
            {
                var current = operation.Capture(CancellationToken.None);
                if (!SnapshotComparer.SameState(record, current))
                {
                    problems.Add($"{operation.Describe()}: po cofnięciu stan różni się od zapisanego ({current.Describe()})");
                }
                else if (record.CreatedKeyRoot is { } root && !string.Equals(root, current.CreatedKeyRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var warning = $"Klucz {root} pozostał po cofnięciu (nie jest pusty).";
                    if (!warnings.Contains(warning))
                    {
                        warnings.Add(warning);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is not OperationException)
                {
                    _s.Log.Fail(Component, $"Nieoczekiwany błąd weryfikacji {operation.Describe()}.", ex);
                }

                problems.Add($"{operation.Describe()}: nie można zweryfikować stanu – {ex.Message}");
            }
        }
    }

    private TweakResult UndoFromDefaults(TweakDefinition tweak, IReadOnlyList<IOperation> mutating, List<string> warnings, CancellationToken cancellationToken)
    {
        var defaults = mutating.Select(operation => (Operation: operation, Record: operation.DefaultsSnapshot())).ToList();
        if (defaults.Count == 0 || defaults.Any(pair => pair.Record is null))
        {
            return Finish(tweak, TweakOutcome.Refused, null, "Brak wpisu w journalu i brak wartości domyślnych w katalogu – nie ma danych do cofnięcia.", warnings);
        }

        var problems = new List<string>();
        var interrupted = false;
        for (var i = defaults.Count - 1; i >= 0; i--)
        {
            try
            {
                defaults[i].Operation.Restore(defaults[i].Record!, cancellationToken);
            }
            catch (Exception ex)
            {
                _s.Log.Fail(Component, $"Undo (domyślne) {defaults[i].Operation.Describe()} nie powiodło się.", ex);
                problems.Add($"{defaults[i].Operation.Describe()}: {ex.Message}");

                // Bez wpisu w journalu stan sprzed zmiany jest nieznany: po pierwszym błędzie nie zmieniamy dalej na ślepo.
                // Pominięte operacje (wcześniejsze na liście) trafiają do NotRestored, żeby raport był kompletny.
                for (var skipped = i - 1; skipped >= 0; skipped--)
                {
                    problems.Add($"{defaults[skipped].Operation.Describe()}: pominięte (stan nieznany)");
                }

                interrupted = true;
                break;
            }
        }

        warnings.Add(interrupted
            ? "Brak wpisu w journalu – próba przywrócenia wartości domyślnych przerwana – stan nieznany."
            : "Brak wpisu w journalu – przywrócono wartości domyślne z katalogu (stan sprzed zmiany na tym PC jest nieznany).");

        var outcome = problems.Count == 0 ? TweakOutcome.UndoneFromDefaults : TweakOutcome.UndoIncomplete;
        return Finish(tweak, outcome, SafeDetect(tweak), null, warnings, problems, requiresReboot: tweak.RequiresReboot && problems.Count == 0);
    }
}
