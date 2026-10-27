using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Tools;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class AppCompositionTests
{
    [Fact]
    public void Create_FreshRoot_RootTrustedAndNoWarnings()
    {
        using var temp = new TempDirectory();
        var composition = AppComposition.Create(temp.AppRoot, null);
        Assert.Empty(composition.StartupWarnings);
        Assert.True(SecureDirectory.IsTrustedDirectory(temp.Sub("app")));
    }

    // Tylko składanie: handlery napraw nie są uruchamiane (Run nigdy nie jest wołane na hoście).
    [Fact]
    public void Create_RegistersEveryCatalogHandler_AndBackupsDirectoryIsNotCreatedAtStart()
    {
        using var temp = new TempDirectory();
        var composition = AppComposition.Create(temp.AppRoot, null);

        var names = composition.Services.Handlers.Names;
        Assert.Contains(ActionHandlerNames.WuReset, names);
        Assert.Contains(ActionHandlerNames.WuResetFull, names);
        foreach (var definition in CatalogLoader.Load(BuiltInCatalog.Sources).Definitions.Where(d => d.Handler is not null))
        {
            Assert.Contains(definition.Handler!, names);
        }

        Assert.False(Directory.Exists(temp.AppRoot.BackupsDirectory));
    }

    // Tylko składanie: winget nie jest uruchamiany (Run nigdy nie jest wołane), a folder logów winget nie powstaje przy starcie.
    [Fact]
    public void Create_ExposesWingetRunnerAndLogFolder_WithoutCreatingWingetLogs()
    {
        using var temp = new TempDirectory();
        var composition = AppComposition.Create(temp.AppRoot, null);

        Assert.IsType<WingetRunner>(composition.Winget);
        Assert.IsType<WingetLogFolder>(composition.WingetLogs);
        Assert.False(Directory.Exists(Path.Combine(temp.AppRoot.LogsDirectory, "winget")));
        Assert.StartsWith(
            Path.Combine(temp.AppRoot.LogsDirectory, "winget") + Path.DirectorySeparatorChar,
            composition.WingetLogs.NewLogPath("Mozilla.Firefox", DateTimeOffset.UtcNow),
            StringComparison.OrdinalIgnoreCase);
    }

    // Tylko składanie: nic nie jest pobierane ani uruchamiane, folder pobrań nie powstaje przy starcie.
    [Fact]
    public void Create_GitHubComposed_DownloadsNotCreated()
    {
        using var temp = new TempDirectory();
        var composition = AppComposition.Create(temp.AppRoot, null);

        var gitHub = composition.GitHub;
        Assert.IsType<WebDownloader>(gitHub.Web);
        Assert.IsType<AuthenticodeVerifier>(gitHub.Signatures);
        Assert.IsType<MsiexecInstaller>(gitHub.Msi);
        Assert.IsType<DownloadFolder>(gitHub.Files);
        Assert.False(Directory.Exists(temp.AppRoot.DownloadsDirectory));
        Assert.Equal(temp.AppRoot.DownloadsDirectory, Path.GetDirectoryName(gitHub.Files.NewDownloadPath(".msi")));
    }

    // Tylko składanie: handler WinUtil nie jest uruchamiany (Run nigdy nie jest wołane na hoście) – nic nie jest pobierane ani otwierane.
    [Fact]
    public void AppComposition_HandlersContainWinUtil()
    {
        using var temp = new TempDirectory();
        var composition = AppComposition.Create(temp.AppRoot, null);

        Assert.True(composition.Services.Handlers.TryGet(ActionHandlerNames.WinUtil, out var handler));
        var winUtil = Assert.IsType<WinUtilHandler>(handler);
        Assert.Same(composition.GitHub.Web, winUtil.Services.Web);
        Assert.Same(composition.GitHub.Files, winUtil.Services.Files);
        Assert.Same(composition.Services.Launcher, winUtil.Services.Launcher);
        Assert.Same(composition.Log, winUtil.Services.Log);
        Assert.False(Directory.Exists(temp.AppRoot.DownloadsDirectory));
    }

    [Fact]
    public void Create_FileLogIsNotTee()
    {
        using var temp = new TempDirectory();
        var observer = new CollectingLog();
        var composition = AppComposition.Create(temp.AppRoot, null, observer);

        composition.FileLog.Info("Test", "tylko plik");
        composition.Log.Info("Test", "przez tee");

        Assert.DoesNotContain(observer.Lines, l => l.Contains("tylko plik"));
        Assert.Contains(observer.Lines, l => l.Contains("przez tee"));
    }

    [Fact]
    public void Create_UntrustedRoot_SetsItAsideBeforeLeaves_AndWarns()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub(@"app\logs"));
        File.WriteAllText(temp.Sub(@"app\logs\stary.log"), "x");
        ForeignAcl.Apply(temp.Sub("app"));

        var composition = AppComposition.Create(temp.AppRoot, null);
        composition.Log.Info("Test", "po starcie");

        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.True(File.Exists(Path.Combine(aside, @"logs\stary.log")));
        Assert.Contains(composition.StartupWarnings, w => w.Contains(aside));
        Assert.True(SecureDirectory.IsTrustedDirectory(temp.Sub("app")));
        Assert.NotEmpty(Directory.GetFiles(temp.Sub(@"app\logs"), "dsoptimizer-*.log"));
    }

    [Fact]
    public void Create_UntrustedJournalLeaf_WarningFromJournalIncluded()
    {
        using var temp = new TempDirectory();
        new JsonFileJournalStore(temp.AppRoot, "PC-01", new FixedClock()).Save(new JournalDocument { Machine = "PC-01" });
        ForeignAcl.Apply(temp.Sub(@"app\journal"));

        var composition = AppComposition.Create(temp.AppRoot, null);

        Assert.Contains(composition.StartupWarnings, w => w.Contains("journal.untrusted-"));
    }

    [Fact]
    public void Create_JournalLeafIsJunction_WarnsThatJournalCannotBeRead()
    {
        using var temp = new TempDirectory();
        temp.AppRoot.Ensure();
        Directory.CreateDirectory(temp.Sub("cel"));
        Junction.Create(temp.Sub(@"app\journal"), temp.Sub("cel"));
        try
        {
            var composition = AppComposition.Create(temp.AppRoot, null);
            var journalPath = Assert.IsType<JsonFileJournalStore>(composition.Services.Journal).FilePath;

            const string Phrase = "Nie można odczytać journala";
            var warning = Assert.Single(composition.StartupWarnings, w => w.Contains(Phrase));
            Assert.StartsWith($"{Phrase} {journalPath}: ", warning, StringComparison.Ordinal);
            Assert.Equal(warning.IndexOf(Phrase, StringComparison.Ordinal), warning.LastIndexOf(Phrase, StringComparison.Ordinal));
            Assert.Empty(Directory.GetFileSystemEntries(temp.Sub("cel")));
        }
        finally
        {
            Directory.Delete(temp.Sub(@"app\journal"), recursive: false);
        }
    }

    [Fact]
    public void Create_RootIsJunction_ThrowsIOException()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("cel"));
        Junction.Create(temp.Sub("app"), temp.Sub("cel"));
        try
        {
            Assert.ThrowsAny<IOException>(() => AppComposition.Create(temp.AppRoot, null));
            Assert.Empty(Directory.GetFileSystemEntries(temp.Sub("cel")));
        }
        finally
        {
            Directory.Delete(temp.Sub("app"), recursive: false);
        }
    }

    [Fact]
    public void Create_WiresJournalUnderRoot()
    {
        using var temp = new TempDirectory();
        var journal = Assert.IsType<JsonFileJournalStore>(AppComposition.Create(temp.AppRoot, null).Services.Journal);
        Assert.StartsWith(temp.AppRoot.JournalDirectory, journal.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WithObserver_ForwardsLogAndStartupWarnings()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("app"));
        ForeignAcl.Apply(temp.Sub("app"));
        var observer = new CollectingLog();

        var composition = AppComposition.Create(temp.AppRoot, null, observer);
        composition.Services.Log.Info("Test", "wpis silnika");
        composition.Log.Info("Test", "wpis okna");

        Assert.Contains(observer.Lines, l => l.StartsWith("WARN Start:", StringComparison.Ordinal) && l.Contains("app.untrusted-"));
        Assert.Contains("INFO Test: wpis silnika", observer.Lines);
        Assert.Contains("INFO Test: wpis okna", observer.Lines);
        Assert.NotEmpty(Directory.GetFiles(temp.Sub(@"app\logs"), "dsoptimizer-*.log"));
    }

    private sealed class CollectingLog : ILog
    {
        public List<string> Lines { get; } = new();

        public void Info(string component, string message) => Lines.Add($"INFO {component}: {message}");

        public void Warn(string component, string message) => Lines.Add($"WARN {component}: {message}");

        public void Fail(string component, string message, Exception? exception = null) => Lines.Add($"FAIL {component}: {message}");
    }
}
