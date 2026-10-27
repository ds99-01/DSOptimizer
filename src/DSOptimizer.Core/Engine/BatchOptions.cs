using System;

namespace DSOptimizer.Core.Engine;

public sealed class BatchOptions
{
    public RestorePointPolicy RestorePointPolicy { get; init; } = RestorePointPolicy.IfAvailable;

    /// <summary>Opcjonalny odbiornik zdarzeń Started/Finished każdego kroku serii.</summary>
    public IProgress<BatchStepEvent>? Steps { get; init; }

    /// <summary>Opcjonalny sygnał „Zatrzymaj”: pozostałe pozycje zostaną pominięte (sprawdzany tylko między krokami).</summary>
    public BatchStop? Stop { get; init; }

    public string RestorePointDescription { get; init; } = "DSOptimizer – przed zmianami";
}
