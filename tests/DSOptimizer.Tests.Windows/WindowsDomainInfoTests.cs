using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class WindowsDomainInfoTests
{
    [Fact]
    public void IsDomainJoined_ReadsCim_ReturnsValue()
    {
        var joined = new WindowsDomainInfo().IsDomainJoined();

        Assert.NotNull(joined);
    }
}
