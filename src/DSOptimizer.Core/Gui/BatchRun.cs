using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Gui;

/// <summary>Stan kroku serii w oknie postępu i wyniku.</summary>
public enum BatchStepState
{
    Waiting,
    Running,
    Done,
    Warning,
    Failed,
    Skipped,
}

/// <summary>Jeden krok serii: punkt przywracania albo pozycja katalogu. Settery tylko dla <see cref="BatchRun"/>.</summary>
public sealed class BatchRunStep
{
    internal BatchRunStep(string? tweakId, string title)
    {
        TweakId = tweakId;
        Title = title;
    }

    /// <summary>Id pozycji katalogu; null dla kroku „Punkt przywracania”.</summary>
    public string? TweakId { get; }

    public string Title { get; }

    public BatchStepState State { get; internal set; } = BatchStepState.Waiting;

    public string StatusText { get; internal set; } = L.T("Czeka");

    public IReadOnlyList<string> Details { get; internal set; } = Array.Empty<string>();
}

/// <summary>
/// Model postępu i wyniku serii (bez WPF): okno pokazuje kroki, nagłówek i pasek postępu, a po końcu wynik.
/// Każdy tekst kroku pochodzi z <see cref="TweakResult"/>/<see cref="RestorePointResult"/>/<see cref="BatchResult"/>.
/// Klasa nie jest bezpieczna wątkowo: wołać na wątku UI.
/// </summary>
public sealed class BatchRun
{
    private readonly bool _undo;
    private readonly bool _remove;
    private readonly bool _install;
    private readonly bool _restorePointRequired;
    private readonly int _itemCount;
    private readonly List<BatchRunStep> _steps = new();

    /// <summary>Ostatni wynik kroku w trybie instalacji (podsumowanie „N z M zainstalowane” także po <see cref="Fail"/>).</summary>
    private readonly Dictionary<BatchRunStep, TweakOutcome> _installOutcomes = new();
    private readonly BatchRunStep? _restorePointStep;
    private DateTimeOffset? _restorePointStarted;
    private DateTimeOffset? _restorePointFinished;
    private int _currentIndex;
    private IReadOnlyList<string> _warnings = Array.Empty<string>();

