using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Wyłączanie i włączanie pozycji autostartu. Jedna pozycja: świeży odczyt przełącznika (format i bramka zapisu od zera), journal z regułą
/// jednego aktywnego wpisu, wpis zapisany przed zmianą, zapis, świeży odczyt po zapisie, a przy niezgodności przywrócenie baseline.
/// Sukces wyłącznie po świeżym odczycie; moduł niczego nie usuwa (wartości Run i pliki z folderu Autostart zostają).
/// Cofanie (przełączenie w drugą stronę i Historia zmian) zapisuje zdarzenie <c>undo</c>/<c>Started</c> przed przywróceniem baseline.
/// </summary>
public sealed class AutostartEngine
{
    /// <summary>Akcja zdarzeń wpisu wyłączenia (<c>Started</c>, potem nazwa wyniku).</summary>
    public const string DisableAction = "disable-autostart";

    /// <summary>Akcja zdarzeń wpisu włączenia.</summary>
    public const string EnableAction = "enable-autostart";

    /// <summary>Akcja zdarzenia cofnięcia wpisu (przełączenie w drugą stronę albo Historia zmian).</summary>
    public const string UndoAction = "undo";

    /// <summary>Akcja zdarzenia wpisu, którego zmianę cofnięto poza programem.</summary>
    public const string ExternalAction = "external";

    private const string Component = "Autostart";

    private readonly IRegistry _registry;
    private readonly IJournalStore _journal;
    private readonly ILog _log;
    private readonly IClock _clock;

