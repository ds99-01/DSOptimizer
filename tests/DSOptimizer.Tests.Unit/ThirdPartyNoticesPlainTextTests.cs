using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core;
using Xunit;

namespace DSOptimizer.Tests.Unit;

public class ThirdPartyNoticesPlainTextTests
{
    [Fact]
    public void ToPlainText_Heading1_KeepsOnlyText()
        => Assert.Equal("Komponenty", ThirdPartyNotices.ToPlainText("# Komponenty"));

    [Fact]
    public void ToPlainText_Heading2_IsPrecededByBlankLine()
        => Assert.Equal("a\n\nWPF-UI", ThirdPartyNotices.ToPlainText("a\n## WPF-UI"));

    [Fact]
    public void ToPlainText_Heading2_AfterBlankLine_NoDoubleBlank()
        => Assert.Equal("a\n\nWPF-UI", ThirdPartyNotices.ToPlainText("a\n\n## WPF-UI"));

    [Fact]
    public void ToPlainText_Fence_RemovedAndContentKeptVerbatim()
    {
        const string md = "x\n\n```text\n# nie nagłówek\n**nie pogrubienie** `nie kod`\n- nie punktor\n  wcięcie  \n```\n\ny";
        const string expected = "x\n\n# nie nagłówek\n**nie pogrubienie** `nie kod`\n- nie punktor\n  wcięcie  \n\ny";

        Assert.Equal(expected, ThirdPartyNotices.ToPlainText(md));
    }

    [Fact]
    public void ToPlainText_Bold_MarkersRemoved()
        => Assert.Equal("to jest ważne", ThirdPartyNotices.ToPlainText("to jest **ważne**"));

    [Fact]
    public void ToPlainText_Backticks_Removed()
        => Assert.Equal("Plik: a.dll", ThirdPartyNotices.ToPlainText("Plik: `a.dll`"));

    [Fact]
    public void ToPlainText_Bullet_BecomesDot()
        => Assert.Equal("• Źródło: https://github.com/x", ThirdPartyNotices.ToPlainText("- Źródło: https://github.com/x"));

    [Fact]
    public void ToPlainText_CrLf_Normalized()
        => Assert.Equal("a\n\nb", ThirdPartyNotices.ToPlainText("a\r\n\r\n## b"));

    [Fact]
    public void ToPlainText_RealNotices_HasNoMarkdownMarkers()
    {
        var markdown = ThirdPartyNotices.Text.Replace("\r\n", "\n");
        var plain = ThirdPartyNotices.ToPlainText(markdown);
        var lines = plain.Split('\n');

        Assert.DoesNotContain("```", plain);

        // Jedyny dozwolony wyjątek: linie z „#” wewnątrz płotka (pełny tekst licencji zostaje 1:1).
        var verbatimHashLines = FencedLines(markdown).Where(l => l.StartsWith("#", StringComparison.Ordinal)).ToHashSet();
        Assert.All(lines.Where(l => l.StartsWith("#", StringComparison.Ordinal)), l => Assert.Contains(l, verbatimHashLines));

        foreach (var title in markdown.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).Select(l => l.Substring(3).Trim()))
        {
            Assert.Contains(title, lines);
        }
    }

    [Fact]
    public void ToPlainText_RealNotices_KeepsFencedLicenseTextVerbatim()
    {
        var markdown = ThirdPartyNotices.Text.Replace("\r\n", "\n");
        var plain = ThirdPartyNotices.ToPlainText(markdown);
        var fenced = FencedLines(markdown).ToList();

        Assert.NotEmpty(fenced);
        Assert.Contains(string.Join("\n", fenced.Take(5)), plain);
        Assert.Contains(string.Join("\n", fenced.Skip(fenced.Count - 5)), plain);
    }

    private static IEnumerable<string> FencedLines(string markdown)
    {
        var inFence = false;
        foreach (var line in markdown.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }
            else if (inFence)
            {
                yield return line;
            }
        }
    }
}
