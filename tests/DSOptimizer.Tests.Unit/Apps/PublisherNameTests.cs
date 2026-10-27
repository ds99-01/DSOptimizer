using DSOptimizer.Core.Apps;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class PublisherNameTests
{
    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "Microsoft Corporation")]
    [InlineData("CN=Spotify AB, O=Spotify AB, L=Stockholm, C=SE", "Spotify AB")]
    [InlineData("CN=\"Dolby Laboratories, Inc.\", O=\"Dolby Laboratories, Inc.\", L=San Francisco, C=US", "Dolby Laboratories, Inc.")]
    [InlineData("O=\"Acme \"\"Tools\"\"\", CN=x", "Acme \"Tools\"")]
    [InlineData("cn=Lenovo, o=LENOVO INC.", "LENOVO INC.")]
    [InlineData("CN=5A894077-D1C3-4AB2-8D16-1C3D1A10C9E5", "5A894077-D1C3-4AB2-8D16-1C3D1A10C9E5")]
    [InlineData("CN=Zażółć, OID.2.25.311729368913984317654407730594956997722=1", "Zażółć")]
    public void Readable_DistinguishedName_TakesOrganizationThenCommonName(string publisher, string expected)
        => Assert.Equal(expected, PublisherName.Readable(publisher));

    [Theory]
    [InlineData("McAfee, LLC", "McAfee, LLC")]
    [InlineData("Google LLC", "Google LLC")]
    [InlineData("  Realtek Semiconductor Corp.  ", "Realtek Semiconductor Corp.")]
    [InlineData("L=Redmond, C=US", "L=Redmond, C=US")]
    [InlineData("CN=, O=", "CN=, O=")]
    public void Readable_NotDistinguishedName_OrWithoutNames_KeepsText(string publisher, string expected)
        => Assert.Equal(expected, PublisherName.Readable(publisher));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Readable_Empty_IsNull(string? publisher)
        => Assert.Null(PublisherName.Readable(publisher));
}
