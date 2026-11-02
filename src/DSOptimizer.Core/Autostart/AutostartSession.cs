using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Autostart;

/// <summary>Wynik odczytu strony „Autostart”: lista (null przy błędzie), chwila odczytu i tekst błędu (null = odczyt udany).</summary>
public sealed record AutostartPageLoad(AutostartList? List, DateTimeOffset ReadAt, string? Error);

/// <summary>
/// Logika strony „Autostart” bez WPF: odczyt z limitem czasu, treść okna potwierdzenia „Wyłącz (N)”/„Włącz (M)” i „Cofnij” z Historii
/// zmian oraz same serie. Zmiany biegną pod tą samą blokadą co seria tweaków (<see cref="GuiSession.TryRunExclusive{T}"/>): zajęta =
/// odmowa, nic nie rusza. Kontekst konta (gałęzie HKU, dowód zapisu od zera) jest liczony od nowa przy każdym odczycie i każdej zmianie.
/// Nic nie zaznacza się samo; restart nie jest potrzebny (zmiana działa od następnego logowania).
/// </summary>
public sealed class AutostartSession
{
    private const string ReportId = "autostart";

    private const string InstanceApi = "Metody sesji strony wołane przez widok na instancji, jak w AppsSession.";

    private readonly GuiSession _session;
    private readonly IAutostartReader _reader;
    private readonly AutostartEngine _engine;
    private readonly AutostartKnownCatalog _catalog;
    private readonly IUserContext _user;
    private readonly string _processSid;
    private readonly IRegistry _registry;
    private readonly ILog _log;
    private readonly IClock _clock;
    private readonly TimeSpan _readTimeout;

    public AutostartSession(
        GuiSession session,
        IAutostartReader reader,
        AutostartEngine engine,
        AutostartKnownCatalog catalog,
        IUserContext user,
        string processSid,
        IRegistry registry,
        ILog log,
        IClock clock,
        TimeSpan? readTimeout = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _processSid = processSid ?? throw new ArgumentNullException(nameof(processSid));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _readTimeout = readTimeout ?? DefaultReadTimeout;
    }

    /// <summary>Limit odczytu strony; po nim strona pokazuje błąd odczytu, a poprzednia lista zostaje.</summary>
    public static TimeSpan DefaultReadTimeout => TimeSpan.FromSeconds(90);

    /// <summary>SID zalogowanego użytkownika (konto, którego autostart jest pokazywany).</summary>
    public string UserSid => _user.Sid;

    /// <summary>Odczyt i ocena listy poza wątkiem UI z limitem czasu; zawieszenie albo wyjątek = <see cref="AutostartText.ReadFailed"/> i linia w logu.</summary>
    public async Task<AutostartPageLoad> LoadAsync()
    {
        var reading = await TimedRead.RunAsync(
            () => AutostartList.Load(_reader, _catalog, Context()),
            _readTimeout,
            _log,
            AutostartText.ReadPartName,
            AutostartText.PageTitle).ConfigureAwait(false);
        var readAt = _clock.UtcNow;
        return reading.State == ReadingState.Done && reading.Value is { } list
            ? new AutostartPageLoad(list, readAt, null)
            : new AutostartPageLoad(null, readAt, AutostartText.ReadFailed);
    }

    /// <summary>
    /// Treść okna „Wyłącz w autostarcie”/„Włącz w autostarcie”: fakty (działa od następnego logowania, bez punktu przywracania, cofanie
    /// w Historii zmian) i pozycje z uwagą, a przy pozycji wszystkich kont – ostrzeżeniem. Wiersz bez <see cref="AutostartRow.CanToggle"/>
    /// albo w stanie innym niż przeciwny do <paramref name="target"/> = <see cref="ArgumentException"/>.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = InstanceApi)]
    public SeriesConfirmation ConfirmationFor(IReadOnlyList<AutostartRow> rows, AutostartState target)
    {
        RequireTarget(target);
        if (rows is null || rows.Count == 0)
        {
            throw new ArgumentException("Seria autostartu musi zawierać co najmniej jedną pozycję.", nameof(rows));
        }

        if (!AllMatch(rows, target))
        {
            throw new ArgumentException(AutostartText.SelectionRefused, nameof(rows));
        }

        var facts = new[]
        {
            new SeriesFact(AutostartText.TakesEffectFact, AutostartText.TakesEffectValue),
            new SeriesFact(L.T("Punkt przywracania"), AutostartText.RestorePointNotNeeded),
            new SeriesFact(L.T("Cofanie"), AutostartText.UndoInHistory),
        };
        var items = rows.Select(row => Item(row, target)).ToList();
        return new SeriesConfirmation(
            OperationOf(target),
            AutostartText.ConfirmTitle(target),
            ItemCount(rows.Count),
            facts,
            items,
            target == AutostartState.Disabled ? AutostartText.DisableButton(rows.Count) : AutostartText.EnableButton(rows.Count),
            false);
    }

