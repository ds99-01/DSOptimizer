using System;
using System.Security.AccessControl;
using System.Security.Principal;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Reguła zaufania na surowym deskryptorze, bez dysku: deskryptory z SDDL albo zbudowane ręcznie.</summary>
[Trait("Category", "Windows")]
public class AclTrustTests
{
    private const string Admins = "O:BAD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";
    private const string Current = Admins + "(A;OICI;0x1200a9;;;BU)";

    private static RawSecurityDescriptor Sddl(string sddl) => new(sddl);

    [Theory]
    [InlineData(Admins, true)]
    [InlineData(Current, true)]
    [InlineData("O:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;AU)", true)]
    [InlineData(Admins + "(A;OICI;0x120089;;;BU)", true)]
    [InlineData(Admins + "(A;OICIIO;0x1200a9;;;BU)", true)]
    [InlineData("O:BAD:PAI(D;OICI;FA;;;BA)(A;OICI;FA;;;SY)", true)]
    [InlineData(Admins + "(A;OICI;0x1301bf;;;BU)", false)]
    [InlineData(Admins + "(A;OICI;FA;;;BU)", false)]
    [InlineData(Admins + "(A;OICI;GR;;;BU)", false)]
    [InlineData(Admins + "(A;OICI;0x1200a9;;;WD)", false)]
    [InlineData(Admins + "(A;OICI;0x1200a9;;;CO)", false)]
    [InlineData(Admins + "(A;OICI;0x1200a9;;;OW)", false)]
    [InlineData(Admins + "(D;OICI;FA;;;BU)", false)]
    [InlineData(Admins + "(A;OICI;FA;;;S-1-5-21-1-2-3-1001)", false)]
    [InlineData("O:S-1-5-21-1-2-3-1001D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", false)]
    [InlineData("O:BAD:NO_ACCESS_CONTROL", false)]
    public void IsTrustedDirectory_Sddl(string sddl, bool expected)
        => Assert.Equal(expected, AclTrust.IsTrustedDirectory(Sddl(sddl)));

    [Fact]
    public void IsTrustedDirectory_CallbackAceForUsers_False()
    {
        var acl = AdminsAcl(GenericAcl.AclRevision);
        acl.InsertAce(acl.Count, new CommonAce(
            AceFlags.None, AceQualifier.AccessAllowed, AclTrust.UsersReadMask, AclTrust.UsersSid, isCallback: true, opaque: new byte[4]));

        Assert.False(AclTrust.IsTrustedDirectory(Descriptor(acl)));
    }

    [Fact]
    public void IsTrustedDirectory_ObjectAce_False()
    {
        var acl = AdminsAcl(GenericAcl.AclRevisionDS);
        acl.InsertAce(acl.Count, new ObjectAce(
            AceFlags.None,
            AceQualifier.AccessAllowed,
            AclTrust.UsersReadMask,
            AclTrust.UsersSid,
            ObjectAceFlags.ObjectAceTypePresent,
            Guid.NewGuid(),
            Guid.Empty,
            isCallback: false,
            opaque: null));

        Assert.False(AclTrust.IsTrustedDirectory(Descriptor(acl)));
    }

