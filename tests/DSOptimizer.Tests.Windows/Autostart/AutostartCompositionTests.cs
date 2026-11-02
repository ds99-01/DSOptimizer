using DSOptimizer.Core.Autostart;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using DSOptimizer.Windows.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Windows.Autostart;

[Trait("Category", "Windows")]
public class AutostartCompositionTests
{
    // Tylko składanie: Read nie jest wołane, więc ani rejestr, ani skrypt AppX, ani pliki pomiaru nie są czytane.
    [Fact]
    public void CreateReader_BuildsScannerWithoutReading()
    {
        using var temp = new TempDirectory();
        var composition = AppComposition.Create(temp.AppRoot, null);

        var reader = AutostartComposition.CreateReader(composition);

        Assert.IsType<AutostartScanner>(reader);
    }
}
