using System;
using System.Collections.Generic;
using System.IO;

namespace DSOptimizer.Windows;

/// <summary>
/// Jedyny zaufany korzeń katalogów aplikacji (allowlista, nie denylista): <c>%ProgramData%\DSOptimizer</c>.
/// Podkatalogi (<c>logs</c>, <c>journal</c>, <c>run</c>, <c>reports</c>, <c>backups</c>, <c>downloads</c>) powstają wyłącznie bezpośrednio pod korzeniem.
/// Konstruktor jest wewnętrzny (tylko <see cref="ProgramData"/> i testy), więc kod spoza zestawu nie poda własnej ścieżki.
/// </summary>
public sealed class SecureRoot
{
    public const string LogsLeafName = "logs";
    public const string JournalLeafName = "journal";
    public const string RunLeafName = "run";
    public const string ReportsLeafName = "reports";
    public const string BackupsLeafName = "backups";
    public const string DownloadsLeafName = "downloads";

    private static readonly Environment.SpecialFolder[] ForbiddenFolders =
    {
        Environment.SpecialFolder.CommonApplicationData,
        Environment.SpecialFolder.Windows,
        Environment.SpecialFolder.ProgramFiles,
        Environment.SpecialFolder.ProgramFilesX86,
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolder.System,
        Environment.SpecialFolder.SystemX86,
    };

    internal SecureRoot(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Ścieżka korzenia nie może być pusta.", nameof(path));
        }

        if (!IsFullyQualified(path))
        {
            throw new ArgumentException($"Korzeń musi być ścieżką w pełni kwalifikowaną (X:\\… lub UNC): {path}", nameof(path));
        }

        string full;
        try
        {
            full = SecureDirectory.NormalizePath(path);
        }
        catch (Exception ex) when (ex is NotSupportedException or PathTooLongException or ArgumentException)
        {
            throw new ArgumentException($"Niedozwolona ścieżka korzenia {path}: {ex.Message}", nameof(path), ex);
        }

        if (IsForbidden(full))
        {
            throw new ArgumentException($"Odmowa użycia {full} jako korzenia: to korzeń dysku lub katalog systemowy.", nameof(path));
        }

        FullPath = full;
    }

    /// <summary><c>%ProgramData%\DSOptimizer</c>. Nie dotyka dysku.</summary>
    public static SecureRoot ProgramData => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DSOptimizer"));

    public string FullPath { get; }

    public string LogsDirectory => Path.Combine(FullPath, LogsLeafName);

    public string JournalDirectory => Path.Combine(FullPath, JournalLeafName);

    public string RunDirectory => Path.Combine(FullPath, RunLeafName);

    public string ReportsDirectory => Path.Combine(FullPath, ReportsLeafName);

    /// <summary>Kopie napraw (polityki i typy startu usług WU); tworzony dopiero przy pierwszym zapisie kopii.</summary>
    public string BackupsDirectory => Path.Combine(FullPath, BackupsLeafName);

    /// <summary>Pliki pobrane z internetu (instalator RustDesk z GitHuba); tworzony dopiero przy pierwszej ścieżce pobrania.</summary>
    public string DownloadsDirectory => Path.Combine(FullPath, DownloadsLeafName);

    /// <summary>
    /// Zabezpiecza sam korzeń (wg <see cref="SecureDirectory.Ensure"/>). Zwraca ścieżki odłożone jako <c>.untrusted-*</c>
    /// (pusta lista, gdy nic nie odłożono).
    /// </summary>
    public IReadOnlyList<string> Ensure() => Collect(SecureDirectory.Ensure(FullPath));

    /// <summary>
    /// Zabezpiecza korzeń, a potem jego bezpośredni podkatalog <paramref name="leaf"/>. Liść spoza korzenia lub głębszy
    /// → <see cref="ArgumentException"/> zanim cokolwiek dotknie dysku. Zwraca ścieżki odłożone (korzeń, liść).
    /// </summary>
    internal IReadOnlyList<string> EnsureLeaf(string leaf)
    {
        if (leaf is null)
        {
            throw new ArgumentNullException(nameof(leaf));
        }

        var full = SecureDirectory.NormalizePath(leaf);
        if (!string.Equals(Path.GetDirectoryName(full), FullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Odmowa zabezpieczenia katalogu {full}: dozwolone są tylko bezpośrednie podkatalogi {FullPath}.",
                nameof(leaf));
        }

        return Collect(SecureDirectory.Ensure(FullPath), SecureDirectory.Ensure(full));
    }

    private static List<string> Collect(params string?[] asides)
    {
        var result = new List<string>();
        foreach (var aside in asides)
        {
            if (aside is not null)
            {
                result.Add(aside);
            }
        }

        return result;
    }

    private static bool IsFullyQualified(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return path.Length >= 3
            && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/');
    }

    private static bool IsForbidden(string normalized)
    {
        var root = Path.GetPathRoot(normalized) ?? string.Empty;
        if (normalized.Length <= root.Length)
        {
            return true;
        }

        foreach (var folder in ForbiddenFolders)
        {
            var known = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(known)
                && string.Equals(SecureDirectory.NormalizePath(known), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
