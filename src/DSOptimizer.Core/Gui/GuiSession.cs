using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

/// <summary>
/// Cała logika okna bez WPF: kategorie, detekcja, bramka potwierdzenia i serializacja operacji na silniku.
/// Jedna blokada: Run/RunBatch/RunAction biorą ją przez TryEnter (zajęta = odmowa lokalna), Detect/DetectAll czekają na nią.
/// Po operacji Run/RunBatch wykrywają ponownie wszystkie tweaki, nadal pod tą samą blokadą (bez ponownego <c>lock</c>).
/// Wyjątek detekcji odświeżającej nie kasuje raportu: pozycja dostaje Unknown i linię Error.
/// </summary>
public sealed class GuiSession
{
    private const string BatchTweakId = "seria";
    private const string Component = "Gui";

    private readonly TweakEngine _engine;
    private readonly EngineServices _services;
    private readonly Dictionary<string, TweakDefinition> _byId = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly List<TweakDefinition> _tweaks;
    private volatile bool _createRestorePoint = true;

    public GuiSession(LoadedCatalog catalog, EngineServices services)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        _services = services ?? throw new ArgumentNullException(nameof(services));
        _engine = new TweakEngine(services, catalog.Hash);
        foreach (var definition in catalog.Definitions)
        {
            _byId[definition.Id] = definition;
        }

