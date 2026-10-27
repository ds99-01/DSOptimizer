using System.Linq;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Prep;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class ExternalLinkPolicyTests
{
    [Theory]
    [InlineData("https://github.com/ds99-01", true)]
    [InlineData("https://github.com/ds99-01/", false)]
    [InlineData("HTTPS://github.com/ds99-01", false)]
    [InlineData("https://github.com/ds99-01 ", false)]
    [InlineData("http://github.com/ds99-01", false)]
    [InlineData("https://github.com/ds99-01\" /x", false)]
    [InlineData("file:///C:/", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("https://www.amd.com/en/support/download/drivers.html", true)]
    [InlineData("https://www.nvidia.com/pl-pl/drivers/", true)]
    [InlineData("https://pcsupport.lenovo.com/pl/pl/", true)]
    [InlineData("http://www.amd.com/en/support/download/drivers.html", false)]
    [InlineData("https://www.amd.com/en/support/download/drivers.html/x", false)]
    [InlineData("https://pcsupport.lenovo.com/pl/pl/ ", false)]
    [InlineData("ms-settings:display", false)]
    [InlineData("https://www.google.com/search?q=PCI%5CVEN_8086%26DEV_A0E8", true)]
    [InlineData("https://www.google.com/search?q=Lenovo \"X1\"", false)]
    [InlineData("https://www.google.com/search?q=Lenovo X1", false)]
    [InlineData("https://www.google.com/search?q=a\"b", false)]
    [InlineData("https://www.google.com/search?q=", false)]
    [InlineData("https://www.google.com/search?q=abc&x=1", false)]
    [InlineData("https://www.google.com/search?q=abc\n", false)]
    [InlineData("https://www.google.com/search?q=%windir%", false)]
    [InlineData("https://www.google.com/search?q=%G1", false)]
    [InlineData("https://www.google.com/search?q=%2", false)]
    [InlineData("https://www.google.com/search?q=%2f", false)]
    [InlineData("https://www.google.com/search?q=%%", false)]
    [InlineData("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1", true)]
    [InlineData("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS2", false)]
    [InlineData("ms-windows-store://pdp/?ProductId=9nblggh4nns1", false)]
    [InlineData("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1 ", false)]
    [InlineData("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1&x=1", false)]
    [InlineData("ms-windows-store://pdp/", false)]
    [InlineData("ms-windows-store://navigatetopage/?Id=9NBLGGH4NNS1", false)]
    [InlineData("https://apps.microsoft.com/detail/9NBLGGH4NNS1", false)]
    [InlineData("http://pdp/?ProductId=9NBLGGH4NNS1", false)]
    public void IsAllowed_Table(string? url, bool expected)
        => Assert.Equal(expected, ExternalLinkPolicy.IsAllowed(url));

    [Theory]
    [InlineData("ms-settings:windowsupdate")]
    [InlineData("ms-settings:windowsupdate-optionalupdates")]
    public void IsAllowed_SettingsUri_FollowsSettingsOpenable(string uri)
        => Assert.Equal(PrepLinks.SettingsOpenable, ExternalLinkPolicy.IsAllowed(uri));

    [Fact]
    public void IsAllowed_EveryListedUrl_IsAllowed()
    {
        var urls = new System.Collections.Generic.List<string>();
        foreach (var cpu in System.Enum.GetValues(typeof(CpuVendor)).Cast<CpuVendor>())
        {
            var link = PrepLinks.Chipset(cpu);
            if (link is not null)
            {
                urls.Add(link.Url);
            }
        }

        foreach (var gpu in System.Enum.GetValues(typeof(GpuVendor)).Cast<GpuVendor>())
        {
            var link = PrepLinks.Graphics(gpu);
            if (link is not null)
            {
                urls.Add(link.Url);
            }
        }

        urls.AddRange(PrepLinks.OemSupports.Select(o => o.Url));

        Assert.Equal(12, urls.Count);
        foreach (var url in urls)
        {
            Assert.True(ExternalLinkPolicy.IsAllowed(url), url);
        }
    }
}
