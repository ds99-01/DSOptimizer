using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

public class BuiltInCatalogTests
{
    /// <summary>Dozwolone kategorie katalogu (Etap 2).</summary>
    public static readonly string[] Categories = { "perf", "disk", "net", "sec", "ux", "panels", "tools", "privacy", "repair" };

    /// <summary>ID pozycji z poprzedniej, niepublicznej wersji 3.2; aliasy w katalogu mogą wskazywać tylko na nie.</summary>
    public static readonly string[] LegacyAliases =
    {
        "tool_msstore", "tool_mas", "tool_winutil", "tool_vis", "tool_pow", "tool_network", "tool_sysinfo",
        "io_hiber", "io_sysmain", "io_ntfs",
        "cpu_vbs", "cpu_spectre", "cpu_paging",
        "net_throttle", "net_ipv6",
        "ux_menu", "ux_gamedvr",
        "lan_godmode", "lan_panic",
        "sec_bitlocker_kill", "tool_restart_explorer",
        "sec_take_ownership", "sec_take_ownership_remove", "sec_storage_reserve",
    };

    /// <summary>Pozycje v3.2, których świadomie nie portujemy (cpu_spectre: odrzucone w C2, patrz docs/tweaks/perf.spectre-mitigations.md; net_throttle: odrzucone w C3, patrz docs/tweaks/net.throttling-index.md).</summary>
    public static readonly string[] NotPorted = { "tool_mas", "tool_sysinfo", "cpu_spectre", "net_throttle" };

    private static readonly string TweaksDocsDirectory = RepoRoot.Combine("docs", "tweaks");

    private static List<DefinitionView> Definitions()
    {
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        return catalog.Definitions.Select(d => new DefinitionView(d.Id, d.Category, d.Aliases)).ToList();
    }

    private sealed record DefinitionView(string Id, string Category, IReadOnlyList<string> Aliases);

    [Fact]
    public void Sources_LoadAndValidateWithoutErrors()
    {
        var catalog = CatalogLoader.Load(BuiltInCatalog.Sources);
        Assert.Empty(CatalogValidator.Validate(catalog.Definitions));
    }

    [Fact]
    public void Definitions_TweakAndAction_HaveBenefit()
    {
        var definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;
        var withBenefit = definitions.Where(d => d.Kind != TweakKind.Panel).ToList();

        Assert.Equal(46, withBenefit.Count);
        Assert.All(withBenefit, d => Assert.False(string.IsNullOrWhiteSpace(d.Benefit), $"{d.Id}: brak benefit"));
        Assert.All(withBenefit, d => Assert.True(d.Benefit!.Length <= CatalogValidator.BenefitMaxLength, $"{d.Id}: benefit ma {d.Benefit.Length} znaków"));
        Assert.All(definitions.Where(d => d.Kind == TweakKind.Panel), d => Assert.Null(d.Benefit));
    }

    [Fact]
    public void Definitions_CategoryIsKnown()
    {
        Assert.All(Definitions(), d => Assert.Contains(d.Category, Categories));
    }

    [Fact]
    public void Definitions_HaveDocsFile()
    {
        Assert.All(Definitions(), d => Assert.True(File.Exists(Path.Combine(TweaksDocsDirectory, d.Id + ".md")), $"Brak docs/tweaks/{d.Id}.md"));
    }

