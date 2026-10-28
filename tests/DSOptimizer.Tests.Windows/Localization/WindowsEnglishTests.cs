using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Scripts;
using DSOptimizer.Core.Settings;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using DSOptimizer.Windows.Apps;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Windows.Localization;

/// <summary>
/// Teksty warstwy Windows po angielsku: dokładne zdania z <c>windows.json</c>. Tylko piaskownica (HKCU\Software\DSOptimizer.Tests\&lt;guid&gt;,
/// <c>%TEMP%\dso-tests</c>), serwer pętli zwrotnej i fałszywe uruchamiacze – żaden restart, winget ani prawdziwa sieć.
/// </summary>
[Trait("Category", "Windows")]
public class WindowsEnglishTests
{
    private const string FirstId = "test.lang-a";
    private const string SecondId = "test.lang-b";

    /// <summary>Pola raportu CLI z tekstem dla człowieka – jedyne, które mogą się różnić między <c>--lang pl</c> i <c>--lang en</c>.</summary>
    private static readonly HashSet<string> TextFields = new(StringComparer.Ordinal) { "message", "warnings", "notRestored", "errors" };

    /// <summary>Stemple czasu przebiegu (różne przy każdym uruchomieniu, niezależnie od języka).</summary>
    private static readonly HashSet<string> TimeFields = new(StringComparer.Ordinal) { "startedAt", "finishedAt" };

    [Fact]
    public void Run_English_SameKeysOutcomesAndExitCodes()
    {
        using var sandbox = new RegistrySandbox();
        using var temp = new TempDirectory();
        using (var key = sandbox.OpenOrCreate())
        {
            key.SetValue("B", 1, Microsoft.Win32.RegistryValueKind.DWord);
        }

        var catalog = WriteCatalog(temp, sandbox);
        (int Code, JObject Json) Detect(string id, string language)
        {
            var output = temp.Sub($"{id}-{language}-{Guid.NewGuid():N}.json");
            var code = CliHost.Run(new[] { "--detect", id, "--catalog", catalog, "--out", output, "--lang", language }, temp.AppRoot, null);
            return (code, JObject.Parse(File.ReadAllText(output)));
        }

        foreach (var id in new[] { FirstId, SecondId })
        {
            var polish = Detect(id, "pl");
            var english = Detect(id, "en");

            Assert.Equal(polish.Code, english.Code);
            AssertSameExceptText(polish.Json, english.Json, id);
            Assert.Contains(polish.Json["warnings"]!.Children(), w => ((string)w!).StartsWith("Użyto zewnętrznego katalogu: ", StringComparison.Ordinal));
            Assert.Contains(english.Json["warnings"]!.Children(), w => ((string)w!).StartsWith("An external catalog was used: ", StringComparison.Ordinal));
        }

        Assert.Equal("NotApplied", (string)Detect(FirstId, "en").Json["state"]!);
        Assert.Equal("Applied", (string)Detect(SecondId, "en").Json["state"]!);
    }

    [Fact]
    public void Journal_UntrustedWarning_English()
    {
        using var temp = new TempDirectory();
        var store = new JsonFileJournalStore(temp.AppRoot, "PC-01", new FixedClock());
        store.Save(new JournalDocument { Machine = "PC-01" });
        var foreign = new FileSecurity();
        foreign.SetOwner(WindowsIdentity.GetCurrent().User!);
        File.SetAccessControl(store.FilePath, foreign);

        JournalLoadResult result;
        using (L.Use(UiLanguage.English))
        {
            result = store.Load();
        }

        var aside = Assert.Single(Directory.GetFiles(temp.Sub(@"app\journal"), "journal.json.untrusted-*"));
        Assert.Equal($"The journal had an untrusted owner or permissions and was set aside: {aside}. A new one was created.", result.Warning);
    }