        _tweaks = catalog.Definitions.Where(item => item.Kind == TweakKind.Tweak).ToList();
        Categories = BuildCategories(catalog.Definitions);
    }

    public IReadOnlyList<GuiCategory> Categories { get; }

    /// <summary>Czy „Zastosuj” (pojedyncze i seria) tworzy punkt przywracania. Ustawiane z wątku UI, czytane w serii.</summary>
    public bool CreateRestorePoint
    {
        get => _createRestorePoint;
        set => _createRestorePoint = value;
    }

    public DetectionState Detect(string id, CancellationToken ct = default)
    {
        var tweak = Find(id);
        if (tweak.Kind != TweakKind.Tweak)
        {
            throw new ArgumentException("Detekcja dotyczy tylko pozycji kind=tweak.", nameof(id));
        }

        lock (_gate)
        {
            return _engine.Detect(tweak, ct);
        }
    }

    public IReadOnlyDictionary<string, DetectionState> DetectAll(
        IProgress<KeyValuePair<string, DetectionState>>? progress = null,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            return DetectAllCore(progress, ct);
        }
    }

    /// <summary>
    /// Pozycje czekające na restart: zdarzenia journala późniejsze niż ostatni start systemu (albo start sesji, gdy nieznany).
    /// Błąd odczytu nie rzuca: puste Ids i ostrzeżenie.
    /// </summary>
    public RebootStatus PendingReboot(DateTimeOffset? lastBoot, DateTimeOffset sessionStart)
    {
        try
        {
            Journal.JournalLoadResult loaded;
            lock (_gate)
            {
                loaded = _services.Journal.Load();
            }

            var kinds = RebootPending.ComputeKinds(loaded.Document, _byId, lastBoot ?? sessionStart);
            var ids = kinds.Keys.ToList();
            var undone = ids.Where(id => kinds[id] == RebootPendingKind.Undone).ToList();
            // Usunięcia i instalacje aplikacji: tylko tytuły do banera (bez id – nie mają wiersza w katalogu ani „Cofnij”).
            var since = lastBoot ?? sessionStart;
            var titles = ids.Select(id => _byId[id].Title)
                .Concat(RebootPending.ComputeAppRemovals(loaded.Document, since)
                    .Concat(RebootPending.ComputeAppInstalls(loaded.Document, since))
                    .Select(entry => string.IsNullOrWhiteSpace(entry.Title) ? entry.TweakId : entry.Title!))
                .ToList();
            return new RebootStatus(ids, titles, undone, loaded.Warning);
        }
        catch (Exception ex)
        {
            return new RebootStatus(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                L.F("Nie udało się sprawdzić, czy czeka restart: {0}", ex.Message));
        }
    }

    /// <summary>
    /// Historia zmian z journala (odczyt pod blokadą sesji, jak <see cref="PendingReboot"/>). <c>since</c> = ostatni start systemu
    /// albo start sesji, gdy nieznany. Błąd odczytu nie rzuca: puste wpisy i ostrzeżenie. Nazwy kont tłumaczy <paramref name="names"/>
    /// poza blokadą (może blokować na LSA).
    /// </summary>
    public HistorySnapshot LoadHistory(DateTimeOffset? lastBoot, DateTimeOffset sessionStart, IAccountNames names)
    {
        if (names is null)
        {
            throw new ArgumentNullException(nameof(names));
        }

        try
        {
            Journal.JournalLoadResult loaded;
            lock (_gate)
            {
                loaded = _services.Journal.Load();
            }

            var entries = ChangeHistory.Build(loaded.Document, Categories, lastBoot ?? sessionStart, _services.User.Sid, names.NameOf);
            return new HistorySnapshot(entries, loaded.Warning);
        }
        catch (Exception ex)
        {
            return new HistorySnapshot(Array.Empty<HistoryEntry>(), HistoryText.LoadFailed(ex.Message));
        }
    }

    public GuiOperationReport Run(string id, GuiOperation operation, bool confirmed, CancellationToken ct = default)
    {
        var tweak = Find(id);
        EnsureOperationMatches(tweak, operation);

        if (GuiRules.RequiresConfirmation(tweak) && !confirmed)
        {
            return Refusal(tweak, operation, L.T("Operacja wymaga potwierdzenia – nic nie zmieniono."));
        }

        if (!Monitor.TryEnter(_gate))
        {
            return Refusal(tweak, operation, L.T("Trwa inna operacja – nic nie zmieniono."));
        }

        try
        {
            return RunLocked(tweak, operation, null, ct);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    /// <summary>
    /// Długa naprawa z oknem postępu (<see cref="GuiRules.UsesProgressWindow"/>; inna pozycja = <see cref="ArgumentException"/>):
    /// ta sama blokada, odmowa i bramka potwierdzenia co <see cref="Run"/>. <paramref name="progress"/> trafia do handlera
    /// (raporty z wątku silnika), <paramref name="stop"/> to „Zatrzymaj” – handler sprawdza go tylko między krokami.
    /// </summary>
    public GuiOperationReport RunAction(string id, bool confirmed, IProgress<ActionProgress>? progress, CancellationToken stop)
    {
        var tweak = Find(id);
        if (!GuiRules.UsesProgressWindow(tweak))
        {
            throw new ArgumentException($"Pozycja {tweak.Id} nie ma okna postępu (długiej naprawy).", nameof(id));
        }

        if (GuiRules.RequiresConfirmation(tweak) && !confirmed)
        {
            return Refusal(tweak, GuiOperation.Execute, L.T("Operacja wymaga potwierdzenia – nic nie zmieniono."));
        }

        if (!Monitor.TryEnter(_gate))
        {
            return Refusal(tweak, GuiOperation.Execute, L.T("Trwa inna operacja – nic nie zmieniono."));
        }

        try
        {
            return RunLocked(tweak, GuiOperation.Execute, progress, stop);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    /// <summary>
    /// Model okna potwierdzenia serii Apply/Undo z podglądem zmian; null, gdy okno niepotrzebne (jedna bezpieczna pozycja).
    /// Wołane poza wątkiem UI (Preview czyta rejestr i usługi); bez blokady sesji.
    /// </summary>
    public SeriesConfirmation? ConfirmationForSeries(IReadOnlyList<string> ids, GuiOperation operation, string? userName)
    {
        var tweaks = ResolveBatch(ids, operation);
        return GuiRules.SeriesRequiresConfirmation(tweaks)
            ? SeriesConfirmationBuilder.Build(operation, tweaks, true, _createRestorePoint, userName, item => _engine.Preview(item), _services.Log)
            : null;
    }

    /// <summary>
    /// Model okna potwierdzenia jednej pozycji (Apply/Undo tweaka albo Execute akcji/panelu); null, gdy pozycja go nie wymaga.
    /// </summary>
    public SeriesConfirmation? ConfirmationForItem(string id, GuiOperation operation, string? userName)
    {
        var tweak = Find(id);
        EnsureOperationMatches(tweak, operation);
        return GuiRules.RequiresConfirmation(tweak)
            ? SeriesConfirmationBuilder.Build(operation, new[] { tweak }, false, _createRestorePoint, userName, item => _engine.Preview(item), _services.Log)
            : null;
    }

    /// <summary>
    /// Seria Apply (jedno ApplyBatch, jeden punkt przywracania) albo Undo (UndoBatch, bez punktu). Ta sama blokada co Run.
    /// <paramref name="steps"/> dostaje zdarzenia kroków, <paramref name="stop"/> pomija pozostałe pozycje (tylko między krokami).
    /// </summary>
    public GuiOperationReport RunBatch(
        IReadOnlyList<string> ids,
        GuiOperation operation,
        bool confirmed,
        IProgress<BatchStepEvent>? steps = null,
        BatchStop? stop = null,
        CancellationToken ct = default)
    {
        var tweaks = ResolveBatch(ids, operation);
        if (GuiRules.SeriesRequiresConfirmation(tweaks) && !confirmed)
        {
            return BatchRefusal(operation, L.T("Operacja wymaga potwierdzenia – nic nie zmieniono."));
        }

        if (!Monitor.TryEnter(_gate))
        {
            return BatchRefusal(operation, L.T("Trwa inna operacja – nic nie zmieniono."));
        }

        try
        {
            return RunBatchLocked(tweaks, operation, steps, stop, ct);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    /// <summary>
    /// Praca pod tą samą blokadą co <see cref="Run"/> i <see cref="RunBatch"/> (np. seria usuwania aplikacji): zajęta blokada =
    /// false bez wywołania <paramref name="work"/>, więc seria tweaków i usuwanie nigdy nie biegną naraz. Wyjątek z pracy
    /// leci dalej, blokada jest zwalniana.
    /// </summary>
    public bool TryRunExclusive<T>(Func<T> work, out T result)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        if (!Monitor.TryEnter(_gate))
        {
            result = default!;
            return false;
        }

        try
        {
            result = work();
            return true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private GuiOperationReport RunBatchLocked(
        List<TweakDefinition> tweaks,
        GuiOperation operation,
        IProgress<BatchStepEvent>? steps,
        BatchStop? stop,
        CancellationToken ct)
    {
        var lines = new List<GuiLogLine>();
        var batch = operation == GuiOperation.Apply
            ? ApplyBatchLines(tweaks, lines, steps, stop, ct)
            : UndoBatchSafely(tweaks, steps, stop, ct);

        var results = batch.Results;
        var byId = tweaks.ToDictionary(tweak => tweak.Id, StringComparer.Ordinal);
        foreach (var result in results)
        {
            AddResultLines(lines, byId[result.TweakId], result);
        }

        var succeeded = results.Where(result => result.IsSuccess).Select(result => result.TweakId).ToList();
        lines.Add(Line(GuiLogLevel.Info, L.F("Seria: {0} z {1} zakończonych powodzeniem.", succeeded.Count, tweaks.Count)));
        var refreshed = RefreshAfterOperation(lines);
        return new GuiOperationReport
        {
            TweakId = BatchTweakId,
            Operation = operation,
            Result = null,
            Lines = lines,
            Refreshed = refreshed,
            RequiresReboot = results.Any(result => result.RequiresReboot),
            SucceededIds = succeeded,
            Batch = batch,
        };
    }

    private BatchResult ApplyBatchLines(
        List<TweakDefinition> tweaks,
        List<GuiLogLine> lines,
        IProgress<BatchStepEvent>? steps,
        BatchStop? stop,
        CancellationToken ct)
    {
        try
        {
            return ApplyViaEngine(tweaks, L.F("DSOptimizer – przed zmianami ({0})", tweaks.Count), lines, steps, stop, ct);
        }
        catch (Exception ex)
        {
            _services.Log.Fail(Component, L.F("Nieoczekiwany błąd serii Zastosuj ({0}).", tweaks.Count), ex);
            return new BatchResult { Results = tweaks.Select(tweak => Failure(tweak, ex)).ToList() };
        }
    }

    private BatchResult UndoBatchSafely(
        List<TweakDefinition> tweaks,
        IProgress<BatchStepEvent>? steps,
        BatchStop? stop,
        CancellationToken ct)
    {
        try
        {
            return _engine.UndoBatch(tweaks, steps, stop, ct);
        }
        catch (Exception ex)
        {
            _services.Log.Fail(Component, L.F("Nieoczekiwany błąd serii Cofnij ({0}).", tweaks.Count), ex);
            return new BatchResult { Results = tweaks.Select(tweak => Failure(tweak, ex)).ToList() };
        }
    }

    /// <summary>Wspólna ścieżka Apply (pojedynczy i seria): jedno ApplyBatch, ostrzeżenia i przerwanie jako linie.</summary>
    private BatchResult ApplyViaEngine(
        IReadOnlyList<TweakDefinition> tweaks,
        string restorePointDescription,
        List<GuiLogLine> lines,
        IProgress<BatchStepEvent>? steps,
        BatchStop? stop,
        CancellationToken ct)
    {
        var policy = _createRestorePoint ? RestorePointPolicy.IfAvailable : RestorePointPolicy.Skip;
        var batch = _engine.ApplyBatch(
            tweaks,
            new BatchOptions
            {
                RestorePointPolicy = policy,
                RestorePointDescription = restorePointDescription,
                Steps = steps,
                Stop = stop,
            },
            null,
            ct);
        foreach (var warning in batch.Warnings)
        {
            lines.Add(Line(GuiLogLevel.Warning, warning));
        }

        if (batch.Aborted)
        {
            lines.Add(Line(GuiLogLevel.Error, batch.AbortReason ?? L.T("Seria przerwana.")));
        }

        return batch;
    }

    private static void EnsureOperationMatches(TweakDefinition tweak, GuiOperation operation)
    {
        var matches = operation == GuiOperation.Execute ? tweak.Kind != TweakKind.Tweak : tweak.Kind == TweakKind.Tweak;
        if (!matches)
        {
            throw new ArgumentException($"Operacja {operation} nie pasuje do pozycji {tweak.Id} ({tweak.Kind}).", nameof(operation));
        }
    }

    private static TweakResult Failure(TweakDefinition tweak, Exception ex)
        => new() { TweakId = tweak.Id, Outcome = TweakOutcome.Failed, Message = L.F("Nieoczekiwany błąd: {0}", ex.Message) };

    private List<TweakDefinition> ResolveBatch(IReadOnlyList<string> ids, GuiOperation operation)
    {
        if (operation == GuiOperation.Execute)
        {
            throw new ArgumentException("Seria obsługuje tylko Apply i Undo.", nameof(operation));
        }

        if (ids is null || ids.Count == 0)
        {
            throw new ArgumentException("Seria musi zawierać co najmniej jedną pozycję.", nameof(ids));
        }

        var tweaks = ids.Distinct(StringComparer.Ordinal).Select(Find).ToList();
        var other = tweaks.FirstOrDefault(tweak => tweak.Kind != TweakKind.Tweak);
        if (other is not null)
        {
            throw new ArgumentException($"Seria dotyczy tylko pozycji kind=tweak ({other.Id}).", nameof(ids));
        }

        return tweaks;
    }

    private GuiOperationReport BatchRefusal(GuiOperation operation, string text) => new()
    {
        TweakId = BatchTweakId,
        Operation = operation,
        Result = null,
        Lines = new[] { Line(GuiLogLevel.Warning, text) },
    };

    private GuiOperationReport RunLocked(TweakDefinition tweak, GuiOperation operation, IProgress<ActionProgress>? progress, CancellationToken ct)
    {
        var lines = new List<GuiLogLine>();
        TweakResult result;
        try
        {
            result = CallEngine(tweak, operation, lines, progress, ct);
        }
        catch (Exception ex)
        {
            _services.Log.Fail(Component, L.F("Nieoczekiwany błąd operacji {0} {1}.", operation, tweak.Id), ex);
            result = Failure(tweak, ex);
        }

        AddResultLines(lines, tweak, result);
        var refreshed = RefreshAfterOperation(lines);
        return new GuiOperationReport
        {
            TweakId = tweak.Id,
            Operation = operation,
            Result = result,
            Lines = lines,
            Refreshed = refreshed,
            RequiresReboot = result.RequiresReboot,
        };
    }

    private TweakResult CallEngine(
        TweakDefinition tweak,
        GuiOperation operation,
        List<GuiLogLine> lines,
        IProgress<ActionProgress>? progress,
        CancellationToken ct)
    {
        switch (operation)
        {
            case GuiOperation.Undo:
                return _engine.Undo(tweak, null, ct);
            case GuiOperation.Execute:
                return _engine.Execute(tweak, null, progress, ct);
            default:
                var results = ApplyViaEngine(new[] { tweak }, L.F("DSOptimizer – przed {0}", tweak.Id), lines, null, null, ct).Results;
                return results.Count > 0
                    ? results[0]
                    : new TweakResult { TweakId = tweak.Id, Outcome = TweakOutcome.Failed, Message = L.T("Silnik nie zwrócił wyniku.") };
        }
    }

    private void AddResultLines(List<GuiLogLine> lines, TweakDefinition tweak, TweakResult result)
    {
        var level = result.IsSuccess
            ? GuiLogLevel.Info
            : result.Outcome is TweakOutcome.Refused or TweakOutcome.Skipped or TweakOutcome.NotSupported
                ? GuiLogLevel.Warning
                : GuiLogLevel.Error;
        var text = $"{tweak.Title}: {GuiText.OutcomeName(result.Outcome)}";
        if (result.Outcome == TweakOutcome.Skipped && string.Equals(result.Message, TweakEngine.StoppedMessage, StringComparison.Ordinal))
        {
            // „Pominięto” już jest w nazwie wyniku – bez powtórzenia „Pominięte” z komunikatu silnika.
            text = L.F("{0} – zatrzymano.", text);
        }
        else if (!string.IsNullOrWhiteSpace(result.Message))
        {
            var message = GuiText.OneLine(result.Message!, GuiText.MaxResultMessageLength);
            if (string.Equals(message, result.Message, StringComparison.Ordinal))
            {
                text += $" – {message}";
            }
            else
            {
                // Dziennik w oknie: jedna czytelna linia; pełny tekst (wielolinijkowy albo długi) tylko w pliku logu.
                text = L.F("{0} – {1} (pełny wynik w pliku logu)", text, message);
                _services.Log.Info(Component, L.F("{0}: pełny wynik:{1}{2}", tweak.Id, Environment.NewLine, result.Message));
            }
        }

        lines.Add(Line(level, text));
        foreach (var warning in result.Warnings)
        {
            lines.Add(Line(GuiLogLevel.Warning, warning));
        }

        foreach (var item in result.NotRestored)
        {
            lines.Add(Line(GuiLogLevel.Error, L.F("Nie przywrócono: {0}", item)));
        }

        if (result.RequiresReboot)
        {
            lines.Add(Line(GuiLogLevel.Warning, L.T("Wymagany restart, żeby zmiana zadziałała.")));
        }
    }

    private GuiOperationReport Refusal(TweakDefinition tweak, GuiOperation operation, string text) => new()
    {
        TweakId = tweak.Id,
        Operation = operation,
        Result = null,
        Lines = new[] { Line(GuiLogLevel.Warning, text) },
    };

    private GuiLogLine Line(GuiLogLevel level, string text) => new(_services.Clock.UtcNow, level, text);

    private Dictionary<string, DetectionState> DetectAllCore(
        IProgress<KeyValuePair<string, DetectionState>>? progress,
        CancellationToken ct)
    {
        var states = new Dictionary<string, DetectionState>(StringComparer.Ordinal);
        foreach (var tweak in _tweaks)
        {
            ct.ThrowIfCancellationRequested();
            var state = _engine.Detect(tweak, ct);
            states[tweak.Id] = state;
            progress?.Report(new KeyValuePair<string, DetectionState>(tweak.Id, state));
        }

        return states;
    }

    /// <summary>
    /// Detekcja wszystkich tweaków po operacji (system już zmieniony, więc raport musi przetrwać): wyjątek detekcji
    /// jednej pozycji = Unknown („Nieznany (błąd detekcji)”) + linia Error w raporcie + Fail w logu; reszta biegnie dalej.
    /// </summary>
    private Dictionary<string, DetectionState> RefreshAfterOperation(List<GuiLogLine> lines)
    {
        var states = new Dictionary<string, DetectionState>(StringComparer.Ordinal);
        foreach (var tweak in _tweaks)
        {
            try
            {
                states[tweak.Id] = _engine.Detect(tweak, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _services.Log.Fail(Component, L.F("Detekcja {0} po operacji przerwana nieoczekiwanym błędem.", tweak.Id), ex);
                states[tweak.Id] = DetectionState.Unknown;
                lines.Add(Line(GuiLogLevel.Error, L.F("{0}: nie udało się sprawdzić stanu po operacji – {1}", tweak.Title, ex.Message)));
            }
        }

        return states;
    }

    private TweakDefinition Find(string id)
    {
        if (id is null || !_byId.TryGetValue(id, out var tweak))
        {
            throw new ArgumentException($"Nieznane id pozycji: {id}.", nameof(id));
        }

        return tweak;
    }

    private static List<GuiCategory> BuildCategories(IReadOnlyList<TweakDefinition> definitions)
    {
        var ids = definitions.Select(item => item.Category).Distinct(StringComparer.Ordinal).ToList();
        var ordered = GuiText.CategoryOrder.Where(ids.Contains)
            .Concat(ids.Where(item => !GuiText.CategoryOrder.Contains(item)).OrderBy(item => item, StringComparer.Ordinal))
            .ToList();
        return ordered
            .Select(category => new GuiCategory(
                category,
                GuiText.CategoryName(category),
                definitions.Where(item => item.Category == category).ToList()))
            .ToList();
    }
}