using System;
using System.Collections.Generic;
using DSOptimizer.Core.Catalog;

namespace DSOptimizer.Core.Cli;

/// <summary>Wynik ładowania katalogu w trybie CLI: katalog albo lista błędów.</summary>
public sealed class CliCatalogResult
{
    public CliCatalogResult(LoadedCatalog? catalog, string source, IReadOnlyList<string> errors)
    {
        Catalog = catalog;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    /// <summary>Null, gdy są błędy.</summary>
    public LoadedCatalog? Catalog { get; }

    /// <summary>"builtin" albo pełna ścieżka pliku.</summary>
    public string Source { get; }

    public IReadOnlyList<string> Errors { get; }
}
