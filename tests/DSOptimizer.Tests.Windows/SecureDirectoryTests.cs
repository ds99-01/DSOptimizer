using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class SecureDirectoryTests
{
    private static readonly SecurityIdentifier AdminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private static string[] Sids(FileSystemSecurity security)
        => security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
            .Distinct()
            .OrderBy(sid => sid, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void SetTrustedOwnerAndProtectedAcl_FileWithForeignOwnerAndOpenAcl_BecomesAdministratorsOnly()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("plik.txt");
        File.WriteAllText(path, "x");
        var foreign = new FileSecurity();
        foreign.SetOwner(WindowsIdentity.GetCurrent().User!);
        foreign.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        File.SetAccessControl(path, foreign);
        Assert.NotEqual(AdminSid, File.GetAccessControl(path).GetOwner(typeof(SecurityIdentifier)));

        SecureDirectory.SetTrustedOwnerAndProtectedAcl(path);

        var security = File.GetAccessControl(path);
        Assert.Equal(AdminSid, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(new[] { "S-1-5-18", "S-1-5-32-544" }, Sids(security));
    }

    [Fact]
    public void CreateTrustedFileSecurity_UsedAtCreation_IgnoresInheritableAceOfDirectory()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("dir"));
        var dir = Directory.GetAccessControl(temp.Sub("dir"));
        dir.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        Directory.SetAccessControl(temp.Sub("dir"), dir);
        var path = Path.Combine(temp.Sub("dir"), "nowy.tmp");

        using (new FileStream(path, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, SecureDirectory.CreateTrustedFileSecurity()))
        {
        }

        var security = File.GetAccessControl(path);
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(new[] { "S-1-5-18", "S-1-5-32-544" }, Sids(security));
        Assert.Equal(AdminSid, security.GetOwner(typeof(SecurityIdentifier)));
    }

    [Fact]
    public void Ensure_UntrustedExisting_ReturnsAsidePath()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("dir"));
        ForeignAcl.Apply(temp.Sub("dir"));

        var aside = SecureDirectory.Ensure(temp.Sub("dir"));

        Assert.NotNull(aside);
        Assert.True(Directory.Exists(aside));
        Assert.True(SecureDirectory.IsTrustedDirectory(temp.Sub("dir")));
    }

    [Fact]
    public void Ensure_TrustedExisting_ReturnsNull()
    {
        using var temp = new TempDirectory();
        SecureDirectory.Ensure(temp.Sub("dir"));

        Assert.Null(SecureDirectory.Ensure(temp.Sub("dir")));
    }

    [Fact]
    public void Ensure_New_CurrentDescriptor()
    {
        using var temp = new TempDirectory();

        Assert.Null(SecureDirectory.Ensure(temp.Sub("dir")));

        var descriptor = RawDescriptors.OfDirectory(temp.Sub("dir"));
        Assert.True(AclTrust.HasCurrentDirectoryDescriptor(descriptor));
        var aces = RawDescriptors.Aces(descriptor)
            .Select(ace => (ace.SecurityIdentifier.Value, ace.AccessMask, ace.AceQualifier, ace.AceFlags))
            .OrderBy(ace => ace.Value, StringComparer.Ordinal)
            .ToArray();
        const AceFlags Inherit = AceFlags.ObjectInherit | AceFlags.ContainerInherit;
        Assert.Equal(
            new[]
            {
                ("S-1-5-18", 0x1F01FF, AceQualifier.AccessAllowed, Inherit),
                ("S-1-5-32-544", 0x1F01FF, AceQualifier.AccessAllowed, Inherit),
                ("S-1-5-32-545", 0x1200A9, AceQualifier.AccessAllowed, Inherit),
            },
            aces);
    }

    [Fact]
    public void Ensure_TrustedLegacyDescriptor_RewrittenInPlace()
    {
        using var temp = new TempDirectory();
        var dir = temp.Sub("dir");
        LegacyAcl.Create(dir);
        WriteProtectedFile(Path.Combine(dir, "a.json"));
        File.WriteAllText(Path.Combine(dir, "b.log"), "log");

        Assert.Null(SecureDirectory.Ensure(dir));

        AssertMigrated(dir);
    }

    // Review Focus 3: Eksplorator albo program użytkownika trzyma plik w migrowanym folderze.
    [Fact]
    public void Ensure_TrustedLegacyWithOpenFileInside_StillRewritten()
    {
        using var temp = new TempDirectory();
        var dir = temp.Sub("dir");
        LegacyAcl.Create(dir);
        WriteProtectedFile(Path.Combine(dir, "a.json"));
        File.WriteAllText(Path.Combine(dir, "b.log"), "log");

        using (new FileStream(Path.Combine(dir, "b.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.Null(SecureDirectory.Ensure(dir));
        }

        AssertMigrated(dir);
    }

    [Fact]
    public void Ensure_TrustedLegacyRewriteDenied_FolderKeptFailureReported()
    {
        using var temp = new TempDirectory();
        var dir = temp.Sub("dir");
        RewriteDenied.Create(dir);
        try
        {
            var failures = new System.Collections.Generic.List<(string Path, string Reason)>();

            Assert.Null(SecureDirectory.Ensure(dir, (path, reason) => failures.Add((path, reason))));

            var failure = Assert.Single(failures);
            Assert.Equal(dir, failure.Path);
            Assert.False(string.IsNullOrWhiteSpace(failure.Reason));
            Assert.False(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(dir)));
            Assert.Empty(Directory.GetDirectories(temp.Root, "*.untrusted-*"));
        }
        finally
        {
            RewriteDenied.Release(dir);
        }
    }

    [Fact]
    public void Ensure_TrustedLegacyRewritten_NoFailureReported()
    {
        using var temp = new TempDirectory();
        var dir = temp.Sub("dir");
        LegacyAcl.Create(dir);
        var failures = 0;

        Assert.Null(SecureDirectory.Ensure(dir, (_, _) => failures++));

        Assert.Equal(0, failures);
        Assert.True(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(dir)));
    }

    // Folder do odłożenia, którego nie da się przenieść (brak prawa usunięcia w nim i w rodzicu): komunikat zwykłymi słowami.
    [Fact]
    public void Ensure_UntrustedCannotBeMoved_IOExceptionInPlainWords()
    {
        using var temp = new TempDirectory();
        var parent = temp.Sub("parent");
        var dir = Path.Combine(parent, "dir");
        Directory.CreateDirectory(dir);
        ForeignAcl.Apply(dir);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var denyDelete = new FileSystemAccessRule(everyone, FileSystemRights.Delete, AccessControlType.Deny);
        var denyDeleteChild = new FileSystemAccessRule(everyone, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny);
        AddRule(dir, denyDelete);
        AddRule(parent, denyDeleteChild);
        try
        {
            using (DSOptimizer.Core.Localization.L.Use(DSOptimizer.Core.Localization.UiLanguage.English))
            {
                var ex = Assert.Throws<IOException>(() => SecureDirectory.Ensure(dir));

                Assert.StartsWith(
                    "The folder " + dir + " had its permissions changed and couldn't be set aside: ",
                    ex.Message,
                    StringComparison.Ordinal);
            }

            using (DSOptimizer.Core.Localization.L.Use(DSOptimizer.Core.Localization.UiLanguage.Polish))
            {
                var polish = Assert.Throws<IOException>(() => SecureDirectory.Ensure(dir));
                Assert.StartsWith(
                    "Folder " + dir + " miał zmienione uprawnienia i nie udało się go odłożyć na bok: ",
                    polish.Message,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            RemoveRule(parent, denyDeleteChild);
            RemoveRule(dir, denyDelete);
        }
    }

    [Fact]
    public void Ensure_UserAccountFullControlAce_SetAside()
    {
        using var temp = new TempDirectory();
        var dir = temp.Sub("dir");
        SecureDirectory.Ensure(dir);
        AddInheritableRule(dir, WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl);

        var aside = SecureDirectory.Ensure(dir);

        Assert.NotNull(aside);
        Assert.StartsWith(dir + ".untrusted-", aside, StringComparison.OrdinalIgnoreCase);
        Assert.True(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(dir)));
    }

    [Fact]
    public void Ensure_UsersModifyAce_SetAside()
    {
        using var temp = new TempDirectory();
        var dir = temp.Sub("dir");
        SecureDirectory.Ensure(dir);
        AddInheritableRule(dir, AclTrust.UsersSid, FileSystemRights.Modify);

        var aside = SecureDirectory.Ensure(dir);

        Assert.NotNull(aside);
        Assert.StartsWith(dir + ".untrusted-", aside, StringComparison.OrdinalIgnoreCase);
        Assert.True(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(dir)));
    }

    private static void WriteProtectedFile(string path)
    {
        using var stream = new FileStream(
            path, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, SecureDirectory.CreateTrustedFileSecurity());
        stream.WriteByte((byte)'{');
    }

    private static void AddInheritableRule(string dir, SecurityIdentifier sid, FileSystemRights rights)
    {
        var security = Directory.GetAccessControl(dir);
        security.AddAccessRule(new FileSystemAccessRule(
            sid, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        Directory.SetAccessControl(dir, security);
    }

    private static void AddRule(string dir, FileSystemAccessRule rule)
    {
        var security = Directory.GetAccessControl(dir, AccessControlSections.Access);
        security.AddAccessRule(rule);
        Directory.SetAccessControl(dir, security);
    }

    private static void RemoveRule(string dir, FileSystemAccessRule rule)
    {
        var security = Directory.GetAccessControl(dir, AccessControlSections.Access);
        security.RemoveAccessRuleSpecific(rule);
        Directory.SetAccessControl(dir, security);
    }

    private static void AssertMigrated(string dir)
    {
        Assert.True(AclTrust.HasCurrentDirectoryDescriptor(RawDescriptors.OfDirectory(dir)));
        Assert.True(AclTrust.IsProtectedAdminOnlyFile(RawDescriptors.OfFile(Path.Combine(dir, "a.json"))));
        Assert.Contains(
            RawDescriptors.Aces(RawDescriptors.OfFile(Path.Combine(dir, "b.log"))),
            ace => ace.SecurityIdentifier.Equals(AclTrust.UsersSid)
                && ace.AccessMask == AclTrust.UsersReadMask
                && ace.AceQualifier == AceQualifier.AccessAllowed
                && (ace.AceFlags & AceFlags.Inherited) != 0);
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(dir)!, "*.untrusted-*"));
    }
}
