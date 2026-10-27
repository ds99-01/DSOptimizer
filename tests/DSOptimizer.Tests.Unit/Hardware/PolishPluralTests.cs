using DSOptimizer.Core.Hardware;
using Xunit;

namespace DSOptimizer.Tests.Unit.Hardware;

public class PolishPluralTests
{
    [Theory]
    [InlineData(1, "rdzeń")]
    [InlineData(2, "rdzenie")]
    [InlineData(4, "rdzenie")]
    [InlineData(5, "rdzeni")]
    [InlineData(12, "rdzeni")]
    [InlineData(13, "rdzeni")]
    [InlineData(22, "rdzenie")]
    [InlineData(24, "rdzenie")]
    [InlineData(25, "rdzeni")]
    [InlineData(112, "rdzeni")]
    [InlineData(0, "rdzeni")]
    public void Choose_Cores(long count, string expected)
        => Assert.Equal(expected, PolishPlural.Choose(count, "rdzeń", "rdzenie", "rdzeni"));

    [Theory]
    [InlineData(24, "cale")]
    [InlineData(27, "cali")]
    [InlineData(32, "cale")]
    public void Choose_Inches(long count, string expected)
        => Assert.Equal(expected, PolishPlural.Choose(count, "cal", "cale", "cali"));
}
