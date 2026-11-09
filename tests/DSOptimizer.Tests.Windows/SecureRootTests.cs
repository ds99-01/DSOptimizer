using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class SecureRootTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData("dso-wzgledna")]
    [InlineData(@"\bez-litery")]
    [InlineData("zły|znak")]
    public void Constructor_RootOrRelativeOrInvalid_Throws(string path)
        => Assert.Throws<ArgumentException>(() => new SecureRoot(path));

    [Theory]
    [InlineData(Environment.SpecialFolder.CommonApplicationData)]
    [InlineData(Environment.SpecialFolder.Windows)]
    [InlineData(Environment.SpecialFolder.ProgramFiles)]
    [InlineData(Environment.SpecialFolder.UserProfile)]
    [InlineData(Environment.SpecialFolder.System)]
    public void Constructor_SystemFolder_Throws(Environment.SpecialFolder folder)
        => Assert.Throws<ArgumentException>(() => new SecureRoot(Environment.GetFolderPath(folder)));

    [Fact]
    public void ProgramData_IsDSOptimizerUnderCommonApplicationData()
        => Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DSOptimizer"),
            SecureRoot.ProgramData.FullPath,
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void PublicSurface_HasNoPublicConstructor()
        => Assert.Empty(typeof(SecureRoot).GetConstructors());

    [Fact]
    public void LeafDirectories_AreDirectChildrenOfRoot()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        Assert.Equal(Path.Combine(root.FullPath, "logs"), root.LogsDirectory);
        Assert.Equal(Path.Combine(root.FullPath, "journal"), root.JournalDirectory);
        Assert.Equal(Path.Combine(root.FullPath, "run"), root.RunDirectory);
    }

    [Fact]
    public void Ensure_UntrustedRootExists_SetsAsideAndReturnsAsidePath()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("app"));
        File.WriteAllText(temp.Sub(@"app\obcy.txt"), "x");
        ForeignAcl.Apply(temp.Sub("app"));

        var aside = Assert.Single(temp.AppRoot.Ensure());

        Assert.StartsWith(temp.Sub("app.untrusted-"), aside, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(aside, "obcy.txt")));
        Assert.True(SecureDirectory.IsTrustedDirectory(temp.Sub("app")));
    }

    [Fact]
    public void Ensure_TrustedRoot_ReturnsEmpty()
    {
        using var temp = new TempDirectory();
        temp.AppRoot.Ensure();
        Assert.Empty(temp.AppRoot.Ensure());
    }

    [Fact]
    public void EnsureLeaf_LeafOutsideRoot_ThrowsBeforeTouchingDisk()
    {
        using var temp = new TempDirectory();
        Assert.Throws<ArgumentException>(() => temp.AppRoot.EnsureLeaf(temp.Sub(@"inny\journal")));
        Assert.False(Directory.Exists(temp.Sub("inny")));
        Assert.False(Directory.Exists(temp.Sub("app")));
    }

    [Theory]
    [InlineData(@"app\a\b")]
    [InlineData(@"app\..\x")]
    [InlineData("app")]
    public void EnsureLeaf_NotDirectChildOfRoot_Throws(string relative)
    {
        using var temp = new TempDirectory();
        Assert.Throws<ArgumentException>(() => temp.AppRoot.EnsureLeaf(temp.Sub(relative)));
    }

    [Fact]
    public void EnsureLeaf_ValidLeaf_RootAndLeafTrusted()
    {
        using var temp = new TempDirectory();
        temp.AppRoot.EnsureLeaf(temp.AppRoot.JournalDirectory);
        Assert.True(SecureDirectory.IsTrustedDirectory(temp.Sub("app")));
        Assert.True(SecureDirectory.IsTrustedDirectory(temp.Sub(@"app\journal")));
    }

    [Fact]
    public void EnsureLeaf_ExistingLeafWithForeignOwnerAndUsersAce_IsSetAsideAndRecreatedEmptyAndTrusted()
    {
        using var temp = new TempDirectory();
        SecureDirectory.Ensure(temp.Sub("app"));
        var leaf = temp.Sub(@"app\leaf");
        Directory.CreateDirectory(leaf);
        File.WriteAllText(Path.Combine(leaf, "stary.txt"), "zawartość");
        ForeignAcl.Apply(leaf);
        Assert.False(SecureDirectory.IsTrustedDirectory(leaf));

        temp.AppRoot.EnsureLeaf(leaf);

        var aside = Assert.Single(Directory.GetDirectories(temp.Sub("app"), "leaf.untrusted-*"));
        Assert.Equal("zawartość", File.ReadAllText(Path.Combine(aside, "stary.txt")));
        Assert.True(SecureDirectory.IsTrustedDirectory(leaf));
        Assert.Empty(Directory.GetFileSystemEntries(leaf));
    }

    [Fact]
    public void EnsureLeaf_ExistingTrustedLeafWithFile_KeepsEverythingInPlace()
    {
        using var temp = new TempDirectory();
        var leaf = temp.Sub(@"app\leaf");
        temp.AppRoot.EnsureLeaf(leaf);
        File.WriteAllText(Path.Combine(leaf, "plik.txt"), "zostaje");

        temp.AppRoot.EnsureLeaf(leaf);

        Assert.Equal("zostaje", File.ReadAllText(Path.Combine(leaf, "plik.txt")));
        Assert.Empty(Directory.GetDirectories(temp.Sub("app"), "*.untrusted-*"));
    }

    [Fact]
    public void WingetLogsDirectory_IsWingetUnderLogs()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        Assert.Equal(Path.Combine(root.LogsDirectory, SecureRoot.WingetLeafName), root.WingetLogsDirectory);
        Assert.Equal("winget", SecureRoot.WingetLeafName);
    }

    [Fact]
    public void EnsureExistingLeaves_LegacyLeaves_RewrittenMissingNotCreated()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        LegacyAcl.Create(root.FullPath);
        LegacyAcl.Create(root.LogsDirectory);
        LegacyAcl.Create(root.BackupsDirectory);
        LegacyAcl.Create(root.WingetLogsDirectory);
        var errors = new List<string>();

        Assert.Empty(root.Ensure());
        var asides = root.EnsureExistingLeaves(errors.Add);

        Assert.Empty(asides);
        Assert.Empty(errors);
        foreach (var path in new[] { root.FullPath, root.LogsDirectory, root.BackupsDirectory, root.WingetLogsDirectory })
        {
            Assert.True(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(path)), path);
        }

        Assert.False(Directory.Exists(root.RunDirectory));
        Assert.False(Directory.Exists(root.DownloadsDirectory));
        Assert.False(Directory.Exists(root.JournalDirectory));
    }

    [Fact]
    public void EnsureExistingLeaves_UntrustedBackups_SetAsideAndReturned()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        root.Ensure();
        Directory.CreateDirectory(root.BackupsDirectory);
        ForeignAcl.Apply(root.BackupsDirectory);
        var errors = new List<string>();

        var aside = Assert.Single(root.EnsureExistingLeaves(errors.Add));

        Assert.StartsWith(root.BackupsDirectory + ".untrusted-", aside, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(aside));
        Assert.Empty(errors);
        Assert.True(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(root.BackupsDirectory)));
    }

    [Fact]
    public void EnsureExistingLeaves_LogsIsJunction_WingetUnderTargetUntouched()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        root.Ensure();
        var target = temp.Sub("target");
        LegacyAcl.Create(target);
        LegacyAcl.Create(Path.Combine(target, SecureRoot.WingetLeafName));
        var targetBefore = RawDescriptors.OfDirectory(target).GetSddlForm(AccessControlSections.All);
        var wingetBefore = RawDescriptors.OfDirectory(Path.Combine(target, SecureRoot.WingetLeafName)).GetSddlForm(AccessControlSections.All);
        Junction.Create(root.LogsDirectory, target);
        var errors = new List<string>();

        var asides = root.EnsureExistingLeaves(errors.Add);

        Assert.Empty(asides);
        Assert.Single(errors);
        Assert.Equal(targetBefore, RawDescriptors.OfDirectory(target).GetSddlForm(AccessControlSections.All));
        Assert.Equal(
            wingetBefore,
            RawDescriptors.OfDirectory(Path.Combine(target, SecureRoot.WingetLeafName)).GetSddlForm(AccessControlSections.All));
    }

    [Fact]
    public void EnsureExistingLeaves_DownloadsIsJunction_ErrorReportedOthersMigrated()
    {
        using var temp = new TempDirectory();
        var root = temp.AppRoot;
        root.Ensure();
        LegacyAcl.Create(root.LogsDirectory);
        Directory.CreateDirectory(temp.Sub("cel"));
        Junction.Create(root.DownloadsDirectory, temp.Sub("cel"));
        try
        {
            var errors = new List<string>();

            var asides = root.EnsureExistingLeaves(errors.Add);

            Assert.Empty(asides);
            Assert.Contains(root.DownloadsDirectory, Assert.Single(errors), StringComparison.OrdinalIgnoreCase);
            Assert.True(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(root.LogsDirectory)));
            Assert.Empty(Directory.GetFileSystemEntries(temp.Sub("cel")));
        }
        finally
        {
            Directory.Delete(root.DownloadsDirectory, recursive: false);
        }
    }

    [Fact]
    public void EnsureLeaf_UntrustedParent_IsSetAsideWithItsContents()
    {
        using var temp = new TempDirectory();
        var parent = temp.Sub("app");
        Directory.CreateDirectory(parent);
        File.WriteAllText(Path.Combine(parent, "obcy.txt"), "x");
        ForeignAcl.Apply(parent);

        temp.AppRoot.EnsureLeaf(Path.Combine(parent, "leaf"));

        Assert.True(SecureDirectory.IsTrustedDirectory(parent));
        Assert.False(File.Exists(Path.Combine(parent, "obcy.txt")));
        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "app.untrusted-*"));
        Assert.True(File.Exists(Path.Combine(aside, "obcy.txt")));
    }
}
