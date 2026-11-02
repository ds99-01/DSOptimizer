using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Localization;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

/// <summary>
/// Wbudowana baza znanych pozycji autostartu (<c>catalog/autostart-known.json</c>): wpisy w ustalonej kolejności, źródła, wiersze
/// w dokumentacji i dopasowanie do ścieżek, wydawców i nazw wartości odczytanych z programów zainstalowanych na maszynie testowej.
/// </summary>
public class AutostartKnownBuiltInTests
{
    private const string ProgramsDocument = "docs/zrodla/2026-11-02-autostart-programy.md";
    private const string StartupDocument = "docs/zrodla/2026-11-02-autostart-pomiar.md";

    private static readonly string[] AdminProgramFolders = { @"%ProgramFiles%\*", @"%ProgramFiles(x86)%\*" };

    /// <summary>Reguły wpisów w kolejności pliku: id, ocena, plik, foldery, wydawcy, nazwa wartości, potwierdzone.</summary>
    private static readonly (string Id, AutostartVerdict Verdict, string File, string[] Folders, string[] Publishers, string? ValueName, bool Confirmed)[] Table =
    {
        ("steam", AutostartVerdict.CanDisable, "steam.exe", new[] { @"%ProgramFiles(x86)%\Steam\" }, new[] { "Valve Corporation" }, "Steam", true),
        ("epic-games-launcher", AutostartVerdict.CanDisable, "EpicGamesLauncher.exe", new[] { @"%ProgramFiles%\Epic Games\Launcher\Portal\Binaries\Win64\" }, new[] { "Epic Games, Inc." }, "EpicGamesLauncher", true),
        ("battle-net", AutostartVerdict.CanDisable, "Battle.net.exe", new[] { @"%ProgramFiles(x86)%\Battle.net\" }, new[] { "Blizzard Entertainment" }, "Battle.net", true),
        ("spotify", AutostartVerdict.CanDisable, "Spotify.exe", new[] { @"%APPDATA%\Spotify\" }, new[] { "Spotify Ltd" }, "Spotify", true),
        ("discord", AutostartVerdict.CanDisable, "Discord.exe", new[] { @"%LOCALAPPDATA%\Discord\" }, new[] { "Discord Inc." }, "Discord", true),
        ("ea-app", AutostartVerdict.CanDisable, "EALauncher.exe", new[] { @"%ProgramFiles%\Electronic Arts\EA Desktop\EA Desktop\" }, new[] { "Electronic Arts" }, "EADM", true),
        ("security-health", AutostartVerdict.Protected, "SecurityHealthSystray.exe", new[] { @"%SystemRoot%\System32\" }, new[] { "Microsoft Corporation" }, "SecurityHealth", true),
        ("realtek-audio-service", AutostartVerdict.Protected, "RtkAudUService64.exe", new[] { @"%SystemRoot%\System32\", @"%SystemRoot%\System32\DriverStore\FileRepository\*\" }, new[] { "Realtek Semiconductor Corp." }, null, false),
        ("realtek-hd-audio-manager", AutostartVerdict.Protected, "RAVCpl64.exe", new[] { @"%ProgramFiles%\Realtek\Audio\HDA\" }, new[] { "Realtek Semiconductor Corp." }, null, false),
        ("elan-touchpad", AutostartVerdict.Protected, "ETDCtrl*.exe", new[] { @"%ProgramFiles%\Elantech\*", @"%SystemRoot%\System32\" }, new[] { "ELAN Microelectronics Corp." }, null, false),
        ("synaptics-touchpad", AutostartVerdict.Protected, "SynTPEnh.exe", new[] { @"%ProgramFiles%\Synaptics\SynTP\" }, new[] { "Synaptics Incorporated" }, null, false),
        ("asus-hotkeys", AutostartVerdict.Protected, "HControl.exe", AdminProgramFolders, new[] { "ASUSTeK Computer Inc." }, null, false),
        ("kaspersky", AutostartVerdict.Protected, "avp.exe", AdminProgramFolders, new[] { "AO Kaspersky Lab" }, null, false),
        ("avast", AutostartVerdict.Protected, "AvastUI.exe", AdminProgramFolders, new[] { "AVAST Software*", "Gen Digital*" }, null, false),
        ("bitdefender", AutostartVerdict.Protected, "bdagent.exe", AdminProgramFolders, new[] { "Bitdefender SRL" }, null, false),
        ("logitech-g-hub", AutostartVerdict.Protected, "lghub.exe", AdminProgramFolders, new[] { "Logitech Europe S.A." }, null, false),
    };

    private static readonly UserEnvironment Environment = UserEnvironment.FromVariables(new Dictionary<string, string>
    {
        ["SystemRoot"] = @"C:\Windows",
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
        ["ProgramData"] = @"C:\ProgramData",
        ["LOCALAPPDATA"] = @"C:\Users\dso\AppData\Local",
        ["APPDATA"] = @"C:\Users\dso\AppData\Roaming",
    });

    private static AutostartKnownCatalog Load() => AutostartKnownCatalog.Load(BuiltInCatalog.AutostartKnownSource.Json);

    [Fact]
    public void BuiltIn_Loads_SixteenEntries_IdsInTableOrder()
    {
        var catalog = Load();

        Assert.Equal(16, catalog.Entries.Count);
        Assert.Equal(Table.Select(row => row.Id), catalog.Entries.Select(e => e.Id));
    }

    [Fact]
    public void BuiltIn_RulesMatchTable()
    {
        var entries = Load().Entries;

        Assert.Equal(Table.Length, entries.Count);
        Assert.All(Table.Zip(entries, (row, entry) => (row, entry)), pair =>
        {
            var (row, entry) = pair;
            Assert.Equal(row.Verdict, entry.Verdict);
            Assert.Equal(row.File, entry.Match.File);
            Assert.Equal(row.Folders, entry.Match.Folders);
            Assert.Equal(row.Publishers, entry.Match.Publishers);
            Assert.Equal(row.ValueName, entry.Match.ValueName);
            Assert.Null(entry.Match.PackageFamily);
            Assert.True(row.Confirmed == entry.Confirmed, entry.Id + ": confirmed");
        });
    }

    [Fact]
    public void BuiltIn_SourcesExist()
    {
        var problems = new List<string>();
        foreach (var entry in Load().Entries)
        {
            Assert.NotEmpty(entry.Sources);
            foreach (var source in entry.Sources)
            {
                if (source.StartsWith("docs/", StringComparison.Ordinal))
                {
                    if (!File.Exists(RepoRoot.Combine(source.Split('/'))))
                    {
                        problems.Add($"{entry.Id}: brak pliku {source}");
                    }
                }
                else if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                {
                    problems.Add($"{entry.Id}: zły adres {source}");
                }
            }

            if (!entry.Sources.Any(s => s.StartsWith("https://", StringComparison.Ordinal)))
            {
                problems.Add($"{entry.Id}: brak adresu https");
            }
        }

        Assert.True(problems.Count == 0, string.Join(System.Environment.NewLine, problems));
    }

    [Fact]
    public void BuiltIn_CanDisable_ConfirmedWithVmAndEffectSources()
    {
        var entries = Load().Entries;
        var canDisable = entries.Where(e => e.Verdict == AutostartVerdict.CanDisable).ToList();

        Assert.Equal(6, canDisable.Count);
        Assert.All(canDisable, e =>
        {
            Assert.True(e.Confirmed, e.Id);
            Assert.Contains(ProgramsDocument, e.Sources);
            Assert.Contains(e.Sources, s => s.StartsWith("https://", StringComparison.Ordinal));
        });
        Assert.Contains(StartupDocument, entries.Single(e => e.Id == "security-health").Sources);
        Assert.All(entries.Where(e => e.Verdict == AutostartVerdict.Protected && e.Id != "security-health"), e => Assert.False(e.Confirmed, e.Id));
    }

    [Fact]
    public void BuiltIn_EveryEntryHasReadmeRow()
    {
        var readme = File.ReadAllLines(RepoRoot.Combine("docs", "autostart", "README.md"), Encoding.UTF8);
        var problems = new List<string>();
        foreach (var entry in Load().Entries)
        {
            var row = readme.SingleOrDefault(line => line.StartsWith("| `" + entry.Id + "` |", StringComparison.Ordinal));
            if (row is null)
            {
                problems.Add($"{entry.Id}: brak wiersza w tabeli");
                continue;
            }

            problems.AddRange(entry.Sources.Where(s => row.IndexOf(s, StringComparison.Ordinal) < 0).Select(s => $"{entry.Id}: wiersz bez źródła {s}"));
        }

        Assert.True(problems.Count == 0, string.Join(System.Environment.NewLine, problems));
    }

    [Fact]
    public void BuiltIn_EaAppSummary_SaysServiceKeepsRunning()
    {
        Assert.Contains("nie zatrzymuje usługi", Load().Entries.Single(e => e.Id == "ea-app").Summary, StringComparison.Ordinal);

        AutostartKnownCatalog english;
        using (L.Use(UiLanguage.English))
        {
            english = Load();
        }

        Assert.Contains("doesn't stop", english.Entries.Single(e => e.Id == "ea-app").Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", "Valve Corporation", "Steam", "steam")]
    [InlineData(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", "Epic Games, Inc.", "EpicGamesLauncher", "epic-games-launcher")]
    [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe", "Blizzard Entertainment", "Battle.net", "battle-net")]
    [InlineData(@"C:\Users\dso\AppData\Roaming\Spotify\Spotify.exe", "Spotify Ltd", "Spotify", "spotify")]
    [InlineData(@"C:\Users\dso\AppData\Local\Discord\Discord.exe", "Discord Inc.", "Discord", "discord")]
    [InlineData(@"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EALauncher.exe", "Electronic Arts", "EADM", "ea-app")]
    [InlineData(@"C:\Windows\System32\SecurityHealthSystray.exe", "Microsoft Corporation", "SecurityHealth", "security-health")]
    public void BuiltIn_MeasuredPaths_Verdict(string file, string publisher, string valueName, string expectedId)
    {
        // Opis pliku (nazwa wiersza) nie bierze udziału w dopasowaniu: Battle.net ma w nim U+2024 zamiast kropki.
        var entry = Run(file, publisher, valueName) with { Name = "Battle\u2024net" };

        Assert.Equal(expectedId, Load().Match(entry, Environment)?.Id);
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Kaspersky Lab\Kaspersky 21.3\avp.exe", "AO Kaspersky Lab", "kaspersky")]
    [InlineData(@"C:\Program Files\Avast Software\Avast\AvastUI.exe", "Gen Digital Inc.", "avast")]
    [InlineData(@"C:\Program Files\Avast Software\Avast\AvastUI.exe", "AVAST Software s.r.o.", "avast")]
    [InlineData(@"C:\Program Files\Bitdefender\Bitdefender Security App\bdagent.exe", "Bitdefender SRL", "bitdefender")]
    [InlineData(@"C:\Program Files\LGHUB\lghub.exe", "Logitech Europe S.A.", "logitech-g-hub")]
    [InlineData(@"C:\Program Files (x86)\ASUS\ATK Package\ATK Hotkey\HControl.exe", "ASUSTeK Computer Inc.", "asus-hotkeys")]
    [InlineData(@"C:\Windows\System32\DriverStore\FileRepository\realtekservice.inf_amd64_1\RtkAudUService64.exe", "Realtek Semiconductor Corp.", "realtek-audio-service")]
    [InlineData(@"C:\Program Files\Elantech\ETDCtrlHelper.exe", "ELAN Microelectronics Corp.", "elan-touchpad")]
    public void BuiltIn_ProtectedInAdminFolders_Lock(string file, string publisher, string expectedId)
    {
        var known = Load().Match(Run(file, publisher, "x"), Environment);

        Assert.Equal(expectedId, known?.Id);
        Assert.Equal(AutostartVerdict.Protected, known!.Verdict);
    }

    [Theory]
    [InlineData(@"C:\Users\dso\AppData\Local\Temp\SecurityHealthSystray.exe", "Microsoft Corporation", "SecurityHealth")]
    [InlineData(@"C:\Users\dso\AppData\Roaming\steam.exe", "Valve Corporation", "Steam")]
    [InlineData(@"C:\Users\dso\AppData\Local\Temp\avp.exe", "AO Kaspersky Lab", "x")]
    [InlineData(@"C:\ProgramData\Avast\AvastUI.exe", "Gen Digital Inc.", "x")]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", "Contoso", "Steam")]
    public void SpoofedPaths_NoVerdict(string file, string publisher, string valueName)
    {
        Assert.Null(Load().Match(Run(file, publisher, valueName), Environment));
    }

    [Fact]
    public void DiscordMachineInstaller_NoVerdict()
    {
        // Wpis HKLM Run 32-bit instalatora maszynowego: C:\ProgramData\SquirrelMachineInstalls\Discord.exe --checkInstall.
        var entry = Run(@"C:\ProgramData\SquirrelMachineInstalls\Discord.exe", "Discord Inc.", "Discord") with
        {
            Source = AutostartSource.Run32,
            Command = @"C:\ProgramData\SquirrelMachineInstalls\Discord.exe --checkInstall",
        };

        Assert.Null(Load().Match(entry, Environment));
    }

    [Theory]
    [InlineData(@"C:\Users\dso\AppData\Local\Microsoft\OneDrive\OneDrive.exe", "Microsoft Corporation", "OneDrive")]
    [InlineData(@"C:\Users\dso\AppData\Local\Microsoft\WindowsApps\MSTeams_8wekyb3d8bbwe\ms-teams.exe", "Microsoft Corporation", "Teams")]
    [InlineData(@"C:\Program Files (x86)\Dropbox\Client\Dropbox.exe", "Dropbox, Inc.", "Dropbox")]
    [InlineData(@"C:\Program Files\Google\Drive File Stream\133.0.2.0\GoogleDriveFS.exe", "Google LLC.", "GoogleDriveFS")]
    [InlineData(@"C:\Program Files (x86)\Common Files\Java\Java Update\jusched.exe", "Oracle Corporation", "SunJavaUpdateSched")]
    [InlineData(@"C:\Program Files\iTunes\iTunesHelper.exe", "Apple Inc.", "iTunesHelper")]
    public void IntentionallyUnassessed_NoVerdict(string file, string publisher, string valueName)
    {
        Assert.Null(Load().Match(Run(file, publisher, valueName), Environment));
    }

    private static AutostartEntry Run(string file, string publisher, string valueName)
        => AutostartKnownCatalogTests.Entry(file, publisher) with { EntryName = valueName, Name = valueName, Command = "\"" + file + "\"" };
}
