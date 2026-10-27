using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Hardware;

/// <summary>EditionID z rejestru → nazwa handlowa (ProductName na Windows 11 nadal mówi „Windows 10”).</summary>
public static class WindowsEditions
{
    private static readonly Dictionary<string, string> Editions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Core"] = "Home",
        ["CoreN"] = "Home N",
        ["CoreSingleLanguage"] = "Home Single Language",
        ["Professional"] = "Pro",
        ["ProfessionalN"] = "Pro N",
        ["ProfessionalWorkstation"] = "Pro for Workstations",
        ["ProfessionalEducation"] = "Pro Education",
        ["Education"] = "Education",
        ["Enterprise"] = "Enterprise",
        ["EnterpriseS"] = "Enterprise LTSC",
        ["IoTEnterprise"] = "IoT Enterprise",
        ["IoTEnterpriseS"] = "IoT Enterprise LTSC",
    };

    /// <summary>Nazwa handlowa; nieznane ID zwraca samo ID, brak albo „unknown” zwraca null.</summary>
    public static string? DisplayName(string? editionId)
    {
        if (string.IsNullOrWhiteSpace(editionId) || string.Equals(editionId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Editions.TryGetValue(editionId!, out var name) ? name : editionId;
    }
}
