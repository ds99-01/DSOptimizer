using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Cli;
using DSOptimizer.Core.Install;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class CliHostTests
{
    private const string TweakId = "test.cli-e2e";

    [Fact]
    public void Run_DetectApplyUndo_OnSandbox_RoundTripsWithReports()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        var catalog = WriteCatalog(temp, sandbox, "Test CLI");
        JObject Step(string command, params string[] extra)
        {
            var output = temp.Sub(command + Guid.NewGuid().ToString("N") + ".json");
            var args = new[] { "--" + command, TweakId, "--catalog", catalog, "--out", output }.Concat(extra).ToArray();
            var code = CliHost.Run(args, temp.AppRoot, null);
            var json = JObject.Parse(File.ReadAllText(output));
            Assert.Equal(code, (int)json["exitCode"]!);
            return json;
        }

        var before = Step("detect");
        Assert.Equal(WindowsIdentity.GetCurrent().User!.Value, (string)before["user"]!["sid"]!); // test z konta zalogowanego użytkownika
        Assert.Equal("NotApplied", (string)before["state"]!);
        var apply = Step("apply", "--restore-point", "skip");
        Assert.Equal("Applied", (string)apply["outcome"]!);
        Assert.Equal(JTokenType.Null, apply["restorePoint"]!.Type);
        Assert.Contains(apply["warnings"]!.Children(), w => ((string)w!).StartsWith("Użyto zewnętrznego katalogu", StringComparison.Ordinal));
        Assert.Equal("Applied", (string)Step("detect")["state"]!);
        Assert.Equal("Undone", (string)Step("undo")["outcome"]!);
        Assert.Equal("NotApplied", (string)Step("detect")["state"]!);
    }

    [Fact]
    public void Run_UsageErrorWithOut_WritesReportAndReturns2()
    {
        using var temp = new TempDirectory();
        var output = temp.Sub("wynik.json");

        var code = CliHost.Run(new[] { "--detect", "--out", output }, temp.AppRoot, null);

        Assert.Equal(2, code);
        var json = JObject.Parse(File.ReadAllText(output));
        Assert.Equal(2, (int)json["exitCode"]!);
        Assert.NotEmpty(json["errors"]!);
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void Run_UsageErrorWithoutOut_Returns2()
    {
        using var temp = new TempDirectory();

        Assert.Equal(2, CliHost.Run(new[] { "--foo" }, temp.AppRoot, null));
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void Run_MissingCatalog_Returns3WithoutTouchingRoot()
    {
        using var temp = new TempDirectory();
        var output = temp.Sub("wynik.json");

        var code = CliHost.Run(
            new[] { "--detect", TweakId, "--catalog", temp.Sub("brak.json"), "--out", output }, temp.AppRoot, null);

        Assert.Equal(3, code);
        var json = JObject.Parse(File.ReadAllText(output));
        Assert.Equal("CatalogError", (string)json["result"]!);
        Assert.Contains(json["errors"]!.Children(), e => ((string)e!).IndexOf("Nie można odczytać katalogu", StringComparison.Ordinal) >= 0);
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void Run_CatalogWithBomAndPolishText_Loads()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        var catalog = WriteCatalog(temp, sandbox, "Zażółć gęślą jaźń");
        var output = temp.Sub("wynik.json");

        var code = CliHost.Run(
            new[] { "--detect", TweakId, "--catalog", catalog, "--out", output }, temp.AppRoot, null);

        Assert.Equal(0, code);
        Assert.Equal("NotApplied", (string)JObject.Parse(File.ReadAllText(output))["state"]!);
    }

    /// <summary>Tylko odczyt listy aplikacji hosta (dozwolony): wbudowana klasyfikacja ładuje się, raport ma wiersze, nic nie jest usuwane.</summary>
    [Fact]
    public void Run_ListApps_ReadsInstalledAppsWithBuiltInClassification()
    {
        using var temp = new TempDirectory();
        var output = temp.Sub("lista.json");

        var code = CliHost.Run(new[] { "--list-apps", "--out", output }, temp.AppRoot, null);

        var json = JObject.Parse(File.ReadAllText(output));
        Assert.True(code == 0, string.Join(" | ", json["errors"]!.Children().Select(e => (string)e!)));
        Assert.Equal("ListApps", (string)json["command"]!);
        var apps = json["apps"]!.Children().ToList();
        Assert.NotEmpty(apps);
        Assert.All(apps, app => Assert.Matches("^(appx|win32):", (string)app["id"]!));
    }

    /// <summary>Id spoza listy: kod 1 z błędem i bez punktu przywracania ani próby usunięcia (na hoście nic się nie uruchamia).</summary>
    [Fact]
    public void Run_RemoveAppUnknownId_Returns1WithoutRemoving()
    {
        using var temp = new TempDirectory();
        var output = temp.Sub("usun.json");

        var code = CliHost.Run(new[] { "--remove-app", "appx:DsoTest.NieMa_0000000000000", "--out", output }, temp.AppRoot, null);

        Assert.Equal(1, code);
        var json = JObject.Parse(File.ReadAllText(output));
        Assert.Equal(JTokenType.Null, json["outcome"]!.Type);
        Assert.Equal(JTokenType.Null, json["restorePoint"]!.Type);
        Assert.Contains("appx:DsoTest.NieMa_0000000000000", (string)json["errors"]![0]!, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_OutDirectoryMissing_Returns6()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        var catalog = WriteCatalog(temp, sandbox, "Test CLI");

        var code = CliHost.Run(
            new[] { "--detect", TweakId, "--catalog", catalog, "--out", temp.Sub(@"brak\x.json") }, temp.AppRoot, null);

        Assert.Equal(6, code);
        Assert.False(Directory.Exists(temp.Sub("brak")));
    }

    [Fact]
    public void Run_RootCannotBeSecured_Returns5WithReport()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        var catalog = WriteCatalog(temp, sandbox, "Test CLI");
        var output = temp.Sub("wynik.json");
        Directory.CreateDirectory(temp.Sub("cel"));
        Junction.Create(temp.Sub("app"), temp.Sub("cel"));
        try
        {
            var code = CliHost.Run(
                new[] { "--detect", TweakId, "--catalog", catalog, "--out", output }, temp.AppRoot, null);

            Assert.Equal(5, code);
            var json = JObject.Parse(File.ReadAllText(output));
            Assert.Equal("InternalError", (string)json["result"]!);
            Assert.Matches(@"Nie można przygotować środowiska \(.+\): \w+: ", (string)json["errors"]![0]!);
            Assert.Contains(json["warnings"]!.Children(), w => ((string)w!).StartsWith("Użyto zewnętrznego katalogu", StringComparison.Ordinal));
            Assert.Empty(Directory.GetFileSystemEntries(temp.Sub("cel")));
        }
        finally
        {
            Directory.Delete(temp.Sub("app"), recursive: false);
        }
    }

    [Fact]
    public void Run_ReadAllTextThrowsUnexpected_ReturnsInternalErrorWithReport()
    {
        using var temp = new TempDirectory();
        var output = temp.Sub("wynik.json");

        var code = CliHost.Run(
            new[] { "--detect", TweakId, "--catalog", temp.Sub("x.json"), "--out", output },
            temp.AppRoot,
            null,
            _ => throw new InvalidOperationException("boom"),
            () => Array.Empty<DSOptimizer.Core.Catalog.CatalogSource>());

        Assert.Equal(5, code);
        var json = JObject.Parse(File.ReadAllText(output));
        Assert.Equal("InternalError", (string)json["result"]!);
        Assert.Contains("InvalidOperationException", (string)json["errors"]![0]!, StringComparison.Ordinal);
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Fact]
    public void Run_BuiltInSourcesThrow_ReturnsInternalErrorWithReport()
    {
        using var temp = new TempDirectory();
        var output = temp.Sub("wynik.json");

        var code = CliHost.Run(
            new[] { "--detect", TweakId, "--out", output },
            temp.AppRoot,
            null,
            File.ReadAllText,
            () => throw new InvalidOperationException("zasoby"));

        Assert.Equal(5, code);
        var json = JObject.Parse(File.ReadAllText(output));
        Assert.Equal("InternalError", (string)json["result"]!);
        Assert.Contains("InvalidOperationException: zasoby", (string)json["errors"]![0]!, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ExternalCatalog_DoesNotReadBuiltInSources()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        var catalog = WriteCatalog(temp, sandbox, "Test CLI");
        var output = temp.Sub("wynik.json");

        var code = CliHost.Run(
            new[] { "--detect", TweakId, "--catalog", catalog, "--out", output },
            temp.AppRoot,
            null,
            File.ReadAllText,
            () => throw new InvalidOperationException("nie wolno czytać wbudowanych"));

        Assert.Equal(0, code);
    }

    [Fact]
    public void Run_SerializerThrowsInCatchAll_ReturnsInternalErrorWithoutThrowing()
    {
        using var temp = new TempDirectory();
        var output = temp.Sub("wynik.json");

        var code = CliHost.Run(
            new[] { "--foo", "--out", output },
            temp.AppRoot,
            null,
            File.ReadAllText,
            () => Array.Empty<DSOptimizer.Core.Catalog.CatalogSource>(),
            _ => throw new InvalidOperationException("serializer"));

        Assert.Equal(5, code);
        Assert.False(File.Exists(output));
    }

    private const string TestAppsJson = """
        {
          "entries": [
            { "id": "install.dso-test-msi", "wingetId": "Dso.TestMsi", "title": "DSO Test MSI", "summary": "Program testowy.",
              "category": "tools", "match": { "displayName": "DSO Test MSI" }, "source": "https://github.com/ds99-01" },
            { "id": "install.dso-test-absent", "wingetId": "Dso.TestAbsent", "title": "DSO Test Absent", "summary": "Program testowy.",
              "category": "tools", "match": { "displayName": "DSO Test Absent" }, "source": "https://github.com/ds99-01" }
          ]
        }
        """;

    /// <summary>winget w pamięci: zapisuje wywołania; <c>--version</c> = 0 z wersją, każde inne = kod <see cref="ExitCode"/>.</summary>
    private sealed class FakeWinget : IWingetRunner, IWingetLogs
    {
        public List<string> Calls { get; } = new();

        public int ExitCode { get; set; }

        public WingetRun Run(string arguments, TimeSpan waitLimit, Action<string>? onLine)
        {
            Calls.Add(arguments);
            return arguments == WingetArguments.Version() ? new WingetRun(0, false, new[] { "v1.11.510" }) : new WingetRun(ExitCode, false, Array.Empty<string>());
        }

        public string NewLogPath(string wingetId, DateTimeOffset at) => @"C:\dso-tests-nie-istnieje\" + wingetId + ".log";
    }

    private sealed class FakeReader : IInstalledAppsReader
    {
        public List<InstalledApp> Apps { get; } = new();

        public InstalledAppsReading Read(string userSid) => new(Apps.ToList(), Array.Empty<string>());

        public AppPresence ReadPresence(InstalledApp app, string userSid, AppPresenceScope scope) => AppPresence.Unknown;
    }

    private static FakeReader ReaderWith(params string[] programs)
    {
        var reader = new FakeReader();
        reader.Apps.Add(new InstalledApp(
            "appx:Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", AppKind.Store, "Instalator aplikacji", "Microsoft Corporation", "1.26.510.0",
            AppScope.User, null,
            new AppxInfo("Microsoft.DesktopAppInstaller_1.26.510.0_x64__8wekyb3d8bbwe", "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
                "Microsoft.DesktopAppInstaller", false, false, false, "Store", true),
            null));
        foreach (var name in programs)
        {
            var key = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + name;
            reader.Apps.Add(new InstalledApp("win32:" + key, AppKind.Program, name, "DSO", "1.0", AppScope.Machine, null, null,
                new Win32Info(key, @"C:\u.exe", null, false, null, null, false)));
        }

        return reader;
    }

    private static int RunInstall(TempDirectory temp, string[] args, Func<AppComposition, AppsInstallCatalog, CliInstallServices> factory)
        => CliHost.Run(args, temp.AppRoot, null, File.ReadAllText, () => BuiltInCatalog.Sources, null, factory);

    /// <summary>Błąd formatu --apps-catalog = kod 3 z komunikatem InstallCatalogException; fabryka usług nie jest wołana, korzeń nietknięty.</summary>
    [Theory]
    [InlineData("{ \"entries\": [ { \"id\": \"zle\" } ] }", "apps: zle, pole id")]
    [InlineData("{ \"entries\": ", "pole json")]
    [InlineData(null, "Nie można odczytać listy aplikacji")]
    public void Run_AppsCatalogInvalid_Returns3_NothingStarts(string? json, string fragment)
    {
        using var temp = new TempDirectory();
        var apps = temp.Sub("apps.json");
        if (json is not null)
        {
            File.WriteAllText(apps, json, new UTF8Encoding(false));
        }

        var output = temp.Sub("wynik.json");
        var factoryCalls = 0;

        var code = RunInstall(temp, new[] { "--install-apps", "install.dso-test-msi", "--apps-catalog", apps, "--out", output }, (_, _) =>
        {
            factoryCalls++;
            throw new InvalidOperationException("fabryka nie powinna być wołana");
        });

        Assert.Equal(3, code);
        var report = JObject.Parse(File.ReadAllText(output));
        Assert.Equal("CatalogError", (string)report["result"]!);
        Assert.Contains(fragment, (string)report["errors"]![0]!, StringComparison.Ordinal);
        Assert.Equal(0, factoryCalls);
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    /// <summary>--list-install z --apps-catalog na podmienionych usługach: status „zainstalowana” z reguły match, winget tylko --version (fałszywy).</summary>
    [Fact]
    public void Run_ListInstall_WithAppsCatalog_UsesInjectedServices()
    {
        using var temp = new TempDirectory();
        var apps = temp.Sub("vm-test-apps.json");
        File.WriteAllText(apps, TestAppsJson, new UTF8Encoding(true));
        var output = temp.Sub("lista.json");
        var winget = new FakeWinget();
        AppsInstallCatalog? passed = null;

        var code = RunInstall(temp, new[] { "--list-install", "--apps-catalog", apps, "--out", output }, (composition, catalog) =>
        {
            passed = catalog;
            return new CliInstallServices
            {
                Runner = winget,
                Logs = winget,
                Reader = ReaderWith("DSO Test MSI"),
                Catalog = catalog,
                ProcessSid = composition.Services.User.Sid,
                AccountName = _ => "dso",
            };
        });

        var report = JObject.Parse(File.ReadAllText(output));
        Assert.True(code == 0, string.Join(" | ", report["errors"]!.Children().Select(e => (string)e!)));
        Assert.Equal("ListInstall", (string)report["command"]!);
        Assert.Equal(new[] { "install.dso-test-msi", "install.dso-test-absent" }, passed!.Entries.Select(e => e.Id));
        var rows = report["installRows"]!.Children().ToList();
        Assert.Equal(new[] { true, false }, rows.Select(r => (bool)r["installed"]!));
        Assert.All(winget.Calls, call => Assert.Equal(WingetArguments.Version(), call));
        Assert.NotNull(report["winget"]!["state"]);
    }

    /// <summary>--install-apps na podmienionych usługach: brak sieci = Failed z kodem szesnastkowo, następna pozycja Skipped, kod 1.</summary>
    [Fact]
    public void Run_InstallApps_InjectedServices_NoNetwork_Returns1()
    {
        using var temp = new TempDirectory();
        var apps = temp.Sub("vm-test-apps.json");
        File.WriteAllText(apps, TestAppsJson, new UTF8Encoding(false));
        var output = temp.Sub("instalacja.json");
        var winget = new FakeWinget { ExitCode = unchecked((int)0x8A150107) };

        var code = RunInstall(
            temp,
            new[] { "--install-apps", "install.dso-test-absent,install.dso-test-msi", "--apps-catalog", apps, "--out", output },
            (composition, catalog) => new CliInstallServices
            {
                Runner = winget,
                Logs = winget,
                Reader = ReaderWith(),
                Catalog = catalog,
                ProcessSid = composition.Services.User.Sid,
                AccountName = _ => "dso",
            });

        Assert.Equal(1, code);
        var report = JObject.Parse(File.ReadAllText(output));
        var items = report["installItems"]!.Children().ToList();
        Assert.Equal(new[] { "install.dso-test-msi", "install.dso-test-absent" }, items.Select(i => (string)i["id"]!));
        Assert.DoesNotContain(winget.Calls, call => !call.StartsWith("--version", StringComparison.Ordinal) && !call.StartsWith("install --id Dso.", StringComparison.Ordinal));
        if ((string)report["winget"]!["state"]! == "Ready")
        {
            Assert.Equal(new[] { "Failed", "Skipped" }, items.Select(i => (string)i["outcome"]!));
            Assert.Equal("0x8A150107", (string)items[0]["wingetCode"]!);
        }
        else
        {
            Assert.All(items, i => Assert.Equal("Refused", (string)i["outcome"]!));
        }
    }

    /// <summary>
    /// Tylko składanie (plan RustDesk, Task 4): usługi CLI dostają te same usługi GitHuba co reszta programu; nic nie jest pobierane
    /// ani uruchamiane, folder pobrań nie powstaje.
    /// </summary>
    [Fact]
    public void CreateInstallServices_UsesCompositionGitHub_NothingStarts()
    {
        using var temp = new TempDirectory();
        var composition = AppComposition.Create(temp.AppRoot, null);
        var catalog = AppsInstallCatalog.Load(BuiltInCatalog.AppsInstallSource.Json);

        var services = CliHost.CreateInstallServices(composition, catalog);

        Assert.Same(composition.GitHub, services.GitHub);
        Assert.Same(composition.Winget, services.Runner);
        Assert.Same(catalog, services.Catalog);
        Assert.False(Directory.Exists(temp.AppRoot.DownloadsDirectory));
    }

    private static string WriteCatalog(TempDirectory temp, RegistrySandbox sandbox, string title)
    {
        var tweak = new JObject
        {
            ["id"] = TweakId,
            ["category"] = "test",
            ["kind"] = "tweak",
            ["title"] = title,
            ["summary"] = "Opis testowy.",
            ["benefit"] = "Zysk testowy.",
            ["risk"] = "safe",
            ["scope"] = "user",
            ["registry"] = new JArray(new JObject
            {
                ["path"] = sandbox.HkcuPath,
                ["name"] = "A",
                ["type"] = "DWord",
                ["value"] = 1,
                ["original"] = "<RemoveEntry>",
            }),
        };
        var path = temp.Sub("katalog-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, new JArray(tweak).ToString(), new UTF8Encoding(true));
        return path;
    }
}
