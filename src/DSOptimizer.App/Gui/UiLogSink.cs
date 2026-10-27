using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Dziennik widoczny w oknie. Jako <see cref="ILog"/> jest obserwatorem logu silnika (może pisać z dowolnego wątku),
/// więc dopisywanie zawsze idzie przez <c>Dispatcher.BeginInvoke</c> (asynchronicznie – nigdy nie blokuje wątku piszącego,
/// także gdy ten trzyma blokadę sesji). Trzyma najwyżej <see cref="MaxLines"/> najnowszych linii.
/// Z logu silnika pokazuje tylko ostrzeżenia i błędy (Info trafia wyłącznie do pliku przez TeeLog → FileLog).
/// Błędy (Fail) są widoczne zawsze i od razu. Ostrzeżenia silnika w czasie operacji (<see cref="BeginOperation"/>) czekają
/// w <see cref="EngineWarningBuffer"/>; po operacji pokazywane są te, których raport nie powtarza (raport ma po polsku
/// np. punkt przywracania albo „id: Failed – …”), a przy wyjątku wszystkie. Do pliku trafiają zawsze (TeeLog).
/// </summary>
public sealed class UiLogSink : ILog
{
    public const int MaxLines = 500;

    private readonly Dispatcher _dispatcher;
    private readonly IClock _clock;
    private Operation? _operation;

    public UiLogSink(Dispatcher dispatcher, IClock clock)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Zmieniana wyłącznie na wątku UI.</summary>
    public ObservableCollection<GuiLogLine> Lines { get; } = new();

    /// <summary>Linia raportu (z sesji albo okna) – pokazywana zawsze.</summary>
    public void Add(GuiLogLine line)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        _dispatcher.BeginInvoke(new Action(() => Append(line)));
    }

    public void Add(GuiLogLevel level, string text, string? path = null) => Add(new GuiLogLine(_clock.UtcNow, level, text, path));

    /// <summary>
    /// Od teraz ostrzeżenia (Warn) silnika czekają w buforze. <see cref="CompleteOperation"/> pokazuje te, których raport
    /// nie powtarza; Dispose zakresu pokazuje resztę (np. wszystkie, gdy operacja rzuciła przed raportem).
    /// Zakres otwarty przed Task.Run i zamknięty po await obejmuje całą operację. Jedna operacja naraz.
    /// </summary>
    public IDisposable BeginOperation(IReadOnlyCollection<string> operationIds)
    {
        if (operationIds is null)
        {
            throw new ArgumentNullException(nameof(operationIds));
        }

        var operation = new Operation(new EngineWarningBuffer(), operationIds);
        if (Interlocked.CompareExchange(ref _operation, operation, null) is not null)
        {
            throw new InvalidOperationException("Poprzednia operacja okna nie została zamknięta.");
        }

        return new OperationScope(this, operation);
    }

    /// <summary>Kończy buforowanie: do panelu idą ostrzeżenia silnika, których <paramref name="report"/> nie powtarza.</summary>
    public void CompleteOperation(GuiOperationReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (Volatile.Read(ref _operation) is { } operation)
        {
            End(operation, report);
        }
    }

    /// <summary>Info silnika (np. „Apply id: start”) idzie tylko do pliku – w panelu dublowałoby linie raportu.</summary>
    public void Info(string component, string message)
    {
    }

    public void Warn(string component, string message)
    {
        var line = new GuiLogLine(_clock.UtcNow, GuiLogLevel.Warning, message);
        // Bufor zamknięty w międzyczasie (TryAdd = false) = linia od razu do panelu, więc nic nie ginie.
        if (Volatile.Read(ref _operation) is not { } operation || !operation.Buffer.TryAdd(line))
        {
            Add(line);
        }
    }

    /// <summary>Błąd silnika jest widoczny zawsze, także w trakcie operacji (np. szczegóły wyjątku, błąd detekcji po operacji).</summary>
    public void Fail(string component, string message, Exception? exception = null)
    {
        var text = message;
        if (exception is not null)
        {
            text += $" ({exception.GetType().Name}: {exception.Message})";
        }

        Add(GuiLogLevel.Error, text);
    }

    private void End(Operation operation, GuiOperationReport? report)
    {
        Interlocked.CompareExchange(ref _operation, null, operation);
        foreach (var line in operation.Buffer.Release(report, operation.Ids))
        {
            Add(line);
        }
    }

    private void Append(GuiLogLine line)
    {
        Lines.Add(line);
        while (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
        }
    }

    private sealed class Operation
    {
        public Operation(EngineWarningBuffer buffer, IReadOnlyCollection<string> ids)
        {
            Buffer = buffer;
            Ids = ids;
        }

        public EngineWarningBuffer Buffer { get; }

        public IReadOnlyCollection<string> Ids { get; }
    }

    private sealed class OperationScope : IDisposable
    {
        private readonly Operation _operation;
        private UiLogSink? _owner;

        public OperationScope(UiLogSink owner, Operation operation)
        {
            _owner = owner;
            _operation = operation;
        }

        /// <summary>Po CompleteOperation bufor jest już pusty i zamknięty – Release zwraca wtedy pustą listę.</summary>
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End(_operation, null);
    }
}
