using System.IO;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class ReparseLinksTests
{
    [Theory]
    [InlineData(0xA0000003u, true)]   // IO_REPARSE_TAG_MOUNT_POINT (junction)
    [InlineData(0xA000000Cu, true)]   // IO_REPARSE_TAG_SYMLINK
    [InlineData(0xA000001Du, true)]   // IO_REPARSE_TAG_LX_SYMLINK
    [InlineData(0x9000001Au, false)]  // IO_REPARSE_TAG_CLOUD (OneDrive)
    [InlineData(0x9000101Au, false)]  // IO_REPARSE_TAG_CLOUD_1
    [InlineData(0x9000701Au, false)]  // IO_REPARSE_TAG_CLOUD_7
    [InlineData(0x80000021u, false)]  // IO_REPARSE_TAG_ONEDRIVE
    [InlineData(0x8000001Bu, false)]  // IO_REPARSE_TAG_APPEXECLINK
    public void IsNameSurrogate_OnlyLinks(uint tag, bool expected) => Assert.Equal(expected, ReparseLinks.IsNameSurrogate(tag));

    [Fact]
    public void IsLink_Junction_True()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("cel"));
        Junction.Create(temp.Sub("j"), temp.Sub("cel"));

        Assert.True(ReparseLinks.IsLink(temp.Sub("j")));
    }

    [Fact]
    public void IsLink_PlainFileFolderAndMissing_False()
    {
        using var temp = new TempDirectory();
        var folder = temp.Sub("folder");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "plik.txt");
        File.WriteAllText(file, "x");

        Assert.False(ReparseLinks.IsLink(folder));
        Assert.False(ReparseLinks.IsLink(file));
        Assert.False(ReparseLinks.IsLink(temp.Sub("brak")));
        Assert.False(ReparseLinks.IsLink(temp.Sub(@"brak\glebiej\r.txt")));
    }

    [Fact]
    public void FirstLink_JunctionAncestor_ReturnsJunction()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("cel"));
        Junction.Create(temp.Sub("j"), temp.Sub("cel"));

        Assert.Equal(temp.Sub("j"), ReparseLinks.FirstLink(temp.Sub(@"j\a\r.txt")));
    }

    [Fact]
    public void FirstLink_NoLinks_Null()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("pulpit"));

        Assert.Null(ReparseLinks.FirstLink(temp.Sub(@"pulpit\r.txt")));
        Assert.Null(ReparseLinks.FirstLink(temp.Sub(@"brak\r.txt")));
    }
}
