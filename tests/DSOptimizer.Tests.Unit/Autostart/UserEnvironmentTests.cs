using System.Collections.Generic;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class UserEnvironmentTests
{
    private static readonly AutostartLocations Locations = AutostartLocations.ForUser("S-1-5-21-1-2-3-1001");

    private static readonly Dictionary<string, string> Machine = new()
    {
        ["SystemRoot"] = @"C:\Windows",
        ["windir"] = @"C:\Windows",
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramData"] = @"C:\ProgramData",
    };

    [Fact]
    public void VolatileAppData_UsedForAppData()
    {
        var registry = new FakeRegistry();
        registry.Set(Locations.VolatileEnvironmentKey, "APPDATA", RegValue.FromString(@"C:\Users\dso\AppData\Roaming"));

        var environment = UserEnvironment.Load(registry, Locations, Machine);

        Assert.True(environment.TryExpand(@"%APPDATA%\x", out var expanded));
        Assert.Equal(@"C:\Users\dso\AppData\Roaming\x", expanded);
    }

    [Fact]
    public void MachineDictionaryOutsideWhitelist_Ignored()
    {
        var machine = new Dictionary<string, string>(Machine)
        {
            ["APPDATA"] = @"C:\Users\Administrator\AppData\Roaming",
            ["USERPROFILE"] = @"C:\Users\Administrator",
        };

        var environment = UserEnvironment.Load(new FakeRegistry(), Locations, machine);

        Assert.False(environment.TryExpand(@"%APPDATA%\x", out _));
        Assert.Null(environment.Get("USERPROFILE"));
        Assert.True(environment.TryExpand(@"%SystemRoot%\x", out var expanded));
        Assert.Equal(@"C:\Windows\x", expanded);
    }

    [Fact]
    public void EnvironmentKeyExpandedWithVolatile()
    {
        var registry = new FakeRegistry();
        registry.Set(Locations.UserEnvironmentKey, "TEMP", RegValue.FromExpandString(@"%USERPROFILE%\AppData\Local\Temp"));
        registry.Set(Locations.UserEnvironmentKey, "Path", RegValue.FromExpandString(@"%NIEZNANA%\bin"));
        registry.Set(Locations.UserEnvironmentKey, "Plain", RegValue.FromString(@"%USERPROFILE%\zostaje"));
        registry.Set(Locations.VolatileEnvironmentKey, "USERPROFILE", RegValue.FromString(@"C:\Users\dso"));

        var environment = UserEnvironment.Load(registry, Locations, Machine);

        Assert.Equal(@"C:\Users\dso\AppData\Local\Temp", environment.Get("TEMP"));
        Assert.Null(environment.Get("Path"));
        Assert.Equal(@"%USERPROFILE%\zostaje", environment.Get("Plain"));
    }

    [Fact]
    public void LaterSourceWins_MachineThenEnvironmentThenVolatile()
    {
        var registry = new FakeRegistry();
        registry.Set(Locations.UserEnvironmentKey, "ProgramData", RegValue.FromString(@"D:\Env"));
        registry.Set(Locations.UserEnvironmentKey, "LOCALAPPDATA", RegValue.FromString(@"D:\Env\Local"));
        registry.Set(Locations.VolatileEnvironmentKey, "LOCALAPPDATA", RegValue.FromString(@"C:\Users\dso\AppData\Local"));

        var environment = UserEnvironment.Load(registry, Locations, Machine);

        Assert.Equal(@"D:\Env", environment.Get("ProgramData"));
        Assert.Equal(@"C:\Users\dso\AppData\Local", environment.Get("LOCALAPPDATA"));
    }

    [Fact]
    public void UnknownVariable_False()
    {
        var environment = UserEnvironment.FromVariables(Machine);

        Assert.False(environment.TryExpand(@"%NOPE%\app.exe", out var expanded));
        Assert.Equal(@"%NOPE%\app.exe", expanded);
        Assert.False(environment.TryExpand(@"%SystemRoot%\%NOPE%\x", out expanded));
        Assert.Equal(@"C:\Windows\%NOPE%\x", expanded);
    }

    [Fact]
    public void TextWithoutVariablesOrUnclosedPercent_Unchanged()
    {
        var environment = UserEnvironment.FromVariables(Machine);

        Assert.True(environment.TryExpand(@"C:\a\b.exe", out var expanded));
        Assert.Equal(@"C:\a\b.exe", expanded);
        Assert.True(environment.TryExpand(@"C:\100%\b.exe", out expanded));
        Assert.Equal(@"C:\100%\b.exe", expanded);
    }

    [Fact]
    public void CaseInsensitiveNames()
    {
        var registry = new FakeRegistry();
        registry.Set(Locations.VolatileEnvironmentKey, "AppData", RegValue.FromString(@"C:\Users\dso\AppData\Roaming"));

        var environment = UserEnvironment.Load(registry, Locations, Machine);

        Assert.True(environment.TryExpand(@"%APPDATA%\a;%WINDIR%\b", out var expanded));
        Assert.Equal(@"C:\Users\dso\AppData\Roaming\a;C:\Windows\b", expanded);
        Assert.Equal(@"C:\Users\dso\AppData\Roaming", environment.Get("appdata"));
    }

    [Fact]
    public void NoUserKeys_MachineOnly()
    {
        var warnings = new List<string>();

        var environment = UserEnvironment.Load(new FakeRegistry(), Locations, Machine, warnings);

        Assert.Empty(warnings);
        Assert.Equal(@"C:\Program Files", environment.Get("ProgramFiles"));
        Assert.Null(environment.Get("APPDATA"));
    }

    [Fact]
    public void UserKeyDenied_Warning_NoThrow()
    {
        var registry = new FakeRegistry();
        registry.CreateKey(Locations.UserEnvironmentKey);
        registry.DenyKey(Locations.UserEnvironmentKey);
        registry.Set(Locations.VolatileEnvironmentKey, "APPDATA", RegValue.FromString(@"C:\Users\dso\AppData\Roaming"));
        registry.Set(Locations.VolatileEnvironmentKey, "HOMEPATH", RegValue.FromString(@"\Users\dso"));
        registry.DenyRead(Locations.VolatileEnvironmentKey, "HOMEPATH");
        var warnings = new List<string>();

        var environment = UserEnvironment.Load(registry, Locations, Machine, warnings);

        Assert.Equal(2, warnings.Count);
        Assert.Contains(Locations.UserEnvironmentKey, warnings[0], System.StringComparison.Ordinal);
        Assert.StartsWith("Nie udało się odczytać zmiennych konta z ", warnings[0], System.StringComparison.Ordinal);
        Assert.Contains(Locations.VolatileEnvironmentKey, warnings[1], System.StringComparison.Ordinal);
        Assert.Equal(@"C:\Users\dso\AppData\Roaming", environment.Get("APPDATA"));
        Assert.Null(environment.Get("HOMEPATH"));
    }

    [Fact]
    public void NonStringValues_Ignored()
    {
        var registry = new FakeRegistry();
        registry.Set(Locations.VolatileEnvironmentKey, "Liczba", RegValue.FromDWord(1));

        var environment = UserEnvironment.Load(registry, Locations, Machine);

        Assert.Null(environment.Get("Liczba"));
    }

    [Fact]
    public void MachineVariableNames_FromPlanDecision()
        => Assert.Equal(
            new[]
            {
                "SystemRoot", "windir", "SystemDrive", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData",
                "ALLUSERSPROFILE", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432", "PUBLIC",
            },
            UserEnvironment.MachineVariableNames);

    [Fact]
    public void English_WarningTranslated()
    {
        using var _ = DSOptimizer.Core.Localization.L.Use(DSOptimizer.Core.Localization.UiLanguage.English);
        Assert.Equal("Couldn't read the account variables from K: E", AutostartText.EnvironmentUnreadable("K", "E"));
    }
}
