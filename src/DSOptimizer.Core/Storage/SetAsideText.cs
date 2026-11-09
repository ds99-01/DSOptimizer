using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Storage;

/// <summary>
/// Jednolite, zrozumiałe teksty ostrzeżeń, gdy program odłożył na bok (<c>.untrusted-*</c>) folder albo plik ze zmienionymi
/// uprawnieniami. Każdy tekst to jeden akapit bez podziałów linii, w jednym <c>L.F</c> z jednym literałem.
/// </summary>
public static class SetAsideText
{
    /// <summary>Odłożony korzeń programu; <paramref name="hadHistory"/> = w odłożonym folderze jest historia zmian.</summary>
    public static string Root(string root, string aside, bool hadHistory)
        => hadHistory
            ? L.F("Folder programu {0} miał zmienione uprawnienia, więc program odłożył go na bok i utworzył nowy. Najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików przy tym folderze. Poprzednie pliki, w tym historia zmian, są w {1}. Program zaczyna z ustawieniami domyślnymi i pustą historią zmian; przy zmianach sprzed odłożenia „Cofnij” przywróci wartości domyślne Windows, a nie stan sprzed zmiany. Nie otwieraj folderu programu w Eksploratorze, a raport zapisuj przyciskiem „Zapisz raport”.", root, aside)
            : L.F("Folder programu {0} miał zmienione uprawnienia, więc program odłożył go na bok i utworzył nowy. Najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików przy tym folderze. Poprzednie pliki są w {1}. Program zaczyna z ustawieniami domyślnymi. Nie otwieraj folderu programu w Eksploratorze, a raport zapisuj przyciskiem „Zapisz raport”.", root, aside);

    /// <summary>Odłożony podfolder programu (pobrania, logi winget, kopie napraw).</summary>
    public static string Folder(string aside)
        => L.F("Podfolder programu miał zmienione uprawnienia, więc program odłożył go na bok i utworzył nowy; najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików. Poprzednie pliki są w {0}. Nie otwieraj folderu programu w Eksploratorze.", aside);

    /// <summary>Odłożony plik ustawień.</summary>
    public static string Settings(string aside)
        => L.F("Plik ustawień miał zmienione uprawnienia, więc program odłożył go na bok i używa ustawień domyślnych; najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików. Poprzedni plik: {0}. Nie otwieraj folderu programu w Eksploratorze.", aside);

    /// <summary>Odłożona historia zmian; <paramref name="asides"/> = ścieżki plików historii rozdzielone <c>", "</c>.</summary>
    public static string History(string asides)
        => L.F("Historia zmian miała zmienione uprawnienia, więc program odłożył ją na bok i prowadzi nową; najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików. Poprzednia historia: {0}. Przy zmianach sprzed odłożenia „Cofnij” przywróci wartości domyślne Windows, a nie stan sprzed zmiany. Nie otwieraj folderu programu w Eksploratorze.", asides);
}
