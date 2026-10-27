using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Tests.Unit.Fakes;

public sealed class FakeServiceManager : IServiceManager
{
    private readonly Dictionary<string, (ServiceStartType StartType, bool Running)> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deniedQueries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OperationErrorKind> _failingSets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OperationErrorKind> _failingStops = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (OperationErrorKind Kind, bool RunsAnyway)> _failingStarts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _dependents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _startsWith = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failingDependentQueries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zmieniające wywołania w kolejności: SetStartType/StopService/StopServiceWithDependents/StartService.</summary>
    public List<string> MutationCalls { get; } = new();

    /// <summary>Dostaje każde zmieniające wywołanie (wspólny dziennik kolejności z innymi fake'ami).</summary>
    public Action<string>? Observer { get; set; }

    public void Add(string name, ServiceStartType startType, bool running) => _services[name] = (startType, running);

    public ServiceStartType StartTypeOf(string name) => _services[name].StartType;

    public bool IsRunning(string name) => _services[name].Running;

    public void DenyQuery(string name) => _deniedQueries.Add(name);

    public void FailSetStartType(string name, OperationErrorKind kind = OperationErrorKind.AccessDenied) => _failingSets[name] = kind;

    public void FailStop(string name, OperationErrorKind kind = OperationErrorKind.Timeout) => _failingStops[name] = kind;

    /// <summary>Start rzuca; <paramref name="runsAnyway"/> = usługa i tak działa po wywołaniu (np. „już działa” dla sterownika).</summary>
    public void FailStart(string name, bool runsAnyway = false, OperationErrorKind kind = OperationErrorKind.Timeout)
        => _failingStarts[name] = (kind, runsAnyway);

    /// <summary>Zależne usługi (bezpośrednie i pośrednie) w kolejności zatrzymywania, jak EnumDependentServices.</summary>
    public void SetDependents(string name, params string[] dependentsInStopOrder) => _dependents[name] = new List<string>(dependentsInStopOrder);

    /// <summary>Start <paramref name="name"/> uruchamia też <paramref name="others"/> (np. AppIDSvc uruchamia sterownik applockerfltr).</summary>
    public void StartsAlong(string name, params string[] others) => _startsWith[name] = new List<string>(others);

    public void FailDependentsQuery(string name) => _failingDependentQueries.Add(name);

    public ServiceQueryResult Query(string name)
    {
        if (_deniedQueries.Contains(name))
        {
            return ServiceQueryResult.AccessDenied("Odmowa dostępu (fake).");
        }

        return _services.TryGetValue(name, out var service)
            ? ServiceQueryResult.Found(service.StartType, service.Running)
            : ServiceQueryResult.NotFound();
    }

    public void SetStartType(string name, ServiceStartType startType)
    {
        Record($"SetStartType:{name}:{startType}");
        Require(name);
        if (_failingSets.TryGetValue(name, out var kind))
        {
            throw new OperationException(kind, $"Zmiana typu startu {name} odrzucona (fake).");
        }

        _services[name] = (startType, _services[name].Running);
    }

    public void StopService(string name, TimeSpan timeout)
    {
        Record($"StopService:{name}");
        Require(name);
        if (_failingStops.TryGetValue(name, out var kind))
        {
            throw new OperationException(kind, $"Zatrzymanie {name} nie powiodło się (fake).");
        }

        _services[name] = (_services[name].StartType, false);
    }

    public void StartService(string name, TimeSpan timeout)
    {
        Record($"StartService:{name}");
        Require(name);
        if (_failingStarts.TryGetValue(name, out var failure))
        {
            if (failure.RunsAnyway)
            {
                _services[name] = (_services[name].StartType, true);
            }

            throw new OperationException(failure.Kind, $"Uruchomienie {name} nie powiodło się (fake).");
        }

        _services[name] = (_services[name].StartType, true);
        if (_startsWith.TryGetValue(name, out var others))
        {
            foreach (var other in others)
            {
                _services[other] = (_services[other].StartType, true);
            }
        }
    }

    public IReadOnlyList<string> GetRunningDependents(string name)
    {
        Require(name);
        if (_failingDependentQueries.Contains(name))
        {
            throw new OperationException(OperationErrorKind.AccessDenied, $"Odczyt zależnych {name} odrzucony (fake).");
        }

        var running = new List<string>();
        if (_dependents.TryGetValue(name, out var dependents))
        {
            foreach (var dependent in dependents)
            {
                if (_services.TryGetValue(dependent, out var service) && service.Running)
                {
                    running.Add(dependent);
                }
            }
        }

        return running;
    }

    /// <summary>Zatrzymuje działające zależne po kolei, potem samą usługę; błąd zależnej przerywa w połowie (jak ServiceController.Stop).</summary>
    public void StopServiceWithDependents(string name, TimeSpan timeout)
    {
        Record($"StopServiceWithDependents:{name}");
        Require(name);
        foreach (var dependent in GetRunningDependents(name))
        {
            StopOne(dependent);
        }

        StopOne(name);
    }

    private void StopOne(string name)
    {
        if (_failingStops.TryGetValue(name, out var kind))
        {
            throw new OperationException(kind, $"Zatrzymanie {name} nie powiodło się (fake).");
        }

        _services[name] = (_services[name].StartType, false);
    }

    private void Record(string call)
    {
        MutationCalls.Add(call);
        Observer?.Invoke(call);
    }

    private void Require(string name)
    {
        if (!_services.ContainsKey(name))
        {
            throw new OperationException(OperationErrorKind.NotFound, $"Brak usługi {name} (fake).");
        }
    }
}
