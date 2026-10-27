using System;
using System.IO;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>RISK: <c>msiexec</c> nigdy nie startuje na hoście – bezpiecznik <c>DSO_BLOCK_REAL_MSIEXEC=1</c> z <see cref="TestGuards"/>.</summary>
[Trait("Category", "Windows")]
public class MsiexecInstallerTests
{
    [Fact]
    public void SystemPath_IsSystem32Msiexec()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "msiexec.exe");

        Assert.Equal(expected, MsiexecInstaller.SystemPath(), StringComparer.OrdinalIgnoreCase);
        Assert.True(File.Exists(MsiexecInstaller.SystemPath()));
    }

    [Fact]
    public void GuardVariable_SetInTestProcess()
        => Assert.Equal("1", Environment.GetEnvironmentVariable(MsiexecInstaller.BlockRealMsiexecVariable));

    // Bezpiecznik działa przed czymkolwiek innym: nawet błędne ścieżki (które dałyby ArgumentException) nie dochodzą do walidacji.
    [Theory]
    [InlineData(@"C:\dso-nie-istnieje\x.msi", @"C:\dso-nie-istnieje\x.log")]
    [InlineData("wzgledna.msi", "wzgledny.log")]
    public void RealMsiexecBlockedInTests(string msi, string log)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new MsiexecInstaller(MsiexecInstaller.SystemPath()).Install(msi, log, TimeSpan.FromSeconds(1)));

        Assert.StartsWith("Bezpiecznik testów: ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Blocked_AlsoForOtherPathNamedMsiexec()
    {
        using var temp = new TempDirectory();

        Assert.Throws<InvalidOperationException>(() =>
            new MsiexecInstaller(temp.Sub("MSIEXEC.EXE")).Install(temp.Sub("a.msi"), temp.Sub("a.log"), TimeSpan.FromSeconds(1)));
    }

    // Inna nazwa pliku niż msiexec.exe: argumenty idą przez MsiArguments.Install (ścieżka względna = ArgumentException), nic nie startuje.
    [Fact]
    public void OtherExecutable_InvalidMsiPath_ArgumentException()
    {
        using var temp = new TempDirectory();

        Assert.Throws<ArgumentException>(() =>
            new MsiexecInstaller(temp.Sub("nie-ma.exe")).Install("wzgledna.msi", temp.Sub("a.log"), TimeSpan.FromSeconds(1)));
    }

    // Start przez UninstallerRunner: brak pliku programu = FileNotFoundException, nic nie startuje.
    [Fact]
    public void OtherExecutable_Missing_FileNotFound()
    {
        using var temp = new TempDirectory();

        Assert.Throws<FileNotFoundException>(() =>
            new MsiexecInstaller(temp.Sub("nie-ma.exe")).Install(temp.Sub("a.msi"), temp.Sub("a.log"), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void NullPath_Throws() => Assert.Throws<ArgumentNullException>(() => new MsiexecInstaller(null!));
}
