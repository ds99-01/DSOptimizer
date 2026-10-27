using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Folder pobrań pod <see cref="SecureRoot"/> w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>.</summary>
[Trait("Category", "Windows")]
public class DownloadFolderTests
{
    [Fact]
    public void DownloadsDirectory_UnderRoot()
    {
        using var temp = new TempDirectory();

        Assert.Equal("downloads", SecureRoot.DownloadsLeafName);
        Assert.Equal(Path.Combine(temp.Sub("app"), "downloads"), temp.AppRoot.DownloadsDirectory);
    }

    [Fact]
    public void Constructor_DoesNotTouchDisk()
    {
        using var temp = new TempDirectory();

        _ = new DownloadFolder(temp.AppRoot, new NullLog());

        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void NewDownloadPath_UnderDownloads_GuidMsi()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        var folder = new DownloadFolder(root, new NullLog());

        var first = folder.NewDownloadPath(".msi");
        var second = folder.NewDownloadPath(".msi");

        Assert.Equal(root.DownloadsDirectory, Path.GetDirectoryName(first));
        Assert.Matches(new Regex("^[0-9a-f]{32}\\.msi$"), Path.GetFileName(first));
        Assert.NotEqual(first, second);
        Assert.False(File.Exists(first));
        Assert.True(SecureDirectory.IsTrustedDirectory(root.DownloadsDirectory));
        Assert.True(SecureDirectory.IsTrustedDirectory(root.FullPath));
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData("msi")]
    [InlineData(".MSI")]
    [InlineData(".PS1")]
    [InlineData("ps1")]
    [InlineData("")]
    [InlineData(@"..\x.msi")]
    public void NewDownloadPath_OtherExtension_Throws(string extension)
    {
        using var temp = new TempDirectory();

        Assert.Throws<ArgumentException>(() => new DownloadFolder(temp.AppRoot, new NullLog()).NewDownloadPath(extension));
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void NewDownloadPath_UntrustedDownloads_SetAsideAndWarns()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        root.EnsureLeaf(root.DownloadsDirectory);
        File.WriteAllText(Path.Combine(root.DownloadsDirectory, "stary.msi"), "x");
        ForeignAcl.Apply(root.DownloadsDirectory);
        var log = new CollectingLog();

        var path = new DownloadFolder(root, log).NewDownloadPath(".msi");

        var aside = Assert.Single(Directory.GetDirectories(root.FullPath, "downloads.untrusted-*"));
        Assert.True(File.Exists(Path.Combine(aside, "stary.msi")));
        Assert.Contains(log.Warnings, w => w.Contains(aside));
        Assert.True(SecureDirectory.IsTrustedDirectory(root.DownloadsDirectory));
        Assert.Equal(root.DownloadsDirectory, Path.GetDirectoryName(path));
    }

