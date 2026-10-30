using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Core;
using DSOptimizer.Core.Localization;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit;

/// <summary>Okno „Licencje komponentów” po angielsku: cała treść po angielsku, teksty licencji i nazwy komponentów bez zmian.</summary>
public class ThirdPartyNoticesEnglishTests
{
    private static readonly Regex PolishLetters = new("[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]", RegexOptions.CultureInvariant);

    // Jedyny uprawniony polski napis w angielskim tekście: nazwa własna znaku programu (Ś), cytowana tak, jak widać ją w programie.
    private const string BrandName = "DŚO";

    private static string English
    {
        get
        {
            using (L.Use(UiLanguage.English))
            {
                return ThirdPartyNotices.Localized;
            }
        }
    }

    private static string Polish
    {
        get
        {
            using (L.Use(UiLanguage.Polish))
            {
                return ThirdPartyNotices.Localized;
            }
        }
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

    private static List<string> Headings(string markdown)
        => markdown.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).ToList();

    private static List<string> FencedBlocks(string markdown)
    {
        var blocks = new List<string>();
        var current = new List<string>();
        var inFence = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inFence)
                {
                    blocks.Add(string.Join("\n", current));
                    current.Clear();
                }

                inFence = !inFence;
            }
            else if (inFence)
            {
                current.Add(line);
            }
        }

        return blocks;
    }

    [Fact]
    public void Localized_Polish_EqualsPolishText()
        => Assert.Equal(ThirdPartyNotices.Text, Polish);

    [Fact]
    public void Localized_English_DiffersFromPolish()
        => Assert.NotEqual(ThirdPartyNotices.Text, English);

    [Fact]
    public void English_HasNoPolishLettersOutsideBrandName()
    {
        var withoutBrand = English.Replace(BrandName, string.Empty);

        Assert.DoesNotMatch(PolishLetters, withoutBrand);
    }

    [Theory]
    [InlineData("Komponenty")]
    [InlineData("Źródło")]
    [InlineData("Licencja")]
    [InlineData("Sprawdzono")]
    public void English_HasNoPolishWords(string word)
        => Assert.DoesNotContain(word, English);

    [Fact]
    public void English_StartsWithEnglishTitle()
        => Assert.StartsWith("# Third-party components", English, StringComparison.Ordinal);

    [Fact]
    public void English_HasNoTranslationStampInPlainText()
        => Assert.DoesNotContain("<!--", ThirdPartyNotices.ToPlainText(English));

    [Theory]
    [InlineData("Hanken Grotesk")]
    [InlineData("WPF-UI")]
    [InlineData("CommunityToolkit.Mvvm")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("PolySharp")]
    [InlineData("Octicons")]
    [InlineData("Costura")]
    public void English_NamesEveryComponent(string component)
        => Assert.Contains(component, English);

    [Fact]
    public void English_HasSameComponentsAsPolish()
    {
        var polish = Headings(ThirdPartyNotices.Text);
        var english = Headings(English);

        Assert.Equal(polish.Count, english.Count);
        Assert.Equal(
            polish.Select(h => h.Substring(3).Split(' ')[0]),
            english.Select(h => h.Substring(3).Split(' ')[0]));
    }

    [Fact]
    public void English_LicenseTextsAreVerbatimCopiesOfPolishFile()
        => Assert.Equal(FencedBlocks(ThirdPartyNotices.Text), FencedBlocks(English));

    [Fact]
    public void English_CopiesSourceAddressesOfPolishFile()
    {
        var url = new Regex(@"https?://[^\s)`]+", RegexOptions.CultureInvariant);
        var polish = url.Matches(ThirdPartyNotices.Text).Cast<Match>().Select(m => m.Value.TrimEnd('.', ',', ';')).ToList();
        var english = url.Matches(English).Cast<Match>().Select(m => m.Value.TrimEnd('.', ',', ';')).ToList();

        Assert.Equal(polish, english);
    }

    [Fact]
    public void EnglishFile_EqualsEmbeddedResource()
    {
        var repoText = System.IO.File.ReadAllText(RepoRoot.Combine("THIRD_PARTY_NOTICES_EN.md"));

        Assert.Equal(Normalize(repoText), Normalize(English));
    }
}
