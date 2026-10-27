using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Model;

public class RegValueTests
{
    [Fact]
    public void Equals_SameTypeAndData_IsTrue()
    {
        Assert.Equal(RegValue.FromDWord(1), RegValue.FromDWord(1));
        Assert.Equal(RegValue.FromString("a"), RegValue.FromString("a"));
        Assert.Equal(RegValue.FromMultiString("a", "b"), RegValue.FromMultiString("a", "b"));
        Assert.Equal(RegValue.FromBinary(new byte[] { 1, 2 }), RegValue.FromBinary(new byte[] { 1, 2 }));
        Assert.Equal(RegValue.Absent, RegValue.Absent);
    }

    [Fact]
    public void Equals_DifferentTypeSameText_IsFalse()
    {
        Assert.NotEqual(RegValue.FromString("1"), RegValue.FromDWord(1));
        Assert.NotEqual(RegValue.FromString("x"), RegValue.FromExpandString("x"));
    }

    [Fact]
    public void Equals_AbsentVersusEmptyString_IsFalse()
        => Assert.NotEqual(RegValue.Absent, RegValue.FromString(string.Empty));

    [Fact]
    public void Equals_StringIsCaseSensitive()
        => Assert.NotEqual(RegValue.FromString("A"), RegValue.FromString("a"));

    [Fact]
    public void GetHashCode_EqualValues_HaveEqualHashes()
        => Assert.Equal(RegValue.FromMultiString("a").GetHashCode(), RegValue.FromMultiString("a").GetHashCode());

    [Fact]
    public void FromBinary_CopiesInput()
    {
        var data = new byte[] { 1 };
        var value = RegValue.FromBinary(data);
        data[0] = 9;
        Assert.Equal(RegValue.FromBinary(new byte[] { 1 }), value);
    }

    [Fact]
    public void ToDisplayString_DescribesValue()
    {
        Assert.Equal("(brak)", RegValue.Absent.ToDisplayString());
        Assert.Equal("10 (0x0000000A) [DWord]", RegValue.FromDWord(10).ToDisplayString());
        Assert.Equal("\"400\" [String]", RegValue.FromString("400").ToDisplayString());
        Assert.Equal("[a, b] [MultiString]", RegValue.FromMultiString("a", "b").ToDisplayString());
        Assert.Equal("0A0B [Binary]", RegValue.FromBinary(new byte[] { 10, 11 }).ToDisplayString());
    }
}
