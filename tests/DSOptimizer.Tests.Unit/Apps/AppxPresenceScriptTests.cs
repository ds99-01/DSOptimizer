using System;
using DSOptimizer.Core.Apps;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

/// <summary>Skrypt obecności pakietu AppX (weryfikacja usunięcia) w harnessie z mockami; tryb wybiera żądanie w <c>$Snapshot</c>.</summary>
public class AppxPresenceScriptTests
{
    private const string UserSid = "S-1-5-21-0-0-0-1000";

    // Get-AppxPackage: w trybie konta zalogowanego wymaga -User (i rzuca przy -AllUsers), w trybie AllUsers odwrotnie.
    // Get-AppxProvisionedPackage rzuca, gdy imageAllowed = false (tryb User nie może czytać obrazu).
    private static string Mocks(bool allUsers, string installed = "Microsoft.BingNews", string? provisioned = null, bool imageAllowed = true)
    {
        var mode = allUsers ? "$true" : "$false";
        var image = provisioned is null
            ? "@()"
            : "[pscustomobject]@{ DisplayName='" + provisioned + "'; PackageName='" + provisioned + "_1.0_neutral_~_fam' }";
        var imageGuard = imageAllowed ? string.Empty : "throw 'Tryb User nie czyta obrazu.'";
        return $$"""
            function Get-AppxPackage {
                param([string]$User, [string]$Name, [switch]$AllUsers)
                if ({{mode}}) {
                    if (-not $AllUsers) { throw 'Tryb AllUsers ma pytać -AllUsers.' }
                    if ($User) { throw 'Tryb AllUsers nie może używać -User.' }
                }
                else {
                    if ($AllUsers) { throw 'Tryb konta nie może używać -AllUsers.' }
                    if ($User -ne '{{UserSid}}') { throw "Zły -User: $User" }
                }
                @(
                    [pscustomobject]@{ Name='{{installed}}'; PackageFullName='{{installed}}_1.0_x64__fam' },
                    [pscustomobject]@{ Name='{{installed}}.Extra'; PackageFullName='{{installed}}.Extra_1.0_x64__fam' }
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

    private static AppPresence Present(string mocks, string name, AppPresenceScope scope)
    {
        var run = PowerShellHarness.Run(AppxScript.PresenceBody, mocks, AppxScript.PresenceRequest(name, scope));
        Assert.True(run.ExitCode == 0, run.Error);
        return AppxScript.ParsePresence(run.LastLine);
    }

    [Fact]
    public void User_InstalledForLoggedOnUser_IsPresent()
        => Assert.Equal(AppPresence.Present, Present(Mocks(allUsers: false, imageAllowed: false), "Microsoft.BingNews", AppPresenceScope.User));

    [Fact]
    public void User_NotInstalled_IsGone_WithoutReadingImage()
        => Assert.Equal(AppPresence.Absent, Present(Mocks(allUsers: false, imageAllowed: false), "Inny.Pakiet", AppPresenceScope.User));

    [Fact]
    public void User_OnlyLongerNameInAnswer_DoesNotCount()
        => Assert.Equal(AppPresence.Absent, Present(Mocks(allUsers: false, installed: "Microsoft.BingNews.Extra"), "Microsoft.BingNews", AppPresenceScope.User));

    [Fact]
    public void UserAndImage_GoneForUserButStillProvisioned_IsImageOnly()
        => Assert.Equal(AppPresence.ImageOnly, Present(Mocks(allUsers: false, installed: "Inny", provisioned: "Microsoft.BingNews"), "Microsoft.BingNews", AppPresenceScope.UserAndImage));

    [Fact]
    public void UserAndImage_GoneEverywhere_IsGone()
        => Assert.Equal(AppPresence.Absent, Present(Mocks(allUsers: false, installed: "Inny", provisioned: "Inny"), "Microsoft.BingNews", AppPresenceScope.UserAndImage));

    [Fact]
    public void AllUsersAndImage_InstalledForSomeAccount_IsPresent()
        => Assert.Equal(AppPresence.Present, Present(Mocks(allUsers: true), "Microsoft.BingNews", AppPresenceScope.AllUsersAndImage));

    [Fact]
    public void AllUsersAndImage_OnlyInImage_IsImageOnly()
        => Assert.Equal(AppPresence.ImageOnly, Present(Mocks(allUsers: true, installed: "Inny", provisioned: "Vendor.Helper"), "Vendor.Helper", AppPresenceScope.AllUsersAndImage));

    [Fact]
    public void AllUsersAndImage_GoneEverywhere_IsGone()
        => Assert.Equal(AppPresence.Absent, Present(Mocks(allUsers: true, installed: "Inny"), "Vendor.Helper", AppPresenceScope.AllUsersAndImage));

    [Fact]
    public void WithoutMocks_HitsHarnessGuard()
    {
        var run = PowerShellHarness.Run(AppxScript.PresenceBody, string.Empty, AppxScript.PresenceRequest("Microsoft.BingNews", AppPresenceScope.User));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyRequest_Fails_InsteadOfSayingGone()
    {
        var run = PowerShellHarness.Run(AppxScript.PresenceBody, Mocks(allUsers: false), string.Empty);

        Assert.NotEqual(0, run.ExitCode);
    }

    [Theory]
    [InlineData("""{"Installed":true,"Provisioned":false}""", AppPresence.Present)]
    [InlineData("""{"Installed":true,"Provisioned":true}""", AppPresence.Present)]
    [InlineData("""{"Installed":false,"Provisioned":true}""", AppPresence.ImageOnly)]
    [InlineData("""{"Installed":false,"Provisioned":false}""", AppPresence.Absent)]
    public void ParsePresence_ReadsBothFlags(string json, AppPresence expected)
        => Assert.Equal(expected, AppxScript.ParsePresence(json));

    [Theory]
    [InlineData("")]
    [InlineData("to nie json")]
    [InlineData("[]")]
    [InlineData("""{"Installed":false}""")]
    [InlineData("""{"Installed":"tak","Provisioned":false}""")]
    public void ParsePresence_Garbage_Throws(string json)
        => Assert.Throws<FormatException>(() => AppxScript.ParsePresence(json));

    [Fact]
    public void PresenceRequest_UnknownScope_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => AppxScript.PresenceRequest("X", (AppPresenceScope)99));

    [Fact]
    public void PresenceRequest_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => AppxScript.PresenceRequest(" ", AppPresenceScope.User));
}
