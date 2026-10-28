using System;

namespace DSOptimizer.Core.Localization;

/// <summary>Język interfejsu programu. Teksty źródłowe w kodzie są polskie; angielski pochodzi z tabeli tłumaczeń.</summary>
public enum UiLanguage
{
    Polish,
    English,
}

/// <summary>Kody języków zapisywane w ustawieniach i plikach tłumaczeń.</summary>
public static class UiLanguageCodes
{
    public const string PolishCode = "pl";

    public const string EnglishCode = "en";

    /// <summary>Kod zapisywany w ustawieniach: <c>pl</c> albo <c>en</c>.</summary>
    public static string ToCode(UiLanguage language) => language switch
    {
        UiLanguage.Polish => PolishCode,
        UiLanguage.English => EnglishCode,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Nieznany język interfejsu."),
    };

    /// <summary>Rozpoznaje dokładnie <c>pl</c> i <c>en</c> (porównanie porządkowe, bez przycinania spacji).</summary>
    public static bool TryParse(string? code, out UiLanguage language)
    {
        if (string.Equals(code, PolishCode, StringComparison.Ordinal))
        {
            language = UiLanguage.Polish;
            return true;
        }

        if (string.Equals(code, EnglishCode, StringComparison.Ordinal))
        {
            language = UiLanguage.English;
            return true;
        }

        language = UiLanguage.Polish;
        return false;
    }
}
