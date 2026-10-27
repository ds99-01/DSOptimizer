using System;
using System.Linq;
using DSOptimizer.Core.Install;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

/// <summary>
/// Reguły źródła RustDesk. Wartości z odpowiedzi API z pomiaru (spike 2026-10-24, <c>releases.json</c> na VM Home): wydanie 1.5.0,
/// asset EXE 25887600 B i MSI 26345472 B z polem <c>digest</c>.
/// </summary>
public class RustDeskSourceTests
{
    private const string MsiName = "rustdesk-1.5.0-x86_64.msi";
    private const string MsiHash = "624fe792ab1d76b35134dab84bff97a6bcd14043ffd090a2c6a2800f77580495";
    private const string MsiUrl = "https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi";
    private const string SpikeSubject =
        "CN=PURSLANE, O=PURSLANE, S=North West, C=SG, OID.2.5.4.15=Business Entity, OID.1.3.6.1.4.1.311.60.2.1.3=SG, SERIALNUMBER=53481265A";

    private static readonly RustDeskRelease Release150 = new("1.5.0", MsiName, new Uri(MsiUrl), 26345472, MsiHash);

    private static JObject Asset(string name, long size, string? digest, string url)
    {
        var asset = new JObject { ["name"] = name, ["size"] = size, ["browser_download_url"] = url, ["content_type"] = "application/octet-stream" };
        asset["digest"] = digest is null ? JValue.CreateNull() : digest;
        return asset;
    }

    private static JObject Latest(string tag = "1.5.0", bool prerelease = false, bool draft = false)
        => new()
        {
            ["tag_name"] = tag,
            ["name"] = tag,
            ["prerelease"] = prerelease,
            ["draft"] = draft,
            ["published_at"] = "2026-10-20T10:00:00Z",
            ["assets"] = new JArray(
                Asset("rustdesk-1.5.0-x86_64.exe", 25887600, "sha256:" + new string('1', 64),
                    "https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.exe"),
                Asset(MsiName, 26345472, "sha256:" + MsiHash, MsiUrl)),
        };

    private static JObject MsiOf(JObject release) => (JObject)((JArray)release["assets"]!).Single(a => (string?)a["name"] == MsiName);

    private static (bool Ok, RustDeskRelease? Release, string? Refusal) Parse(JObject json) => Parse(json.ToString(Formatting.None));

    private static (bool Ok, RustDeskRelease? Release, string? Refusal) Parse(string json)
    {
        var ok = RustDeskSource.TryParseRelease(json, out var release, out var refusal);
        return (ok, release, refusal);
    }

    [Fact]
    public void Constants_FromSpec()
    {
        Assert.Equal("install.rustdesk", RustDeskSource.EntryId);
        Assert.Equal("rustdesk/rustdesk", RustDeskSource.Repository);
        Assert.Equal(new Uri("https://api.github.com/repos/rustdesk/rustdesk/releases/latest"), RustDeskSource.ReleasesLatestUrl);
        Assert.Equal("application/vnd.github+json", RustDeskSource.ApiAccept);
        Assert.Equal("DSOptimizer", RustDeskSource.UserAgent);
        Assert.Equal(TimeSpan.FromSeconds(15), RustDeskSource.ApiTimeout);
        Assert.Equal(TimeSpan.FromMinutes(10), RustDeskSource.DownloadTimeout);
        Assert.Equal(209715200L, RustDeskSource.MaxBytes);
        Assert.Equal("rustdesk-msi", RustDeskSource.MsiLogKey);
        Assert.True(GitHubSources.IsAllowed(RustDeskSource.EntryId, RustDeskSource.Repository));
    }

    [Fact]
    public void TryParseRelease_Latest150_SelectsMsi()
    {
        var (ok, release, refusal) = Parse(Latest());

        Assert.True(ok);
        Assert.Null(refusal);
        Assert.Equal(Release150, release);
        Assert.Equal(MsiName, release!.FileName);
        Assert.Equal(26345472, release.Size);
        Assert.Equal(64, release.Sha256.Length);
        Assert.DoesNotContain("sha256:", release.Sha256, StringComparison.Ordinal);
        Assert.True(RustDeskSource.IsAllowedDownloadUrl(release.DownloadUrl, release));
    }

