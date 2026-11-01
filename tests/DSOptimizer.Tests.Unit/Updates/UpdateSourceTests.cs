using DSOptimizer.Core.Updates;
using Xunit;

namespace DSOptimizer.Tests.Unit.Updates;

public class UpdateSourceTests
{
    private static string J(string tag, bool pre = false, bool draft = false)
        => "{\"tag_name\":" + Newtonsoft.Json.JsonConvert.ToString(tag) + ",\"prerelease\":" + (pre ? "true" : "false") + ",\"draft\":" + (draft ? "true" : "false") + "}";

    [Theory]
    [InlineData("v4.0.3", "4.0.3")]
    [InlineData("v4.0.10", "4.0.10")]
    [InlineData("v04.0.3", "4.0.3")]
    public void Read_ValidTag_IsStable(string tag, string expected)
    {
        var reading = UpdateSource.Read(J(tag), out var version);

        Assert.Equal(ReleaseReading.Stable, reading);
        Assert.Equal(expected, version!.ToString(3));
    }

    [Theory]
    [InlineData("4.0.3")]
    [InlineData("v4.0")]
    [InlineData("v4.0.3.1")]
    [InlineData("v4.0.3-rc1")]
    [InlineData("v4.0.3\n")]
    [InlineData("V4.0.3")]
    [InlineData("v\u0664.\u0660.\u0663")]
    [InlineData("v99999999999.0.0")]
    [InlineData("<b>v4.0.3</b>")]
    [InlineData("")]
    public void Read_BadTag_IsInvalid(string tag)
    {
        var reading = UpdateSource.Read(J(tag), out var version);

        Assert.Equal(ReleaseReading.Invalid, reading);
        Assert.Null(version);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Read_PrereleaseOrDraft_IsSkipped(bool pre, bool draft)
    {
        Assert.Equal(ReleaseReading.Skipped, UpdateSource.Read(J("v4.0.3", pre, draft), out var version));
        Assert.Null(version);
        Assert.Equal(ReleaseReading.Skipped, UpdateSource.Read(J("v4.0.3-rc1", pre, draft), out version));
        Assert.Null(version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Zaloguj się do sieci</html>")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"v4.0.3\"}")]
    [InlineData("{\"tag_name\":\"v4.0.3\",\"prerelease\":\"false\",\"draft\":false}")]
    [InlineData("{\"tag_name\":\"v4.0.3\",\"tag_name\":\"v9.9.9\",\"prerelease\":false,\"draft\":false}")]
    public void Read_NotARelease_IsInvalid(string json)
    {
        Assert.Equal(ReleaseReading.Invalid, UpdateSource.Read(json, out var version));
        Assert.Null(version);
    }

    [Fact]
    public void Read_NullJson_IsInvalid()
        => Assert.Equal(ReleaseReading.Invalid, UpdateSource.Read(null, out _));
}