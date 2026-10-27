namespace DSOptimizer.Core;

/// <summary>Stałe identyfikujące produkt (nazwa, autor, wersja z assembly).</summary>
public static class ProductInfo
{
    public const string Name = "DSOptimizer";

    public const string AuthorName = "Dawid Świeboda";

    public const string AuthorUrl = "https://github.com/ds99-01";

    public const string AuthorUrlText = "github.com/ds99-01";

    /// <summary>Licencja programu (pełny tekst w pliku LICENSE w katalogu głównym repo).</summary>
    public const string LicenseName = "Mozilla Public License 2.0";

    /// <summary>Nazwa i znak zostają przy autorze (MPL 2.0, pkt 2.3).</summary>
    public const string TrademarkNotice =
        "Nazwa DSOptimizer i znak DŚO nie są objęte licencją MPL 2.0; przerobione wersje muszą nosić inną nazwę.";

    public static string Version =>
        typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
