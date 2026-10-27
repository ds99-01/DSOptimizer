using System.IO;
using DSOptimizer.Core;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit;

public class ThirdPartyNoticesTests
{
    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

    [Fact]
    public void Text_EqualsRepoFile()
    {
        var repoText = File.ReadAllText(RepoRoot.Combine("THIRD_PARTY_NOTICES.md"));

        Assert.Equal(Normalize(repoText), Normalize(ThirdPartyNotices.Text));
    }

    [Theory]
    [InlineData("Hanken Grotesk")]
    [InlineData("WPF-UI")]
    [InlineData("CommunityToolkit.Mvvm")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("PolySharp")]
    [InlineData("Octicons")]
    [InlineData("Costura")]
    public void Text_NamesEveryComponent(string component)
        => Assert.Contains(component, ThirdPartyNotices.Text);
}
