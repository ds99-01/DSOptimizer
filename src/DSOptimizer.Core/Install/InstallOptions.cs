using System;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Install;

/// <summary>Opcje serii instalacji aplikacji (bez punktu przywracania, spec 5.4).</summary>
public sealed class InstallOptions
{
    /// <summary>Opcjonalny odbiornik zdarzeń Started/Progress/Finished każdej pozycji.</summary>
    public IProgress<BatchStepEvent>? Steps { get; init; }

    /// <summary>Opcjonalny sygnał „Zatrzymaj po tej aplikacji”: sprawdzany przed każdą pozycją, nigdy w trakcie instalacji.</summary>
    public BatchStop? Stop { get; init; }

    /// <summary>Jak długo najwyżej czekać na instalację jednej aplikacji.</summary>
    public TimeSpan PerAppLimit { get; init; } = InstallTimeouts.PerApp;
}
