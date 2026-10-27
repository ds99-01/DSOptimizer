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
    /// <summary>Id pierwszej pozycji z <c>conflicts</c>, która ma aktywny wpis w dokumencie journala; null, gdy brak.</summary>
    private string? FirstActiveConflict(TweakDefinition tweak, JournalDocument document)
        => tweak.Conflicts.FirstOrDefault(id => document.FindActive(id, ScopedUserSid(tweak)) is not null);

    private static string ConflictMessage(string conflictId) => $"Pozycja {conflictId} jest zastosowana i konfliktuje z tą – najpierw ją cofnij.";

    /// <summary>
    /// Stosuje tweak atomowo: snapshot → journal → zmiany → detekcja. Każdy błąd cofa już wykonane operacje.
    /// Sukces tylko wtedy, gdy detekcja po zmianie zwraca Applied (albo PendingReboot dla tweaków z restartem).
    /// Operacje detect-only nigdy nie są przechwytywane, stosowane ani przywracane.
    /// </summary>
    public TweakResult Apply(TweakDefinition tweak, IProgress<string>? output = null, CancellationToken cancellationToken = default)
    {
        if (tweak is null)
        {
            throw new ArgumentNullException(nameof(tweak));
        }

        if (tweak.Kind != TweakKind.Tweak)
        {
            return Finish(tweak, TweakOutcome.Refused, null, "Apply dotyczy tylko kind=tweak – dla akcji użyj Execute.");
        }

        _s.Log.Info(Component, $"Apply {tweak.Id}: start");
        var warnings = UserWarnings(tweak);

        // Pozycje z conflicts: jeden odczyt journala, przed jakimkolwiek efektem. Błąd odczytu = odmowa (fail-closed);
        // ten sam dokument i ostrzeżenie (np. odłożony uszkodzony journal) służą dalej, bo prawdziwy store odkłada plik przy pierwszym Load.
        JournalLoadResult? preloaded = null;
        if (tweak.Conflicts.Count > 0)
        {
            try
            {
                preloaded = _s.Journal.Load();
            }
            catch (Exception ex)
            {
                if (ex is not JournalStoreException)
                {
                    _s.Log.Fail(Component, $"Nieoczekiwany błąd odczytu journala przy sprawdzaniu konfliktów {tweak.Id}.", ex);
                }

                return Finish(tweak, TweakOutcome.Failed, null, $"Nie udało się odczytać journala – zmiana nie została wykonana: {ex.Message}", warnings);
            }

            if (preloaded.Warning is not null)
            {
                AddLoadWarning(preloaded.Warning, warnings);
            }

            var conflict = FirstActiveConflict(tweak, preloaded.Document);
            if (conflict is not null)
            {
                return Finish(tweak, TweakOutcome.Refused, null, ConflictMessage(conflict), warnings);
            }
        }

        IReadOnlyList<IOperation> operations;
        try
        {
            operations = _factory.Create(tweak, output);
        }
        catch (Exception ex)
        {
            _s.Log.Fail(Component, $"Nieoczekiwany błąd przy budowie operacji {tweak.Id}.", ex);
            return Finish(tweak, TweakOutcome.Failed, null, $"Nie udało się przygotować operacji: {ex.Message}", warnings);
        }

        var before = DetectSafely(tweak, operations, cancellationToken);
        if (before == DetectionState.NotSupported)
        {
            return Finish(tweak, TweakOutcome.NotSupported, before, "Tweak nie dotyczy tego systemu.", warnings);
        }

        if (before == DetectionState.Applied)
        {
            PromotePendingReboot(tweak, before);
            return Finish(tweak, TweakOutcome.AlreadyApplied, before, null, warnings);
        }

        var mutating = operations.Where(operation => !operation.IsDetectOnly).ToList();
        List<SnapshotRecord> snapshots;
        try
        {
            snapshots = mutating.Select(operation => operation.Capture(cancellationToken)).ToList();
        }
        catch (Exception ex)
        {
            if (ex is not OperationException)
            {
                _s.Log.Fail(Component, $"Nieoczekiwany błąd przy zapisie stanu {tweak.Id}.", ex);
            }

            return Finish(tweak, TweakOutcome.Failed, before, $"Nie udało się zapisać stanu przed zmianą: {ex.Message}", warnings);
        }

        JournalDocument document;
        JournalEntry entry;
        bool isNewEntry;
        try
        {
            var load = preloaded ?? _s.Journal.Load();
            if (preloaded is null && load.Warning is not null)
            {
                AddLoadWarning(load.Warning, warnings);
            }

            document = load.Document;
            var existing = document.FindActive(tweak.Id, ScopedUserSid(tweak));
            isNewEntry = existing is null;
            entry = existing ?? NewEntry(tweak, snapshots);
            if (isNewEntry)
            {
                document.Entries.Add(entry);
            }
            else
            {
                // Katalog mógł zyskać operację od pierwszego apply – zapisz jej stan sprzed zmiany (istniejących rekordów nie ruszamy).
                for (var i = 0; i < mutating.Count; i++)
                {
                    if (!entry.Baseline.Any(record => mutating[i].Owns(record)))
                    {
                        entry.Baseline.Add(snapshots[i]);
                    }
                }
            }

            AddEvent(entry, "apply", "Started", null);
            _s.Journal.Save(document);
        }
        catch (Exception ex)
        {
            if (ex is not JournalStoreException)
            {
                _s.Log.Fail(Component, $"Nieoczekiwany błąd journala przy {tweak.Id}.", ex);
            }

            return Finish(tweak, TweakOutcome.Failed, before, $"Nie udało się zapisać journala – zmiana nie została wykonana: {ex.Message}", warnings);
        }

        var executed = new List<int>();
        string? failure = null;
        for (var i = 0; i < mutating.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                failure = "Anulowano.";
                break;
            }

            try
            {
                mutating[i].Apply(cancellationToken);
                executed.Add(i);
            }
            catch (Exception ex)
            {
                if (ex is not OperationException)
                {
                    _s.Log.Fail(Component, $"Nieoczekiwany błąd (Unexpected) przy {mutating[i].Describe()}.", ex);
                }

                // Operacje wieloetapowe (usługa: typ startu + stop; skrypt) mogły zmienić część stanu
                // przed błędem — je też trzeba przywrócić. Nieudany zapis rejestru niczego nie zmienił.
                if (mutating[i].Kind != OperationKind.Registry)
                {
                    executed.Add(i);
                }

                failure = $"{mutating[i].Describe()}: {ex.Message}";
                break;
            }
        }

        if (failure is not null)
        {
            return RollBack(tweak, document, entry, isNewEntry, mutating, snapshots, executed, failure, warnings);
        }

        var after = DetectSafely(tweak, operations, CancellationToken.None);
        if (after == DetectionState.Applied)
        {
            entry.Status = JournalEntryStatus.Active;
            AddEvent(entry, "apply", nameof(TweakOutcome.Applied), null);
            TrySave(document, warnings);
            return Finish(tweak, TweakOutcome.Applied, after, null, warnings, requiresReboot: tweak.RequiresReboot);
        }

        if (tweak.RequiresReboot)
        {
            entry.Status = JournalEntryStatus.PendingReboot;
            AddEvent(entry, "apply", nameof(TweakOutcome.PendingReboot), null);
            TrySave(document, warnings);
            return Finish(tweak, TweakOutcome.PendingReboot, after, "Zmiana zadziała po restarcie.", warnings, requiresReboot: true);
        }

        var all = Enumerable.Range(0, mutating.Count).ToList();
        return RollBack(tweak, document, entry, isNewEntry, mutating, snapshots, all, $"Detekcja po zmianie: {after}", warnings);
    }

    private JournalEntry NewEntry(TweakDefinition tweak, IEnumerable<SnapshotRecord> baseline)
    {
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            TweakId = tweak.Id,
            UserSid = _s.User.Sid,
            Status = JournalEntryStatus.Active,
            AppliedAt = _s.Clock.UtcNow,
            CatalogHash = _catalogHash,
        };
        entry.Baseline.AddRange(baseline);
        return entry;
    }

    private TweakResult RollBack(
        TweakDefinition tweak,
        JournalDocument document,
        JournalEntry entry,
        bool isNewEntry,
        List<IOperation> mutating,
        List<SnapshotRecord> snapshots,
        List<int> executed,
        string reason,
        List<string> warnings)
    {
        var notRestored = new List<string>();
        for (var k = executed.Count - 1; k >= 0; k--)
        {
            var index = executed[k];
            try
            {
                mutating[index].Restore(snapshots[index], CancellationToken.None);
            }
            catch (Exception ex)
            {
                _s.Log.Fail(Component, $"Rollback {mutating[index].Describe()} nie powiódł się.", ex);
                notRestored.Add($"{mutating[index].Describe()}: {ex.Message}");
            }
        }

        var outcome = notRestored.Count == 0 ? TweakOutcome.FailedRolledBack : TweakOutcome.FailedRollbackIncomplete;
        if (isNewEntry && notRestored.Count == 0)
        {
            entry.Status = JournalEntryStatus.Aborted;
        }

        AddEvent(entry, "apply", outcome.ToString(), reason);
        TrySave(document, warnings);
        return Finish(tweak, outcome, SafeDetect(tweak), reason, warnings, notRestored);
    }
}
