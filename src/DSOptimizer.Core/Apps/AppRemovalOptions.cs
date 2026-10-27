using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Apps;

/// <summary>Opcje serii usuwania aplikacji.</summary>
public sealed class AppRemovalOptions
{
    /// <summary>Przełącznik punktu przywracania z Ustawień; dotyczy tylko serii z samymi aplikacjami ze Sklepu.</summary>
    public bool RestorePointSetting { get; init; }

    /// <summary>
    /// Punkt wymagany także dla serii samych aplikacji ze Sklepu (CLI <c>--restore-point required</c>, Ruling R20): punkt powstaje
    /// zawsze, a gdy nie powstanie, seria pyta jak przy programie. GUI tego nie ustawia (działa według przełącznika z Ustawień).
    /// </summary>
    public bool RestorePointRequired { get; init; }

    /// <summary>
    /// Pytanie „Kontynuować bez punktu przywracania?”, gdy w serii jest program Win32 (albo <see cref="RestorePointRequired"/>), a punkt nie powstał.
    /// false = seria przerwana przed pierwszym usunięciem. Domyślnie „Nie”.
    /// </summary>
    public Func<RestorePointResult, bool> ContinueWithoutRestorePoint { get; init; } = _ => false;

    /// <summary>Opcjonalny odbiornik zdarzeń Started/Finished każdego kroku.</summary>
    public IProgress<BatchStepEvent>? Steps { get; init; }

    /// <summary>Opcjonalny sygnał „Zatrzymaj”: sprawdzany przed każdą pozycją, nigdy w jej trakcie.</summary>
    public BatchStop? Stop { get; init; }
}
