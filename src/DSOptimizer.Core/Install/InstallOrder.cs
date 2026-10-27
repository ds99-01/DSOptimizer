using System;
using System.Collections.Generic;
using System.Linq;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Kolejność serii (spec 5.4): najpierw środowiska uruchomieniowe, potem pozostałe pozycje listy w kolejności
/// <see cref="AppsInstallCatalog.Entries"/>, na końcu pozycje z wyszukiwania (i spoza listy) w kolejności wejścia.
/// </summary>
public static class InstallOrder
{
    public static IReadOnlyList<InstallTarget> Sort(IReadOnlyList<InstallTarget> targets, AppsInstallCatalog catalog)
    {
        if (targets is null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < catalog.Entries.Count; i++)
        {
            positions[catalog.Entries[i].Id] = i;
        }

        return targets
            .Select((target, input) => (Target: target, Input: input, Position: positions.TryGetValue(target.Id, out var at) ? at : (int?)null))
            .OrderBy(item => Group(item.Target, item.Position))
            .ThenBy(item => item.Position ?? item.Input)
            .Select(item => item.Target)
            .ToList();
    }

    private static int Group(InstallTarget target, int? position)
        => position is null ? 2 : target.Category == InstallCategory.Runtimes ? 0 : 1;
}
