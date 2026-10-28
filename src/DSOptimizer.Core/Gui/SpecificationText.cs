using System;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Gui;

/// <summary>Teksty strony „Specyfikacja”: linie dziennika po zapisie i kopiowaniu raportu oraz łamanie wartości na ekranie.</summary>
public static class SpecificationText
{
    /// <summary>Nazwa komponentu w pliku logu i Dzienniku (tłumaczona, jak nazwa strony).</summary>
    public static string Component => L.T("Specyfikacja");

    public static string CopiedMessage => L.T("Skopiowano specyfikację do schowka.");

    private const char NoBreakSpace = '\u00A0';

    public static string CopyFailedMessage(string reason) => L.F("Nie skopiowano specyfikacji do schowka: {0}", reason);

    /// <summary>
    /// Wynik zapisu jednej kopii raportu do dziennika: zapisana = Info ze ścieżką (plik logu oraz, gdy podano
    /// <paramref name="panel"/>, widoczny panel okna – UiLogSink pomija Info z TeeLog), inaczej Warn ze ścieżką i powodem
    /// (Warn sam trafia do panelu przez TeeLog, więc nie jest dublowany).
    /// </summary>
    public static void LogSaveResult(ILog log, string path, bool saved, string? error, Action<GuiLogLevel, string, string?>? panel = null)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        if (saved)
        {
            var text = L.F("Zapisano raport: {0}", path);
            log.Info(Component, text);
            panel?.Invoke(GuiLogLevel.Info, text, path);
        }
        else
        {
            log.Warn(Component, L.F("Nie zapisano raportu w {0}: {1}", path, error));
        }
    }

    /// <summary>Potwierdzenie skopiowania: Info do pliku logu i (gdy podano <paramref name="panel"/>) do panelu okna.</summary>
    public static void LogCopied(ILog log, Action<GuiLogLevel, string, string?>? panel = null)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        var message = CopiedMessage;
        log.Info(Component, message);
        panel?.Invoke(GuiLogLevel.Info, message, null);
    }

    /// <summary>
    /// Najdłuższy człon (w znakach) trzymany w jednym wierszu. Dłuższy zostaje ze zwykłymi spacjami: wiersz zawija się
    /// wewnątrz niego zamiast go uciąć. 26 znaków („MSI MAG B650 TOMAHAWK WIFI”) mieści się w kolumnie wartości
    /// przy najwęższym układzie dwukolumnowym (szerokość treści 640 px).
    /// </summary>
    public const int MaxUnbreakableLength = 26;

    /// <summary>
    /// Wartość na ekran: spacje wewnątrz członów (między przecinkami) stają się twarde, więc wiersz łamie się tylko po przecinku
    /// („NVIDIA GeForce RTX 4060, 8 GB, sterownik 581.57”); wyjątek: człon dłuższy niż <see cref="MaxUnbreakableLength"/>.
    /// Raport i schowek dostają tekst bez tej zamiany.
    /// </summary>
    public static string BreakOnlyAfterCommas(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var text = new StringBuilder(value.Length);
        var segments = value.Split(',');
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }

            var segment = segments[i];
            var start = 0;
            while (start < segment.Length && segment[start] == ' ')
            {
                start++;
            }

            // Spacja zaraz po przecinku zostaje zwykła – to jedyne miejsce łamania wiersza.
            text.Append(segment, 0, start);
            var member = segment.Substring(start);
            text.Append(member.Length <= MaxUnbreakableLength ? member.Replace(' ', NoBreakSpace) : member);
        }

        return text.ToString();
    }
}
