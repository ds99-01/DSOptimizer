using System;
using System.IO;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;

namespace DSOptimizer.Windows;

/// <summary>
/// Uruchamia winget przez <c>CreateProcessW</c> dokładnie podanej ścieżki (w programie: alias <see cref="AliasPath"/> z profilu
/// konta procesu; nigdy <c>PATH</c> ani ShellExecute) z katalogiem roboczym <see cref="Environment.SystemDirectory"/> (nigdy katalog
/// pendrive'a). Przebieg jak <see cref="ServicingToolRunner"/> (<see cref="UnkilledProcess"/>): stdout i stderr jednym potokiem,
/// stdin = NUL, **bez Job Objectu i bez zabijania** – po <c>waitLimit</c> wraca od razu z <c>TimedOut=true</c>, a winget (i instalator)
/// pracuje dalej, potok opróżnia w tle wątek czytający do EOF (Ruling R16). Wyjście dekodowane jako UTF-8 bez BOM.
/// Bezpiecznik testów: przy zmiennej środowiskowej <c>DSO_BLOCK_REAL_WINGET=1</c> plik o nazwie <c>winget.exe</c> nie startuje
/// (<see cref="InvalidOperationException"/> „Bezpiecznik testów: …” przed czymkolwiek innym) – ustawia ją proces <c>Tests.Windows</c>.
/// </summary>
public sealed class WingetRunner : IWingetRunner
{
    /// <summary>Zmienna środowiskowa bezpiecznika testów; wartość <c>1</c> blokuje start pliku <c>winget.exe</c>.</summary>
    public const string BlockRealWingetVariable = "DSO_BLOCK_REAL_WINGET";

    private const string WingetFileName = "winget.exe";

    private readonly string _executablePath;

    /// <param name="executablePath">Pełna ścieżka programu (w programie zawsze <see cref="AliasPath"/>).</param>
    public WingetRunner(string executablePath)
    {
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
    }

    /// <summary>Alias winget z profilu konta procesu: <c>%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe</c>.</summary>
    public static string AliasPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");

    public WingetRun Run(string arguments, TimeSpan waitLimit, Action<string>? onLine)
    {
        if (arguments is null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        if (string.Equals(Environment.GetEnvironmentVariable(BlockRealWingetVariable), "1", StringComparison.Ordinal)
            && string.Equals(Path.GetFileName(_executablePath), WingetFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Bezpiecznik testów: {BlockRealWingetVariable}=1, start {_executablePath} zablokowany (winget nigdy nie startuje w testach).");
        }

        // Profil bez LocalApplicationData daje ścieżkę względną: nigdy nie szukamy jej w katalogu bieżącym ani w PATH.
        if (!IsFullPath(_executablePath))
        {
            throw new OperationException(OperationErrorKind.NotFound, $"Nie znaleziono pliku {_executablePath} (niepełna ścieżka).");
        }

        var request = new ToolRequest { FileName = Path.GetFileName(_executablePath), Arguments = arguments };
        var run = UnkilledProcess.Run(
            _executablePath,
            request,
            Environment.SystemDirectory,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            waitLimit,
            onLine);
        return new WingetRun(run.ExitCode, run.TimedOut, run.Output);
    }

    private static bool IsFullPath(string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