    [Fact]
    public void TryParseRelease_UppercaseDigest_NormalizedToLowercase()
    {
        var json = Latest();
        MsiOf(json)["digest"] = "sha256:" + MsiHash.ToUpperInvariant();

        var (ok, release, _) = Parse(json);

        Assert.True(ok);
        Assert.Equal(MsiHash, release!.Sha256);
    }

    [Fact]
    public void TryParseRelease_MaxSizeAllowed()
    {
        var json = Latest();
        MsiOf(json)["size"] = RustDeskSource.MaxBytes;

        Assert.True(Parse(json).Ok);
    }

    [Fact]
    public void Prerelease_Refused()
    {
        var (ok, release, refusal) = Parse(Latest(prerelease: true));

        Assert.False(ok);
        Assert.Null(release);
        Assert.Equal("wydanie testowe (prerelease)", refusal);
    }

    [Fact]
    public void Draft_Refused()
    {
        var (ok, release, refusal) = Parse(Latest(draft: true));

        Assert.False(ok);
        Assert.Null(release);
        Assert.Equal("szkic wydania", refusal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nie json")]
    [InlineData("[1]")]
    [InlineData("{ \"tag_name\": ")]
    [InlineData(null)]
    public void InvalidJson_Refused(string? json)
    {
        var (ok, release, refusal) = Parse(json!);

        Assert.False(ok);
        Assert.Null(release);
        Assert.Equal("błędna odpowiedź GitHuba", refusal);
    }

    [Theory]
    [InlineData("tag_name")]
    [InlineData("prerelease")]
    [InlineData("draft")]
    [InlineData("assets")]
    public void MissingReleaseField_Refused(string field)
    {
        var json = Latest();
        json.Remove(field);

        var (ok, release, refusal) = Parse(json);

        Assert.False(ok);
        Assert.Null(release);
        Assert.Equal("błędna odpowiedź GitHuba", refusal);
    }

    [Fact]
    public void WrongFieldTypes_Refused()
    {
        foreach (var (field, value) in new (string, JToken)[]
                 {
                     ("tag_name", 150), ("prerelease", "false"), ("draft", 0), ("assets", new JObject()),
                 })
        {
            var json = Latest();
            json[field] = value;

            Assert.Equal("błędna odpowiedź GitHuba", Parse(json).Refusal);
        }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("size")]
    [InlineData("browser_download_url")]
    public void MissingMsiAssetField_Refused(string field)
    {
        var json = Latest();
        var msi = MsiOf(json);
        if (field == "name")
        {
            // Asset bez nazwy nie jest MSI tego wydania; drugi, prawdziwy wpis MSI sprawdza resztę.
            ((JArray)json["assets"]!).Add(new JObject { ["size"] = 1 });
            Assert.True(Parse(json).Ok);
            return;
        }

        msi.Remove(field);

        var (ok, release, refusal) = Parse(json);

        Assert.False(ok);
        Assert.Null(release);
        Assert.Equal("błędna odpowiedź GitHuba", refusal);
    }

    [Theory]
    [InlineData("\"26345472\"")]
    [InlineData("26345472.5")]
    [InlineData("99999999999999999999999")]
    public void SizeNotInteger_Refused(string sizeJson)
    {
        var text = Latest().ToString(Formatting.None).Replace("\"size\":26345472", "\"size\":" + sizeJson);

        Assert.Equal("błędna odpowiedź GitHuba", Parse(text).Refusal);
    }

    [Theory]
    [InlineData("/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi")]
    [InlineData("nie adres")]
    [InlineData("")]
    public void DownloadUrlNotAbsolute_Refused(string url)
    {
        var json = Latest();
        MsiOf(json)["browser_download_url"] = url;

        Assert.Equal("błędna odpowiedź GitHuba", Parse(json).Refusal);
    }

    [Fact]
    public void DownloadUrlForeign_ParsedButNotAllowed()
    {
        // Adres ocenia IsAllowedDownloadUrl (Task 2: osobny powód odmowy), nie parser.
        var json = Latest();
        MsiOf(json)["browser_download_url"] = "ftp://evil.example/rustdesk-1.5.0-x86_64.msi";

        var (ok, release, _) = Parse(json);

        Assert.True(ok);
        Assert.False(RustDeskSource.IsAllowedDownloadUrl(release!.DownloadUrl, release));
    }

    [Theory]
    [InlineData("\"prerelease\":false", "\"prerelease\":true,\"prerelease\":false")]
    [InlineData("\"draft\":false", "\"draft\":true,\"draft\":false")]
    [InlineData("\"tag_name\":\"1.5.0\"", "\"tag_name\":\"nightly\",\"tag_name\":\"1.5.0\"")]
    [InlineData("\"size\":26345472", "\"size\":1,\"size\":26345472")]
    public void DuplicateProperty_Refused(string original, string duplicated)
    {
        var text = Latest().ToString(Formatting.None);
        Assert.Contains(original, text, StringComparison.Ordinal);

        Assert.Equal("błędna odpowiedź GitHuba", Parse(text.Replace(original, duplicated)).Refusal);
    }

    [Fact]
    public void AssetNotObject_Refused()
    {
        var json = Latest();
        ((JArray)json["assets"]!).Add("rustdesk-1.5.0-x86_64.msi");

        Assert.Equal("błędna odpowiedź GitHuba", Parse(json).Refusal);
    }

    [Fact]
    public void DuplicateMsiAsset_Refused()
    {
        var json = Latest();
        ((JArray)json["assets"]!).Add(Asset(MsiName, 1000, "sha256:" + new string('2', 64), MsiUrl));

        Assert.Equal("błędna odpowiedź GitHuba", Parse(json).Refusal);
    }

    [Theory]
    [InlineData("v1.5.0")]
    [InlineData("1.5.0-rc1")]
    [InlineData("nightly")]
    [InlineData("1.5")]
    [InlineData("1.5.0.1")]
    [InlineData("1.5.0\n")]
    [InlineData("1.٥.0")]
    public void TagNotNumeric_Refused(string tag)
    {
        var (ok, release, refusal) = Parse(Latest(tag: tag));

        Assert.False(ok);
        Assert.Null(release);
        Assert.Equal("nieoczekiwany numer wersji", refusal);
    }

    [Fact]
    public void NoMsiAsset_ReleaseNullNoRefusal()
    {
        var json = Latest();
        MsiOf(json).Remove();

        var (ok, release, refusal) = Parse(json);

        Assert.False(ok);
        Assert.Null(release);
        Assert.Null(refusal);
    }

    [Fact]
    public void MsiOfOtherTag_ReleaseNullNoRefusal()
    {
        var json = Latest();
        MsiOf(json)["name"] = "rustdesk-1.4.9-x86_64.msi";

        var (ok, release, refusal) = Parse(json);

        Assert.False(ok);
        Assert.Null(release);
        Assert.Null(refusal);
    }

    [Theory]
    [InlineData("rustdesk-1.5.0-aarch64.msi")]
    [InlineData("RustDesk-1.5.0-x86_64.msi")]
    [InlineData("rustdesk-1.5.0-x86_64.MSI")]
    [InlineData("rustdesk-1.5.0-x86_64.msi ")]
    public void MsiNameNotExact_ReleaseNullNoRefusal(string name)
    {
        var json = Latest();
        MsiOf(json)["name"] = name;

        var (ok, release, refusal) = Parse(json);

        Assert.False(ok);
        Assert.Null(release);
        Assert.Null(refusal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha1:ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12")]
    [InlineData("sha256:xyz")]
    [InlineData("sha256:624fe792ab1d76b35134dab84bff97a6bcd14043ffd090a2c6a2800f7758049")]
    [InlineData("sha256:624fe792ab1d76b35134dab84bff97a6bcd14043ffd090a2c6a2800f775804955")]
    [InlineData("SHA256:624fe792ab1d76b35134dab84bff97a6bcd14043ffd090a2c6a2800f77580495")]
    [InlineData("624fe792ab1d76b35134dab84bff97a6bcd14043ffd090a2c6a2800f77580495")]
    [InlineData("")]
    public void DigestMissingOrMalformed_Refused(string? digest)
    {
        var json = Latest();
        MsiOf(json)["digest"] = digest is null ? JValue.CreateNull() : digest;

        var (ok, release, refusal) = Parse(json);

        Assert.False(ok);
        Assert.Null(release);
        Assert.Equal("brak skrótu SHA256", refusal);
    }

    [Fact]
    public void DigestFieldAbsent_Refused()
    {
        var json = Latest();
        MsiOf(json).Remove("digest");

        Assert.Equal("brak skrótu SHA256", Parse(json).Refusal);
    }

    [Theory]
    [InlineData(209715201L)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void SizeOutOfRange_Refused(long size)
    {
        var json = Latest();
        MsiOf(json)["size"] = size;

        var (ok, release, refusal) = Parse(json);

        Assert.False(ok);
        Assert.Null(release);
        Assert.Equal("plik większy niż 200 MB", refusal);
    }

    [Theory]
    [InlineData(MsiUrl, true)]
    [InlineData("https://GITHUB.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", true)]
    [InlineData("https://github.com:443/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", true)]
    [InlineData("http://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com.evil.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://api.github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://objects.githubusercontent.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com/rustdesk/rustdesk-server/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://x@github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com:8443/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.4.9/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi?x=1", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi#x", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.exe", false)]
    [InlineData("https://github.com/RustDesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com/evil/x/../../rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", true)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0%2Frustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi/", false)]
    [InlineData("https://\uFF47ithub.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com./rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi?", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi#", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/%2E%2E/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com/evil/%2E%2E/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com/%72ustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64%2Emsi", false)]
    public void IsAllowedDownloadUrl(string url, bool expected)
    {
        Assert.Equal(expected, RustDeskSource.IsAllowedDownloadUrl(new Uri(url), Release150));
    }

    [Fact]
    public void IsAllowedDownloadUrl_RelativeOrNull_False()
    {
        Assert.False(RustDeskSource.IsAllowedDownloadUrl(new Uri("/rustdesk/rustdesk/releases/download/1.5.0/" + MsiName, UriKind.Relative), Release150));
        Assert.False(RustDeskSource.IsAllowedDownloadUrl(null!, Release150));
        Assert.False(RustDeskSource.IsAllowedDownloadUrl(new Uri(MsiUrl), null!));
    }

    [Theory]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/abc?sp=r&sig=x", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/1/abc?X-Amz=1", true)]
    [InlineData("https://release-assets.githubusercontent.com:443/x", true)]
    [InlineData("http://release-assets.githubusercontent.com/x", false)]
    [InlineData("https://evil.githubusercontent.com/x", false)]
    [InlineData("https://release-assets.githubusercontent.com.evil.com/x", false)]
    [InlineData("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi", false)]
    [InlineData("https://release-assets.githubusercontent.com:444/x", false)]
    [InlineData("https://user@release-assets.githubusercontent.com/x", false)]
    [InlineData("ftp://objects.githubusercontent.com/x", false)]
    [InlineData("https://\uFF4Fbjects.githubusercontent.com/x", false)]
    [InlineData("https://release-assets.githubusercontent.com./x", false)]
    [InlineData("https://objects.githubusercontent.com./x", false)]
    public void IsAllowedRedirect(string url, bool expected)
    {
        Assert.Equal(expected, RustDeskSource.IsAllowedRedirect(new Uri(url)));
    }

    [Fact]
    public void IsAllowedRedirect_RelativeOrNull_False()
    {
        Assert.False(RustDeskSource.IsAllowedRedirect(new Uri("/x", UriKind.Relative)));
        Assert.False(RustDeskSource.IsAllowedRedirect(null!));
    }

    [Fact]
    public void IsTrustedSigner_SpikeSubject_True()
    {
        Assert.True(RustDeskSource.IsTrustedSigner(SpikeSubject));
    }

    [Theory]
    [InlineData("O=PURSLANE, C=SG, SERIALNUMBER=53481265A")]
    [InlineData("serialNumber=53481265A,o=PURSLANE,c=SG")]
    [InlineData("  O = PURSLANE ,  C=SG , SERIALNUMBER=53481265A  ")]
    [InlineData("CN=\"PURSLANE, Ltd\", O=\"PURSLANE\", C=SG, SERIALNUMBER=53481265A")]
    [InlineData("CN=\"A \"\"quoted\"\" + name\", O=PURSLANE, C=SG, SERIALNUMBER=53481265A")]
    public void IsTrustedSigner_ValidVariants_True(string subject)
    {
        Assert.True(RustDeskSource.IsTrustedSigner(subject));
    }

    [Fact]
    public void IsTrustedSigner_ZlatinStamatov_False()
        => Assert.False(RustDeskSource.IsTrustedSigner("CN=Zlatin Stamatov, O=Zlatin Stamatov, L=Sofia, C=BG"));

    [Fact]
    public void IsTrustedSigner_Microsoft_False()
        => Assert.False(RustDeskSource.IsTrustedSigner("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));

    [Fact]
    public void IsTrustedSigner_MissingSerial_False()
        => Assert.False(RustDeskSource.IsTrustedSigner("CN=PURSLANE, O=PURSLANE, S=North West, C=SG"));

    [Fact]
    public void IsTrustedSigner_OtherSerial_False()
        => Assert.False(RustDeskSource.IsTrustedSigner(SpikeSubject.Replace("SERIALNUMBER=53481265A", "SERIALNUMBER=53481265B")));

    [Fact]
    public void IsTrustedSigner_CountryUs_False()
        => Assert.False(RustDeskSource.IsTrustedSigner(SpikeSubject.Replace("C=SG", "C=US")));

    [Fact]
    public void IsTrustedSigner_ValueCase_False()
        => Assert.False(RustDeskSource.IsTrustedSigner(SpikeSubject.Replace("O=PURSLANE", "O=purslane")));

    [Fact]
    public void IsTrustedSigner_DuplicateO_False()
    {
        Assert.False(RustDeskSource.IsTrustedSigner(SpikeSubject + ", O=Evil"));
        Assert.False(RustDeskSource.IsTrustedSigner(SpikeSubject + ", O=PURSLANE"));
        Assert.False(RustDeskSource.IsTrustedSigner(SpikeSubject + ", c=SG"));
        Assert.False(RustDeskSource.IsTrustedSigner(SpikeSubject + ", SERIALNUMBER=53481265A"));
    }

    [Theory]
    [InlineData("CN=\"X, O=PURSLANE, C=SG, SERIALNUMBER=53481265A\", O=Evil, C=SG")]
    [InlineData("CN=\"X, O=PURSLANE, C=SG, SERIALNUMBER=53481265A\"")]
    [InlineData("CN=\"X\"\", O=PURSLANE, C=SG, SERIALNUMBER=53481265A\", O=Evil")]
    [InlineData("O=\"PURSLANE\" , C=\"SG\", SERIALNUMBER=\"53481265A\", CN=\"unterminated")]
    [InlineData("O=PURSLANE\"x\", C=SG, SERIALNUMBER=53481265A")]
    [InlineData("O=\"PURSLANE\"x, C=SG, SERIALNUMBER=53481265A")]
    [InlineData("O=\"PURS\"LANE\", C=SG, SERIALNUMBER=53481265A")]
    public void IsTrustedSigner_QuotedInjection_False(string subject)
    {
        Assert.False(RustDeskSource.IsTrustedSigner(subject));
    }

    [Theory]
    [InlineData("CN=Evil + O=PURSLANE, C=SG, SERIALNUMBER=53481265A")]
    [InlineData("O=PURSLANE + C=SG, SERIALNUMBER=53481265A")]
    [InlineData("O=PURSLANE, C=SG, SERIALNUMBER=53481265A+CN=x")]
    public void IsTrustedSigner_PlusMultiValued_False(string subject)
    {
        Assert.False(RustDeskSource.IsTrustedSigner(subject));
    }

    [Theory]
    [InlineData("O=PURSLANE, C=SG, SERIALNUMBER=53481265A,")]
    [InlineData(", O=PURSLANE, C=SG, SERIALNUMBER=53481265A")]
    [InlineData("O=PURSLANE,, C=SG, SERIALNUMBER=53481265A")]
    [InlineData("O=PURSLANE, C=SG, SERIALNUMBER=53481265A, garbage")]
    [InlineData("O=PURSLANE, =SG, C=SG, SERIALNUMBER=53481265A")]
    [InlineData("O=PURSLANE C=SG, SERIALNUMBER=53481265A")]
    [InlineData("CN=X\\, O=PURSLANE, C=SG, SERIALNUMBER=53481265A")]
    [InlineData("O=PURS\\LANE, C=SG, SERIALNUMBER=53481265A")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTrustedSigner_SyntaxError_False(string subject)
    {
        Assert.False(RustDeskSource.IsTrustedSigner(subject));
    }

    [Fact]
    public void IsTrustedSigner_Null_False()
        => Assert.False(RustDeskSource.IsTrustedSigner(null));
}
