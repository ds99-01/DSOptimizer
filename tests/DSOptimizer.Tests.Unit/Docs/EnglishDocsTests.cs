using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Localization;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Docs;

/// <summary>
/// Angielska dokumentacja w <c>docs/en/</c> (tweaks, apps, prep): odcisk polskiego źródła w pierwszej linii, szablon nagłówków,
/// tytuł z angielskiego katalogu, dopisek „(in Polish)” przy <c>docs/zrodla/</c> i te same adresy oraz wartości techniczne co
/// w polskim oryginale. Każdy test sprawdza pliki, które są w <c>docs/en</c> (brak plików = nic do sprawdzenia), poza szablonem
/// <c>docs/en/tweaks/README.md</c>, który musi istnieć. Aktualność odcisku (hash) sprawdza raport braków, nie test.
/// </summary>
public class EnglishDocsTests
{
    private const string ObservationHeading = "Obserwacja skutku";
    private const string ObservedEffectHeading = "Observed effect";

    private static readonly Regex StampLine = new(@"^<!-- source: (docs/(tweaks|apps|prep)/[^ ]+\.md) sha256: [0-9a-f]{64} -->$", RegexOptions.CultureInvariant);
    private static readonly Regex Url = new(@"https?://[^\s)>\]]+", RegexOptions.CultureInvariant);
    private static readonly Regex CodeSpan = new(@"`([^`\r\n]+)`", RegexOptions.CultureInvariant);
    private static readonly Regex Placeholder = new("<[^<>]*>", RegexOptions.CultureInvariant);

    private static readonly string EnglishDocsDirectory = RepoRoot.Combine("docs", "en");
    private static readonly string EnglishTweaksDirectory = RepoRoot.Combine("docs", "en", "tweaks");
    private static readonly string EnglishTemplate = RepoRoot.Combine("docs", "en", "tweaks", "README.md");
    private static readonly string PolishTemplate = RepoRoot.Combine("docs", "tweaks", "README.md");

