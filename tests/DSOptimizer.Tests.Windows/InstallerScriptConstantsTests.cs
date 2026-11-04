using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DSOptimizer.Windows;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Stałe wspólne dla programu i instalatora czytane z <c>tools/release/DSOptimizer.iss</c> w repo: nazwa mutexa programu
/// (<c>CheckForMutexes</c>) i nazwa wpisu Uninstall (chroniony wiersz <c>prot.dsoptimizer</c> w <c>apps-bloat.json</c>).
/// Testy Pestera instalatora nie idą w CI, więc zgodność pilnuje xUnit.
/// </summary>
[Trait("Category", "Windows")]
public class InstallerScriptConstantsTests
{
    [Fact]
    public void IssProgramMutex_EqualsProgramMutexName()
    {
        var iss = ReadIss();
        var define = Regex.Match(iss, @"^#define\s+ProgramMutex\s+""(?<name>[^""]+)""\s*$", RegexOptions.Multiline);

        Assert.True(define.Success, "Brak #define ProgramMutex w DSOptimizer.iss.");
        Assert.Equal(SingleInstanceGuard.ProgramMutexName, define.Groups["name"].Value);
        Assert.Contains("CheckForMutexes('{#ProgramMutex}')", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void IssUninstallDisplayName_EqualsProtectedRowOfAppsBloat()
    {
        var setting = Regex.Match(ReadIss(), @"^UninstallDisplayName=(?<name>.+?)\s*$", RegexOptions.Multiline);
        Assert.True(setting.Success, "Brak UninstallDisplayName w DSOptimizer.iss.");

        var bloat = JObject.Parse(File.ReadAllText(RepoFile("src", "DSOptimizer.Catalog", "catalog", "apps-bloat.json"), Encoding.UTF8));
        var row = Assert.Single(bloat.Descendants().OfType<JObject>(), o => (string?)o["id"] == "prot.dsoptimizer");

        Assert.Equal(setting.Groups["name"].Value, (string?)row["match"]?["displayName"]);
    }

    private static string ReadIss() => File.ReadAllText(RepoFile("tools", "release", "DSOptimizer.iss"), Encoding.UTF8);

    /// <summary>Plik repozytorium: katalog z <c>DSOptimizer.sln</c> nad katalogiem wyjściowym testów.</summary>
    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DSOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }
}
