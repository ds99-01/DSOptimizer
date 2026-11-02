using System;
using DSOptimizer.Core.Autostart;

namespace DSOptimizer.Windows.Autostart;

/// <summary>
/// Składanie odczytu autostartu na prawdziwym systemie: rejestr i uruchamiacz skryptów z <see cref="AppComposition"/>, pliki, zadania Sklepu,
/// pomiar Windows z <c>wdi\LogFiles\StartupInfo</c> i zmienne maszyny z procesu. Tylko składanie – nic nie jest czytane ani uruchamiane,
/// dopóki ktoś nie wywoła <see cref="AutostartScanner.Read"/>.
/// </summary>
public static class AutostartComposition
{
    public static AutostartScanner CreateReader(AppComposition composition)
    {
        if (composition is null)
        {
            throw new ArgumentNullException(nameof(composition));
        }

        return new AutostartScanner(
            composition.Services.Registry,
            new AutostartFiles(),
            new StartupTaskReader(composition.Services.Scripts),
            StartupInfoFolder.ForSystem(),
            MachineEnvironment.FromProcess(),
            composition.Log);
    }
}