    [Fact]
    public void EnglishDocs_EveryFileHasStampWithExistingPolishSource()
    {
        var problems = new List<string>();
        foreach (var file in EnglishFiles())
        {
            var relative = Relative(file);
            var lines = ReadLines(file);
            var match = lines.Length > 0 ? StampLine.Match(lines[0]) : Match.Empty;
            if (!match.Success)
            {
                problems.Add($"{relative}: pierwsza linia nie jest odciskiem: {(lines.Length > 0 ? lines[0] : "(pusty plik)")}");
                continue;
            }

            var source = match.Groups[1].Value;
            if (source != PolishRelative(relative))
            {
                problems.Add($"{relative}: odcisk wskazuje {source}, a powinien {PolishRelative(relative)}");
            }

            if (!File.Exists(RepoRoot.Combine(source.Split('/'))))
            {
                problems.Add($"{relative}: brak polskiego źródła {source}");
            }

            if (lines.Length < 2 || lines[1].Length != 0)
            {
                problems.Add($"{relative}: druga linia nie jest pusta");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EnglishTweakDocs_UseEnglishTemplateHeadingsInOrder()
    {
        Assert.True(File.Exists(EnglishTemplate), "Brak szablonu docs/en/tweaks/README.md");
        var polishAll = Headings(PolishTemplate);
        var englishAll = Headings(EnglishTemplate);
        Assert.Equal(9, polishAll.Length);
        Assert.Equal(polishAll.Length, englishAll.Length);
        Assert.Equal(Array.IndexOf(polishAll, ObservationHeading), Array.IndexOf(englishAll, ObservedEffectHeading));

        var template = englishAll.Where(h => h != ObservedEffectHeading).ToArray();
        var problems = new List<string>();
        foreach (var file in EnglishTweakDocs())
        {
            var name = Path.GetFileName(file);
            var headings = Headings(file);
            var withoutObservation = headings.Where(h => h != ObservedEffectHeading).ToArray();
            if (!template.SequenceEqual(withoutObservation))
            {
                problems.Add($"docs/en/tweaks/{name}: nagłówki " + string.Join(" | ", headings));
            }

            var polish = RepoRoot.Combine("docs", "tweaks", name);
            if (!File.Exists(polish))
            {
                problems.Add($"docs/en/tweaks/{name}: brak polskiego docs/tweaks/{name}");
                continue;
            }

            if (headings.Contains(ObservedEffectHeading) != Headings(polish).Contains(ObservationHeading))
            {
                problems.Add($"docs/en/tweaks/{name}: \"{ObservedEffectHeading}\" musi być wtedy i tylko wtedy, gdy polski ma „{ObservationHeading}”");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EnglishTweakDocs_TitleLineUsesEnglishCatalogTitle()
    {
        var polishTitles = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.ToDictionary(d => d.Id, d => d.Title, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var file in EnglishTweakDocs())
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var lines = ReadLines(file);
            var title = lines.Length > 2 ? lines[2] : "(brak linii tytułu)";
            if (polishTitles.TryGetValue(id, out var polishTitle))
            {
                string expected;
                using (L.Use(UiLanguage.English))
                {
                    expected = $"# {id} – {L.Data(polishTitle)}";
                }

                if (title != expected)
                {
                    problems.Add($"docs/en/tweaks/{id}.md: \"{title}\", oczekiwano \"{expected}\"");
                }
            }
            else if (!title.StartsWith($"# {id} – ", StringComparison.Ordinal))
            {
                problems.Add($"docs/en/tweaks/{id}.md: \"{title}\" nie zaczyna się od \"# {id} – \"");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EnglishDocs_MarkPolishSourceRecords()
    {
        var problems = EnglishFiles()
            .SelectMany(file => ReadLines(file)
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(x => x.Line.Contains("docs/zrodla/") && !x.Line.Contains("(in Polish)"))
                .Select(x => $"{Relative(file)}:{x.Number}: {x.Line}"))
            .ToList();

        Assert.True(problems.Count == 0, "Odwołanie do docs/zrodla/ bez \"(in Polish)\":" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EnglishDocs_KeepUrlsAndTechnicalSpans()
    {
        var problems = new List<string>();
        foreach (var file in EnglishFiles())
        {
            var relative = Relative(file);
            var polishPath = RepoRoot.Combine(PolishRelative(relative).Split('/'));
            if (!File.Exists(polishPath))
            {
                continue; // sierotę zgłasza EnglishDocs_EveryFileHasStampWithExistingPolishSource
            }

            var english = File.ReadAllText(file, Encoding.UTF8);
            var polish = File.ReadAllText(polishPath, Encoding.UTF8);

            var missingUrls = Difference(Urls(polish), Urls(english));
            var extraUrls = Difference(Urls(english), Urls(polish));
            if (missingUrls.Count > 0 || extraUrls.Count > 0)
            {
                problems.Add($"{relative}: adresy bez pary w EN: [{string.Join(", ", missingUrls)}], nadmiarowe w EN: [{string.Join(", ", extraUrls)}]");
            }

            var spanMismatches = TechnicalSpanMismatches(polish, english);
            if (spanMismatches.Count > 0)
            {
                problems.Add($"{relative}: spany techniczne – {string.Join("; ", spanMismatches)}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [InlineData(@"Klucz `HKU\<SID zalogowanego>`.", @"Key `HKU\<SID of the signed-in user>`.")]
    [InlineData(@"`C:\Users\<profil>\AppData` i `0x<indeks>`", @"`C:\Users\<profile>\AppData` and `0x<index>`")]
    [InlineData(@"`VolumeCaches\<program obsługi>`", @"`VolumeCaches\<handler>`")]
    public void TechnicalSpanMismatches_PlaceholdersInAngleBrackets_AreText(string polish, string english)
    {
        Assert.Empty(TechnicalSpanMismatches(polish, english));
    }

    [Theory]
    [InlineData(@"`HKU\Software`", @"`HKU\Softwar`")]
    [InlineData(@"`HKU\<SID>\Software`", @"`HKU\<SID>\Softwar`")]
    [InlineData(@"`HKU\Software` i `HKU\Software`", @"`HKU\Software`")]
    public void TechnicalSpanMismatches_ChangedValue_IsReported(string polish, string english)
    {
        Assert.NotEmpty(TechnicalSpanMismatches(polish, english));
    }

    /// <summary>
    /// Spany techniczne PL bez pary w EN i odwrotnie (multizbiór, ordinal). Tekst w nawiasach ostrych (<c>&lt;SID zalogowanego&gt;</c>)
    /// to symbol zastępczy do przetłumaczenia, nie wartość – przed porównaniem każda grupa <c>&lt;…&gt;</c> staje się <c>&lt;&gt;</c>.
    /// </summary>
    internal static List<string> TechnicalSpanMismatches(string polish, string english)
    {
        var polishSpans = TechnicalSpans(polish).Select(NormalizePlaceholders).ToList();
        var englishSpans = TechnicalSpans(english).Select(NormalizePlaceholders).ToList();
        return Difference(polishSpans, englishSpans).Select(s => "bez pary w EN: " + s)
            .Concat(Difference(englishSpans, polishSpans).Select(s => "nadmiarowy w EN: " + s))
            .ToList();
    }

    private static string NormalizePlaceholders(string span) => Placeholder.Replace(span, "<>");

    private static IEnumerable<string> EnglishFiles()
        => Directory.Exists(EnglishDocsDirectory)
            ? Directory.GetFiles(EnglishDocsDirectory, "*.md", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    private static IEnumerable<string> EnglishTweakDocs()
        => Directory.Exists(EnglishTweaksDirectory)
            ? Directory.GetFiles(EnglishTweaksDirectory, "*.md")
                .Where(f => !string.Equals(Path.GetFileName(f), "README.md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    private static string[] ReadLines(string path) => File.ReadAllLines(path, Encoding.UTF8);

    private static string[] Headings(string path)
        => ReadLines(path)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line.Substring(3).Trim())
            .ToArray();

    private static string Relative(string path)
        => path.Substring(RepoRoot.Path.Length).TrimStart('\\', '/').Replace('\\', '/');

    /// <summary>Ścieżka angielskiego pliku w <c>docs/en/</c> → ścieżka polskiego źródła (bez segmentu <c>en/</c>).</summary>
    private static string PolishRelative(string englishRelative) => "docs/" + englishRelative.Substring("docs/en/".Length);

    private static List<string> Urls(string text)
        => Url.Matches(text).Cast<Match>().Select(m => m.Value.TrimEnd('.', ',', ';', ':')).ToList();

    private static List<string> TechnicalSpans(string text)
        => CodeSpan.Matches(text).Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(s => s.Contains("\\") || s.Contains("0x") || s.StartsWith("HK", StringComparison.Ordinal))
            .ToList();

    /// <summary>Elementy <paramref name="left"/> bez pary w <paramref name="right"/> (multizbiór, porównanie ordinal).</summary>
    private static List<string> Difference(IEnumerable<string> left, IEnumerable<string> right)
    {
        var remaining = right.GroupBy(s => s, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var item in left)
        {
            if (remaining.TryGetValue(item, out var count) && count > 0)
            {
                remaining[item] = count - 1;
            }
            else
            {
                missing.Add(item);
            }
        }

        return missing;
    }
}
