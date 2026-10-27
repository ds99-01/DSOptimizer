using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>
/// winget w pamięci (nigdy prawdziwy proces): zapisuje każde wywołanie, a wynik bierze z kolejki <see cref="Enqueue"/>
/// (pusta kolejka = <see cref="Default"/>). Linie wyniku idą po kolei do <c>onLine</c> (np. próbka <c>spike-*</c> z VM).
/// </summary>
public sealed class FakeWingetRunner : IWingetRunner
{
    private readonly Queue<Func<string, WingetRun>> _results = new();

    public List<(string Arguments, TimeSpan WaitLimit, bool HasOnLine)> Calls { get; } = new();

    /// <summary>Wynik, gdy kolejka jest pusta; domyślnie kod 0 bez wyjścia.</summary>
    public Func<string, WingetRun> Default { get; set; } = _ => Exit(0);

    /// <summary>Wywoływane przed zwróceniem wyniku (np. do sprawdzenia journala w chwili startu winget).</summary>
    public Action<string>? OnRun { get; set; }

    public FakeWingetRunner Enqueue(WingetRun result) => Enqueue(_ => result);

    public FakeWingetRunner Enqueue(Func<string, WingetRun> result)
    {
        _results.Enqueue(result);
        return this;
    }

    public static WingetRun Exit(int code, params string[] lines) => new(code, false, lines);

    public static WingetRun Exit(uint code, params string[] lines) => new(unchecked((int)code), false, lines);

    public static WingetRun TimedOut(params string[] lines) => new(null, true, lines);

    public WingetRun Run(string arguments, TimeSpan waitLimit, Action<string>? onLine)
    {
        Calls.Add((arguments, waitLimit, onLine is not null));
        OnRun?.Invoke(arguments);
        var result = (_results.Count > 0 ? _results.Dequeue() : Default)(arguments);
        foreach (var line in result.Output)
        {
            onLine?.Invoke(line);
        }

        return result;
    }
}
