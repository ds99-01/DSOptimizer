using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DSOptimizer.Core.Install;

/// <summary>Etap instalacji pozycji widoczny w oknie serii.</summary>
public enum InstallStage
{
    Downloading,
    Installing,
}

/// <summary>
/// Odczyt wyjścia <c>winget install</c> po podziale na linie (<c>ConsoleLineSplitter</c>, UTF-8). Format z pomiaru na VM 26H2
/// (winget 1.11, wyjście po angielsku mimo pl-PL): spinner <c>\r   - \r   \ …</c>, pasek <c>  ██████▒▒▒▒  14.0 MB / 44.1 MB</c>
/// (U+2588/U+2592, kropka dziesiętna), linie <c>Found &lt;nazwa&gt; [&lt;id&gt;] Version &lt;v&gt;</c>, <c>Downloading &lt;url&gt;</c>,
/// <c>Starting package install...</c>.
/// </summary>
public static class WingetOutput
{
    private const string SpinnerChars = "-\\|/";

    private static readonly Regex Sizes = new(
        @"(\d+(\.\d+)?)\s*(B|KB|MB|GB)\s*/\s*(\d+(\.\d+)?)\s*(B|KB|MB|GB)",
        RegexOptions.CultureInvariant);

    private static readonly Regex Percent = new(@"(?<!\d)(\d{1,3})\s?%", RegexOptions.CultureInvariant);

    private static readonly Regex OnlySizes = new(
        @"^\s*\d+(\.\d+)?\s*(B|KB|MB|GB)\s*/\s*\d+(\.\d+)?\s*(B|KB|MB|GB)\s*\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex OnlyPercent = new(@"^\s*\d{1,3}\s?%\s*\z", RegexOptions.CultureInvariant);

    private static readonly Regex Found = new(@"^Found (.+) \[([^\]\s]+)\] Version (\S+)", RegexOptions.CultureInvariant);

    private static readonly Regex VersionNumber = new(@"v\d+(\.\d+)+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Linia postępu (nie trafia do dziennika): po <c>Trim</c> same znaki spinnera <c>-\|/</c> (także pusta), znaki paska
    /// U+2588/U+2592, sam procent albo same rozmiary „X MB / Y MB”.
    /// </summary>
    public static bool IsProgressLine(string line)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        var trimmed = line.Trim();
        var spinner = true;
        foreach (var c in trimmed)
        {
            if (SpinnerChars.IndexOf(c) < 0)
            {
                spinner = false;
                break;
            }
        }

        return spinner
            || line.IndexOf('█') >= 0
            || line.IndexOf('▒') >= 0
            || OnlyPercent.IsMatch(line)
            || OnlySizes.IsMatch(line);
    }

    /// <summary>
    /// Procent z linii postępu (0–100, w dół): najpierw z rozmiarów „X / Y” (jednostki binarne, kropka dziesiętna), potem
    /// z „N%”. Linia, która nie jest linią postępu, albo wartość spoza 0–100 = <c>null</c>.
    /// </summary>
    public static int? TryParsePercent(string line)
    {
        if (!IsProgressLine(line))
        {
            return null;
        }

        var sizes = Sizes.Match(line);
        if (sizes.Success)
        {
            var done = Bytes(sizes.Groups[1].Value, sizes.Groups[3].Value);
            var total = Bytes(sizes.Groups[4].Value, sizes.Groups[6].Value);
            return total <= 0 || done > total ? null : (int)Math.Floor(done / total * 100m);
        }

        var percent = Percent.Match(line);
        if (!percent.Success)
        {
            return null;
        }

        var value = int.Parse(percent.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        return value <= 100 ? value : null;
    }

    /// <summary>Etap z linii tekstu: <c>Downloading </c> = pobieranie, <c>Starting package install</c> = instalowanie, inne = <c>null</c>.</summary>
    public static InstallStage? StageOf(string line)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        var text = line.TrimStart();
        if (text.StartsWith("Downloading ", StringComparison.Ordinal))
        {
            return InstallStage.Downloading;
        }

        return text.StartsWith("Starting package install", StringComparison.Ordinal) ? InstallStage.Installing : null;
    }

    /// <summary>Wersja z linii <c>Found &lt;nazwa&gt; [&lt;wingetId&gt;] Version &lt;v&gt;</c> dla tego identyfikatora (bez wielkości liter).</summary>
    public static string? FoundVersion(IEnumerable<string> lines, string wingetId)
    {
        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        if (wingetId is null)
        {
            throw new ArgumentNullException(nameof(wingetId));
        }

        foreach (var line in lines)
        {
            var match = Found.Match(line.TrimStart());
            if (match.Success && string.Equals(match.Groups[2].Value, wingetId, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups[3].Value;
            }
        }

        return null;
    }

    /// <summary>Wersja winget z wyjścia <c>--version</c>: pierwsze dopasowanie <c>v\d+(\.\d+)+</c> (jak <c>repair.winget</c>).</summary>
    public static string? ParseVersion(IReadOnlyList<string> output)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        foreach (var line in output)
        {
            var match = VersionNumber.Match(line);
            if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
    }

    private static decimal Bytes(string number, string unit)
    {
        var value = decimal.Parse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return unit switch
        {
            "KB" => value * 1024m,
            "MB" => value * 1024m * 1024m,
            "GB" => value * 1024m * 1024m * 1024m,
            _ => value,
        };
    }
}
