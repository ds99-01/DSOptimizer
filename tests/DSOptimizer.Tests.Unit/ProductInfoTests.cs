using DSOptimizer.Core;
using Xunit;

namespace DSOptimizer.Tests.Unit;

public class ProductInfoTests
{
    [Fact]
    public void Version_ComesFromAssembly_AndIsMajor4()
        => Assert.StartsWith("4.", ProductInfo.Version);

    [Fact]
    public void Author_Constants()
    {
        Assert.Equal("Dawid Świeboda", ProductInfo.AuthorName);
        Assert.Equal("https://github.com/ds99-01/DSOptimizer", ProductInfo.RepositoryUrl);
        Assert.Equal("github.com/ds99-01/DSOptimizer", ProductInfo.RepositoryUrlText);
    }

    [Fact]
    public void License_Constants()
    {
        Assert.Equal("Mozilla Public License 2.0", ProductInfo.LicenseName);
        Assert.Equal(
            "Nazwa DSOptimizer i znak DŚO nie są objęte licencją MPL 2.0; przerobione wersje muszą nosić inną nazwę.",
            ProductInfo.TrademarkNotice);
    }
}
