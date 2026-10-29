using System;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Operations;

public sealed class RegistryValueOperation : IOperation
{
    private readonly RegistryEntry _entry;
    private readonly IRegistry _registry;

    public RegistryValueOperation(RegistryEntry entry, IRegistry registry)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public OperationKind Kind => OperationKind.Registry;

    public bool IsDetectOnly => _entry.DetectOnly;

    public bool ParticipatesInDetection => true;

    public string Describe() => $"{_entry.Path}\\{_entry.Name}";

    public OperationState Detect(CancellationToken cancellationToken)
    {
        var read = _registry.Read(_entry.Path, _entry.Name);
        if (read.Status is not (ReadStatus.Found or ReadStatus.NotFound))
        {
            return OperationState.Unknown;
        }

        return Accepts(read.Value) ? OperationState.Match : OperationState.Mismatch;
    }

    /// <summary>
    /// Wartość, którą zapisuje <see cref="Apply"/> i pokazuje podgląd: przy <see cref="RegistryEntry.Localized"/> tekst z katalogu
    /// w bieżącym języku programu (w chwili wywołania), inaczej dosłowna wartość z katalogu.
    /// </summary>
    private RegValue TargetValue()
        => LocalizedText() is { } polish ? RegValue.FromString(L.Data(polish)) : _entry.Value;

    /// <summary>Przy <see cref="RegistryEntry.Localized"/> pasuje tekst typu String w każdym języku interfejsu, inaczej tylko dokładna wartość.</summary>
    private bool Accepts(RegValue value)
        => LocalizedText() is { } polish
            ? L.AllLanguages(() => L.Data(polish)).Any(text => value.Equals(RegValue.FromString(text)))
            : value.Equals(_entry.Value);

    private string? LocalizedText()
        => _entry.Localized && _entry.Value.Exists && _entry.Value.Type == RegistryValueType.String ? _entry.Value.Data as string : null;

    public SnapshotRecord Capture(CancellationToken cancellationToken)
    {
        var read = _registry.Read(_entry.Path, _entry.Name);
        return read.Status switch
        {
            ReadStatus.Found => SnapshotRecord.ForRegistry(_entry.Path, _entry.Name, read.Value),
            ReadStatus.NotFound => SnapshotRecord.ForRegistry(_entry.Path, _entry.Name, read.Value, FindMissingKeyRoot()),
            ReadStatus.AccessDenied => throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak dostępu do odczytu {0}: {1}", Describe(), read.Error)),
            _ => throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można odczytać {0}: {1}", Describe(), read.Error)),
        };
    }

    public void Apply(CancellationToken cancellationToken) => _registry.Write(_entry.Path, _entry.Name, TargetValue());

    public void Restore(SnapshotRecord snapshot, CancellationToken cancellationToken)
    {
        if (snapshot is null || !Owns(snapshot))
        {
            throw new ArgumentException($"Snapshot nie należy do operacji {Describe()}.", nameof(snapshot));
        }

        var value = snapshot.Value ?? RegValue.Absent;
        _registry.Write(_entry.Path, _entry.Name, value);
        if (!value.Exists && snapshot.CreatedKeyRoot is { } createdRoot)
        {
            try
            {
                DeleteCreatedKeys(createdRoot);
            }
            catch (OperationException)
            {
                // Sprzątanie jest best-effort: wartość jest już przywrócona, a pozostały klucz zgłosi VerifyRestored jako ostrzeżenie.
            }
        }
    }

    /// <summary>Najwyższy brakujący klucz na ścieżce wpisu (nie wyżej niż 2 segmenty pod korzeniem hive); null przy niepewności.</summary>
    private string? FindMissingKeyRoot()
    {
        if (_registry.KeyStatus(_entry.Path) != ReadStatus.NotFound || !RegistryKeyPaths.IsDeletable(_entry.Path))
        {
            return null;
        }

        var top = _entry.Path;
        while (RegistryKeyPaths.Parent(top) is { } parent && RegistryKeyPaths.IsDeletable(parent))
        {
            var status = _registry.KeyStatus(parent);
            if (status == ReadStatus.Found)
            {
                break;
            }

            if (status != ReadStatus.NotFound)
            {
                return null;
            }

            top = parent;
        }

        return top;
    }

    /// <summary>Kasuje puste klucze od ścieżki wpisu w górę do <paramref name="createdRoot"/> włącznie; pierwszy niepusty przerywa.</summary>
    private void DeleteCreatedKeys(string createdRoot)
    {
        if (!RegistryKeyPaths.IsDeletable(createdRoot) || !RegistryKeyPaths.IsSelfOrAncestor(createdRoot, _entry.Path))
        {
            return;
        }

        var depth = RegistryKeyPaths.Segments(createdRoot).Length;
        var current = _entry.Path;
        while (RegistryKeyPaths.Segments(current).Length >= depth)
        {
            if (!_registry.DeleteKeyIfEmpty(current))
            {
                return;
            }

            if (RegistryKeyPaths.Parent(current) is not { } parent)
            {
                return;
            }

            current = parent;
        }
    }

    public SnapshotRecord? DefaultsSnapshot()
        => _entry.Original is null ? null : SnapshotRecord.ForRegistry(_entry.Path, _entry.Name, _entry.Original);

    public bool Owns(SnapshotRecord snapshot)
        => snapshot is not null
           && snapshot.Op == OperationKind.Registry
           && string.Equals(snapshot.Path, _entry.Path, StringComparison.OrdinalIgnoreCase)
           && string.Equals(snapshot.Name, _entry.Name, StringComparison.OrdinalIgnoreCase);

    public PlannedChange Plan(CancellationToken cancellationToken)
    {
        var read = _registry.Read(_entry.Path, _entry.Name);
        var current = read.Status is ReadStatus.Found or ReadStatus.NotFound
            ? read.Value.ToDisplayString()
            : L.F("(nieznana: {0})", read.Status);
        return new PlannedChange { Description = Describe(), Current = current, Target = TargetValue().ToDisplayString() };
    }
}
