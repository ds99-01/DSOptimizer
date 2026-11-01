using System.IO;
using System.Xml.Linq;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Branding;

/// <summary>Wersja w <c>app.manifest</c> musi zgadzać się z <c>Version</c> w <c>Directory.Build.props</c> (numer z końcowym <c>.0</c>).</summary>
public class AppManifestVersionTests
{
    [Fact]
    public void ManifestVersion_MatchesDirectoryBuildPropsVersion()
    {
        var props = XDocument.Load(RepoRoot.Combine("Directory.Build.props"));
        var version = Assert.Single(props.Descendants("Version")).Value.Trim();

        var manifest = XDocument.Load(RepoRoot.Combine("src", "DSOptimizer.App", "app.manifest"));
        XNamespace asm = "urn:schemas-microsoft-com:asm.v1";
        var identity = Assert.Single(manifest.Descendants(asm + "assemblyIdentity"));

        Assert.Equal(version + ".0", (string?)identity.Attribute("version"));
        Assert.True(File.Exists(RepoRoot.Combine("src", "DSOptimizer.App", "app.manifest")));
    }
}
