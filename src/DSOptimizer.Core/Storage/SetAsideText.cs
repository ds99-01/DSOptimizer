using System;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Storage;

/// <summary>
/// Jednolite, zrozumiałe teksty, gdy program odłożył na bok (<c>.untrusted-*</c>) folder albo plik ze zmienionymi uprawnieniami
/// i gdy przenosi z niego historię zmian oraz ustawienia. Każdy tekst to jeden akapit bez podziałów linii, w jednym
/// <c>L.T</c>/<c>L.F</c> z jednym literałem.
/// </summary>
public static class SetAsideText
{
    /// <summary>Linia INFO: korzeń programu odłożony przy tym starcie.</summary>
    public static string RootMoved(string aside)
        => L.F("Folder programu miał zmienione uprawnienia, więc program odłożył go na bok ({0}) i utworzył nowy.", aside);

    /// <summary>Linia INFO: historia zmian z odłożonego korzenia przeszła do nowego.</summary>
    public static string HistoryMoved => L.T("Historię zmian przeniesiono do nowego folderu programu.");

    /// <summary>Linia INFO: ustawienia z odłożonego korzenia przeszły do nowego.</summary>
    public static string SettingsMoved => L.T("Ustawienia przeniesiono do nowego folderu programu.");

    /// <summary>Linia INFO: historia zmian odzyskana z dawnego odłożonego folderu (<paramref name="source"/> = plik).</summary>
    public static string HistoryRestored(string source)
        => L.F("Historię zmian odzyskano z {0}.", source);

    /// <summary>Linia WARN: plik historii <paramref name="source"/> nie przeszedł; <paramref name="reason"/> z <see cref="Reason"/>.</summary>
    public static string HistoryNotMoved(string source, string reason)
        => L.F("Nie przeniesiono historii zmian z {0}: {1}.", source, reason);

    /// <summary>Linia WARN: plik ustawień <paramref name="source"/> nie przeszedł; <paramref name="reason"/> z <see cref="Reason"/>.</summary>
    public static string SettingsNotMoved(string source, string reason)
        => L.F("Nie przeniesiono ustawień z {0}: {1}. Program używa ustawień domyślnych.", source, reason);

    /// <summary>Okno startowe: historia zmian utracona; <paramref name="asides"/> = foldery rozdzielone <c>", "</c>.</summary>
    public static string HistoryLost(string asides)
        => L.F("Dane programu zostały zmienione poza programem, więc historia zmian zaczyna się od nowa. Cofnięcie wcześniejszych zmian przywróci wartości domyślne Windows, a nie stan sprzed zmiany. Poprzednie pliki są w {0}.", asides);

    /// <summary>Odłożony podfolder programu (pobrania, logi winget, kopie napraw).</summary>
    public static string Folder(string aside)
        => L.F("Podfolder programu miał zmienione uprawnienia, więc program odłożył go na bok i utworzył nowy. Poprzednie pliki są w {0}.", aside);

    /// <summary>Odłożony plik ustawień.</summary>
    public static string Settings(string aside)
        => L.F("Plik ustawień miał zmienione uprawnienia, więc program odłożył go na bok i używa ustawień domyślnych. Poprzedni plik: {0}.", aside);

    /// <summary>Odłożona historia zmian; <paramref name="asides"/> = ścieżki plików historii rozdzielone <c>", "</c>.</summary>
    public static string History(string asides)
        => L.F("Historia zmian miała zmienione uprawnienia, więc program odłożył ją na bok i prowadzi nową. Poprzednia historia: {0}. Przy zmianach sprzed odłożenia „Cofnij” przywróci wartości domyślne Windows, a nie stan sprzed zmiany.", asides);

    /// <summary>
    /// Powód, dla którego plik nie przeszedł; <paramref name="detail"/> (komunikat, rozmiar, nazwa komputera) tylko dla powodów,
    /// które go pokazują. Jedna kropka na końcu <paramref name="detail"/> (komunikat wyjątku) jest zdejmowana, bo zdania
    /// <see cref="HistoryNotMoved"/> i <see cref="SettingsNotMoved"/> stawiają własną. Nieznana wartość = <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public static string Reason(SalvageRejection rejection, string? detail)
    {
        var shown = detail ?? string.Empty;
        if (shown.EndsWith(".", StringComparison.Ordinal))
        {
            shown = shown.Substring(0, shown.Length - 1);
        }

        return rejection switch
        {
            SalvageRejection.OpenFailed => L.F("nie można otworzyć pliku: {0}", shown),
            SalvageRejection.NotRegularFile => L.T("to nie jest zwykły plik (link, folder albo urządzenie)"),
            SalvageRejection.MultipleLinks => L.T("plik ma więcej niż jedno dowiązanie"),
            SalvageRejection.UntrustedAcl => L.T("plik ma obcego właściciela albo uprawnienia"),
            SalvageRejection.TooLarge => L.F("plik jest za duży ({0} B)", shown),
            SalvageRejection.Corrupted => L.F("plik jest uszkodzony ({0})", shown),
            SalvageRejection.OtherComputer => L.F("historia zmian pochodzi z innego komputera ({0})", shown),
            SalvageRejection.WriteFailed => L.F("nie można zapisać kopii: {0}", shown),
            _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, null),
        };
    }
}
