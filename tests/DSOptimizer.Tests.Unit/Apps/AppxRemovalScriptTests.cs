using System;
using DSOptimizer.Core.Apps;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

/// <summary>
/// Skrypt usuwania AppX w harnessie: prawdziwe Remove-Appx* są bezpiecznikami, a mocki wypisują wywołania na konsolę
/// (z pominięciem potoku, bo skrypt nie zwraca wyniku). Tryb wybiera żądanie w <c>$Snapshot</c>, nie treść skryptu.
/// </summary>
public class AppxRemovalScriptTests
{
    private const string UserSid = "S-1-5-21-0-0-0-1000";

    private static string ReadMocks(bool allUsers, string installed, string? provisioned, bool imageAllowed)
    {
        var mode = allUsers ? "$true" : "$false";
        var image = provisioned is null
            ? "@()"
            : "[pscustomobject]@{ DisplayName='" + provisioned + "'; PackageName='" + provisioned + "_2.0_neutral_~_fam' }";
        var imageGuard = imageAllowed ? string.Empty : "throw 'Bez deprovisioningu obraz nie jest czytany.'";
        return $$"""
            function Get-AppxPackage {
                param([string]$User, [string]$Name, [switch]$AllUsers)
                if ({{mode}}) {
                    if (-not $AllUsers -or $User) { throw 'Tryb AllUsers: tylko -AllUsers.' }
                }
                elseif ($AllUsers -or $User -ne '{{UserSid}}') { throw "Tryb konta: tylko -User (było: $User, AllUsers=$AllUsers)." }
                @(
                    [pscustomobject]@{ Name='{{installed}}'; PackageFullName='{{installed}}_2.0_x64__fam' },
                    [pscustomobject]@{ Name='{{installed}}.Extra'; PackageFullName='{{installed}}.Extra_2.0_x64__fam' }
                ) | Where-Object { $_.Name -like $Name }
            }
            function Get-AppxProvisionedPackage {
                param([switch]$Online)
                {{imageGuard}}
                if (-not $Online) { throw 'Obraz ma być czytany online.' }
                {{image}}
            }
            """;
    }

    private static string RemoveMocks(string? removeError)
    {
        var fail = removeError is null ? string.Empty : "throw '" + removeError + "'";
        return $$"""
            function Remove-AppxPackage {
                param([string]$Package, [string]$User, [switch]$AllUsers)
                {{fail}}
                [Console]::Out.WriteLine("REMOVE package=$Package user=$User allUsers=$AllUsers")
            }
            function Remove-AppxProvisionedPackage {
                param([switch]$Online, [string]$PackageName)
                if (-not $Online) { throw 'Obraz ma być zmieniany online.' }
                [Console]::Out.WriteLine("DEPROVISION $PackageName")
                [pscustomobject]@{ Online = $true }
            }
            """;
    }

    private static string Mocks(
        bool allUsers,
        string installed = "Microsoft.BingNews",
        string? provisioned = "Microsoft.BingNews",
        string? removeError = null,
        bool imageAllowed = true)
        => ReadMocks(allUsers, installed, provisioned, imageAllowed) + "\r\n" + RemoveMocks(removeError);

    private static ScriptRun Run(string mocks, string name, StoreRemovalScope scope, bool deprovision)
        => PowerShellHarness.Run(AppxRemovalScript.Body, mocks, AppxRemovalScript.Request(name, scope, deprovision));

    [Fact]
    public void CurrentUser_RemovesForLoggedOnUser_ThenDeprovisions()
    {
        var run = Run(Mocks(allUsers: false), "Microsoft.BingNews", StoreRemovalScope.CurrentUser, deprovision: true);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Equal(
            new[]
            {
                "REMOVE package=Microsoft.BingNews_2.0_x64__fam user=" + UserSid + " allUsers=False",
                "DEPROVISION Microsoft.BingNews_2.0_neutral_~_fam",
            },
            run.Output);
    }

    [Fact]
    public void CurrentUser_WithoutDeprovision_NeverTouchesImage()
    {
        var run = Run(Mocks(allUsers: false, imageAllowed: false), "Microsoft.BingNews", StoreRemovalScope.CurrentUser, deprovision: false);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Equal("REMOVE package=Microsoft.BingNews_2.0_x64__fam user=" + UserSid + " allUsers=False", Assert.Single(run.Output));
    }

    [Fact]
    public void CurrentUser_RemovesOnlyExactName()
    {
        var run = Run(Mocks(allUsers: false, installed: "Microsoft.BingNews.Extra"), "Microsoft.BingNews", StoreRemovalScope.CurrentUser, deprovision: false);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void Deprovision_NotInImage_IsSkipped()
    {
        var run = Run(Mocks(allUsers: false, provisioned: null), "Microsoft.BingNews", StoreRemovalScope.CurrentUser, deprovision: true);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.DoesNotContain(run.Output, line => line.StartsWith("DEPROVISION", StringComparison.Ordinal));
    }

    [Fact]
    public void AllUsers_UsesAllUsersSwitch_NeverUser()
    {
        var run = Run(Mocks(allUsers: true, installed: "Vendor.Helper", provisioned: "Vendor.Helper"), "Vendor.Helper", StoreRemovalScope.AllUsers, deprovision: true);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.Equal(
            new[] { "REMOVE package=Vendor.Helper_2.0_x64__fam user= allUsers=True", "DEPROVISION Vendor.Helper_2.0_neutral_~_fam" },
            run.Output);
    }

    [Fact]
    public void RemoveFails_ScriptFailsWithMessage_AndDoesNotDeprovision()
    {
        var run = Run(Mocks(allUsers: false, removeError: "0x80073CFA odmowa"), "Microsoft.BingNews", StoreRemovalScope.CurrentUser, deprovision: true);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("0x80073CFA odmowa", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, line => line.StartsWith("DEPROVISION", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutMocks_HitsHarnessGuard()
    {
        var run = Run(string.Empty, "Microsoft.BingNews", StoreRemovalScope.CurrentUser, deprovision: true);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyReadMocked_RemoveStillHitsGuard()
    {
        var run = Run(ReadMocks(false, "Microsoft.BingNews", "Microsoft.BingNews", true), "Microsoft.BingNews", StoreRemovalScope.CurrentUser, deprovision: true);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu: Remove-AppxPackage", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyPackageRemovalMocked_DeprovisionStillHitsGuard()
    {
        var mocks = ReadMocks(false, "Microsoft.BingNews", "Microsoft.BingNews", true) + "\r\n" + """
            function Remove-AppxPackage { param([string]$Package, [string]$User, [switch]$AllUsers) }
            """;

        var run = Run(mocks, "Microsoft.BingNews", StoreRemovalScope.CurrentUser, deprovision: true);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu: Remove-AppxProvisionedPackage", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyRequest_Fails()
    {
        var run = PowerShellHarness.Run(AppxRemovalScript.Body, Mocks(allUsers: false), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
    }

    [Fact]
    public void Body_NeverUsesHkcu()
        => Assert.DoesNotContain("HKCU:", AppxRemovalScript.Body, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Request_Validates()
    {
        Assert.Throws<ArgumentException>(() => AppxRemovalScript.Request("", StoreRemovalScope.CurrentUser, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => AppxRemovalScript.Request("X", (StoreRemovalScope)7, false));
    }
}
