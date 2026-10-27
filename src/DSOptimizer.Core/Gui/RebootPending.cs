using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Reguła banera „Wymagany restart”: zdarzenia z journala po ostatnim starcie systemu.</summary>
public static class RebootPending
{
    private const string AppRemovalAction = "remove-app";
    private const string AppInstallAction = "install-app";

    /// <summary>
    /// Id pozycji z <c>RequiresReboot</c>, których ostatnie zdarzenie <c>apply</c> (Applied/PendingReboot) albo
    /// <c>undo</c> (Undone) ma czas późniejszy niż <paramref name="since"/>. Kolejność wg tego zdarzenia (od najstarszego),
    /// bez powtórzeń; id spoza katalogu pomijane. Zdarzenia <c>detect</c> i porażki się nie liczą.
    /// </summary>
    public static IReadOnlyList<string> Compute(
        JournalDocument journal,
        IReadOnlyDictionary<string, TweakDefinition> catalog,
        DateTimeOffset since)
        => Ordered(journal, catalog, since).Select(pair => pair.Key).ToList();

    /// <summary>
    /// Te same pozycje co <see cref="Compute"/> (i w tej samej kolejności), z rodzajem oczekiwania: <c>apply</c> daje
    /// <see cref="RebootPendingKind.Applied"/>, <c>undo</c> – <see cref="RebootPendingKind.Undone"/>, wg najpóźniejszego istotnego zdarzenia.
    /// </summary>
    public static IReadOnlyDictionary<string, RebootPendingKind> ComputeKinds(
        JournalDocument journal,
        IReadOnlyDictionary<string, TweakDefinition> catalog,
        DateTimeOffset since)
        => Ordered(journal, catalog, since).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>
    /// Wpisy usunięć aplikacji (<c>Kind="app-removal"</c>) czekające na restart: status <c>PendingReboot</c>, a ostatnie zdarzenie
    /// <c>remove-app</c> późniejsze niż <paramref name="since"/>. Od najstarszego zdarzenia. Usunięć nie ma w katalogu, więc
    /// nie wchodzą do <see cref="Compute"/> ani <see cref="ComputeKinds"/> (nie ma dla nich „Cofnij” ani dopisku wiersza).
    /// </summary>
    public static IReadOnlyList<JournalEntry> ComputeAppRemovals(JournalDocument journal, DateTimeOffset since)
        => PendingApps(journal, JournalEntry.AppRemovalKind, AppRemovalAction, since);

    /// <summary>
    /// Wpisy instalacji aplikacji (<c>Kind="app-install"</c>) czekające na restart: status <c>PendingReboot</c>, a ostatnie zdarzenie
    /// <c>install-app</c> późniejsze niż <paramref name="since"/>. Od najstarszego zdarzenia; jak usunięcia – bez „Cofnij” i dopisku wiersza.
    /// </summary>
    public static IReadOnlyList<JournalEntry> ComputeAppInstalls(JournalDocument journal, DateTimeOffset since)
        => PendingApps(journal, JournalEntry.AppInstallKind, AppInstallAction, since);

    private static List<JournalEntry> PendingApps(JournalDocument journal, string kind, string action, DateTimeOffset since)
    {
        if (journal is null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        return journal.Entries
            .Where(entry => entry.Kind == kind && entry.Status == JournalEntryStatus.PendingReboot)
            .Select(entry => (Entry: entry, At: LastEventAt(entry, action)))
            .Where(pair => pair.At is { } at && at > since)
            .OrderBy(pair => pair.At)
            .Select(pair => pair.Entry)
            .ToList();
    }

    private static DateTimeOffset? LastEventAt(JournalEntry entry, string action)
    {
        DateTimeOffset? latest = null;
        foreach (var item in entry.Events.Where(e => e.Action == action))
        {
            if (latest is null || item.At >= latest)
            {
                latest = item.At;
            }
        }

        return latest;
    }

    private static List<KeyValuePair<string, RebootPendingKind>> Ordered(
        JournalDocument journal,
        IReadOnlyDictionary<string, TweakDefinition> catalog,
        DateTimeOffset since)
    {
        if (journal is null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        var latest = new Dictionary<string, (DateTimeOffset At, RebootPendingKind Kind)>(StringComparer.Ordinal);
        foreach (var entry in journal.Entries)
        {
            if (entry.Kind is not null || !catalog.TryGetValue(entry.TweakId, out var tweak) || !tweak.RequiresReboot)
            {
                continue;
            }

            foreach (var item in entry.Events.Where(IsRebootRelevant))
            {
                if (!latest.TryGetValue(entry.TweakId, out var known) || item.At >= known.At)
                {
                    latest[entry.TweakId] = (item.At, item.Action == "undo" ? RebootPendingKind.Undone : RebootPendingKind.Applied);
                }
            }
        }

        return latest
            .Where(pair => pair.Value.At > since)
            .OrderBy(pair => pair.Value.At)
            .Select(pair => new KeyValuePair<string, RebootPendingKind>(pair.Key, pair.Value.Kind))
            .ToList();
    }

    internal static bool IsRebootRelevant(JournalEvent item)
        => (item.Action == "apply" && item.Outcome is "Applied" or "PendingReboot")
           || (item.Action == "undo" && item.Outcome == "Undone");
}
