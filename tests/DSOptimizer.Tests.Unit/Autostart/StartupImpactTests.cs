using DSOptimizer.Core.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class StartupImpactTests
{
    [Theory]
    [InlineData(1_000_001, 0, StartupImpactLevel.High)]
    [InlineData(1_000_000, 0, StartupImpactLevel.Medium)]
    [InlineData(300_000, 0, StartupImpactLevel.Medium)]
    [InlineData(299_999, 0, StartupImpactLevel.Low)]
    [InlineData(0, 3_145_729, StartupImpactLevel.High)]
    [InlineData(0, 3_145_728, StartupImpactLevel.Medium)]
    [InlineData(0, 314_573, StartupImpactLevel.Medium)]
    [InlineData(0, 314_572, StartupImpactLevel.Low)]
    [InlineData(0, 0, StartupImpactLevel.Low)]
    [InlineData(19_000, 550_912, StartupImpactLevel.Medium)]
    [InlineData(430_000, 50_331_648, StartupImpactLevel.High)]
    [InlineData(1_534_474, 19_456, StartupImpactLevel.High)]
    public void Of(long cpu, long disk, StartupImpactLevel expected)
        => Assert.Equal(expected, StartupImpact.Of(cpu, disk));
}
