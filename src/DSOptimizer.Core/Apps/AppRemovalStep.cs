using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Journal;

namespace DSOptimizer.Core.Apps;

/// <summary>
/// Krok serii usuwania (jedna aplikacja) dla <see cref="AppRemovalEngine"/>. Wynik:
/// <list type="bullet">
/// <item>zniknęła → <see cref="TweakOutcome.Removed"/> (3010/1641 = restart, inny niezerowy kod = ostrzeżenie);</item>
/// <item>została, a deinstalator nie skończył w limicie → <see cref="TweakOutcome.NotConfirmed"/>;</item>
/// <item>odczyt obecności po próbie się nie udał → <see cref="TweakOutcome.NotConfirmed"/> (R19);</item>
/// <item>AppX zniknął z konta, ale został w obrazie → <see cref="TweakOutcome.Failed"/> z osobnym tekstem (R19);</item>
/// <item>została inaczej → <see cref="TweakOutcome.Failed"/> z kodem wyjścia albo błędem skryptu.</item>
/// </list>
/// Wpis journala (<c>Kind="app-removal"</c>) powstaje przed próbą; do potwierdzenia ma status <see cref="JournalEntryStatus.Aborted"/>.
/// </summary>
internal sealed class AppRemovalStep
{
    private const string Component = "Apps";

    private readonly IInstalledAppsReader _reader;
    private readonly IAppRemover _remover;
    private readonly IJournalStore _journal;
    private readonly IUserContext _user;
    private readonly ILog _log;
    private readonly IClock _clock;
    private readonly Action<TimeSpan> _sleep;

    public AppRemovalStep(
        IInstalledAppsReader reader,
        IAppRemover remover,
        IJournalStore journal,
        IUserContext user,
        ILog log,
        IClock clock,
        Action<TimeSpan> sleep)
    {
        _reader = reader;
        _remover = remover;
        _journal = journal;
        _user = user;
        _log = log;
        _clock = clock;
        _sleep = sleep;
    }

    /// <summary>
    /// Jedna pozycja: obecność przed próbą, wpis journala, próba, weryfikacja świeżym odczytem i wynik.
    /// <paramref name="stop"/> i <paramref name="ct"/> skracają tylko okno weryfikacji (nigdy samą próbę).
    /// </summary>
    public TweakResult Run(AppRow row, StoreRemovalScope scope, BatchStop? stop, CancellationToken ct)
    {
        var app = row.App;
        var presence = app.Kind == AppKind.Program ? AppPresenceScope.User
            : scope == StoreRemovalScope.AllUsers ? AppPresenceScope.AllUsersAndImage
            : IsMicrosoft(row) ? AppPresenceScope.UserAndImage
            : AppPresenceScope.User;
        // Tylko potwierdzony brak to „już jej nie ma”; nieudany odczyt przed próbą nie blokuje próby (weryfikacja i tak sprawdza).
        if (ReadPresence(app, presence) == AppPresence.Absent)
        {
            return Finish(app.Id, TweakOutcome.Removed, AppsText.AlreadyGone, new List<string>());
        }

        var warnings = new List<string>();
        JournalDocument document;
        JournalEntry entry;
        try
        {
            var load = _journal.Load();
            if (load.Warning is not null)
            {
                _log.Warn(Component, load.Warning);
                warnings.Add(load.Warning);
            }

            document = load.Document;
            entry = NewEntry(app);
            document.Entries.Add(entry);
            AddEvent(entry, "Started", null);
            _journal.Save(document);
        }
        catch (Exception ex)
        {
            if (ex is not JournalStoreException)
            {
                _log.Fail(Component, $"Nieoczekiwany błąd journala przy {app.Id}.", ex);
            }

            return Finish(app.Id, TweakOutcome.Failed, $"Nie udało się zapisać journala – usunięcie nie zostało wykonane: {ex.Message}", warnings);
        }

        // Zakres AllUsers usuwa też z obrazu (pomiar VM), a weryfikacja sprawdza obraz, więc deprovisioning idzie zawsze w tym trybie.
        var attempt = app.Kind == AppKind.Store
            ? RemoveStore(app.Appx!, scope, scope == StoreRemovalScope.AllUsers || IsMicrosoft(row), ct)
            : RunUninstaller(row.Plan ?? throw new InvalidOperationException($"Program {app.Id} nie ma planu deinstalacji."), ct);
        var verification = WaitUntilGone(app, presence, stop, ct);
        TweakOutcome outcome;
        string? message = null;
        var requiresReboot = false;
        if (verification == Verification.Gone)
        {
            outcome = TweakOutcome.Removed;
            requiresReboot = attempt.ExitCode is 3010 or 1641;
            if (attempt.ExitCode is { } code and not (0 or 3010 or 1641))
            {
                warnings.Add(AppsText.UninstallerExitCode(code));
            }

            if (attempt.Error is not null)
            {
                warnings.Add(AppsText.RemovalError(attempt.Error));
            }

            if (app.Kind == AppKind.Store && scope == StoreRemovalScope.AllUsers)
            {
                warnings.Add(AppsText.RemovedForAllUsers);
            }
        }
        else
        {
            (outcome, message, requiresReboot) = StillPresent(row, attempt, verification);
        }

        if (!string.IsNullOrWhiteSpace(row.CleanupUrl))
        {
            warnings.Add(AppsText.CleanupHint(row.CleanupUrl!));
        }

        // Restart zgłoszony przez deinstalator (także przy NotConfirmed, R12) idzie do journala jako PendingReboot – baner czyta journal.
        entry.Status = requiresReboot ? JournalEntryStatus.PendingReboot
            : outcome == TweakOutcome.Removed ? JournalEntryStatus.Active
            : JournalEntryStatus.Aborted;
        var details = new[] { message }.Concat(warnings).Where(text => !string.IsNullOrEmpty(text)).ToList();
        AddEvent(entry, outcome.ToString(), details.Count == 0 ? null : string.Join(" ", details));
        TrySave(document, warnings);
        return Finish(app.Id, outcome, message, warnings, requiresReboot);
    }

