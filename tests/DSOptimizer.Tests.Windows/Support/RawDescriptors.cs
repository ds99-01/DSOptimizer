using System.IO;
using System.Linq;
using System.Security.AccessControl;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>Surowy deskryptor (właściciel + DACL) folderu albo pliku, w tej postaci, którą ocenia reguła zaufania.</summary>
public static class RawDescriptors
{
    private const AccessControlSections OwnerAndAccess = AccessControlSections.Owner | AccessControlSections.Access;

    public static RawSecurityDescriptor OfDirectory(string path)
        => new(Directory.GetAccessControl(path, OwnerAndAccess).GetSecurityDescriptorBinaryForm(), 0);

    public static RawSecurityDescriptor OfFile(string path)
        => new(File.GetAccessControl(path, OwnerAndAccess).GetSecurityDescriptorBinaryForm(), 0);

    public static CommonAce[] Aces(RawSecurityDescriptor descriptor)
        => descriptor.DiscretionaryAcl!.Cast<GenericAce>().Cast<CommonAce>().ToArray();
}
