using System;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Autostart;

/// <summary>Opcje serii autostartu: odbiornik zdarzeń postępu i sygnał „Zatrzymaj” (sprawdzany tylko między pozycjami).</summary>
public sealed class AutostartRunOptions
{
    public IProgress<BatchStepEvent>? Steps { get; init; }

    public BatchStop? Stop { get; init; }
}
