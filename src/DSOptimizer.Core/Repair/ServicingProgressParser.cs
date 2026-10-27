using System.Globalization;
using System.Text.RegularExpressions;

namespace DSOptimizer.Core.Repair;

/// <summary>Procent postępu z linii DISM (<c>[====  45.0%  ]</c>) i SFC („Weryfikacja 70% ukończona.”) – bez zdań w języku systemu.</summary>
public static class ServicingProgressParser
{
    private static readonly Regex Percent = new(
        // [0-9], nie \d: \d dopuszcza cyfry Unicode (np. arabskie), a int.Parse rzuca na nich FormatException.
        @"(?<![0-9.,])([0-9]{1,3})(?:[.,][0-9]+)?\s?%",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Pierwszy procent w linii zaokrąglony w dół; brak albo poza 0–100 = <c>null</c>.</summary>
    public static int? TryParsePercent(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var match = Percent.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var value = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return value is >= 0 and <= 100 ? value : null;
    }
}
