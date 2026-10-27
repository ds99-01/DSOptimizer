using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Operations;

/// <summary>Stan jednej operacji przed zmianą — zapisywany w journalu i używany przy rollbacku/undo.</summary>
public sealed class SnapshotRecord
{
    public required OperationKind Op { get; init; }

    public string? Path { get; init; }

    public string? Name { get; init; }

    public RegValue? Value { get; init; }

    public string? ServiceName { get; init; }

    public bool ServiceExisted { get; init; }

    public ServiceStartType? StartType { get; init; }

    public bool WasRunning { get; init; }

    public string? Capture { get; init; }

    /// <summary>
    /// Najwyższy klucz na ścieżce wpisu, którego nie było w chwili Capture (do posprzątania przy undo/rollbacku).
    /// Null: klucz istniał albo stan nieznany (też dla starych journali).
    /// </summary>
    public string? CreatedKeyRoot { get; init; }

    public static SnapshotRecord ForRegistry(string path, string name, RegValue value, string? createdKeyRoot = null)
        => new() { Op = OperationKind.Registry, Path = path, Name = name, Value = value, CreatedKeyRoot = createdKeyRoot };

    public static SnapshotRecord ForService(string name, bool existed, ServiceStartType? startType, bool wasRunning)
        => new() { Op = OperationKind.Service, ServiceName = name, ServiceExisted = existed, StartType = startType, WasRunning = wasRunning };

    public static SnapshotRecord ForScript(string? capture)
        => new() { Op = OperationKind.Script, Capture = capture };

    public string Describe() => Op switch
    {
        OperationKind.Registry => $"{Path}\\{Name} = {(Value ?? RegValue.Absent).ToDisplayString()}",
        OperationKind.Service => ServiceExisted
            ? $"Usługa {ServiceName}: {StartType}{(WasRunning ? ", działała" : string.Empty)}"
            : $"Usługa {ServiceName}: (brak usługi)",
        _ => $"Skrypt: {Capture ?? "(brak snapshotu)"}",
    };
}
