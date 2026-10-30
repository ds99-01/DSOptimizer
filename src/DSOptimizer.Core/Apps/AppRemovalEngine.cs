using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Apps;

/// <summary>
/// Seria usuwania aplikacji: strażnik wierszy, punkt przywracania, potem każda pozycja osobno (<see cref="AppRemovalStep"/>:
/// obecność → journal → próba → weryfikacja).
/// Sukces = aplikacja zniknęła w niezależnym, świeżym odczycie; nigdy „usunięte” bez sprawdzenia. Bez cofania.
/// AppX: zakres wybiera porównanie SID procesu z SID zalogowanego (Ruling R8).
/// </summary>
public sealed class AppRemovalEngine
{
    /// <summary>Akcja zdarzeń journala usunięcia (<c>remove-app</c>: <c>Started</c>, potem nazwa wyniku).</summary>
    public const string JournalAction = "remove-app";

    /// <summary>Opis punktu przywracania serii usuwania (w bieżącym języku; widać go w Przywracaniu systemu).</summary>
    public static string RestorePointDescription => L.T("DSOptimizer – przed usunięciem aplikacji");

    private const string Component = "Apps";

    private readonly IRestorePointService _restorePoints;
    private readonly IUserContext _user;
    private readonly string _processSid;
    private readonly ILog _log;
    private readonly IClock _clock;
    private readonly AppRemovalStep _step;

    public AppRemovalEngine(
        IInstalledAppsReader reader,
        IAppRemover remover,
        IJournalStore journal,
        IRestorePointService restorePoints,
        IUserContext user,
        string processSid,
        ILog log,
        IClock clock,
        Action<TimeSpan>? sleep = null)
    {
        _restorePoints = restorePoints ?? throw new ArgumentNullException(nameof(restorePoints));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _processSid = processSid ?? throw new ArgumentNullException(nameof(processSid));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _step = new AppRemovalStep(
            reader ?? throw new ArgumentNullException(nameof(reader)),
            remover ?? throw new ArgumentNullException(nameof(remover)),
            journal ?? throw new ArgumentNullException(nameof(journal)),
            user,
            log,
            clock,
            sleep ?? Thread.Sleep);
    }

    /// <summary>
    /// Usuwa wiersze po kolei; błąd jednego nie zatrzymuje reszty. Wiersz inny niż <see cref="AppRemovability.Removable"/> (chroniony,
    /// innego konta, niepewny użytkownik, bez bezpiecznego polecenia) daje <see cref="ArgumentException"/> zanim cokolwiek się wykona.
    /// </summary>
    public BatchResult Remove(IReadOnlyList<AppRow> rows, AppRemovalOptions options, CancellationToken ct)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        foreach (var row in rows)
        {
            Guard(row);
        }

        var hasProgram = rows.Any(row => row.App.Kind == AppKind.Program);
        // Punkt wymagany: zawsze przy programie (spec 6.3), przy samych AppX tylko na żądanie CLI `required` (R20).
        var required = hasProgram || options.RestorePointRequired;
        var withRestorePoint = required || options.RestorePointSetting;
        var offset = withRestorePoint ? 1 : 0;
        var count = rows.Count + offset;
        var warnings = new List<string>();
        RestorePointResult? restorePoint = null;
        if (withRestorePoint)
        {
            Report(options.Steps, new BatchStepEvent(0, count, BatchStepKind.RestorePoint, null, BatchStepPhase.Started, _clock.UtcNow));
            restorePoint = CreateRestorePoint();
            Report(options.Steps, new BatchStepEvent(0, count, BatchStepKind.RestorePoint, null, BatchStepPhase.Finished, _clock.UtcNow, RestorePoint: restorePoint));
            if (restorePoint.Status != RestorePointStatus.Created)
            {
                var text = L.F("Punkt przywracania nie powstał: {0}", restorePoint.Message);
                _log.Warn(Component, text);
                if (required && !AskToContinue(options, restorePoint))
                {
                    return new BatchResult
                    {
                        RestorePoint = restorePoint,
                        Aborted = true,
                        AbortReason = text + " " + AppsText.AbortedWithoutRestorePoint,
                        Results = rows.Select(row => Skipped(row, L.T("Seria przerwana przed rozpoczęciem."))).ToList(),
                    };
                }

                warnings.Add(text);
            }
        }

