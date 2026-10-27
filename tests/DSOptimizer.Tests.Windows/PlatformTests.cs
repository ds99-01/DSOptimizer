using System;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class PlatformTests
{
    [Fact]
    public void TestHost_RunsAs64BitProcess()
        => Assert.True(Environment.Is64BitProcess);
}
