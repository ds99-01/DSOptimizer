using System;

namespace DSOptimizer.Core.Catalog;

/// <summary>Jeden plik katalogu: nazwa (do komunikatów błędów i hasha) + treść JSON.</summary>
public sealed class CatalogSource
{
    public CatalogSource(string name, string json)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Json = json ?? throw new ArgumentNullException(nameof(json));
    }

    public string Name { get; }

    public string Json { get; }
}
