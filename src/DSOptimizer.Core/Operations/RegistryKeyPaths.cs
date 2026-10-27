using System;

namespace DSOptimizer.Core.Operations;

/// <summary>
/// Operacje na ścieżkach kluczy (HKLM\..., HKU\&lt;SID&gt;\..., HKCR\...) potrzebne do sprzątania kluczy utworzonych przez Apply.
/// Korzeń hive: sam hive (HKLM/HKCR/HKCU), a dla HKU para HKU\&lt;SID&gt;. Klucz wolno kasować dopiero od 2 segmentów pod korzeniem.
/// </summary>
internal static class RegistryKeyPaths
{
    private const int MinDepthBelowRoot = 2;

    private static readonly char[] Separator = { '\\' };

    public static string[] Segments(string path)
        => (path ?? string.Empty).Split(Separator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Liczba segmentów pod korzeniem hive (dla HKU pod HKU\&lt;SID&gt;). Poniżej zera nie schodzi.</summary>
    public static int DepthBelowRoot(string path)
    {
        var segments = Segments(path);
        if (segments.Length == 0)
        {
            return 0;
        }

        var rootLength = string.Equals(segments[0], "HKU", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        return Math.Max(0, segments.Length - rootLength);
    }

    /// <summary>Czy klucz leży wystarczająco głęboko, by móc być kasowany / być CreatedKeyRoot.</summary>
    public static bool IsDeletable(string path) => DepthBelowRoot(path) >= MinDepthBelowRoot;

    /// <summary>Ścieżka rodzica albo null, gdy nie ma segmentów nad kluczem.</summary>
    public static string? Parent(string path)
    {
        var segments = Segments(path);
        return segments.Length <= 1 ? null : string.Join("\\", segments, 0, segments.Length - 1);
    }

    /// <summary>Czy <paramref name="ancestor"/> jest przodkiem <paramref name="path"/> albo równa mu się (segmentami, bez wielkości liter).</summary>
    public static bool IsSelfOrAncestor(string ancestor, string path)
    {
        var a = Segments(ancestor);
        var p = Segments(path);
        if (a.Length == 0 || a.Length > p.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], p[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
