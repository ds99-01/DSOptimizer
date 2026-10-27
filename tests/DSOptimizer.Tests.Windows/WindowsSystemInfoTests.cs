using System;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class WindowsSystemInfoTests
{
    [Fact]
    public void ReadsBuildEditionAndMachineName()
    {
        var info = new WindowsSystemInfo(new WindowsRegistry());

        Assert.True(info.Build >= 17763, $"Nieoczekiwany build: {info.Build}");
        Assert.True(info.Revision >= 0);
        Assert.False(string.IsNullOrWhiteSpace(info.Edition));
        Assert.Equal(Environment.MachineName, info.MachineName);
    }
}
