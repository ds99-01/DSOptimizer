using System;
using DSOptimizer.Core.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartLocationsTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";

    [Fact]
    public void ForUser_PathsFromSpecTable()
    {
        var locations = AutostartLocations.ForUser(Sid);

        Assert.Equal(@"HKU\S-1-5-21-1-2-3-1001", locations.UserRoot);
        Assert.Equal("HKLM", locations.MachineRoot);
        Assert.Equal(@"HKU\S-1-5-21-1-2-3-1001_Classes", locations.UserClassesRoot);
        Assert.Equal(@"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup", locations.UserStartupFolder);
        Assert.Equal(@"%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp", locations.CommonStartupFolder);

        Assert.Equal(@"HKU\S-1-5-21-1-2-3-1001\Software\Microsoft\Windows\CurrentVersion\Run", locations.RunKey(AutostartSource.RunUser));
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", locations.RunKey(AutostartSource.RunMachine));
        Assert.Equal(@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", locations.RunKey(AutostartSource.Run32));

        Assert.Equal(
            @"HKU\S-1-5-21-1-2-3-1001\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            locations.StartupApprovedKey(AutostartSource.RunUser));
        Assert.Equal(
            @"HKU\S-1-5-21-1-2-3-1001\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
            locations.StartupApprovedKey(AutostartSource.FolderUser));
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", locations.StartupApprovedKey(AutostartSource.RunMachine));
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", locations.StartupApprovedKey(AutostartSource.Run32));
        Assert.Equal(
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
            locations.StartupApprovedKey(AutostartSource.FolderCommon));

        Assert.Equal(
            @"HKU\S-1-5-21-1-2-3-1001_Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\Microsoft.WindowsTerminal_8wekyb3d8bbwe\StartTerminalOnLoginTask",
            locations.StartupTaskKey("Microsoft.WindowsTerminal_8wekyb3d8bbwe", "StartTerminalOnLoginTask"));

        Assert.Equal(@"HKU\S-1-5-21-1-2-3-1001\Volatile Environment", locations.VolatileEnvironmentKey);
        Assert.Equal(@"HKU\S-1-5-21-1-2-3-1001\Environment", locations.UserEnvironmentKey);
    }

    [Fact]
    public void StartupApprovedKey_StartupTask_Throws()
        => Assert.Throws<ArgumentException>(() => AutostartLocations.ForUser(Sid).StartupApprovedKey(AutostartSource.StartupTask));

    [Theory]
    [InlineData(AutostartSource.FolderUser)]
    [InlineData(AutostartSource.FolderCommon)]
    [InlineData(AutostartSource.StartupTask)]
    public void RunKey_NotRunSource_Throws(AutostartSource source)
        => Assert.Throws<ArgumentException>(() => AutostartLocations.ForUser(Sid).RunKey(source));

    [Theory]
    [InlineData("", "Task")]
    [InlineData("Fam", "")]
    [InlineData(@"Fam\..", "Task")]
    [InlineData("Fam", @"a\b")]
    public void StartupTaskKey_EmptyOrNestedPart_Throws(string family, string taskId)
        => Assert.Throws<ArgumentException>(() => AutostartLocations.ForUser(Sid).StartupTaskKey(family, taskId));

    [Theory]
    [InlineData("")]
    [InlineData(@"S-1\x")]
    public void ForUser_InvalidSid_Throws(string sid)
        => Assert.Throws<ArgumentException>(() => AutostartLocations.ForUser(sid));
}
