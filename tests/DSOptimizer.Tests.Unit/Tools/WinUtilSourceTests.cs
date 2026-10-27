using System;
using System.IO;
using DSOptimizer.Core.Tools;
using DSOptimizer.Tests.Unit.Support;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Tools;

/// <summary>Reguły źródła WinUtil. Próbka JSON wzorowana na prawdziwej odpowiedzi API z 2026-10-26 (tag 26.10.07).</summary>
public class WinUtilSourceTests
{
    private const string Hash = "eeae69922fbe6354ea4e2a37f705eaa17bdaf45cc7889618a313178a513b13ee";
    private const string Url = "https://github.com/ChrisTitusTech/winutil/releases/download/26.10.07/winutil.ps1";

    private static readonly WinUtilRelease Release = new("26.10.07", new Uri(Url), 927798, Hash);

    private static JObject Sample()
        => JObject.Parse(File.ReadAllText(RepoRoot.Combine("tests", "DSOptimizer.Tests.Unit", "Tools", "Samples", "winutil-release-26.10.07.json")));

    private static JObject FirstAsset(JObject root) => (JObject)root["assets"]![0]!;

    private static string? Refusal(JObject root)
    {
        Assert.False(WinUtilSource.TryParseRelease(root.ToString(), out var release, out var refusal));
        Assert.Null(release);
        return refusal;
    }

    [Fact]
    public void TryParseRelease_RealSample_ReturnsTagUrlSizeDigest()
    {
        Assert.True(WinUtilSource.TryParseRelease(Sample().ToString(), out var release, out var refusal));
        Assert.Null(refusal);
        Assert.Equal("26.10.07", release!.Tag);
        Assert.Equal(new Uri(Url), release.DownloadUrl);
        Assert.Equal(927798, release.Size);
        Assert.Equal(Hash, release.Sha256);
    }

    [Fact]
    public void TryParseRelease_UppercaseDigest_IsLowercased()
    {
        var root = Sample();
        FirstAsset(root)["digest"] = "sha256:" + Hash.ToUpperInvariant();
        Assert.True(WinUtilSource.TryParseRelease(root.ToString(), out var release, out _));
        Assert.Equal(Hash, release!.Sha256);
    }

    [Fact]
    public void TryParseRelease_Prerelease_Refused()
    {
        var root = Sample();
        root["prerelease"] = true;
        Assert.Equal("wydanie testowe (prerelease)", Refusal(root));
    }

    [Fact]
    public void TryParseRelease_Draft_Refused()
    {
        var root = Sample();
        root["draft"] = true;
        Assert.Equal("szkic wydania", Refusal(root));
    }

    [Fact]
    public void TryParseRelease_MissingDigest_Refused()
    {
        var root = Sample();
        FirstAsset(root)["digest"] = JValue.CreateNull();
        Assert.Equal("brak skrótu SHA256", Refusal(root));
    }

    [Fact]
    public void TryParseRelease_DigestNotSha256_Refused()
    {
        var root = Sample();
        FirstAsset(root)["digest"] = "sha512:" + Hash;
        Assert.Equal("brak skrótu SHA256", Refusal(root));
    }

    [Fact]
    public void TryParseRelease_TwoWinutilAssets_RefusedResponse()
    {
        var root = Sample();
        ((JArray)root["assets"]!).Add(FirstAsset(root).DeepClone());
        Assert.Equal("błędna odpowiedź GitHuba", Refusal(root));
    }

    [Fact]
    public void TryParseRelease_NoWinutilAsset_FalseWithoutRefusal()
    {
        var root = Sample();
        FirstAsset(root)["name"] = "other.zip";
        Assert.Null(Refusal(root));
    }

    [Fact]
    public void TryParseRelease_SizeOver5MB_Refused()
    {
        var root = Sample();
        FirstAsset(root)["size"] = (5L * 1024 * 1024) + 1;
        Assert.Equal("plik większy niż 5 MB", Refusal(root));
    }

    [Fact]
    public void TryParseRelease_DuplicateJsonKey_RefusedResponse()
    {
        Assert.False(WinUtilSource.TryParseRelease("{\"tag_name\":\"1.0\",\"tag_name\":\"2.0\",\"prerelease\":false,\"draft\":false,\"assets\":[]}", out _, out var refusal));
        Assert.Equal("błędna odpowiedź GitHuba", refusal);
    }

