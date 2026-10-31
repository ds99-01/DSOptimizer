using System.IO;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class MirrorPathGuardTests
{
    [Fact]
    public void EnsureDirectory_NormalPath_CreatesIt()
    {
        using var temp = new TempDirectory();
        var mirror = Path.Combine(temp.Sub("exe"), "logs", "PC");

        MirrorPathGuard.EnsureDirectory(mirror);

        Assert.True(Directory.Exists(mirror));
    }

    [Fact]
    public void EnsureDirectory_JunctionOnTheWay_RefusesAndWritesNothingToTarget()
    {
        using var temp = new TempDirectory();
        var target = temp.Sub("cel");
        Directory.CreateDirectory(target);
        var exe = temp.Sub("exe");
        Directory.CreateDirectory(exe);
        Junction.Create(Path.Combine(exe, "logs"), target);

        Assert.Throws<IOException>(() => MirrorPathGuard.EnsureDirectory(Path.Combine(exe, "logs", "PC")));

        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Fact]
    public void EnsureDirectory_MirrorItselfIsJunction_Refuses()
    {
        using var temp = new TempDirectory();
        var target = temp.Sub("cel");
        Directory.CreateDirectory(target);
        var logs = Path.Combine(temp.Sub("exe"), "logs");
        Directory.CreateDirectory(logs);
        Junction.Create(Path.Combine(logs, "PC"), target);

        Assert.Throws<IOException>(() => MirrorPathGuard.EnsureDirectory(Path.Combine(logs, "PC")));
    }

    [Fact]
    public void FileLog_JunctionMirror_WritesOnlyPrimary()
    {
        using var temp = new TempDirectory();
        var target = temp.Sub("cel");
        Directory.CreateDirectory(target);
        var exe = temp.Sub("exe");
        Directory.CreateDirectory(exe);
        Junction.Create(Path.Combine(exe, "logs"), target);
        var log = new FileLog(temp.AppRoot, Path.Combine(exe, "logs", "PC"), new FixedClock());

        log.Warn("Test", "linia");

        Assert.Empty(Directory.GetFileSystemEntries(target));
        Assert.Single(Directory.GetFiles(temp.AppRoot.LogsDirectory));
    }

    [Fact]
    public void ReportFileWriter_JunctionMirror_PrimarySavedMirrorReportsError()
    {
        using var temp = new TempDirectory();
        var target = temp.Sub("cel");
        Directory.CreateDirectory(target);
        var exe = temp.Sub("exe");
        Directory.CreateDirectory(exe);
        Junction.Create(Path.Combine(exe, "logs"), target);
        var writer = new ReportFileWriter(temp.AppRoot, Path.Combine(exe, "logs", "PC"));

        var results = writer.Save("r.txt", "tekst");

        Assert.True(results[0].Saved);
        Assert.False(results[1].Saved);
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }
}
