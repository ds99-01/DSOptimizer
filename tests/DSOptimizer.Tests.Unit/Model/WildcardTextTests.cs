using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Model;

public class WildcardTextTests
{
    [Theory]
    [InlineData("*Systray.exe", "SecurityHealthSystray.exe", true)]
    [InlineData("*Systray.exe", "SecurityHealthSystray.exe.bak", false)]
    [InlineData("Microsoft.*", "Microsoft.BingNews", true)]
    [InlineData("Microsoft.*", "Contoso.Microsoft.BingNews", false)]
    [InlineData("Lenovo*Vantage", "Lenovo Commercial Vantage", true)]
    [InlineData("Lenovo*Vantage", "Lenovo Vantage Service", false)]
    [InlineData("a*b*c", "a-b-c", true)]
    [InlineData("a*b*c", "a-c-b", false)]
    [InlineData("ab*ab", "ab", false)]
    [InlineData("*", "", true)]
    [InlineData("*", "anything", true)]
    public void Star_StartMiddleEnd(string pattern, string text, bool expected)
        => Assert.Equal(expected, WildcardText.Matches(pattern, text));

    [Theory]
    [InlineData("steam.exe", "steam.exe", true)]
    [InlineData("steam.exe", "steam.exe2", false)]
    [InlineData("steam.exe", "xsteam.exe", false)]
    [InlineData("", "", true)]
    [InlineData("", "x", false)]
    public void NoStar_Equality(string pattern, string text, bool expected)
        => Assert.Equal(expected, WildcardText.Matches(pattern, text));

    [Fact]
    public void IgnoresCase()
    {
        Assert.True(WildcardText.Matches("SECURITYHEALTH*.EXE", "securityhealthsystray.exe"));
        Assert.True(WildcardText.Matches(@"c:\program files\*", @"C:\Program Files\Kaspersky Lab\"));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<System.ArgumentNullException>(() => WildcardText.Matches(null!, "x"));
        Assert.Throws<System.ArgumentNullException>(() => WildcardText.Matches("x", null!));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\DriverStore\FileRepository\*\", @"C:\Windows\System32\DriverStore\FileRepository\realtek.inf_amd64_1\", true)]
    [InlineData(@"C:\Windows\System32\DriverStore\FileRepository\*\", @"C:\Windows\System32\DriverStore\FileRepository\a\b\", true)]
    [InlineData(@"C:\Windows\System32\DriverStore\FileRepository\*\", @"C:\Windows\System32\DriverStore\FileRepository\", false)]
    [InlineData(@"C:\Program Files\*", @"C:\Program Files\Avast Software\Avast\", true)]
    [InlineData(@"C:\Program Files\*", @"C:\Users\dso\AppData\Local\Temp\", false)]
    public void Star_SpansBackslash(string pattern, string text, bool expected)
        => Assert.Equal(expected, WildcardText.Matches(pattern, text));
}