    private Attempt RemoveStore(AppxInfo appx, StoreRemovalScope scope, bool deprovision, CancellationToken ct)
    {
        try
        {
            var script = _remover.RemoveStoreApp(appx, _user.Sid, scope, deprovision, ct);
            if (script.ExitCode == 0)
            {
                return new Attempt(null, false, null);
            }

            var reason = string.Join(" ", script.Errors.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()));
            return new Attempt(null, false, reason.Length > 0 ? reason : $"Skrypt usuwania zakończył się kodem {script.ExitCode}.");
        }
        catch (OperationException ex) when (ex.Kind is OperationErrorKind.Timeout or OperationErrorKind.Cancelled)
        {
            return new Attempt(null, true, ex.Message);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, $"Usuwanie pakietu {appx.PackageName} rzuciło wyjątek.", ex);
            return new Attempt(null, false, ex.Message);
        }
    }

    private Attempt RunUninstaller(UninstallPlan plan, CancellationToken ct)
    {
        try
        {
            var run = _remover.RunUninstaller(plan, AppRemovalTimeouts.For(plan.Mode), ct);
            return new Attempt(run.ExitCode, run.TimedOut, null);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, $"Nie udało się uruchomić deinstalatora {plan.FileName}.", ex);
            return new Attempt(null, false, ex.Message);
        }
    }

    /// <summary>
    /// Aplikacja została po próbie. Kolejność reguł:
    /// <list type="number">
    /// <item>kod 3010/1641 → <see cref="TweakOutcome.NotConfirmed"/> z restartem (R12);</item>
    /// <item>minął limit (deinstalator działa dalej albo skrypt AppX przerwany) → <see cref="TweakOutcome.NotConfirmed"/>;</item>
    /// <item>sprawdzanie przerwane przez „Zatrzymaj”/anulowanie → <see cref="TweakOutcome.NotConfirmed"/>;</item>
    /// <item>ostatni odczyt w oknie się nie udał → <see cref="TweakOutcome.NotConfirmed"/> „Nie udało się sprawdzić…” (R19);</item>
    /// <item>AppX tylko w obrazie systemu → <see cref="TweakOutcome.Failed"/> „Usunięta z konta, ale została w obrazie…” (R19);</item>
    /// <item>interaktywny deinstalator skończył (użytkownik mógł nie dokończyć) → <see cref="TweakOutcome.NotConfirmed"/>;</item>
    /// <item>cichy deinstalator z kodem 0 (mógł dokończyć w tle, R18) → <see cref="TweakOutcome.NotConfirmed"/>;</item>
    /// <item>inaczej → <see cref="TweakOutcome.Failed"/> z kodem wyjścia albo błędem.</item>
    /// </list>
    /// </summary>
    private static (TweakOutcome Outcome, string Message, bool RequiresReboot) StillPresent(AppRow row, Attempt attempt, Verification verification)
    {
        if (attempt.ExitCode is 3010 or 1641)
        {
            return (TweakOutcome.NotConfirmed, AppsText.NotConfirmedUntilReboot, true);
        }

        if (attempt.TimedOut)
        {
            return (TweakOutcome.NotConfirmed, row.App.Kind == AppKind.Store ? AppsText.StoreRemovalInterrupted(attempt.Error) : AppsText.NotConfirmed, false);
        }

        if (verification == Verification.Interrupted)
        {
            return (TweakOutcome.NotConfirmed, AppsText.VerificationInterrupted, false);
        }

        if (verification == Verification.Unknown)
        {
            return (TweakOutcome.NotConfirmed, AppsText.PresenceUnknown, false);
        }

        if (verification == Verification.ImageOnly)
        {
            return (TweakOutcome.Failed, AppsText.LeftInImage + ErrorSuffix(attempt), false);
        }

        if (row.Plan is { Mode: UninstallMode.Interactive } && attempt.ExitCode is not null)
        {
            return (TweakOutcome.NotConfirmed, AppsText.InteractiveMayNotHaveFinished, false);
        }

        // R18: cichy deinstalator z kodem 0 mógł dokończyć w tle (Inno Setup usuwa wpis na końcu). Msi i kod niezerowy = porażka.
        if (row.Plan is { Mode: UninstallMode.Quiet } && attempt.ExitCode == 0)
        {
            return (TweakOutcome.NotConfirmed, AppsText.QuietMayFinishInBackground, false);
        }

        var message = AppsText.StillPresent
            + (attempt.ExitCode is { } code ? " " + AppsText.UninstallerExitCode(code) : string.Empty)
            + ErrorSuffix(attempt);
        return (TweakOutcome.Failed, message, false);
    }

    private static string ErrorSuffix(Attempt attempt)
        => attempt.Error is not null ? " " + AppsText.RemovalError(attempt.Error) : string.Empty;

    /// <summary>
    /// Czeka na zniknięcie: odczyt co <see cref="AppRemovalTimeouts.VerifyPoll"/> przez <see cref="AppRemovalTimeouts.VerifyWindow"/>.
    /// Po pełnym oknie wynik mówi, co pokazał ostatni odczyt (nadal jest, tylko w obrazie, nie wiadomo).
    /// „Zatrzymaj” albo anulowanie przed końcem okna daje <see cref="Verification.Interrupted"/>.
    /// </summary>
    private Verification WaitUntilGone(InstalledApp app, AppPresenceScope presence, BatchStop? stop, CancellationToken ct)
    {
        var start = _clock.UtcNow;
        while (true)
        {
            var read = ReadPresence(app, presence);
            if (read == AppPresence.Absent)
            {
                return Verification.Gone;
            }

            if (_clock.UtcNow - start >= AppRemovalTimeouts.VerifyWindow)
            {
                return read switch
                {
                    AppPresence.Unknown => Verification.Unknown,
                    AppPresence.ImageOnly => Verification.ImageOnly,
                    _ => Verification.StillPresent,
                };
            }

            if (ct.IsCancellationRequested || stop is { IsRequested: true })
            {
                return Verification.Interrupted;
            }

            _sleep(AppRemovalTimeouts.VerifyPoll);
        }
    }

    /// <summary>Wyjątek odczytu = <see cref="AppPresence.Unknown"/>: brak dowodu zniknięcia to nie „usunięte”.</summary>
    private AppPresence ReadPresence(InstalledApp app, AppPresenceScope presence)
    {
        try
        {
            return _reader.ReadPresence(app, _user.Sid, presence);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, $"Odczyt obecności {app.Id} nie powiódł się.", ex);
            return AppPresence.Unknown;
        }
    }

    private static bool IsMicrosoft(AppRow row) => string.Equals(row.BloatGroup, "microsoft", StringComparison.Ordinal);

    private JournalEntry NewEntry(InstalledApp app) => new()
    {
        EntryId = Guid.NewGuid(),
        Kind = JournalEntry.AppRemovalKind,
        TweakId = app.Id,
        Title = app.Name,
        UserSid = _user.Sid,
        // Do potwierdzenia zniknięcia wpis nie twierdzi, że coś usunięto.
        Status = JournalEntryStatus.Aborted,
        AppliedAt = _clock.UtcNow,
    };

    private void AddEvent(JournalEntry entry, string outcome, string? message)
        => entry.Events.Add(new JournalEvent { At = _clock.UtcNow, Action = AppRemovalEngine.JournalAction, Outcome = outcome, Message = message });

    private void TrySave(JournalDocument document, List<string> warnings)
    {
        try
        {
            _journal.Save(document);
        }
        catch (Exception ex)
        {
            _log.Warn(Component, $"Nie udało się zapisać journala: {ex.Message}");
            warnings.Add($"Nie udało się zapisać journala: {ex.Message}");
        }
    }

    private TweakResult Finish(string id, TweakOutcome outcome, string? message, List<string> warnings, bool requiresReboot = false)
    {
        var result = new TweakResult { TweakId = id, Outcome = outcome, Message = message, Warnings = warnings, RequiresReboot = requiresReboot };
        var line = message is null ? $"{id}: {outcome}" : $"{id}: {outcome} – {message}";
        if (result.IsSuccess)
        {
            _log.Info(Component, line);
        }
        else
        {
            _log.Warn(Component, line);
        }

        return result;
    }

    /// <summary>Wynik próby: kod wyjścia deinstalatora, czy minął limit (proces działa dalej) i błąd próby.</summary>
    private sealed record Attempt(int? ExitCode, bool TimedOut, string? Error);

    /// <summary>
    /// Wynik okna weryfikacji: zniknęła; po pełnym oknie nadal jest, jest tylko w obrazie systemu albo ostatni odczyt się nie udał;
    /// sprawdzanie przerwane przed końcem okna.
    /// </summary>
    private enum Verification
    {
        Gone,
        StillPresent,
        ImageOnly,
        Unknown,
        Interrupted,
    }
}
