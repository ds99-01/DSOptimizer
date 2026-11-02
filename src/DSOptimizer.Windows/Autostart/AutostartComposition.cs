using System;
using DSOptimizer.Core.Abstractions;
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

        return CreateReader(composition.Services.Registry, composition.Services.Scripts, composition.Log);
    }

    /// <summary>Składanie z podanych usług (szew testów: dowód, że składanie nie woła rejestru, skryptów ani dziennika).</summary>
    internal static AutostartScanner CreateReader(IRegistry registry, IScriptRunner scripts, ILog log)
        => new(
            registry,
            new AutostartFiles(),
            new StartupTaskReader(scripts),
            StartupInfoFolder.ForSystem(),
            MachineEnvironment.FromProcess(),
            log);
}
