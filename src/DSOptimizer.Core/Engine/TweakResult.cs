using System;
using System.Collections.Generic;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Engine;

public sealed class TweakResult
{
    public required string TweakId { get; init; }

    public required TweakOutcome Outcome { get; init; }

    public DetectionState? FinalState { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Elementy, których nie udało się przywrócić (rollback/undo).</summary>
    public IReadOnlyList<string> NotRestored { get; init; } = Array.Empty<string>();

    public bool RequiresReboot { get; init; }

    public bool IsSuccess => Outcome is TweakOutcome.Applied or TweakOutcome.AlreadyApplied or TweakOutcome.PendingReboot
        or TweakOutcome.Undone or TweakOutcome.UndoneFromDefaults or TweakOutcome.Executed or TweakOutcome.Removed
        or TweakOutcome.Installed or TweakOutcome.AlreadyInstalled;
}
