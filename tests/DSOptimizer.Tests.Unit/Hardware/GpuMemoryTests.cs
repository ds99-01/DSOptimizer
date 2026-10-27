using DSOptimizer.Core.Hardware;
using Xunit;

namespace DSOptimizer.Tests.Unit.Hardware;

public class GpuMemoryTests
{
    [Fact]
    public void GpuMemory_Resolve_PrefersQwMemorySize()
        => Assert.Equal(17179869184UL, GpuMemory.Resolve(17179869184UL, 4293918720U));

    [Fact]
    public void GpuMemory_Resolve_FallsBackToAdapterRam()
        => Assert.Equal(2147483648UL, GpuMemory.Resolve(null, 2147483648U));

    [Fact]
    public void GpuMemory_Resolve_SaturatedAdapterRam_IsNull()
        => Assert.Null(GpuMemory.Resolve(null, 4294967295U));

    [Fact]
    public void GpuMemory_Resolve_NothingKnown_IsNull()
        => Assert.Null(GpuMemory.Resolve(null, null));

    [Fact]
    public void GpuMemory_Resolve_Zeros_IsNull()
        => Assert.Null(GpuMemory.Resolve(0UL, 0U));

    [Fact]
    public void GpuMemory_Resolve_ZeroQwWithAdapterRam_UsesAdapterRam()
        => Assert.Equal(1073741824UL, GpuMemory.Resolve(0UL, 1073741824U));

    [Fact]
    public void GpuMemory_Resolve_DriverCap_IsNull()
        => Assert.Null(GpuMemory.Resolve(null, 4293918720U));

    [Fact]
    public void GpuMemory_Resolve_JustBelowDriverCap_IsKept()
        => Assert.Equal(4293918719UL, GpuMemory.Resolve(null, 4293918719U));
}
