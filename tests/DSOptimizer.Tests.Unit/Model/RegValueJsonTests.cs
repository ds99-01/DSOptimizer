using System;
using DSOptimizer.Core.Model;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Model;

public class RegValueJsonTests
{
    [Fact]
    public void FromToken_ParsesEveryType()
    {
        Assert.Equal(RegValue.FromString("x"), RegValueJson.FromToken(RegistryValueType.String, new JValue("x")));
        Assert.Equal(RegValue.FromExpandString("%TEMP%"), RegValueJson.FromToken(RegistryValueType.ExpandString, new JValue("%TEMP%")));
        Assert.Equal(RegValue.FromDWord(4294967295), RegValueJson.FromToken(RegistryValueType.DWord, JToken.Parse("4294967295")));
        Assert.Equal(RegValue.FromQWord(18446744073709551615), RegValueJson.FromToken(RegistryValueType.QWord, JToken.Parse("18446744073709551615")));
        Assert.Equal(RegValue.FromMultiString("a", "b"), RegValueJson.FromToken(RegistryValueType.MultiString, JToken.Parse("[\"a\",\"b\"]")));
        Assert.Equal(RegValue.FromBinary(new byte[] { 0xAB, 0x01 }), RegValueJson.FromToken(RegistryValueType.Binary, new JValue("ab01")));
        Assert.Equal(RegValue.FromBinary(Array.Empty<byte>()), RegValueJson.FromToken(RegistryValueType.Binary, new JValue(string.Empty)));
    }

    [Fact]
    public void FromToken_RemoveEntryMarker_IsAbsent()
        => Assert.Equal(RegValue.Absent, RegValueJson.FromToken(RegistryValueType.DWord, new JValue("<RemoveEntry>")));

    [Theory]
    [InlineData(RegistryValueType.DWord, "4294967296")]
    [InlineData(RegistryValueType.DWord, "-1")]
    [InlineData(RegistryValueType.DWord, "\"1\"")]
    [InlineData(RegistryValueType.DWord, "1.5")]
    [InlineData(RegistryValueType.String, "1")]
    [InlineData(RegistryValueType.MultiString, "[\"a\",1]")]
    [InlineData(RegistryValueType.Binary, "\"abc\"")]
    [InlineData(RegistryValueType.Binary, "\"zz\"")]
    public void FromToken_InvalidValue_ThrowsFormatException(RegistryValueType type, string json)
        => Assert.Throws<FormatException>(() => RegValueJson.FromToken(type, JToken.Parse(json)));

    [Fact]
    public void ToToken_RoundTripsEveryType()
    {
        var values = new[]
        {
            RegValue.Absent,
            RegValue.FromString("żółw"),
            RegValue.FromExpandString("%x%"),
            RegValue.FromDWord(7),
            RegValue.FromQWord(ulong.MaxValue),
            RegValue.FromMultiString("a"),
            RegValue.FromBinary(new byte[] { 0, 255 }),
        };
        foreach (var value in values)
        {
            Assert.Equal(value, RegValueJson.FromToken(value.Type, RegValueJson.ToToken(value)));
        }
    }
}
