using System.Globalization;

namespace DSOptimizer.Core.Repair;

/// <summary>Tekst „zwolniono” z dwóch odczytów wolnego miejsca. Nigdy nie podaje ujemnej ani zmyślonej liczby.</summary>
public static class DiskSpaceText
{
    private const long Megabyte = 1024L * 1024;
    private const long Gigabyte = 1024L * Megabyte;

    public static string Freed(long? before, long? after)
    {
        if (before is not { } b || after is not { } a)
        {
            return "nie udało się odczytać wolnego miejsca";
        }

        var delta = a - b;
        if (delta < 0)
        {
            return "nie da się uczciwie podać zwolnionego miejsca (inne programy zapisywały na dysku)";
        }

        return Size(delta);
    }

    /// <summary>Rozmiar w MB albo GB z jednym miejscem po przecinku, zawsze zaokrąglony w dół (nigdy zawyżony).</summary>
    public static string Size(long bytes)
    {
        if (bytes < Megabyte)
        {
            return "mniej niż 1 MB";
        }

        if (bytes < Gigabyte)
        {
            return $"{(bytes / Megabyte).ToString(CultureInfo.InvariantCulture)} MB";
        }

        // Dziesiąte części z pełnego iloczynu: dzielnik Gigabyte / 10 jest ucięty (107374182), więc tuż pod 2 GB dawał „2,0 GB”.
        var tenths = bytes * 10 / Gigabyte;
        return $"{(tenths / 10).ToString(CultureInfo.InvariantCulture)},{(tenths % 10).ToString(CultureInfo.InvariantCulture)} GB";
    }
}
