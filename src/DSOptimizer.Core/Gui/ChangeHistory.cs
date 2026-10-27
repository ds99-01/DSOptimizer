using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>Budowa listy „Historia zmian” z journala (bez WPF, bez dostępu do systemu).</summary>
public static class ChangeHistory
{
    /// <summary>
    /// Wpisy od najnowszego <c>AppliedAt</c>; przy remisie późniejszy w journalu pierwszy.
    /// <paramref name="since"/> = ostatni start systemu (reguła restartu jak w <see cref="RebootPending"/>).
    /// <paramref name="accountName"/> wołane najwyżej raz na SID; null, pusty wynik albo wyjątek = pokazujemy SID.
    /// </summary>
    public static IReadOnlyList<HistoryEntry> Build(
        JournalDocument journal,
        IReadOnlyList<GuiCategory> categories,
        DateTimeOffset since,
        string currentSid,
        Func<string, string?> accountName)
    {
        if (journal is null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        if (categories is null)
        {
            throw new ArgumentNullException(nameof(categories));
        }

        if (accountName is null)
        {
            throw new ArgumentNullException(nameof(accountName));
        }

        var catalog = new Dictionary<string, TweakDefinition>(StringComparer.Ordinal);
        var categoryOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var categoryIdOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in categories)
        {
            foreach (var item in category.Items)
            {
                if (!catalog.ContainsKey(item.Id))
                {
                    catalog[item.Id] = item;
                    categoryOf[item.Id] = category.Name;
                    categoryIdOf[item.Id] = category.Id;
                }
            }
        }

        var pendingIds = new HashSet<string>(RebootPending.Compute(journal, catalog, since), StringComparer.Ordinal);
        var pendingOwner = FindPendingOwners(journal, pendingIds);
        var pendingRemovals = new HashSet<JournalEntry>(RebootPending.ComputeAppRemovals(journal, since));
        var pendingInstalls = new HashSet<JournalEntry>(RebootPending.ComputeAppInstalls(journal, since));
        var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string ResolveAccount(string sid)
        {
            if (accounts.TryGetValue(sid, out var cached))
            {
                return cached;
            }

            string? name;
            try
            {
                name = accountName(sid);
            }
            catch (Exception)
            {
                name = null;
            }

            var text = string.IsNullOrWhiteSpace(name) ? sid : name!;
            accounts[sid] = text;
            return text;
        }

        var result = new List<(HistoryEntry Item, int Index)>(journal.Entries.Count);
        for (var index = 0; index < journal.Entries.Count; index++)
        {
            var entry = journal.Entries[index];
            if (entry.Kind == JournalEntry.AppRemovalKind)
            {
                result.Add((BuildRemoval(entry, pendingRemovals.Contains(entry), ResolveAccount(entry.UserSid)), index));
                continue;
            }

            if (entry.Kind == JournalEntry.AppInstallKind)
            {
                result.Add((BuildInstall(entry, pendingInstalls.Contains(entry), ResolveAccount(entry.UserSid)), index));
                continue;
            }

            var inCatalog = catalog.TryGetValue(entry.TweakId, out var tweak);
            var isUser = inCatalog && tweak!.Scope == TweakScope.User;

            string account;
            if (!inCatalog)
            {
                account = HistoryText.UnknownAccount;
            }
            else if (isUser)
            {
                account = ResolveAccount(entry.UserSid);
            }
            else
            {
                account = HistoryText.WholeComputer;
            }

            var pending = pendingOwner.TryGetValue(entry.TweakId, out var owner) && ReferenceEquals(owner, entry);
            var state = StateOf(entry.Status, pending);

            string? blocked = null;
            if (entry.IsActive)
            {
                if (!inCatalog)
                {
                    blocked = HistoryText.UndoOutOfCatalog;
                }
                else if (isUser && !string.Equals(entry.UserSid, currentSid, StringComparison.OrdinalIgnoreCase))
                {
                    blocked = HistoryText.UndoOtherAccount(account);
                }
                else if (!ReferenceEquals(entry, journal.FindActive(entry.TweakId, isUser ? entry.UserSid : null)))
                {
                    blocked = HistoryText.UndoNotNewest;
                }
            }

            var events = entry.Events
                .Select(e => new HistoryEvent(e.At, HistoryText.Event(e.Action, e.Outcome, e.Message), IsFailure(e.Outcome)))
                .ToList();

            result.Add((
                new HistoryEntry(
                    entry.EntryId,
                    entry.TweakId,
                    inCatalog ? tweak!.Title : entry.TweakId,
                    inCatalog,
                    inCatalog ? categoryOf[entry.TweakId] : null,
                    entry.AppliedAt,
                    account,
                    state,
                    events,
                    entry.IsActive,
                    blocked,
                    HistoryKind.Change,
                    inCatalog ? GuiIcons.ForCategory(categoryIdOf[entry.TweakId]) : "Tools",
                    null),
                index));
        }

        return result
            .OrderByDescending(pair => pair.Item.AppliedAt)
            .ThenByDescending(pair => pair.Index)
            .Select(pair => pair.Item)
            .ToList();
    }

    public static bool Matches(HistoryState state, HistoryFilter filter) => filter switch
    {
        HistoryFilter.All => true,
        HistoryFilter.Active => state is HistoryState.Active or HistoryState.PendingReboot or HistoryState.Removed or HistoryState.Installed,
        HistoryFilter.PendingReboot => state is HistoryState.PendingReboot or HistoryState.UndonePendingReboot,
        HistoryFilter.Inactive => state is HistoryState.Undone or HistoryState.UndonePendingReboot or HistoryState.Aborted
            or HistoryState.RemovalNotConfirmed or HistoryState.RemovalFailed or HistoryState.InstallNotConfirmed or HistoryState.InstallNotDone,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
    };

