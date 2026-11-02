using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

/// <summary>
/// Skrypt odczytu zadań startowych aplikacji ze Sklepu uruchomiony w harnessie z atrapą <c>Get-AppxPackage</c>; manifesty pakietów
/// w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>.
/// </summary>
public sealed class StartupTaskScriptTests : IDisposable
{
    private const string UserSid = "S-1-5-21-0-0-0-1000";

    // Węzeł rozszerzenia skopiowany z odczytu pakietu Windows Terminal na VM 26H2 (Pro), w aplikacji z manifestu pakietu.
    private const string TerminalManifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Properties><DisplayName>ms-resource:AppName</DisplayName><PublisherDisplayName>Microsoft Corporation</PublisherDisplayName></Properties>
          <Applications>
            <Application Id="App" Executable="WindowsTerminal.exe" EntryPoint="Windows.FullTrustApplication">
              <Extensions>
                <uap5:Extension Category="windows.startupTask" xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"><uap5:StartupTask TaskId="StartTerminalOnLoginTask" Enabled="false" DisplayName="ms-resource:AppName" /></uap5:Extension>
              </Extensions>
            </Application>
          </Applications>
        </Package>
        """;

    private const string TeamsManifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5">
          <Properties><DisplayName>Microsoft Teams</DisplayName><PublisherDisplayName>Microsoft Corporation</PublisherDisplayName></Properties>
          <Applications>
            <Application Id="MSTeams" Executable="ms-teams.exe" EntryPoint="Windows.FullTrustApplication">
              <Extensions>
                <uap5:Extension Category="windows.startupTask" Executable="msteams_autostarter.exe" EntryPoint="Windows.FullTrustApplication">
                  <uap5:StartupTask TaskId="TeamsTfwStartupTask" Enabled="true" DisplayName="Microsoft Teams" />
                </uap5:Extension>
              </Extensions>
            </Application>
          </Applications>
        </Package>
        """;

