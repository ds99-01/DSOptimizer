using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Jedyne miejsce składania linii argumentów winget. Każde polecenie pakietu ma <c>--source winget</c>; instalacja zawsze
/// z <c>--exact</c> i <c>--no-upgrade</c>, nigdy z <c>--force</c>, <c>--ignore-security-hash</c>, <c>--scope</c> ani
/// <c>--no-progress</c> (nieznany w 1.11).
/// </summary>
public static class WingetArguments
{
    /// <summary>Najdłuższe zapytanie wyszukiwania (po <c>Trim</c>).</summary>
    public const int MaxQueryLength = 100;

    private static readonly char[] QuoteTriggers = { ' ', '\t', '"' };

    private static readonly Regex WingetId = new(@"^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Pełny identyfikator pakietu (np. <c>Notepad++.Notepad++</c>, <c>Microsoft.VCRedist.2015+.x64</c>). Id obcięte „…” albo puste
    /// z tabeli <c>winget search</c> nie przechodzi, więc nie da się go zainstalować.
    /// </summary>
    public static bool IsValidWingetId(string? id) => id is not null && WingetId.IsMatch(id);

    /// <summary>
    /// Zapytanie: niepuste po <c>Trim</c>, najwyżej 100 znaków, bez cudzysłowu i znaków sterujących, nie zaczyna się od <c>-</c>
    /// (token od „-” winget mógłby wziąć za opcję, np. <c>--source msstore</c>). Teksty błędów z <see cref="InstallText"/>.
    /// </summary>
    public static bool TryValidateQuery(string? query, out string? error)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            error = InstallText.QueryEmpty;
            return false;
        }

        if (text.Length > MaxQueryLength)
        {
            error = InstallText.QueryTooLong(MaxQueryLength);
            return false;
        }

        if (text.Any(c => c == '"' || char.IsControl(c)))
        {
            error = InstallText.QueryInvalidChars;
            return false;
        }

        if (text[0] == '-')
        {
            error = InstallText.QueryLeadingDash;
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Cytowanie jednego argumentu według reguł <c>CommandLineToArgvW</c>: w cudzysłowie, gdy jest pusty albo ma spację, tabulator
    /// lub cudzysłów; ukośniki przed cudzysłowem (także zamykającym) podwojone, cudzysłów wewnątrz jako <c>\"</c>.
    /// </summary>
    public static string Quote(string argument)
    {
        if (argument is null)
        {
            throw new ArgumentNullException(nameof(argument));
        }

        if (argument.Length > 0 && argument.IndexOfAny(QuoteTriggers) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        return builder.Append('\\', backslashes * 2).Append('"').ToString();
    }

    public static string Version() => "--version";

    /// <summary>
    /// <c>install --id &lt;id&gt; --exact --source winget --silent --no-upgrade --accept-package-agreements
    /// --accept-source-agreements --disable-interactivity --log &lt;plik&gt;</c> (spec 5.1, kolejność stała). Bez <c>--locale</c>
    /// (Ruling R14: z <c>pl-PL</c> winget wybierał stare wydania, a większość pakietów nie ma instalatora w tym języku).
    /// </summary>
    public static string Install(string wingetId, string logPath)
    {
        RequireId(wingetId);
        if (string.IsNullOrWhiteSpace(logPath))
        {
            throw new ArgumentException("Brak ścieżki logu winget.", nameof(logPath));
        }

        return "install --id " + wingetId + " --exact --source winget --silent --no-upgrade"
            + " --accept-package-agreements --accept-source-agreements --disable-interactivity --log " + Quote(logPath);
    }

    /// <summary><c>search --query &lt;tekst&gt; --source winget --count 50 …</c> (spec 7); nieprawidłowe zapytanie = <see cref="ArgumentException"/>.</summary>
    public static string Search(string query)
    {
        if (!TryValidateQuery(query, out var error))
        {
            throw new ArgumentException(error, nameof(query));
        }

        return "search --query " + Quote(query.Trim()) + " --source winget --count 50 --accept-source-agreements --disable-interactivity";
    }

    /// <summary>Odczyt zwrotny pozycji spoza listy: <c>list --id &lt;id&gt; --exact --source winget …</c>.</summary>
    public static string ListExact(string wingetId)
    {
        RequireId(wingetId);
        return "list --id " + wingetId + " --exact --source winget --accept-source-agreements --disable-interactivity";
    }

    private static void RequireId(string wingetId)
    {
        if (!IsValidWingetId(wingetId))
        {
            throw new ArgumentException("Nieprawidłowy identyfikator winget: " + wingetId, nameof(wingetId));
        }
    }
}
