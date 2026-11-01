using DSOptimizer.Core.Prep;

namespace DSOptimizer.Core.Gui;

/// <summary>
/// Lista dozwolonych adresów zewnętrznych: strona repozytorium programu, strona jego wydań i strony wsparcia (dokładnie w tych postaciach, bez przycinania i normalizacji),
/// adresy strony „Przygotowanie” (<see cref="PrepLinks"/>) oraz strona „Instalatora aplikacji” w Microsoft Store (naprawa winget).
/// </summary>
public static class ExternalLinkPolicy
{
    /// <summary>„Instalator aplikacji” (App Installer, winget) w Sklepie; id 9NBLGGH4NNS1 potwierdzone w pomiarach Etapu 4b (tabela decyzji, 6i). Ten sam adres jest w komunikacie naprawy winget.</summary>
    public const string AppInstallerStoreUrl = "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1";

    public static bool IsAllowed(string? url)
        => string.Equals(url, ProductInfo.RepositoryUrl, System.StringComparison.Ordinal)
            || string.Equals(url, ProductInfo.ReleasesUrl, System.StringComparison.Ordinal)
            || string.Equals(url, ProductInfo.PolishSupportUrl, System.StringComparison.Ordinal)
            || string.Equals(url, ProductInfo.EnglishSupportUrl, System.StringComparison.Ordinal)
            || string.Equals(url, AppInstallerStoreUrl, System.StringComparison.Ordinal)
            || PrepLinks.IsAllowed(url);
}
