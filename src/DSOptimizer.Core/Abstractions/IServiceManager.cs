using System;
using System.Collections.Generic;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Usługi Windows. Odczyt nie rzuca; zapisy rzucają OperationException.</summary>
public interface IServiceManager
{
    ServiceQueryResult Query(string name);

    void SetStartType(string name, ServiceStartType startType);

    void StopService(string name, TimeSpan timeout);

    void StartService(string name, TimeSpan timeout);

    /// <summary>
    /// Działające (nie zatrzymane) usługi i sterowniki zależne od <paramref name="name"/>, bezpośrednio i pośrednio, w kolejności
    /// zatrzymywania (odwrotnej do startu). Brak zależnych = pusta lista. Błędy (np. brak usługi): OperationException.
    /// </summary>
    IReadOnlyList<string> GetRunningDependents(string name);

    /// <summary>
    /// Zatrzymuje usługę razem z jej działającymi zależnymi (najpierw zależne). W odróżnieniu od <see cref="StopService"/> nie odmawia,
    /// gdy zależne działają – wywołujący odpowiada za ich ponowne uruchomienie (lista z <see cref="GetRunningDependents"/>).
    /// Usługa już zatrzymana = nic do zrobienia. Błędy: OperationException.
    /// </summary>
    void StopServiceWithDependents(string name, TimeSpan timeout);
}
