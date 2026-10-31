using DSOptimizer.Windows.Apps;
using Xunit;

namespace DSOptimizer.Tests.Windows.Apps;

public class IndirectStringSourceTests
{
    private const string Windows = @"C:\Windows";
    private const string ProgramFiles = @"C:\Program Files";
    private const string ProgramFilesX86 = @"C:\Program Files (x86)";

    private static string? Resolve(string source)
        => IndirectStringLoader.ResolveSource(source, Windows, ProgramFiles, ProgramFilesX86);

    [Theory]
    [InlineData(@"@%SystemRoot%\system32\mstsc.exe,-4000", @"@C:\Windows\system32\mstsc.exe,-4000")]
    [InlineData(@"@%windir%\system32\mstsc.exe,-4000", @"@C:\Windows\system32\mstsc.exe,-4000")]
    [InlineData(@"@%ProgramFiles%\App\res.dll,-1", @"@C:\Program Files\App\res.dll,-1")]
    [InlineData(@"@%ProgramFiles(x86)%\App\res.dll,-1", @"@C:\Program Files (x86)\App\res.dll,-1")]
    [InlineData(@"@C:\Windows\system32\mstsc.exe,-4000", @"@C:\Windows\system32\mstsc.exe,-4000")]
    [InlineData(@"@c:\program files\App\res.dll,-1;komentarz", @"@c:\program files\App\res.dll,-1;komentarz")]
    public void LocalResourceUnderProtectedFolder_IsAllowed(string source, string expected)
    {
        Assert.Equal(expected, Resolve(source));
    }

    [Theory]
    [InlineData("@{Microsoft.WindowsCalculator_11.2.0.0_x64__8wekyb3d8bbwe?ms-resource://Microsoft.WindowsCalculator/Resources/AppName}")]
    [InlineData("@{Microsoft.WindowsCalculator_11.2.0.0_x64__8wekyb3d8bbwe?ms-resource:AppName}")]
    public void PackageResource_IsAllowedUnchanged(string source)
    {
        Assert.Equal(source, Resolve(source));
    }

    [Theory]
    [InlineData(@"@\\serwer\udzial\x.dll,-1")]
    [InlineData(@"@//serwer/udzial/x.dll,-1")]
    [InlineData(@"@\\?\UNC\serwer\udzial\x.dll,-1")]
    [InlineData(@"@\\127.0.0.1\c$\Windows\system32\mstsc.exe,-4000")]
    [InlineData(@"@{\\serwer\udzial\resources.pri?ms-resource://x/y}")]
    [InlineData(@"@{C:\Users\a\resources.pri?ms-resource://x/y}")]
    [InlineData(@"@%USERPROFILE%\x.dll,-1")]
    [InlineData(@"@%TEMP%\x.dll,-1")]
    [InlineData(@"@%NIEZNANA%\x.dll,-1")]
    [InlineData(@"@C:\Users\a\x.dll,-1")]
    [InlineData(@"@D:\x.dll,-1")]
    [InlineData(@"@C:\WindowsX\x.dll,-1")]
    [InlineData(@"@C:\Windows\..\Users\a\x.dll,-1")]
    [InlineData(@"@C:\Windows\\serwer\x.dll,-1")]
    [InlineData(@"@C:\Windows/x.dll,-1")]
    [InlineData(@"@C:\Windows\x.dll:strumien,-1")]
    [InlineData(@"@C:\Windows\x.dll")]
    [InlineData(@"@C:\Windows\x.dll,1")]
    [InlineData(@"@x.dll,-1")]
    [InlineData("zwykły tekst")]
    [InlineData("")]
    public void AnythingElse_IsRefused(string source)
    {
        Assert.Null(Resolve(source));
    }

    [Fact]
    public void TryLoad_UncSource_NeverReachesTheApi_ReturnsNull()
    {
        Assert.Null(IndirectStringLoader.TryLoad(@"@\\127.0.0.1\c$\Windows\system32\mstsc.exe,-4000"));
    }
}
