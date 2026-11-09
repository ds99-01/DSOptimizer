using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Folder zaufany według reguły programu (właściciel Administratorzy, ACE tylko Administratorzy i SYSTEM), ale z jawnym
/// zakazem zmiany właściciela dla Administratorów: przepisanie na bieżący deskryptor (zmienia też właściciela) się nie udaje.
/// <see cref="Release"/> zdejmuje zakaz, żeby test mógł posprzątać.
/// </summary>
public static class RewriteDenied
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static void Create(string path)
    {
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.TakeOwnership, AccessControlType.Deny));
        foreach (var type in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(type, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        Directory.SetAccessControl(path, security);
    }

    /// <summary>Zdejmuje zakaz (sama DACL, bez zmiany właściciela – to wolno właścicielowi).</summary>
    public static void Release(string path)
    {
        var security = Directory.GetAccessControl(path, AccessControlSections.Access);
        security.RemoveAccessRuleAll(new FileSystemAccessRule(Administrators, FileSystemRights.TakeOwnership, AccessControlType.Deny));
        Directory.SetAccessControl(path, security);
    }
}
