using System.Linq;
using DSOptimizer.Core.Model;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Tylko odczyt WMI (Win32_Process): nic nie jest uruchamiane ani zatrzymywane.</summary>
[Trait("Category", "Windows")]
public class WindowsExplorerProcessQueryTests
{
    [Fact]
    public void GetExplorerProcesses_ReturnsOnlyEntriesWithOwnerSidAndSession()
    {
        var explorers = new WindowsExplorerProcessQuery().GetExplorerProcesses();

        Assert.NotEmpty(explorers);
        Assert.All(explorers, explorer =>
        {
            Assert.StartsWith("S-1-", explorer.OwnerSid, System.StringComparison.Ordinal);
            Assert.True(explorer.SessionId >= 0);
        });
    }

    [Fact]
    public void GetExplorerProcesses_ContainsShellOfCurrentAccountInCurrentSession()
    {
        // Testy Windows wymagają interaktywnej sesji administratora z Eksploratorem (tylko odczyt WMI).
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        using var process = System.Diagnostics.Process.GetCurrentProcess();

        var mine = new WindowsExplorerProcessQuery().GetExplorerProcesses()
            .Where(e => e.SessionId == process.SessionId && string.Equals(e.OwnerSid, sid, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(mine);
        Assert.Contains(mine, e => !string.IsNullOrEmpty(e.CommandLine));
        Assert.True(ShellSessionGuard.HasOwnExplorer(new WindowsExplorerProcessQuery().GetExplorerProcesses(), process.SessionId, sid));
    }
}
