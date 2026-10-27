using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Długie narzędzie w pamięci: zapisuje wywołania, podaje linie do <c>onLine</c> i zwraca wynik z <see cref="Handler"/>.</summary>
public sealed class FakeServicingToolRunner : IServicingToolRunner
{
    public List<(ToolRequest Request, TimeSpan WaitLimit)> Calls { get; } = new();

    /// <summary>Wynik dla żądania; domyślnie kod 0 bez wyjścia.</summary>
    public Func<ToolRequest, ServicingToolRun> Handler { get; set; } = _ => new ServicingToolRun(0, false, Array.Empty<string>());

    public ServicingToolRun Run(ToolRequest request, TimeSpan waitLimit, Action<string>? onLine)
    {
        Calls.Add((request, waitLimit));
        var result = Handler(request);
        foreach (var line in result.Output)
        {
            onLine?.Invoke(line);
        }

        return result;
    }
}
