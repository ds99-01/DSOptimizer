using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Czytnik autostartu zwracający przygotowany odczyt (albo wyjątek) zamiast prawdziwego autostartu hosta; zapamiętuje konteksty wywołań.
/// </summary>
public sealed class FakeAutostartReader : IAutostartReader
{
    public FakeAutostartReader(AutostartReading? reading = null)
    {
        Reading = reading ?? new AutostartReading(
            Array.Empty<AutostartEntry>(),
            UserEnvironment.FromVariables(new Dictionary<string, string>()),
            null,
            Array.Empty<string>());
    }

    public AutostartReading Reading { get; set; }

    public Exception? Failure { get; set; }

    public List<AutostartContext> Contexts { get; } = new();

    public AutostartReading Read(AutostartContext context)
    {
        Contexts.Add(context);
        if (Failure is not null)
        {
            throw Failure;
        }

        return Reading;
    }
}
