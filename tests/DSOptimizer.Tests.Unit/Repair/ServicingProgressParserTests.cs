using DSOptimizer.Core.Repair;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

public class ServicingProgressParserTests
{
    [Theory]
    [InlineData("[==========================  45.0%                           ]", 45)]
    [InlineData("Weryfikacja 70% ukończona.", 70)]
    [InlineData("[==========================100.0%==========================] ", 100)]
    [InlineData("postęp 100,0%", 100)]
    [InlineData("Verification 7 % complete", 7)]
    [InlineData("[                           0.0%                           ]", 0)]
    [InlineData("99.9%", 99)]
    [InlineData("najpierw 12% potem 80%", 12)]
    public void TryParsePercent_ReturnsFlooredPercent(string line, int expected)
        => Assert.Equal(expected, ServicingProgressParser.TryParsePercent(line));

    [Theory]
    [InlineData("150%")]
    [InlineData("1234%")]
    [InlineData("101,5%")]
    [InlineData("Operacja zakończyła się pomyślnie.")]
    [InlineData("")]
    [InlineData("100 procent")]
    public void TryParsePercent_OutOfRangeOrMissing_ReturnsNull(string line)
        => Assert.Null(ServicingProgressParser.TryParsePercent(line));

    [Theory]
    [InlineData("Weryfikacja ٤٥% ukończona.")]
    [InlineData("१००%")]
    [InlineData("４５%")]
    public void TryParsePercent_NonAsciiDigits_ReturnsNullWithoutThrowing(string line)
        => Assert.Null(ServicingProgressParser.TryParsePercent(line));

    [Fact]
    public void TryParsePercent_NullLine_ReturnsNull()
        => Assert.Null(ServicingProgressParser.TryParsePercent(null!));
}
