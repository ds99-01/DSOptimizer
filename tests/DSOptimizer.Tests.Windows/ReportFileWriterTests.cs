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
    public void Save_ChosenPath_Utf8BomCrlf_NoReportsFolder()
    {
        using var temp = new TempDirectory();
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var target = Path.Combine(pulpit, "specyfikacja-test.txt");
        var writer = new ReportFileWriter(temp.AppRoot, null);

        var results = writer.Save(target, "a\nb");

        var result = Assert.Single(results);
        Assert.True(result.Saved);
        Assert.Null(result.Error);
        Assert.Equal(target, result.Path);
        var bytes = File.ReadAllBytes(target);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Equal("a\r\nb", File.ReadAllText(target));
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void Save_WithMirror_SameNameNextToExe()
    {
        using var temp = new TempDirectory();
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var mirror = temp.Sub("pendrive");
        var writer = new ReportFileWriter(temp.AppRoot, mirror);

        var results = writer.Save(Path.Combine(pulpit, "specyfikacja-test.txt"), "x\r\ny\nz");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Saved));
        Assert.Equal(Path.Combine(mirror, "specyfikacja-test.txt"), results[1].Path);
        Assert.Equal("x\r\ny\r\nz", File.ReadAllText(results[0].Path));
        Assert.Equal(File.ReadAllBytes(results[0].Path), File.ReadAllBytes(results[1].Path));
    }

    [Fact]
    public void Save_MirrorUnwritable_ChosenSavedMirrorError()
    {
        using var temp = new TempDirectory();
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var blockingFile = temp.Sub("to-jest-plik");
        File.WriteAllText(blockingFile, "x");
        var writer = new ReportFileWriter(temp.AppRoot, blockingFile);

        var results = writer.Save(Path.Combine(pulpit, "r.txt"), "tekst");

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Saved);
        Assert.False(results[1].Saved);
        Assert.False(string.IsNullOrWhiteSpace(results[1].Error));
    }

    [Fact]
    public void Save_ExistingLongerFile_OverwrittenCompletely()
    {
        using var temp = new TempDirectory();
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var target = Path.Combine(pulpit, "r.txt");
        var writer = new ReportFileWriter(temp.AppRoot, null);

        writer.Save(target, "pierwszy dluzszy tekst");
        var results = writer.Save(target, "drugi");

        Assert.True(results[0].Saved);
        Assert.Equal("drugi", File.ReadAllText(target));
    }

    [Theory]
    [InlineData("r.txt")]
    [InlineData(@"logs\r.txt")]
    public void Save_InsideProgramFolder_RefusedNothingCreated(string relative)
    {
        using var temp = new TempDirectory();
        var mirror = temp.Sub("pendrive");
        var writer = new ReportFileWriter(temp.AppRoot, mirror);
        var target = Path.Combine(temp.AppRoot.FullPath, relative);

        var results = writer.Save(target, "tekst");

        var result = Assert.Single(results);
        Assert.False(result.Saved);
        Assert.Equal(ReportFileWriter.ProtectedLocationReason, result.Error);
        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(mirror));
    }

    [Fact]
    public void Save_LoopbackAdminShare_RefusedWithoutIo()
    {
        using var temp = new TempDirectory();
        var writer = new ReportFileWriter(temp.AppRoot, null);

        var result = Assert.Single(writer.Save(@"\\localhost\C$\ProgramData\DSOptimizer\r.txt", "tekst"));

        Assert.False(result.Saved);
        Assert.Equal(ReportFileWriter.ProtectedLocationReason, result.Error);
    }

    [Fact]
    public void Save_JunctionAncestor_RefusedNothingInTarget()
    {
        using var temp = new TempDirectory();
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var cel = temp.Sub("cel");
        Directory.CreateDirectory(cel);
        Junction.Create(temp.Sub(@"pulpit\j"), cel);
        var writer = new ReportFileWriter(temp.AppRoot, null);

        var result = Assert.Single(writer.Save(temp.Sub(@"pulpit\j\r.txt"), "tekst"));

        Assert.False(result.Saved);
        Assert.Equal(ReportFileWriter.LinkReason(temp.Sub(@"pulpit\j")), result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(cel));
    }

    [Fact]
    public void Save_TargetIsJunction_Refused()
    {
        using var temp = new TempDirectory();
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var cel = temp.Sub("cel");
        Directory.CreateDirectory(cel);
        var target = temp.Sub(@"pulpit\r.txt");
        Junction.Create(target, cel);
        var writer = new ReportFileWriter(temp.AppRoot, null);

        var result = Assert.Single(writer.Save(target, "tekst"));

        Assert.False(result.Saved);
        Assert.Equal(ReportFileWriter.LinkReason(target), result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(cel));
    }

    [Fact]
    public void SaveCopy_SourceOpenForWriting_Copies()
    {
        using var temp = new TempDirectory();
        var source = temp.Sub(@"app\logs\x.log");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, new byte[] { 1, 2, 3, 0xEF, 0xBB });
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var target = Path.Combine(pulpit, "x.log");
        var writer = new ReportFileWriter(temp.AppRoot, null);

        ReportSaveResult result;
        using (new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            result = writer.SaveCopy(source, target);
        }

        Assert.True(result.Saved);
        Assert.Null(result.Error);
        Assert.Equal(target, result.Path);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
    }

    [Fact]
    public void SaveCopy_TargetInsideProgramFolder_Refused()
    {
        using var temp = new TempDirectory();
        var source = temp.Sub("x.log");
        File.WriteAllText(source, "log");
        var writer = new ReportFileWriter(temp.AppRoot, null);
        var target = Path.Combine(temp.AppRoot.FullPath, "x.log");

        var result = writer.SaveCopy(source, target);

        Assert.False(result.Saved);
        Assert.Equal(ReportFileWriter.ProtectedLocationReason, result.Error);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void SaveCopy_JunctionAncestor_Refused()
    {
        using var temp = new TempDirectory();
        var source = temp.Sub("x.log");
        File.WriteAllText(source, "log");
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var cel = temp.Sub("cel");
        Directory.CreateDirectory(cel);
        Junction.Create(temp.Sub(@"pulpit\j"), cel);
        var writer = new ReportFileWriter(temp.AppRoot, null);

        var result = writer.SaveCopy(source, temp.Sub(@"pulpit\j\x.log"));

        Assert.False(result.Saved);
        Assert.Equal(ReportFileWriter.LinkReason(temp.Sub(@"pulpit\j")), result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(cel));
    }
}
