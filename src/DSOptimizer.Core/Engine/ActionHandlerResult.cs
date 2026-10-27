using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Engine;

/// <summary>Wynik handlera. Dozwolone <see cref="Outcome"/>: Executed, Failed, Refused, NotConfirmed, NotSupported.</summary>
public sealed class ActionHandlerResult
{
    public required TweakOutcome Outcome { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> NotRestored { get; init; } = Array.Empty<string>();

    /// <summary>Restart po akcji; liczy się tylko przy <see cref="TweakOutcome.Executed"/> (silnik dodatkowo uwzględnia <c>requiresReboot</c> z katalogu).</summary>
    public bool RequiresReboot { get; init; }
}
