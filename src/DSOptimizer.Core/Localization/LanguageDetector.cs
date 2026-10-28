using System;
using System.Globalization;

namespace DSOptimizer.Core.Localization;

/// <summary>Wybór języka interfejsu z języka systemu: polski dla <c>pl</c>, angielski dla każdego innego.</summary>
public static class LanguageDetector
{
    public static UiLanguage Detect(CultureInfo uiCulture)
    {
        if (uiCulture is null)
        {
            throw new ArgumentNullException(nameof(uiCulture));
        }

        return string.Equals(uiCulture.TwoLetterISOLanguageName, UiLanguageCodes.PolishCode, StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Polish
            : UiLanguage.English;
    }

    public static UiLanguage DetectCurrent() => Detect(CultureInfo.CurrentUICulture);
}
