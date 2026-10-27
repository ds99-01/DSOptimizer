using System;
using System.IO;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Errors;

namespace DSOptimizer.Core.Model;

/// <summary>
/// Reguły pola <c>launch</c> (spec, sekcja 4, A6), wspólne dla walidatora katalogu (statycznie, bez dysku) i launchera
/// (w czasie wykonania). Dozwolona jest sama nazwa pliku <c>*.exe|*.cpl|*.msc</c> (litery i cyfry ASCII, w środku też
/// <c>_</c> i <c>-</c>; bez ścieżki, separatorów, <c>..</c>, spacji, znaków wieloznacznych i cudzysłowów) albo dokładnie
/// <c>shell:::{GUID}</c>. Wszystko inne, także <c>ms-settings:</c> i inne schematy, jest odrzucane.
/// </summary>
public static class LaunchTarget
{
    /// <summary>Jedyna nazwa spoza bezpośredniego System32: rozwiązywana do stałej ścieżki PowerShell 5.1 (64-bit).</summary>
    public const string PowerShellFileName = "powershell.exe";

    private static readonly Regex SystemFileName = new(
        @"\A[A-Za-z0-9][A-Za-z0-9_-]*\.(?:[eE][xX][eE]|[cC][pP][lL]|[mM][sS][cC])\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex ShellNamespace = new(
        @"\Ashell:::\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}\z",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Nazwa pliku z System32. <c>explorer.exe</c> jest wykluczony jawnie: leży w <c>%SystemRoot%</c>, nie w System32, więc
    /// bezpośrednio nie wolno go wskazać w <c>launch</c> (startuje tylko pod <c>shell:::{GUID}</c>, za strażnikiem powłoki);
    /// bezpieczeństwo nie może zależeć od tego, że <c>System32\explorer.exe</c> nie istnieje.
    /// </summary>
    public static bool IsSystemFileName(string? file)
        => file is not null
            && SystemFileName.IsMatch(file)
            && !string.Equals(file, "explorer.exe", StringComparison.OrdinalIgnoreCase);

    public static bool IsShellNamespace(string? file) => file is not null && ShellNamespace.IsMatch(file);

    public static bool IsAllowed(string? file) => IsSystemFileName(file) || IsShellNamespace(file);

    /// <summary>
    /// Zwraca pełną ścieżkę programu do CreateProcess: w <paramref name="systemDirectory"/> (dla <c>powershell.exe</c>
    /// <paramref name="powerShellPath"/>, dla <c>shell:::{GUID}</c> <paramref name="explorerPath"/> = <c>%SystemRoot%\explorer.exe</c>,
    /// nie System32). Nigdy katalog bieżący ani PATH.
    /// Niedozwolona nazwa albo niepełne ścieżki bazowe: <see cref="OperationErrorKind.Unexpected"/> (bez sięgania do dysku);
    /// brak pliku: <see cref="OperationErrorKind.NotFound"/>.
    /// </summary>
    public static string Resolve(string file, string systemDirectory, string powerShellPath, string explorerPath, Func<string, bool> fileExists)
    {
        if (fileExists is null)
        {
            throw new ArgumentNullException(nameof(fileExists));
        }

        if (!IsFullyQualifiedLocal(systemDirectory) || !IsFullyQualifiedLocal(powerShellPath) || !IsFullyQualifiedLocal(explorerPath))
        {
            throw new OperationException(
                OperationErrorKind.Unexpected,
                $"Ścieżki bazowe launch muszą być pełne i lokalne (System32: '{systemDirectory}', PowerShell: '{powerShellPath}', Explorer: '{explorerPath}').");
        }

        if (IsShellNamespace(file))
        {
            if (!fileExists(explorerPath))
            {
                throw new OperationException(OperationErrorKind.NotFound, $"Nie znaleziono pliku {explorerPath}.");
            }

            return explorerPath;
        }

        if (!IsSystemFileName(file))
        {
            throw new OperationException(
                OperationErrorKind.Unexpected,
                $"Niedozwolony plik launch '{file}': dozwolona jest tylko nazwa pliku .exe/.cpl/.msc z System32 albo shell:::{{GUID}}.");
        }

        var path = string.Equals(file, PowerShellFileName, StringComparison.OrdinalIgnoreCase)
            ? powerShellPath
            : Path.Combine(systemDirectory, file);
        if (!fileExists(path))
        {
            throw new OperationException(OperationErrorKind.NotFound, $"Nie znaleziono pliku {path}.");
        }

        return path;
    }

    /// <summary>
    /// Polecenie startu bez asocjacji powłoki, zawsze CreateProcess: <c>.exe</c> → plik, <c>.msc</c> → <c>System32\mmc.exe "pełna ścieżka"</c>,
    /// <c>.cpl</c> → <c>System32\control.exe "pełna ścieżka"</c> (argumenty katalogu po spacji), <c>shell:::{GUID}</c> →
    /// <c>%SystemRoot%\explorer.exe shell:::{GUID}</c> z <see cref="LaunchCommand.RequiresUserShell"/> (strażnik powłoki,
    /// <see cref="ShellSessionGuard"/>). ShellExecute nie jest używany (dane VM: dla <c>shell:::</c> blokuje ok. 108 s albo zwraca
    /// fałszywy błąd). Asocjacje z HKCR (<c>mscfile</c>, <c>cplfile</c>, <c>exefile</c>) scalają HKCU\Software\Classes,
    /// zapisywalny bez uprawnień admina, więc ShellExecute pliku z podniesionego procesu mógłby uruchomić program spoza
    /// Windows (znane obejścia UAC: eventvwr/mscfile, sdclt/exefile). Brak pliku albo hosta: <see cref="OperationErrorKind.NotFound"/>.
    /// </summary>
    public static LaunchCommand ToCommand(LaunchSpec launch, string systemDirectory, string powerShellPath, string explorerPath, Func<string, bool> fileExists)
    {
        if (launch is null)
        {
            throw new ArgumentNullException(nameof(launch));
        }

        var arguments = launch.Arguments ?? string.Empty;
        var target = Resolve(launch.File, systemDirectory, powerShellPath, explorerPath, fileExists);
        if (IsShellNamespace(launch.File))
        {
            var shellArguments = arguments.Length == 0 ? launch.File : launch.File + " " + arguments;
            return new LaunchCommand { FileName = target, Arguments = shellArguments, RequiresUserShell = true };
        }

        var host = HostFor(launch.File);
        if (host is null)
        {
            return new LaunchCommand { FileName = target, Arguments = arguments, RequiresUserShell = false };
        }

        var hostPath = Path.Combine(systemDirectory, host);
        if (!fileExists(hostPath))
        {
            throw new OperationException(OperationErrorKind.NotFound, $"Nie znaleziono pliku {hostPath}.");
        }

        var hostArguments = "\"" + target + "\"" + (arguments.Length == 0 ? string.Empty : " " + arguments);
        return new LaunchCommand { FileName = hostPath, Arguments = hostArguments, RequiresUserShell = false };
    }

    /// <summary>Program z System32 otwierający plik danego rozszerzenia; null = plik sam jest programem (.exe).</summary>
    private static string? HostFor(string file)
    {
        if (file.EndsWith(".msc", StringComparison.OrdinalIgnoreCase))
        {
            return "mmc.exe";
        }

        return file.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase) ? "control.exe" : null;
    }

    /// <summary>Litera dysku + <c>:\</c>. Odrzuca ścieżki względne, względne wobec dysku (<c>C:</c>, <c>\Windows</c>) i UNC.</summary>
    private static bool IsFullyQualifiedLocal(string? path)
        => path is { Length: >= 3 }
            && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
            && path[1] == ':'
            && path[2] == '\\';
}
