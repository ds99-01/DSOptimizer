using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Localization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization;

/// <summary>
/// Tytuł pozycji katalogu cytowany w innym tekście (np. „włącz ją pozycją „Ochrona systemu””) musi po angielsku być dokładnie
/// angielskim tytułem tej pozycji, inaczej użytkownik szuka pozycji, której nie ma.
/// </summary>
public class QuotedTitleTests
{
    private static readonly Dictionary<string, string> Titles = new() { ["Ochrona systemu"] = "System Protection" };

    /// <summary>Błędy: wpis zawiera polski tytuł w cudzysłowie „…”, ale jego angielski nie zawiera "angielski tytuł".</summary>
    internal static List<string> Mismatches(
        IEnumerable<(string Pl, string En)> entries,
        IReadOnlyDictionary<string, string> titlePlToEn)
    {
        var violations = new List<string>();
        foreach (var (pl, en) in entries)
        {
            foreach (var title in titlePlToEn)
            {
                if (!pl.Contains("„" + title.Key + "”"))
                {
                    continue;
                }

                if (title.Key == title.Value)
                {
                    continue; // tytuł bez tłumaczenia – raport braków domyka
                }

                if (!en.Contains("\"" + title.Value + "\""))
                {
                    violations.Add($"\"{pl}\" cites \"{title.Key}\" but the English text lacks \"{title.Value}\": \"{en}\"");
                }
            }
        }

        return violations;
    }

    [Fact]
    public void QuotedTitle_Mismatch_Flagged()
    {
        var entries = new[] { ("Włącz ją pozycją „Ochrona systemu”.", "Turn it on with \"System protection\".") };
        Assert.Single(Mismatches(entries, Titles));
    }

    [Fact]
    public void QuotedTitle_Match_Accepted()
    {
        var entries = new[]
        {
            ("Włącz ją pozycją „Ochrona systemu”.", "Turn it on with \"System Protection\"."),
            ("Tekst bez cytatu.", "Text without a quote."),
        };
        Assert.Empty(Mismatches(entries, Titles));
    }

    [Fact]
    public void QuotedTitle_UntranslatedTitle_Skipped()
    {
        var titles = new Dictionary<string, string> { ["Ochrona systemu"] = "Ochrona systemu" };
        var entries = new[] { ("Włącz „Ochrona systemu”.", "Turn on something else.") };
        Assert.Empty(Mismatches(entries, titles));
    }

    [Fact]
    public void QuotedCatalogTitles_UseEnglishTitle()
    {
        var polish = CatalogLoader.Load(BuiltInCatalog.Sources);
        LoadedCatalog english;
        using (L.Use(UiLanguage.English))
        {
            english = CatalogTranslation.Translate(polish);
        }

        var englishById = english.Definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var titles = polish.Definitions
            .GroupBy(d => d.Title, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => englishById[g.First().Id].Title, StringComparer.Ordinal);

        var entries = new List<(string Pl, string En)>();
        foreach (var source in L.BuiltInSources())
        {
            var root = JObject.Parse(source.Json);
            foreach (var entry in (JArray?)root["entries"] ?? new JArray())
            {
                var pl = (string?)entry["pl"];
                var en = (string?)entry["en"];
                if (pl is not null && en is not null)
                {
                    entries.Add((pl, en));
                }
            }
        }

        var violations = Mismatches(entries, titles);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
