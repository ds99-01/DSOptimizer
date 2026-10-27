using System;
using System.Security.Principal;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class InteractiveUserContextTests
{
    [Fact]
    public void Resolve_ReturnsUserSid_OrCurrentProcessWhenUncertain()
    {
        var context = InteractiveUserContext.Resolve(new NullLog());

        Assert.StartsWith("S-1-5-", context.Sid, StringComparison.Ordinal);
        if (!context.IsCertain)
        {
            Assert.Equal(WindowsIdentity.GetCurrent().User!.Value, context.Sid);
        }
    }
}
