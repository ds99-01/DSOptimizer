using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Engine;

public sealed class BatchResult
{
    public IReadOnlyList<TweakResult> Results { get; init; } = Array.Empty<TweakResult>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Wynik tworzenia punktu przywracania; null przy RestorePointPolicy.Skip.</summary>
    public RestorePointResult? RestorePoint { get; init; }

    public bool Aborted { get; init; }

    public string? AbortReason { get; init; }

    /// <summary>True, gdy sygnał Stop pominął co najmniej jedną pozycję.</summary>
    public bool Stopped { get; init; }

    public int SucceededCount => Results.Count(result => result.IsSuccess);

    /// <summary>Nieudane; <see cref="TweakOutcome.NotConfirmed"/> też (nigdy „wykonano” bez sprawdzenia, Ruling R3).</summary>
    public int FailedCount => Results.Count(result => result.Outcome is TweakOutcome.Failed or TweakOutcome.FailedRolledBack
        or TweakOutcome.FailedRollbackIncomplete or TweakOutcome.UndoIncomplete or TweakOutcome.NotConfirmed);

    public int SkippedCount => Results.Count(result => result.Outcome == TweakOutcome.Skipped);

    public bool RequiresReboot => Results.Any(result => result.RequiresReboot);
}
