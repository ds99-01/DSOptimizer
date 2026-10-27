using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DSOptimizer.Core.Storage;

namespace DSOptimizer.Core.Gui;

/// <summary>Jak pokazać ścieżkę z linii dziennika.</summary>
public enum LogPathMode
{
    /// <summary>Linia bez ścieżki.</summary>
    None,

    /// <summary>Link „Otwórz w Eksploratorze”.</summary>
    Reveal,

    /// <summary>Link nieaktywny: ścieżkę wolno pokazać, ale pliku już nie ma.</summary>
    RevealMissing,

    /// <summary>Zwykły tekst z „Kopiuj ścieżkę” (katalog programu albo ścieżka niebezwzględna).</summary>
    CopyOnly,
}

/// <summary>Tekst linii dziennika podzielony wokół ścieżki: <see cref="Before"/> + <see cref="Path"/> + <see cref="After"/>.</summary>
public sealed record LogLineParts(string Before, string? Path, string After);

/// <summary>Tekst linii dziennika w oknie i w schowku (bez WPF).</summary>
public static class LogLineText
{
    private const string CopiedNote = "Skopiowano ścieżkę.";

    /// <summary>Godzina linii w czasie lokalnym, <c>HH:mm:ss</c>.</summary>
    public static string Time(DateTimeOffset at) => at.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Linia do schowka: godzina, poziom w nawiasach, tekst.</summary>
    public static string ForClipboard(GuiLogLine line)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        return $"{Time(line.At)} {LevelName(line.Level)} {line.Text}";
    }

    /// <summary>„Kopiuj cały dziennik”: linie jak <see cref="ForClipboard(GuiLogLine)"/>, każda w osobnym wierszu.</summary>
    public static string ForClipboard(IEnumerable<GuiLogLine> lines)
    {
        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        return string.Join(Environment.NewLine, lines.Select(ForClipboard));
    }

    /// <summary>
    /// Dzieli tekst wokół ścieżki linii (ostatnie wystąpienie, porównanie dokładne). Ścieżki nie ma w tekście = dopisana po spacji.
    /// Linia bez ścieżki: cały tekst w <see cref="LogLineParts.Before"/>.
    /// </summary>
    public static LogLineParts Split(GuiLogLine line)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        if (string.IsNullOrEmpty(line.Path))
        {
            return new LogLineParts(line.Text, null, string.Empty);
        }

        var path = line.Path!;
        var index = line.Text.LastIndexOf(path, StringComparison.Ordinal);
        return index < 0
            ? new LogLineParts(line.Text + " ", path, string.Empty)
            : new LogLineParts(line.Text.Substring(0, index), path, line.Text.Substring(index + path.Length));
    }

    /// <summary>
    /// Link tylko dla ścieżki, którą <see cref="LogPathPolicy.CanReveal"/> pozwala pokazać w Eksploratorze; istnienie pliku
    /// sprawdzane tylko wtedy. Reszta = samo kopiowanie.
    /// </summary>
    public static LogPathMode PathMode(string? path, string protectedRoot, Func<string, bool> fileExists)
    {
        if (fileExists is null)
        {
            throw new ArgumentNullException(nameof(fileExists));
        }

        if (string.IsNullOrEmpty(path))
        {
            return LogPathMode.None;
        }

        if (!LogPathPolicy.CanReveal(path, protectedRoot))
        {
            return LogPathMode.CopyOnly;
        }

        return fileExists(path!) ? LogPathMode.Reveal : LogPathMode.RevealMissing;
    }

    /// <summary>Linia ostrzeżenia po odmowie Eksploratora: komunikat i (gdy ścieżka trafiła do schowka) „Skopiowano ścieżkę.”.</summary>
    public static string RevealFailed(string message, bool copied)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (!copied)
        {
            return message;
        }

        var sentence = message.EndsWith(".", StringComparison.Ordinal) ? message : message + ".";
        return $"{sentence} {CopiedNote}";
    }

    private static string LevelName(GuiLogLevel level) => level switch
    {
        GuiLogLevel.Error => "[Błąd]",
        GuiLogLevel.Warning => "[Ostrzeżenie]",
        _ => "[Info]",
    };
}
