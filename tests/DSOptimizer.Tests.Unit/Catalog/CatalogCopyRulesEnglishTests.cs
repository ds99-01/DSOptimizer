using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// Reguły tekstów widocznych w programie po angielsku (te same co <see cref="CatalogCopyRulesTests"/>). Pola bez tłumaczenia
/// (angielski równy polskiemu) są pomijane, więc testy są zielone przy niepełnych tabelach i zaczynają obowiązywać wraz z tłumaczeniem.
/// </summary>
public class CatalogCopyRulesEnglishTests
{
    private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Frazy zakazane w każdym polu widocznym w programie (także w <c>description</c>).</summary>
    private static readonly Regex[] ForbiddenPhrases =
    {
        new(@"\bVM\b", RegexOptions.CultureInvariant),
        new(@"26H2", Ci),
        new(@"\bfresh", Ci),
        new(@"measur", Ci),
        new(@"test machine", Ci),
        new(@"virtual machine", Ci),
        new(@"No guidance", Ci),
        new(@"\b20\d\d-\d\d-\d\d\b", Ci),
        new(@"\b\d{1,2}[./]\d{1,2}[./]20\d\d\b", Ci),
        new(@"Microsoft:\s*""", Ci),
        new(@"not (been )?(yet )?(verified|confirmed|checked)", Ci),
    };

    /// <summary>Elementy techniczne, których nie ma w polach widocznych od razu; „undo” jest słowem przycisku, więc dozwolone.</summary>
    private static readonly Regex[] TechnicalTokens =
    {
        new(@"HK(LM|CU|U|CR)\\", Ci),
        new(@"=", RegexOptions.CultureInvariant),
        new(@"""[a-z]+\.[a-z0-9-]+""", Ci),
        new(@"\bsnapshot", Ci),
        new(@"\bExecuted\b", RegexOptions.CultureInvariant),
        new(@"\bFailed\b", RegexOptions.CultureInvariant),
    };

    private static readonly Regex Abbreviations = new(@"(?<![\p{L}])(e\.g|i\.e|etc|vs|approx)\.", Ci);

    private static readonly Regex SentenceEnd = new(@"[.!?](\s|$)", RegexOptions.CultureInvariant);

    private static readonly Regex RestartWords = new(@"restart|sign (out|in)", Ci);

    private static readonly Regex NoUndoWords = new(@"undo|revers|revert", Ci);

    private const int MaxTitleLength = 60;
    private const int MaxWarnings = 2;
    private const int MaxWarningLength = 140;
    private const int MaxBenefitSentences = 2;

    private static readonly LoadedCatalog Polish = CatalogLoader.Load(BuiltInCatalog.Sources);

    public static IEnumerable<object[]> CategoryNames()
        => Polish.Definitions
            .Select(d => d.Category)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .Select(c => new object[] { c });

    /// <summary>Wpisy kategorii: polska definicja i ta sama po tłumaczeniu na angielski (parowane po <c>Id</c>).</summary>
    private static List<(TweakDefinition Pl, TweakDefinition En)> Pairs(string category)
    {
        LoadedCatalog english;
        using (L.Use(UiLanguage.English))
        {
            english = CatalogTranslation.Translate(Polish);
        }

        var byId = english.Definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
        return Polish.Definitions
            .Where(d => string.Equals(d.Category, category, StringComparison.Ordinal))
            .Select(d => (d, byId[d.Id]))
            .ToList();
    }

    private static bool Translated(string? pl, string? en)
        => en is not null && !string.Equals(pl, en, StringComparison.Ordinal);

    /// <summary>Pola tłumaczone w oknie od razu: tytuł, summary, benefit, uwagi.</summary>
    private static IEnumerable<string> ImmediateTranslated(TweakDefinition pl, TweakDefinition en)
    {
        if (Translated(pl.Title, en.Title))
        {
            yield return en.Title;
        }

        if (Translated(pl.Summary, en.Summary))
        {
            yield return en.Summary;
        }

        if (Translated(pl.Benefit, en.Benefit))
        {
            yield return en.Benefit!;
        }

        for (var i = 0; i < en.Warnings.Count && i < pl.Warnings.Count; i++)
        {
            if (Translated(pl.Warnings[i], en.Warnings[i]))
            {
                yield return en.Warnings[i];
            }
        }
    }

