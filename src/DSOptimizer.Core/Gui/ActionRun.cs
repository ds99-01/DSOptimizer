using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Gui;

/// <summary>
/// Model postępu i wyniku jednej długiej naprawy w oknie serii (bez WPF): etap i procent z <see cref="ActionProgress"/>,
/// „Zatrzymaj po tym kroku” (tylko przed ostatnim krokiem i tylko gdy zdarzenie nie mówi
/// <see cref="ActionProgress.Stoppable"/> = <c>false</c> – przebieg nieprzerywalny, np. <c>windows-old</c>), a po końcu wynik wyłącznie z <see cref="TweakResult"/> (jak
/// <see cref="BatchRun"/>). Procent <c>null</c> = nieokreślony: okno pokazuje sam etap, bez liczby. Po końcu <see cref="Stage"/>
/// to tytuł pozycji, a <see cref="Percent"/> = 100 tylko przy wyniku udanym (bez udawania ukończenia). „Zatrzymano” tylko wtedy,
/// gdy zatrzymanie zażądano i wynik nie jest udany: prośba mogła dojść, gdy handler był już w ostatnim kroku (postęp przychodzi
/// do okna asynchronicznie), a wtedy handler kończy całość. Handler zatrzymany między krokami nie zwraca więc <c>Executed</c>.
/// Klasa nie jest bezpieczna wątkowo: wołać na wątku UI.
/// </summary>
public sealed class ActionRun
{
    private const string Finished = "Zakończono";
    private const string FinishedWithError = "Zakończono z błędem";
    private const string Stopped = "Zatrzymano";

    private static readonly string[] LineBreaks = { "\r\n", "\n", "\r" };

    private readonly string _itemTitle;
    private bool _stoppable = true;
    private IReadOnlyList<string> _resultLines = Array.Empty<string>();
    private IReadOnlyList<string> _warnings = Array.Empty<string>();

    public ActionRun(SeriesConfirmation confirmation)
    {
        if (confirmation is null)
        {
            throw new ArgumentNullException(nameof(confirmation));
        }

        if (confirmation.Operation != GuiOperation.Execute || confirmation.Items.Count != 1)
        {
            throw new ArgumentException("Okno postępu akcji dotyczy dokładnie jednej pozycji uruchamianej przez Execute.", nameof(confirmation));
        }

        _itemTitle = confirmation.Items[0].Title;
        Title = _itemTitle;
        Subtitle = GuiText.ActionRunningSubtitle;
        Stage = GuiText.ActionStarting;
    }

    public event EventHandler? Changed;

    /// <summary>Pasek tytułu: w trakcie tytuł pozycji, po końcu „Zakończono”, „Zatrzymano” albo „Zakończono z błędem”.</summary>
    public string Title { get; private set; }

    /// <summary>W trakcie „w toku”, po końcu nazwa wyniku (<see cref="GuiText.OutcomeName"/>).</summary>
    public string Subtitle { get; private set; }

    public string Stage { get; private set; }

    /// <summary>0–100 albo null (nieokreślony).</summary>
    public int? Percent { get; private set; }

    /// <summary>Indeks bieżącego kroku (od 0).</summary>
    public int StepIndex { get; private set; }

    /// <summary>Liczba kroków; 0 przed pierwszym zdarzeniem postępu.</summary>
    public int StepCount { get; private set; }

    /// <summary>„Krok 1 z 2”; pusty przed pierwszym zdarzeniem i po końcu.</summary>
    public string StepText => StepCount > 0 && !IsFinished ? GuiText.ActionStepText(StepIndex, StepCount) : string.Empty;

    /// <summary>
    /// „Zatrzymaj po tym kroku” ma sens tylko wtedy, gdy po bieżącym kroku jest jeszcze następny, a handler nie oznaczył przebiegu
    /// jako nieprzerywalnego (<see cref="ActionProgress.Stoppable"/> ostatniego zdarzenia).
    /// </summary>
    public bool CanStop => !IsFinished && !StopRequested && StepCount > 0 && _stoppable && StepIndex < StepCount - 1;

    /// <summary>
    /// Ostatnie <see cref="ActionProgress.Stoppable"/> (domyślnie <c>true</c>). <c>false</c> = przebieg nieprzerywalny (np. <c>windows-old</c>):
    /// okno nie pokazuje wtedy stopki z „Zatrzymaj”.
    /// </summary>
    public bool IsStoppable => _stoppable;

    /// <summary>Następny krok z ostatniego zdarzenia postępu; null na ostatnim kroku, gdy handler go nie podał, i po końcu.</summary>
    public string? NextStage { get; private set; }

