using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Catalog;

/// <summary>Grupy tematyczne pozycji w kategoriach, które je mają. Klucze są wewnętrznymi identyfikatorami, nazwy widoczne w programie daje <c>GuiText.GroupName</c>.</summary>
public static class CatalogGroups
{
    private static readonly IReadOnlyList<string> None = Array.Empty<string>();

    private static readonly IReadOnlyList<string> Privacy = new[] { "start", "ads", "telemetry", "ai" };

    /// <summary>Klucze grup kategorii w kolejności wyświetlania; pusta lista = kategoria bez grup.</summary>
    public static IReadOnlyList<string> For(string category)
        => string.Equals(category, "privacy", StringComparison.Ordinal) ? Privacy : None;

    public static bool IsKnown(string category, string group)
    {
        foreach (var known in For(category))
        {
            if (string.Equals(known, group, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
