namespace DSOptimizer.Core.Gui;

/// <summary>
/// Klucze ikon stylu A (konturowe, siatka 20×20) dla menu i akcji; geometrie leżą w <c>Themes/Icons.xaml</c> jako
/// <c>Icon{Klucz}Stroke</c> (kontur) i <c>Icon{Klucz}Fill</c> (część wypełniona, gdy jest).
/// </summary>
public static class GuiIcons
{
    public const string Specification = "Specification";
    public const string Settings = "Settings";
    public const string History = "History";
    public const string Suggested = "Suggested";

    /// <summary>Strona „Aplikacje” (K1: siatka 2×2 kafli, wybór Dawida 2026-10-21).</summary>
    public const string Apps = "Apps";

    /// <summary>Strona „Instalacja” (I2: strzałka w dół nad tacą, render Tasku 1 Etapu 4c, ruling R5).</summary>
    public const string Install = "Install";

    /// <summary>Strona „Przygotowanie” (I1: lista z ptaszkami, wybór Dawida 2026-10-21).</summary>
    public const string Prep = "Prep";

    /// <summary>Kłódka aplikacji chronionej (zamiast pola wyboru).</summary>
    public const string Lock = "Lock";

    /// <summary>Kategoria „Naprawy” (klucz płaski dwustronny, wariant A, wybór Dawida 2026-10-22).</summary>
    public const string Repair = "Repair";

    /// <summary>Klucz ikony kategorii z katalogu; kategoria spoza wbudowanych dostaje ikonę narzędzi.</summary>
    public static string ForCategory(string categoryId) => categoryId switch
    {
        "privacy" => "Privacy",
        "perf" => "Performance",
        "disk" => "Disk",
        "net" => "Network",
        "sec" => "Security",
        "ux" => "Explorer",
        "panels" => "Panels",
        "repair" => "Repair",
        _ => "Tools",
    };
}