    private const string NoTaskManifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Properties><DisplayName>Kalkulator</DisplayName></Properties>
          <Applications><Application Id="App" Executable="CalculatorApp.exe" /></Applications>
        </Package>
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "dso-tests", Guid.NewGuid().ToString("N"));

    public StartupTaskScriptTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Sprzątanie best-effort.
        }
    }

    private string Package(string name, string? manifest)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        if (manifest is not null)
        {
            File.WriteAllText(Path.Combine(directory, "AppxManifest.xml"), manifest, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        return directory;
    }

    // Atrapa sprawdza -User (konto zalogowane, nigdy -AllUsers) i zwraca obiekty jak prawdziwy cmdlet.
    private static string Mocks(params (string Name, string Family, string Location)[] packages)
    {
        var items = string.Join(
            ",\r\n",
            packages.Select(p =>
                $"[pscustomobject]@{{ Name='{p.Name}'; PackageFullName='{p.Name}_1.0.0.0_x64__8wekyb3d8bbwe'; PackageFamilyName='{p.Family}'; InstallLocation='{p.Location}' }}"));
        return $$"""
            function Get-AppxPackage {
                param([string]$User, [string]$Name, [switch]$AllUsers)
                if ($AllUsers) { throw 'Skrypt nie może używać -AllUsers.' }
                if ($User -ne '{{UserSid}}') { throw "Zły -User: $User" }
                @(
                {{items}}
                )
            }
            """;
    }

    private static IReadOnlyList<StartupTaskManifest> RunScript(string mocks, ICollection<SkippedStartupPackage>? skipped = null)
    {
        var run = PowerShellHarness.Run(StartupTaskScript.Body, mocks, string.Empty);
        Assert.True(run.ExitCode == 0, run.Error);
        return StartupTaskScript.Parse(run.LastLine, skipped);
    }

    [Fact]
    public void Script_TerminalManifest_EmitsTask()
    {
        var location = Package("terminal", TerminalManifest);

        var task = Assert.Single(RunScript(Mocks(("Microsoft.WindowsTerminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", location))));

        Assert.Equal("Microsoft.WindowsTerminal", task.PackageName);
        Assert.Equal("Microsoft.WindowsTerminal_1.0.0.0_x64__8wekyb3d8bbwe", task.PackageFullName);
        Assert.Equal("Microsoft.WindowsTerminal_8wekyb3d8bbwe", task.PackageFamilyName);
        Assert.Equal(location, task.InstallLocation);
        Assert.Equal("StartTerminalOnLoginTask", task.TaskId);
        Assert.False(task.EnabledInManifest);
        Assert.Equal("ms-resource:AppName", task.DisplayName);
        Assert.Equal("Microsoft Corporation", task.PublisherDisplayName);
        Assert.Equal("WindowsTerminal.exe", task.Executable);
    }

    [Fact]
    public void Script_ExtensionExecutable_Wins()
    {
        var location = Package("teams", TeamsManifest);

        var task = Assert.Single(RunScript(Mocks(("MSTeams", "MSTeams_8wekyb3d8bbwe", location))));

        Assert.Equal("msteams_autostarter.exe", task.Executable);
        Assert.Equal("TeamsTfwStartupTask", task.TaskId);
        Assert.True(task.EnabledInManifest);
        Assert.Equal("Microsoft Teams", task.DisplayName);
    }

    [Fact]
    public void Script_NoInstallLocation_Skipped()
    {
        var location = Package("teams", TeamsManifest);

        var tasks = RunScript(Mocks(("Bez.Lokalizacji", "Bez.Lokalizacji_8wekyb3d8bbwe", string.Empty), ("MSTeams", "MSTeams_8wekyb3d8bbwe", location)));

        Assert.Equal(new[] { "MSTeams" }, tasks.Select(t => t.PackageName));
    }

    [Fact]
    public void Script_ManifestWithoutStartupTask_Skipped()
    {
        var calculator = Package("calc", NoTaskManifest);
        var noManifest = Package("pusty", null);
        var skipped = new List<SkippedStartupPackage>();

        var tasks = RunScript(
            Mocks(("Microsoft.WindowsCalculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe", calculator), ("Pusty", "Pusty_8wekyb3d8bbwe", noManifest)),
            skipped);

        Assert.Empty(tasks);
        Assert.Empty(skipped);
    }

    [Fact]
    public void Script_BrokenManifest_SkippedWithReason_OthersRead()
    {
        var broken = Package("zepsuty", "<Package><Extension Category=\"windows.startupTask\">");
        var teams = Package("teams", TeamsManifest);
        var skipped = new List<SkippedStartupPackage>();

        var tasks = RunScript(Mocks(("Zepsuty", "Zepsuty_8wekyb3d8bbwe", broken), ("MSTeams", "MSTeams_8wekyb3d8bbwe", teams)), skipped);

        Assert.Equal(new[] { "MSTeams" }, tasks.Select(t => t.PackageName));
        var skip = Assert.Single(skipped);
        Assert.Equal("Zepsuty_1.0.0.0_x64__8wekyb3d8bbwe", skip.PackageFullName);
        Assert.False(string.IsNullOrWhiteSpace(skip.Reason));
    }

    [Fact]
    public void Script_ManifestWithDtd_SkippedWithoutExpanding()
    {
        var dtd = Package(
            "dtd",
            "<?xml version=\"1.0\"?><!DOCTYPE Package [<!ENTITY a \"windows.startupTask\">]><Package><Applications><Application Id=\"A\" Executable=\"a.exe\"><Extensions><Extension Category=\"&a;\"><StartupTask TaskId=\"T\" Enabled=\"true\" /></Extension></Extensions></Application></Applications></Package>");
        var skipped = new List<SkippedStartupPackage>();

        var tasks = RunScript(Mocks(("Dtd", "Dtd_8wekyb3d8bbwe", dtd)), skipped);

        Assert.Empty(tasks);
        Assert.Single(skipped);
    }

    [Fact]
    public void Script_AsksForLoggedOnUser_ReadsSharedAndNeverUsesHkcu()
    {
        Assert.Contains("Get-AppxPackage -User $UserSid", StartupTaskScript.Body, StringComparison.Ordinal);
        Assert.Contains("ReadWrite, Delete", StartupTaskScript.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("HKCU:", StartupTaskScript.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-AllUsers", StartupTaskScript.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_WithoutMocks_HitsHarnessGuard()
    {
        var run = PowerShellHarness.Run(StartupTaskScript.Body, string.Empty, string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Json_AllFields()
    {
        const string json = """
            [{"PackageName":"Microsoft.WindowsTerminal","PackageFullName":"Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe","PackageFamilyName":"Microsoft.WindowsTerminal_8wekyb3d8bbwe","InstallLocation":"C:\\Program Files\\WindowsApps\\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe","TaskId":"StartTerminalOnLoginTask","Enabled":"false","DisplayName":"ms-resource:AppName","PublisherDisplayName":"Microsoft Corporation","Executable":"WindowsTerminal.exe"},
             {"PackageName":"MSTeams","PackageFullName":"MSTeams_25.1.0.0_x64__8wekyb3d8bbwe","PackageFamilyName":"MSTeams_8wekyb3d8bbwe","InstallLocation":"C:\\Program Files\\WindowsApps\\MSTeams_25.1.0.0_x64__8wekyb3d8bbwe","TaskId":"TeamsTfwStartupTask","Enabled":"1","DisplayName":null,"PublisherDisplayName":"","Executable":"msteams_autostarter.exe"}]
            """;

        var tasks = StartupTaskScript.Parse(json);

        Assert.Equal(2, tasks.Count);
        var terminal = tasks[0];
        Assert.Equal(
            new StartupTaskManifest(
                "Microsoft.WindowsTerminal",
                "Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe",
                "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
                @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe",
                "StartTerminalOnLoginTask",
                EnabledInManifest: false,
                "ms-resource:AppName",
                "Microsoft Corporation",
                "WindowsTerminal.exe"),
            terminal);
        Assert.True(tasks[1].EnabledInManifest);
        Assert.Null(tasks[1].DisplayName);
        Assert.Null(tasks[1].PublisherDisplayName);
    }

    [Fact]
    public void Parse_EmptyArrayAndSingleObject()
    {
        Assert.Empty(StartupTaskScript.Parse("[]"));
        var single = StartupTaskScript.Parse(
            """{"PackageName":"P","PackageFullName":"P_1_x64__x","PackageFamilyName":"P_x","InstallLocation":"C:\\P","TaskId":"T","Enabled":"true","Executable":"p.exe"}""");
        Assert.Equal("T", Assert.Single(single).TaskId);
    }

    [Fact]
    public void Parse_UnknownEnabled_SkippedWithReason()
    {
        var skipped = new List<SkippedStartupPackage>();

        var tasks = StartupTaskScript.Parse(
            """[{"PackageName":"P","PackageFullName":"P_1_x64__x","PackageFamilyName":"P_x","InstallLocation":"C:\\P","TaskId":"T","Enabled":"maybe"}]""",
            skipped);

        Assert.Empty(tasks);
        var skip = Assert.Single(skipped);
        Assert.Equal("P_1_x64__x", skip.PackageFullName);
        Assert.Contains("maybe", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ErrorItem_GoesToSkipped()
    {
        var skipped = new List<SkippedStartupPackage>();

        var tasks = StartupTaskScript.Parse("""[{"PackageFullName":"Z_1_x64__x","Error":"Unexpected end of file."}]""", skipped);

        Assert.Empty(tasks);
        Assert.Equal(new SkippedStartupPackage("Z_1_x64__x", "Unexpected end of file."), Assert.Single(skipped));
    }

    [Theory]
    [InlineData("nie json")]
    [InlineData("42")]
    [InlineData("[1]")]
    [InlineData("""[{"PackageName":"P","PackageFamilyName":"P_x","InstallLocation":"C:\\P","TaskId":"T","Enabled":"true"}]""")]
    public void Parse_BadJson_FormatException(string json)
        => Assert.Throws<FormatException>(() => StartupTaskScript.Parse(json));
}
