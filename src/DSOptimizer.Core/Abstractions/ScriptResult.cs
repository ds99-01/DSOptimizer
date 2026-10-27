using System;
using System.Collections.Generic;
using DSOptimizer.Core.Scripts;

namespace DSOptimizer.Core.Abstractions;

public sealed class ScriptResult
{
    public required int ExitCode { get; init; }

    public IReadOnlyList<string> Output { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public string? LastLine => ScriptOutput.LastNonEmptyLine(Output);
}
