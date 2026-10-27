using System;
using System.Collections.Generic;
using System.Threading;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Narzędzia System32 bez uruchamiania procesów: zapisuje żądania, wynik z <see cref="OnRun"/> (domyślnie kod 0).</summary>
public sealed class FakeToolRunner : IToolRunner
{
    public List<ToolRequest> Calls { get; } = new();

    /// <summary>Dostaje każde wywołanie (wspólny dziennik kolejności z innymi fake'ami).</summary>
    public Action<string>? Observer { get; set; }

    public Func<ToolRequest, ToolResult> OnRun { get; set; } = _ => new ToolResult { ExitCode = 0 };

    public ToolResult Run(ToolRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        Observer?.Invoke($"Tool:{request.FileName} {request.Arguments}");
        return OnRun(request);
    }
}
