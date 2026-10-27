using System.Collections.Generic;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Wynik narzędzia: kod wyjścia i linie wyjścia (stdout i stderr w kolejności).</summary>
public sealed class ToolResult
{
    public int ExitCode { get; init; }

    public IReadOnlyList<string> Output { get; init; } = new List<string>();
}
