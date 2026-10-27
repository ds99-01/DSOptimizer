using DSOptimizer.Core.Storage;
using Xunit;

namespace DSOptimizer.Tests.Unit.Storage;

public class LogMirrorPolicyTests
{
    [Theory]
    [InlineData(@"C:\Users\jan\Desktop\dso\")]
    [InlineData(@"c:\dso\")]
    public void MirrorDirectory_ExeOnSystemDrive_ReturnsNull(string exeDirectory)
        => Assert.Null(LogMirrorPolicy.MirrorDirectory(exeDirectory, @"C:\Windows", "PC"));

    [Fact]
    public void MirrorDirectory_ExeOnOtherDrive_ReturnsLogsMachineFolder()
        => Assert.Equal(
            @"E:\DSOptimizer\logs\KLIENT-PC",
            LogMirrorPolicy.MirrorDirectory(@"E:\DSOptimizer\", @"C:\Windows", "KLIENT-PC"));

    [Fact]
    public void MirrorDirectory_UncPath_ReturnsLogsMachineFolder()
        => Assert.Equal(
            @"\\nas\narzedzia\dso\logs\PC",
            LogMirrorPolicy.MirrorDirectory(@"\\nas\narzedzia\dso\", @"C:\Windows", "PC"));

    [Fact]
    public void MirrorDirectory_WindowsOnD_ExeOnC_ReturnsMirror()
        => Assert.Equal(
            @"C:\dso\logs\PC",
            LogMirrorPolicy.MirrorDirectory(@"C:\dso\", @"D:\Windows", "PC"));
}
