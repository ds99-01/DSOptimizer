using System;
using DSOptimizer.Core.Prep;
using Xunit;

namespace DSOptimizer.Tests.Unit.Prep;

public class HardwareIdQueryTests
{
    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_A0E8&SUBSYS_0A1B17AA&REV_20", @"PCI\VEN_8086&DEV_A0E8")]
    [InlineData(@"USB\VID_8087&PID_0033&REV_0001", @"USB\VID_8087&PID_0033")]
    [InlineData(@"ACPI\PNP0C0A", @"ACPI\PNP0C0A")]
    [InlineData(@"PCI\CC_0300", @"PCI\CC_0300")]
    public void For_ShortensKnownBusesAndKeepsOthers(string first, string expected)
        => Assert.Equal(expected, HardwareIdQuery.For(new[] { first, "drugi" }));

    [Fact]
    public void For_EmptyList_ReturnsNull()
        => Assert.Null(HardwareIdQuery.For(Array.Empty<string>()));

    [Fact]
    public void For_BlankFirst_ReturnsNull()
        => Assert.Null(HardwareIdQuery.For(new[] { "  " }));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void For_NeverReturnsEmptyString(string? first)
    {
        var result = HardwareIdQuery.For(new[] { first!, "" });

        Assert.Null(result);
    }
}
