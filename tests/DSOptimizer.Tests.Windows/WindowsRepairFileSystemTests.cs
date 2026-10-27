using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class WindowsRepairFileSystemTests
{
    private readonly WindowsRepairFileSystem _fs = new();

    [Fact]
    public void DirectoryExists_ReflectsDisk()
    {
        using var temp = new TempDirectory();

        Assert.True(_fs.DirectoryExists(temp.Root));
        Assert.False(_fs.DirectoryExists(temp.Sub("nie-ma")));
    }

    [Fact]
    public void ProbeDirectory_ExistingFolder_True_MissingOrFile_False()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("plik"), "x");

        Assert.True(_fs.ProbeDirectory(temp.Root));
        Assert.False(_fs.ProbeDirectory(temp.Sub("nie-ma")));
        Assert.False(_fs.ProbeDirectory(Path.Combine(temp.Sub("nie-ma"), "ani-tu")));
        Assert.False(_fs.ProbeDirectory(temp.Sub("plik")));
    }

    [Fact]
    public void ProbeDirectory_AccessDenied_ThrowsInsteadOfFalse()
    {
        // Odmowa tylko na folderze testowym w %TEMP%\dso-tests\<guid>, zdejmowana w finally (właściciel = ten proces).
        using var temp = new TempDirectory();
        var locked = temp.Sub("zablokowany");
        var child = Path.Combine(locked, "Windows.old");
        Directory.CreateDirectory(child);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var deny = new FileSystemAccessRule(
            everyone,
            FileSystemRights.ListDirectory | FileSystemRights.ReadAttributes,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny);
        var acl = Directory.GetAccessControl(locked);
        acl.AddAccessRule(deny);
        Directory.SetAccessControl(locked, acl);
        try
        {
            // M10: SeBackupPrivilege/SeRestorePrivilege omijają odmowę z ACL, a Git Bash (MSYS) uruchamia procesy z oboma
            // włączonymi (PowerShell – wyłączonymi). Wyłączone tylko w tokenie tego wątku, więc wynik nie zależy od powłoki.
            using (ThreadPrivileges.Disable(ThreadPrivileges.Backup, ThreadPrivileges.Restore))
            {
                Assert.False(ThreadPrivileges.IsEnabled(ThreadPrivileges.Backup));
                Assert.False(ThreadPrivileges.IsEnabled(ThreadPrivileges.Restore));
                Assert.False(Directory.Exists(child)); // to samo pytanie przez Directory.Exists: fałszywe „nie ma”
                var ex = Assert.Throws<OperationException>(() => _fs.ProbeDirectory(child));
                Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
            }
        }
        finally
        {
            var restore = Directory.GetAccessControl(locked);
            restore.RemoveAccessRuleAll(new FileSystemAccessRule(everyone, FileSystemRights.ListDirectory, AccessControlType.Deny));
            Directory.SetAccessControl(locked, restore);
        }

        Assert.True(_fs.ProbeDirectory(child));
    }

    [Fact]
    public void MoveDirectory_MovesContent()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("src"));
        File.WriteAllText(Path.Combine(temp.Sub("src"), "a.txt"), "x");

        _fs.MoveDirectory(temp.Sub("src"), temp.Sub("dst"));

        Assert.False(Directory.Exists(temp.Sub("src")));
        Assert.True(File.Exists(Path.Combine(temp.Sub("dst"), "a.txt")));
    }

    [Fact]
    public void MoveDirectory_MissingSource_ThrowsNotFound()
    {
        using var temp = new TempDirectory();

        var ex = Assert.Throws<OperationException>(() => _fs.MoveDirectory(temp.Sub("nie-ma"), temp.Sub("dst")));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public void MoveDirectory_ExistingDestination_ThrowsOperationExceptionAndKeepsSource()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("src"));
        Directory.CreateDirectory(temp.Sub("dst"));

        Assert.Throws<OperationException>(() => _fs.MoveDirectory(temp.Sub("src"), temp.Sub("dst")));

        Assert.True(Directory.Exists(temp.Sub("src")));
    }

    [Fact]
    public void ListSiblingDirectories_ReturnsPrefixedDirectoriesOnly_SortedAndWithoutSelf()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("SoftwareDistribution"));
        Directory.CreateDirectory(temp.Sub("softwaredistribution.bak2"));
        Directory.CreateDirectory(temp.Sub("SoftwareDistribution.bak1"));
        Directory.CreateDirectory(temp.Sub("Inny"));
        File.WriteAllText(temp.Sub("SoftwareDistribution.plik"), "x");

        var found = _fs.ListSiblingDirectories(temp.Sub("SoftwareDistribution"), "SoftwareDistribution.");

        Assert.Equal(new[] { temp.Sub("SoftwareDistribution.bak1"), temp.Sub("softwaredistribution.bak2") }, found);
    }

    [Fact]
    public void ListSiblingDirectories_PathItselfMatchingPrefix_IsExcluded()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("Wu"));
        Directory.CreateDirectory(temp.Sub("Wu2"));

        var found = _fs.ListSiblingDirectories(temp.Sub("Wu"), "Wu");

        Assert.Equal(new[] { temp.Sub("Wu2") }, found);
    }

    [Fact]
    public void ListSiblingDirectories_MissingParent_IsEmpty()
    {
        using var temp = new TempDirectory();

        Assert.Empty(_fs.ListSiblingDirectories(Path.Combine(temp.Sub("nie-ma"), "x"), "x"));
    }

    [Fact]
    public void DeleteDirectoryTree_RemovesNestedContentIncludingReadOnlyFiles()
    {
        using var temp = new TempDirectory();
        var deep = Path.Combine(temp.Sub("tree"), "a", "b");
        Directory.CreateDirectory(deep);
        var readOnly = Path.Combine(deep, "ro.txt");
        File.WriteAllText(readOnly, "x");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        _fs.DeleteDirectoryTree(temp.Sub("tree"));

        Assert.False(Directory.Exists(temp.Sub("tree")));
    }

    [Fact]
    public void DeleteDirectoryTree_MissingDirectory_IsNoOp()
    {
        using var temp = new TempDirectory();

        _fs.DeleteDirectoryTree(temp.Sub("nie-ma"));
    }

    [Fact]
    public void DeleteDirectoryTree_JunctionInside_RemovesLinkButNotTarget()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("target"));
        var important = Path.Combine(temp.Sub("target"), "wazne.txt");
        File.WriteAllText(important, "nie ruszac");
        Directory.CreateDirectory(temp.Sub("tree"));
        File.WriteAllText(Path.Combine(temp.Sub("tree"), "a.txt"), "x");
        Junction.Create(Path.Combine(temp.Sub("tree"), "link"), temp.Sub("target"));

        _fs.DeleteDirectoryTree(temp.Sub("tree"));

        Assert.False(Directory.Exists(temp.Sub("tree")));
        Assert.True(File.Exists(important));
        Assert.Equal("nie ruszac", File.ReadAllText(important));
    }

    [Fact]
    public void DeleteDirectoryTree_PathIsJunction_RemovesOnlyTheLink()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("target"));
        File.WriteAllText(Path.Combine(temp.Sub("target"), "wazne.txt"), "x");
        Junction.Create(temp.Sub("link"), temp.Sub("target"));

        _fs.DeleteDirectoryTree(temp.Sub("link"));

        Assert.False(Directory.Exists(temp.Sub("link")));
        Assert.True(File.Exists(Path.Combine(temp.Sub("target"), "wazne.txt")));
    }

    [Fact]
    public void DeleteDirectoryTree_PathIsFile_ThrowsOperationException()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("plik.txt"), "x");

        Assert.Throws<OperationException>(() => _fs.DeleteDirectoryTree(temp.Sub("plik.txt")));
        Assert.True(File.Exists(temp.Sub("plik.txt")));
    }

    [Fact]
    public void WriteAllText_WritesUtf8WithBomAndTrustedAcl_AndFileLengthMatches()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("plik.txt");
        var content = "Zażółć gęślą jaźń";

        _fs.WriteAllText(path, content);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, new[] { bytes[0], bytes[1], bytes[2] });
        Assert.Equal(content, new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
        Assert.True(SecureDirectory.IsTrustedFile(path));
        Assert.Equal(bytes.Length, _fs.FileLength(path));
    }

    [Fact]
    public void WriteAllText_ExistingFile_IsReplaced()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("plik.txt");
        File.WriteAllText(path, "dlugi stary tekst");

        _fs.WriteAllText(path, "nowy");

        Assert.Equal("nowy", File.ReadAllText(path));
        Assert.True(SecureDirectory.IsTrustedFile(path));
    }

    [Fact]
    public void WriteAllText_MissingParent_ThrowsNotFound()
    {
        using var temp = new TempDirectory();

        var ex = Assert.Throws<OperationException>(() => _fs.WriteAllText(Path.Combine(temp.Sub("nie-ma"), "x.txt"), "x"));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public void FileLength_MissingFileOrDirectory_IsNull()
    {
        using var temp = new TempDirectory();

        Assert.Null(_fs.FileLength(temp.Sub("nie-ma.txt")));
        Assert.Null(_fs.FileLength(temp.Root));
    }

    [Fact]
    public void DeleteOldFiles_DoesNotFollowJunction_KeepsYoungAndLocked()
    {
        using var temp = new TempDirectory();
        var old = DateTime.UtcNow.AddDays(-10);
        var root = temp.Sub("tmp");
        Directory.CreateDirectory(root);

        // Cel junction (np. Dokumenty konta) z plikiem-strażnikiem: stary, ale poza drzewem czyszczenia.
        var target = temp.Sub("target");
        Directory.CreateDirectory(target);
        var guard = OldFile(Path.Combine(target, "straznik.txt"), "nie ruszac", old);
        Junction.Create(Path.Combine(root, "link"), target);

        var oldFile = OldFile(Path.Combine(root, "stary.tmp"), "0123456789", old);
        var readOnly = OldFile(Path.Combine(root, "tylko-odczyt.tmp"), "abc", old);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var sub = Path.Combine(root, "pod");
        Directory.CreateDirectory(sub);
        var subFile = OldFile(Path.Combine(sub, "stary2.tmp"), "12345", old);
        Directory.SetLastWriteTimeUtc(sub, old);

        // Młodsze niż próg: plik i pusty folder (próg = za minutę, więc „młode” mają czas w przyszłości,
        // a świeżo założony junction jest „stary” według własnego czasu).
        var young = OldFile(Path.Combine(root, "swiezy.tmp"), "x", DateTime.UtcNow.AddDays(1));
        var youngDir = Path.Combine(root, "swiezy-folder");
        Directory.CreateDirectory(youngDir);
        Directory.SetLastWriteTimeUtc(youngDir, DateTime.UtcNow.AddDays(1));

        // W użyciu: otwarty bez współdzielenia oraz trzymany z FILE_SHARE_DELETE (jak .msu instalatora na VM).
        var locked = OldFile(Path.Combine(root, "zablokowany.tmp"), "lock", old);
        var shared = OldFile(Path.Combine(root, "instalator.msu"), "msu", old);
        OldFilesCleanup result;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (new FileStream(shared, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            result = _fs.DeleteOldFiles(root, DateTime.UtcNow.AddMinutes(1));
        }

        Assert.Equal(new OldFilesCleanup(Deleted: 4, SkippedInUse: 2, SkippedYoung: 1, FreedBytes: 10 + 3 + 5), result);
        Assert.False(File.Exists(oldFile));
        Assert.False(File.Exists(readOnly));
        Assert.False(File.Exists(subFile));
        Assert.False(Directory.Exists(sub));
        Assert.False(Directory.Exists(Path.Combine(root, "link")));
        Assert.True(File.Exists(guard));
        Assert.Equal("nie ruszac", File.ReadAllText(guard));
        Assert.True(File.Exists(young));
        Assert.True(Directory.Exists(youngDir));
        Assert.True(File.Exists(locked));
        Assert.True(File.Exists(shared));
        Assert.Equal("msu", File.ReadAllText(shared));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void DeleteOldFiles_SubfolderSwappedForJunctionAfterEnumeration_GuardOutsideTreeSurvives()
    {
        using var temp = new TempDirectory();
        var old = DateTime.UtcNow.AddDays(-10);
        var root = temp.Sub("tmp");
        var sub = Path.Combine(root, "pod");
        Directory.CreateDirectory(sub);
        OldFile(Path.Combine(sub, "stary.tmp"), "atak", old);
        Directory.SetLastWriteTimeUtc(sub, old);

        // Folder-strażnik spoza drzewa z plikiem o tej samej nazwie, starszym niż próg.
        var target = temp.Sub("target");
        Directory.CreateDirectory(target);
        var guard = OldFile(Path.Combine(target, "stary.tmp"), "nie ruszac", old);

        // Atak TOCTOU: po wyliczeniu „pod” użytkownik przemianowuje go i zakłada w jego miejsce junction do strażnika.
        var swapped = false;
        _fs.AfterDirectoryEnumerated = path =>
        {
            if (!swapped && string.Equals(path, sub, StringComparison.OrdinalIgnoreCase))
            {
                swapped = true;
                Directory.Move(sub, Path.Combine(root, "pod-przeniesiony"));
                Junction.Create(sub, target);
            }
        };

        var result = _fs.DeleteOldFiles(root, DateTime.UtcNow.AddMinutes(1));

        Assert.True(swapped);
        Assert.True(File.Exists(guard), "Usunięto plik spoza czyszczonego drzewa (przez podmieniony junction).");
        Assert.Equal("nie ruszac", File.ReadAllText(guard));
        Assert.Equal(0, result.Deleted);
        Assert.Equal(0, result.FreedBytes);
    }

    [Fact]
    public void DeleteOldFiles_FileSwappedForJunctionAfterEnumeration_OnlyLinkRemoved()
    {
        using var temp = new TempDirectory();
        var old = DateTime.UtcNow.AddDays(-10);
        var root = temp.Sub("tmp");
        Directory.CreateDirectory(root);
        var file = OldFile(Path.Combine(root, "stary.tmp"), "x", old);
        var target = temp.Sub("target");
        Directory.CreateDirectory(target);
        var guard = OldFile(Path.Combine(target, "straznik.txt"), "nie ruszac", old);

        // Po wyliczeniu korzenia plik znika, a pod jego nazwą pojawia się junction do strażnika (ostatni człon ścieżki).
        _fs.AfterDirectoryEnumerated = path =>
        {
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                Junction.Create(file, target);
            }
        };

        var result = _fs.DeleteOldFiles(root, DateTime.UtcNow.AddMinutes(1));

        Assert.False(Directory.Exists(file), "Link nie został usunięty.");
        Assert.True(File.Exists(guard));
        Assert.Equal("nie ruszac", File.ReadAllText(guard));
        Assert.Equal(0, result.FreedBytes);
    }

    [Fact]
    public void DeleteOldFiles_FreshEntriesUnderPastThreshold_AllKept()
    {
        using var temp = new TempDirectory();
        var root = temp.Sub("tmp");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(temp.Sub("target"));
        File.WriteAllText(Path.Combine(root, "a.tmp"), "a");
        File.WriteAllText(Path.Combine(root, "b.tmp"), "b");
        Directory.CreateDirectory(Path.Combine(root, "pusty"));
        Junction.Create(Path.Combine(root, "link"), temp.Sub("target"));

        var result = _fs.DeleteOldFiles(root, DateTime.UtcNow.AddHours(-24));

        // Dwa pliki i link są młodsze niż próg (link według własnego czasu); świeży pusty folder zostaje bez liczenia.
        Assert.Equal(new OldFilesCleanup(0, 0, 3, 0), result);
        Assert.True(Directory.Exists(Path.Combine(root, "pusty")));
        Assert.True(Directory.Exists(Path.Combine(root, "link")));
    }

    [Fact]
    public void DeleteOldFiles_MissingRoot_IsNone()
    {
        using var temp = new TempDirectory();

        Assert.Equal(new OldFilesCleanup(0, 0, 0, 0), _fs.DeleteOldFiles(temp.Sub("nie-ma"), DateTime.MaxValue));
    }

    [Fact]
    public void DeleteOldFiles_RootIsJunction_NothingTouched()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("target"));
        var guard = OldFile(Path.Combine(temp.Sub("target"), "straznik.txt"), "x", DateTime.UtcNow.AddDays(-10));
        Junction.Create(temp.Sub("link"), temp.Sub("target"));

        var result = _fs.DeleteOldFiles(temp.Sub("link"), DateTime.MaxValue);

        Assert.Equal(0, result.Deleted);
        Assert.Contains("link", result.SkipReason, StringComparison.Ordinal);
        Assert.True(File.Exists(guard));
        Assert.True(Directory.Exists(temp.Sub("link")));
    }

    [Fact]
    public void DeleteOldFiles_RootReachedThroughJunctionInMiddleSegment_SkippedWithReason_NothingDeleted()
    {
        // <guid>\profil → junction do <guid>\inny; czyszczony „profil\Temp” to naprawdę „inny\Temp”.
        using var temp = new TempDirectory();
        var realTemp = Path.Combine(temp.Sub("inny"), "Temp");
        Directory.CreateDirectory(realTemp);
        var guard = OldFile(Path.Combine(realTemp, "straznik.txt"), "x", DateTime.UtcNow.AddDays(-10));
        Junction.Create(temp.Sub("profil"), temp.Sub("inny"));

        var result = _fs.DeleteOldFiles(Path.Combine(temp.Sub("profil"), "Temp"), DateTime.MaxValue);

        Assert.Equal(0, result.Deleted);
        Assert.Contains("link", result.SkipReason, StringComparison.Ordinal);
        Assert.True(File.Exists(guard));
    }

    [Fact]
    public void DeleteOldFiles_RootWithTrailingSeparatorAndDifferentCase_NotSkipped()
    {
        using var temp = new TempDirectory();
        var root = temp.Sub("Temp");
        Directory.CreateDirectory(root);
        var old = OldFile(Path.Combine(root, "stary.txt"), "x", DateTime.UtcNow.AddDays(-10));

        var result = _fs.DeleteOldFiles(root.ToUpperInvariant() + @"\", DateTime.MaxValue);

        Assert.Null(result.SkipReason);
        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void DeleteOldFiles_MaxValueThreshold_EmptiesTreeButKeepsRoot()
    {
        using var temp = new TempDirectory();
        var root = temp.Sub("Download");
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllText(Path.Combine(root, "a", "b", "x.cab"), "1234");
        File.WriteAllText(Path.Combine(root, "y.msu"), "12");

        var result = _fs.DeleteOldFiles(root, DateTime.MaxValue);

        Assert.Equal(new OldFilesCleanup(2, 0, 0, 6), result);
        Assert.True(Directory.Exists(root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public void DeleteOldFiles_RootIsFile_ThrowsOperationException()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("plik.txt"), "x");

        Assert.Throws<OperationException>(() => _fs.DeleteOldFiles(temp.Sub("plik.txt"), DateTime.MaxValue));
        Assert.True(File.Exists(temp.Sub("plik.txt")));
    }

    [Fact]
    public void DeleteOldFiles_SubfolderSwappedForJunctionBeforeItsEnumeration_TargetNotTraversed()
    {
        using var temp = new TempDirectory();
        var old = DateTime.UtcNow.AddDays(-10);
        var root = temp.Sub("tmp");
        var sub = Path.Combine(root, "pod");
        Directory.CreateDirectory(sub);
        Directory.SetLastWriteTimeUtc(sub, old);

        // Cel spoza drzewa: plik młody i stary plik w użyciu – bez poprawki wyliczenie celu zawyża liczniki młodych i „w użyciu”.
        var target = temp.Sub("target");
        Directory.CreateDirectory(target);
        var young = OldFile(Path.Combine(target, "swiezy.txt"), "mlody", DateTime.UtcNow.AddDays(1));
        var locked = OldFile(Path.Combine(target, "zablokowany.txt"), "lock", old);

        // Atak TOCTOU: po wyliczeniu korzenia, a przed wejściem do „pod”, folder zostaje podmieniony na junction do celu.
        _fs.AfterDirectoryEnumerated = path =>
        {
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(sub, Path.Combine(root, "pod-przeniesiony"));
                Junction.Create(sub, target);
            }
        };

        OldFilesCleanup result;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = _fs.DeleteOldFiles(root, DateTime.UtcNow.AddMinutes(1));
        }

        Assert.Equal(0, result.SkippedYoung);
        Assert.Equal(0, result.SkippedInUse);
        Assert.Equal(0, result.FreedBytes);
        Assert.True(File.Exists(young));
        Assert.True(File.Exists(locked));
        Assert.False(Directory.Exists(sub), "Podmieniony stary folder (teraz link) powinien zniknąć jako link.");
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void DeleteFilesByName_DeletesOnlyMatchingTopLevelFiles_KeepsLockedAndOthers()
    {
        using var temp = new TempDirectory();
        var dir = temp.Sub("Explorer");
        Directory.CreateDirectory(Path.Combine(dir, "pod"));
        File.WriteAllText(Path.Combine(dir, "thumbcache_32.db"), "1234");
        File.WriteAllText(Path.Combine(dir, "THUMBCACHE_256.DB"), "12");
        var locked = Path.Combine(dir, "thumbcache_idx.db");
        File.WriteAllText(locked, "idx");
        var others = new[]
        {
            Path.Combine(dir, "iconcache_32.db"),
            Path.Combine(dir, "thumbcache_32.dbx"),
            Path.Combine(dir, "xthumbcache_32.db"),
            Path.Combine(dir, "pod", "thumbcache_pod.db"),
        };
        foreach (var other in others)
        {
            File.WriteAllText(other, "zostaje");
        }

        OldFilesCleanup result;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = _fs.DeleteFilesByName(dir, "thumbcache_", ".db");
        }

        Assert.Equal(new OldFilesCleanup(Deleted: 2, SkippedInUse: 1, SkippedYoung: 0, FreedBytes: 6), result);
        Assert.False(File.Exists(Path.Combine(dir, "thumbcache_32.db")));
        Assert.False(File.Exists(Path.Combine(dir, "THUMBCACHE_256.DB")));
        Assert.True(File.Exists(locked));
        Assert.All(others, other => Assert.True(File.Exists(other), other));
        Assert.True(Directory.Exists(Path.Combine(dir, "pod")));
    }

    [Fact]
    public void DeleteFilesByName_JunctionWithMatchingName_OnlyLinkRemoved()
    {
        using var temp = new TempDirectory();
        var dir = temp.Sub("Explorer");
        Directory.CreateDirectory(dir);
        var target = temp.Sub("target");
        Directory.CreateDirectory(target);
        var guard = OldFile(Path.Combine(target, "thumbcache_cel.db"), "nie ruszac", DateTime.UtcNow.AddDays(-10));
        Junction.Create(Path.Combine(dir, "thumbcache_link.db"), target);

        var result = _fs.DeleteFilesByName(dir, "thumbcache_", ".db");

        Assert.Equal(new OldFilesCleanup(1, 0, 0, 0), result);
        Assert.False(Directory.Exists(Path.Combine(dir, "thumbcache_link.db")));
        Assert.Equal("nie ruszac", File.ReadAllText(guard));
    }

    [Fact]
    public void DeleteFilesByName_MissingOrLinkedDirectory_IsNone()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("target"));
        var guard = OldFile(Path.Combine(temp.Sub("target"), "thumbcache_32.db"), "x", DateTime.UtcNow.AddDays(-10));
        Junction.Create(temp.Sub("link"), temp.Sub("target"));

        Assert.Equal(OldFilesCleanup.None, _fs.DeleteFilesByName(temp.Sub("nie-ma"), "thumbcache_", ".db"));
        Assert.Contains("link", _fs.DeleteFilesByName(temp.Sub("link"), "thumbcache_", ".db").SkipReason, StringComparison.Ordinal);
        Assert.True(File.Exists(guard));
    }

    [Fact]
    public void DeleteFilesByName_DirectoryReachedThroughJunctionInMiddleSegment_Skipped()
    {
        using var temp = new TempDirectory();
        var realExplorer = Path.Combine(temp.Sub("inny"), "Explorer");
        Directory.CreateDirectory(realExplorer);
        var guard = OldFile(Path.Combine(realExplorer, "thumbcache_32.db"), "x", DateTime.UtcNow.AddDays(-10));
        Junction.Create(temp.Sub("profil"), temp.Sub("inny"));

        var result = _fs.DeleteFilesByName(Path.Combine(temp.Sub("profil"), "Explorer"), "thumbcache_", ".db");

        Assert.Equal(0, result.Deleted);
        Assert.Contains("link", result.SkipReason, StringComparison.Ordinal);
        Assert.True(File.Exists(guard));
    }

    [Fact]
    public void ReadSharedFrom_FileOpenForWritingByTest_ReturnsTextFromOffset()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("CBS.log");
        var utf8 = new UTF8Encoding(false);
        using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var head = utf8.GetBytes("stary wpis\r\n");
        writer.Write(head, 0, head.Length);
        var offset = writer.Position;
        var segment = utf8.GetBytes("[SR] Verify complete – zażółć\r\n");
        writer.Write(segment, 0, segment.Length);
        writer.Flush();

        Assert.Equal("[SR] Verify complete – zażółć\r\n", _fs.ReadSharedFrom(path, offset));
        Assert.Equal("stary wpis\r\n[SR] Verify complete – zażółć\r\n", _fs.ReadSharedFrom(path, 0));
    }

    [Fact]
    public void ReadSharedFrom_BomAtStart_IsSkipped()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("log.txt");
        File.WriteAllText(path, "tekst", new UTF8Encoding(true));

        Assert.Equal("tekst", _fs.ReadSharedFrom(path, 0));
    }

    [Fact]
    public void ReadSharedFrom_OffsetBeyondEnd_ReadsWholeNewFile()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("CBS.log");
        File.WriteAllText(path, "nowy plik po rotacji\r\n", new UTF8Encoding(false));

        Assert.Equal("nowy plik po rotacji\r\n", _fs.ReadSharedFrom(path, 1_000_000));
    }

    [Fact]
    public void ReadSharedFrom_MissingFile_IsNull()
    {
        using var temp = new TempDirectory();

        Assert.Null(_fs.ReadSharedFrom(temp.Sub("nie-ma.log"), 0));
        Assert.Null(_fs.ReadSharedFrom(Path.Combine(temp.Sub("nie-ma"), "CBS.log"), 0));
    }

    [Fact]
    public void ReadSharedFrom_NegativeOffset_Throws()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("a.log"), "x");

        Assert.Throws<ArgumentOutOfRangeException>(() => _fs.ReadSharedFrom(temp.Sub("a.log"), -1));
    }

    [Fact]
    public void ReadSharedFrom_SegmentOver16Mb_LastMegabytesFromFullLine()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("CBS.log");
        var line = new string('a', 99) + "\n";
        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            for (var i = 0; i < 17 * 1024 * 1024 / line.Length; i++)
            {
                writer.Write(line);
            }

            writer.Write("[SR] Repair complete\n");
        }

        var text = _fs.ReadSharedFrom(path, 0)!;

        Assert.True(text.Length <= 16 * 1024 * 1024, $"Odczytano {text.Length} znaków.");
        Assert.True(text.Length > 15 * 1024 * 1024, $"Odczytano {text.Length} znaków.");
        Assert.StartsWith(line, text, StringComparison.Ordinal);
        Assert.EndsWith("[SR] Repair complete\n", text, StringComparison.Ordinal);
    }

    private static string OldFile(string path, string content, DateTime lastWriteUtc)
    {
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }
}
