using System;
using System.Collections.Generic;
using System.Security.Principal;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Microsoft.Win32;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class WindowsRegistryTests
{
    private readonly WindowsRegistry _registry = new();

    [Fact]
    public void WriteThenRead_RoundTripsEveryType()
    {
        using var sandbox = new RegistrySandbox();
        var values = new Dictionary<string, RegValue>
        {
            ["s"] = RegValue.FromString("Zażółć gęślą jaźń"),
            ["e"] = RegValue.FromExpandString(@"%SystemRoot%\x"),
            ["d"] = RegValue.FromDWord(uint.MaxValue),
            ["q"] = RegValue.FromQWord(ulong.MaxValue),
            ["m"] = RegValue.FromMultiString("a", "b"),
            ["b"] = RegValue.FromBinary(new byte[] { 0, 1, 255 }),
            [string.Empty] = RegValue.FromString("domyślna"),
        };

        foreach (var pair in values)
        {
            _registry.Write(sandbox.HkcuPath, pair.Key, pair.Value);
        }

        foreach (var pair in values)
        {
            var read = _registry.Read(sandbox.HkcuPath, pair.Key);
            Assert.Equal(ReadStatus.Found, read.Status);
            Assert.Equal(pair.Value, read.Value);
        }
    }

    [Fact]
    public void Read_MissingValueOrKey_IsNotFound()
    {
        using var sandbox = new RegistrySandbox();
        Assert.Equal(ReadStatus.NotFound, _registry.Read(sandbox.HkcuPath, "brak").Status);
        Assert.Equal(ReadStatus.NotFound, _registry.Read(sandbox.HkcuPath + @"\nie-ma", "x").Status);
    }

    [Fact]
    public void Write_Absent_DeletesDefaultValue()
    {
        using var sandbox = new RegistrySandbox();
        _registry.Write(sandbox.HkcuPath, string.Empty, RegValue.FromString("x"));

        _registry.Write(sandbox.HkcuPath, string.Empty, RegValue.Absent);

        Assert.Equal(ReadStatus.NotFound, _registry.Read(sandbox.HkcuPath, string.Empty).Status);
    }

    [Fact]
    public void Write_AbsentOnMissingKey_DoesNothing()
    {
        using var sandbox = new RegistrySandbox();
        _registry.Write(sandbox.HkcuPath + @"\nie-ma", "x", RegValue.Absent);
        Assert.Equal(ReadStatus.NotFound, _registry.Read(sandbox.HkcuPath + @"\nie-ma", "x").Status);
    }

    [Fact]
    public void Read_ViaHkuSid_SeesHkcuValue()
    {
        using var sandbox = new RegistrySandbox();
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        _registry.Write(sandbox.HkcuPath, "v", RegValue.FromDWord(5));

        Assert.Equal(RegValue.FromDWord(5), _registry.Read(sandbox.HkuPath(sid), "v").Value);
    }

    [Fact]
    public void Read_ProtectedKey_IsAccessDenied()
        => Assert.Equal(ReadStatus.AccessDenied, _registry.Read(@"HKLM\SECURITY\Policy", "x").Status);

    [Fact]
    public void Write_ProtectedKey_ThrowsAccessDenied()
    {
        var ex = Assert.Throws<OperationException>(() => _registry.Write(@"HKLM\SECURITY\DSOptimizerTest", "x", RegValue.FromDWord(1)));
        Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public void Read_UnsupportedKind_ReportsError()
    {
        using var sandbox = new RegistrySandbox();
        using (var key = sandbox.OpenOrCreate())
        {
            key.SetValue("n", new byte[] { 1 }, RegistryValueKind.None);
        }

        Assert.Equal(ReadStatus.Error, _registry.Read(sandbox.HkcuPath, "n").Status);
    }

    [Fact]
    public void UnknownRoot_Throws()
        => Assert.Throws<ArgumentException>(() => _registry.Read(@"HKEY_LOCAL_MACHINE\X", "y"));

    [Fact]
    public void KeyStatus_ExistingAndMissing()
    {
        using var sandbox = new RegistrySandbox();
        sandbox.OpenOrCreate().Dispose();

        Assert.Equal(ReadStatus.Found, _registry.KeyStatus(sandbox.HkcuPath));
        Assert.Equal(ReadStatus.NotFound, _registry.KeyStatus(sandbox.HkcuPath + @"\nie-ma"));
    }

    [Fact]
    public void SubKeyNames_DirectChildrenOnly_MissingKeyEmpty()
    {
        using var sandbox = new RegistrySandbox();
        _registry.Write(sandbox.HkcuPath + @"\Previous Installations", "StateFlags0001", RegValue.FromDWord(2));
        _registry.Write(sandbox.HkcuPath + @"\Temporary Files\Deep", "x", RegValue.FromDWord(1));

        var names = new List<string>(_registry.SubKeyNames(sandbox.HkcuPath + @"\"));
        names.Sort(StringComparer.Ordinal);

        Assert.Equal(new[] { "Previous Installations", "Temporary Files" }, names);
        Assert.Empty(_registry.SubKeyNames(sandbox.HkcuPath + @"\Previous Installations"));
        Assert.Empty(_registry.SubKeyNames(sandbox.HkcuPath + @"\nie-ma"));
    }

    [Fact]
    public void SubKeyNames_ProtectedKey_ThrowsAccessDenied()
    {
        var ex = Assert.Throws<OperationException>(() => _registry.SubKeyNames(@"HKLM\SECURITY\Policy"));
        Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public void SubKeyNames_UnknownRoot_Throws()
        => Assert.Throws<ArgumentException>(() => _registry.SubKeyNames(@"HKEY_LOCAL_MACHINE\X"));

    [Fact]
    public void DeleteKeyIfEmpty_EmptyKey_Deleted()
    {
        using var sandbox = new RegistrySandbox();
        sandbox.OpenOrCreate().Dispose();

        Assert.True(_registry.DeleteKeyIfEmpty(sandbox.HkcuPath));
        Assert.Equal(ReadStatus.NotFound, _registry.KeyStatus(sandbox.HkcuPath));
    }

    [Fact]
    public void DeleteKeyIfEmpty_KeyWithDefaultValue_NotDeleted()
    {
        using var sandbox = new RegistrySandbox();
        _registry.Write(sandbox.HkcuPath, string.Empty, RegValue.FromString("x"));

        Assert.False(_registry.DeleteKeyIfEmpty(sandbox.HkcuPath));
        Assert.Equal(ReadStatus.Found, _registry.KeyStatus(sandbox.HkcuPath));
    }

    [Fact]
    public void DeleteKeyIfEmpty_KeyWithSubKey_NotDeleted()
    {
        using var sandbox = new RegistrySandbox();
        using (var key = sandbox.OpenOrCreate())
        {
            key.CreateSubKey("dziecko")!.Dispose();
        }

        Assert.False(_registry.DeleteKeyIfEmpty(sandbox.HkcuPath));
        Assert.Equal(ReadStatus.Found, _registry.KeyStatus(sandbox.HkcuPath));
    }

    [Fact]
    public void DeleteKeyIfEmpty_Missing_ReturnsTrue()
    {
        using var sandbox = new RegistrySandbox();
        Assert.True(_registry.DeleteKeyIfEmpty(sandbox.HkcuPath + @"\nie-ma"));
    }

    [Fact]
    public void DeleteKeyIfEmpty_HiveRoot_Throws()
        => Assert.Throws<ArgumentException>(() => _registry.DeleteKeyIfEmpty(@"HKCU"));

    [Fact]
    public void DeleteKeyTree_RemovesKeyWithSubKeysAndValues()
    {
        using var sandbox = new RegistrySandbox();
        var tree = sandbox.HkcuPath + @"\drzewo";
        _registry.Write(tree, "a", RegValue.FromDWord(1));
        _registry.Write(tree + @"\sub\deep", "b", RegValue.FromString("x"));
        _registry.Write(sandbox.HkcuPath + @"\obok", "c", RegValue.FromDWord(3));

        _registry.DeleteKeyTree(tree);

        Assert.Equal(ReadStatus.NotFound, _registry.KeyStatus(tree));
        Assert.Equal(ReadStatus.NotFound, _registry.KeyStatus(tree + @"\sub\deep"));
        Assert.Equal(ReadStatus.Found, _registry.KeyStatus(sandbox.HkcuPath + @"\obok"));
    }

    [Fact]
    public void DeleteKeyTree_MissingKey_IsNoOp()
    {
        using var sandbox = new RegistrySandbox();

        _registry.DeleteKeyTree(sandbox.HkcuPath + @"\nie-ma\wcale");

        Assert.Equal(ReadStatus.NotFound, _registry.KeyStatus(sandbox.HkcuPath + @"\nie-ma"));
    }

    [Theory]
    [InlineData(@"HKLM\")]
    [InlineData(@"HKLM\SOFTWARE")]
    [InlineData(@"HKU\S-1-5-18")]
    [InlineData(@"HKCU\Software")]
    public void DeleteKeyTree_RootOrDirectChild_IsRefused(string path)
    {
        Assert.Throws<ArgumentException>(() => _registry.DeleteKeyTree(path));
    }

    [Fact]
    public void DeleteKeyTree_UnsupportedRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => _registry.DeleteKeyTree(@"HKXX\a\b"));
    }
}
