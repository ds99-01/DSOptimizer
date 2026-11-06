using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Reguła strażnika ustawień: różnica między zastosowanym a stanem teraz.</summary>
public static class SettingsDrift
{
    /// <summary>
    /// Id pozycji (<c>Kind=Tweak</c>), które dziennik uznaje za zastosowane (najnowszy aktywny wpis tej pozycji, dla zakresu
    /// użytkownika – tego użytkownika, z co najmniej jednym udanym zdarzeniem <c>apply</c>: <c>Applied</c> albo
    /// <c>PendingReboot</c>; wpis z samym <c>Started</c> lub nieudanym zastosowaniem nie wchodzi), a świeża detekcja daje <c>NotApplied</c> albo <c>Partial</c>. Wpis oczekujący na restart
    /// liczy się dopiero, gdy jego ostatnie zdarzenie <c>apply</c> jest nie późniejsze niż <paramref name="since"/> (restart już był).
    /// Kolejność jak w <paramref name="tweaks"/>, bez powtórzeń.
    /// </summary>
    public static IReadOnlyList<string> Compute(
        JournalDocument journal,
        IReadOnlyList<TweakDefinition> tweaks,
        IReadOnlyDictionary<string, DetectionState> states,
        string userSid,
        DateTimeOffset since)
    {
        if (journal is null) throw new ArgumentNullException(nameof(journal));
        if (tweaks is null) throw new ArgumentNullException(nameof(tweaks));
        if (states is null) throw new ArgumentNullException(nameof(states));
        if (userSid is null) throw new ArgumentNullException(nameof(userSid));

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tweak in tweaks)
        {
            if (tweak.Kind != TweakKind.Tweak
                || !states.TryGetValue(tweak.Id, out var state)
                || state is not (DetectionState.NotApplied or DetectionState.Partial))
            {
                continue;
            }

            var entry = journal.Entries.LastOrDefault(e =>
                e.Kind is null
                && e.IsActive
                && string.Equals(e.TweakId, tweak.Id, StringComparison.Ordinal)
                && (tweak.Scope != TweakScope.User || string.Equals(e.UserSid, userSid, StringComparison.OrdinalIgnoreCase)));
            if (entry is null
                || !entry.Events.Any(e => e.Action == "apply" && RebootPending.IsRebootRelevant(e))
                || (entry.Status == JournalEntryStatus.PendingReboot && !RebootAlreadyHappened(entry, since)))
            {
                continue;
            }

            if (seen.Add(tweak.Id))
            {
                result.Add(tweak.Id);
            }
        }

        return result;
    }

    private static bool RebootAlreadyHappened(JournalEntry entry, DateTimeOffset since)
    {
        DateTimeOffset? latest = null;
        foreach (var item in entry.Events.Where(e => e.Action == "apply" && RebootPending.IsRebootRelevant(e)))
        {
            if (latest is null || item.At >= latest)
            {
                latest = item.At;
            }
        }

        return latest is { } at && at <= since;
    }
}
