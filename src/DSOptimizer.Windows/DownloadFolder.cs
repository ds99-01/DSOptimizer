using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Windows;

/// <summary>
/// Folder pobrań <c>&lt;root&gt;\downloads</c> (<see cref="SecureRoot.DownloadsDirectory"/>): tworzony leniwie przy pierwszej ścieżce
/// przez <see cref="SecureRoot.EnsureLeaf"/> (Administratorzy + SYSTEM; niezaufany folder odkładany jako <c>.untrusted-*</c> z ostrzeżeniem
/// w logu, junction/link = <see cref="IOException"/>). Nazwy plików <c>&lt;guid N&gt;.msi</c> (RustDesk) albo <c>&lt;guid N&gt;.ps1</c> (skrypt WinUtil), sam plik tworzy pobieranie
/// (<c>FileMode.CreateNew</c>). Usuwanie tylko bezpośrednich dzieci folderu pobrań.
/// </summary>
public sealed class DownloadFolder : IDownloadFiles
{
    private const string MsiExtension = ".msi";
    private const string ScriptExtension = ".ps1";

    /// <summary>Wiek, po którym plik <c>.msi</c> albo <c>.ps1</c> w folderze pobrań uznajemy za osierocony (limit <c>msiexec</c> to 30 min).</summary>
    public static readonly TimeSpan OrphanAge = TimeSpan.FromHours(24);

    private readonly SecureRoot _root;
    private readonly ILog _log;

    public DownloadFolder(SecureRoot root, ILog log)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Jedyne dozwolone rozszerzenia to dokładnie <c>.msi</c> i <c>.ps1</c> (porównanie Ordinal); inne = <see cref="ArgumentException"/> bez dotykania dysku.</summary>
    public string NewDownloadPath(string extension)
    {
        if (!string.Equals(extension, MsiExtension, StringComparison.Ordinal)
            && !string.Equals(extension, ScriptExtension, StringComparison.Ordinal))
        {
            throw new ArgumentException("Dozwolone rozszerzenia pobrania to tylko .msi i .ps1: " + extension, nameof(extension));
        }

        foreach (var aside in _root.EnsureLeaf(_root.DownloadsDirectory))
        {
            _log.Warn(L.T("Instalacja"), L.F("Katalog {0} miał niezaufanego właściciela lub uprawnienia i został odłożony. Utworzono nowy.", aside));
        }

        DeleteOrphans();
        return Path.Combine(_root.DownloadsDirectory, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + extension);
    }

    /// <summary>
    /// Osierocone pobrania (M4 final review): plik po limicie <c>msiexec</c> (decyzja planu 7) albo po zabiciu programu w trakcie
    /// pobierania. Usuwa bezpośrednie dzieci folderu pobrań z rozszerzeniem <c>.msi</c> albo <c>.ps1</c> i <c>LastWriteTimeUtc</c> starszym
    /// niż <see cref="OrphanAge"/>, tą samą drogą co <see cref="TryDelete"/>; bez podfolderów i linków (punkt ponownej analizy
    /// pomijany). Błąd = ostrzeżenie w logu, nigdy wyjątek.
    /// </summary>
    private void DeleteOrphans()
    {
        var cutoff = DateTime.UtcNow - OrphanAge;
        try
        {
            foreach (var info in new DirectoryInfo(_root.DownloadsDirectory).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                if (!(string.Equals(info.Extension, MsiExtension, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(info.Extension, ScriptExtension, StringComparison.OrdinalIgnoreCase))
                    || (info.Attributes & FileAttributes.ReparsePoint) != 0
                    || info.LastWriteTimeUtc >= cutoff)
                {
                    continue;
                }

                if (TryDelete(info.FullName))
                {
                    _log.Info(L.T("Instalacja"), L.F("Usunięto osierocony pobrany plik {0} (starszy niż 24 h).", info.FullName));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            _log.Warn(L.T("Instalacja"), L.F("Nie udało się sprzątnąć starych plików w {0}: {1}", _root.DownloadsDirectory, ex.Message));
        }
    }

    public string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var text = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>Ścieżka spoza folderu pobrań (także podfolder, <c>..</c> albo sam folder) = <see cref="ArgumentException"/>.</summary>
    public bool TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Pusta ścieżka pliku do usunięcia.", nameof(path));
        }

        var full = SecureDirectory.NormalizePath(path);
        if (!string.Equals(Path.GetDirectoryName(full), _root.DownloadsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Odmowa usunięcia {full}: dozwolone są tylko pliki w {_root.DownloadsDirectory}.", nameof(path));
        }

        try
        {
            if (!File.Exists(full))
            {
                return true;
            }

            File.Delete(full);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn(L.T("Instalacja"), L.F("Nie udało się usunąć pobranego pliku {0}: {1}", full, ex.Message));
            return false;
        }
    }
}