    public BatchRun(SeriesConfirmation confirmation)
    {
        if (confirmation is null)
        {
            throw new ArgumentNullException(nameof(confirmation));
        }

        _undo = confirmation.Operation == GuiOperation.Undo;
        _remove = confirmation.Operation == GuiOperation.RemoveApps;
        _install = confirmation.Operation == GuiOperation.InstallApps;
        _restorePointRequired = confirmation.RestorePointRequired;
        _itemCount = confirmation.Items.Count;
        if (confirmation.CreatesRestorePoint)
        {
            _restorePointStep = new BatchRunStep(null, L.T("Punkt przywracania"));
            _steps.Add(_restorePointStep);
        }

        foreach (var item in confirmation.Items)
        {
            _steps.Add(new BatchRunStep(item.Id, item.Title));
        }

        Title = _install ? L.T("Instalowanie aplikacji") : _remove ? L.T("Usuwanie aplikacji") : _undo ? L.T("Cofanie zmian") : L.T("Zastosowywanie zmian");
        Subtitle = ProgressSubtitle();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<BatchRunStep> Steps => _steps;

    public bool IsFinished { get; private set; }

    public string Title { get; private set; }

    public string Subtitle { get; private set; }

    /// <summary>Ukończone kroki przez wszystkie kroki, 0–1.</summary>
    public double Progress
    {
        get
        {
            if (_steps.Count == 0)
            {
                return IsFinished ? 1 : 0;
            }

            var finished = _steps.Count(step => step.State is not (BatchStepState.Waiting or BatchStepState.Running));
            return (double)finished / _steps.Count;
        }
    }

    public IReadOnlyList<string> Warnings => _warnings;

    public int RebootCount { get; private set; }

    /// <summary>Blok restartu w wyniku; dla serii usuwania tekst o dokończeniu usuwania (Ruling R17), dla instalacji – o aplikacjach.</summary>
    public string? RebootText => RebootCount <= 0
        ? null
        : _install ? InstallText.RebootText(RebootCount)
        : _remove ? AppsPageText.RemovalRebootText(RebootCount) : GuiText.RebootBannerText(RebootCount);

    /// <summary>Zdarzenie postępu z silnika. Po <see cref="Complete"/>/<see cref="Fail"/> ignorowane.</summary>
    public void OnStep(BatchStepEvent e)
    {
        if (e is null)
        {
            throw new ArgumentNullException(nameof(e));
        }

        if (IsFinished)
        {
            return;
        }

        var step = FindStep(e);
        if (step is null)
        {
            return;
        }

        if (e.Phase == BatchStepPhase.Progress)
        {
            // Etap i procent winget w trakcie pozycji (spec 5.2) pokazuje tylko tryb instalacji; w innych seriach stan kroku się nie zmienia.
            if (_install && step.State == BatchStepState.Running)
            {
                step.StatusText = InstallProgressText(e.Stage, e.Percent);
                RaiseChanged();
            }

            return;
        }

        if (e.Phase == BatchStepPhase.Started)
        {
            _currentIndex = Math.Max(0, Math.Min(_steps.Count - 1, _steps.IndexOf(step)));
            step.State = BatchStepState.Running;
            step.StatusText = L.T("W toku");
            if (ReferenceEquals(step, _restorePointStep))
            {
                _restorePointStarted = e.At;
            }

            Subtitle = ProgressSubtitle();
        }
        else if (ReferenceEquals(step, _restorePointStep))
        {
            _restorePointFinished = e.At;
            ApplyRestorePoint(e.RestorePoint);
        }
        else if (e.Result is not null)
        {
            Apply(step, e.Result);
            if (_install)
            {
                // Wynik pozycji (także wczesny, bez Started) może zmienić „Dalej”.
                Subtitle = ProgressSubtitle();
            }
        }

        RaiseChanged();
    }

    /// <summary>Rozstrzyga ostatecznie z wyniku serii; nadpisuje stany ze zdarzeń.</summary>
    public void Complete(GuiOperationReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var batch = report.Batch;
        if (batch is null)
        {
            // Odmowa lokalna (np. „Trwa inna operacja”): powód z raportu zamiast ogólnego tekstu.
            Fail(report.Lines.Count > 0 ? report.Lines[0].Text : L.T("Brak wyniku serii."));
            return;
        }

        if (_restorePointStep is not null)
        {
            ApplyRestorePoint(batch.RestorePoint);
        }

        foreach (var step in _steps.Where(step => step.TweakId is not null))
        {
            var result = batch.Results.FirstOrDefault(r => string.Equals(r.TweakId, step.TweakId, StringComparison.Ordinal));
            if (result is null)
            {
                step.State = BatchStepState.Skipped;
                step.StatusText = L.T("Pominięte");
                step.Details = Array.Empty<string>();
            }
            else
            {
                Apply(step, result);
            }
        }

        var warnings = new List<string>(batch.Warnings);
        if (!string.IsNullOrEmpty(batch.AbortReason))
        {
            warnings.Add(batch.AbortReason!);
        }

        _warnings = warnings.Distinct(StringComparer.Ordinal).ToList();
        // Instalacja: restart zgłasza też „Nie wykonano – wymaga restartu przed instalacją” (spec 5.3).
        RebootCount = _install
            ? batch.Results.Count(r => r.RequiresReboot)
            : batch.Results.Count(r => (r.IsSuccess || r.Outcome == TweakOutcome.NotConfirmed) && r.RequiresReboot);
        Title = batch.Stopped ? L.T("Zatrzymano") : L.T("Zakończono");
        Subtitle = _install
            ? InstallText.BatchSummary(batch.Results.Count(r => InstallPage.CountsAsInstalled(r.Outcome)), _itemCount)
            : SucceededSubtitle(batch.SucceededCount);
        IsFinished = true;
        RaiseChanged();
    }

    /// <summary>Awaria całej serii (wyjątek): niedokończone kroki kończą się błędem z podanym komunikatem.</summary>
    public void Fail(string message)
    {
        foreach (var step in _steps.Where(step => step.State is BatchStepState.Waiting or BatchStepState.Running))
        {
            step.State = BatchStepState.Failed;
            step.StatusText = _install ? InstallText.NotDone : _remove ? AppsPageText.FailedStatus : L.T("Błąd");
            step.Details = new[] { message };
        }

        Title = L.T("Zakończono z błędem");
        Subtitle = _install
            ? InstallText.BatchSummary(_installOutcomes.Values.Count(InstallPage.CountsAsInstalled), _itemCount)
            : SucceededSubtitle(_steps.Count(step => step.TweakId is not null && step.State == BatchStepState.Done));
        IsFinished = true;
        RaiseChanged();
    }

    /// <summary>Wynik jako zwykły tekst (do schowka): nagłówek, kroki ze szczegółami, ostrzeżenia, restart.</summary>
    public string ResultText()
    {
        var lines = new List<string> { $"{Title}: {Subtitle}" };
        foreach (var step in _steps)
        {
            lines.Add($"{step.Title} – {step.StatusText}");
            lines.AddRange(step.Details.Select(detail => "  " + detail));
        }

        lines.AddRange(_warnings);
        if (RebootText is not null)
        {
            lines.Add(L.F("Wymagany restart. {0}", RebootText));
        }

        return string.Join("\r\n", lines);
    }

    private BatchRunStep? FindStep(BatchStepEvent e)
        => e.Kind == BatchStepKind.RestorePoint
            ? _restorePointStep
            : _steps.FirstOrDefault(step => step.TweakId is not null && string.Equals(step.TweakId, e.TweakId, StringComparison.Ordinal));

    private string ProgressSubtitle()
    {
        if (!_install)
        {
            return L.F("krok {0} z {1}", _currentIndex + 1, _steps.Count);
        }

        // Instalacja (spec 5.2): „Aplikacja N z M · Dalej: <tytuł>”. „Dalej” = pierwsza czekająca pozycja po bieżącej – pozycja
        // odrzucona przed startem (wczesne Finished bez Started) już ma wynik, więc jest pomijana (M6 z review Tasku 2); bez
        // czekających – bez „Dalej”.
        var items = _steps.Where(step => step.TweakId is not null).ToList();
        var index = _steps.Count == 0 ? 0 : Math.Max(0, items.IndexOf(_steps[_currentIndex]));
        var next = items.Skip(index + 1).FirstOrDefault(step => step.State == BatchStepState.Waiting);
        return next is null
            ? L.F("Aplikacja {0} z {1}", index + 1, items.Count)
            : L.F("Aplikacja {0} z {1} · Dalej: {2}", index + 1, items.Count, next.Title);
    }

    /// <summary>Status kroku w trakcie instalacji: „Pobieranie 45%”, „Pobieranie”, „Instalowanie”; bez etapu – „W toku”.</summary>
    private static string InstallProgressText(string? stage, int? percent)
    {
        var text = string.IsNullOrWhiteSpace(stage) ? L.T("W toku") : stage!;
        return percent is { } value ? string.Format(CultureInfo.InvariantCulture, "{0} {1}%", text, value) : text;
    }

    private string SucceededSubtitle(int succeeded)
        => _remove ? L.F("{0} z {1} usunięte", succeeded, _itemCount)
            : _undo ? L.F("{0} z {1} cofnięte", succeeded, _itemCount)
            : L.F("{0} z {1} zastosowane", succeeded, _itemCount);

    private void ApplyRestorePoint(RestorePointResult? result)
    {
        var step = _restorePointStep!;
        step.Details = Array.Empty<string>();
        if (result is null)
        {
            step.State = BatchStepState.Skipped;
            step.StatusText = L.T("Pominięte");
        }
        else if (result.Status == RestorePointStatus.Created)
        {
            step.State = BatchStepState.Done;
            step.StatusText = _restorePointStarted is { } start && _restorePointFinished is { } end
                ? L.F("Utworzony, {0} s", Math.Max(1, (int)Math.Ceiling((end - start).TotalSeconds)))
                : L.T("Utworzony");
        }
        else
        {
            // Usuwanie z programem: punkt nie powstał = porażka kroku (render pytania). Seria samych AppX idzie dalej bez pytania,
            // więc jak tweaki: ostrzeżenie (R20).
            step.State = _remove && _restorePointRequired ? BatchStepState.Failed : BatchStepState.Warning;
            step.StatusText = L.T("Nie powstał");
            if (!string.IsNullOrEmpty(result.Message))
            {
                step.Details = new[] { result.Message! };
            }
        }
    }

    private void Apply(BatchRunStep step, TweakResult result)
    {
        if (_install)
        {
            ApplyInstallResult(step, result);
        }
        else
        {
            ApplyResult(step, result, _remove);
        }
    }

    /// <summary>
    /// Wynik pozycji instalacji: status z <see cref="InstallText.OutcomeName"/>, szczegół z <see cref="InstallText.ResultDetail"/>.
    /// „Zgłoszone przez winget” to słabszy dowód (bez odczytu zwrotnego), więc ostrzeżenie, nie „sprawdzone”.
    /// </summary>
    private void ApplyInstallResult(BatchRunStep step, TweakResult result)
    {
        _installOutcomes[step] = result.Outcome;
        var state = result.Outcome switch
        {
            TweakOutcome.Installed or TweakOutcome.AlreadyInstalled => BatchStepState.Done,
            TweakOutcome.InstalledUnverified or TweakOutcome.NotConfirmed or TweakOutcome.Refused => BatchStepState.Warning,
            TweakOutcome.Skipped => BatchStepState.Skipped,
            _ => BatchStepState.Failed,
        };
        Set(step, state, InstallText.OutcomeName(result.Outcome));
        var details = new List<string>();
        if (InstallText.ResultDetail(result) is { } detail)
        {
            details.Add(detail);
        }

        details.AddRange(result.Warnings);
        step.Details = details;
    }

    private static void ApplyResult(BatchRunStep step, TweakResult result, bool remove)
    {
        var details = new List<string>();
        switch (result.Outcome)
        {
            case TweakOutcome.Applied:
            case TweakOutcome.Executed:
                Set(step, BatchStepState.Done, L.T("Zastosowane i sprawdzone"));
                break;
            case TweakOutcome.PendingReboot:
                Set(step, BatchStepState.Done, L.T("Zastosowane, po restarcie"));
                break;
            case TweakOutcome.AlreadyApplied:
                Set(step, BatchStepState.Done, L.T("Było już zastosowane"));
                break;
            case TweakOutcome.Undone:
                Set(step, BatchStepState.Done, L.T("Cofnięte i sprawdzone"));
                break;
            case TweakOutcome.UndoneFromDefaults:
                Set(step, BatchStepState.Done, L.T("Cofnięte do wartości domyślnych"));
                break;
            case TweakOutcome.Removed:
                // „Już nie było” (wzorzec jak StoppedMessage przy Skipped): DSOptimizer nic nie usunął, więc nie „Usunięte i sprawdzone”.
                if (string.Equals(result.Message, AppsText.AlreadyGone, StringComparison.Ordinal))
                {
                    Set(step, BatchStepState.Done, L.T("Nie było już na komputerze"));
                }
                else
                {
                    Set(step, BatchStepState.Done, L.T("Usunięte i sprawdzone"));
                    AddMessage(details, result);
                }

                break;
            case TweakOutcome.Installed:
                Set(step, BatchStepState.Done, L.T("Zainstalowane i sprawdzone"));
                AddMessage(details, result);
                break;
            case TweakOutcome.AlreadyInstalled:
                Set(step, BatchStepState.Done, L.T("Była już zainstalowana"));
                AddMessage(details, result);
                break;
            case TweakOutcome.InstalledUnverified:
                // Słabszy dowód (zgłoszone przez winget, bez odczytu zwrotnego): ostrzeżenie, nie „sprawdzone”.
                Set(step, BatchStepState.Warning, L.T("Zainstalowane (zgłoszone przez winget)"));
                AddMessage(details, result);
                break;
            case TweakOutcome.NotConfirmed:
                // Ogólny powód (deinstalator nadal działa) jest statusem; inny powód (restart, przerwane, Sklep) idzie do szczegółów.
                if (result.Message is null || string.Equals(result.Message, AppsText.NotConfirmed, StringComparison.Ordinal))
                {
                    Set(step, BatchStepState.Warning, AppsText.NotConfirmed);
                }
                else
                {
                    Set(step, BatchStepState.Warning, L.T("Nie potwierdzono"));
                    AddMessage(details, result);
                }

                break;
            case TweakOutcome.NotSupported:
                Set(step, BatchStepState.Warning, L.T("Nieobsługiwane w tym systemie"));
                break;
            case TweakOutcome.Refused:
                Set(step, BatchStepState.Warning, L.T("Odmowa"));
                AddMessage(details, result);
                break;
            case TweakOutcome.Skipped:
                Set(
                    step,
                    BatchStepState.Skipped,
                    string.Equals(result.Message, TweakEngine.StoppedMessage, StringComparison.Ordinal) ? L.T("Pominięte – zatrzymano") : L.T("Pominięte"));
                break;
            default:
                if (remove)
                {
                    // Usuwanie (R17): „Nie udało się” z powodem, bez powtórzonego „Nie udało się – ” w szczegółach.
                    Set(step, BatchStepState.Failed, AppsPageText.FailedStatus);
                    if (!string.IsNullOrEmpty(result.Message))
                    {
                        details.Add(AppsPageText.FailureDetail(result.Message!));
                    }

                    break;
                }

                Set(step, BatchStepState.Failed, L.T("Błąd"));
                AddMessage(details, result);
                if (result.Outcome == TweakOutcome.FailedRolledBack)
                {
                    details.Add(L.T("Zmiana została wycofana, stan jak przed operacją."));
                }
                else if (result.Outcome == TweakOutcome.FailedRollbackIncomplete)
                {
                    details.Add(L.T("Nie wszystko udało się wycofać:"));
                    details.AddRange(result.NotRestored);
                }
                else if (result.Outcome == TweakOutcome.UndoIncomplete)
                {
                    details.Add(L.T("Nie wszystko udało się przywrócić:"));
                    details.AddRange(result.NotRestored);
                }

                break;
        }

        details.AddRange(result.Warnings);
        step.Details = details;
    }

    private static void Set(BatchRunStep step, BatchStepState state, string text)
    {
        step.State = state;
        step.StatusText = text;
    }

    private static void AddMessage(List<string> details, TweakResult result)
    {
        if (!string.IsNullOrEmpty(result.Message))
        {
            details.Add(result.Message!);
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