    [Fact]
    public void DocsFiles_UseTemplateHeadingsInOrder_ForEveryCatalogEntryAndNotPortedDoc()
    {
        // Jedyne miejsce pilnujące układu docs/tweaks/<id>.md: wzorzec to nagłówki z README.md. Obejmuje pozycje katalogu
        // (każda musi mieć plik) oraz dokumenty pozycji nieportowanych (np. ux.game-dvr.md), które w katalogu nie występują.
        // Sekcja „Obserwacja skutku” (Etap 4a) jest w szablonie, ale starsze dokumenty jej nie mają: wymagana tylko w nowych (partia 4a), tu opcjonalna.
        var template = Headings(Path.Combine(TweaksDocsDirectory, "README.md")).Where(h => h != ObservationHeading).ToArray();
        var docs = Directory.GetFiles(TweaksDocsDirectory, "*.md")
            .Where(f => !string.Equals(Path.GetFileName(f), "README.md", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => f, StringComparer.Ordinal);

        Assert.All(Definitions(), d => Assert.True(docs.ContainsKey(d.Id), $"Brak docs/tweaks/{d.Id}.md"));
        Assert.All(docs, doc =>
        {
            var headings = Headings(doc.Value).Where(h => h != ObservationHeading).ToArray();
            Assert.True(template.SequenceEqual(headings), $"docs/tweaks/{doc.Key}.md: nagłówki " + string.Join(" | ", headings));
        });
    }

    private const string ObservationHeading = "Obserwacja skutku";

    private static string[] Headings(string path)
        => File.ReadAllLines(path, Encoding.UTF8)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line.Substring(3).Trim())
            .ToArray();

    /// <summary>Id z wcześniejszych wersji katalogu 4.0, przeniesione do innej kategorii i zachowane jako alias (CLI: <c>--execute tools.store-repair</c>).</summary>
    private static readonly string[] RenamedIds = { "tools.store-repair" };

