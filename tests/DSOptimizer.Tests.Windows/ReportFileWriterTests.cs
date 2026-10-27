using System.IO;
using System.Linq;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class ReportFileWriterTests
{
    [Fact]
    public void Save_NoMirror_WritesUtf8BomCrlfUnderReports()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        var writer = new ReportFileWriter(root, null);

        var results = writer.Save("specyfikacja-test.txt", "a\nb");

        var result = Assert.Single(results);
        Assert.True(result.Saved);
        Assert.Null(result.Error);
        Assert.Equal(Path.Combine(root.ReportsDirectory, "specyfikacja-test.txt"), result.Path);
        var bytes = File.ReadAllBytes(result.Path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Equal("a\r\nb", File.ReadAllText(result.Path));
        Assert.True(SecureDirectory.IsTrustedDirectory(root.ReportsDirectory));
    }

    [Fact]
    public void Save_WithMirror_WritesBothCopies()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        var mirror = temp.Sub("pendrive");
        var writer = new ReportFileWriter(root, mirror);

        var results = writer.Save("r.txt", "x\r\ny\nz");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Saved));
        Assert.Equal(Path.Combine(root.ReportsDirectory, "r.txt"), results[0].Path);
        Assert.Equal(Path.Combine(mirror, "r.txt"), results[1].Path);
        Assert.Equal("x\r\ny\r\nz", File.ReadAllText(results[0].Path));
        Assert.Equal(File.ReadAllBytes(results[0].Path), File.ReadAllBytes(results[1].Path));
    }

    [Fact]
    public void Save_MirrorUnwritable_PrimarySavedMirrorReportsError()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        var blockingFile = temp.Sub("to-jest-plik");
        File.WriteAllText(blockingFile, "x");
        var writer = new ReportFileWriter(root, blockingFile);

        var results = writer.Save("r.txt", "tekst");

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Saved);
        Assert.False(results[1].Saved);
        Assert.False(string.IsNullOrWhiteSpace(results[1].Error));
    }

    [Fact]
    public void Save_SameNameTwice_Overwrites()
    {
        using var temp = new TempDirectory();
        var writer = new ReportFileWriter(temp.AppRoot, null);

        writer.Save("r.txt", "pierwszy dluzszy tekst");
        var results = writer.Save("r.txt", "drugi");

        Assert.True(results[0].Saved);
        Assert.Equal("drugi", File.ReadAllText(results[0].Path));
    }
}
