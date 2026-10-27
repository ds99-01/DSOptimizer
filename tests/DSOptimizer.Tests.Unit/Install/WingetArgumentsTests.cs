using System;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class WingetArgumentsTests
{
    /// <summary>Kolejność argumentów stała (spec 5.1); bez <c>--locale</c> (Ruling R14).</summary>
    [Fact]
    public void Install_ExactCommand_NoLocale()
    {
        var args = WingetArguments.Install("Notepad++.Notepad++", @"C:\x\a.log");

        Assert.Equal(
            @"install --id Notepad++.Notepad++ --exact --source winget --silent --no-upgrade --accept-package-agreements --accept-source-agreements --disable-interactivity --log C:\x\a.log",
            args);
        Assert.DoesNotContain("--locale", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_LogPathWithSpace_Quoted()
    {
        Assert.EndsWith(
            "--log \"C:\\Program Data\\a.log\"",
            WingetArguments.Install("7zip.7zip", @"C:\Program Data\a.log"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Install_NeverForceIgnoreHashScopeOrNoProgress()
    {
        var args = WingetArguments.Install("Microsoft.VCRedist.2015+.x64", @"C:\x\a.log");

        Assert.DoesNotContain("--force", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--ignore-security-hash", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--scope", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-progress", args, StringComparison.Ordinal);
        Assert.Contains("--source winget", args, StringComparison.Ordinal);
        Assert.Contains("--no-upgrade", args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Foo.Bar\u2026")]
    [InlineData("")]
    [InlineData("7zip 7zip")]
    [InlineData("--force")]
    public void Install_InvalidId_Throws(string id)
    {
        Assert.Throws<ArgumentException>(() => WingetArguments.Install(id, @"C:\x\a.log"));
    }

    [Fact]
    public void Install_EmptyLogPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => WingetArguments.Install("7zip.7zip", " "));
        Assert.Throws<ArgumentException>(() => WingetArguments.Install("7zip.7zip", null!));
    }

    [Fact]
    public void Search_QueryQuoted()
    {
        Assert.Equal(
            "search --query \"visual studio code\" --source winget --count 50 --accept-source-agreements --disable-interactivity",
            WingetArguments.Search("visual studio code"));
        Assert.Equal(
            "search --query vlc --source winget --count 50 --accept-source-agreements --disable-interactivity",
            WingetArguments.Search("  vlc "));
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("")]
    public void Search_InvalidQuery_Throws(string query)
    {
        Assert.Throws<ArgumentException>(() => WingetArguments.Search(query));
    }

    [Fact]
    public void ListExactAndVersion()
    {
        Assert.Equal(
            "list --id 7zip.7zip --exact --source winget --accept-source-agreements --disable-interactivity",
            WingetArguments.ListExact("7zip.7zip"));
        Assert.Equal("--version", WingetArguments.Version());
        Assert.Throws<ArgumentException>(() => WingetArguments.ListExact("a\u2026"));
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("a\u0007b")]
    [InlineData("a\u007Fb")]
    [InlineData("a\tb")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryValidateQuery_Rejects(string? query)
    {
        Assert.False(WingetArguments.TryValidateQuery(query, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>M6: zapytanie zaczynające się od „-” mogłoby zostać wzięte za opcję winget (np. „--source msstore”).</summary>
    [Theory]
    [InlineData("-e")]
    [InlineData("--source msstore")]
    [InlineData("  -vlc")]
    public void TryValidateQuery_LeadingDash_Rejected(string query)
    {
        Assert.False(WingetArguments.TryValidateQuery(query, out var error));
        Assert.Equal("Zapytanie nie może zaczynać się od „-”.", error);
        Assert.Equal(InstallText.QueryLeadingDash, error);
        Assert.Throws<ArgumentException>(() => WingetArguments.Search(query));
    }

    [Fact]
    public void TryValidateQuery_DashInside_Accepted()
    {
        Assert.True(WingetArguments.TryValidateQuery("notepad-plus", out _));
        Assert.True(WingetArguments.TryValidateQuery("k-lite", out _));
    }

    [Fact]
    public void TryValidateQuery_Length()
    {
        Assert.False(WingetArguments.TryValidateQuery(new string('a', 101), out _));
        Assert.True(WingetArguments.TryValidateQuery(new string('a', 100), out var error));
        Assert.Null(error);
        Assert.True(WingetArguments.TryValidateQuery("przeglądarka", out _));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\tb", "\"a\tb\"")]
    [InlineData(@"a b\", @"""a b\\""")]
    [InlineData(@"C:\x\a.log", @"C:\x\a.log")]
    [InlineData("", "\"\"")]
    [InlineData("a\"b", @"""a\""b""")]
    [InlineData(@"a\\""b c", @"""a\\\\\""b c""")]
    public void Quote_CommandLineToArgvWRules(string argument, string expected)
    {
        Assert.Equal(expected, WingetArguments.Quote(argument));
    }

    [Theory]
    [InlineData("7zip.7zip", true)]
    [InlineData("Notepad++.Notepad++", true)]
    [InlineData("Microsoft.VCRedist.2015+.x64", true)]
    [InlineData("CodecGuide.K-LiteCodecPack.Standard", true)]
    [InlineData("ndd.Notepad--", true)]
    [InlineData("Foo.Bar\u2026", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData(".Foo", false)]
    [InlineData("Foo Bar", false)]
    [InlineData("Foo\"Bar", false)]
    [InlineData("Foo.Bar\n", false)]
    public void IsValidWingetId(string? id, bool expected)
    {
        Assert.Equal(expected, WingetArguments.IsValidWingetId(id));
    }

    [Fact]
    public void IsValidWingetId_LengthLimit128()
    {
        Assert.True(WingetArguments.IsValidWingetId(new string('a', 128)));
        Assert.False(WingetArguments.IsValidWingetId(new string('a', 129)));
    }

    [Fact]
    public void Quote_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WingetArguments.Quote(null!));
    }
}
