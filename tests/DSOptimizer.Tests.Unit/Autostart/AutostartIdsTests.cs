using System;
using DSOptimizer.Core.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartIdsTests
{
    private const string Terminal = "Microsoft.WindowsTerminal_8wekyb3d8bbwe";

    [Theory]
    [InlineData(AutostartSource.RunUser, "Steam", null, "run:user:Steam")]
    [InlineData(AutostartSource.RunMachine, "SecurityHealth", null, "run:machine:SecurityHealth")]
    [InlineData(AutostartSource.Run32, "DsoRunWow", null, "run32:DsoRunWow")]
    [InlineData(AutostartSource.FolderUser, "DsoLnkUser.lnk", null, "folder:user:DsoLnkUser.lnk")]
    [InlineData(AutostartSource.FolderCommon, "x.lnk", null, "folder:common:x.lnk")]
    [InlineData(AutostartSource.StartupTask, "StartTerminalOnLoginTask", Terminal, "appx:" + Terminal + "!StartTerminalOnLoginTask")]
    public void For_EachSource(AutostartSource source, string name, string? family, string expected)
        => Assert.Equal(expected, AutostartIds.For(source, name, family));

    [Fact]
    public void For_StartupTaskWithoutFamily_Throws()
        => Assert.Throws<ArgumentException>(() => AutostartIds.For(AutostartSource.StartupTask, "Task"));

    [Fact]
    public void TryParse_RoundTripAll()
    {
        var cases = new (AutostartSource Source, string Name, string? Family)[]
        {
            (AutostartSource.RunUser, "Steam", null),
            (AutostartSource.RunMachine, "SecurityHealth", null),
            (AutostartSource.Run32, "DsoRunWow", null),
            (AutostartSource.FolderUser, "DsoLnkUser.lnk", null),
            (AutostartSource.FolderCommon, "x.lnk", null),
            (AutostartSource.StartupTask, "StartTerminalOnLoginTask", Terminal),
        };

        foreach (var (source, name, family) in cases)
        {
            Assert.True(AutostartIds.TryParse(AutostartIds.For(source, name, family), out var parsedSource, out var parsedName, out var parsedFamily));
            Assert.Equal(source, parsedSource);
            Assert.Equal(name, parsedName);
            Assert.Equal(family, parsedFamily);
        }
    }

    [Fact]
    public void TryParse_NameWithColonAndSpace()
    {
        Assert.True(AutostartIds.TryParse("run:user:a:b c", out var source, out var name, out var family));
        Assert.Equal(AutostartSource.RunUser, source);
        Assert.Equal("a:b c", name);
        Assert.Null(family);
    }

    [Fact]
    public void TryParse_PrefixCaseInsensitive_RestVerbatim()
    {
        Assert.True(AutostartIds.TryParse("RUN:Machine:SecurityHealth", out var source, out var name, out _));
        Assert.Equal(AutostartSource.RunMachine, source);
        Assert.Equal("SecurityHealth", name);

        Assert.True(AutostartIds.TryParse("APPX:Fam_x!Task!B", out source, out name, out var family));
        Assert.Equal(AutostartSource.StartupTask, source);
        Assert.Equal("Fam_x", family);
        Assert.Equal("Task!B", name);
    }

    [Theory]
    [InlineData("run:x")]
    [InlineData("")]
    [InlineData("appx:bezWykrzyknika")]
    [InlineData("run32:")]
    [InlineData("appx:!Task")]
    [InlineData("appx:Fam!")]
    [InlineData(null)]
    public void TryParse_Invalid_False(string? id)
    {
        Assert.False(AutostartIds.TryParse(id!, out _, out var name, out var family));
        Assert.Equal(string.Empty, name);
        Assert.Null(family);
    }

    [Fact]
    public void IsUserScoped_UserAndStoreTrue_MachineFalse()
    {
        Assert.True(AutostartIds.IsUserScoped("run:user:Steam"));
        Assert.True(AutostartIds.IsUserScoped("folder:user:a.lnk"));
        Assert.True(AutostartIds.IsUserScoped("appx:" + Terminal + "!StartTerminalOnLoginTask"));
        Assert.False(AutostartIds.IsUserScoped("run:machine:SecurityHealth"));
        Assert.False(AutostartIds.IsUserScoped("run32:DsoRunWow"));
        Assert.False(AutostartIds.IsUserScoped("folder:common:x.lnk"));
        Assert.False(AutostartIds.IsUserScoped("nieznane:x"));
    }

    [Theory]
    [InlineData(AutostartSource.RunUser, AutostartScope.User)]
    [InlineData(AutostartSource.FolderUser, AutostartScope.User)]
    [InlineData(AutostartSource.RunMachine, AutostartScope.Machine)]
    [InlineData(AutostartSource.Run32, AutostartScope.Machine)]
    [InlineData(AutostartSource.FolderCommon, AutostartScope.Machine)]
    [InlineData(AutostartSource.StartupTask, AutostartScope.Store)]
    public void ScopeOf_EachSource(AutostartSource source, AutostartScope expected)
        => Assert.Equal(expected, AutostartSources.ScopeOf(source));

    [Fact]
    public void Evidence_FiveRegistryAndFolderSources_StartupTaskFlagsSeparate()
    {
        Assert.Equal(
            new[] { AutostartSource.RunUser, AutostartSource.RunMachine, AutostartSource.Run32, AutostartSource.FolderUser, AutostartSource.FolderCommon },
            AutostartEvidence.WriteFromZeroConfirmed);
        Assert.DoesNotContain(AutostartSource.StartupTask, AutostartEvidence.WriteFromZeroConfirmed);
        Assert.True(AutostartEvidence.StartupTaskKeyAbsentEnable);
        Assert.True(AutostartEvidence.StartupTaskState0Enable);
    }
}
