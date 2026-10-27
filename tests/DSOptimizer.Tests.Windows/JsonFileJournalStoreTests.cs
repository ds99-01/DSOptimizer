using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using DSOptimizer.Core.Journal;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class JsonFileJournalStoreTests
{
    private static readonly SecurityIdentifier AdminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private static JsonFileJournalStore Store(TempDirectory temp) => new(temp.AppRoot, "PC-01", new FixedClock());

    private static string TrustedAppDirectory(TempDirectory temp)
    {
        var app = temp.Sub("app");
        SecureDirectory.Ensure(app);
        return app;
    }

    [Fact]
    public void Load_NoFile_ReturnsEmptyDocumentForMachine()
    {
        using var temp = new TempDirectory();
        var result = Store(temp).Load();

        Assert.Empty(result.Document.Entries);
        Assert.Equal("PC-01", result.Document.Machine);
        Assert.Null(result.Warning);
    }

    private static void SaveOneEntry(JsonFileJournalStore store)
    {
        var document = new JournalDocument { Machine = "PC-01" };
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.Active });
        store.Save(document);
    }

    [Fact]
    public void Load_AfterUntrustedJournalDirectoryWasSetAside_WarnsWithSetAsidePath()
    {
        using var temp = new TempDirectory();
        SaveOneEntry(Store(temp));
        ForeignAcl.Apply(temp.Sub(@"app\journal"));

        var result = Store(temp).Load();

        Assert.Empty(result.Document.Entries);
        Assert.NotNull(result.Warning);
        var aside = Assert.Single(Directory.GetDirectories(temp.Sub("app"), "journal.untrusted-*"));
        Assert.Contains(Path.Combine(aside, "journal.json"), result.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_AfterParentWasSetAsideByAnotherComponent_WarnsWithSetAsidePath()
    {
        using var temp = new TempDirectory();
        SaveOneEntry(Store(temp));
        ForeignAcl.Apply(temp.Sub("app"));
        temp.AppRoot.EnsureLeaf(temp.AppRoot.LogsDirectory); // np. FileLog uruchomiony przed journalem odkłada rodzica

        var result = Store(temp).Load();

        Assert.Empty(result.Document.Entries);
        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.NotNull(result.Warning);
        Assert.Contains(Path.Combine(aside, "journal", "journal.json"), result.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_AfterSaveOfNewJournal_NoLongerWarnsAboutSetAsideCopy()
    {
        using var temp = new TempDirectory();
        SaveOneEntry(Store(temp));
        ForeignAcl.Apply(temp.Sub(@"app\journal"));
        Assert.NotNull(Store(temp).Load().Warning);

        SaveOneEntry(Store(temp));

        Assert.Null(Store(temp).Load().Warning);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAndLeavesNoTempFile()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        var document = new JournalDocument { Machine = "PC-01" };
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.Active });

        store.Save(document);
        store.Save(document);

        Assert.Equal("a.b", Assert.Single(store.Load().Document.Entries).TweakId);
        Assert.Equal(new[] { "journal.json" }, Directory.GetFiles(temp.Sub(@"app\journal")).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void Load_CorruptFile_IsMovedAsideWithWarning()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(new JournalDocument { Machine = "PC-01" });
        File.WriteAllText(store.FilePath, "{ uszkodzony");

        var result = store.Load();

        Assert.Empty(result.Document.Entries);
        Assert.Contains("uszkodzony", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(temp.Sub(@"app\journal"), "journal.corrupt-*.json"));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_CreatesDirectoryRestrictedToAdministratorsAndSystem()
    {
        using var temp = new TempDirectory();
        Store(temp).Save(new JournalDocument { Machine = "PC-01" });

        var security = Directory.GetAccessControl(temp.Sub(@"app\journal"));
        var sids = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
            .Distinct()
            .OrderBy(sid => sid, StringComparer.Ordinal)
            .ToArray();

        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(new[] { "S-1-5-18", "S-1-5-32-544" }, sids);
    }

    [Fact]
    public void Save_PathBlockedByFile_ThrowsJournalStoreException()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(TrustedAppDirectory(temp), "journal"), "to jest plik, nie katalog");

        Assert.Throws<JournalStoreException>(() => Store(temp).Save(new JournalDocument { Machine = "PC-01" }));
    }

    [Fact]
    public void Load_FileLockedByAnotherProcess_ThrowsJournalStoreException()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(new JournalDocument { Machine = "PC-01" });

        using var exclusive = new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<JournalStoreException>(() => store.Load());
    }

    [Fact]
    public void Save_ExistingDirectoryWithForeignOwnerAndUsersAce_IsSetAsideAndRecreatedTrusted()
    {
        using var temp = new TempDirectory();
        var app = TrustedAppDirectory(temp);
        var path = Path.Combine(app, "journal");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "stary.txt"), "zawartość");
        var open = Directory.GetAccessControl(path);
        open.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        open.SetOwner(WindowsIdentity.GetCurrent().User!);
        Directory.SetAccessControl(path, open);
        Assert.NotEqual(AdminSid, (SecurityIdentifier)Directory.GetAccessControl(path).GetOwner(typeof(SecurityIdentifier)));

        Store(temp).Save(new JournalDocument { Machine = "PC-01" });

        var aside = Assert.Single(Directory.GetDirectories(app, "journal.untrusted-*"));
        Assert.True(File.Exists(Path.Combine(aside, "stary.txt")));
        Assert.True(SecureDirectory.IsTrustedDirectory(path));
        Assert.Equal(new[] { "journal.json" }, Directory.GetFileSystemEntries(path).Select(Path.GetFileName).ToArray());
    }
    [Fact]
    public void Load_FileWithForeignOwner_IsSetAsideAsUntrusted()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        var document = new JournalDocument { Machine = "PC-01" };
        document.Entries.Add(new JournalEntry { TweakId = "a.b", Status = JournalEntryStatus.Active });
        store.Save(document);
        var foreign = new FileSecurity();
        // Obcy właściciel = konkretny użytkownik (konto bieżące); grupa Users nie może być właścicielem bez dodatkowych przywilejów.
        foreign.SetOwner(WindowsIdentity.GetCurrent().User!);
        File.SetAccessControl(store.FilePath, foreign);

        var result = store.Load();

        Assert.Empty(result.Document.Entries);
        Assert.Contains("niezaufanego właściciela", result.Warning, StringComparison.Ordinal);
        Assert.False(File.Exists(store.FilePath));
        Assert.Single(Directory.GetFiles(temp.Sub(@"app\journal"), "journal.json.untrusted-*"));
    }

    [Fact]
    public void Save_SetsJournalFileOwnerToAdministrators()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);

        store.Save(new JournalDocument { Machine = "PC-01" });
        var foreign = new FileSecurity();
        foreign.SetOwner(WindowsIdentity.GetCurrent().User!);
        File.SetAccessControl(store.FilePath, foreign);
        Assert.NotEqual(AdminSid, File.GetAccessControl(store.FilePath).GetOwner(typeof(SecurityIdentifier)));

        store.Save(new JournalDocument { Machine = "PC-01" });

        var owner = File.GetAccessControl(store.FilePath).GetOwner(typeof(SecurityIdentifier));
        Assert.Equal(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), owner);
    }
    [Fact]
    public void Load_FileOwnedByAdministratorsWithExtraAce_IsSetAsideAsUntrusted()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(new JournalDocument { Machine = "PC-01" });
        var security = File.GetAccessControl(store.FilePath);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        File.SetAccessControl(store.FilePath, security);

        var result = store.Load();

        Assert.Contains("niezaufanego", result.Warning, StringComparison.Ordinal);
        Assert.False(File.Exists(store.FilePath));
        Assert.Single(Directory.GetFiles(temp.Sub(@"app\journal"), "journal.json.untrusted-*"));
    }

    [Fact]
    public void Save_WithPreexistingTempFileHavingOpenAcl_ProducesJournalOnlyForAdministratorsAndSystem()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        SecureDirectory.Ensure(Path.Combine(TrustedAppDirectory(temp), "journal"));
        var planted = store.FilePath + ".tmp";
        File.WriteAllText(planted, "podrzucony");
        var open = new FileSecurity();
        open.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        open.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        open.AddAccessRule(new FileSystemAccessRule(AdminSid, FileSystemRights.FullControl, AccessControlType.Allow));
        File.SetAccessControl(planted, open);

        store.Save(new JournalDocument { Machine = "PC-01" });

        var security = File.GetAccessControl(store.FilePath);
        var sids = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
            .Distinct()
            .OrderBy(sid => sid, StringComparer.Ordinal)
            .ToArray();
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(new[] { "S-1-5-18", "S-1-5-32-544" }, sids);
        Assert.Equal(AdminSid, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.Empty(store.Load().Document.Entries);
        Assert.Null(store.Load().Warning);
    }

    [Fact]
    public void Save_JournalDirectoryIsJunction_ThrowsJournalStoreException()
    {
        using var temp = new TempDirectory();
        var target = temp.Sub("target");
        var link = Path.Combine(TrustedAppDirectory(temp), "journal");
        Directory.CreateDirectory(target);
        Junction.Create(link, target);

        try
        {
            Assert.Throws<JournalStoreException>(() => Store(temp).Save(new JournalDocument { Machine = "PC-01" }));
            Assert.Empty(Directory.GetFiles(target));
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }
    [Fact]
    public void Save_ParentDirectoryIsJunction_ThrowsJournalStoreException()
    {
        using var temp = new TempDirectory();
        var target = temp.Sub("target");
        var link = temp.Sub("DSOptimizer");
        Directory.CreateDirectory(target);
        Junction.Create(link, target);

        try
        {
            var store = new JsonFileJournalStore(new SecureRoot(link), "PC-01", new FixedClock());
            Assert.Throws<JournalStoreException>(() => store.Save(new JournalDocument { Machine = "PC-01" }));
            Assert.Empty(Directory.GetDirectories(target));
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }

    [Fact]
    public void Save_LeafDirectoryWithInheritableEveryoneAce_YieldsProtectedJournalForAdministratorsAndSystemOnly()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Save(new JournalDocument { Machine = "PC-01" });
        File.Delete(store.FilePath);
        var leaf = Directory.GetAccessControl(temp.Sub(@"app\journal"));
        leaf.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        Directory.SetAccessControl(temp.Sub(@"app\journal"), leaf);

        store.Save(new JournalDocument { Machine = "PC-01" });

        var security = File.GetAccessControl(store.FilePath);
        var sids = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
            .Distinct()
            .OrderBy(sid => sid, StringComparer.Ordinal)
            .ToArray();
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(new[] { "S-1-5-18", "S-1-5-32-544" }, sids);
    }
}
