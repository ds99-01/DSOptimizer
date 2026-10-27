using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DSOptimizer.Tests.Windows.Support;

public static class ForeignAcl
{
    public static void Apply(string directory)
    {
        var foreign = Directory.GetAccessControl(directory);
        foreign.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        foreign.SetOwner(WindowsIdentity.GetCurrent().User!);
        Directory.SetAccessControl(directory, foreign);
    }
}