    public static int ActiveCount(IReadOnlyList<HistoryEntry> entries)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        return entries.Count(entry => Matches(entry.State, HistoryFilter.Active));
    }

    /// <summary>Wpis usunięcia aplikacji: tytuł z <c>Title</c>, kategoria „Aplikacje”, bez „Cofnij” (usunięć nie cofa się z programu).</summary>
    private static HistoryEntry BuildRemoval(JournalEntry entry, bool pending, string account)
    {
        var last = entry.Events.Count == 0 ? null : entry.Events[entry.Events.Count - 1].Outcome;
        var state = entry.Status switch
        {
            JournalEntryStatus.Active => HistoryState.Removed,
            JournalEntryStatus.PendingReboot when pending => HistoryState.PendingReboot,
            _ => last switch
            {
                "Removed" => HistoryState.Removed,
                "Failed" => HistoryState.RemovalFailed,
                _ => HistoryState.RemovalNotConfirmed,
            },
        };

        var reason = state is HistoryState.RemovalFailed or HistoryState.RemovalNotConfirmed && entry.Events.Count > 0
            ? HistoryText.ShortReason(entry.Events[entry.Events.Count - 1].Message)
            : null;
        return AppEntry(entry, account, state, HistoryText.AppsCategory, null, HistoryKind.AppRemoval, GuiIcons.Apps, reason);
    }

    /// <summary>
    /// Wpis instalacji aplikacji: tytuł z <c>Title</c>, kategoria „Instalacja”, bez „Cofnij” (podpowiedź w <c>UndoBlockedReason</c>).
    /// Status <c>Active</c> albo ostatni wynik <c>Installed</c>/<c>InstalledUnverified</c>/<c>AlreadyInstalled</c> = „Zainstalowana”,
    /// <c>Failed</c> = „Nie zainstalowano”, reszta (limit czasu, brak potwierdzenia, przerwane w trakcie) = „Nie potwierdzono instalacji”.
    /// </summary>
    private static HistoryEntry BuildInstall(JournalEntry entry, bool pending, string account)
    {
        var last = entry.Events.Count == 0 ? null : entry.Events[entry.Events.Count - 1].Outcome;
        var state = entry.Status switch
        {
            JournalEntryStatus.Active => HistoryState.Installed,
            JournalEntryStatus.PendingReboot when pending => HistoryState.PendingReboot,
            _ => last switch
            {
                "Installed" or "InstalledUnverified" or "AlreadyInstalled" => HistoryState.Installed,
                "Failed" => HistoryState.InstallNotDone,
                _ => HistoryState.InstallNotConfirmed,
            },
        };

        var reason = state is HistoryState.InstallNotDone or HistoryState.InstallNotConfirmed && entry.Events.Count > 0
            ? HistoryText.ShortReason(entry.Events[entry.Events.Count - 1].Reason)
            : null;
        return AppEntry(entry, account, state, HistoryText.InstallCategory, HistoryText.InstallUndoBlocked, HistoryKind.AppInstall, GuiIcons.Install, reason);
    }

    private static HistoryEntry AppEntry(JournalEntry entry, string account, HistoryState state, string category, string? undoBlocked, HistoryKind kind, string iconKind, string? reason)
    {
        var events = entry.Events
            .Select(e => new HistoryEvent(e.At, HistoryText.Event(e.Action, e.Outcome, e.Message, e.Reason), IsFailure(e.Outcome)))
            .ToList();
        var title = string.IsNullOrWhiteSpace(entry.Title) ? entry.TweakId : entry.Title!;
        return new HistoryEntry(
            entry.EntryId,
            entry.TweakId,
            title,
            true,
            category,
            entry.AppliedAt,
            account,
            state,
            events,
            false,
            undoBlocked,
            kind,
            iconKind,
            reason);
    }

    private static HistoryState StateOf(JournalEntryStatus status, bool pending) => status switch
    {
        JournalEntryStatus.Undone => pending ? HistoryState.UndonePendingReboot : HistoryState.Undone,
        JournalEntryStatus.Aborted => pending ? HistoryState.PendingReboot : HistoryState.Aborted,
        _ => pending ? HistoryState.PendingReboot : HistoryState.Active,
    };

    /// <summary>Dla każdego id „czekającego na restart” wpis z najpóźniejszym istotnym zdarzeniem (remis: późniejszy w journalu).</summary>
    private static Dictionary<string, JournalEntry> FindPendingOwners(JournalDocument journal, HashSet<string> pendingIds)
    {
        var owners = new Dictionary<string, JournalEntry>(StringComparer.Ordinal);
        var latest = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var entry in journal.Entries)
        {
            if (!pendingIds.Contains(entry.TweakId))
            {
                continue;
            }

            foreach (var item in entry.Events.Where(RebootPending.IsRebootRelevant))
            {
                if (!latest.TryGetValue(entry.TweakId, out var at) || item.At >= at)
                {
                    latest[entry.TweakId] = item.At;
                    owners[entry.TweakId] = entry;
                }
            }
        }

        return owners;
    }

    private static bool IsFailure(string outcome)
        => HistoryText.TryParseOutcome(outcome, out var parsed)
           && parsed is TweakOutcome.Failed or TweakOutcome.FailedRolledBack or TweakOutcome.FailedRollbackIncomplete
               or TweakOutcome.Refused or TweakOutcome.UndoIncomplete;
}
