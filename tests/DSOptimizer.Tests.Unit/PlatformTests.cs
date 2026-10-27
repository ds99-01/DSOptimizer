using System;
using Xunit;

namespace DSOptimizer.Tests.Unit;

public class PlatformTests
{
    [Fact]
    public void TestHost_RunsAs64BitProcess()
        => Assert.True(Environment.Is64BitProcess, "Testy i aplikacja muszą działać jako x64 (bez WOW64).");
}
