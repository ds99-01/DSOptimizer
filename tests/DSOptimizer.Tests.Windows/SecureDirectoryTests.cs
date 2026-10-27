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
}
