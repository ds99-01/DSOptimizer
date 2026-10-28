using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Storage;

namespace DSOptimizer.Windows;

/// <summary>
/// Pokazuje plik w Eksploratorze (<c>explorer.exe /select,"ścieżka"</c>) z programu podniesionego, ale bez podnoszenia Eksploratora:
/// CreateProcess stałej ścieżki <see cref="ShellProcessLauncher.ExplorerPath"/> (nie ShellExecute), tylko za strażnikiem powłoki
/// (<see cref="ShellSessionCheck.RequireOwnExplorer"/>) – wtedy explorer przekazuje polecenie niepodniesionej powłoce konta.
/// Ścieżka z katalogu programu (<c>%ProgramData%\DSOptimizer</c>) nigdy nie trafia do Eksploratora. <see cref="LogPathPolicy"/> jest
/// leksykalna, więc dodatkowo odmawiamy dla UNC do własnego komputera (udział może wskazywać katalog chroniony).
/// </summary>
public sealed class ShellFileRevealer : IFileRevealer
{
    private static string RefusalInsideProgramDirectory => L.T("Ten plik leży w katalogu programu – skopiuj ścieżkę zamiast otwierać.");

    private readonly Func<ProcessStartInfo, Process?> _start;
    private readonly IExplorerProcessQuery _explorers;
    private readonly int _sessionId;
    private readonly string _ownerSid;
    private readonly string _protectedRoot;
    private readonly Func<string, bool> _fileExists;
    private readonly string _machineName;

    public ShellFileRevealer(IExplorerProcessQuery explorers, string protectedRoot)
        : this(Process.Start, explorers, ShellSessionCheck.CurrentSessionId(), ShellSessionCheck.CurrentSid(), protectedRoot, File.Exists, Environment.MachineName)
    {
    }

    /// <summary>Testy podmieniają start procesu, zapytanie o Eksploratory, istnienie pliku i nazwę komputera.</summary>
    internal ShellFileRevealer(
        Func<ProcessStartInfo, Process?> start,
        IExplorerProcessQuery explorers,
        int sessionId,
        string ownerSid,
        string protectedRoot,
        Func<string, bool> fileExists,
        string machineName)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _explorers = explorers ?? throw new ArgumentNullException(nameof(explorers));
        _sessionId = sessionId;
        _ownerSid = ownerSid ?? throw new ArgumentNullException(nameof(ownerSid));
        _protectedRoot = string.IsNullOrWhiteSpace(protectedRoot)
            ? throw new ArgumentException("Katalog chroniony nie może być pusty.", nameof(protectedRoot))
            : protectedRoot;
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _machineName = machineName ?? throw new ArgumentNullException(nameof(machineName));
    }

    public void Reveal(string path)
    {
        if (!IsPlainAbsolutePath(path))
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nieprawidłowa ścieżka pliku: {0}", path));
        }

        // Leksykalne granice sprawdzenia własnego komputera: nie łapie adresu IP, długiej nazwy DNS, WebDAV ani junction/subst/dysku sieciowego
        // wskazujących na katalog chroniony; tych przypadków nie rozstrzygamy, bo to druga linia obrony, a pierwszą jest ukrycie linku w UI.
        if (!LogPathPolicy.CanReveal(path, _protectedRoot) || IsUncToOwnMachine(path))
        {
            throw new OperationException(OperationErrorKind.Unexpected, RefusalInsideProgramDirectory);
        }

        // Sprawdzamy i uruchamiamy dokładnie tę samą, znormalizowaną ścieżkę.
        var full = Path.GetFullPath(path.Replace('/', '\\'));
        if (!_fileExists(full))
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Plik nie istnieje: {0}", full));
        }

        ShellSessionCheck.RequireOwnExplorer(_explorers, _sessionId, _ownerSid);

        var info = new ProcessStartInfo(ShellProcessLauncher.ExplorerPath, $"/select,\"{full}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.SystemDirectory,
            ErrorDialog = false,
        };

        try
        {
            _start(info)?.Dispose();
        }
        catch (Win32Exception ex)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można pokazać pliku w Eksploratorze: {0}", ex.Message), ex);
        }
    }

    // Bezwzględna (litera dysku albo UNC), bez cudzysłowu – cudzysłów pozwoliłby wstrzyknąć dodatkowe argumenty explorer.exe.
    private static bool IsPlainAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path!.IndexOf('"') >= 0)
        {
            return false;
        }

        var driveLetter = path.Length >= 3 && char.IsLetter(path[0]) && path[0] < 128 && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
        return driveLetter || path.StartsWith(@"\\", StringComparison.Ordinal);
    }

    private bool IsUncToOwnMachine(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        var host = normalized.Substring(2).Split('\\')[0].Trim('[', ']');
        return host.Equals(_machineName, StringComparison.OrdinalIgnoreCase)
            || host.StartsWith(_machineName + ".", StringComparison.OrdinalIgnoreCase);
    }
}
