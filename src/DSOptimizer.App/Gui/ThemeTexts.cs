using System;
using System.Windows;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Teksty używane w stylach i szablonach <c>Themes/Tokens.xaml</c>. Słowniki stylów powstają przy <c>App.InitializeComponent</c>,
/// zanim jest wybrany język, więc nie mają tekstów ani <c>{l:T}</c>: odwołują się do zasobów dynamicznych o tych kluczach,
/// a <see cref="Apply"/> wpisuje je do zasobów aplikacji po ustaleniu języka (<c>Program</c>, za <c>L.SetDefault</c>).
/// </summary>
internal static class ThemeTexts
{
    /// <summary>Podpowiedź trójkąta ▶ przy akcji i panelu (<c>RunMarkerTemplate</c>).</summary>
    public const string RunToolTipKey = "ThemeRunToolTip";

    /// <summary>Podpowiedź skrótu przy pustym polu wyszukiwania (<c>SearchTextBox</c>).</summary>
    public const string SearchShortcutKey = "ThemeSearchShortcut";

    /// <summary>Podpowiedź przycisku czyszczenia pola wyszukiwania.</summary>
    public const string SearchClearToolTipKey = "ThemeSearchClearToolTip";

    /// <summary>Nazwa przycisku czyszczenia dla czytnika ekranu.</summary>
    public const string SearchClearNameKey = "ThemeSearchClearName";

    public static void Apply(ResourceDictionary resources)
    {
        if (resources is null)
        {
            throw new ArgumentNullException(nameof(resources));
        }

        resources[RunToolTipKey] = L.T("Uruchom");
        // Nazwa klawiszy jak na klawiaturze i w Windows – bez tłumaczenia.
        resources[SearchShortcutKey] = "Ctrl+F";
        resources[SearchClearToolTipKey] = L.T("Wyczyść (Esc)");
        resources[SearchClearNameKey] = L.T("Wyczyść wyszukiwanie");
    }
}
