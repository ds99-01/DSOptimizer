using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Fakes;

public class FakeRegistryTests
{
    private const string Path = @"HKLM\SOFTWARE\DSO\Test";

    [Fact]
    public void ReadWrite_BehavesLikeRegistry()
    {
        var registry = new FakeRegistry();
        Assert.Equal(ReadStatus.NotFound, registry.Read(Path, "A").Status);

        registry.Write(Path, "A", RegValue.FromDWord(1));
        Assert.Equal(RegValue.FromDWord(1), registry.Read(Path.ToUpperInvariant(), "a").Value);

        registry.Write(Path, "A", RegValue.Absent);
        Assert.Equal(ReadStatus.NotFound, registry.Read(Path, "A").Status);
        Assert.Equal(2, registry.WriteLog.Count);
        Assert.Equal(3, registry.ReadCount);
    }

    [Fact]
    public void Failures_AreInjectable()
    {
        var registry = new FakeRegistry();
        registry.DenyRead(Path, "A");
        registry.FailWrite(Path, "B", OperationErrorKind.Unexpected);

        Assert.Equal(ReadStatus.AccessDenied, registry.Read(Path, "A").Status);
        var ex = Assert.Throws<OperationException>(() => registry.Write(Path, "B", RegValue.FromDWord(1)));
        Assert.Equal(OperationErrorKind.Unexpected, ex.Kind);
    }

    [Fact]
    public void KeyStatus_AfterWrite_ParentKeysExist()
    {
        var registry = new FakeRegistry();
        registry.Write(Path, "A", RegValue.FromDWord(1));

        Assert.Equal(ReadStatus.Found, registry.KeyStatus(Path));
        Assert.Equal(ReadStatus.Found, registry.KeyStatus(@"HKLM\SOFTWARE\DSO"));
        Assert.Equal(ReadStatus.Found, registry.KeyStatus(@"hklm\software"));
        Assert.Equal(ReadStatus.NotFound, registry.KeyStatus(@"HKLM\SOFTWARE\Inny"));
    }

    [Fact]
    public void DeleteKeyIfEmpty_WithValue_ReturnsFalse()
    {
        var registry = new FakeRegistry();
        registry.Write(Path, "A", RegValue.FromDWord(1));

        Assert.False(registry.DeleteKeyIfEmpty(Path));
        Assert.True(registry.HasKey(Path));
        Assert.Empty(registry.DeletedKeys);
    }

    [Fact]
    public void DeleteKeyIfEmpty_WithSubKey_ReturnsFalse()
    {
        var registry = new FakeRegistry();
        registry.Write(Path + @"\Sub", "A", RegValue.FromDWord(1));

        Assert.False(registry.DeleteKeyIfEmpty(Path));
        Assert.True(registry.HasKey(Path));
    }

    [Fact]
    public void DeleteKeyIfEmpty_Empty_DeletesAndReturnsTrue()
    {
        var registry = new FakeRegistry();
        registry.CreateKey(Path);

        Assert.True(registry.DeleteKeyIfEmpty(Path + @"\"));
        Assert.False(registry.HasKey(Path));
        Assert.Equal(ReadStatus.NotFound, registry.KeyStatus(Path));
        Assert.Equal(new[] { Path }, registry.DeletedKeys);
    }

    [Fact]
    public void DeleteKeyIfEmpty_Missing_ReturnsTrue()
    {
        var registry = new FakeRegistry();

        Assert.True(registry.DeleteKeyIfEmpty(Path));
        Assert.Empty(registry.DeletedKeys);
    }

    [Fact]
    public void WriteAbsent_KeepsKey()
    {
        var registry = new FakeRegistry();
        registry.Write(Path, "A", RegValue.FromDWord(1));

        registry.Write(Path, "A", RegValue.Absent);

        Assert.True(registry.HasKey(Path));
        Assert.Equal(ReadStatus.Found, registry.KeyStatus(Path));
    }

    [Fact]
    public void DenyKey_KeyStatusAccessDenied()
    {
        var registry = new FakeRegistry();
        registry.CreateKey(Path);
        registry.DenyKey(Path);

        Assert.Equal(ReadStatus.AccessDenied, registry.KeyStatus(Path));
        var ex = Assert.Throws<OperationException>(() => registry.DeleteKeyIfEmpty(Path));
        Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public void DeleteKeyTree_RemovesKeyWithSubKeysAndValues_AndIgnoresMissing()
    {
        var registry = new FakeRegistry();
        registry.Write(Path, "A", RegValue.FromDWord(1));
        registry.Write(Path + @"\Sub\Deep", "B", RegValue.FromDWord(2));
        registry.Write(Path + "X", "C", RegValue.FromDWord(3));

        registry.DeleteKeyTree(Path);
        registry.DeleteKeyTree(Path);

        Assert.False(registry.HasKey(Path));
        Assert.False(registry.HasKey(Path + @"\Sub\Deep"));
        Assert.Equal(ReadStatus.NotFound, registry.Read(Path + @"\Sub\Deep", "B").Status);
        Assert.Equal(ReadStatus.NotFound, registry.Read(Path, "A").Status);
        Assert.True(registry.HasKey(@"HKLM\SOFTWARE\DSO"));
        Assert.Equal(RegValue.FromDWord(3), registry.Get(Path + "X", "C"));
    }

    [Fact]
    public void SubKeyNames_DirectChildrenOnly_MissingKeyEmpty()
    {
        var registry = new FakeRegistry();
        registry.Write(Path + @"\B\Deep", "x", RegValue.FromDWord(1));
        registry.CreateKey(Path + @"\A");
        registry.CreateKey(Path + "X");

        Assert.Equal(new[] { "A", "B" }, registry.SubKeyNames(Path + @"\").OrderBy(n => n, System.StringComparer.Ordinal).ToArray());
        Assert.Empty(registry.SubKeyNames(Path + @"\A"));
        Assert.Empty(registry.SubKeyNames(@"HKLM\SOFTWARE\Nie-ma"));
    }

    [Fact]
    public void SubKeyNames_DeniedKey_Throws()
    {
        var registry = new FakeRegistry();
        registry.CreateKey(Path + @"\A");
        registry.DenyKey(Path);

        var ex = Assert.Throws<OperationException>(() => registry.SubKeyNames(Path));
        Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public void DeleteKeyTree_DeniedKey_Throws()
    {
        var registry = new FakeRegistry();
        registry.CreateKey(Path);
        registry.DenyKey(Path);

        var ex = Assert.Throws<OperationException>(() => registry.DeleteKeyTree(Path));
        Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
    }

    [Theory]
    [InlineData(@"HKLM")]
    [InlineData(@"HKLM\SOFTWARE")]
    [InlineData(@"HKU\S-1-5-21-1\")]
    public void DeleteKeyTree_RootOrDirectChild_ThrowsArgumentException_LikeWindowsRegistry(string path)
    {
        var registry = new FakeRegistry();
        registry.CreateKey(@"HKLM\SOFTWARE\DSO");

        Assert.Throws<System.ArgumentException>(() => registry.DeleteKeyTree(path));
        Assert.True(registry.HasKey(@"HKLM\SOFTWARE\DSO"));
    }
}