    [Fact]
    public void NewDownloadPath_DownloadsIsJunction_ThrowsIOException_NothingInTarget()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        root.Ensure();
        Directory.CreateDirectory(temp.Sub("cel"));
        Junction.Create(root.DownloadsDirectory, temp.Sub("cel"));
        try
        {
            Assert.ThrowsAny<IOException>(() => new DownloadFolder(root, new NullLog()).NewDownloadPath(".msi"));
            Assert.Empty(Directory.GetFileSystemEntries(temp.Sub("cel")));
        }
        finally
        {
            Directory.Delete(root.DownloadsDirectory, recursive: false);
        }
    }

    [Fact]
    public void Sha256_KnownBytes()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("abc.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));

        var hash = new DownloadFolder(temp.AppRoot, new NullLog()).Sha256(path);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public void Sha256_MissingFile_Throws()
    {
        using var temp = new TempDirectory();

        Assert.Throws<FileNotFoundException>(() => new DownloadFolder(temp.AppRoot, new NullLog()).Sha256(temp.Sub("brak.bin")));
    }

    [Theory]
    [InlineData(@"inny\x.msi")]
    [InlineData(@"app\x.msi")]
    [InlineData(@"app\downloads\sub\x.msi")]
    [InlineData(@"app\downloads\..\x.msi")]
    [InlineData(@"app\downloads")]
    public void TryDelete_OutsideDownloads_Throws(string relative)
    {
        using var temp = new TempDirectory();
        var path = temp.Sub(relative);

        Assert.Throws<ArgumentException>(() => new DownloadFolder(temp.AppRoot, new NullLog()).TryDelete(path));
    }

    [Fact]
    public void TryDelete_Missing_True()
    {
        using var temp = new TempDirectory();
        var folder = new DownloadFolder(temp.AppRoot, new NullLog());

        Assert.True(folder.TryDelete(Path.Combine(temp.AppRoot.DownloadsDirectory, "brak.msi")));
    }

    [Fact]
    public void TryDelete_Existing_DeletedTrue()
    {
        using var temp = new TempDirectory();
        var folder = new DownloadFolder(temp.AppRoot, new NullLog());
        var path = folder.NewDownloadPath(".msi");
        File.WriteAllText(path, "msi");

        Assert.True(folder.TryDelete(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TryDelete_FileInUse_FalseAndWarns()
    {
        using var temp = new TempDirectory();
        var log = new CollectingLog();
        var folder = new DownloadFolder(temp.AppRoot, log);
        var path = folder.NewDownloadPath(".msi");
        File.WriteAllText(path, "msi");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(folder.TryDelete(path));
        }

        Assert.True(File.Exists(path));
        Assert.Contains(log.Warnings, w => w.Contains(path));
    }

    // --- M4 final review: sprzątanie osieroconych pobrań (limit msiexec, zabity program) ---

    private static string Orphan(SecureRoot root, string name, TimeSpan age)
    {
        root.EnsureLeaf(root.DownloadsDirectory);
        var path = Path.Combine(root.DownloadsDirectory, name);
        File.WriteAllText(path, "msi");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    [Fact]
    public void NewDownloadPath_OldMsiDeleted_FreshAndOtherKept()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        var old = Orphan(root, "0123456789abcdef0123456789abcdef.msi", TimeSpan.FromHours(25));
        var fresh = Orphan(root, "fedcba9876543210fedcba9876543210.msi", TimeSpan.FromHours(23));
        var msix = Orphan(root, "stary.msix", TimeSpan.FromDays(3));
        var txt = Orphan(root, "stary.txt", TimeSpan.FromDays(3));
        var subfolder = Path.Combine(root.DownloadsDirectory, "sub.msi");
        Directory.CreateDirectory(subfolder);
        var nested = Path.Combine(subfolder, "w-srodku.msi");
        File.WriteAllText(nested, "msi");
        File.SetLastWriteTimeUtc(nested, DateTime.UtcNow - TimeSpan.FromDays(3));
        Directory.SetLastWriteTimeUtc(subfolder, DateTime.UtcNow - TimeSpan.FromDays(3));
        var log = new CollectingLog();

        var path = new DownloadFolder(root, log).NewDownloadPath(".msi");

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(msix));
        Assert.True(File.Exists(txt));
        Assert.True(File.Exists(nested));
        Assert.Equal(root.DownloadsDirectory, Path.GetDirectoryName(path));
        Assert.Contains(log.Infos, line => line.Contains(old));
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public void NewDownloadPath_OldMsiLocked_WarnsAndReturnsPath()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        var old = Orphan(root, "0123456789abcdef0123456789abcdef.msi", TimeSpan.FromDays(2));
        var log = new CollectingLog();
        string path;

        using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            path = new DownloadFolder(root, log).NewDownloadPath(".msi");
        }

        Assert.True(File.Exists(old));
        Assert.Contains(log.Warnings, w => w.Contains(old));
        Assert.Equal(root.DownloadsDirectory, Path.GetDirectoryName(path));
    }

    // --- Etap 5: skrypt .ps1 (WinUtil) w folderze pobrań ---

    [Fact]
    public void NewDownloadPath_Ps1_ReturnsGuidPathInDownloads()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;

        var path = new DownloadFolder(root, new NullLog()).NewDownloadPath(".ps1");

        Assert.Equal(root.DownloadsDirectory, Path.GetDirectoryName(path));
        Assert.Matches(new Regex("^[0-9a-f]{32}\\.ps1$"), Path.GetFileName(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void NewDownloadPath_UpperCasePs1_Throws()
    {
        using var temp = new TempDirectory();

        Assert.Throws<ArgumentException>(() => new DownloadFolder(temp.AppRoot, new NullLog()).NewDownloadPath(".PS1"));
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void NewDownloadPath_Exe_Throws()
    {
        using var temp = new TempDirectory();

        Assert.Throws<ArgumentException>(() => new DownloadFolder(temp.AppRoot, new NullLog()).NewDownloadPath(".exe"));
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void NewDownloadPath_DeletesOrphanPs1OlderThan24h()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        var old = Orphan(root, "0123456789abcdef0123456789abcdef.ps1", TimeSpan.FromHours(25));
        var log = new CollectingLog();

        new DownloadFolder(root, log).NewDownloadPath(".ps1");

        Assert.False(File.Exists(old));
        Assert.Contains(log.Infos, line => line.Contains(old));
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public void NewDownloadPath_KeepsPs1YoungerThan24h()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        var fresh = Orphan(root, "fedcba9876543210fedcba9876543210.ps1", TimeSpan.FromHours(23));

        new DownloadFolder(root, new NullLog()).NewDownloadPath(".ps1");

        Assert.True(File.Exists(fresh));
    }

    private sealed class CollectingLog : ILog
    {
        public List<string> Warnings { get; } = new();

        public List<string> Infos { get; } = new();

        public void Info(string component, string message) => Infos.Add(message);

        public void Warn(string component, string message) => Warnings.Add(message);

        public void Fail(string component, string message, Exception? exception = null)
        {
        }
    }
}
