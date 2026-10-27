using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using DSOptimizer.Core.Catalog;

namespace DSOptimizer.Core.Cli;

/// <summary>Ładuje katalog dla trybu CLI: wbudowany albo zewnętrzny (zastępuje wbudowany), z tymi samymi walidacjami.</summary>
public static class CliCatalog
{
    public const string BuiltInSource = "builtin";

    public static CliCatalogResult Load(string? externalPath, Func<string, string> readAllText, IReadOnlyList<CatalogSource> builtInSources)
    {
        if (readAllText is null)
        {
            throw new ArgumentNullException(nameof(readAllText));
        }

        if (builtInSources is null)
        {
            throw new ArgumentNullException(nameof(builtInSources));
        }

        if (externalPath is null)
        {
            return LoadAndValidate(builtInSources, BuiltInSource);
        }

        var fullPath = externalPath;
        string json;
        try
        {
            fullPath = Path.GetFullPath(externalPath);
            json = readAllText(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
        {
            return Failed(fullPath, $"Nie można odczytać katalogu {fullPath}: {ex.Message}");
        }

        return LoadAndValidate(new[] { new CatalogSource(Path.GetFileName(fullPath), json) }, fullPath);
    }

    private static CliCatalogResult LoadAndValidate(IReadOnlyList<CatalogSource> sources, string source)
    {
        LoadedCatalog catalog;
        try
        {
            catalog = CatalogLoader.Load(sources);
        }
        catch (CatalogLoadException ex)
        {
            return Failed(source, ex.Message);
        }

        var validation = CatalogValidator.Validate(catalog.Definitions);
        if (validation.Count > 0)
        {
            return new CliCatalogResult(null, source, validation.Select(error => error.ToString()).ToList());
        }

        return new CliCatalogResult(catalog, source, Array.Empty<string>());
    }

    private static CliCatalogResult Failed(string source, string message)
        => new(null, source, new[] { message });
}
