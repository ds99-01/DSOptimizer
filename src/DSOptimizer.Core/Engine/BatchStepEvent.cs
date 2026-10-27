using System;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Engine;

/// <summary>Rodzaj kroku serii: punkt przywracania albo pozycja katalogu.</summary>
public enum BatchStepKind
{
    RestorePoint,
    Item,
}

/// <summary>Faza kroku serii: początek, postęp w trakcie (etap i procent, np. instalacja winget) albo koniec (z wynikiem).</summary>
public enum BatchStepPhase
{
    Started,
    Finished,

    /// <summary>Postęp bieżącej pozycji: <see cref="BatchStepEvent.Stage"/> i/lub <see cref="BatchStepEvent.Percent"/>, wysyłany tylko przy zmianie.</summary>
    Progress,
}

/// <summary>
/// Zdarzenie postępu serii. Index od 0, Count = wszystkie kroki (punkt przywracania liczony tylko przy polityce innej niż Skip).
/// <see cref="Stage"/> i <see cref="Percent"/> niesie tylko faza <see cref="BatchStepPhase.Progress"/> (procent 0–100 albo null, gdy nieznany).
/// </summary>
public sealed record BatchStepEvent(
    int Index,
    int Count,
    BatchStepKind Kind,
    string? TweakId,
    BatchStepPhase Phase,
    DateTimeOffset At,
    TweakResult? Result = null,
    RestorePointResult? RestorePoint = null,
    string? Stage = null,
    int? Percent = null);