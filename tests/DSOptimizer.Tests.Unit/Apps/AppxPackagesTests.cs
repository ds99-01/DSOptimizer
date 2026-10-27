using System;
using DSOptimizer.Core.Apps;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

public class AppxPackagesTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";

    private const string OneRecord =
        """[{"Name":"Contoso.App","PackageFullName":"Contoso.App_1.0.0.0_x64__abc","PackageFamilyName":"Contoso.App_abc","Publisher":"CN=Contoso","Version":"1.0.0.0","IsFramework":false,"IsResourcePackage":false,"NonRemovable":true,"SignatureKind":"System","IsProvisioned":true,"DisplayName":"Contoso"}]""";

    [Fact]
    public void Parse_MapsEveryField()
    {
        var app = Assert.Single(AppxPackages.Parse(OneRecord, Sid, (_, raw) => raw));

        Assert.Equal("appx:Contoso.App_abc", app.Id);
        Assert.Equal("Contoso", app.Name);
        Assert.Equal("CN=Contoso", app.Publisher);
        Assert.Equal("1.0.0.0", app.Version);
        Assert.Equal(Sid, app.OwnerSid);
        Assert.Equal(AppScope.User, app.Scope);
        Assert.Equal(AppKind.Store, app.Kind);
        Assert.Null(app.Win32);
        Assert.Equal(new AppxInfo("Contoso.App_1.0.0.0_x64__abc", "Contoso.App_abc", "Contoso.App", false, false, true, "System", true), app.Appx);
    }

    [Fact]
    public void Parse_NullDisplayName_FallsBackToPackageName()
    {
        var json = OneRecord.Replace("\"DisplayName\":\"Contoso\"", "\"DisplayName\":null");

        Assert.Equal("Contoso.App", Assert.Single(AppxPackages.Parse(json, Sid, (_, raw) => raw)).Name);
    }

    [Fact]
    public void Parse_ResolverReturnsBlank_UsesPackageName()
    {
        Assert.Equal("Contoso.App", Assert.Single(AppxPackages.Parse(OneRecord, Sid, (_, _) => "  ")).Name);
    }

    [Fact]
    public void Parse_PublisherDisplayName_WinsOverDistinguishedName()
    {
        var json = OneRecord.Replace("\"DisplayName\":\"Contoso\"", "\"DisplayName\":\"Contoso\",\"PublisherDisplayName\":\"ms-resource:Pub\"");
        string? asked = null;

        var app = Assert.Single(AppxPackages.Parse(json, Sid, (_, raw) => raw, (_, raw) =>
        {
            asked = raw;
            return "Contoso Ltd";
        }));

        Assert.Equal("ms-resource:Pub", asked);
        Assert.Equal("Contoso Ltd", app.Publisher);
        Assert.Equal("Contoso", app.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_PublisherResolverGivesNothing_KeepsDistinguishedName(string? resolved)
    {
        var json = OneRecord.Replace("\"DisplayName\":\"Contoso\"", "\"DisplayName\":\"Contoso\",\"PublisherDisplayName\":\"ms-resource:Pub\"");

        var app = Assert.Single(AppxPackages.Parse(json, Sid, (_, raw) => raw, (_, _) => resolved));

        Assert.Equal("CN=Contoso", app.Publisher);
    }

    [Fact]
    public void Parse_NoPublisherDisplayName_ResolverGetsNull_KeepsDistinguishedName()
    {
        string? asked = "nie-ustawiono";

        var app = Assert.Single(AppxPackages.Parse(OneRecord, Sid, (_, raw) => raw, (_, raw) =>
        {
            asked = raw;
            return raw;
        }));

        Assert.Null(asked);
        Assert.Equal("CN=Contoso", app.Publisher);
    }

    [Fact]
    public void Parse_SingleObjectInsteadOfArray_IsAccepted()
    {
        var single = OneRecord.Substring(1, OneRecord.Length - 2);

        Assert.Single(AppxPackages.Parse(single, Sid, (_, raw) => raw));
    }

    [Fact]
    public void Parse_EmptyArray_GivesNoApps()
    {
        Assert.Empty(AppxPackages.Parse("[]", Sid, (_, raw) => raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("to nie jest json")]
    [InlineData("[1,2]")]
    [InlineData("\"tekst\"")]
    [InlineData("""[{"PackageFullName":"x"}]""")]
    public void Parse_Garbage_ThrowsFormatException(string json)
    {
        Assert.Throws<FormatException>(() => AppxPackages.Parse(json, Sid, (_, raw) => raw));
    }

    [Fact]
    public void Parse_MissingBooleans_AreFalse()
    {
        var json = """[{"Name":"A","PackageFullName":"A_1","PackageFamilyName":"A_f"}]""";

        var appx = Assert.Single(AppxPackages.Parse(json, Sid, (_, raw) => raw)).Appx!;

        Assert.False(appx.IsFramework);
        Assert.False(appx.NonRemovable);
        Assert.False(appx.IsProvisioned);
        Assert.Null(appx.SignatureKind);
    }

    [Theory]
    [InlineData("Kalkulator", null)]
    [InlineData("ms-resource://Contoso.App/Resources/AppName", "@{Contoso.App_1_x64__abc?ms-resource://Contoso.App/Resources/AppName}")]
    [InlineData("ms-resource:/Resources/AppName", "@{Contoso.App_1_x64__abc?ms-resource://Contoso.App/Resources/AppName}")]
    [InlineData("ms-resource:AppName", "@{Contoso.App_1_x64__abc?ms-resource://Contoso.App/Resources/AppName}")]
    public void IndirectSource_FollowsResearchFormat(string raw, string? expected)
    {
        Assert.Equal(expected, AppxPackages.IndirectSource("Contoso.App", "Contoso.App_1_x64__abc", raw));
    }

    [Fact]
    public void ResolveDisplayName_LiteralKept_WithoutCallingLoader()
    {
        var result = AppxPackages.ResolveDisplayName("Contoso.App", "full", "Moja aplikacja", _ => throw new InvalidOperationException());

        Assert.Equal("Moja aplikacja", result);
    }

    [Fact]
    public void ResolveDisplayName_ResourceExpanded()
    {
        string? asked = null;
        var result = AppxPackages.ResolveDisplayName("Contoso.App", "full", "ms-resource:/Resources/N", source =>
        {
            asked = source;
            return "Rozwinięta";
        });

        Assert.Equal("@{full?ms-resource://Contoso.App/Resources/N}", asked);
        Assert.Equal("Rozwinięta", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ms-resource:/Resources/N")]
    [InlineData("@{full?ms-resource://Contoso.App/Resources/N}")]
    public void ResolveDisplayName_LoaderFailsOrReturnsRawResource_FallsBackToName(string? loaded)
    {
        var result = AppxPackages.ResolveDisplayName("Contoso.App", "full", "ms-resource:/Resources/N", _ => loaded);

        Assert.Equal("Contoso.App", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveDisplayName_NoRawName_FallsBackToName(string? raw)
    {
        Assert.Equal("Contoso.App", AppxPackages.ResolveDisplayName("Contoso.App", "full", raw, _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void ResolvePublisherName_LiteralKept_ResourceExpandedSameWayAsName()
    {
        Assert.Equal("Clipchamp", AppxPackages.ResolvePublisherName("Clipchamp.Clipchamp", "full", "Clipchamp", _ => throw new InvalidOperationException()));

        string? asked = null;
        var result = AppxPackages.ResolvePublisherName("Contoso.App", "full", "ms-resource:PublisherName", source =>
        {
            asked = source;
            return "Contoso Ltd";
        });

        Assert.Equal("@{full?ms-resource://Contoso.App/Resources/PublisherName}", asked);
        Assert.Equal("Contoso Ltd", result);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("ms-resource:PublisherName", null)]
    [InlineData("ms-resource:PublisherName", "ms-resource:PublisherName")]
    [InlineData("ms-resource:PublisherName", "@{full?ms-resource://Contoso.App/Resources/PublisherName}")]
    public void ResolvePublisherName_NothingUsable_IsNull(string? raw, string? loaded)
    {
        Assert.Null(AppxPackages.ResolvePublisherName("Contoso.App", "full", raw, _ => loaded));
    }
}
