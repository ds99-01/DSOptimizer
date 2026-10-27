namespace DSOptimizer.Core.Engine;

/// <summary>Sygnał „Zatrzymaj” dla serii: sprawdzany tylko między krokami, nigdy nie przerywa bieżącego kroku.</summary>
public sealed class BatchStop
{
    private volatile bool _requested;

    public bool IsRequested => _requested;

    /// <summary>Idempotentne, bezpieczne między wątkami.</summary>
    public void Request() => _requested = true;
}