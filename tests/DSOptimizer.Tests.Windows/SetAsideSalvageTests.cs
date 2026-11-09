using System;
using System.IO;
using System.Text;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Storage;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class SetAsideSalvageTests
{
    private const string Machine = "PC-01";

    private static SecureRoot EnsuredRoot(TempDirectory temp)
    {
        var root = temp.AppRoot;
        root.Ensure();
        return root;
    }

    private static string RootJournal(SecureRoot root) => Path.Combine(root.JournalDirectory, JsonFileJournalStore.FileName);

    private static string RootSettings(SecureRoot root) => Path.Combine(root.FullPath, JsonFileSettingsStore.FileName);

    private static byte[] Corrupt => Encoding.UTF8.GetBytes("{");

    [Fact]
    public void FromAside_JournalAndSettings_CopiedByteForByteProtected()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        var journal = HistoryFiles.WriteJournal(aside, Machine);
        var settings = HistoryFiles.WriteSettings(aside, UiLanguage.English);

        var result = SetAsideSalvage.FromAside(aside, root, Machine);

        Assert.Equal(new SalvageFile(SalvageOutcome.Moved, journal), result.Journal);
        Assert.Equal(new SalvageFile(SalvageOutcome.Moved, settings), result.Settings);
        Assert.Equal(File.ReadAllBytes(journal), File.ReadAllBytes(RootJournal(root)));
        Assert.Equal(File.ReadAllBytes(settings), File.ReadAllBytes(RootSettings(root)));
        Assert.True(AclTrust.IsProtectedAdminOnlyFile(RawDescriptors.OfFile(RootJournal(root))));
        Assert.True(AclTrust.IsProtectedAdminOnlyFile(RawDescriptors.OfFile(RootSettings(root))));
        Assert.True(File.Exists(journal));
        Assert.True(File.Exists(settings));
    }

    [Fact]
    public void FromAside_NoFiles_BothNotPresent()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        Directory.CreateDirectory(aside);

        var result = SetAsideSalvage.FromAside(aside, root, Machine);

        Assert.Equal(SalvageOutcome.NotPresent, result.Journal.Outcome);
        Assert.Equal(SalvageOutcome.NotPresent, result.Settings.Outcome);
        Assert.Null(result.Journal.Rejection);
        Assert.False(File.Exists(RootJournal(root)));
        Assert.False(File.Exists(RootSettings(root)));
    }

    [Fact]
    public void FromAside_JournalFromOtherComputer_OtherComputer_NothingWritten()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        var journal = HistoryFiles.WriteJournal(aside, "PC-01");

        var result = SetAsideSalvage.FromAside(aside, root, "PC-02");

        Assert.Equal(new SalvageFile(SalvageOutcome.Rejected, journal, SalvageRejection.OtherComputer, "PC-01"), result.Journal);
        Assert.False(File.Exists(RootJournal(root)));
    }

    [Fact]
    public void FromAside_CorruptJournal_Corrupted()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        HistoryFiles.WriteProtected(Path.Combine(aside, "journal", "journal.json"), Corrupt);

        var result = SetAsideSalvage.FromAside(aside, root, Machine);

        Assert.Equal(SalvageOutcome.Rejected, result.Journal.Outcome);
        Assert.Equal(SalvageRejection.Corrupted, result.Journal.Rejection);
        Assert.False(string.IsNullOrWhiteSpace(result.Journal.Detail));
        Assert.False(File.Exists(RootJournal(root)));
    }

    [Fact]
    public void FromAside_CorruptSettings_SettingsRejected_JournalMoved()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        HistoryFiles.WriteJournal(aside, Machine);
        HistoryFiles.WriteProtected(Path.Combine(aside, "settings.json"), Corrupt);

        var result = SetAsideSalvage.FromAside(aside, root, Machine);

        Assert.Equal(SalvageOutcome.Moved, result.Journal.Outcome);
        Assert.Equal(SalvageOutcome.Rejected, result.Settings.Outcome);
        Assert.Equal(SalvageRejection.Corrupted, result.Settings.Rejection);
        Assert.False(File.Exists(RootSettings(root)));
    }

    [Fact]
    public void FromAside_JournalWithUsersAce_UntrustedAcl()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        var journal = HistoryFiles.WriteJournal(aside, Machine);
        ProtectedFileReaderTests.AddUsersRead(journal);

        var result = SetAsideSalvage.FromAside(aside, root, Machine);

        Assert.Equal(new SalvageFile(SalvageOutcome.Rejected, journal, SalvageRejection.UntrustedAcl), result.Journal);
        Assert.False(File.Exists(RootJournal(root)));
    }

    [Fact]
    public void FromAside_AsideRootWithForeignAce_FilesJudgedByOwnAcl()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        HistoryFiles.WriteJournal(aside, Machine);
        HistoryFiles.WriteSettings(aside, UiLanguage.Polish);
        ForeignAcl.Apply(aside);

        var result = SetAsideSalvage.FromAside(aside, root, Machine);

        Assert.Equal(SalvageOutcome.Moved, result.Journal.Outcome);
        Assert.Equal(SalvageOutcome.Moved, result.Settings.Outcome);
    }

    [Fact]
    public void FromAside_TargetJournalExists_WriteFailed_ExistingKept()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        var journal = HistoryFiles.WriteJournal(aside, Machine);
        HistoryFiles.WriteProtected(RootJournal(root), Corrupt);

        var result = SetAsideSalvage.FromAside(aside, root, Machine);

        Assert.Equal(SalvageOutcome.Rejected, result.Journal.Outcome);
        Assert.Equal(journal, result.Journal.Source);
        Assert.Equal(SalvageRejection.WriteFailed, result.Journal.Rejection);
        Assert.False(string.IsNullOrWhiteSpace(result.Journal.Detail));
        Assert.Equal(Corrupt, File.ReadAllBytes(RootJournal(root)));
        Assert.False(File.Exists(RootJournal(root) + ".tmp"));
    }

    // Zapis przez plik .tmp i przeniesienie: pozostałość po przerwanym zapisie nie blokuje kopii i nie zostaje w korzeniu.
    [Fact]
    public void FromAside_StaleTmpLeftInRoot_MovedAndNoTmpLeft()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        var journal = HistoryFiles.WriteJournal(aside, Machine);
        var settings = HistoryFiles.WriteSettings(aside, UiLanguage.English);
        root.EnsureLeaf(root.JournalDirectory);
        HistoryFiles.WriteProtected(RootJournal(root) + ".tmp", Corrupt);
        HistoryFiles.WriteProtected(RootSettings(root) + ".tmp", Corrupt);

        var result = SetAsideSalvage.FromAside(aside, root, Machine);

        Assert.Equal(SalvageOutcome.Moved, result.Journal.Outcome);
        Assert.Equal(SalvageOutcome.Moved, result.Settings.Outcome);
        Assert.Equal(File.ReadAllBytes(journal), File.ReadAllBytes(RootJournal(root)));
        Assert.Equal(File.ReadAllBytes(settings), File.ReadAllBytes(RootSettings(root)));
        Assert.True(AclTrust.IsProtectedAdminOnlyFile(RawDescriptors.OfFile(RootJournal(root))));
        Assert.False(File.Exists(RootJournal(root) + ".tmp"));
        Assert.False(File.Exists(RootSettings(root) + ".tmp"));
    }

    // Plik chwilowo zablokowany przez inny proces: odrzucenie oznaczone jako przejściowe (decyzja przy kolejnym starcie).
    // Nowszy plik chwilowo zablokowany: starszego poprawnego nie przenosimy, bo zamknąłby skanowanie i przekreślił nowszą historię.
    [Fact]
    public void FromOldAsides_NewerLockedOlderValid_NothingMoved()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var older = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-1"), Machine);
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-2"), Machine);
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        OldAsidesSalvage result;
        using (new FileStream(newer, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = SetAsideSalvage.FromOldAsides(root, Machine);
        }

        Assert.Null(result.Moved);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(newer, rejected.Source);
        Assert.True(rejected.Transient);
        Assert.False(File.Exists(RootJournal(root)));
    }

    [Fact]
    public void FromOldAsides_JournalLockedByOther_RejectedTransient()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var source = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-1"), Machine);

        OldAsidesSalvage result;
        using (new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = SetAsideSalvage.FromOldAsides(root, Machine);
        }

        Assert.Null(result.Moved);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(SalvageRejection.OpenFailed, rejected.Rejection);
        Assert.True(rejected.Transient);
        Assert.False(File.Exists(RootJournal(root)));
    }

    [Fact]
    public void FromOldAsides_PicksNewestByFileTime_NotFolderName()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var older = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-29991231000000000-aaaaaaaa"), Machine);
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-20000101000000000-bbbbbbbb"), Machine);
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = SetAsideSalvage.FromOldAsides(root, Machine);

        Assert.Equal(new SalvageFile(SalvageOutcome.Moved, newer), result.Moved);
        Assert.Empty(result.Rejected);
        Assert.Equal(File.ReadAllBytes(newer), File.ReadAllBytes(RootJournal(root)));
        Assert.True(AclTrust.IsProtectedAdminOnlyFile(RawDescriptors.OfFile(RootJournal(root))));
    }

    [Fact]
    public void FromAside_UntrustedJournalLeafInRoot_SetAsideReported()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        Directory.CreateDirectory(root.JournalDirectory);
        ForeignAcl.Apply(root.JournalDirectory);
        var aside = temp.Sub("app.untrusted-1");
        HistoryFiles.WriteJournal(aside, Machine);

        var result = SetAsideSalvage.FromAside(aside, root, Machine);

        Assert.Equal(SalvageOutcome.Moved, result.Journal.Outcome);
        var leafAside = Assert.Single(result.SetAside);
        Assert.StartsWith(root.JournalDirectory + ".untrusted-", leafAside, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromAside_NothingSetAside_EmptyList()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var aside = temp.Sub("app.untrusted-1");
        HistoryFiles.WriteJournal(aside, Machine);

        Assert.Empty(SetAsideSalvage.FromAside(aside, root, Machine).SetAside);
    }

    [Fact]
    public void FromOldAsides_UntrustedJournalLeafInRoot_SetAsideReported()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        Directory.CreateDirectory(root.JournalDirectory);
        ForeignAcl.Apply(root.JournalDirectory);
        var genuine = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-1"), Machine);

        var result = SetAsideSalvage.FromOldAsides(root, Machine);

        Assert.Equal(new SalvageFile(SalvageOutcome.Moved, genuine), result.Moved);
        var leafAside = Assert.Single(result.SetAside);
        Assert.StartsWith(root.JournalDirectory + ".untrusted-", leafAside, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromOldAsides_AsideFolderIsJunction_RejectedNothingWritten()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        HistoryFiles.WriteJournal(temp.Sub("elsewhere"), Machine);
        Junction.Create(temp.Sub("app.untrusted-z"), temp.Sub("elsewhere"));

        var result = SetAsideSalvage.FromOldAsides(root, Machine);

        // Folder-link odpada już przy wyborze folderów, więc nie trafia do listy plików odrzuconych.
        Assert.Null(result.Moved);
        Assert.Empty(result.Rejected);
        Assert.False(File.Exists(RootJournal(root)));
    }

    [Fact]
    public void FromOldAsides_JournalFolderIsJunction_RejectedNotRegularFile()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        HistoryFiles.WriteJournal(temp.Sub("elsewhere"), Machine);
        var aside = temp.Sub("app.untrusted-1");
        Directory.CreateDirectory(aside);
        Junction.Create(Path.Combine(aside, "journal"), temp.Sub(@"elsewhere\journal"));

        var result = SetAsideSalvage.FromOldAsides(root, Machine);

        Assert.Null(result.Moved);
        Assert.Equal(
            new[] { new SalvageFile(SalvageOutcome.Rejected, Path.Combine(aside, "journal", "journal.json"), SalvageRejection.NotRegularFile) },
            result.Rejected);
        Assert.False(File.Exists(RootJournal(root)));
    }

    [Fact]
    public void FromOldAsides_ManyUserOwnedFoldersSortingFirst_GenuineStillSalvaged()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        for (var i = 0; i <= SetAsideSalvage.MaxOldAsides; i++)
        {
            var planted = temp.Sub("app.untrusted-z" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            HistoryFiles.WriteProtected(Path.Combine(planted, "journal", "journal.json"), Corrupt);
            ForeignAcl.Apply(planted);
        }

        var genuine = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-20260101000000000-aaaaaaaa"), Machine);

        var result = SetAsideSalvage.FromOldAsides(root, Machine);

        Assert.Equal(new SalvageFile(SalvageOutcome.Moved, genuine), result.Moved);
        Assert.Empty(result.Rejected);
        Assert.Equal(File.ReadAllBytes(genuine), File.ReadAllBytes(RootJournal(root)));
    }

    [Fact]
    public void FromOldAsides_NoCandidates_Empty()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        Directory.CreateDirectory(temp.Sub("app.untrusted-1"));
        Directory.CreateDirectory(temp.Sub(@"app.untrusted-2\journal"));

        var result = SetAsideSalvage.FromOldAsides(root, Machine);

        Assert.Null(result.Moved);
        Assert.Empty(result.Rejected);
        Assert.False(File.Exists(RootJournal(root)));
    }

    [Fact]
    public void FromOldAsides_AllRejected_ListedNothingWritten()
    {
        using var temp = new TempDirectory();
        var root = EnsuredRoot(temp);
        var foreignMachine = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-1"), "PC-99");
        var usersAce = HistoryFiles.WriteJournal(temp.Sub("app.untrusted-2"), Machine);
        ProtectedFileReaderTests.AddUsersRead(usersAce);

        var result = SetAsideSalvage.FromOldAsides(root, Machine);

        Assert.Null(result.Moved);
        Assert.Equal(
            new[]
            {
                new SalvageFile(SalvageOutcome.Rejected, usersAce, SalvageRejection.UntrustedAcl),
                new SalvageFile(SalvageOutcome.Rejected, foreignMachine, SalvageRejection.OtherComputer, "PC-99"),
            },
            result.Rejected);
        Assert.False(File.Exists(RootJournal(root)));
    }
}
