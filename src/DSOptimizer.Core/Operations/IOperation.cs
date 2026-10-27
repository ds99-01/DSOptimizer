using System.Threading;

namespace DSOptimizer.Core.Operations;

/// <summary>Jedna atomowa zmiana systemu ze wspólnym kontraktem snapshot/detect/apply/restore.</summary>
public interface IOperation
{
    OperationKind Kind { get; }

    /// <summary>Tylko weryfikuje stan — silnik nie robi dla niej Capture/Apply/Restore.</summary>
    bool IsDetectOnly { get; }

    bool ParticipatesInDetection { get; }

    string Describe();

    /// <summary>Nie rzuca — problemy zwraca jako Unknown.</summary>
    OperationState Detect(CancellationToken cancellationToken);

    /// <summary>Rzuca OperationException, gdy stanu nie da się odczytać.</summary>
    SnapshotRecord Capture(CancellationToken cancellationToken);

    void Apply(CancellationToken cancellationToken);

    void Restore(SnapshotRecord snapshot, CancellationToken cancellationToken);

    /// <summary>Snapshot z wartości domyślnych katalogu (undo bez journala); null = brak danych.</summary>
    SnapshotRecord? DefaultsSnapshot();

    bool Owns(SnapshotRecord snapshot);

    PlannedChange Plan(CancellationToken cancellationToken);
}
