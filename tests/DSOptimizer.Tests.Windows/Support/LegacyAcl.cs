using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Deskryptor folderu programu z wersji 4.0.7 i starszych: właściciel Administratorzy, DACL chroniony,
/// SYSTEM i Administratorzy <c>FullControl</c> z dziedziczeniem (bez wpisu dla Users).
/// </summary>
public static class LegacyAcl
{
    public static void Create(string path)
    {
        Directory.CreateDirectory(path);
        Apply(path);
    }

    public static void Apply(string path)
    {
        var security = new DirectorySecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
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
}
