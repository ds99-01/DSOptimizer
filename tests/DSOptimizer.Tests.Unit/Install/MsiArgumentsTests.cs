using System;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class MsiArgumentsTests
{
    private const string Msi = @"C:\ProgramData\DSOptimizer\downloads\a.msi";
    private const string Log = @"C:\ProgramData\DSOptimizer\logs\winget\rustdesk-msi-x.log";

    [Fact]
    public void Install_ExactLine()
    {
        Assert.Equal(
            "/i \"C:\\ProgramData\\DSOptimizer\\downloads\\a.msi\" /qn /norestart /l*v \"C:\\ProgramData\\DSOptimizer\\logs\\winget\\rustdesk-msi-x.log\"",
            MsiArguments.Install(Msi, Log));
    }

    [Theory]
    [InlineData("a.msi")]
    [InlineData("\\\\srv\\x.msi")]
    [InlineData("C:\\a\".msi")]
    [InlineData("C:a.msi")]
    [InlineData("C:\\dir\\")]
    [InlineData("C:\\a\n.msi")]
    [InlineData("")]
    [InlineData(null)]
    public void Install_BadPath_Throws(string? path)
    {
        Assert.Throws<ArgumentException>(() => MsiArguments.Install(path!, Log));
        Assert.Throws<ArgumentException>(() => MsiArguments.Install(Msi, path!));
    }
}
