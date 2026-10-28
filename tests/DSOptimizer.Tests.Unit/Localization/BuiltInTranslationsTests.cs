using System;
using System.IO;
using System.Linq;
using DSOptimizer.Core.Localization;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Localization;

public class BuiltInTranslationsTests
{
    [Fact]
    public void Resources_MatchFilesOnDisk()
    {
        var root = RepoRoot.Combine("src", "DSOptimizer.Core", "Localization", "Translations");
        var onDisk = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(f => "DSOptimizer.Core.Translations." + Path.GetFileName(f))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        var embedded = typeof(L).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("DSOptimizer.Core.Translations.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.NotEmpty(onDisk);
        Assert.True(
            onDisk.SequenceEqual(embedded, StringComparer.Ordinal),
            "Pliki na dysku: [" + string.Join(", ", onDisk) + "], osadzone: [" + string.Join(", ", embedded) + "]");
    }

    [Fact]
    public void BuiltIn_LoadsWithoutErrors()
    {
        var table = TranslationTable.Load(L.BuiltInSources());
        Assert.True(table.Count >= 0);
    }

    [Fact]
    public void LoadWarning_IsNull()
    {
        _ = L.T("x");
        Assert.Null(L.LoadWarning);
    }
}
