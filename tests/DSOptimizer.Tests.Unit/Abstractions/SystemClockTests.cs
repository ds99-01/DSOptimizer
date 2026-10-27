using System;
using DSOptimizer.Core.Abstractions;
using Xunit;

namespace DSOptimizer.Tests.Unit.Abstractions;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_IsCurrentUtcTime()
        => Assert.InRange(new SystemClock().UtcNow, DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(5));
}
