using System;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

public class VmTestCatalogTests
{
    private static readonly string CatalogPath = RepoRoot.Combine("tests", "vm", "catalog", "vm-test-catalog.json");

    [Fact]
    public void VmTestCatalog_LoadsAndPassesValidation()
    {
        var catalog = CatalogLoader.Load(new[] { new CatalogSource("vm-test-catalog.json", File.ReadAllText(CatalogPath, Encoding.UTF8)) });
        Assert.Empty(CatalogValidator.Validate(catalog.Definitions));
    }

    [Fact]
    public void VmTestCatalog_CoversRequiredScenarios()
    {
        var ids = CatalogLoader.Load(new[] { new CatalogSource("c", File.ReadAllText(CatalogPath, Encoding.UTF8)) }).Definitions.Select(d => d.Id);
        Assert.Equal(
            new[] { "vmtest.action-marker", "vmtest.driver-start-type", "vmtest.hkcu-value", "vmtest.hklm-value", "vmtest.reboot-hklm", "vmtest.service-delayed-stop", "vmtest.service-dependents-running", "vmtest.service-manual-to-disabled" },
            ids.OrderBy(id => id, StringComparer.Ordinal));
    }
}