    public AutostartEngine(IRegistry registry, IJournalStore journal, ILog log, IClock clock)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Ustawia pozycje po kolei na <paramref name="target"/> (<see cref="AutostartState.Enabled"/> albo <see cref="AutostartState.Disabled"/>);
    /// błąd jednej nie zatrzymuje reszty. Wiersz z <see cref="AutostartRow.CanToggle"/> = false daje <see cref="ArgumentException"/>, zanim
    /// cokolwiek zostanie odczytane. „Zatrzymaj” sprawdzany między pozycjami (reszta <see cref="TweakOutcome.Skipped"/>).
    /// </summary>
    public BatchResult Set(IReadOnlyList<AutostartRow> rows, AutostartState target, AutostartContext context, AutostartRunOptions options, CancellationToken ct)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (target is not (AutostartState.Enabled or AutostartState.Disabled))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }

        foreach (var row in rows)
        {
            Guard(row);
        }

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

            Report(options.Steps, new BatchStepEvent(i, rows.Count, BatchStepKind.Item, row.Id, BatchStepPhase.Started, _clock.UtcNow));
            TweakResult result;
            try
            {
                result = SetOne(row, target, context);
            }
            catch (Exception ex)
            {
                _log.Fail(Component, L.F("Nieoczekiwany błąd w serii przy {0}.", row.Id), ex);
                result = new TweakResult { TweakId = row.Id, Outcome = TweakOutcome.Failed, Message = L.F("Nieoczekiwany błąd: {0}", ex.Message) };
            }

            results.Add(result);
            Report(options.Steps, new BatchStepEvent(i, rows.Count, BatchStepKind.Item, row.Id, BatchStepPhase.Finished, _clock.UtcNow, result));
        }

        return new BatchResult { Results = results, Stopped = stopped };
    }

    private static void Guard(AutostartRow? row)
    {
        if (row is null)
        {
            throw new ArgumentException("Pusty wiersz w serii autostartu.", nameof(row));
        }

        // Ta sama reguła co pole wyboru, potwierdzenie i CLI (AutostartRow.CanToggle).
        if (!row.CanToggle)
        {
            throw new ArgumentException($"Pozycji {row.Id} nie wolno przełączać ({row.Blocker}, format {row.Entry.Switch.Format}, stan {row.State}).", nameof(row));
        }
    }

    /// <summary>Jedna pozycja; kolejność kroków jest wymaganiem (świeży odczyt → journal → wpis przed zapisem → zapis → świeży odczyt).</summary>
    private TweakResult SetOne(AutostartRow row, AutostartState target, AutostartContext context)
    {
        var now = _clock.UtcNow;
        var warnings = new List<string>();

        // Konto zmienione po odczycie listy: przełącznik pozycji konta musi leżeć w gałęzi konta z kontekstu (tego SID trafia do journala).
        if (!InAccountHive(row.Entry, context.Locations))
        {
            return Finish(row.Id, TweakOutcome.Refused, AutostartText.FormatChanged, warnings);
        }

        // 1. Świeży odczyt tym samym czytnikiem co lista; dane z wiersza służą tylko do wskazania przełącznika.
        var fresh = AutostartSwitchReader.Reread(_registry, row.Entry.Switch);
        if (fresh.Format is not (AutostartSwitchFormat.Valid or AutostartSwitchFormat.Absent))
        {
            return Finish(row.Id, TweakOutcome.Refused, AutostartText.FormatChanged, warnings);
        }

        if (fresh.State == target)
        {
            return Finish(row.Id, TweakOutcome.AlreadyApplied, target == AutostartState.Disabled ? AutostartText.AlreadyDisabled : AutostartText.AlreadyEnabled, warnings);
        }

        // Bramka zapisu od zera dopiero po „już w stanie docelowym”: bez zapisu nie ma czego bramkować.
        if (!AutostartClassifier.WriteFromZeroAllowed(fresh, row.Entry.Source, context))
        {
            return Finish(row.Id, TweakOutcome.Refused, AutostartText.FormatChanged, warnings);
        }

        // 2. Journal i reguła jednego aktywnego wpisu.
        if (!TryLoad(row.Id, warnings, out var loaded, out var loadFailure))
        {
            return loadFailure!;
        }

        var document = loaded!;
        var active = AutostartJournal.FindActive(document, row.Id, context.UserSid);
        if (active is not null && AutostartJournal.TargetOf(active) == Opposite(target))
        {
            return UndoEntry(row.Id, document, active, AutostartText.UndoneByToggle, warnings);
        }

        if (active is not null)
        {
            // Ten sam kierunek, a stan wrócił poza programem: stary wpis się zamyka, powstaje nowy (najwyżej jeden aktywny).
            active.Status = JournalEntryStatus.Undone;
            AddEvent(active, ExternalAction, TweakOutcome.Undone.ToString(), AutostartText.ChangedOutside);
        }

        // 3. Nowy wpis z baseline, zapisany przed zmianą.
        var baseline = CaptureBaseline(fresh);
        if (baseline is null)
        {
            return Finish(row.Id, TweakOutcome.Refused, AutostartText.FormatChanged, warnings);
        }

        var action = target == AutostartState.Disabled ? DisableAction : EnableAction;
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = JournalEntry.AutostartKind,
            TweakId = row.Id,
            Title = row.Entry.Name,
            UserSid = context.UserSid,
            // Do potwierdzenia świeżym odczytem wpis nie twierdzi, że coś zmieniono.
            Status = JournalEntryStatus.Aborted,
            AppliedAt = now,
        };
        entry.Baseline.AddRange(baseline.Records);
        AddEvent(entry, action, "Started", null);
        document.Entries.Add(entry);
        try
        {
            _journal.Save(document);
        }
        catch (Exception ex)
        {
            return JournalFailure(row.Id, ex, warnings);
        }

        // 4. Zapis.
        string? writeError = null;
        try
        {
            Write(fresh, baseline.Raw, target, now);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.F("Nieoczekiwany błąd przy zapisie stanu {0}.", row.Id), ex);
            writeError = ex.Message;
        }

        // 5. Świeży odczyt po zapisie; niezgodność albo wyjątek zapisu = przywrócenie baseline.
        TweakOutcome outcome;
        string message;
        IReadOnlyList<string> notRestored = Array.Empty<string>();
        if (writeError is null && Confirmed(row.Id, fresh, target))
        {
            outcome = TweakOutcome.Applied;
            message = target == AutostartState.Disabled ? AutostartText.Disabled : AutostartText.Enabled;
            entry.Status = JournalEntryStatus.Active;
        }
        else
        {
            notRestored = AutostartJournal.RestoreBaseline(_registry, entry);
            outcome = notRestored.Count == 0 ? TweakOutcome.FailedRolledBack : TweakOutcome.FailedRollbackIncomplete;
            message = notRestored.Count == 0 ? AutostartText.NotConfirmed : AutostartText.RollbackIncomplete;
        }

        AddEvent(entry, action, outcome.ToString(), message, writeError);
        TrySave(document, warnings);
        return Finish(row.Id, outcome, message, warnings, notRestored);
    }

    /// <summary>
    /// Cofa wpis autostartu z Historii zmian: przywraca jego baseline i sprawdza go świeżym odczytem. Odmowa (bez zapisu): wpis nieznany,
    /// innego rodzaju, nieaktywny albo nie najnowszy aktywny dla pozycji; dla pozycji konta także niepewny użytkownik, inny SID wpisu
    /// i brak gałęzi konta przy innym koncie procesu. Zdarzenie <c>undo</c>/<c>Started</c> jest zapisywane przed przywróceniem.
    /// </summary>
    public TweakResult Undo(Guid entryId, AutostartContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var warnings = new List<string>();
        return TryLoad(entryId.ToString(), warnings, out var document, out var failure)
            ? UndoIn(document!, entryId, context, warnings)
            : failure!;
    }

    /// <summary>Cofa aktywny wpis pozycji (<see cref="AutostartJournal.FindActive"/> z SID kontekstu); brak wpisu = <see cref="TweakOutcome.Refused"/>.</summary>
    public TweakResult UndoActive(string itemId, AutostartContext context)
    {
        if (itemId is null)
        {
            throw new ArgumentNullException(nameof(itemId));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var warnings = new List<string>();
        if (!TryLoad(itemId, warnings, out var document, out var failure))
        {
            return failure!;
        }

        var entry = AutostartJournal.FindActive(document!, itemId, context.UserSid);
        return entry is null
            ? Finish(itemId, TweakOutcome.Refused, AutostartText.NoActiveEntry, warnings)
            : UndoIn(document!, entry.EntryId, context, warnings);
    }

    /// <summary>Odmowy i cofnięcie wpisu w już wczytanym journalu (jeden odczyt na wywołanie, ostrzeżenie odczytu w wyniku).</summary>
    private TweakResult UndoIn(JournalDocument document, Guid entryId, AutostartContext context, List<string> warnings)
    {
        var entry = document.Entries.FirstOrDefault(candidate =>
            candidate.EntryId == entryId && string.Equals(candidate.Kind, JournalEntry.AutostartKind, StringComparison.Ordinal));
        if (entry is null || !entry.IsActive)
        {
            return Finish(entry?.TweakId ?? entryId.ToString(), TweakOutcome.Refused, AutostartText.NoActiveEntry, warnings);
        }

        if (!ReferenceEquals(entry, AutostartJournal.FindActive(document, entry.TweakId, entry.UserSid)))
        {
            return Finish(entry.TweakId, TweakOutcome.Refused, HistoryText.UndoNotNewest, warnings);
        }

        if (AccountRefusal(entry, context) is { } refusal)
        {
            return Finish(entry.TweakId, TweakOutcome.Refused, refusal, warnings);
        }

        return UndoEntry(entry.TweakId, document, entry, AutostartText.UndoneFromHistory, warnings);
    }

    /// <summary>Powód odmowy cofnięcia wpisu pozycji konta; null = wolno cofać (także każda pozycja maszyny).</summary>
    private static string? AccountRefusal(JournalEntry entry, AutostartContext context)
    {
        if (!AutostartIds.TryParse(entry.TweakId, out var source, out _, out _))
        {
            return AutostartText.NoActiveEntry;
        }

        var scope = AutostartSources.ScopeOf(source);
        if (scope == AutostartScope.Machine)
        {
            return null;
        }

        if (!context.UserCertain)
        {
            return AppsText.UserUncertain;
        }

        if (!string.Equals(entry.UserSid, context.UserSid, StringComparison.OrdinalIgnoreCase))
        {
            return HistoryText.UndoOtherAccount(entry.UserSid);
        }

        // Ta sama reguła co blokada wiersza (AutostartClassifier): gałąź konta musi być dostępna, gdy proces działa innym kontem.
        var otherAccount = !string.Equals(context.ProcessSid, context.UserSid, StringComparison.OrdinalIgnoreCase);
        var hiveLoaded = scope == AutostartScope.User ? context.UserHiveLoaded : context.ClassesHiveLoaded;
        return otherAccount && !hiveLoaded ? AutostartText.Blocked(AutostartBlocker.UserHiveMissing) : null;
    }

    private bool TryLoad(string id, List<string> warnings, out JournalDocument? document, out TweakResult? failure)
    {
        try
        {
            var load = _journal.Load();
            if (load.Warning is not null)
            {
                _log.Warn(Component, load.Warning);
                warnings.Add(load.Warning);
            }

            document = load.Document;
            failure = null;
            return true;
        }
        catch (Exception ex)
        {
            document = null;
            failure = JournalFailure(id, ex, warnings);
            return false;
        }
    }

    /// <summary>
    /// Przywrócenie baseline aktywnego wpisu (przełączenie w drugą stronę albo Historia zmian). Najpierw zapis zdarzenia
    /// <c>undo</c>/<c>Started</c> (błąd = <see cref="TweakOutcome.Failed"/>, rejestr nietknięty), potem przywrócenie i zdarzenie wyniku.
    /// </summary>
    private TweakResult UndoEntry(string id, JournalDocument document, JournalEntry active, string undoneMessage, List<string> warnings)
    {
        AddEvent(active, UndoAction, "Started", null);
        try
        {
            _journal.Save(document);
        }
        catch (Exception ex)
        {
            return JournalFailure(id, ex, warnings);
        }

        var notRestored = AutostartJournal.RestoreBaseline(_registry, active);
        var outcome = notRestored.Count == 0 ? TweakOutcome.Undone : TweakOutcome.UndoIncomplete;
        string? message = null;
        if (outcome == TweakOutcome.Undone)
        {
            active.Status = JournalEntryStatus.Undone;
            message = undoneMessage;
        }

        AddEvent(active, UndoAction, outcome.ToString(), message ?? string.Join("; ", notRestored));
        TrySave(document, warnings);
        return Finish(id, outcome, message, warnings, notRestored);
    }

    /// <summary>
    /// Stan sprzed zmiany i bieżąca wartość <c>StartupApproved</c> (do wyliczenia zapisu). Null = odczyt niezgodny ze świeżym odczytem
    /// przełącznika (stan zmienił się w międzyczasie) albo nieczytelny – nic nie jest zapisywane.
    /// </summary>
    private Baseline? CaptureBaseline(AutostartSwitch fresh)
    {
        if (fresh.Kind == AutostartSwitchKind.StartupApproved)
        {
            var read = _registry.Read(fresh.KeyPath, fresh.ValueName);
            var current = StartupApprovedValue.Interpret(read);
            if (current.Format is not (AutostartSwitchFormat.Valid or AutostartSwitchFormat.Absent) || current.State != fresh.State)
            {
                return null;
            }

            var value = read.Status == ReadStatus.Found ? read.Value : RegValue.Absent;
            return new Baseline(new[] { SnapshotRecord.ForRegistry(fresh.KeyPath, fresh.ValueName, value) }, current.Raw);
        }

        var names = new[] { StartupTaskValue.State, StartupTaskValue.LastDisabledTime, StartupTaskValue.UserEnabledStartupOnce, StartupTaskValue.ShowNotification };
        var records = new List<SnapshotRecord>();
        foreach (var name in names)
        {
            var read = _registry.Read(fresh.KeyPath, name);
            if (read.Status is not (ReadStatus.Found or ReadStatus.NotFound))
            {
                return null;
            }

            var value = read.Status == ReadStatus.Found ? read.Value : RegValue.Absent;
            // Klucza zadania nie było: cofnięcie skasuje go, jeśli zostanie pusty (klucz rodziny pakietu istnieje zawsze).
            var createdKey = name == StartupTaskValue.State && !fresh.KeyExists ? fresh.KeyPath : null;
            records.Add(SnapshotRecord.ForRegistry(fresh.KeyPath, name, value, createdKey));
        }

        var expectedState = fresh.TaskState is { } state ? RegValue.FromDWord(state) : RegValue.Absent;
        if (!records[0].Value!.Equals(expectedState) || (!fresh.KeyExists && records.Any(record => record.Value!.Exists)))
        {
            return null;
        }

        return new Baseline(records, null);
    }

    /// <summary>Zapis przełącznika; <c>State</c> zadania startowego na końcu, po wartościach towarzyszących.</summary>
    private void Write(AutostartSwitch fresh, byte[]? raw, AutostartState target, DateTimeOffset now)
    {
        if (fresh.Kind == AutostartSwitchKind.StartupApproved)
        {
            var bytes = target == AutostartState.Disabled ? StartupApprovedValue.Disabled(raw, now) : StartupApprovedValue.Enabled(raw!);
            _registry.Write(fresh.KeyPath, fresh.ValueName, RegValue.FromBinary(bytes));
            return;
        }

        if (target == AutostartState.Disabled)
        {
            _registry.Write(fresh.KeyPath, StartupTaskValue.LastDisabledTime, RegValue.FromQWord((ulong)now.ToUnixTimeSeconds()));
            _registry.Write(fresh.KeyPath, StartupTaskValue.State, RegValue.FromDWord(1));
        }
        else
        {
            _registry.Write(fresh.KeyPath, StartupTaskValue.UserEnabledStartupOnce, RegValue.FromDWord(1));
            _registry.Write(fresh.KeyPath, StartupTaskValue.ShowNotification, RegValue.Absent);
            _registry.Write(fresh.KeyPath, StartupTaskValue.State, RegValue.FromDWord(2));
        }
    }

    /// <summary>Świeży odczyt po zapisie: zapisany format i stan docelowy; wyjątek odczytu = niepotwierdzone.</summary>
    private bool Confirmed(string id, AutostartSwitch fresh, AutostartState target)
    {
        try
        {
            var after = AutostartSwitchReader.Reread(_registry, fresh);
            return after.Format == AutostartSwitchFormat.Valid && after.State == target;
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.F("Nieoczekiwany błąd weryfikacji {0}.", id), ex);
            return false;
        }
    }

    /// <summary>
    /// Pozycja maszyny = zawsze true. Pozycja konta: klucz przełącznika w <see cref="AutostartLocations.UserRoot"/> (Run i folder konta) albo
    /// <see cref="AutostartLocations.UserClassesRoot"/> (Sklep), bez rozróżniania wielkości liter i na granicy segmentu ścieżki.
    /// </summary>
    private static bool InAccountHive(AutostartEntry entry, AutostartLocations locations)
    {
        var scope = AutostartSources.ScopeOf(entry.Source);
        if (scope == AutostartScope.Machine)
        {
            return true;
        }

        var root = scope == AutostartScope.Store ? locations.UserClassesRoot : locations.UserRoot;
        var path = entry.Switch.KeyPath;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && (path.Length == root.Length || path[root.Length] == '\\');
    }

    private static AutostartState Opposite(AutostartState target)
        => target == AutostartState.Disabled ? AutostartState.Enabled : AutostartState.Disabled;

    private TweakResult JournalFailure(string id, Exception ex, List<string> warnings)
    {
        if (ex is not JournalStoreException)
        {
            _log.Fail(Component, L.F("Nieoczekiwany błąd journala przy {0}.", id), ex);
        }

        return Finish(id, TweakOutcome.Failed, AutostartText.JournalFailed(ex.Message), warnings);
    }

    private void AddEvent(JournalEntry entry, string action, string outcome, string? message, string? reason = null)
        => entry.Events.Add(new JournalEvent { At = _clock.UtcNow, Action = action, Outcome = outcome, Message = message, Reason = reason });

    private void TrySave(JournalDocument document, List<string> warnings)
    {
        try
        {
            _journal.Save(document);
        }
        catch (Exception ex)
        {
            var text = L.F("Nie udało się zapisać journala: {0}", ex.Message);
            _log.Warn(Component, text);
            warnings.Add(text);
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
            _log.Fail(Component, L.F("Odbiornik postępu serii rzucił wyjątek ({0} {1}).", step.Kind, step.Phase), ex);
        }
    }

    private TweakResult Finish(string id, TweakOutcome outcome, string? message, List<string> warnings, IReadOnlyList<string>? notRestored = null)
    {
        var result = new TweakResult
        {
            TweakId = id,
            Outcome = outcome,
            Message = message,
            Warnings = warnings,
            NotRestored = notRestored ?? Array.Empty<string>(),
        };
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

    private static TweakResult Skipped(AutostartRow row, string message)
        => new() { TweakId = row.Id, Outcome = TweakOutcome.Skipped, Message = message };

    /// <summary>Rekordy baseline i bieżące bajty <c>StartupApproved</c> (null: brak wartości albo zadanie startowe).</summary>
    private sealed record Baseline(IReadOnlyList<SnapshotRecord> Records, byte[]? Raw);
}
