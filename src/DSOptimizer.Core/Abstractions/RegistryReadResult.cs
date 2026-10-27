using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Wynik odczytu wartości rejestru. Odczyt nigdy nie rzuca — „brak” i „brak uprawnień” to różne stany.</summary>
public sealed class RegistryReadResult
{
    private RegistryReadResult(ReadStatus status, RegValue value, string? error)
    {
        Status = status;
        Value = value;
        Error = error;
    }

    public ReadStatus Status { get; }

    /// <summary>Odczytana wartość; <see cref="RegValue.Absent"/> dla każdego statusu innego niż Found.</summary>
    public RegValue Value { get; }

    public string? Error { get; }

    public static RegistryReadResult Found(RegValue value) => new(ReadStatus.Found, value, null);

    public static RegistryReadResult NotFound() => new(ReadStatus.NotFound, RegValue.Absent, null);

    public static RegistryReadResult AccessDenied(string error) => new(ReadStatus.AccessDenied, RegValue.Absent, error);

    public static RegistryReadResult Failed(string error) => new(ReadStatus.Error, RegValue.Absent, error);
}
