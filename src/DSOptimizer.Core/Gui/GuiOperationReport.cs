using System;
using System.Collections.Generic;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Gui;

public sealed class GuiOperationReport
{
    public required string TweakId { get; init; }

    public required GuiOperation Operation { get; init; }

    /// <summary>Null = odmowa lokalna (silnik nie został wywołany).</summary>
    public TweakResult? Result { get; init; }

    public IReadOnlyList<GuiLogLine> Lines { get; init; } = Array.Empty<GuiLogLine>();

    /// <summary>Stany wszystkich pozycji kind=tweak po operacji (puste przy odmowie lokalnej).</summary>
    public IReadOnlyDictionary<string, DetectionState> Refreshed { get; init; } = new Dictionary<string, DetectionState>();

    public bool RequiresReboot { get; init; }

    /// <summary>Wynik serii z silnika (punkt przywracania, zatrzymanie, wyniki pozycji); ustawiany tylko przez <c>RunBatch</c>, przy odmowie null.</summary>
    public BatchResult? Batch { get; init; }

    /// <summary>Id pozycji, których własny wynik w serii był udany (<c>IsSuccess</c>); dla pojedynczej operacji puste.</summary>
    public IReadOnlyList<string> SucceededIds { get; init; } = Array.Empty<string>();
}