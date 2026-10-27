using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Gui;

/// <summary>
/// Ostrzeżenia (Warn) logu silnika zebrane w trakcie operacji okna. Po operacji zostają tylko te, których raport
/// nie powtarza (np. „Nie udało się przywrócić SystemRestorePointCreationFrequency”, ostrzeżenie journala z detekcji
/// odświeżającej) – pozostałe byłyby dublem linii raportu. Nic nie ginie: bez raportu (wyjątek) oddaje wszystko.
/// Bezpieczny dla wątków: TryAdd woła wątek silnika, Release wątek UI; po Release bufor jest zamknięty.
/// </summary>
public sealed class EngineWarningBuffer
{
    private static readonly HashSet<string> OutcomeNames = new(Enum.GetNames(typeof(TweakOutcome)), StringComparer.Ordinal);

    private readonly object _gate = new();
    private readonly List<GuiLogLine> _lines = new();
    private bool _released;

    /// <summary>False = bufor już oddany; linię trzeba pokazać od razu.</summary>
    public bool TryAdd(GuiLogLine line)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        lock (_gate)
        {
            if (_released)
            {
                return false;
            }

            _lines.Add(line);
            return true;
        }
    }

    /// <summary>
    /// Zamyka bufor i zwraca (w kolejności) ostrzeżenia, których <paramref name="report"/> nie powtarza.
    /// <paramref name="report"/> null (operacja rzuciła) = wszystkie. Drugie wywołanie zwraca pustą listę.
    /// </summary>
    public IReadOnlyList<GuiLogLine> Release(GuiOperationReport? report, IReadOnlyCollection<string> operationIds)
    {
        if (operationIds is null)
        {
            throw new ArgumentNullException(nameof(operationIds));
        }

        List<GuiLogLine> lines;
        lock (_gate)
        {
            _released = true;
            lines = _lines.ToList();
            _lines.Clear();
        }

        return report is null
            ? lines
            : lines.Where(line => !IsRepeatedByReport(line.Text, report, operationIds)).ToList();
    }

    /// <summary>
    /// Powtórzone = linia wyniku silnika („id: Outcome” albo „id: Outcome – komunikat”) dla pozycji operacji
    /// (raport ma jej wynik po polsku) albo tekst zawarty w którejś linii raportu (punkt przywracania, journal).
    /// </summary>
    public static bool IsRepeatedByReport(string message, GuiOperationReport report, IReadOnlyCollection<string> operationIds)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (operationIds is null)
        {
            throw new ArgumentNullException(nameof(operationIds));
        }

        return operationIds.Any(id => IsFinishLine(message, id))
            || report.Lines.Any(line => line.Text.IndexOf(message, StringComparison.Ordinal) >= 0);
    }

    private static bool IsFinishLine(string message, string id)
    {
        var prefix = id + ": ";
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = message.Substring(prefix.Length);
        var end = rest.IndexOf(" – ", StringComparison.Ordinal);
        return OutcomeNames.Contains(end < 0 ? rest : rest.Substring(0, end));
    }
}
