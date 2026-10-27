using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DSOptimizer.Core.Gui;

/// <summary>Tekstowy schowek systemu (w aplikacji WPF <c>Clipboard</c>, w testach fake).</summary>
public interface IClipboardText
{
    void SetText(string text);

    /// <summary>Tekst ze schowka albo null, gdy schowek nie zawiera tekstu.</summary>
    string? GetText();
}

/// <summary>
/// Wynik kopiowania. <see cref="Error"/> to błąd schowka: przy <see cref="Copied"/> = false tekst nie trafił do schowka,
/// przy true zapis zgłosił błąd, ale odczyt zwrotny potwierdził tekst (ślad do pliku logu).
/// </summary>
public sealed record ClipboardCopyResult(bool Copied, ExternalException? Error);

/// <summary>
/// Kopiowanie tekstu do schowka odporne na fałszywy CLIPBRD_E_CANT_OPEN (0x800401D0). WPF sam ponawia otwarcie
/// i opróżnienie schowka (po 10 prób co 100 ms) i rzuca wyjątek dopiero po nich; pod RustDesk tekst bywa już wtedy
/// w schowku. Własnych ponowień nie ma (wydłużałyby blokadę wątku UI) – po błędzie rozstrzyga jeden odczyt zwrotny.
/// </summary>
public static class ClipboardCopy
{
    /// <summary>Kopiuje <paramref name="text"/>; łapie tylko błędy schowka (<see cref="ExternalException"/>, w tym COMException).</summary>
    public static ClipboardCopyResult Copy(IClipboardText clipboard, string text)
    {
        if (clipboard is null)
        {
            throw new ArgumentNullException(nameof(clipboard));
        }

        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        try
        {
            clipboard.SetText(text);
            return new ClipboardCopyResult(true, null);
        }
        catch (ExternalException ex)
        {
            return new ClipboardCopyResult(Holds(clipboard, text), ex);
        }
    }

    /// <summary>Linia do pliku logu, gdy zapis zgłosił błąd, a odczyt zwrotny potwierdził tekst w schowku.</summary>
    public static string RecoveredMessage(ExternalException error)
    {
        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "Schowek zgłosił błąd 0x{0:X8} ({1}), ale tekst jest w schowku – uznano za skopiowane.",
            error.HResult,
            error.Message);
    }

    /// <summary>Czy schowek ma już ten tekst (końce linii bez znaczenia); zablokowany odczyt = nie.</summary>
    private static bool Holds(IClipboardText clipboard, string text)
    {
        string? current;
        try
        {
            current = clipboard.GetText();
        }
        catch (ExternalException)
        {
            return false;
        }

        return current is not null && string.Equals(NormalizeNewLines(current), NormalizeNewLines(text), StringComparison.Ordinal);
    }

    private static string NormalizeNewLines(string value) => value.Replace("\r\n", "\n");
}
