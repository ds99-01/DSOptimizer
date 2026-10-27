using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Wpis journala <c>Kind="app-install"</c> jednej pozycji serii (winget i GitHub): otwarcie ze zdarzeniem <c>Started</c> i statusem
/// <see cref="JournalEntryStatus.Aborted"/> <b>przed</b> instalatorem, zamknięcie zdarzeniem wyniku.
/// </summary>
internal sealed class InstallJournal
{
    private const string Component = "Install";

    private readonly IJournalStore _journal;
    private readonly IUserContext _user;
    private readonly ILog _log;
    private readonly IClock _clock;

    public InstallJournal(IJournalStore journal, IUserContext user, ILog log, IClock clock)
    {
        _journal = journal;
        _user = user;
        _log = log;
        _clock = clock;
    }

    /// <summary>Dopisuje i zapisuje wpis ze zdarzeniem <c>Started</c>; błąd odczytu albo zapisu = <c>null</c> (zapisany w logu).</summary>
    public OpenEntry? Open(InstallTarget target, string startedMessage, List<string> warnings)
    {
        try
        {
            var load = _journal.Load();
            if (load.Warning is not null)
            {
                _log.Warn(Component, load.Warning);
                warnings.Add(load.Warning);
            }

            var document = load.Document;
            var entry = NewEntry(target);
            document.Entries.Add(entry);
            AddEvent(entry, "Started", startedMessage);
            _journal.Save(document);
            return new OpenEntry(document, entry);
        }
        catch (Exception ex)
        {
            if (ex is not JournalStoreException)
            {
                _log.Fail(Component, $"Nieoczekiwany błąd journala przy {target.Id}.", ex);
            }

            _log.Warn(Component, $"Nie udało się zapisać journala przy {target.Id}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Zdarzenie wyniku i status: zainstalowana = <see cref="JournalEntryStatus.Active"/> albo
    /// <see cref="JournalEntryStatus.PendingReboot"/>, inaczej zostaje <see cref="JournalEntryStatus.Aborted"/>. Błąd zapisu = ostrzeżenie.
    /// </summary>
    public void Close(OpenEntry open, TweakOutcome outcome, bool requiresReboot, string message, string? reason, List<string> warnings)
    {
        open.Entry.Status = outcome is TweakOutcome.Installed or TweakOutcome.InstalledUnverified
            ? requiresReboot ? JournalEntryStatus.PendingReboot : JournalEntryStatus.Active
            : JournalEntryStatus.Aborted;
        AddEvent(open.Entry, outcome.ToString(), message, reason);
        try
        {
            _journal.Save(open.Document);
        }
        catch (Exception ex)
        {
            _log.Warn(Component, $"Nie udało się zapisać journala: {ex.Message}");
            warnings.Add($"Nie udało się zapisać journala: {ex.Message}");
        }
    }

    private JournalEntry NewEntry(InstallTarget target) => new()
    {
        EntryId = Guid.NewGuid(),
        Kind = JournalEntry.AppInstallKind,
        TweakId = target.Id,
        Title = target.Title,
        UserSid = _user.Sid,
        // Do potwierdzenia instalacji wpis nie twierdzi, że coś zainstalowano (ślad zostaje, gdy program padnie w trakcie).
        Status = JournalEntryStatus.Aborted,
        AppliedAt = _clock.UtcNow,
    };

    private void AddEvent(JournalEntry entry, string outcome, string message, string? reason = null)
        => entry.Events.Add(new JournalEvent { At = _clock.UtcNow, Action = AppInstallEngine.JournalAction, Outcome = outcome, Message = message, Reason = reason });

    /// <summary>Otwarty wpis: dokument journala i wpis pozycji.</summary>
    internal sealed record OpenEntry(JournalDocument Document, JournalEntry Entry);
}
