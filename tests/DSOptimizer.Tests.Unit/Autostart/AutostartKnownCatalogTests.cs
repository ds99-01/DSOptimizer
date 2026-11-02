using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Autostart;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartKnownCatalogTests
{
    private const string SecurityHealth = @"C:\Windows\System32\SecurityHealthSystray.exe";

    private const string ProtectedEntry = """
        { "id": "windows.security-health",
          "match": { "file": "SecurityHealthSystray.exe", "folders": ["%SystemRoot%\\System32\\"], "publishers": ["Microsoft Corporation"] },
          "verdict": "protected", "summary": "Ikona Zabezpieczeń Windows.", "source": ["https://learn.microsoft.com/windows/security"],
          "confirmed": true }
        """;

    private const string CanDisableEntry = """
        { "id": "steam",
          "match": { "file": "steam.exe", "folders": ["%ProgramFiles(x86)%\\Steam\\"], "publishers": [" Valve* "], "valueName": "Steam" },
          "verdict": "can-disable", "summary": "Klient Steam.", "source": ["docs/zrodla/2026-11-02-autostart-pomiar.md", "https://help.steampowered.com/x"],
          "confirmed": true }
        """;

    private static readonly UserEnvironment Environment = UserEnvironment.FromVariables(new Dictionary<string, string>
    {
        ["SystemRoot"] = @"C:\Windows",
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
        ["ProgramData"] = @"C:\ProgramData",
        ["LOCALAPPDATA"] = @"C:\Users\dso\AppData\Local",
        ["APPDATA"] = @"C:\Users\dso\AppData\Roaming",
    });

    public static IEnumerable<object[]> InvalidCases()
    {
        yield return Case(ProtectedEntry, e => ((JObject)e["match"]!).Remove("folders"), "match.folders");
        yield return Case(ProtectedEntry, e => e["match"]!["folders"] = new JArray(), "match.folders");
        yield return Case(ProtectedEntry, e => e["match"]!["folders"] = new JArray(@"C:\x\"), "match.folders");
        yield return Case(ProtectedEntry, e => e["match"]!["folders"] = new JArray("*"), "match.folders");
        yield return Case(ProtectedEntry, e => e["match"]!["folders"] = new JArray(@"%SystemRoot%\System32"), "match.folders");
        yield return Case(ProtectedEntry, e => e["match"]!["folders"] = new JArray(@"%ProgramFiles%*"), "match.folders");
        yield return Case(ProtectedEntry, e => e["match"]!["folders"] = new JArray(@"%ProgramFilesX%\x\"), "match.folders");
        yield return Case(ProtectedEntry, e => e["match"]!["folders"] = new JArray(1), "match.folders");
        yield return Case(ProtectedEntry, e => e["match"]!["folders"] = "%SystemRoot%\\", "match.folders");
        yield return Case(ProtectedEntry, e => ((JObject)e["match"]!).Remove("publishers"), "match.publishers");
        yield return Case(ProtectedEntry, e => e["match"]!["publishers"] = new JArray(" "), "match.publishers");
        yield return Case(ProtectedEntry, e => ((JObject)e["match"]!).Remove("file"), "match.file");
        yield return Case(ProtectedEntry, e => e["match"]!["file"] = @"System32\SecurityHealthSystray.exe", "match.file");
        yield return Case(ProtectedEntry, e => e["match"]!["valueName"] = 5, "match.valueName");
        yield return Case(ProtectedEntry, e => e.Remove("match"), "match");
        yield return Case(ProtectedEntry, e => e["verdict"] = "maybe", "verdict");
        yield return Case(ProtectedEntry, e => e.Remove("verdict"), "verdict");
        yield return Case(ProtectedEntry, e => e["summary"] = new string('a', 161), "summary");
        yield return Case(ProtectedEntry, e => e.Remove("summary"), "summary");
        yield return Case(ProtectedEntry, e => e["source"] = new JArray(), "source");
        yield return Case(ProtectedEntry, e => e["source"] = "https://learn.microsoft.com/x", "source");
        yield return Case(ProtectedEntry, e => e["source"] = new JArray("http://x"), "source");
        yield return Case(ProtectedEntry, e => e["source"] = new JArray("docs/i18n/glossary.md"), "source");
        yield return Case(ProtectedEntry, e => e["source"] = new JArray(@"C:\x.md"), "source");
        yield return Case(ProtectedEntry, e => e["source"] = new JArray("docs/zrodla/2026-11-02-AUTOSTART-pomiar.md"), "source");
        yield return Case(ProtectedEntry, e => e.Remove("confirmed"), "confirmed");
        yield return Case(ProtectedEntry, e => e["confirmed"] = "true", "confirmed");
        yield return Case(ProtectedEntry, e => e["id"] = " ", "id");
        yield return Case(CanDisableEntry, e => e["confirmed"] = false, "confirmed");
        yield return Case(CanDisableEntry, e => e["source"] = new JArray("https://help.steampowered.com/x"), "source");
        yield return Case(CanDisableEntry, e => e["source"] = new JArray("docs/zrodla/2026-11-02-autostart-pomiar.md"), "source");
        yield return new object[] { Wrap(ProtectedEntry, ProtectedEntry.Replace("windows.security-health", "Windows.Security-Health")), "id" };
        yield return new object[] { "{ \"entries\": {} }", "entries" };
        yield return new object[] { "{ \"entries\": [1] }", "entries" };
        yield return new object[] { "{ \"entries\": [", "json" };
    }

    [Fact]
    public void Load_ValidEntry_AllFields()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(CanDisableEntry, ProtectedEntry));

        Assert.Equal(new[] { "steam", "windows.security-health" }, catalog.Entries.Select(e => e.Id));
        var steam = catalog.Entries[0];
        Assert.Equal(AutostartVerdict.CanDisable, steam.Verdict);
        Assert.Equal("Klient Steam.", steam.Summary);
        Assert.True(steam.Confirmed);
        Assert.Equal(new[] { "docs/zrodla/2026-11-02-autostart-pomiar.md", "https://help.steampowered.com/x" }, steam.Sources);
        Assert.Equal("steam.exe", steam.Match.File);
        Assert.Equal(new[] { @"%ProgramFiles(x86)%\Steam\" }, steam.Match.Folders);
        Assert.Equal(new[] { "Valve*" }, steam.Match.Publishers);
        Assert.Equal("Steam", steam.Match.ValueName);
        Assert.Null(steam.Match.PackageFamily);
        Assert.Equal(AutostartVerdict.Protected, catalog.Entries[1].Verdict);
    }

    [Fact]
    public void Load_AllFolderRootsAccepted_CaseIgnored()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(Mutate(ProtectedEntry, e => e["match"]!["folders"] = new JArray(
            @"%systemroot%\System32\", @"%ProgramFiles%\*", @"%PROGRAMFILES(X86)%\x\", @"%ProgramData%\x\", @"%LocalAppData%\x\", @"%APPDATA%\x*"))));

        Assert.Equal(6, catalog.Entries[0].Match.Folders.Count);
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Load_InvalidField_Throws(string json, string field)
    {
        var ex = Assert.Throws<AutostartCatalogException>(() => AutostartKnownCatalog.Load(json));

        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void Load_ErrorNamesEntryAndField()
    {
        var ex = Assert.Throws<AutostartCatalogException>(() => AutostartKnownCatalog.Load(Wrap(Mutate(ProtectedEntry, e => e["verdict"] = "maybe"))));

        Assert.Equal("windows.security-health", ex.EntryId);
        Assert.Contains("windows.security-health", ex.Message, StringComparison.Ordinal);
        Assert.Contains("verdict", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => AutostartKnownCatalog.Load(null!));

    [Fact]
    public void Match_FileFolderPublisher_Matches()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(ProtectedEntry));

        Assert.Equal("windows.security-health", catalog.Match(Entry(@"c:\windows\system32\securityhealthsystray.exe", "microsoft corporation"), Environment)?.Id);
    }

    [Fact]
    public void SameFileOtherFolder_NoMatch()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(ProtectedEntry));

        Assert.Null(catalog.Match(Entry(@"C:\Users\dso\AppData\Local\Temp\SecurityHealthSystray.exe", "Microsoft Corporation"), Environment));
        Assert.Null(catalog.Match(Entry(@"C:\Windows\System32\Temp\SecurityHealthSystray.exe", "Microsoft Corporation"), Environment));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\..\..\Users\dso\AppData\Local\Temp\SecurityHealthSystray.exe")]
    [InlineData(@"C:\Users\dso\AppData\Local\Temp\..\..\..\..\..\Windows\System32\.\SecurityHealthSystray.exe")]
    [InlineData(@"C:/Windows/System32/SecurityHealthSystray.exe")]
    [InlineData(@"SecurityHealthSystray.exe")]
    public void Match_PathNotCanonical_NoMatch(string filePath)
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(ProtectedEntry));

        Assert.Null(catalog.Match(Entry(filePath, "Microsoft Corporation"), Environment));
    }

    [Fact]
    public void Match_PublisherDiffers_NoMatch()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(ProtectedEntry));

        Assert.Null(catalog.Match(Entry(SecurityHealth, "Contoso"), Environment));
    }

    [Fact]
    public void Match_FileNotDetermined_NoMatch()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(ProtectedEntry));

        Assert.Null(catalog.Match(Entry(null, "Microsoft Corporation"), Environment));
    }

    [Fact]
    public void Match_NoPublisher_NoMatch()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(ProtectedEntry));

        Assert.Null(catalog.Match(Entry(SecurityHealth, null), Environment));
        Assert.Null(catalog.Match(Entry(SecurityHealth, "  "), Environment));
    }

    [Fact]
    public void Match_ValueNameRequired()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(CanDisableEntry));
        var steam = Entry(@"C:\Program Files (x86)\Steam\steam.exe", "Valve Corporation") with { EntryName = "steam" };

        Assert.Equal("steam", catalog.Match(steam, Environment)?.Id);
        Assert.Null(catalog.Match(steam with { EntryName = "Steam Helper" }, Environment));
    }

    [Fact]
    public void Match_PackageFamilyRequired()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(Mutate(ProtectedEntry, e => e["match"]!["packageFamily"] = "Microsoft.SecHealthUI_8wekyb3d8bbwe")));
        var entry = Entry(SecurityHealth, "Microsoft Corporation");

        Assert.Null(catalog.Match(entry, Environment));
        Assert.NotNull(catalog.Match(entry with { PackageFamily = "microsoft.sechealthui_8wekyb3d8bbwe" }, Environment));
    }

    [Fact]
    public void Match_FolderWildcardDriverStore()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(Mutate(ProtectedEntry, e =>
        {
            e["match"]!["file"] = "RtkAudUService64.exe";
            e["match"]!["folders"] = new JArray(@"%SystemRoot%\System32\DriverStore\FileRepository\*\");
            e["match"]!["publishers"] = new JArray("Realtek*");
        })));

        Assert.NotNull(catalog.Match(
            Entry(@"C:\Windows\System32\DriverStore\FileRepository\realtek.inf_amd64_1\RtkAudUService64.exe", "Realtek Semiconductor"), Environment));
        Assert.Null(catalog.Match(Entry(@"C:\Windows\System32\DriverStore\FileRepository\RtkAudUService64.exe", "Realtek Semiconductor"), Environment));
    }

    [Fact]
    public void Match_UnexpandableFolder_NoMatch()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(Mutate(ProtectedEntry, e => e["match"]!["folders"] = new JArray(@"%LOCALAPPDATA%\Microsoft\OneDrive\"))));
        var onedrive = Entry(@"C:\Users\dso\AppData\Local\Microsoft\OneDrive\SecurityHealthSystray.exe", "Microsoft Corporation");

        Assert.NotNull(catalog.Match(onedrive, Environment));
        Assert.Null(catalog.Match(onedrive, UserEnvironment.FromVariables(new Dictionary<string, string> { ["SystemRoot"] = @"C:\Windows" })));
    }

    [Fact]
    public void Match_ProtectedWinsOverCanDisable()
    {
        var canDisable = Mutate(CanDisableEntry, e =>
        {
            e["match"]!["file"] = "SecurityHealth*.exe";
            e["match"]!["folders"] = new JArray(@"%SystemRoot%\*");
            e["match"]!["publishers"] = new JArray("Microsoft*");
            ((JObject)e["match"]!).Remove("valueName");
        });
        var catalog = AutostartKnownCatalog.Load(Wrap(canDisable, ProtectedEntry));

        Assert.Equal(AutostartVerdict.Protected, catalog.Match(Entry(SecurityHealth, "Microsoft Corporation"), Environment)?.Verdict);
        Assert.Equal("steam", catalog.Match(Entry(@"C:\Windows\SecurityHealthSystray.exe", "Microsoft Corporation"), Environment)?.Id);
    }

    [Fact]
    public void Match_NullArguments_Throw()
    {
        var catalog = AutostartKnownCatalog.Load(Wrap(ProtectedEntry));

        Assert.Throws<ArgumentNullException>(() => catalog.Match(null!, Environment));
        Assert.Throws<ArgumentNullException>(() => catalog.Match(Entry(SecurityHealth, "Microsoft Corporation"), null!));
    }

    internal static string Wrap(params string[] entries) => "{ \"entries\": [" + string.Join(",", entries) + "] }";

    internal static AutostartEntry Entry(string? filePath, string? publisher) => new()
    {
        Id = "run:machine:SecurityHealth",
        Source = AutostartSource.RunMachine,
        EntryName = "SecurityHealth",
        Name = "Windows Security notification icon",
        Location = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        FileStatus = AutostartFileStatus.Exists,
        Switch = new AutostartSwitch(
            AutostartSwitchKind.StartupApproved,
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            "SecurityHealth",
            AutostartSwitchFormat.Absent,
            AutostartState.Enabled,
            null,
            null),
        Publisher = publisher,
        FilePath = filePath,
    };

    private static string Mutate(string entry, Action<JObject> change)
    {
        var obj = JObject.Parse(entry);
        change(obj);
        return obj.ToString();
    }

    private static object[] Case(string entry, Action<JObject> change, string field) => new object[] { Wrap(Mutate(entry, change)), field };
}
