using DSOptimizer.Core.Hardware;
using Xunit;

namespace DSOptimizer.Tests.Unit.Hardware;

public class WindowsEditionsTests
{
    [Theory]
    [InlineData("Professional", "Pro")]
    [InlineData("professional", "Pro")]
    [InlineData("EnterpriseS", "Enterprise LTSC")]
    [InlineData("Core", "Home")]
    [InlineData("NoweID", "NoweID")]
    public void DisplayName_MapsKnownAndKeepsUnknown(string id, string expected)
        => Assert.Equal(expected, WindowsEditions.DisplayName(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown")]
    [InlineData("UNKNOWN")]
    public void DisplayName_MissingOrUnknown_IsNull(string? id)
        => Assert.Null(WindowsEditions.DisplayName(id));
}
