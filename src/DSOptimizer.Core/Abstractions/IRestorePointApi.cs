using System.Collections.Generic;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Surowe wywołanie SystemRestore.CreateRestorePoint (WMI) i odczyt listy punktów.</summary>
public interface IRestorePointApi
{
    /// <summary>Zwraca kod: 0 = wywołanie przyjęte (nie gwarantuje, że punkt powstał – trzeba to potwierdzić na liście).</summary>
    uint CreateRestorePoint(string description);

    /// <summary>Lista punktów z root\default:SystemRestore; pusta, gdy brak punktów lub ochrona wyłączona.</summary>
    IReadOnlyList<RestorePointInfo> GetRestorePoints();
}
