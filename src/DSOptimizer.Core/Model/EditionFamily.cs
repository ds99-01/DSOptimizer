using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Model;

/// <summary>Rodzina edycji Windows, dla której pozycja katalogu ma sens (pole <c>editions</c>).</summary>
public enum EditionFamily
{
    Home,
    Pro,
    Business,
}

/// <summary>Mapowanie <c>EditionID</c> z rejestru na rodzinę edycji.</summary>
public static class EditionFamilies
{
    private static readonly Dictionary<string, EditionFamily> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Core"] = EditionFamily.Home,
        ["CoreN"] = EditionFamily.Home,
        ["CoreSingleLanguage"] = EditionFamily.Home,
        ["CoreCountrySpecific"] = EditionFamily.Home,
        ["Professional"] = EditionFamily.Pro,
        ["ProfessionalN"] = EditionFamily.Pro,
        ["ProfessionalWorkstation"] = EditionFamily.Pro,
        ["ProfessionalWorkstationN"] = EditionFamily.Pro,
        ["ProfessionalEducation"] = EditionFamily.Pro,
        ["ProfessionalEducationN"] = EditionFamily.Pro,
        ["Enterprise"] = EditionFamily.Business,
        ["EnterpriseN"] = EditionFamily.Business,
        ["EnterpriseS"] = EditionFamily.Business,
        ["EnterpriseSN"] = EditionFamily.Business,
        ["Education"] = EditionFamily.Business,
        ["EducationN"] = EditionFamily.Business,
        ["IoTEnterprise"] = EditionFamily.Business,
        ["IoTEnterpriseS"] = EditionFamily.Business,
    };

    /// <summary>Rodzina dla znanego ID; <c>null</c> dla nieznanego, pustego i „unknown” (brak wiedzy nie blokuje).</summary>
    public static EditionFamily? FromEditionId(string? editionId)
        => editionId is not null && Map.TryGetValue(editionId, out var family) ? family : null;
}
