using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Tłumaczenie SID na nazwę konta (LSA, tylko odczyt hosta).</summary>
[Trait("Category", "Windows")]
public class WindowsAccountNamesTests
{
    [Fact]
    public void NameOf_WellKnownSid_ReturnsName()
    {
        var name = new WindowsAccountNames().NameOf("S-1-5-18");

        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.DoesNotContain('\\', name!);
    }

    [Theory]
    [InlineData("to nie jest sid")]
    [InlineData("")]
    public void NameOf_Garbage_ReturnsNull(string sid)
        => Assert.Null(new WindowsAccountNames().NameOf(sid));

    [Fact]
    public void NameOf_UnmappedSid_ReturnsNull()
        => Assert.Null(new WindowsAccountNames().NameOf("S-1-5-21-1-2-3-999999"));
}
