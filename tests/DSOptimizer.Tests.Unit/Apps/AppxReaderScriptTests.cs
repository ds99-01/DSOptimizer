using System;
using System.IO;
using System.Linq;
using DSOptimizer.Core.Apps;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Apps;

/// <summary>Skrypt odczytu AppX uruchomiony w harnessie z mockami <c>Get-AppxPackage</c> i <c>Get-AppxProvisionedPackage</c>.</summary>
public class AppxReaderScriptTests
{
    private const string UserSid = "S-1-5-21-0-0-0-1000";

    // Mock sprawdza -User (skrypt ma pytać o konto zalogowane, nigdy -AllUsers) i zwraca obiekty jak prawdziwy cmdlet (SignatureKind jako enum).
    private static string PackageMocks(string installLocation = "") => $$"""
        function Get-AppxPackage {
            param([string]$User, [string]$Name, [switch]$AllUsers)
            if ($AllUsers) { throw 'Skrypt nie może używać -AllUsers.' }
            if ($User -ne '{{UserSid}}') { throw "Zły -User: $User" }
            Add-Type -TypeDefinition 'public enum DsoSig { None, Developer, Store, System }' -ErrorAction SilentlyContinue
            $all = @(
                [pscustomobject]@{ Name='Microsoft.BingNews'; PackageFullName='Microsoft.BingNews_4.55.0.0_x64__8wekyb3d8bbwe'; PackageFamilyName='Microsoft.BingNews_8wekyb3d8bbwe'; Publisher='CN=Microsoft Corporation'; Version=[version]'4.55.0.0'; IsFramework=$false; IsResourcePackage=$false; NonRemovable=$false; SignatureKind=[DsoSig]::Store; InstallLocation='{{installLocation}}' },
                [pscustomobject]@{ Name='Microsoft.VCLibs.140.00'; PackageFullName='Microsoft.VCLibs.140.00_14.0.0.0_x64__8wekyb3d8bbwe'; PackageFamilyName='Microsoft.VCLibs.140.00_8wekyb3d8bbwe'; Publisher='CN=Microsoft Corporation'; Version=[version]'14.0.0.0'; IsFramework=$true; IsResourcePackage=$false; NonRemovable=$false; SignatureKind=[DsoSig]::Store; InstallLocation='' }
            )
            if ($Name) { $all | Where-Object { $_.Name -like $Name } } else { $all }
        }
        function Get-AppxProvisionedPackage {
            param([switch]$Online)
            if (-not $Online) { throw 'Skrypt ma czytać obraz online.' }
            [pscustomobject]@{ DisplayName='Microsoft.BingNews'; PackageName='Microsoft.BingNews_4.55.0.0_neutral_~_8wekyb3d8bbwe' }
        }
        """;

    private static string Run(string mocks, string filter = "")
    {
        var run = PowerShellHarness.Run(AppxScript.Body, mocks, filter);
        Assert.True(run.ExitCode == 0, run.Error);
        return run.LastLine;
    }

    [Fact]
    public void Script_ReturnsJson_ThatParsesToModel()
    {
        var apps = AppxPackages.Parse(Run(PackageMocks()), UserSid, (_, raw) => raw);

        Assert.Equal(2, apps.Count);
        var news = apps.Single(a => a.Appx!.PackageName == "Microsoft.BingNews");
        Assert.Equal("appx:Microsoft.BingNews_8wekyb3d8bbwe", news.Id);
        Assert.Equal(AppKind.Store, news.Kind);
        Assert.Equal(AppScope.User, news.Scope);
        Assert.Equal(UserSid, news.OwnerSid);
        Assert.Equal("4.55.0.0", news.Version);
        Assert.Equal("Store", news.Appx!.SignatureKind);
        Assert.True(news.Appx.IsProvisioned);
        Assert.False(news.Appx.IsFramework);
        var libs = apps.Single(a => a.Appx!.PackageName == "Microsoft.VCLibs.140.00");
        Assert.True(libs.Appx!.IsFramework);
        Assert.False(libs.Appx.IsProvisioned);
    }

    [Fact]
    public void Script_IgnoresSnapshot_AlwaysReadsWholeList()
    {
        var apps = AppxPackages.Parse(Run(PackageMocks(), "Microsoft.BingNews"), UserSid, (_, raw) => raw);

        Assert.Equal(2, apps.Count);
    }

    [Fact]
    public void Script_ReadsRawDisplayNameFromManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dso-tests", "appx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "AppxManifest.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Properties><DisplayName>ms-resource:AppName</DisplayName></Properties>
                </Package>
                """);
            var apps = AppxPackages.Parse(Run(PackageMocks(directory)), UserSid, (_, raw) => raw);

            Assert.Equal("ms-resource:AppName", apps.Single(a => a.Appx!.PackageName == "Microsoft.BingNews").Name);
            Assert.Equal("Microsoft.VCLibs.140.00", apps.Single(a => a.Appx!.PackageName == "Microsoft.VCLibs.140.00").Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Script_ReadsRawPublisherDisplayNameFromManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dso-tests", "appx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "AppxManifest.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Properties><DisplayName>Clipchamp</DisplayName><PublisherDisplayName>ms-resource:PublisherName</PublisherDisplayName></Properties>
                </Package>
                """);
            string? seen = null;
            var apps = AppxPackages.Parse(Run(PackageMocks(directory)), UserSid, (_, raw) => raw, (record, raw) =>
            {
                if (record.Name == "Microsoft.BingNews")
                {
                    seen = raw;
                }

                return raw;
            });

            Assert.Equal("ms-resource:PublisherName", seen);
            Assert.Equal("Clipchamp", apps.Single(a => a.Appx!.PackageName == "Microsoft.BingNews").Name);
            Assert.Equal("CN=Microsoft Corporation", apps.Single(a => a.Appx!.PackageName == "Microsoft.VCLibs.140.00").Publisher);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Script_MissingManifest_GivesNullRawDisplayName()
    {
        string? seen = "nie-ustawiono";
        AppxPackages.Parse(Run(PackageMocks(@"C:\dso-nie-ma-takiego-katalogu")), UserSid, (record, raw) =>
        {
            if (record.Name == "Microsoft.BingNews")
            {
                seen = raw;
            }

            return raw;
        });

        Assert.Null(seen);
    }

    // Odczyt listy dotyczy zawsze konta zalogowanego: -AllUsers wolno tylko w trybie AllUsers skryptów obecności i usuwania
    // (AppxPresenceScriptTests, AppxRemovalScriptTests pilnują tego mockami, które rzucają przy złym przełączniku).
    [Fact]
    public void Script_AsksForLoggedOnUser_AndNeverUsesHkcu()
    {
        Assert.Contains("Get-AppxPackage -User $UserSid", AppxScript.Body, StringComparison.Ordinal);
        Assert.Contains("Get-AppxProvisionedPackage -Online", AppxScript.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("HKCU:", AppxScript.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-AllUsers", AppxScript.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_WithoutMocks_HitsHarnessGuard()
    {
        var run = PowerShellHarness.Run(AppxScript.Body, string.Empty, string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_OnlyGetAppxPackageMocked_StillHitsProvisionedGuard()
    {
        var run = PowerShellHarness.Run(
            AppxScript.Body,
            "function Get-AppxPackage { param($User, $Name) @() }",
            string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Get-AppxProvisionedPackage", run.Error, StringComparison.Ordinal);
    }
}