    public static TheoryData<string, int> UsersPlusForbiddenBit
    {
        get
        {
            var data = new TheoryData<string, int>();
            var bits = new[]
            {
                0x2, 0x4, 0x10, 0x40, 0x100, 0x10000, 0x40000, 0x80000,
                0x1000000, // ACCESS_SYSTEM_SECURITY
                0x2000000, // MAXIMUM_ALLOWED
                0x10000000, // GENERIC_ALL
                0x20000000, // GENERIC_EXECUTE
                0x40000000, // GENERIC_WRITE
            };
            foreach (var sid in new[] { "BU", "AU" })
            {
                foreach (var bit in bits)
                {
                    data.Add(sid, bit);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(UsersPlusForbiddenBit))]
    public void IsTrustedDirectory_UsersReadPlusForbiddenBit_False(string sid, int bit)
    {
        var acl = AdminsAcl(GenericAcl.AclRevision);
        var account = sid == "BU" ? AclTrust.UsersSid : AclTrust.AuthenticatedUsersSid;
        acl.InsertAce(acl.Count, new CommonAce(
            AceFlags.ObjectInherit | AceFlags.ContainerInherit,
            AceQualifier.AccessAllowed,
            AclTrust.UsersReadMask | bit,
            account,
            isCallback: false,
            opaque: null));

        Assert.False(AclTrust.IsTrustedDirectory(Descriptor(acl)));
    }

    [Fact]
    public void IsTrustedDirectory_DenyReadForAuthenticatedUsers_False()
        => Assert.False(AclTrust.IsTrustedDirectory(Sddl(Admins + "(D;OICI;0x1200a9;;;AU)")));

    [Fact]
    public void IsTrustedDirectory_AuditAceForUsersInDacl_False()
    {
        var acl = AdminsAcl(GenericAcl.AclRevision);
        acl.InsertAce(acl.Count, new CommonAce(
            AceFlags.SuccessfulAccess, AceQualifier.SystemAudit, AclTrust.UsersReadMask, AclTrust.UsersSid, isCallback: false, opaque: null));

        Assert.False(AclTrust.IsTrustedDirectory(Descriptor(acl)));
    }

    [Fact]
    public void IsTrustedDirectory_AdminsOnlyHandBuilt_True()
        => Assert.True(AclTrust.IsTrustedDirectory(Descriptor(AdminsAcl(GenericAcl.AclRevision))));

    [Theory]
    [InlineData("O:BAD:PAI(A;;FA;;;SY)(A;;FA;;;BA)", true)]
    [InlineData("O:BAD:AI(A;ID;FA;;;SY)(A;ID;FA;;;BA)", true)]
    [InlineData("O:BAD:PAI(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;BU)", false)]
    [InlineData("O:BAD:AI(A;ID;FA;;;SY)(A;ID;FA;;;BA)(A;ID;0x1200a9;;;BU)", false)]
    [InlineData("O:S-1-5-21-1-2-3-1001D:PAI(A;;FA;;;SY)(A;;FA;;;BA)", false)]
    [InlineData("O:BAD:NO_ACCESS_CONTROL", false)]
    public void IsTrustedFile_Sddl(string sddl, bool expected)
        => Assert.Equal(expected, AclTrust.IsTrustedFile(Sddl(sddl)));

    [Theory]
    [InlineData("O:BAD:PAI(A;;FA;;;SY)(A;;FA;;;BA)", true)]
    [InlineData("O:BAD:AI(A;;FA;;;SY)(A;;FA;;;BA)", false)]
    [InlineData("O:SYD:PAI(A;;FA;;;SY)(A;;FA;;;BA)", true)]
    [InlineData("O:BAD:PAI(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;BU)", false)]
    public void IsProtectedAdminOnlyFile_Sddl(string sddl, bool expected)
        => Assert.Equal(expected, AclTrust.IsProtectedAdminOnlyFile(Sddl(sddl)));

    [Theory]
    [InlineData(Current, true)]
    [InlineData(Admins, false)]
    [InlineData("O:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)", false)]
    [InlineData(Admins + "(A;OICIIO;0x1200a9;;;BU)", false)]
    [InlineData("O:BAD:AI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)", false)]
    [InlineData("O:BAD:PAI(A;OICIID;FA;;;SY)(A;OICIID;FA;;;BA)(A;OICIID;0x1200a9;;;BU)", false)]
    [InlineData(Admins + "(A;OICI;0x120089;;;BU)", false)]
    [InlineData("O:BAD:PAI(A;OICI;0x1200a9;;;BU)(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)", true)]
    [InlineData(Current + "(A;OICI;0x1200a9;;;AU)", false)]
    [InlineData(Current + "(D;OICI;FA;;;BA)", false)]
    [InlineData(Current + "(A;OICI;FA;;;SY)", false)]
    [InlineData("O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)", false)]
    [InlineData("O:BAD:PAI(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU)", false)]
    [InlineData("O:BAD:PAI(A;OICI;FA;;;SY)(A;OICI;0x1301bf;;;BA)(A;OICI;0x1200a9;;;BU)", false)]
    [InlineData("O:BAD:PAI(A;OICI;FA;;;SY)(A;CI;FA;;;BA)(A;OICI;0x1200a9;;;BU)", false)]
    [InlineData("O:BAD:PAI(A;OICI;FA;;;SY)(D;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)", false)]
    public void HasCurrentDirectoryDescriptor_Sddl(string sddl, bool expected)
        => Assert.Equal(expected, AclTrust.HasCurrentDirectoryDescriptor(Sddl(sddl)));

    [Fact]
    public void WellKnownSids_MatchUsersAndAuthenticatedUsers()
    {
        Assert.Equal("S-1-5-32-545", AclTrust.UsersSid.Value);
        Assert.Equal("S-1-5-11", AclTrust.AuthenticatedUsersSid.Value);
        Assert.Equal(0x1200A9, AclTrust.UsersReadMask);
        Assert.Equal((int)(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize), AclTrust.UsersReadMask);
    }

    private static RawAcl AdminsAcl(byte revision)
    {
        var acl = new RawAcl(revision, 4);
        foreach (var sid in new[] { SecureDirectory.SystemSid, SecureDirectory.AdministratorsSid })
        {
            acl.InsertAce(acl.Count, new CommonAce(
                AceFlags.ObjectInherit | AceFlags.ContainerInherit, AceQualifier.AccessAllowed, 0x1F01FF, sid, isCallback: false, opaque: null));
        }

        return acl;
    }

    private static RawSecurityDescriptor Descriptor(RawAcl dacl)
        => new(
            ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected | ControlFlags.SelfRelative,
            SecureDirectory.AdministratorsSid,
            null,
            null,
            dacl);
}