    /// <summary>„Dalej: …” obok „Krok N z M”; null, gdy <see cref="NextStage"/> jest pusty.</summary>
    public string? NextStageText => string.IsNullOrWhiteSpace(NextStage) ? null : GuiText.ActionNextStageText(NextStage!);

    public bool StopRequested { get; private set; }

    public bool IsFinished { get; private set; }

    /// <summary>Wynik silnika; null przed końcem, po odmowie lokalnej i po wyjątku.</summary>
    public TweakOutcome? Outcome { get; private set; }

    /// <summary>Treść wyniku: komunikat <see cref="TweakResult.Message"/> linia po linii i elementy nieprzywrócone.</summary>
    public IReadOnlyList<string> ResultLines => _resultLines;

    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Restart wymagany (silnik ustawia go wyłącznie przy <see cref="TweakOutcome.Executed"/>).</summary>
    public bool RequiresReboot { get; private set; }

    public string? RebootText => RequiresReboot ? GuiText.ActionRebootText : null;

    /// <summary>Zdarzenie postępu z handlera. Po <see cref="Complete"/>/<see cref="Fail"/> ignorowane.</summary>
    public void OnProgress(ActionProgress p)
    {
        if (p is null)
        {
            throw new ArgumentNullException(nameof(p));
        }

        if (IsFinished)
        {
            return;
        }

        Stage = p.Stage;
        Percent = p.Percent is { } percent ? Math.Max(0, Math.Min(100, percent)) : null;
        StepIndex = Math.Max(0, p.StepIndex);
        StepCount = Math.Max(0, p.StepCount);
        NextStage = p.NextStage;
        _stoppable = p.Stoppable;
        RaiseChanged();
    }

    /// <summary>„Zatrzymaj po tym kroku”: true, gdy prośba przyjęta (wołający anuluje token handlera).</summary>
    public bool RequestStop()
    {
        if (!CanStop)
        {
            return false;
        }

        StopRequested = true;
        RaiseChanged();
        return true;
    }

    /// <summary>Wynik z raportu sesji; raport bez <see cref="GuiOperationReport.Result"/> (odmowa lokalna) = <see cref="Fail"/> z jego linią.</summary>
    public void Complete(GuiOperationReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (report.Result is not { } result)
        {
            Fail(report.Lines.Count > 0 ? report.Lines[0].Text : "Brak wyniku akcji.");
            return;
        }

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            lines.AddRange(result.Message!.Split(LineBreaks, StringSplitOptions.None)
                .Select(line => line.TrimEnd())
                .Where(line => line.Length > 0));
        }

        lines.AddRange(result.NotRestored.Select(item => "Nie przywrócono: " + item));
        _resultLines = lines;
        _warnings = result.Warnings.ToList();
        Outcome = result.Outcome;
        RequiresReboot = result.Outcome == TweakOutcome.Executed && result.RequiresReboot;
        Title = IsFailure(result.Outcome) ? FinishedWithError : StopRequested && !result.IsSuccess ? Stopped : Finished;
        Subtitle = GuiText.OutcomeName(result.Outcome);
        Finish(result.IsSuccess ? 100 : null);
    }

    /// <summary>Awaria (wyjątek albo brak wyniku): komunikat jako treść wyniku.</summary>
    public void Fail(string message)
    {
        _resultLines = new[] { message ?? string.Empty };
        _warnings = Array.Empty<string>();
        Outcome = null;
        RequiresReboot = false;
        Title = FinishedWithError;
        Subtitle = GuiText.OutcomeName(TweakOutcome.Failed);
        Finish(null);
    }

    /// <summary>Wynik jako zwykły tekst (do schowka): nagłówek, pozycja, treść wyniku, ostrzeżenia, restart.</summary>
    public string ResultText()
    {
        var lines = new List<string> { string.Format(CultureInfo.InvariantCulture, "{0}: {1}", Title, Subtitle), _itemTitle };
        lines.AddRange(_resultLines.Select(line => "  " + line));
        lines.AddRange(_warnings);
        if (RebootText is not null)
        {
            lines.Add("Wymagany restart. " + RebootText);
        }

        return string.Join("\r\n", lines);
    }

    private static bool IsFailure(TweakOutcome outcome)
        => outcome is TweakOutcome.Failed or TweakOutcome.FailedRolledBack or TweakOutcome.FailedRollbackIncomplete or TweakOutcome.UndoIncomplete;

    private void Finish(int? percent)
    {
        Stage = _itemTitle;
        Percent = percent;
        NextStage = null;
        IsFinished = true;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
