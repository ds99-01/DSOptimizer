using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Storage;
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
    public void Create_UntrustedRootWithoutHistory_InfoOnly()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub(@"app\logs"));
        File.WriteAllText(temp.Sub(@"app\logs\stary.log"), "x");
        ForeignAcl.Apply(temp.Sub("app"));

        var composition = AppComposition.Create(temp.AppRoot, null);
        composition.Log.Info("Test", "po starcie");

        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.True(File.Exists(Path.Combine(aside, @"logs\stary.log")));
        Assert.Empty(composition.StartupWarnings);
        Assert.True(SecureDirectory.IsTrustedDirectory(temp.Sub("app")));
        Assert.Contains("[INFO] [Start] " + SetAsideText.RootMoved(aside), LogText(temp.AppRoot), StringComparison.Ordinal);
        Assert.False(File.Exists(JournalPath(temp.AppRoot)));
    }

    // Spec E: odłożenie z nietkniętą historią i ustawieniami nie daje okna; oba pliki przechodzą bajt w bajt.
    [Fact]
    public void Create_UntrustedRootWithHistoryAndSettings_MovedWithoutStartupWarning()
    {
        using var temp = new TempDirectory();
        HistoryFiles.WriteJournal(temp.Sub("app"), Environment.MachineName);
        HistoryFiles.WriteSettings(temp.Sub("app"), UiLanguage.English);
        ForeignAcl.Apply(temp.Sub("app"));

        var composition = AppComposition.Create(temp.AppRoot, null);

        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.Empty(composition.StartupWarnings);
        Assert.Equal(File.ReadAllBytes(Path.Combine(aside, "journal", "journal.json")), File.ReadAllBytes(JournalPath(temp.AppRoot)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(aside, "settings.json")), File.ReadAllBytes(temp.Sub(@"app\settings.json")));
        var log = LogText(temp.AppRoot);
        Assert.Contains("[INFO] [Start] " + SetAsideText.RootMoved(aside), log, StringComparison.Ordinal);
        Assert.Contains("[INFO] [Start] " + SetAsideText.HistoryMoved, log, StringComparison.Ordinal);
        Assert.Contains("[INFO] [Start] " + SetAsideText.SettingsMoved, log, StringComparison.Ordinal);
        Assert.DoesNotContain("[WARN] [Start]", log, StringComparison.Ordinal);
    }

    // Review Focus 2: komputer przemianowany po zapisaniu historii, potem odłożenie folderu.
    [Fact]
    public void Create_UntrustedRootJournalFromOtherComputer_OneHistoryLostWarning_ThenSilent()
    {
        using var temp = new TempDirectory();
        HistoryFiles.WriteJournal(temp.Sub("app"), "PC-01");
        ForeignAcl.Apply(temp.Sub("app"));

        var composition = AppComposition.Create(temp.AppRoot, null);

        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.Equal(new[] { SetAsideText.HistoryLost(aside) }, composition.StartupWarnings);
        var warning = Assert.Single(composition.StartupWarnings);
        Assert.Equal(warning.IndexOf(aside, StringComparison.Ordinal), warning.LastIndexOf(aside, StringComparison.Ordinal));
        Assert.Empty(ReadJournal(temp.AppRoot).Entries);

        var second = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(second.StartupWarnings);
    }

    [Fact]
    public void Create_UntrustedRootJournalWithUsersAce_HistoryLostWarning()
    {
        using var temp = new TempDirectory();
        var journal = HistoryFiles.WriteJournal(temp.Sub("app"), Environment.MachineName);
        ProtectedFileReaderTests.AddUsersRead(journal);
        ForeignAcl.Apply(temp.Sub("app"));

        var composition = AppComposition.Create(temp.AppRoot, null);

        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.Equal(new[] { SetAsideText.HistoryLost(aside) }, composition.StartupWarnings);
        var source = Path.Combine(aside, "journal", "journal.json");
        Assert.Contains(
            "[WARN] [Start] " + SetAsideText.HistoryNotMoved(source, SetAsideText.Reason(SalvageRejection.UntrustedAcl, null)),
            LogText(temp.AppRoot),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Create_UntrustedRootCorruptSettings_WarnLineOnly()
    {
        using var temp = new TempDirectory();
        HistoryFiles.WriteProtected(temp.Sub(@"app\settings.json"), Encoding.UTF8.GetBytes("{"));
        ForeignAcl.Apply(temp.Sub("app"));

        var composition = AppComposition.Create(temp.AppRoot, null);

        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.Empty(composition.StartupWarnings);
        Assert.False(File.Exists(temp.Sub(@"app\settings.json")));
        const char Marker = '\u0001';
        var parts = SetAsideText.SettingsNotMoved(
            Path.Combine(aside, "settings.json"), SetAsideText.Reason(SalvageRejection.Corrupted, Marker.ToString())).Split(Marker);
        var prefix = "[WARN] [Start] " + parts[0];
        var line = Assert.Single(LogLines(temp.AppRoot), l => l.Contains(prefix));
        Assert.EndsWith(parts[1], line, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_SecondStartAfterRootAside_NoWarnings()
    {
        using var temp = new TempDirectory();
        HistoryFiles.WriteJournal(temp.Sub("app"), Environment.MachineName);
        ForeignAcl.Apply(temp.Sub("app"));
        var first = AppComposition.Create(temp.AppRoot, null);

        var second = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(first.StartupWarnings);
        Assert.Empty(second.StartupWarnings);
        Assert.Single(ReadJournal(temp.AppRoot).Entries);
    }

    // Migracja z 4.0.7: historia z dawnego odłożonego folderu wraca do korzenia bez journala, raz.
    [Fact]
    public void Create_OldAsideWithHistory_NoCurrentJournal_RecoveredOnce()
    {
        using var temp = new TempDirectory();
        var source = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-20261101000000000-abcd1234"), Environment.MachineName);
        temp.AppRoot.Ensure();

        var composition = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(composition.StartupWarnings);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(JournalPath(temp.AppRoot)));
        var expected = "[INFO] [Start] " + SetAsideText.HistoryRestored(source);
        Assert.Single(LogLines(temp.AppRoot), l => l.Contains(expected));

        var second = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(second.StartupWarnings);
        Assert.Single(LogLines(temp.AppRoot), l => l.Contains(expected));
    }

    [Fact]
    public void Create_OldAsideRecovered_UntrustedJournalLeafSetAside_WarnLine()
    {
        using var temp = new TempDirectory();
        var source = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-20261101000000000-abcd1234"), Environment.MachineName);
        temp.AppRoot.Ensure();
        Directory.CreateDirectory(temp.AppRoot.JournalDirectory);
        ForeignAcl.Apply(temp.AppRoot.JournalDirectory);

        var composition = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(composition.StartupWarnings);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(JournalPath(temp.AppRoot)));
        var leafAside = Assert.Single(Directory.GetDirectories(temp.AppRoot.FullPath, "journal.untrusted-*"));
        Assert.Single(LogLines(temp.AppRoot), l => l.Contains("[WARN] [Start] " + SetAsideText.Folder(leafAside)));
    }

    [Fact]
    public void Create_OldAsideRejectedOnContent_HistoryLostOnce_EmptyJournalWritten()
    {
        using var temp = new TempDirectory();
        var oldAside = temp.Sub("app.untrusted-20261101000000000-abcd1234");
        HistoryFiles.WriteJournal(oldAside, "PC-01");
        temp.AppRoot.Ensure();

        var composition = AppComposition.Create(temp.AppRoot, null);

        Assert.Equal(new[] { SetAsideText.HistoryLost(oldAside) }, composition.StartupWarnings);
        Assert.Empty(ReadJournal(temp.AppRoot).Entries);

        var second = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(second.StartupWarnings);
    }

    // Review Focus 4: zwykłe konto zakłada w C:\ProgramData własny folder <korzeń>.untrusted-x z plikiem historii. Folder ma
    // właściciela = to konto, więc ani skan ratowania, ani opis odłożonych journali go nie biorą: bez okna przy każdym starcie.
    [Fact]
    public void Create_UserPlantedOldAside_NoStartupWarning()
    {
        using var temp = new TempDirectory();
        var planted = temp.Sub("app.untrusted-x");
        var source = Path.Combine(planted, "journal", "journal.json");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, JournalSerializer.Serialize(new JournalDocument { Machine = Environment.MachineName }));
        ForeignAcl.Apply(planted);
        temp.AppRoot.Ensure();

        var first = AppComposition.Create(temp.AppRoot, null);
        var second = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(first.StartupWarnings);
        Assert.Empty(second.StartupWarnings);
        Assert.False(File.Exists(JournalPath(temp.AppRoot)));
    }

    // Plik o obcym właścicielu w folderze administratora odpada na samym pliku: tylko WARN, a pusty journal kończy skanowanie.
    [Fact]
    public void Create_OldAsideWithUserOwnedFile_WarnLineOnly_EmptyJournalWritten()
    {
        using var temp = new TempDirectory();
        var source = temp.Sub(@"app.untrusted-x\journal\journal.json");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, JournalSerializer.Serialize(new JournalDocument { Machine = Environment.MachineName }));
        var security = File.GetAccessControl(source);
        security.SetOwner(WindowsIdentity.GetCurrent().User!);
        File.SetAccessControl(source, security);
        temp.AppRoot.Ensure();

        var composition = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(composition.StartupWarnings);
        Assert.Contains(
            "[WARN] [Start] " + SetAsideText.HistoryNotMoved(source, SetAsideText.Reason(SalvageRejection.UntrustedAcl, null)),
            LogText(temp.AppRoot),
            StringComparison.Ordinal);
        Assert.Empty(ReadJournal(temp.AppRoot).Entries);
    }

    // Plik historii chwilowo zablokowany przez inny proces (np. antywirus): decyzja odłożona, bez pustego journala i bez okna,
    // a przy kolejnym starcie historia wraca.
    [Fact]
    public void Create_OldAsideJournalLocked_NoEmptyJournal_RecoveredNextStart()
    {
        using var temp = new TempDirectory();
        var source = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-20261101000000000-abcd1234"), Environment.MachineName);
        temp.AppRoot.Ensure();

        AppComposition first;
        using (new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            first = AppComposition.Create(temp.AppRoot, null);
        }

        Assert.Empty(first.StartupWarnings);
        Assert.False(File.Exists(JournalPath(temp.AppRoot)));
        Assert.Contains("[WARN] [Start] ", LogText(temp.AppRoot), StringComparison.Ordinal);

        var second = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(second.StartupWarnings);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(JournalPath(temp.AppRoot)));
    }

    // Dwa dawne foldery, nowszy plik zablokowany: nic nie przechodzi, bez okna i bez journala; po zwolnieniu wraca NOWSZA historia.
    [Fact]
    public void Create_OldAsidesNewerLocked_Deferred_ThenNewerMoved()
    {
        using var temp = new TempDirectory();
        var older = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-20261101000000000-aaaaaaaa"), Environment.MachineName);
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-20261102000000000-bbbbbbbb"), Environment.MachineName);
        File.AppendAllText(newer, " ");
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        temp.AppRoot.Ensure();

        AppComposition first;
        using (new FileStream(newer, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            first = AppComposition.Create(temp.AppRoot, null);
        }

        Assert.Empty(first.StartupWarnings);
        Assert.False(File.Exists(JournalPath(temp.AppRoot)));

        var second = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(second.StartupWarnings);
        Assert.Equal(File.ReadAllBytes(newer), File.ReadAllBytes(JournalPath(temp.AppRoot)));
    }

    // Korzeń odłożony przy tym starcie (właściciel zostaje Administratorzy, jak po „Kontynuuj”), a jego historia chwilowo
    // zablokowana: bez okna utraty i bez pustego journala; kolejny start przenosi ją ze skanu dawnych folderów.
    [Fact]
    public void Create_UntrustedRootJournalLocked_Deferred_ThenMovedNextStart()
    {
        using var temp = new TempDirectory();
        HistoryFiles.WriteJournal(temp.Sub("app"), Environment.MachineName);
        AddAccountAce(temp.Sub("app"));
        var locker = new LockingLog(temp.Root);

        AppComposition first;
        using (locker)
        {
            first = AppComposition.Create(temp.AppRoot, null, locker);
        }

        Assert.True(locker.Locked);
        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.Empty(first.StartupWarnings);
        Assert.False(File.Exists(JournalPath(temp.AppRoot)));

        var second = AppComposition.Create(temp.AppRoot, null);

        Assert.Empty(second.StartupWarnings);
        Assert.Equal(File.ReadAllBytes(Path.Combine(aside, "journal", "journal.json")), File.ReadAllBytes(JournalPath(temp.AppRoot)));
    }

    // Odłożony korzeń z właścicielem = konto nie wejdzie do skanu dawnych folderów, więc zablokowana historia to utrata (okno raz).
    [Fact]
    public void Create_UserOwnedRootJournalLocked_HistoryLostWarning()
    {
        using var temp = new TempDirectory();
        HistoryFiles.WriteJournal(temp.Sub("app"), Environment.MachineName);
        ForeignAcl.Apply(temp.Sub("app"));
        var locker = new LockingLog(temp.Root);

        AppComposition first;
        using (locker)
        {
            first = AppComposition.Create(temp.AppRoot, null, locker);
        }

        Assert.True(locker.Locked);
        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.Equal(new[] { SetAsideText.HistoryLost(aside) }, first.StartupWarnings);
        Assert.Empty(ReadJournal(temp.AppRoot).Entries);
    }

    // Korzeń zaufany, ale bez prawa zmiany właściciela dla Administratorów: przepisanie deskryptora się nie udaje.
    // Jedna linia WARN przy starcie (mimo wielu prób w trakcie startu), bez okna.
    [Fact]
    public void Create_RootRewriteFails_OneWarnLineWithoutStartupWarning()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        RewriteDenied.Create(root.FullPath);
        try
        {
            var composition = AppComposition.Create(root, null);

            Assert.Empty(composition.StartupWarnings);
            var expected = "[WARN] [Start] " + AppComposition.RewriteFailed(root.FullPath, "\u0001").Split('\u0001')[0];
            Assert.Single(LogLines(root), l => l.Contains(expected));
            Assert.False(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(root.FullPath)));
        }
        finally
        {
            RewriteDenied.Release(root.FullPath);
        }
    }

    [Fact]
    public void Create_CurrentJournalPresent_OldAsidesNotTouched()
    {
        using var temp = new TempDirectory();
        var current = HistoryFiles.WriteJournal(temp.Sub("app"), Environment.MachineName);
        var before = File.ReadAllBytes(current);
        var old = new JsonFileJournalStore(new SecureRoot(temp.Sub("app.untrusted-20261101000000000-abcd1234")), Environment.MachineName, new FixedClock());
        var oldDocument = new JournalDocument { Machine = Environment.MachineName };
        oldDocument.Entries.Add(new JournalEntry { TweakId = "c.d", Status = JournalEntryStatus.Active });
        old.Save(oldDocument);

        var composition = AppComposition.Create(temp.AppRoot, null);
        composition.Log.Info("Test", "po starcie");

        Assert.Empty(composition.StartupWarnings);
        Assert.Equal(before, File.ReadAllBytes(current));
        Assert.DoesNotContain("[INFO] [Start] " + SetAsideText.HistoryRestored(old.FilePath), LogText(temp.AppRoot), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_UntrustedJournalLeaf_WarningFromJournalIncluded()
    {
        using var temp = new TempDirectory();
        new JsonFileJournalStore(temp.AppRoot, "PC-01", new FixedClock()).Save(new JournalDocument { Machine = "PC-01" });
        ForeignAcl.Apply(temp.Sub(@"app\journal"));

        var composition = AppComposition.Create(temp.AppRoot, null);

        var aside = Assert.Single(Directory.GetDirectories(temp.Sub("app"), "journal.untrusted-*"));
        Assert.Equal(new[] { SetAsideText.History(Path.Combine(aside, "journal.json")) }, composition.StartupWarnings);
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

    // Aktualizacja z 4.0.7: korzeń i podfoldery zaufane, ale bez odczytu dla Users – przepisane w miejscu, bez okna.
    [Fact]
    public void Create_TrustedLegacyRootAndLeaves_RewrittenWithoutWarnings()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        foreach (var path in new[] { root.FullPath, root.LogsDirectory, root.JournalDirectory, root.BackupsDirectory })
        {
            LegacyAcl.Create(path);
        }

        var journalPath = Path.Combine(root.JournalDirectory, JsonFileJournalStore.FileName);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
            JournalSerializer.Serialize(new JournalDocument { Machine = Environment.MachineName }));
        using (var stream = new FileStream(
            journalPath, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, SecureDirectory.CreateTrustedFileSecurity()))
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        var composition = AppComposition.Create(root, null);

        Assert.Empty(composition.StartupWarnings);
        foreach (var path in new[] { root.FullPath, root.LogsDirectory, root.JournalDirectory, root.BackupsDirectory })
        {
            Assert.True(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(path)), path);
        }

        Assert.Equal(bytes, File.ReadAllBytes(journalPath));
        Assert.True(AclTrust.IsProtectedAdminOnlyFile(RawDescriptors.OfFile(journalPath)));
        Assert.False(Directory.Exists(root.RunDirectory));
        Assert.False(Directory.Exists(root.DownloadsDirectory));
        Assert.Empty(Directory.GetDirectories(temp.Root, "*.untrusted-*", SearchOption.AllDirectories));
    }

    // Review Focus 1: podfolder z 4.0.7 z ACE konta trafia na bok z jedną linią WARN, bez okna; korzeń zostaje.
    [Fact]
    public void Create_UntrustedBackupsLeaf_WarnLineWithoutStartupWarning()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        root.Ensure();
        Directory.CreateDirectory(root.BackupsDirectory);
        ForeignAcl.Apply(root.BackupsDirectory);

        var composition = AppComposition.Create(root, null);

        Assert.Empty(composition.StartupWarnings);
        var aside = Assert.Single(Directory.GetDirectories(root.FullPath, "backups.untrusted-*"));
        Assert.Empty(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        var logFile = Assert.Single(Directory.GetFiles(root.LogsDirectory, "dsoptimizer-*.log"));
        var expected = "[WARN] [Start] " + SetAsideText.Folder(aside);
        var text = File.ReadAllText(logFile);
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Equal(text.IndexOf(expected, StringComparison.Ordinal), text.LastIndexOf(expected, StringComparison.Ordinal));
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

        Assert.Contains(observer.Lines, l => l.StartsWith("INFO Start:", StringComparison.Ordinal) && l.Contains("app.untrusted-"));
        Assert.Contains("INFO Test: wpis silnika", observer.Lines);
        Assert.Contains("INFO Test: wpis okna", observer.Lines);
        Assert.NotEmpty(Directory.GetFiles(temp.Sub(@"app\logs"), "dsoptimizer-*.log"));
    }

    private static string JournalPath(SecureRoot root) => Path.Combine(root.JournalDirectory, JsonFileJournalStore.FileName);

    private static JournalDocument ReadJournal(SecureRoot root) => JournalSerializer.Deserialize(File.ReadAllText(JournalPath(root)));

    private static string LogText(SecureRoot root) => File.ReadAllText(Assert.Single(Directory.GetFiles(root.LogsDirectory, "dsoptimizer-*.log")));

    private static string[] LogLines(SecureRoot root) => File.ReadAllLines(Assert.Single(Directory.GetFiles(root.LogsDirectory, "dsoptimizer-*.log")));

    private static void AddAccountAce(string directory)
    {
        var security = Directory.GetAccessControl(directory, AccessControlSections.Access);
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        Directory.SetAccessControl(directory, security);
    }

    /// <summary>
    /// Obserwator logu, który przy pierwszym wpisie (korzeń jest już odłożony, ratowanie jeszcze nie ruszyło) blokuje plik historii
    /// w odłożonym korzeniu bez współdzielenia, jak antywirus czytający plik w chwili ratowania.
    /// </summary>
    private sealed class LockingLog : ILog, IDisposable
    {
        private readonly string _parent;
        private FileStream? _lock;

        public LockingLog(string parent) => _parent = parent;

        public bool Locked { get; private set; }

        public void Info(string component, string message) => TryLock();

        public void Warn(string component, string message) => TryLock();

        public void Fail(string component, string message, Exception? exception = null) => TryLock();

        public void Dispose() => _lock?.Dispose();

        private void TryLock()
        {
            if (Locked)
            {
                return;
            }

            foreach (var aside in Directory.GetDirectories(_parent, "app.untrusted-*"))
            {
                var file = Path.Combine(aside, "journal", "journal.json");
                if (File.Exists(file))
                {
                    _lock = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    Locked = true;
                    return;
                }
            }
        }
    }

    private sealed class CollectingLog : ILog
    {
        public List<string> Lines { get; } = new();

        public void Info(string component, string message) => Lines.Add($"INFO {component}: {message}");

        public void Warn(string component, string message) => Lines.Add($"WARN {component}: {message}");

        public void Fail(string component, string message, Exception? exception = null) => Lines.Add($"FAIL {component}: {message}");
    }
}
