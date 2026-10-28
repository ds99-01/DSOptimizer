using System;
using System.Linq;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Catalog;

/// <summary>
/// Teksty katalogu w języku interfejsu. Wczytany katalog jest zawsze polski (polski tekst = klucz tłumaczenia); okno tłumaczy go raz,
/// po ostatecznym wyborze języka. Hash, <c>id</c> i pola wykonawcze (skrypty, rejestr, usługi, launch, handler) nie zależą od języka,
/// więc journal i cofanie działają tak samo po zmianie języka.
/// </summary>
public static class CatalogTranslation
{
    /// <summary>
    /// Nowy katalog z tym samym <see cref="LoadedCatalog.Hash"/> i polami <c>Title</c>, <c>Summary</c>, <c>Benefit</c>,
    /// <c>Description</c>, <c>Warnings</c> w bieżącym języku (<see cref="L.Current"/>); brak tłumaczenia = tekst polski.
    /// Po polsku zwraca ten sam katalog bez kopiowania.
    /// </summary>
    public static LoadedCatalog Translate(LoadedCatalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (L.Current == UiLanguage.Polish)
        {
            return catalog;
        }

        var definitions = catalog.Definitions.Select(Translate).ToList();
        return new LoadedCatalog(definitions, catalog.Hash);
    }

    private static TweakDefinition Translate(TweakDefinition definition) => definition with
    {
        Title = L.Data(definition.Title),
        Summary = L.Data(definition.Summary),
        Benefit = definition.Benefit is null ? null : L.Data(definition.Benefit),
        Description = L.Data(definition.Description),
        Warnings = definition.Warnings.Select(L.Data).ToList(),
    };
}
