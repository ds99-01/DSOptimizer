using System;
using System.Globalization;
using System.IO;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Install;

namespace DSOptimizer.Windows;

/// <summary>
/// Pliki logu winget (<c>--log</c>) w <c>&lt;logs&gt;\winget\&lt;wingetId&gt;-&lt;yyyyMMdd-HHmmss&gt;.log</c> (czas UTC). Folder powstaje
/// leniwie, przy pierwszej ścieżce (nie przy starcie programu), tym samym mechanizmem co kopie napraw: korzeń i <c>logs</c> przez
/// <see cref="SecureRoot.EnsureLeaf"/>, a <c>winget</c> przez <see cref="SecureDirectory.Ensure"/> (Administratorzy + SYSTEM; niezaufany
/// folder odkładany jako <c>.untrusted-*</c> z ostrzeżeniem w logu, junction/link = <see cref="IOException"/>, nic nie powstaje w celu).
/// Sam plik tworzy winget. Id spoza <see cref="WingetArguments.IsValidWingetId"/> (np. <c>..\x</c>, obcięte „…”) =
/// <see cref="ArgumentException"/> bez dotykania dysku.
/// </summary>
public sealed class WingetLogFolder : IWingetLogs
{
    private const string FolderName = "winget";

    private readonly SecureRoot _root;
    private readonly ILog _log;

    /// <param name="root">Korzeń programu; folder logów winget to <c>&lt;root.LogsDirectory&gt;\winget</c>.</param>
    /// <param name="log">Ostrzeżenia o odłożonych niezaufanych folderach.</param>
    public WingetLogFolder(SecureRoot root, ILog log)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Folder logów winget (nie dotyka dysku).</summary>
    public string FolderPath => Path.Combine(_root.LogsDirectory, FolderName);

    public string NewLogPath(string wingetId, DateTimeOffset at)
    {
        if (!WingetArguments.IsValidWingetId(wingetId))
        {
            throw new ArgumentException("Nieprawidłowy identyfikator winget: " + wingetId, nameof(wingetId));
        }

        var folder = FolderPath;
        foreach (var aside in _root.EnsureLeaf(_root.LogsDirectory))
        {
            Warn(aside);
        }

        var folderAside = SecureDirectory.Ensure(folder);
        if (folderAside is not null)
        {
            Warn(folderAside);
        }

        var stamp = at.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(folder, wingetId + "-" + stamp + ".log");
    }

    private void Warn(string aside)
        => _log.Warn("Instalacja", $"Katalog {aside} miał niezaufanego właściciela lub uprawnienia i został odłożony. Utworzono nowy.");
}