    private static IEnumerable<string> VisibleTranslated(TweakDefinition pl, TweakDefinition en)
    {
        foreach (var field in ImmediateTranslated(pl, en))
        {
            yield return field;
        }

        if (Translated(pl.Description, en.Description))
        {
            yield return en.Description;
        }
    }

    internal static int CountSentences(string text)
        => SentenceEnd.Matches(Abbreviations.Replace(text, "x")).Count;

    internal static bool MentionsRestart(string text) => RestartWords.IsMatch(text);

    internal static bool MentionsNoUndo(string text) => NoUndoWords.IsMatch(text);

    internal static List<string> ForbiddenMatches(string label, IEnumerable<string?> fields)
    {
        var found = new List<string>();
        foreach (var field in fields)
        {
            if (string.IsNullOrEmpty(field))
            {
                continue;
            }

            foreach (var pattern in ForbiddenPhrases)
            {
                if (pattern.IsMatch(field))
                {
                    found.Add($"{label}: /{pattern}/ in \"{field}\"");
                }
            }
        }

        return found;
    }

    internal static List<string> TechnicalMatches(string label, string field)
        => TechnicalTokens.Where(p => p.IsMatch(field)).Select(p => $"{label}: /{p}/ in \"{field}\"").ToList();

    [Theory]
    [MemberData(nameof(CategoryNames))]
    public void VisibleText_HasNoForbiddenPhrases_English(string category)
    {
        var violations = Pairs(category).SelectMany(p => ForbiddenMatches(p.Pl.Id, VisibleTranslated(p.Pl, p.En))).ToList();
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [MemberData(nameof(CategoryNames))]
    public void ImmediateText_HasNoTechnicalTokens_English(string category)
    {
        var violations = Pairs(category)
            .SelectMany(p => ImmediateTranslated(p.Pl, p.En).SelectMany(f => TechnicalMatches(p.Pl.Id, f)))
            .ToList();
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [MemberData(nameof(CategoryNames))]
    public void Lengths_FitPanel_English(string category)
    {
        var violations = new List<string>();
        foreach (var (pl, en) in Pairs(category))
        {
            if (Translated(pl.Title, en.Title) && en.Title.Length > MaxTitleLength)
            {
                violations.Add($"{pl.Id}: title has {en.Title.Length} characters (limit {MaxTitleLength})");
            }

            if (en.Warnings.Count > MaxWarnings)
            {
                violations.Add($"{pl.Id}: {en.Warnings.Count} warnings (limit {MaxWarnings})");
            }

            for (var i = 0; i < en.Warnings.Count && i < pl.Warnings.Count; i++)
            {
                if (Translated(pl.Warnings[i], en.Warnings[i]) && en.Warnings[i].Length > MaxWarningLength)
                {
                    violations.Add($"{pl.Id}: warning has {en.Warnings[i].Length} characters (limit {MaxWarningLength}): \"{en.Warnings[i]}\"");
                }
            }

            if (Translated(pl.Benefit, en.Benefit) && CountSentences(en.Benefit!) > MaxBenefitSentences)
            {
                violations.Add($"{pl.Id}: benefit has more than {MaxBenefitSentences} sentences: \"{en.Benefit}\"");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [MemberData(nameof(CategoryNames))]
    public void DecisionWarnings_StayVisible_English(string category)
    {
        var violations = new List<string>();
        foreach (var (pl, en) in Pairs(category))
        {
            // Sprawdzamy dopiero, gdy wszystkie uwagi pozycji są przetłumaczone (nieprzetłumaczona mogłaby być właśnie ta o restarcie).
            var allTranslated = en.Warnings.Count > 0
                && en.Warnings.Count == pl.Warnings.Count
                && en.Warnings.Select((w, i) => Translated(pl.Warnings[i], w)).All(t => t);
            if (!allTranslated)
            {
                continue;
            }

            if (pl.Kind == TweakKind.Tweak && !pl.Reversible && !en.Warnings.Any(MentionsNoUndo))
            {
                violations.Add($"{pl.Id}: irreversible tweak without a warning about no undo");
            }

            if (pl.RequiresReboot && !en.Warnings.Any(MentionsRestart))
            {
                violations.Add($"{pl.Id}: item that needs a restart without a restart warning");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AppsTexts_HaveNoForbiddenPhrases_English()
    {
        AppsInstallCatalog installPl;
        AppsBloatCatalog bloatPl;
        AppsInstallCatalog installEn;
        AppsBloatCatalog bloatEn;
        using (L.Use(UiLanguage.Polish))
        {
            installPl = AppsInstallCatalog.Load(BuiltInCatalog.AppsInstallSource.Json);
            bloatPl = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);
        }

        using (L.Use(UiLanguage.English))
        {
            installEn = AppsInstallCatalog.Load(BuiltInCatalog.AppsInstallSource.Json);
            bloatEn = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);
        }

        var violations = new List<string>();
        foreach (var pl in installPl.Entries)
        {
            var en = installEn.Find(pl.Id)!;
            violations.AddRange(ForbiddenMatches(pl.Id, TranslatedOnly((pl.Summary, en.Summary), (pl.Warning, en.Warning))));
        }

        foreach (var pl in bloatPl.Entries)
        {
            var en = bloatEn.Entries.Single(e => e.Id == pl.Id);
            violations.AddRange(ForbiddenMatches(pl.Id, TranslatedOnly((pl.Summary, en.Summary), (pl.Warning, en.Warning))));
        }

        foreach (var pl in bloatPl.Protected)
        {
            var en = bloatEn.Protected.Single(e => e.Id == pl.Id);
            violations.AddRange(ForbiddenMatches(pl.Id, TranslatedOnly((pl.Reason, en.Reason))));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string?> TranslatedOnly(params (string? Pl, string? En)[] pairs)
        => pairs.Where(p => Translated(p.Pl, p.En)).Select(p => p.En);

    [Theory]
    [InlineData("Measured on a VM")]
    [InlineData("Checked on 26H2")]
    [InlineData("Verified 2026-10-28")]
    [InlineData("Tested on 28.10.2026")]
    [InlineData("Microsoft: \"Do not change\"")]
    [InlineData("This has not been verified")]
    [InlineData("A fresh install of Windows")]
    [InlineData("Run on a virtual machine")]
    public void EnglishForbidden_Flags(string text)
        => Assert.NotEmpty(ForbiddenMatches("x", new[] { text }));

    [Theory]
    [InlineData("Turns off the sync service.")]
    [InlineData("Valid until 2026")]
    [InlineData("Vmware tools are unaffected")]
    public void EnglishForbidden_AcceptsPlainText(string text)
        => Assert.Empty(ForbiddenMatches("x", new[] { text }));

    [Theory]
    [InlineData("The update failed to start.")]
    [InlineData("It has executed twice.")]
    public void EnglishTechnical_FailedLowercaseAllowed(string text)
        => Assert.Empty(TechnicalMatches("x", text));

    [Theory]
    [InlineData("Result: Failed")]
    [InlineData("Result: Executed")]
    [InlineData("Key HKLM\\Software")]
    [InlineData("Value = 1")]
    [InlineData("Use \"repair.network-reset\" instead")]
    [InlineData("Keeps a snapshot of the state")]
    public void EnglishTechnical_FailedResultFlagged(string text)
        => Assert.NotEmpty(TechnicalMatches("x", text));

    [Fact]
    public void EnglishTechnical_UndoAllowed()
        => Assert.Empty(TechnicalMatches("x", "Use Undo to restore it."));

    [Theory]
    [InlineData("E.g. VLC works. Second.", 2)]
    [InlineData("It works, i.e. it is fine.", 1)]
    [InlineData("Files, etc. are kept. Done!", 2)]
    [InlineData("One sentence without a period", 0)]
    public void EnglishSentences_IgnoreAbbreviations(string text, int expected)
        => Assert.Equal(expected, CountSentences(text));

    [Theory]
    [InlineData("Takes effect after you sign out.", true)]
    [InlineData("Takes effect after you sign in again.", true)]
    [InlineData("Needs a restart.", true)]
    [InlineData("Works right away.", false)]
    public void EnglishRestart_Accepts(string text, bool expected)
        => Assert.Equal(expected, MentionsRestart(text));

    [Theory]
    [InlineData("This can't be undone.", true)]
    [InlineData("The change is irreversible.", true)]
    [InlineData("You can revert it manually.", true)]
    [InlineData("It affects every user.", false)]
    public void EnglishNoUndo_Accepts(string text, bool expected)
        => Assert.Equal(expected, MentionsNoUndo(text));
}
