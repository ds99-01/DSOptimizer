using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Repair;

/// <summary>
/// Zatrzymanie usług na czas zmiany i przywrócenie ich w odwrotnej kolejności – wspólne dla resetu Windows Update
/// (<see cref="WindowsUpdateReset"/>) i kroku „Pobrane aktualizacje” czyszczenia dysku (<see cref="DiskCleanupHandler"/>).
/// Każda zatrzymywana usługa (i jej działające zależne) trafia na listę do ponownego startu jeszcze przed próbą zatrzymania,
/// więc nieudane zatrzymanie też kończy się próbą startu. O przywróceniu decyduje odczyt stanu po starcie, nie kod Start
/// („już działa” to nie błąd). Wywołania usług idą przez <c>attempt</c> wywołującego (komunikat błędu zamiast wyjątku).
/// </summary>
public sealed class ServiceStopRestore
{
    private readonly IServiceManager _services;
    private readonly ILog _log;
    private readonly string _component;
    private readonly TimeSpan _timeout;
    private readonly Func<Action, string?> _attempt;

    /// <summary>Usługi do ponownego startu, w kolejności zatrzymywania; start w odwrotnej.</summary>
    private readonly List<string> _restart = new();

    public ServiceStopRestore(IServiceManager services, ILog log, string component, TimeSpan timeout, Func<Action, string?> attempt)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _component = component;
        _timeout = timeout;
        _attempt = attempt ?? throw new ArgumentNullException(nameof(attempt));
    }

    /// <summary>
    /// Zatrzymuje działającą usługę <paramref name="name"/>; z <paramref name="withDependents"/> razem z działającymi zależnymi
    /// (lista z systemu, zwracana w <paramref name="dependents"/>). Zwraca komunikat błędu albo <c>null</c>.
    /// </summary>
    public string? Stop(string name, bool withDependents, out IReadOnlyList<string> dependents)
    {
        dependents = Array.Empty<string>();
        if (!withDependents)
        {
            _restart.Add(name);
            return _attempt(() => _services.StopService(name, _timeout));
        }

        IReadOnlyList<string> found = Array.Empty<string>();
        var error = _attempt(() => found = _services.GetRunningDependents(name));
        if (error is not null)
        {
            return error;
        }

        dependents = found;
        _restart.AddRange(found);
        _restart.Add(name);
        return _attempt(() => _services.StopServiceWithDependents(name, _timeout));
    }

    /// <summary>Uruchamia zatrzymane w odwrotnej kolejności i czyści listę; wynik z odczytu stanu po każdym starcie.</summary>
    public ServiceRestoreResult Restore()
    {
        var failed = new List<ServiceRestoreFailure>();
        var started = new List<string>();
        for (var i = _restart.Count - 1; i >= 0; i--)
        {
            var name = _restart[i];
            var error = _attempt(() => _services.StartService(name, _timeout));
            var state = _services.Query(name);
            if (state.Status == ReadStatus.Found && state.IsRunning)
            {
                started.Add(name);
                if (error is not null)
                {
                    _log.Info(_component, L.F("Start {0} zgłosił błąd ({1}), ale usługa działa – przywrócona.", name, error));
                }

                continue;
            }

            failed.Add(new ServiceRestoreFailure(name, error ?? state.Error ?? L.T("brak stanu Running")));
        }

        _restart.Clear();
        return new ServiceRestoreResult(started, failed);
    }
}

/// <summary>Wynik <see cref="ServiceStopRestore.Restore"/>: uruchomione ponownie i te, które po starcie nie działają.</summary>
public sealed record ServiceRestoreResult(IReadOnlyList<string> Started, IReadOnlyList<ServiceRestoreFailure> Failed);

/// <summary>Usługa, która nie działa po przywróceniu, z surowym powodem (błąd startu albo odczytu).</summary>
public sealed record ServiceRestoreFailure(string Name, string Reason);
