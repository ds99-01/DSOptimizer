using System;
using System.IO;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Install;
using DSOptimizer.Windows.Apps;

namespace DSOptimizer.Windows;

/// <summary>
/// Cicha instalacja MSI: <c>%SystemRoot%\System32\msiexec.exe</c> z argumentami wyłącznie z <see cref="MsiArguments.Install"/>, start
/// tą samą drogą co deinstalatory (<see cref="UninstallerRunner"/>: <c>CreateProcessW</c> pełnej ścieżki, <c>bInheritHandles=FALSE</c>,
/// katalog roboczy System32, bez Job Objectu, nigdy nie zabijany – po limicie tylko przestajemy czekać, instalator pracuje dalej).
/// Bezpiecznik testów: przy zmiennej <c>DSO_BLOCK_REAL_MSIEXEC=1</c> plik o nazwie <c>msiexec.exe</c> nie startuje
/// (<see cref="InvalidOperationException"/> „Bezpiecznik testów: …” przed czymkolwiek innym) – ustawia ją proces <c>Tests.Windows</c>.
/// </summary>
public sealed class MsiexecInstaller : IMsiInstaller
{
    /// <summary>Zmienna środowiskowa bezpiecznika testów; wartość <c>1</c> blokuje start pliku <c>msiexec.exe</c>.</summary>
    public const string BlockRealMsiexecVariable = "DSO_BLOCK_REAL_MSIEXEC";

    private const string MsiexecFileName = "msiexec.exe";

    private readonly string _executablePath;

    /// <param name="executablePath">Pełna ścieżka programu (w programie zawsze <see cref="SystemPath"/>).</param>
    public MsiexecInstaller(string executablePath)
    {
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
    }

    /// <summary><c>%SystemRoot%\System32\msiexec.exe</c> (katalog Windows z API systemu, nie ze zmiennej środowiskowej).</summary>
    public static string SystemPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", MsiexecFileName);

    public MsiRun Install(string msiPath, string logPath, TimeSpan waitLimit)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(BlockRealMsiexecVariable), "1", StringComparison.Ordinal)
            && string.Equals(Path.GetFileName(_executablePath), MsiexecFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Bezpiecznik testów: {BlockRealMsiexecVariable}=1, start {_executablePath} zablokowany (msiexec nigdy nie startuje w testach).");
        }

        var plan = new UninstallPlan(_executablePath, MsiArguments.Install(msiPath, logPath), UninstallMode.Msi);
        var run = UninstallerRunner.Run(plan, waitLimit, CancellationToken.None);
        return new MsiRun(run.ExitCode, run.TimedOut);
    }
}
