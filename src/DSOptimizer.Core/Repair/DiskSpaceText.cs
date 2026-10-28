using System.Globalization;
using DSOptimizer.Core.Localization;

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
            return L.T("nie udało się odczytać wolnego miejsca");
        }

        var delta = a - b;
        if (delta < 0)
        {
            return L.T("nie da się uczciwie podać zwolnionego miejsca (inne programy zapisywały na dysku)");
        }

        return Size(delta);
    }

    /// <summary>
    /// Rozmiar w MB albo GB z jednym miejscem po separatorze dziesiętnym języka (<see cref="L.Culture"/>: „1,5 GB” po polsku,
    /// „1.5 GB” po angielsku), zawsze zaokrąglony w dół (nigdy zawyżony).
    /// </summary>
    public static string Size(long bytes)
    {
        if (bytes < Megabyte)
        {
            return L.T("mniej niż 1 MB");
        }

        if (bytes < Gigabyte)
        {
            return $"{(bytes / Megabyte).ToString(CultureInfo.InvariantCulture)} MB";
        }

        // Dziesiąte części z pełnego iloczynu: dzielnik Gigabyte / 10 jest ucięty (107374182), więc tuż pod 2 GB dawał „2,0 GB”.
        var tenths = bytes * 10 / Gigabyte;
        var separator = L.Culture.NumberFormat.NumberDecimalSeparator;
        return $"{(tenths / 10).ToString(CultureInfo.InvariantCulture)}{separator}{(tenths % 10).ToString(CultureInfo.InvariantCulture)} GB";
    }
}