    [Theory]
    [InlineData("v26.10.07")]
    [InlineData("26.10.07-beta")]
    [InlineData("26")]
    [InlineData("1.2.3.4.5")]
    public void TryParseRelease_TagWithLetters_Refused(string tag)
    {
        var root = Sample();
        root["tag_name"] = tag;
        Assert.Equal("nieoczekiwany numer wersji", Refusal(root));
    }

    [Fact]
    public void IsAllowedDownloadUrl_ExactPath_True() => Assert.True(WinUtilSource.IsAllowedDownloadUrl(new Uri(Url), Release));

    [Fact]
    public void IsAllowedDownloadUrl_OtherRepo_False()
        => Assert.False(WinUtilSource.IsAllowedDownloadUrl(new Uri("https://github.com/evil/winutil/releases/download/26.10.07/winutil.ps1"), Release));

    [Fact]
    public void IsAllowedDownloadUrl_PercentEncoded_False()
        => Assert.False(WinUtilSource.IsAllowedDownloadUrl(new Uri("https://github.com/ChrisTitusTech/winutil/releases/download/26.10.07/%77inutil.ps1"), Release));

    [Fact]
    public void IsAllowedDownloadUrl_Query_False()
        => Assert.False(WinUtilSource.IsAllowedDownloadUrl(new Uri(Url + "?a=b"), Release));

    [Fact]
    public void IsAllowedDownloadUrl_Fragment_False()
        => Assert.False(WinUtilSource.IsAllowedDownloadUrl(new Uri(Url + "#a"), Release));

    [Fact]
    public void IsAllowedDownloadUrl_HttpScheme_False()
        => Assert.False(WinUtilSource.IsAllowedDownloadUrl(new Uri(Url.Replace("https", "http")), Release));

    [Fact]
    public void IsAllowedDownloadUrl_OtherTagOrHost_False()
    {
        Assert.False(WinUtilSource.IsAllowedDownloadUrl(new Uri(Url.Replace("26.10.07/", "26.10.06/")), Release));
        Assert.False(WinUtilSource.IsAllowedDownloadUrl(new Uri(Url.Replace("//github.com", "//raw.githubusercontent.com")), Release));
    }

    [Fact]
    public void IsAllowedRedirect_ReleaseAssets_True()
    {
        Assert.True(WinUtilSource.IsAllowedRedirect(new Uri("https://release-assets.githubusercontent.com/x/y")));
        Assert.True(WinUtilSource.IsAllowedRedirect(new Uri("https://objects.githubusercontent.com/x/y")));
    }

    [Fact]
    public void IsAllowedRedirect_RawGithubusercontent_False()
        => Assert.False(WinUtilSource.IsAllowedRedirect(new Uri("https://raw.githubusercontent.com/x/y")));

    [Fact]
    public void Constants_MatchDecision()
    {
        Assert.Equal("ChrisTitusTech/winutil", WinUtilSource.Repository);
        Assert.Equal("winutil.ps1", WinUtilSource.FileName);
        Assert.Equal("https://api.github.com/repos/ChrisTitusTech/winutil/releases/latest", WinUtilSource.ReleasesLatestUrl.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(15), WinUtilSource.ApiTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), WinUtilSource.DownloadTimeout);
        Assert.Equal(5L * 1024 * 1024, WinUtilSource.MaxBytes);
        Assert.Equal("RemoteSigned", WinUtilSource.ExecutionPolicy);
    }

    [Fact]
    public void LaunchArguments_QuotesPathAndUsesPolicy()
        => Assert.Equal(
            "-NoExit -NoProfile -ExecutionPolicy RemoteSigned -File \"C:\\ProgramData\\DSOptimizer\\downloads\\a.ps1\"",
            WinUtilSource.LaunchArguments("C:\\ProgramData\\DSOptimizer\\downloads\\a.ps1"));

    [Theory]
    [InlineData("C:\\a\"b.ps1")]
    [InlineData("C:\\a\\")]
    [InlineData("C:\\a\nb.ps1")]
    [InlineData("\\\\server\\share\\a.ps1")]
    public void LaunchArguments_PathWithQuote_Throws(string path)
        => Assert.Throws<ArgumentException>(() => WinUtilSource.LaunchArguments(path));

    [Theory]
    [InlineData("a.ps1")]
    [InlineData("\\a\\b.ps1")]
    [InlineData("C:a.ps1")]
    public void LaunchArguments_RelativePath_Throws(string path)
        => Assert.Throws<ArgumentException>(() => WinUtilSource.LaunchArguments(path));

    [Fact]
    public void LaunchArguments_Null_Throws()
        => Assert.Throws<ArgumentException>(() => WinUtilSource.LaunchArguments(null!));
}
