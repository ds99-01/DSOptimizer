using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core;

/// <summary>Stałe identyfikujące produkt (nazwa, autor, wersja z assembly).</summary>
public static class ProductInfo
{
    public const string Name = "DSOptimizer";

    public const string AuthorName = "Dawid Świeboda";

    public const string RepositoryUrl = "https://github.com/ds99-01/DSOptimizer";

    /// <summary>Strona wydań na GitHubie (otwierana z baneru nowej wersji); dokładnie ten adres przepuszcza <c>ExternalLinkPolicy</c>.</summary>
    public const string ReleasesUrl = "https://github.com/ds99-01/DSOptimizer/releases";

    public const string RepositoryUrlText = "github.com/ds99-01/DSOptimizer";

    /// <summary>Strona wsparcia dla polskiego interfejsu; dokładnie ten adres przepuszcza <c>ExternalLinkPolicy</c>.</summary>
    public const string PolishSupportUrl = "https://buycoffee.to/dsoptimizer";

    /// <summary>Strona wsparcia dla angielskiego interfejsu; dokładnie ten adres przepuszcza <c>ExternalLinkPolicy</c>.</summary>
    public const string EnglishSupportUrl = "https://github.com/sponsors/ds99-01";

    /// <summary>Adres „Wesprzyj projekt” w języku, w którym działa program.</summary>
    public static string SupportUrl(UiLanguage language)
        => language == UiLanguage.Polish ? PolishSupportUrl : EnglishSupportUrl;

    /// <summary>Licencja programu (pełny tekst w pliku LICENSE w katalogu głównym repo).</summary>
    public const string LicenseName = "Mozilla Public License 2.0";

    /// <summary>Nazwa i znak zostają przy autorze (MPL 2.0, pkt 2.3).</summary>
    public static string TrademarkNotice =>
        L.T("Nazwa DSOptimizer i znak DŚO nie są objęte licencją MPL 2.0; przerobione wersje muszą nosić inną nazwę.");

    public static string Version =>
        typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
