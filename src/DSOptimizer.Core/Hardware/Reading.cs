namespace DSOptimizer.Core.Hardware;

public enum ReadingState
{
    Pending,
    Done,
    Failed,
}

/// <summary>Wynik odczytu jednej części sprzętu: trwa (<see cref="Pending"/>), udał się albo się nie udał.</summary>
public sealed record Reading<T>(ReadingState State, T? Value)
{
#pragma warning disable CA1000 // Kontrakt zadania: fabryki statyczne na typie generycznym.
    public static Reading<T> Pending => new(ReadingState.Pending, default);

    public static Reading<T> Failed => new(ReadingState.Failed, default);

    public static Reading<T> Done(T value) => new(ReadingState.Done, value);
#pragma warning restore CA1000
}
