using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DSOptimizer.Core.Catalog;

namespace DSOptimizer.Catalog;

/// <summary>Wbudowany katalog: pliki <c>catalog\*.json</c> osadzone w assembly jako zasoby.</summary>
public static class BuiltInCatalog
{
    private const string ResourceInfix = ".catalog.";

    private static readonly Lazy<IReadOnlyList<CatalogSource>> LazySources =
        new(() => ReadSources(typeof(BuiltInCatalog).Assembly, null));

    /// <summary>Źródła posortowane po nazwie pliku (np. <c>ux.json</c>), treść jako UTF-8.</summary>
    public static IReadOnlyList<CatalogSource> Sources => LazySources.Value;

    /// <summary>Nazwa pliku klasyfikacji aplikacji (<c>catalog\apps-bloat.json</c>); nie jest częścią <see cref="Sources"/>.</summary>
    public const string AppsBloatFileName = "apps-bloat.json";

    private const string AppsBloatResource = "DSOptimizer.AppsBloat.apps-bloat.json";

    private static readonly Lazy<CatalogSource> LazyAppsBloat =
        new(() => ReadNamed(typeof(BuiltInCatalog).Assembly, AppsBloatResource, AppsBloatFileName));

    /// <summary>Plik klasyfikacji aplikacji osadzony obok katalogu tweaków (czyta go <c>AppsBloatCatalog.Load</c>).</summary>
    public static CatalogSource AppsBloatSource => LazyAppsBloat.Value;

    /// <summary>Nazwa pliku listy instalacji (<c>catalog\apps.json</c>, Etap 4c); nie jest częścią <see cref="Sources"/>.</summary>
    public const string AppsInstallFileName = "apps.json";

    private const string AppsInstallResource = "DSOptimizer.AppsInstall.apps.json";

    private static readonly Lazy<CatalogSource> LazyAppsInstall =
        new(() => ReadNamed(typeof(BuiltInCatalog).Assembly, AppsInstallResource, AppsInstallFileName));

    /// <summary>Lista aplikacji do instalacji przez winget osadzona obok katalogu tweaków (czyta ją <c>AppsInstallCatalog.Load</c>).</summary>
    public static CatalogSource AppsInstallSource => LazyAppsInstall.Value;

    /// <summary>Jeden zasób osadzony pod własną nazwą (pliki aplikacji poza katalogiem tweaków), treść jako UTF-8.</summary>
    internal static CatalogSource ReadNamed(Assembly assembly, string resource, string fileName)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Brak zasobu: " + resource);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return new CatalogSource(fileName, reader.ReadToEnd());
    }

    /// <summary>Czyta zasoby JSON z assembly; <paramref name="prefix"/> = null oznacza zasoby <c>*.catalog.*.json</c>.</summary>
    internal static IReadOnlyList<CatalogSource> ReadSources(Assembly assembly, string? prefix)
    {
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && (prefix is null
                    ? name.IndexOf(ResourceInfix, StringComparison.Ordinal) >= 0
                    : name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(name => (Resource: name, FileName: prefix is null
                ? name.Substring(name.IndexOf(ResourceInfix, StringComparison.Ordinal) + ResourceInfix.Length)
                : name.Substring(prefix.Length)))
            .OrderBy(item => item.FileName, StringComparer.Ordinal)
            .ToList();

        var sources = new List<CatalogSource>(names.Count);
        foreach (var (resource, fileName) in names)
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("Brak zasobu katalogu: " + resource);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            sources.Add(new CatalogSource(fileName, reader.ReadToEnd()));
        }

        return sources;
    }
}