    /// <summary>
    /// Seria wyłączania albo włączania pod blokadą sesji. Zajęta blokada, wiersz bez <see cref="AutostartRow.CanToggle"/> albo w złym stanie =
    /// odmowa: jedna linia Warning, <see cref="GuiOperationReport.Batch"/> = null, nic nie zostało uruchomione. Raport: linia na pozycję
    /// „&lt;nazwa&gt;: &lt;wynik&gt; – &lt;komunikat&gt;” i podsumowanie „Wyłączono N z M.”, bez restartu.
    /// </summary>
    public GuiOperationReport Set(IReadOnlyList<AutostartRow> rows, AutostartState target, AutostartRunOptions options)
    {
        RequireTarget(target);
        if (rows is null || rows.Count == 0)
        {
            throw new ArgumentException("Seria autostartu musi zawierać co najmniej jedną pozycję.", nameof(rows));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var operation = OperationOf(target);
        if (!AllMatch(rows, target))
        {
            return Refusal(ReportId, operation, AutostartText.SelectionRefused);
        }

        return _session.TryRunExclusive(() => SetLocked(rows, target, options), out var report)
            ? report
            : Refusal(ReportId, operation, L.T("Trwa inna operacja – nic nie zmieniono."));
    }

    /// <summary>Treść okna „Cofnij” wpisu autostartu z Historii zmian: jedna pozycja z tytułem wpisu, fakt „Działa od”, przycisk „Cofnij”.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = InstanceApi)]
    public SeriesConfirmation ConfirmationForUndo(HistoryEntry entry)
    {
        RequireAutostart(entry);
        var warnings = IsMachine(entry.TweakId) ? new[] { AutostartText.AllAccountsWarning } : Array.Empty<string>();
        var item = new SeriesItem(entry.TweakId, entry.Title, RiskLevel.Safe, entry.AccountText, Array.Empty<string>(), warnings, Array.Empty<SeriesFact>());
        return new SeriesConfirmation(
            GuiOperation.Undo,
            AutostartText.UndoConfirmTitle,
            ItemCount(1),
            new[] { new SeriesFact(AutostartText.TakesEffectFact, AutostartText.TakesEffectValue) },
            new[] { item },
            L.T("Cofnij"),
            false);
    }

    /// <summary>
    /// Cofnięcie wpisu z Historii zmian pod blokadą sesji: zdarzenia <c>Started</c>/<c>Finished</c> dla <see cref="HistoryEntry.TweakId"/>,
    /// wynik silnika (także odmowa) w <see cref="BatchResult"/> z jednym wynikiem i jedna linia raportu. Zajęta blokada = odmowa bez silnika.
    /// </summary>
    public GuiOperationReport Undo(HistoryEntry entry, AutostartRunOptions options)
    {
        RequireAutostart(entry);
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return _session.TryRunExclusive(() => UndoLocked(entry, options), out var report)
            ? report
            : Refusal(entry.TweakId, GuiOperation.Undo, L.T("Trwa inna operacja – nic nie zmieniono."));
    }

    private GuiOperationReport SetLocked(IReadOnlyList<AutostartRow> rows, AutostartState target, AutostartRunOptions options)
    {
        var batch = _engine.Set(rows, target, Context(), options, CancellationToken.None);
        var names = rows.GroupBy(row => row.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Entry.Name, StringComparer.Ordinal);
        var lines = new List<GuiLogLine>();
        foreach (var result in batch.Results)
        {
            var name = names.TryGetValue(result.TweakId, out var known) ? known : result.TweakId;
            AddResultLines(lines, name, AutostartText.ResultStatus(result.Outcome, target), result);
        }

        var changed = batch.Results.Count(result => AutostartText.CountsAsChanged(result.Outcome));
        lines.Add(Line(GuiLogLevel.Info, AutostartText.Summary(target, changed, rows.Count)));
        return new GuiOperationReport
        {
            TweakId = ReportId,
            Operation = OperationOf(target),
            Lines = lines,
            RequiresReboot = false,
            SucceededIds = batch.Results.Where(result => result.IsSuccess).Select(result => result.TweakId).ToList(),
            Batch = batch,
        };
    }

    private GuiOperationReport UndoLocked(HistoryEntry entry, AutostartRunOptions options)
    {
        Report(options.Steps, new BatchStepEvent(0, 1, BatchStepKind.Item, entry.TweakId, BatchStepPhase.Started, _clock.UtcNow));
        TweakResult result;
        try
        {
            result = _engine.Undo(entry.EntryId, Context());
        }
        catch (Exception ex)
        {
            _log.Fail(AutostartText.PageTitle, L.F("Nieoczekiwany błąd w serii przy {0}.", entry.TweakId), ex);
            result = new TweakResult { TweakId = entry.TweakId, Outcome = TweakOutcome.Failed, Message = L.F("Nieoczekiwany błąd: {0}", ex.Message) };
        }

        Report(options.Steps, new BatchStepEvent(0, 1, BatchStepKind.Item, entry.TweakId, BatchStepPhase.Finished, _clock.UtcNow, result));
        var lines = new List<GuiLogLine>();
        AddResultLines(lines, entry.Title, GuiText.OutcomeName(result.Outcome), result);
        return new GuiOperationReport
        {
            TweakId = entry.TweakId,
            Operation = GuiOperation.Undo,
            Result = result,
            Lines = lines,
            RequiresReboot = false,
            SucceededIds = result.IsSuccess ? new[] { entry.TweakId } : Array.Empty<string>(),
            Batch = new BatchResult { Results = new[] { result } },
        };
    }

    /// <summary>Linia wyniku pozycji i jej ostrzeżenia; cofnięcie niepełne bez komunikatu silnika dostaje własny tekst z rekordami.</summary>
    private void AddResultLines(List<GuiLogLine> lines, string name, string status, TweakResult result)
    {
        var level = result.IsSuccess ? GuiLogLevel.Info
            : result.Outcome is TweakOutcome.Refused or TweakOutcome.Skipped ? GuiLogLevel.Warning
            : GuiLogLevel.Error;
        var message = result.Outcome == TweakOutcome.UndoIncomplete && string.IsNullOrWhiteSpace(result.Message)
            ? AutostartText.UndoIncomplete(result.NotRestored)
            : result.Message;
        var text = $"{name}: {status}";
        if (!string.IsNullOrWhiteSpace(message))
        {
            text += " – " + GuiText.OneLine(message!, GuiText.MaxResultMessageLength);
        }

        lines.Add(Line(level, text));
        foreach (var warning in result.Warnings)
        {
            lines.Add(Line(GuiLogLevel.Warning, warning));
        }
    }

    private static SeriesItem Item(AutostartRow row, AutostartState target)
    {
        var scope = AutostartText.Scope(row.Entry.Scope);
        var summary = string.IsNullOrWhiteSpace(row.Entry.Publisher) ? scope : row.Entry.Publisher!.Trim() + " · " + scope;
        var warnings = row.Entry.Scope == AutostartScope.Machine ? new[] { AutostartText.AllAccountsWarning } : Array.Empty<string>();
        return new SeriesItem(row.Id, row.Entry.Name, RiskLevel.Safe, summary, new[] { AutostartText.ItemNote(target) }, warnings, Array.Empty<SeriesFact>());
    }

    /// <summary>Ta sama reguła co liczniki „Wyłącz (N)”/„Włącz (M)” na stronie: przełączalny wiersz w stanie przeciwnym do docelowego.</summary>
    private static bool AllMatch(IReadOnlyList<AutostartRow> rows, AutostartState target)
    {
        var from = target == AutostartState.Disabled ? AutostartState.Enabled : AutostartState.Disabled;
        return rows.All(row => row is not null && row.CanToggle && row.State == from);
    }

    private static bool IsMachine(string id)
        => AutostartIds.TryParse(id, out var source, out _, out _) && AutostartSources.ScopeOf(source) == AutostartScope.Machine;

    private static void RequireTarget(AutostartState target)
    {
        if (target is not (AutostartState.Enabled or AutostartState.Disabled))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    private static void RequireAutostart(HistoryEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (entry.Kind != HistoryKind.Autostart)
        {
            throw new ArgumentException("Wpis Historii zmian nie jest zmianą autostartu.", nameof(entry));
        }
    }

    private static GuiOperation OperationOf(AutostartState target)
        => target == AutostartState.Disabled ? GuiOperation.DisableAutostart : GuiOperation.EnableAutostart;

    private static string ItemCount(int count) => L.P("{0} pozycja", "{0} pozycje", "{0} pozycji", count);

    private AutostartContext Context() => AutostartContext.Create(_user, _processSid, _registry);

    /// <summary>Odbiornik zdarzeń nie może przerwać cofania: wyjątek ląduje w logu.</summary>
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
            _log.Fail(AutostartText.PageTitle, L.F("Odbiornik postępu serii rzucił wyjątek ({0} {1}).", step.Kind, step.Phase), ex);
        }
    }

    private GuiOperationReport Refusal(string id, GuiOperation operation, string text) => new()
    {
        TweakId = id,
        Operation = operation,
        Lines = new[] { Line(GuiLogLevel.Warning, text) },
    };

    private GuiLogLine Line(GuiLogLevel level, string text) => new(_clock.UtcNow, level, text);
}
