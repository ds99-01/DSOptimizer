using System;
using System.Collections.Generic;
using System.Linq;

namespace DSOptimizer.Core.Gui;

/// <summary>Strona menu: jedna kategoria albo scalona strona „Naprawy i narzędzia” z kilkoma sekcjami.</summary>
public sealed record NavCategoryPage(string Id, string Name, IReadOnlyList<GuiCategory> Sections, bool IsService);

/// <summary>Układ stron menu: kategorie napraw, narzędzi i paneli systemowych łączą się w jedną stronę.</summary>
public static class GuiNav
{
    public const string ServicePageId = "service";

    /// <summary>Kategorie scalone w stronę serwisową, w kolejności sekcji.</summary>
    public static IReadOnlyList<string> ServiceSections { get; } = new[] { "repair", "tools", "panels" };

    /// <summary>Strony menu: kategorie spoza serwisowych w kolejności wejścia, na końcu strona serwisowa (gdy ma sekcje).</summary>
    public static IReadOnlyList<NavCategoryPage> CategoryPages(IReadOnlyList<GuiCategory> categories)
    {
        if (categories is null)
        {
            throw new ArgumentNullException(nameof(categories));
        }

        var pages = categories
            .Where(c => !ServiceSections.Contains(c.Id))
            .Select(c => new NavCategoryPage(c.Id, c.Name, new[] { c }, false))
            .ToList();
        var sections = ServiceSections
            .Select(id => categories.FirstOrDefault(c => c.Id == id))
            .OfType<GuiCategory>()
            .ToList();
        if (sections.Count > 0)
        {
            pages.Add(new NavCategoryPage(ServicePageId, GuiText.ServicePageName, sections, true));
        }

        return pages;
    }
}
