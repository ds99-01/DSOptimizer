using System;
using System.Text;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Filtr listy strony „Instalacja”: zapytanie szuka w tytule, opisie, Id winget i repozytorium GitHub pozycji. Obie strony są
/// normalizowane tak samo (<see cref="Normalize"/>), więc „7zip” znajduje „7-Zip”, a „paint net” – „Paint.NET”.
/// </summary>
public static class InstallSearch
{
    /// <summary><see cref="SearchText.Fold"/> bez spacji, „-”, „.” i „_”.</summary>
    public static string Normalize(string? text)
    {
        var folded = SearchText.Fold(text);
        var builder = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (c is not (' ' or '-' or '.' or '_'))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Puste (po normalizacji) zapytanie = <c>true</c>; inaczej podciąg w tytule, opisie, <c>WingetId</c> albo <c>GitHub</c>.</summary>
    public static bool Matches(InstallTarget target, string? summary, string query)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var needle = Normalize(query);
        if (needle.Length == 0)
        {
            return true;
        }

        return Contains(target.Title, needle)
            || Contains(summary, needle)
            || Contains(target.WingetId, needle)
            || Contains(target.GitHub, needle);
    }

    private static bool Contains(string? haystack, string needle)
        => Normalize(haystack).IndexOf(needle, StringComparison.Ordinal) >= 0;
}
