using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// Reguły tekstów widocznych w programie (spec opisów 2026-10-26): krótko, po ludzku, bez śladów pomiarów i dopisków technicznych.
/// </summary>
public class CatalogCopyRulesTests
{
    /// <summary>Frazy zakazane w każdym polu widocznym w programie (także w <c>description</c>).</summary>
    private static readonly Regex[] ForbiddenPhrases =
    {
        new(@"\bVM\b", RegexOptions.CultureInvariant),
        new(@"26H2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"śwież", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"zmierz", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"pomiar", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"maszyn[a-z]* testow", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"No guidance", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b20\d\d-\d\d-\d\d\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b\d{1,2}\.\d{1,2}\.20\d\d\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"Microsoft:\s*„", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"nie zostało (to |jeszcze )?(sprawdzone|potwierdzone)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    };

    /// <summary>Elementy techniczne, których nie ma w polach widocznych od razu (tytuł, opis, zysk, uwagi).</summary>
    private static readonly Regex[] TechnicalTokens =
    {
        new(@"HK(LM|CU|U|CR)\\", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"=", RegexOptions.CultureInvariant),
        new(@"„[a-z]+\.[a-z0-9-]+”", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bsnapshot", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bundo\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bExecuted\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bFailed\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    };

    /// <summary>Skróty z kropką, które nie kończą zdania (Ruling R2).</summary>
    private static readonly Regex Abbreviations = new(@"(?<![\p{L}])(np|m\.in|tzn|itp|ok|ang)\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SentenceEnd = new(@"[.!?](\s|$)", RegexOptions.CultureInvariant);

    private const int MaxTitleLength = 60;
    private const int MaxWarnings = 2;
    private const int MaxWarningLength = 140;
    private const int MaxBenefitSentences = 2;

    /// <summary>Wbudowany katalog wczytany raz dla wszystkich przypadków testu.</summary>
    private static readonly IReadOnlyList<TweakDefinition> Definitions = CatalogLoader.Load(BuiltInCatalog.Sources).Definitions;

    public static IEnumerable<object[]> CategoryNames()
        => Definitions
            .Select(d => d.Category)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .Select(c => new object[] { c });

    private static List<TweakDefinition> InCategory(string category)
        => Definitions
            .Where(d => string.Equals(d.Category, category, StringComparison.Ordinal))
            .ToList();

    private static IEnumerable<string> ImmediateFields(TweakDefinition d)
    {
        yield return d.Title;
        yield return d.Summary;
        if (d.Benefit is not null)
        {
            yield return d.Benefit;
        }

        foreach (var warning in d.Warnings)
        {
            yield return warning;
        }
    }

    private static IEnumerable<string> VisibleFields(TweakDefinition d)
        => ImmediateFields(d).Append(d.Description);

    private static bool Mentions(string text, string fragment)
        => text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Uwaga o restarcie: „restart”, odmiana „restarcie”/„restartu” albo „ponowne uruchomienie/zalogowanie”.</summary>
    private static bool MentionsRestart(string text)
        => Mentions(text, "restar") || Mentions(text, "ponown");

    private static int CountSentences(string text)
        => SentenceEnd.Matches(Abbreviations.Replace(text, "x")).Count;

    private static List<string> ForbiddenMatches(string label, IEnumerable<string?> fields)
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
                    found.Add($"{label}: /{pattern}/ w „{field}”");
                }
            }
        }

        return found;
    }

    [Theory]
    [MemberData(nameof(CategoryNames))]
    public void VisibleText_HasNoForbiddenPhrases(string category)
    {
        var violations = InCategory(category).SelectMany(d => ForbiddenMatches(d.Id, VisibleFields(d))).ToList();
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [MemberData(nameof(CategoryNames))]
    public void ImmediateText_HasNoTechnicalTokens(string category)
    {
        var violations = new List<string>();
        foreach (var d in InCategory(category))
        {
            foreach (var field in ImmediateFields(d))
            {
                violations.AddRange(TechnicalTokens.Where(p => p.IsMatch(field)).Select(p => $"{d.Id}: /{p}/ w „{field}”"));
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [MemberData(nameof(CategoryNames))]
    public void Lengths_FitPanel(string category)
    {
        var violations = new List<string>();
        foreach (var d in InCategory(category))
        {
            if (d.Title.Length > MaxTitleLength)
            {
                violations.Add($"{d.Id}: tytuł ma {d.Title.Length} znaków (limit {MaxTitleLength})");
            }

            if (d.Warnings.Count > MaxWarnings)
            {
                violations.Add($"{d.Id}: {d.Warnings.Count} uwag (limit {MaxWarnings})");
            }

            violations.AddRange(d.Warnings
                .Where(w => w.Length > MaxWarningLength)
                .Select(w => $"{d.Id}: uwaga ma {w.Length} znaków (limit {MaxWarningLength}): „{w}”"));

            if (d.Benefit is not null && CountSentences(d.Benefit) > MaxBenefitSentences)
            {
                violations.Add($"{d.Id}: zysk ma więcej niż {MaxBenefitSentences} zdania: „{d.Benefit}”");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [MemberData(nameof(CategoryNames))]
    public void DecisionWarnings_StayVisible(string category)
    {
        var violations = new List<string>();
        foreach (var d in InCategory(category))
        {
            // Ruling R3: brak cofania wymagany tylko dla tweaków; restart dla każdej pozycji z restartem.
            if (d.Kind == TweakKind.Tweak && !d.Reversible
                && !d.Warnings.Any(w => Mentions(w, "cof")))
            {
                violations.Add($"{d.Id}: nieodwracalny tweak bez uwagi o braku cofania");
            }

            if (d.RequiresReboot && !d.Warnings.Any(MentionsRestart))
            {
                violations.Add($"{d.Id}: pozycja wymagająca restartu bez uwagi o restarcie");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("Np. VLC działa. Drugie.", 2)]
    [InlineData("m.in. to.", 1)]
    [InlineData("Jedno zdanie bez kropki", 0)]
    public void CountSentences_IgnoresAbbreviations(string text, int expected)
        => Assert.Equal(expected, CountSentences(text));

    [Theory]
    [InlineData("Działa po restarcie komputera.", true)]
    [InlineData("Wymaga restartu.", true)]
    [InlineData("Zadziała po ponownym zalogowaniu.", true)]
    [InlineData("Zmiana działa od razu.", false)]
    public void MentionsRestart_AcceptsPolishInflection(string text, bool expected)
        => Assert.Equal(expected, MentionsRestart(text));

    [Fact]
    public void AppsTexts_HaveNoForbiddenPhrases()
    {
        var install = AppsInstallCatalog.Load(BuiltInCatalog.AppsInstallSource.Json);
        var bloat = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);

        var violations = new List<string>();
        foreach (var e in install.Entries)
        {
            violations.AddRange(ForbiddenMatches(e.Id, new[] { e.Summary, e.Warning }));
        }

        foreach (var e in bloat.Entries)
        {
            violations.AddRange(ForbiddenMatches(e.Id, new[] { e.Summary, e.Warning }));
        }

        foreach (var e in bloat.Protected)
        {
            violations.AddRange(ForbiddenMatches(e.Id, new[] { e.Reason }));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void InstallText_OutsideListNote_HasNoVm()
    {
        Assert.DoesNotMatch(new Regex(@"\bVM\b", RegexOptions.CultureInvariant), InstallText.WingetSectionNote);
    }
}
