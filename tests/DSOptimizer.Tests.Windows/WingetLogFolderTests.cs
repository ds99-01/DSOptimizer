using System;
using System.IO;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Ścieżki logów winget (<c>--log</c>): tylko katalog w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>, winget nie startuje.</summary>
[Trait("Category", "Windows")]
public class WingetLogFolderTests
{
    [Fact]
    public void NewLogPath_UnderWingetFolder_IdAndUtcTimeInName_FolderCreatedAndTrusted()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        var folder = new WingetLogFolder(root, new NullLog());
        var at = new DateTimeOffset(2026, 10, 24, 23, 5, 9, TimeSpan.FromHours(2));

        var path = folder.NewLogPath("Notepad++.Notepad++", at);

        var expectedFolder = Path.Combine(root.LogsDirectory, "winget");
        Assert.Equal(expectedFolder, folder.FolderPath);
        Assert.Equal(Path.Combine(expectedFolder, "Notepad++.Notepad++-20261024-210509.log"), path);
        Assert.True(Directory.Exists(expectedFolder));
        Assert.True(SecureDirectory.IsTrustedDirectory(expectedFolder));
        Assert.True(SecureDirectory.IsTrustedDirectory(root.LogsDirectory));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Constructor_DoesNotTouchDisk()
    {
        using var temp = new TempDirectory();

        _ = new WingetLogFolder(temp.AppRoot, new NullLog());

        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Theory]
    [InlineData(@"..\x")]
    [InlineData("")]
    [InlineData("Some.App…")]
    [InlineData(@"C:\x")]
    public void InvalidWingetId_ThrowsArgumentException_WithoutTouchingDisk(string id)
    {
        using var temp = new TempDirectory();
        var folder = new WingetLogFolder(temp.AppRoot, new NullLog());

        Assert.Throws<ArgumentException>(() => folder.NewLogPath(id, DateTimeOffset.UtcNow));
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void WingetFolderIsJunction_ThrowsIOException_NothingInTarget()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        root.EnsureLeaf(root.LogsDirectory);
        var target = temp.Sub("cel");
        Directory.CreateDirectory(target);
        var link = Path.Combine(root.LogsDirectory, "winget");
        Junction.Create(link, target);
        try
        {
            var folder = new WingetLogFolder(root, new NullLog());

            Assert.ThrowsAny<IOException>(() => folder.NewLogPath("Mozilla.Firefox", DateTimeOffset.UtcNow));
            Assert.Empty(Directory.GetFileSystemEntries(target));
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }
}
