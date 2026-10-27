using System;
using System.IO;
using System.Linq;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class WindowsDiskSpaceTests
{
    private readonly WindowsDiskSpace _disk = new();

    [Fact]
    public void FreeBytes_SystemDrive_IsPositive()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;

        Assert.True(_disk.FreeBytes(root) > 0);
    }

    [Fact]
    public void FreeBytes_MissingDrive_IsNull()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).Last(c => !used.Contains(c));

        Assert.Null(_disk.FreeBytes(free + @":\"));
    }

    [Fact]
    public void FreeBytes_EmptyRoot_IsNull() => Assert.Null(_disk.FreeBytes(string.Empty));
}
