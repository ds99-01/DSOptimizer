using System;
using DSOptimizer.Core.Abstractions;
using Xunit;

namespace DSOptimizer.Tests.Unit.Abstractions;

public class ToolRequestTests
{
    [Theory]
    [InlineData("reg.exe")]
    [InlineData("REG.EXE")]
    [InlineData("dism.exe")]
    [InlineData("sfc.exe")]
    [InlineData("cleanmgr.exe")]
    [InlineData("takeown.exe")]
    [InlineData("icacls.exe")]
    [InlineData("net1.exe")]
    [InlineData("ping.exe")]
    public void FileName_PlainSystemExe_IsAccepted(string file)
    {
        var request = new ToolRequest { FileName = file };

        Assert.Equal(file, request.FileName);
        Assert.Equal(string.Empty, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(60), request.Timeout);
    }

    [Theory]
    [InlineData(@"C:\x\a.exe")]
    [InlineData(@"..\a.exe")]
    [InlineData("a/b.exe")]
    [InlineData("a")]
    [InlineData("a.cmd")]
    [InlineData("a.msc")]
    [InlineData("a.cpl")]
    [InlineData("a b.exe")]
    [InlineData("a\".exe")]
    [InlineData("")]
    [InlineData("explorer.exe")]
    [InlineData("cmd.exe")]
    [InlineData("CMD.EXE")]
    [InlineData("powershell.exe")]
    [InlineData("powershell_ise.exe")]
    [InlineData("pwsh.exe")]
    [InlineData("wscript.exe")]
    [InlineData("cscript.exe")]
    [InlineData("mshta.exe")]
    [InlineData("rundll32.exe")]
    [InlineData("regsvr32.exe")]
    [InlineData("conhost.exe")]
    [InlineData("wmic.exe")]
    [InlineData("WMIC.EXE")]
    [InlineData("bitsadmin.exe")]
    [InlineData("certutil.exe")]
    [InlineData("schtasks.exe")]
    [InlineData("sc.exe")]
    [InlineData("msbuild.exe")]
    [InlineData("installutil.exe")]
    public void FileName_PathOrNonExeOrDeniedInterpreter_Throws(string file)
    {
        Assert.False(ToolRequest.IsAllowedFileName(file));
        Assert.Throws<ArgumentException>(() => new ToolRequest { FileName = file });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-60000)]
    public void Timeout_Negative_Throws(int milliseconds)
    {
        Assert.Throws<ArgumentException>(() => new ToolRequest { FileName = "dism.exe", Timeout = TimeSpan.FromMilliseconds(milliseconds) });
    }

    [Fact]
    public void Timeout_Zero_Throws()
        => Assert.Throws<ArgumentException>(() => new ToolRequest { FileName = "dism.exe", Timeout = TimeSpan.Zero });

    [Fact]
    public void Timeout_Infinite_Throws()
        => Assert.Throws<ArgumentException>(() => new ToolRequest { FileName = "dism.exe", Timeout = System.Threading.Timeout.InfiniteTimeSpan });

    [Fact]
    public void Timeout_Positive_IsKept()
        => Assert.Equal(TimeSpan.FromMinutes(90), new ToolRequest { FileName = "dism.exe", Timeout = TimeSpan.FromMinutes(90) }.Timeout);

    [Fact]
    public void IsAllowedFileName_Null_IsFalse() => Assert.False(ToolRequest.IsAllowedFileName(null));

    [Fact]
    public void ToolResult_DefaultsToEmptyOutput()
    {
        var result = new ToolResult { ExitCode = 5 };

        Assert.Equal(5, result.ExitCode);
        Assert.Empty(result.Output);
    }
}
