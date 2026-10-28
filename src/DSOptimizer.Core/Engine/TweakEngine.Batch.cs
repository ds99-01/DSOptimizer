using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Engine;

public sealed partial class TweakEngine
{
    /// <summary>Message wyniku Skipped dla pozycji pominiętej po „Zatrzymaj”.</summary>
    public static string StoppedMessage => L.T("Pominięte – zatrzymano.");

    /// <summary>Seria: restore point wg polityki, potem każdy tweak atomowo; błąd jednego nie zatrzymuje reszty.</summary>
    public BatchResult ApplyBatch(
        IReadOnlyList<TweakDefinition> tweaks,
        BatchOptions options,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        if (tweaks is null)
        {
            throw new ArgumentNullException(nameof(tweaks));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var warnings = new List<string>();
        RestorePointResult? restorePoint = null;
        var steps = options.Steps;
        var stop = options.Stop;
        var withRestorePoint = options.RestorePointPolicy != RestorePointPolicy.Skip;
        var offset = withRestorePoint ? 1 : 0;
        var count = tweaks.Count + offset;
        if (withRestorePoint)
        {
            ReportStep(steps, new BatchStepEvent(0, count, BatchStepKind.RestorePoint, null, BatchStepPhase.Started, _s.Clock.UtcNow));
            restorePoint = CreateRestorePoint(options.RestorePointDescription);
            ReportStep(steps, new BatchStepEvent(0, count, BatchStepKind.RestorePoint, null, BatchStepPhase.Finished, _s.Clock.UtcNow, RestorePoint: restorePoint));
            if (restorePoint.Status != RestorePointStatus.Created)
            {
                var text = L.F("Punkt przywracania nie powstał ({0}): {1}", restorePoint.Status, restorePoint.Message);
                _s.Log.Warn(Component, text);
                if (options.RestorePointPolicy == RestorePointPolicy.Required)
                {
                    return new BatchResult
                    {
                        RestorePoint = restorePoint,
                        Aborted = true,
                        AbortReason = text,
                        Results = tweaks.Select(tweak => Skipped(tweak, L.T("Seria przerwana przed rozpoczęciem."))).ToList(),
                    };
                }

                warnings.Add(text);
            }
        }

        var results = RunItems(
            tweaks,
            offset,
            count,
            steps,
            stop,
            out var stopped,
            tweak => tweak.Kind == TweakKind.Tweak
                ? Apply(tweak, output, cancellationToken)
                : Execute(tweak, output, cancellationToken),
            undo: false,
            cancellationToken);

        return new BatchResult { Results = results, Warnings = warnings, RestorePoint = restorePoint, Stopped = stopped };
    }

    /// <summary>Seria cofania: kolejno Undo bez punktu przywracania; wyjątek jednej pozycji = Failed, reszta biegnie dalej.</summary>
    public BatchResult UndoBatch(
        IReadOnlyList<TweakDefinition> tweaks,
        IProgress<BatchStepEvent>? steps = null,
        BatchStop? stop = null,
        CancellationToken cancellationToken = default)
    {
        if (tweaks is null)
        {
            throw new ArgumentNullException(nameof(tweaks));
        }

        var results = RunItems(
            tweaks,
            0,
            tweaks.Count,
            steps,
            stop,
            out var stopped,
            tweak => Undo(tweak, null, cancellationToken),
            undo: true,
            cancellationToken);

        return new BatchResult { Results = results, Stopped = stopped };
    }

    /// <summary>Pozycje serii po kolei; Stop sprawdzany wyłącznie przed pozycją, nigdy w jej trakcie.</summary>
    private List<TweakResult> RunItems(
        IReadOnlyList<TweakDefinition> tweaks,
        int offset,
        int count,
        IProgress<BatchStepEvent>? steps,
        BatchStop? stop,
        out bool stopped,
        Func<TweakDefinition, TweakResult> run,
        bool undo,
        CancellationToken cancellationToken)
    {
        stopped = false;
        var results = new List<TweakResult>();
        for (var i = 0; i < tweaks.Count; i++)
        {
            var tweak = tweaks[i];
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(Skipped(tweak, L.T("Anulowano przed rozpoczęciem.")));
                continue;
            }

            if (stop is { IsRequested: true })
            {
                stopped = true;
                results.Add(Skipped(tweak, StoppedMessage));
                continue;
            }

            var index = i + offset;
            ReportStep(steps, new BatchStepEvent(index, count, BatchStepKind.Item, tweak.Id, BatchStepPhase.Started, _s.Clock.UtcNow));
            TweakResult result;
            try
            {
                result = run(tweak);
            }
            catch (Exception ex)
            {
                var text = undo
                    ? L.F("Nieoczekiwany błąd Cofnij w serii przy {0}.", tweak.Id)
                    : L.F("Nieoczekiwany błąd w serii przy {0}.", tweak.Id);
                _s.Log.Fail(Component, text, ex);
                result = new TweakResult { TweakId = tweak.Id, Outcome = TweakOutcome.Failed, Message = L.F("Nieoczekiwany błąd: {0}", ex.Message) };
            }

            results.Add(result);
            ReportStep(steps, new BatchStepEvent(index, count, BatchStepKind.Item, tweak.Id, BatchStepPhase.Finished, _s.Clock.UtcNow, result));
        }

        return results;
    }

    /// <summary>Odbiornik zdarzeń nie może zatrzymać serii: wyjątek ląduje w logu.</summary>
    private void ReportStep(IProgress<BatchStepEvent>? steps, BatchStepEvent step)
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
            _s.Log.Fail(Component, L.F("Odbiornik postępu serii rzucił wyjątek ({0} {1}).", step.Kind, step.Phase), ex);
        }
    }

    /// <summary>Serwis punktu przywracania nie powinien rzucać, ale wyjątek traktujemy jak porażkę (zgodnie z polityką).</summary>
    private RestorePointResult CreateRestorePoint(string description)
    {
        try
        {
            return _s.RestorePoints.Create(description);
        }
        catch (Exception ex)
        {
            _s.Log.Fail(Component, L.T("Serwis punktu przywracania rzucił wyjątek."), ex);
            return new RestorePointResult(RestorePointStatus.Failed, L.F("Wyjątek serwisu punktu przywracania: {0}", ex.Message));
        }
    }

    private static TweakResult Skipped(TweakDefinition tweak, string message)
        => new() { TweakId = tweak.Id, Outcome = TweakOutcome.Skipped, Message = message };
}
