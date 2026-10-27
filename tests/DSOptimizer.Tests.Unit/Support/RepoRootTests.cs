using System.IO;
using Xunit;

namespace DSOptimizer.Tests.Unit.Support;

public class RepoRootTests
{
    [Fact]
    public void Path_ContainsSolutionAndDocsAndCatalog()
    {
        Assert.True(File.Exists(RepoRoot.Combine("DSOptimizer.sln")));
        Assert.True(Directory.Exists(RepoRoot.Combine("docs", "tweaks")));
        Assert.True(Directory.Exists(RepoRoot.Combine("src", "DSOptimizer.Catalog", "catalog")));
        Assert.True(File.Exists(RepoRoot.Combine("tests", "vm", "catalog", "vm-test-catalog.json")));
    }
}
