using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Windows.Autostart;

/// <summary>Pliki pomiaru Windows w katalogu testu (<c>%TEMP%\dso-tests\&lt;guid&gt;</c>); prawdziwy katalog <c>wdi</c> nie jest czytany.</summary>
[Trait("Category", "Windows")]
public class StartupInfoFolderTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";

    private static void Write(TempDirectory temp, string name, DateTime writtenUtc, string text = "<StartupData />")
    {
        File.WriteAllText(temp.Sub(name), text, Encoding.Unicode);
        File.SetLastWriteTimeUtc(temp.Sub(name), writtenUtc);
    }

    [Fact]
    public void Files_OnlyThisSid_NumbersParsed()
    {
        using var temp = new TempDirectory();
        var first = new DateTime(2026, 11, 2, 8, 20, 5, DateTimeKind.Utc);
        var second = new DateTime(2026, 11, 2, 8, 45, 10, DateTimeKind.Utc);
        Write(temp, Sid + "_StartupInfo1.xml", first);
        Write(temp, Sid + "_StartupInfo12.xml", second);
        Write(temp, "S-1-5-21-1-2-3-1002_StartupInfo3.xml", second);
        Write(temp, Sid + "0_StartupInfo4.xml", second);
        Write(temp, "S-1-5-21-1-2-3x1001_StartupInfo5.xml", second);
        Write(temp, Sid + "_StartupInfo.xml", second);
        Write(temp, Sid + "_StartupInfo6.xml.bak", second);
        Write(temp, Sid + "_StartupInfo99999999999.xml", second);
        Directory.CreateDirectory(temp.Sub(Sid + "_StartupInfo7.xml"));

        var files = new StartupInfoFolder(temp.Root).Files(Sid).OrderBy(f => f.Number).ToArray();

        Assert.Equal(new[] { 1, 12 }, files.Select(f => f.Number));
        Assert.Equal(temp.Sub(Sid + "_StartupInfo1.xml"), files[0].Path, ignoreCase: true);
        Assert.Equal(new DateTimeOffset(first), files[0].WrittenAt);
        Assert.Equal(new DateTimeOffset(second), files[1].WrittenAt);
    }

    [Fact]
    public void Files_NoDirectory_Empty()
    {
        using var temp = new TempDirectory();

        Assert.Empty(new StartupInfoFolder(temp.Sub("nie-ma")).Files(Sid));
    }

    [Fact]
    public void ForSystem_PointsToWdiStartupInfo()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\wdi\LogFiles\StartupInfo");

        Assert.Equal(expected, StartupInfoFolder.ForSystem().FolderPath, ignoreCase: true);
    }

    [Fact]
    public void Read_Utf16WithBom()
    {
        using var temp = new TempDirectory();
        const string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<StartupData><Process Name=\"C:\\Program Files\\Zażółć\\a.exe\" /></StartupData>";
        File.WriteAllText(temp.Sub(Sid + "_StartupInfo1.xml"), xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        var source = new StartupInfoFolder(temp.Root);

        var text = source.Read(Assert.Single(source.Files(Sid)));

        Assert.Equal(xml, text);
    }

    [Fact]
    public void Read_Utf16WithoutBom_DecodedAsUtf16()
    {
        using var temp = new TempDirectory();
        const string xml = "<StartupData><Process Name=\"C:\\Zażółć\\a.exe\" /></StartupData>";
        File.WriteAllBytes(temp.Sub(Sid + "_StartupInfo1.xml"), new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetBytes(xml));
        var source = new StartupInfoFolder(temp.Root);

        Assert.Equal(xml, source.Read(Assert.Single(source.Files(Sid))));
    }

    // Windows może trzymać plik otwarty do zapisu: odczyt dzieli dostęp (ReadWrite | Delete) i niczego nie blokuje.
    [Fact]
    public void Read_FileOpenForWriting_StillReads()
    {
        using var temp = new TempDirectory();
        Write(temp, Sid + "_StartupInfo1.xml", DateTime.UtcNow);
        var source = new StartupInfoFolder(temp.Root);
        var file = Assert.Single(source.Files(Sid));

        using (new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Write | FileShare.Delete))
        {
            Assert.Equal("<StartupData />", source.Read(file));
        }
    }

    [Fact]
    public void Read_OverLimit_OperationException()
    {
        using var temp = new TempDirectory();
        Write(temp, Sid + "_StartupInfo1.xml", DateTime.UtcNow, new string('x', 100));
        var source = new StartupInfoFolder(temp.Root, maxBytes: 64);

        var ex = Assert.Throws<OperationException>(() => source.Read(Assert.Single(source.Files(Sid))));

        Assert.Equal(OperationErrorKind.Unexpected, ex.Kind);
    }

    [Fact]
    public void Read_Missing_OperationException()
    {
        using var temp = new TempDirectory();
        var source = new StartupInfoFolder(temp.Root);

        var ex = Assert.Throws<OperationException>(() => source.Read(new StartupInfoFile(temp.Sub(Sid + "_StartupInfo1.xml"), 1, DateTimeOffset.UtcNow)));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public void DefaultLimit_SixteenMegabytes()
        => Assert.Equal(16L * 1024 * 1024, StartupInfoFolder.DefaultMaxBytes);
}
