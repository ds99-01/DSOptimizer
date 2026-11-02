using System;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Windows.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Windows.Autostart;

[Trait("Category", "Windows")]
public class MachineEnvironmentTests
{
    // Zmienne konta (APPDATA, TEMP, USERPROFILE …) procesu nigdy nie trafiają do słownika – proces bywa innym kontem niż zalogowane.
    [Fact]
    public void FromProcess_OnlyWhitelistedNames()
    {
        var machine = MachineEnvironment.FromProcess();

        Assert.NotEmpty(machine);
        foreach (var name in machine.Keys)
        {
            Assert.Contains(name, UserEnvironment.MachineVariableNames);
        }

        Assert.Equal(Environment.GetEnvironmentVariable("SystemRoot"), machine["SystemRoot"]);
        Assert.Equal(Environment.GetEnvironmentVariable("ProgramData"), machine["programdata"]);
        Assert.False(machine.ContainsKey("APPDATA"));
        Assert.False(machine.ContainsKey("TEMP"));
        Assert.False(machine.ContainsKey("USERPROFILE"));
        Assert.False(machine.ContainsKey("LOCALAPPDATA"));
    }
}
