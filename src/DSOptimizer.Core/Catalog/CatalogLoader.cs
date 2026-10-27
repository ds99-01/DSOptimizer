using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DSOptimizer.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Catalog;

/// <summary>Wczytuje katalog z wielu plików JSON (każdy = tablica tweaków) i liczy jego hash.</summary>
public static class CatalogLoader
{
    public static LoadedCatalog Load(IEnumerable<CatalogSource> sources)
    {
        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        var ordered = sources.OrderBy(source => source.Name, StringComparer.Ordinal).ToList();
        var definitions = new List<TweakDefinition>();
        foreach (var source in ordered)
        {
            foreach (var item in ParseArray(source))
            {
                if (item is not JObject obj)
                {
                    throw TweakParser.Error(item, source.Name, "element katalogu musi być obiektem tweaka");
                }

                definitions.Add(TweakParser.Parse(obj, source.Name));
            }
        }

        return new LoadedCatalog(definitions, ComputeHash(ordered));
    }

    private static JArray ParseArray(CatalogSource source)
    {
        try
        {
            return JArray.Parse(source.Json);
        }
        catch (JsonReaderException ex)
        {
            throw new CatalogLoadException(
                string.Format(CultureInfo.InvariantCulture, "{0} (linia {1}, kolumna {2}): błędny JSON – {3}", source.Name, ex.LineNumber, ex.LinePosition, ex.Message),
                ex);
        }
    }

    private static string ComputeHash(IEnumerable<CatalogSource> ordered)
    {
        var builder = new StringBuilder();
        foreach (var source in ordered)
        {
            builder.Append(source.Name).Append('\n').Append(source.Json).Append('\n');
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return "sha256:" + string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
