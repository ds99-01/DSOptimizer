using System;
using System.IO;

namespace DSOptimizer.Core.Storage;

/// <summary>
/// Reguła „link czy kopiowanie” dla ścieżki w dzienniku: lokalizację w Eksploratorze można pokazać tylko dla ścieżki
/// bezwzględnej poza katalogiem programu (<c>%ProgramData%\DSOptimizer</c> ma chroniony DACL), tam zostaje samo „Kopiuj ścieżkę”.
/// Reguła jest leksykalna: dowiązania symboliczne, junctions i nazwy 8.3 nie są rozwiązywane; drugą linią obrony jest ShellFileRevealer.
/// </summary>
public static class LogPathPolicy
{
    /// <summary>True tylko dla ścieżki bezwzględnej (litera dysku albo UNC) leżącej poza <paramref name="protectedRoot"/>.</summary>
    public static bool CanReveal(string? path, string protectedRoot)
    {
        if (string.IsNullOrWhiteSpace(protectedRoot))
        {
            throw new ArgumentException("Katalog chroniony nie może być pusty.", nameof(protectedRoot));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        path = path!.Replace('/', '\\');
        if (!IsAbsolute(path) || IsDevicePath(path) || IsLocalOrAdministrativeUnc(path))
        {
            return false;
        }

        try
        {
            var full = Normalize(path);
            var root = Normalize(protectedRoot);
            var inside = string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root.TrimEnd('\\') +"\\", StringComparison.OrdinalIgnoreCase);
            return !inside;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsAbsolute(string path)
    {
        if (path.Length >= 3 && IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        return path.Length >= 2 && path[0] == '\\' && path[1] == '\\';
    }

    // Ścieżki urządzeń (\\?\ i \\.\) GetFullPath zostawia bez zmian, więc nie da się ich porównać z korzeniem.
    private static bool IsDevicePath(string path)
        => path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal);

    // UNC do tego komputera (loopback) albo udział administracyjny (C$, ADMIN$) może wskazywać katalog chroniony.
    private static bool IsLocalOrAdministrativeUnc(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = path.Substring(2).Split('\\');
        var host = parts[0].Trim('[', ']');
        var share = parts.Length > 1 ? parts[1] : string.Empty;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("127.", StringComparison.Ordinal)
            || host == "::1"
            || host == "0:0:0:0:0:0:0:1"
            || share.EndsWith("$", StringComparison.Ordinal);
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    // Jak SecureDirectory.NormalizePath w DSOptimizer.Windows (Core nie widzi tego projektu).
    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length ? full.TrimEnd('\\') : full;
    }
}
