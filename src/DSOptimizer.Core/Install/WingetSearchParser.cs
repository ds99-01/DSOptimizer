using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Wiersz tabeli <c>winget search</c>/<c>list</c>. <see cref="Installable"/> = pełny identyfikator (<see cref="WingetArguments.IsValidWingetId"/>):
/// Id obcięte „…” albo puste = wiersz widoczny, ale nie do instalacji.
/// </summary>
public sealed record WingetSearchRow(string Name, string WingetId, string? Version, bool Installable);

/// <summary>
/// Tabela tekstowa winget (bez trybu JSON). Linia kresek (pierwsza z samych <c>-</c>, co najmniej 10) lokalizuje nagłówek: najbliższą
/// niepustą linię nad nią. Kreski nie mają przerw między kolumnami (pomiar), więc początki kolumn bierzemy z nagłówka: indeks znaku
/// niebiałego na początku linii albo po spacji. Kolejność kolumn: Name, Id, Version, reszta pominięta; słów nagłówka parser nie czyta
/// (są tłumaczone). Linie przed nagłówkiem (spinner, komunikaty źródła) i linie po tabeli zaczynające się od <c>&lt;</c> są
/// pomijane; brak linii kresek = pusta lista. Znak szeroki przed kolumną Id (R8) albo wiersz przesunięty względem nagłówka (znak
/// niebiały tuż przed kolumną Id albo Version – M5 final review) = <see cref="WingetSearchRow.Installable"/> false.
/// </summary>
public static class WingetSearchParser
{
    private const int MinDashes = 10;

    public static IReadOnlyList<WingetSearchRow> Parse(IReadOnlyList<string> lines)
    {
        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        var dashes = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsDashLine(lines[i]))
            {
                dashes = i;
                break;
            }
        }

        var header = dashes - 1;
        while (header >= 0 && string.IsNullOrWhiteSpace(lines[header]))
        {
            header--;
        }

        var rows = new List<WingetSearchRow>();
        if (header < 0)
        {
            return rows;
        }

        var starts = ColumnStarts(lines[header]);
        if (starts.Count < 2)
        {
            return rows;
        }

        for (var i = dashes + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("<", StringComparison.Ordinal))
            {
                // Pusta linia albo komunikat po tabeli (np. „<additional entries truncated due to result limit>”), nie wiersz.
                continue;
            }

            var name = Column(line, starts, 0);
            var id = Column(line, starts, 1);
            var version = starts.Count > 2 ? Column(line, starts, 2) : string.Empty;
            var installable = WingetArguments.IsValidWingetId(id) && IsAligned(line, starts) && !HasWideChar(line, starts[1]);
            rows.Add(new WingetSearchRow(name, id, version.Length == 0 ? null : version, installable));
        }

        return rows;
    }

    /// <summary>
    /// Ogólny strażnik przesunięcia kolumn (M5 final review): winget wyrównuje kolumny po szerokości wyświetlania i zawsze stawia
    /// spację między kolumnami, więc w wierszu zgodnym z nagłówkiem znak tuż przed początkiem kolumny Id i tuż przed początkiem
    /// kolumny Version (gdy jest i wiersz do niej sięga) jest biały. Znak zerowej szerokości (łączący, U+200B, selektor wariantu)
    /// przesuwa wiersz w prawo, znak szeroki spoza zakresów <see cref="HasWideChar"/> (np. emoji U+2615) – w lewo; oba
    /// przypadki łapie ten warunek bez listy zakresów. Id ze spacją odpada już w <see cref="WingetArguments.IsValidWingetId"/>.
    /// </summary>
    private static bool IsAligned(string line, List<int> starts)
    {
        var beforeId = starts[1] - 1;
        if (beforeId >= line.Length || !char.IsWhiteSpace(line[beforeId]))
        {
            return false;
        }

        return starts.Count < 3 || line.Length <= starts[2] || char.IsWhiteSpace(line[starts[2] - 1]);
    }

    /// <summary>
    /// Ruling R8: winget wyrównuje kolumny po szerokości wyświetlania, a parser tnie po znakach UTF-16, więc znak szeroki przed
    /// kolumną Id przesuwa wiersz i Id może być ucięte z przodu (np. „ncent.QQ”). Szerokości nie liczymy (reguły winget nie
    /// zmierzono): para zastępcza albo znak East Asian Wide/Fullwidth przed początkiem kolumny Id = wiersz nie do instalacji.
    /// </summary>
    private static bool HasWideChar(string line, int idStart)
    {
        var end = Math.Min(idStart, line.Length);
        for (var i = 0; i < end; i++)
        {
            var c = line[i];
            if (char.IsSurrogate(c)
                || (c >= 'ᄀ' && c <= 'ᅟ')
                || (c >= '⺀' && c <= '꓏')
                || (c >= '가' && c <= '힣')
                || (c >= '豈' && c <= '﫿')
                || (c >= '︰' && c <= '﹏')
                || (c >= '＀' && c <= '｠')
                || (c >= '￠' && c <= '￦'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDashLine(string line)
    {
        var text = line?.Trim() ?? string.Empty;
        if (text.Length < MinDashes)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (!char.IsWhiteSpace(header[i]) && (i == 0 || char.IsWhiteSpace(header[i - 1])))
            {
                starts.Add(i);
            }
        }

        return starts;
    }

    private static string Column(string line, List<int> starts, int index)
    {
        var start = starts[index];
        if (start >= line.Length)
        {
            return string.Empty;
        }

        var end = index + 1 < starts.Count ? Math.Min(starts[index + 1], line.Length) : line.Length;
        return line.Substring(start, end - start).Trim();
    }
}
