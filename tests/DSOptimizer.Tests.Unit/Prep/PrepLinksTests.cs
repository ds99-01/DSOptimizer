using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Core.Prep;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Prep;

public class PrepLinksTests
{
    private const string SearchPrefix = "https://www.google.com/search?q=";

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_A0E8")]
    [InlineData("Lenovo \"X1\" Gen 9")]
    [InlineData("Łącze")]
    [InlineData("a b'c(d)e!f*g\\h&i=j#k+l")]
    public void Search_EscapesAndResultIsAllowed(string query)
    {
        var url = PrepLinks.Search(query);

        Assert.StartsWith(SearchPrefix, url, StringComparison.Ordinal);
        Assert.True(PrepLinks.IsAllowed(url, settingsOpenable: false), url);
        var encoded = url.Substring(SearchPrefix.Length);
        foreach (var forbidden in new[] { " ", "\"", "\\", "&", "'", "(", ")", "!", "*", "#", "+", "=" })
        {
            Assert.DoesNotContain(forbidden, encoded, StringComparison.Ordinal);
        }

        Assert.Equal(query, Uri.UnescapeDataString(encoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" 	 ")]
    public void Search_BlankQuery_Throws(string query)
        => Assert.Throws<ArgumentException>(() => PrepLinks.Search(query));

    [Fact]
    public void Search_NullQuery_Throws()
        => Assert.Throws<ArgumentNullException>(() => PrepLinks.Search(null!));

    [Fact]
    public void Search_VeryLongQuery_IsTruncatedToAllowedUrl()
        => Assert.True(PrepLinks.IsAllowed(PrepLinks.Search(new string('Ł', 500)), false));

    [Theory]
    [InlineData("LENOVO", "Lenovo")]
    [InlineData("lenovo ", "Lenovo")]
    [InlineData("HP", "HP")]
    [InlineData("Hewlett-Packard", "HP")]
    [InlineData("Dell Inc.", "Dell")]
    [InlineData("Dell Computer Corporation", "Dell")]
    [InlineData("ASUSTeK COMPUTER INC.", "ASUS")]
    [InlineData("ASUSTeK Computer Inc.", "ASUS")]
    [InlineData("Acer", "Acer")]
    [InlineData("Micro-Star International Co., Ltd.", "MSI")]
    [InlineData("Microsoft Corporation", "Microsoft Surface")]
    public void Oem_MatchesManufacturerVariants(string manufacturer, string vendor)
        => Assert.Equal(vendor, PrepLinks.Oem(manufacturer)?.Vendor);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ASUS")]
    [InlineData("Dell")]
    [InlineData("Lenovo Group")]
    public void Oem_UnknownOrPartial_ReturnsNull(string? manufacturer)
        => Assert.Null(PrepLinks.Oem(manufacturer));

    [Fact]
    public void Chipset_And_Graphics_ByVendor()
    {
        Assert.Null(PrepLinks.Chipset(CpuVendor.Unknown));
        Assert.Equal("Intel", PrepLinks.Chipset(CpuVendor.Intel)?.Vendor);
        Assert.Equal("AMD", PrepLinks.Chipset(CpuVendor.Amd)?.Vendor);
        Assert.Null(PrepLinks.Graphics(GpuVendor.Unknown));
        Assert.Equal("NVIDIA", PrepLinks.Graphics(GpuVendor.Nvidia)?.Vendor);
        Assert.Equal("AMD", PrepLinks.Graphics(GpuVendor.Amd)?.Vendor);
        Assert.Equal("Intel", PrepLinks.Graphics(GpuVendor.Intel)?.Vendor);
    }

    [Fact]
    public void IsAllowed_SettingsBranches()
    {
        Assert.True(PrepLinks.IsAllowed(PrepLinks.WindowsUpdate, true));
        Assert.True(PrepLinks.IsAllowed(PrepLinks.OptionalUpdates, true));
        Assert.False(PrepLinks.IsAllowed(PrepLinks.WindowsUpdate, false));
        Assert.False(PrepLinks.IsAllowed(PrepLinks.OptionalUpdates, false));
        Assert.False(PrepLinks.IsAllowed("ms-settings:display", true));
        Assert.False(PrepLinks.IsAllowed("ms-settings:windowsupdate ", true));
        Assert.Equal(PrepLinks.IsAllowed(PrepLinks.WindowsUpdate, PrepLinks.SettingsOpenable), PrepLinks.IsAllowed(PrepLinks.WindowsUpdate));
    }

    /// <summary>
    /// Polski README i jego angielska wersja mają te same tabele; różnią się tylko słowa kolumny celu i nagłówek tabeli nazw.
    /// Klucz porównania zostaje z polskimi słowami celu (angielskie są mapowane na polskie).
    /// </summary>
    [Theory]
    [InlineData("docs/prep/README.md", "chipset", "grafika", "wsparcie", "wsparcie-numer", "Producent")]
    [InlineData("docs/en/prep/README.md", "chipset", "graphics", "support", "support-number", "Manufacturer")]
    public void LinksMatchDocsPrepReadme(string path, string chipset, string graphics, string support, string supportNumber, string headerWord)
    {
        var lines = File.ReadAllLines(RepoRoot.Combine(path.Split('/')), Encoding.UTF8);
        var purposes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [chipset] = "chipset",
            [graphics] = "grafika",
            [support] = "wsparcie",
            [supportNumber] = "wsparcie-numer",
        };

        var readmeLinks = new HashSet<string>(StringComparer.Ordinal);
        var readmeNames = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var cells = Cells(line);
            if (cells is null)
            {
                continue;
            }

            if (cells.Length == 5 && purposes.TryGetValue(cells[1], out var purpose))
            {
                // Brak obsługi szablonu z numerem: kto doda taki wiersz, musi świadomie dodać też kod.
                Assert.NotEqual("wsparcie-numer", purpose);
                readmeLinks.Add(Key(cells[0], purpose, cells[2], DateTime.ParseExact(cells[4], "yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }
            else if (cells.Length == 2 && cells[0] != headerWord && !cells[0].StartsWith("---", StringComparison.Ordinal))
            {
                readmeNames[cells[0]] = cells[1].Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        var codeLinks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cpu in new[] { CpuVendor.Intel, CpuVendor.Amd })
        {
            var l = PrepLinks.Chipset(cpu)!;
            codeLinks.Add(Key(l.Vendor, "chipset", l.Url, l.CheckedOn));
        }

        foreach (var gpu in new[] { GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel })
        {
            var l = PrepLinks.Graphics(gpu)!;
            codeLinks.Add(Key(l.Vendor, "grafika", l.Url, l.CheckedOn));
        }

        foreach (var oem in PrepLinks.OemSupports)
        {
            codeLinks.Add(Key(oem.Vendor, "wsparcie", oem.Url, oem.CheckedOn));
        }

        Assert.NotEmpty(readmeLinks);
        Assert.Equal(readmeLinks.OrderBy(x => x, StringComparer.Ordinal), codeLinks.OrderBy(x => x, StringComparer.Ordinal));

        Assert.Equal(readmeNames.Keys.OrderBy(x => x, StringComparer.Ordinal), PrepLinks.OemSupports.Select(o => o.Vendor).OrderBy(x => x, StringComparer.Ordinal));
        foreach (var oem in PrepLinks.OemSupports)
        {
            Assert.Equal(readmeNames[oem.Vendor], oem.ManufacturerNames);
        }
    }

    private static string Key(string vendor, string purpose, string url, DateTime date)
        => string.Join(" | ", vendor, purpose, url, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static string[]? Cells(string line)
        => line.StartsWith("|", StringComparison.Ordinal)
            ? line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray()
            : null;
}
