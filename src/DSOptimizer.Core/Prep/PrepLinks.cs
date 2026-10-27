using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DSOptimizer.Core.Prep;

/// <summary>Sprawdzony adres producenta (chipset albo grafika); <see cref="CheckedOn"/> = data sprawdzenia z docs/prep/README.md.</summary>
public sealed record VendorLink(string Vendor, string Url, DateTime CheckedOn);

/// <summary>Strona wsparcia producenta laptopa i nazwy, pod jakimi producent występuje w WMI (<c>Win32_ComputerSystem.Manufacturer</c>).</summary>
public sealed record OemSupport(string Vendor, string[] ManufacturerNames, string Url, DateTime CheckedOn);

/// <summary>
/// Jedyne adresy, które strona „Przygotowanie” może otworzyć: sprawdzone strony producentów, wyszukiwanie Google z poprawnie
/// zakodowaną frazą i dwa adresy Ustawień. Treść list pochodzi z docs/prep/README.md (test <c>LinksMatchDocsPrepReadme</c> pilnuje zgodności).
/// Do argumentu <c>explorer.exe</c> nigdy nie trafia surowy napis z WMI: pochodzi z tej klasy, a <see cref="IsAllowed(string?)"/> odrzuca resztę.
/// </summary>
public static class PrepLinks
{
    public const string WindowsUpdate = "ms-settings:windowsupdate";
    public const string OptionalUpdates = "ms-settings:windowsupdate-optionalupdates";

    private const string SearchPrefix = "https://www.google.com/search?q=";
    private const int MaxEncodedQueryLength = 300;

    private static readonly DateTime Checked = new(2026, 10, 21);

    // Dokładnie wyjście Search: znak niezakodowany albo %XX (wielkie litery hex). Lookahead ogranicza całą frazę do 300 znaków
    // zakodowanych (tak samo przycina Search); sam kwantyfikator liczyłby %XX jako jedną jednostkę.
    private static readonly Regex SearchUrl = new(
        @"^https://www\.google\.com/search\?q=(?=.{1,300}\z)(?:[A-Za-z0-9._~-]|%[0-9A-F]{2})+\z",
        RegexOptions.CultureInvariant);

    private static readonly VendorLink IntelChipset = new("Intel", "https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html", Checked);
    private static readonly VendorLink AmdChipset = new("AMD", "https://www.amd.com/en/support/download/drivers.html", Checked);
    private static readonly VendorLink NvidiaGraphics = new("NVIDIA", "https://www.nvidia.com/pl-pl/drivers/", Checked);
    private static readonly VendorLink AmdGraphics = new("AMD", "https://www.amd.com/en/support/download/drivers.html", Checked);
    private static readonly VendorLink IntelGraphics = new("Intel", "https://www.intel.com/content/www/us/en/support/products/80939/graphics.html", Checked);

    private static readonly OemSupport[] OemList =
    {
        new("Lenovo", new[] { "LENOVO" }, "https://pcsupport.lenovo.com/pl/pl/", Checked),
        new("HP", new[] { "HP", "Hewlett-Packard" }, "https://support.hp.com/pl-pl/drivers", Checked),
        new("Dell", new[] { "Dell Inc.", "Dell Computer Corporation" }, "https://www.dell.com/support/home/pl-pl", Checked),
        new("ASUS", new[] { "ASUSTeK COMPUTER INC." }, "https://www.asus.com/pl/support/", Checked),
        new("Acer", new[] { "Acer" }, "https://www.acer.com/pl-pl/support/drivers-and-manuals", Checked),
        new("MSI", new[] { "Micro-Star International Co., Ltd." }, "https://www.msi.com/support", Checked),
        new("Microsoft Surface", new[] { "Microsoft Corporation" }, "https://support.microsoft.com/pl-pl/surface/drivers-firmware/download-drivers-and-firmware-for-surface", Checked),
    };

    private static readonly HashSet<string> ListedUrls = new(
        new[] { IntelChipset.Url, AmdChipset.Url, NvidiaGraphics.Url, AmdGraphics.Url, IntelGraphics.Url }.Concat(OemList.Select(o => o.Url)),
        StringComparer.Ordinal);

    /// <summary>Czy Ustawienia (<c>ms-settings:</c>) otwierają się z <c>explorer.exe</c> niepodniesionego. Pomiar na VM Home i Pro 2026-10-21: startują niepodniesione.</summary>
    public static bool SettingsOpenable { get; } = true;

    /// <summary>Wszystkie wpisy wsparcia producentów laptopów (do testów zgodności z dokumentacją).</summary>
    public static IReadOnlyList<OemSupport> OemSupports => OemList;

    public static VendorLink? Chipset(CpuVendor vendor) => vendor switch
    {
        CpuVendor.Intel => IntelChipset,
        CpuVendor.Amd => AmdChipset,
        _ => null,
    };

    public static VendorLink? Graphics(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => NvidiaGraphics,
        GpuVendor.Amd => AmdGraphics,
        GpuVendor.Intel => IntelGraphics,
        _ => null,
    };

    /// <summary>Dokładna równość (po <c>Trim</c>, bez rozróżniania wielkości liter) z jedną z nazw producenta z WMI; inaczej null.</summary>
    public static OemSupport? Oem(string? manufacturer)
    {
        var name = manufacturer?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return OemList.FirstOrDefault(o => o.ManufacturerNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Wyszukiwanie Google. Fraza kodowana jako UTF-8 z %XX dla wszystkiego poza <c>A-Za-z0-9._~-</c> (RFC 3986; <c>Uri.EscapeDataString</c>
    /// na .NET Framework zostawia <c>!*'()</c>). Zbyt długa fraza jest przycinana na granicy znaku, żeby wynik przeszedł <see cref="IsAllowed(string?)"/>.
    /// </summary>
    public static string Search(string query)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Fraza wyszukiwania nie może być pusta.", nameof(query));
        }

        var builder = new StringBuilder(SearchPrefix);
        var encodedLength = 0;
        for (var i = 0; i < query.Length; i++)
        {
            var length = char.IsHighSurrogate(query[i]) && i + 1 < query.Length && char.IsLowSurrogate(query[i + 1]) ? 2 : 1;
            var piece = Encode(query.Substring(i, length));
            if (encodedLength + piece.Length > MaxEncodedQueryLength)
            {
                break;
            }

            builder.Append(piece);
            encodedLength += piece.Length;
            i += length - 1;
        }

        return builder.ToString();
    }

    public static bool IsAllowed(string? url) => IsAllowed(url, SettingsOpenable);

    /// <summary>Wariant z jawną flagą, żeby testy obejmowały obie gałęzie (<see cref="SettingsOpenable"/> jest stałą pomiaru).</summary>
    public static bool IsAllowed(string? url, bool settingsOpenable)
    {
        if (url is null || url.Length == 0)
        {
            return false;
        }

        if (ListedUrls.Contains(url))
        {
            return true;
        }

        if (settingsOpenable
            && (string.Equals(url, WindowsUpdate, StringComparison.Ordinal) || string.Equals(url, OptionalUpdates, StringComparison.Ordinal)))
        {
            return true;
        }

        return SearchUrl.IsMatch(url);
    }

    private static string Encode(string text)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            var c = (char)b;
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '~' || c == '-')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(((int)b).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
