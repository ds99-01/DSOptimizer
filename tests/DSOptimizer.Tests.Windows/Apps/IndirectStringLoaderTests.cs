using System;
using System.Linq;
using System.Security.Principal;
using DSOptimizer.Core.Apps;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using DSOptimizer.Windows.Apps;
using Xunit;

namespace DSOptimizer.Tests.Windows.Apps;

[Trait("Category", "Windows")]
public class IndirectStringLoaderTests
{
    [Fact]
    public void TryLoad_BadSource_ReturnsNull()
    {
        Assert.Null(IndirectStringLoader.TryLoad(string.Empty));
        Assert.Null(IndirectStringLoader.TryLoad("to nie jest źródło pośrednie"));
        Assert.Null(IndirectStringLoader.TryLoad("@{Dso.Nie.Ma_1.0_x64__x?ms-resource://Dso.Nie.Ma/Resources/X}"));
    }

    [Fact]
    public void TryLoad_SystemDllString_ReturnsText()
    {
        var text = IndirectStringLoader.TryLoad(@"@%SystemRoot%\system32\mstsc.exe,-4000");

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    /// <summary>
    /// Tylko odczyt prawdziwej listy pakietów hosta (prawdziwy skrypt, prawdziwy <c>SHLoadIndirectString</c>). Kalkulator ma w manifeście
    /// <c>ms-resource:</c>; gdy host go nie ma (np. usunięty), test nic nie sprawdza (xUnit 2 nie ma dynamicznego pominięcia).
    /// </summary>
    [Fact]
    public void InstalledSystemPackage_DisplayNameIsExpanded_NotRawResource()
    {
        using var temp = new TempDirectory();
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var reader = new AppxReader(new PowerShellScriptRunner(temp.AppRoot, new NullLog()), IndirectStringLoader.TryLoad);

        var calculator = reader.Read(sid).FirstOrDefault(a => a.Appx!.PackageName == "Microsoft.WindowsCalculator");
        if (calculator is null)
        {
            return;
        }

        Assert.False(calculator.Name.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase), calculator.Name);
        Assert.False(calculator.Name.StartsWith("@{", StringComparison.Ordinal), calculator.Name);
        Assert.NotEqual("Microsoft.WindowsCalculator", calculator.Name);
        Assert.False(string.IsNullOrWhiteSpace(calculator.Name));
    }
}
