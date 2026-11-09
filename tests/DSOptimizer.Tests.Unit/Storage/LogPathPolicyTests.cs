using System;
using DSOptimizer.Core.Storage;
using Xunit;

namespace DSOptimizer.Tests.Unit.Storage;

public sealed class LogPathPolicyTests
{
    private const string Root = @"C:\ProgramData\DSOptimizer";

    [Theory]
    [InlineData(@"E:\logs\PC\r.txt", true)]
    [InlineData(@"\\nas\usb\r.txt", true)]
    [InlineData(@"C:\ProgramData\DSOptimizerX\r.txt", true)]
    [InlineData(@"C:\ProgramData\DSOptimizer\..\x.txt", true)]
    [InlineData(@"C:\ProgramData\DSOptimizer\reports\r.txt", false)]
    [InlineData(@"c:\programdata\dsoptimizer\reports\r.txt", false)]
    [InlineData("C:/ProgramData/DSOptimizer/reports/r.txt", false)]
    [InlineData(@"C:\ProgramData\DSOptimizer", false)]
    [InlineData(@"C:\ProgramData\DSOptimizer\", false)]
    [InlineData(@"reports\r.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData(@"C:\a|b.txt", false)]
    [InlineData(@"\\?\C:\ProgramData\DSOptimizer\reports\r.txt", false)]
    [InlineData(@"\\.\C:\ProgramData\DSOptimizer\reports\r.txt", false)]
    [InlineData("//?/C:/ProgramData/DSOptimizer/reports/r.txt", false)]
    [InlineData("//./C:/ProgramData/DSOptimizer/reports/r.txt", false)]
    [InlineData(@"\\localhost\C$\ProgramData\DSOptimizer\reports\r.txt", false)]
    [InlineData(@"\\LOCALHOST\share\r.txt", false)]
    [InlineData(@"\\127.0.0.1\share\r.txt", false)]
    [InlineData(@"\\127.1.2.3\share\r.txt", false)]
    [InlineData(@"\\[::1]\share\r.txt", false)]
    [InlineData(@"\\::1\share\r.txt", false)]
    [InlineData(@"\\0:0:0:0:0:0:0:1\share\r.txt", false)]
    [InlineData(@"\\pc\e$\r.txt", false)]
    [InlineData(@"\\nas\ADMIN$\x", false)]
    public void CanReveal_Table(string? path, bool expected)
        => Assert.Equal(expected, LogPathPolicy.CanReveal(path, Root));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CanReveal_NullOrEmptyProtectedRoot_ThrowsArgumentException(string? root)
        => Assert.ThrowsAny<ArgumentException>(() => LogPathPolicy.CanReveal(@"E:\x.txt", root!));

    [Theory]
    [InlineData(@"E:\logs\PC\r.txt", true)]
    [InlineData(@"C:\ProgramData\DSOptimizerX\r.txt", true)]
    [InlineData(@"C:\ProgramData\DSOptimizer\reports\r.txt", false)]
    [InlineData(@"C:\ProgramData\DSOptimizer", false)]
    public void CanReveal_RootWithTrailingSlash_SameResult(string path, bool expected)
        => Assert.Equal(expected, LogPathPolicy.CanReveal(path, Root + @"\"));

    [Theory]
    [InlineData(@"C:\ProgramData\DSOptimizer", true)]
    [InlineData(@"c:\programdata\dsoptimizer\logs\winget\a.log", true)]
    [InlineData("C:/ProgramData/DSOptimizer/x.txt", true)]
    [InlineData(@"C:\ProgramData\DSOptimizer.untrusted-1\a", false)]
    [InlineData(@"E:\logs\r.txt", false)]
    [InlineData(@"logs\r.txt", false)]
    [InlineData(@"\\?\C:\ProgramData\DSOptimizer\a", false)]
    [InlineData(@"\\localhost\C$\ProgramData\DSOptimizer\a", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsInside_Classifies(string? path, bool expected)
        => Assert.Equal(expected, LogPathPolicy.IsInside(path, Root));
}