        var scope = string.Equals(_processSid, _user.Sid, StringComparison.OrdinalIgnoreCase)
            ? StoreRemovalScope.CurrentUser
            : StoreRemovalScope.AllUsers;
        var results = new List<TweakResult>();
        var stopped = false;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (ct.IsCancellationRequested)
            {
                results.Add(Skipped(row, L.T("Anulowano przed rozpoczęciem.")));
                continue;
            }

            if (options.Stop is { IsRequested: true })
            {
                stopped = true;
                results.Add(Skipped(row, TweakEngine.StoppedMessage));
                continue;
            }

            var index = i + offset;
            Report(options.Steps, new BatchStepEvent(index, count, BatchStepKind.Item, row.App.Id, BatchStepPhase.Started, _clock.UtcNow));
            TweakResult result;
            try
            {
                result = _step.Run(row, scope, options.Stop, ct);
            }
            catch (Exception ex)
            {
                _log.Fail(Component, L.F("Nieoczekiwany błąd usuwania {0}.", row.App.Id), ex);
                result = new TweakResult { TweakId = row.App.Id, Outcome = TweakOutcome.Failed, Message = L.F("Nieoczekiwany błąd: {0}", ex.Message) };
            }

            results.Add(result);
            Report(options.Steps, new BatchStepEvent(index, count, BatchStepKind.Item, row.App.Id, BatchStepPhase.Finished, _clock.UtcNow, result));
        }

        return new BatchResult { Results = results, Warnings = warnings, RestorePoint = restorePoint, Stopped = stopped };
    }

    private static void Guard(AppRow row)
    {
        if (row is null)
        {
            throw new ArgumentException("Pusty wiersz w serii usuwania.", nameof(row));
        }

        // Ta sama reguła co pole wyboru i strażnik CLI (AppRow.CanRemove): usuwalny, spoza grupy chronionej (obrona w głąb przed
        // wierszem przerobionym przez `with`, Review Focus 1) i z danymi do usunięcia.
        if (!row.CanRemove)
        {
            throw new ArgumentException($"Aplikacji {row.App.Id} nie wolno usunąć ({row.Removability}, grupa {row.Group}).", nameof(row));
        }
    }

    private bool AskToContinue(AppRemovalOptions options, RestorePointResult restorePoint)
    {
        try
        {
            return options.ContinueWithoutRestorePoint(restorePoint);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.T("Pytanie o kontynuację bez punktu przywracania rzuciło wyjątek – przyjęto „Nie”."), ex);
            return false;
        }
    }

    private RestorePointResult CreateRestorePoint()
    {
        try
        {
            return _restorePoints.Create(RestorePointDescription);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.T("Serwis punktu przywracania rzucił wyjątek."), ex);
            return new RestorePointResult(RestorePointStatus.Failed, L.F("Wyjątek serwisu punktu przywracania: {0}", ex.Message));
        }
    }

    /// <summary>Odbiornik zdarzeń nie może zatrzymać serii: wyjątek ląduje w logu.</summary>
    private void Report(IProgress<BatchStepEvent>? steps, BatchStepEvent step)
    {
        if (steps is null)
        {
            return;
        }

        try
        {
            steps.Report(step);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.F("Odbiornik postępu usuwania rzucił wyjątek ({0} {1}).", step.Kind, step.Phase), ex);
        }
    }

    private static TweakResult Skipped(AppRow row, string message)
        => new() { TweakId = row.App.Id, Outcome = TweakOutcome.Skipped, Message = message };
}