    [Fact]
    public void Aliases_AreLegacyIdsAndUnique()
    {
        var aliases = Definitions().SelectMany(d => d.Aliases).ToList();
        Assert.All(aliases, a =>
        {
            Assert.True(LegacyAliases.Contains(a) || RenamedIds.Contains(a), "Alias spoza v3.2 i spoza przeniesionych id: " + a);
            Assert.DoesNotContain(a, NotPorted);
        });
        Assert.Equal(aliases.Count, aliases.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void LegacyAliases_Has24UniqueIds_AndNotPortedIsSubset()
    {
        Assert.Equal(24, LegacyAliases.Distinct(StringComparer.Ordinal).Count());
        Assert.All(NotPorted, id => Assert.Contains(id, LegacyAliases));
    }

    [Fact]
    public void EveryPortedLegacyAlias_AppearsInCatalogExactlyOnce()
    {
        // A5 (spec, sekcja 4): po C5 każdy alias v3.2 spoza NotPorted jest w katalogu wbudowanym, i to dokładnie raz.
        var aliases = Definitions().SelectMany(d => d.Aliases).ToList();
        var problems = LegacyAliases
            .Where(a => !NotPorted.Contains(a))
            .Select(a => (Alias: a, Count: aliases.Count(x => string.Equals(x, a, StringComparison.Ordinal))))
            .Where(x => x.Count != 1)
            .Select(x => $"{x.Alias} x{x.Count}")
            .ToList();
        Assert.True(problems.Count == 0, "Aliasy v3.2 spoza NotPorted bez dokładnie jednego wystąpienia: " + string.Join(", ", problems));
    }

    [Fact]
    public void DocsReadme_HasTemplateHeadings()
    {
        Assert.Equal(
            new[] { "Werdykt 26H2", "Co zmienia", "Źródła", "Wartości domyślne na świeżym 26H2", "Detekcja i undo", "Ryzyka i ostrzeżenia", "Test VM", "Obserwacja skutku" },
            Headings(Path.Combine(TweaksDocsDirectory, "README.md")));
    }

    [Fact]
    public void Sources_MatchCatalogFilesOnDisk()
    {
        var root = RepoRoot.Combine("src", "DSOptimizer.Catalog", "catalog");
        var files = Directory.Exists(root)
            ? Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            : Array.Empty<string>();

        var nested = files.Where(f => !string.Equals(Path.GetDirectoryName(f), root, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(nested.Count == 0, "JSON w podfolderze catalog/ nie jest osadzany (glob catalog\\*.json): " + string.Join(", ", nested));

        // apps-bloat.json i apps.json to osobne formaty (klasyfikacja aplikacji i lista instalacji, nie tablice tweaków): osadzone
        // pod własnymi nazwami zasobów i dostępne przez AppsBloatSource/AppsInstallSource, więc Sources ich nie zawiera.
        // Testy niżej pilnują, że pliki są na dysku.
        var onDisk = files.Select(Path.GetFileName)
            .Where(n => !string.Equals(n, BuiltInCatalog.AppsBloatFileName, StringComparison.Ordinal)
                && !string.Equals(n, BuiltInCatalog.AppsInstallFileName, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        var embedded = BuiltInCatalog.Sources.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(
            onDisk.SequenceEqual(embedded, StringComparer.Ordinal),
            "Pliki na dysku: [" + string.Join(", ", onDisk) + "], osadzone: [" + string.Join(", ", embedded) + "]");
    }

    [Fact]
    public void AppsBloatSource_IsEmbeddedAndMatchesFileOnDisk()
    {
        var path = RepoRoot.Combine("src", "DSOptimizer.Catalog", "catalog", BuiltInCatalog.AppsBloatFileName);
        Assert.True(File.Exists(path), "Brak pliku na dysku: " + path);
        Assert.Equal(BuiltInCatalog.AppsBloatFileName, BuiltInCatalog.AppsBloatSource.Name);
        Assert.Equal(File.ReadAllText(path, System.Text.Encoding.UTF8).TrimStart('﻿'), BuiltInCatalog.AppsBloatSource.Json.TrimStart('﻿'));
        Assert.DoesNotContain(BuiltInCatalog.Sources, s => s.Name == BuiltInCatalog.AppsBloatFileName);
    }

    [Fact]
    public void AppsInstallSource_IsEmbeddedAndMatchesFileOnDisk()
    {
        Assert.Equal("apps.json", BuiltInCatalog.AppsInstallFileName);
        var path = RepoRoot.Combine("src", "DSOptimizer.Catalog", "catalog", BuiltInCatalog.AppsInstallFileName);
        Assert.True(File.Exists(path), "Brak pliku na dysku: " + path);
        Assert.Equal(BuiltInCatalog.AppsInstallFileName, BuiltInCatalog.AppsInstallSource.Name);
        Assert.Equal(File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿'), BuiltInCatalog.AppsInstallSource.Json.TrimStart('﻿'));
        Assert.DoesNotContain(BuiltInCatalog.Sources, s => s.Name == BuiltInCatalog.AppsInstallFileName);
    }

    [Fact]
    public void ReadNamed_MissingResource_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => BuiltInCatalog.ReadNamed(typeof(BuiltInCatalog).Assembly, "Brak.zasobu.json", "x.json"));
    }

    [Fact]
    public void AppsInstallSource_LoadsWithSeedEntries()
    {
        var catalog = AppsInstallCatalog.Load(BuiltInCatalog.AppsInstallSource.Json);

        Assert.Equal("7zip.7zip", catalog.Find("install.7zip")!.WingetId);
        Assert.Equal("VideoLAN.VLC", catalog.Find("install.vlc")!.WingetId);
        Assert.Equal(new AppMatch(null, "7-Zip*", "Igor Pavlov"), catalog.Find("install.7zip")!.Match);
        Assert.Equal(new AppMatch(null, "VLC media player", "VideoLAN"), catalog.Find("install.vlc")!.Match);
    }

    [Fact]
    public void Sources_AreSortedByName()
    {
        var names = BuiltInCatalog.Sources.Select(s => s.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
    }

    [Fact]
    public void ReadSources_UnknownPrefix_ReturnsEmpty()
    {
        Assert.Empty(BuiltInCatalog.ReadSources(typeof(BuiltInCatalog).Assembly, "Nieistniejacy.Prefiks."));
    }

    [Fact]
    public void ReadSources_ReadsJsonResourcesSortedByNameAsUtf8()
    {
        // Zasoby testowe: assembly testowe ma osadzone dwa pliki z prefiksem DSOptimizer.Tests.Unit.Resources.
        var sources = BuiltInCatalog.ReadSources(typeof(BuiltInCatalogTests).Assembly, "DSOptimizer.Tests.Unit.Resources.");
        Assert.Equal(new[] { "a.json", "b.json" }, sources.Select(s => s.Name));
        Assert.Contains("zażółć", sources[1].Json, StringComparison.Ordinal);
    }
}
