using System;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Operations;

public sealed class ServiceStartupOperation : IOperation
{
    public static readonly TimeSpan DefaultServiceTimeout = TimeSpan.FromSeconds(30);

    private readonly ServiceEntry _entry;
    private readonly IServiceManager _services;
    private readonly TimeSpan _serviceTimeout;

    public ServiceStartupOperation(ServiceEntry entry, IServiceManager services, TimeSpan serviceTimeout)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _serviceTimeout = serviceTimeout;
    }

    public OperationKind Kind => OperationKind.Service;

    public bool IsDetectOnly => false;

    public bool ParticipatesInDetection => true;

    public string Describe() => L.F("Usługa {0}", _entry.Name);

    public OperationState Detect(CancellationToken cancellationToken)
    {
        var query = _services.Query(_entry.Name);
        return query.Status switch
        {
            ReadStatus.Found => query.StartType == _entry.StartType ? OperationState.Match : OperationState.Mismatch,
            ReadStatus.NotFound => OperationState.NotApplicable,
            _ => OperationState.Unknown,
        };
    }

    public SnapshotRecord Capture(CancellationToken cancellationToken)
    {
        var query = _services.Query(_entry.Name);
        return query.Status switch
        {
            ReadStatus.Found when query.StartType is ServiceStartType.Boot or ServiceStartType.System => throw new OperationException(
                OperationErrorKind.Unexpected,
                L.F("Usługa {0} ma typ startu {1} (sterownik) – zmiana nie byłaby odwracalna w tej wersji.", _entry.Name, query.StartType)),
            ReadStatus.Found => SnapshotRecord.ForService(_entry.Name, existed: true, query.StartType, query.IsRunning),
            ReadStatus.NotFound => SnapshotRecord.ForService(_entry.Name, existed: false, startType: null, wasRunning: false),
            ReadStatus.AccessDenied => throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak dostępu do usługi {0}: {1}", _entry.Name, query.Error)),
            _ => throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można odczytać usługi {0}: {1}", _entry.Name, query.Error)),
        };
    }

    public void Apply(CancellationToken cancellationToken)
    {
        var query = _services.Query(_entry.Name);
        if (query.Status == ReadStatus.NotFound)
        {
            return;
        }

        _services.SetStartType(_entry.Name, _entry.StartType);
        if (_entry.Stop && query.IsRunning)
        {
            _services.StopService(_entry.Name, _serviceTimeout);
        }
    }

    public void Restore(SnapshotRecord snapshot, CancellationToken cancellationToken)
    {
        if (snapshot is null || !Owns(snapshot))
        {
            throw new ArgumentException($"Snapshot nie należy do operacji {Describe()}.", nameof(snapshot));
        }

        if (!snapshot.ServiceExisted || snapshot.StartType is not { } startType)
        {
            return;
        }

        _services.SetStartType(_entry.Name, startType);
        if (snapshot.WasRunning)
        {
            var query = _services.Query(_entry.Name);
            if (query.Status == ReadStatus.Found && !query.IsRunning)
            {
                _services.StartService(_entry.Name, _serviceTimeout);
            }
        }
    }

    public SnapshotRecord? DefaultsSnapshot()
        => _entry.Original is not { } original
            ? null
            : SnapshotRecord.ForService(_entry.Name, existed: true, original, wasRunning: original is ServiceStartType.Automatic or ServiceStartType.AutomaticDelayed);

    public bool Owns(SnapshotRecord snapshot)
        => snapshot is not null
           && snapshot.Op == OperationKind.Service
           && string.Equals(snapshot.ServiceName, _entry.Name, StringComparison.OrdinalIgnoreCase);

    public PlannedChange Plan(CancellationToken cancellationToken)
    {
        var query = _services.Query(_entry.Name);
        var current = query.Status switch
        {
            ReadStatus.Found => query.IsRunning ? L.F("{0}, działa", query.StartType) : query.StartType.ToString(),
            ReadStatus.NotFound => L.T("(brak usługi)"),
            _ => L.T("(nieznany)"),
        };
        var target = _entry.Stop ? L.F("{0}, zatrzymana", _entry.StartType) : _entry.StartType.ToString();
        return new PlannedChange { Description = Describe(), Current = current, Target = target };
    }
}
