namespace DSOptimizer.Core.Gui;

/// <summary>Kolory akcentu treści jednego motywu („#RRGGBB”): tło, najechanie, wciśnięcie oraz tekst i ptaszek na akcencie.</summary>
public sealed record BrandAccentColors(string Accent, string Hover, string Pressed, string OnAccent);

/// <summary>
/// Akcent „S4” (cyjan, decyzja autora 2026-10-26, wariant B): w treści zależny od motywu – w jasnym ciemniejszy cyjan z białym tekstem
/// (kontrast pola wyboru i paska z tłem ≥ 4:1), w ciemnym jasny cyjan z ciemnym tekstem. Znacznik menu i pierścień znaku leżą na stałej
/// ciemnej ramie, więc mają jeden kolor w obu motywach. Aplikacja wpisuje te wartości do zasobów przy każdej zmianie motywu
/// (<c>ThemeController</c>); <c>Tokens.xaml</c> ma wartości jasnego motywu, <c>BrandMark.xaml</c> literalny pierścień (pilnują tego testy).
/// </summary>
public static class BrandPalette
{
    /// <summary>Znacznik wybranej pozycji menu (ciemna rama w obu motywach).</summary>
    public const string NavMarker = "#00A9D4";

    /// <summary>Pierścień „O” znaku „DŚO” (okno, pasek marki, ikona exe).</summary>
    public const string MarkRing = "#00A9D4";

    /// <summary>Litery „DŚ” znaku na białym kwadracie.</summary>
    public const string MarkLetters = "#17191B";

    public static BrandAccentColors Light { get; } = new("#007CA5", "#006D93", "#005E82", "#FFFFFF");

    public static BrandAccentColors Dark { get; } = new("#00A9D4", "#37B9E2", "#0093BA", "#17191B");

    /// <summary>Akcent treści dla motywu: ciemny tylko przy ciemnym, każdy inny (jasny, wysoki kontrast) = jasny.</summary>
    public static BrandAccentColors For(bool darkTheme) => darkTheme ? Dark : Light;
}
