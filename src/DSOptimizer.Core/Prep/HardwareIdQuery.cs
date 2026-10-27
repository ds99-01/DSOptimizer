using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DSOptimizer.Core.Prep;

/// <summary>Fraza do wyszukiwania sterownika po identyfikatorze sprzętu urządzenia (bez numeru podsystemu i rewizji).</summary>
public static class HardwareIdQuery
{
    private static readonly Regex Pci = new(@"^PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Usb = new(@"^USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Pierwszy niepusty identyfikator; PCI i USB skrócone do producenta i urządzenia. Pusta lista = null.</summary>
    public static string? For(IReadOnlyList<string> hardwareIds)
    {
        if (hardwareIds is null)
        {
            return null;
        }

        foreach (var raw in hardwareIds)
        {
            var id = raw?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var match = Pci.Match(id);
            if (!match.Success)
            {
                match = Usb.Match(id);
            }

            return match.Success ? match.Value : id;
        }

        return null;
    }
}