    [Fact]
    public void Journal_Corrupt_English()
    {
        using var temp = new TempDirectory();
        var store = new JsonFileJournalStore(temp.AppRoot, "PC-01", new FixedClock());
        store.Save(new JournalDocument { Machine = "PC-01" });
        File.WriteAllText(store.FilePath, "{ uszkodzony");

        JournalLoadResult result;
        using (L.Use(UiLanguage.English))
        {
            result = store.Load();
        }

        var corrupt = Assert.Single(Directory.GetFiles(temp.Sub(@"app\journal"), "journal.corrupt-*.json"));
        Assert.StartsWith("The journal was damaged (", result.Warning, StringComparison.Ordinal);
        Assert.EndsWith($"). It was moved to {corrupt} and a new one was created.", result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_UntrustedWarning_English()
    {
        using var temp = new TempDirectory();
        var store = new JsonFileSettingsStore(temp.AppRoot, new FixedClock());
        store.Save(new AppSettings { CreateRestorePoint = false });
        var security = File.GetAccessControl(store.FilePath);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        File.SetAccessControl(store.FilePath, security);

        SettingsLoadResult result;
        using (L.Use(UiLanguage.English))
        {
            result = store.Load();
        }

        var aside = Assert.Single(Directory.GetFiles(temp.Sub("app"), "settings.json.untrusted-*"));
        Assert.Equal($"The settings file had an untrusted owner or permissions and was set aside: {aside}. Using default settings.", result.Warning);
    }

    [Fact]
    public void Settings_TooLarge_English()
    {
        using var temp = new TempDirectory();
        var store = new JsonFileSettingsStore(temp.AppRoot, new FixedClock());
        store.Save(AppSettings.Default);
        File.WriteAllText(store.FilePath, new string(' ', JsonFileSettingsStore.MaxFileBytes + 1));

        SettingsLoadResult result;
        using (L.Use(UiLanguage.English))
        {
            result = store.Load();
        }

        Assert.Equal($"The settings file {store.FilePath} is too large (65537 B, limit 65536 B). Using default settings.", result.Warning);
    }

    [Fact]
    public void Settings_Corrupt_English()
    {
        using var temp = new TempDirectory();
        var store = new JsonFileSettingsStore(temp.AppRoot, new FixedClock());
        store.Save(AppSettings.Default);
        File.WriteAllText(store.FilePath, "{");

        SettingsLoadResult result;
        using (L.Use(UiLanguage.English))
        {
            result = store.Load();
        }

        Assert.StartsWith($"The settings file {store.FilePath} is damaged (", result.Warning, StringComparison.Ordinal);
        Assert.EndsWith("). Using default settings; the file will be overwritten on the next change.", result.Warning, StringComparison.Ordinal);
    }

    /// <summary>Plik ustawień we własnym korzeniu testu otwarty na wyłączność: odczyt rzuca IOException (tekst .NET w środku zdania).</summary>
    [Fact]
    public void Settings_Unreadable_English()
    {
        using var temp = new TempDirectory();
        var store = new JsonFileSettingsStore(temp.AppRoot, new FixedClock());
        store.Save(AppSettings.Default);

        SettingsLoadResult result;
        using (new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (L.Use(UiLanguage.English))
        {
            result = store.Load();
        }

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.StartsWith($"Can't read the settings file {store.FilePath}: ", result.Warning, StringComparison.Ordinal);
        Assert.EndsWith(". Using default settings.", result.Warning, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gałąź „brak uprawnień” w Guard: odmowa (Wszyscy) tylko na folderze w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>, zdejmowana w finally;
    /// SeBackup/SeRestore wyłączone w tokenie wątku (omijałyby ACL, np. w Git Bash).
    /// </summary>
    [Fact]
    public void RepairFileSystem_ProbeDenied_English()
    {
        using var temp = new TempDirectory();
        var locked = temp.Sub("zablokowany");
        var child = Path.Combine(locked, "Windows.old");
        Directory.CreateDirectory(child);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var acl = Directory.GetAccessControl(locked);
        acl.AddAccessRule(new FileSystemAccessRule(
            everyone,
            FileSystemRights.ListDirectory | FileSystemRights.ReadAttributes,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny));
        Directory.SetAccessControl(locked, acl);
        try
        {
            OperationException ex;
            using (ThreadPrivileges.Disable(ThreadPrivileges.Backup, ThreadPrivileges.Restore))
            using (L.Use(UiLanguage.English))
            {
                ex = Assert.Throws<OperationException>(() => new WindowsRepairFileSystem().ProbeDirectory(child));
            }

            Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
            Assert.StartsWith($"No permission to check the folder {child}: ", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            var restore = Directory.GetAccessControl(locked);
            restore.RemoveAccessRuleAll(new FileSystemAccessRule(everyone, FileSystemRights.ListDirectory, AccessControlType.Deny));
            Directory.SetAccessControl(locked, restore);
        }
    }

    /// <summary>Termin mija w predykacie przekierowania, więc kolejne żądanie nie startuje – zawsze nasz komunikat, nie tekst .NET.</summary>
    [Fact]
    public void WebDownloader_Timeout_English()
    {
        using var second = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 200, new byte[10]));
        using var first = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 302, location: second.Url("/f.msi").AbsoluteUri));
        using var temp = new TempDirectory();

        WebFile result;
        using (L.Use(UiLanguage.English))
        {
            result = new WebDownloader("DSOptimizer").Download(
                first.Url("/start"), _ => { Thread.Sleep(1500); return true; }, 1024, temp.Sub("out.msi"), TimeSpan.FromSeconds(1), null);
        }

        Assert.Equal(WebFailure.TimedOut, result.Failure);
        Assert.Equal("Timed out.", result.Error);
        Assert.Empty(second.Requests);
    }

    [Fact]
    public void WebDownloader_RedirectRefused_English()
    {
        using var second = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 200, new byte[10]));
        using var first = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 302, location: second.Url("/evil.msi").AbsoluteUri));
        using var temp = new TempDirectory();

        WebFile result;
        using (L.Use(UiLanguage.English))
        {
            result = new WebDownloader("DSOptimizer").Download(first.Url("/start"), _ => false, 1024, temp.Sub("out.msi"), TimeSpan.FromSeconds(20), null);
        }

        Assert.Equal(WebFailure.RedirectRefused, result.Failure);
        Assert.Equal($"Redirect to an address that isn't allowed: {second.Url("/evil.msi").AbsoluteUri}", result.Error);
    }

    /// <summary>Tylko odczyt chronionego klucza (odmowa dostępu), nic nie jest zapisywane.</summary>
    [Fact]
    public void Registry_AccessDenied_English()
    {
        OperationException ex;
        using (L.Use(UiLanguage.English))
        {
            ex = Assert.Throws<OperationException>(() => new WindowsRegistry().SubKeyNames(@"HKLM\SECURITY\Policy"));
        }

        Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
        Assert.StartsWith(@"No permission to read the subkeys of HKLM\SECURITY\Policy: ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairFileSystem_MoveMissingSource_English()
    {
        using var temp = new TempDirectory();
        var source = temp.Sub("brak");
        var destination = temp.Sub("cel");

        OperationException ex;
        using (L.Use(UiLanguage.English))
        {
            ex = Assert.Throws<OperationException>(() => new WindowsRepairFileSystem().MoveDirectory(source, destination));
        }

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
        Assert.Equal($"Can't move the folder {source} to {destination}: The source folder doesn't exist: {source}", ex.Message);
    }

    /// <summary>Nieistniejący plik w <c>%TEMP%\dso-tests</c>: CreateProcess kończy się błędem 2, nic nie startuje.</summary>
    [Fact]
    public void RedirectedProcess_StartFailed_English()
    {
        using var temp = new TempDirectory();
        var (read, write) = RedirectedProcess.CreateOutputPipe();
        var nul = RedirectedProcess.OpenNul();
        try
        {
            OperationException ex;
            using (L.Use(UiLanguage.English))
            {
                ex = Assert.Throws<OperationException>(() => RedirectedProcess.Start(
                    temp.Sub("dso-missing.exe"), new ToolRequest { FileName = "dso-missing.exe" }, temp.Root, nul, write, 0));
            }

            Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
            Assert.StartsWith("Can't start dso-missing.exe: ", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            RedirectedProcess.CloseIfSet(nul);
            RedirectedProcess.CloseIfSet(write);
            RedirectedProcess.CloseIfSet(read);
        }
    }

    [Fact]
    public void Relauncher_LogLinesAndComponent_English()
    {
        var events = new List<string>();
        var relauncher = new ProgramRelauncher(new RecordingLog(events), () => @"C:\x\DSOptimizer.exe", (_, _, _) => throw new Win32Exception("Access is denied"));

        using (L.Use(UiLanguage.English))
        {
            Assert.Throws<OperationException>(() => relauncher.Relaunch());
        }

        Assert.Equal(
            new[] { @"Info Startup: Restarting the program: C:\x\DSOptimizer.exe", "Warn Startup: Restart failed: Access is denied" },
            events);
    }

    [Fact]
    public void Restarter_NonZeroExit_English()
    {
        var events = new List<string>();
        var restarter = new ShutdownRestarter(new RecordingLog(events), (_, _) => 5);

        OperationException ex;
        using (L.Use(UiLanguage.English))
        {
            ex = Assert.Throws<OperationException>(() => restarter.Restart());
        }

        Assert.Equal("Couldn't restart the computer: shutdown.exe exited with code 5.", ex.Message);
        Assert.Equal(
            new[]
            {
                "Info Restart: shutdown.exe /r /t 0 requested by the user.",
                "Info Restart: Couldn't restart the computer: shutdown.exe exited with code 5.",
            },
            events);
    }

    [Fact]
    public void AppxReader_ExitCode_English()
    {
        var reader = new AppxReader(new FailingRunner(), _ => null);

        InvalidOperationException ex;
        using (L.Use(UiLanguage.English))
        {
            ex = Assert.Throws<InvalidOperationException>(() => reader.Read("S-1-5-21-0-0-0-1000"));
        }

        Assert.Equal("The AppX read script exited with code 3.", ex.Message);
    }

    /// <summary>Błąd formatu <c>--apps-catalog</c>: komunikat po angielsku, fabryka usług (winget) nigdy nie jest wołana.</summary>
    [Fact]
    public void Cli_InvalidAppsCatalog_English()
    {
        using var temp = new TempDirectory();
        var apps = temp.Sub("apps.json");
        File.WriteAllText(apps, "{ \"entries\": ", new UTF8Encoding(false));
        var output = temp.Sub("wynik.json");

        var code = CliHost.Run(
            new[] { "--list-install", "--apps-catalog", apps, "--out", output, "--lang", "en" },
            temp.AppRoot,
            null,
            File.ReadAllText,
            () => BuiltInCatalog.Sources,
            null,
            (_, _) => throw new InvalidOperationException("fabryka nie powinna być wołana"));

        Assert.Equal(3, code);
        var error = (string)JObject.Parse(File.ReadAllText(output))["errors"]![0]!;
        Assert.StartsWith($"Invalid app list {apps}: ", error, StringComparison.Ordinal);
    }

    /// <summary>Te same klucze w tej samej kolejności i te same wartości; pola tekstowe i stemple czasu tylko tego samego typu (i liczby elementów).</summary>
    private static void AssertSameExceptText(JToken polish, JToken english, string path)
    {
        Assert.True(polish.Type == english.Type, $"{path}: {polish.Type} vs {english.Type}");
        if (polish is JObject polishObject)
        {
            var englishObject = (JObject)english;
            Assert.Equal(polishObject.Properties().Select(p => p.Name).ToArray(), englishObject.Properties().Select(p => p.Name).ToArray());
            foreach (var property in polishObject.Properties())
            {
                var other = englishObject[property.Name]!;
                var childPath = path + "." + property.Name;
                if (TextFields.Contains(property.Name))
                {
                    Assert.True(property.Value.Type == other.Type, $"{childPath}: {property.Value.Type} vs {other.Type}");
                    if (property.Value is JArray texts)
                    {
                        Assert.Equal(texts.Count, ((JArray)other).Count);
                        Assert.All(texts.Concat(other), t => Assert.Equal(JTokenType.String, t.Type));
                    }
                }
                else if (TimeFields.Contains(property.Name))
                {
                    Assert.True(property.Value.Type == other.Type, $"{childPath}: {property.Value.Type} vs {other.Type}");
                }
                else
                {
                    AssertSameExceptText(property.Value, other, childPath);
                }
            }

            return;
        }

        if (polish is JArray polishArray)
        {
            var englishArray = (JArray)english;
            Assert.Equal(polishArray.Count, englishArray.Count);
            for (var i = 0; i < polishArray.Count; i++)
            {
                AssertSameExceptText(polishArray[i], englishArray[i], $"{path}[{i}]");
            }

            return;
        }

        Assert.True(JToken.DeepEquals(polish, english), $"{path}: {polish} vs {english}");
    }

    private static string WriteCatalog(TempDirectory temp, RegistrySandbox sandbox)
    {
        JObject Tweak(string id, string name) => new()
        {
            ["id"] = id,
            ["category"] = "test",
            ["kind"] = "tweak",
            ["title"] = "Test języka " + name,
            ["summary"] = "Opis testowy.",
            ["benefit"] = "Zysk testowy.",
            ["risk"] = "safe",
            ["scope"] = "user",
            ["registry"] = new JArray(new JObject
            {
                ["path"] = sandbox.HkcuPath,
                ["name"] = name,
                ["type"] = "DWord",
                ["value"] = 1,
                ["original"] = "<RemoveEntry>",
            }),
        };
        var path = temp.Sub("katalog-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, new JArray(Tweak(FirstId, "A"), Tweak(SecondId, "B")).ToString(), new UTF8Encoding(true));
        return path;
    }

    private sealed class FailingRunner : IScriptRunner
    {
        public ScriptResult Run(ScriptRequest request, IProgress<string>? output, CancellationToken cancellationToken)
            => new() { ExitCode = 3, Output = Array.Empty<string>(), Errors = Array.Empty<string>() };
    }

    private sealed class RecordingLog : ILog
    {
        private readonly List<string> _events;

        public RecordingLog(List<string> events) => _events = events;

        public void Info(string component, string message) => _events.Add($"Info {component}: {message}");

        public void Warn(string component, string message) => _events.Add($"Warn {component}: {message}");

        public void Fail(string component, string message, Exception? exception = null) => _events.Add($"Fail {component}: {message}");
    }
}
