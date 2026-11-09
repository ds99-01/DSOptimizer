using System.Security.AccessControl;
using System.Security.Principal;

namespace DSOptimizer.Windows;

/// <summary>
/// Reguła zaufania folderów i plików programu jako czysta funkcja surowego deskryptora (właściciel + DACL), bez dysku.
/// Folder: właściciel Administratorzy albo SYSTEM, każde ACE to zwykły <see cref="CommonAce"/> (bez callback), Administratorzy
/// i SYSTEM w dowolnej postaci, a Users i Authenticated Users tylko jako allow z prawami odczytu (<see cref="UsersReadMask"/>).
/// Plik: właściciel i każde ACE wyłącznie Administratorzy albo SYSTEM. Brak DACL, ACE obiektowy, złożony albo nieznany = niezaufany.
/// </summary>
internal static class AclTrust
{
    /// <summary><c>ReadAndExecute | Synchronize</c>: przeglądanie folderu i odczyt plików, bez żadnego prawa zapisu.</summary>
    public const int UsersReadMask = 0x1200A9;

    private const AceFlags CurrentAceFlags = AceFlags.ObjectInherit | AceFlags.ContainerInherit;

    /// <summary><c>FileSystemRights.FullControl</c>.</summary>
    private const int FullControlMask = 0x1F01FF;

    public static SecurityIdentifier UsersSid { get; } = new(WellKnownSidType.BuiltinUsersSid, null);

    public static SecurityIdentifier AuthenticatedUsersSid { get; } = new(WellKnownSidType.AuthenticatedUserSid, null);

    public static bool IsTrustedDirectory(RawSecurityDescriptor descriptor)
    {
        if (!HasTrustedOwner(descriptor) || descriptor.DiscretionaryAcl is not { } dacl)
        {
            return false;
        }

        foreach (var ace in dacl)
        {
            if (ace is not CommonAce common || common.IsCallback)
            {
                return false;
            }

            if (IsAdministratorsOrSystem(common.SecurityIdentifier))
            {
                continue;
            }

            var isUsers = common.SecurityIdentifier.Equals(UsersSid) || common.SecurityIdentifier.Equals(AuthenticatedUsersSid);
            if (!isUsers || common.AceQualifier != AceQualifier.AccessAllowed || (common.AccessMask & ~UsersReadMask) != 0)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsTrustedFile(RawSecurityDescriptor descriptor)
    {
        if (!HasTrustedOwner(descriptor) || descriptor.DiscretionaryAcl is not { } dacl)
        {
            return false;
        }

        foreach (var ace in dacl)
        {
            if (ace is not CommonAce common || !IsAdministratorsOrSystem(common.SecurityIdentifier))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><see cref="IsTrustedFile"/> i DACL chroniony (bez dziedziczenia z folderu).</summary>
    public static bool IsProtectedAdminOnlyFile(RawSecurityDescriptor descriptor)
        => IsTrustedFile(descriptor) && (descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0;

    /// <summary>
    /// Folder ma dokładnie bieżący deskryptor programu: właściciel Administratorzy, DACL chroniony i dokładnie trzy jawne ACE
    /// allow z dziedziczeniem <c>ObjectInherit | ContainerInherit</c> (kolejność dowolna): SYSTEM i Administratorzy
    /// <c>FullControl</c>, Users <see cref="UsersReadMask"/>. Każda różnica (brak, nadmiar, inna maska albo flagi) = deskryptor
    /// do przepisania w miejscu (o ile folder jest zaufany).
    /// </summary>
    public static bool HasCurrentDirectoryDescriptor(RawSecurityDescriptor descriptor)
    {
        if (descriptor.Owner is null
            || !descriptor.Owner.Equals(SecureDirectory.AdministratorsSid)
            || (descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0
            || descriptor.DiscretionaryAcl is not { Count: 3 } dacl)
        {
            return false;
        }

        bool system = false, administrators = false, users = false;
        foreach (var ace in dacl)
        {
            if (ace is not CommonAce { IsCallback: false } common
                || common.AceQualifier != AceQualifier.AccessAllowed
                || common.AceFlags != CurrentAceFlags)
            {
                return false;
            }

            var sid = common.SecurityIdentifier;
            if (sid.Equals(SecureDirectory.SystemSid) && common.AccessMask == FullControlMask && !system)
            {
                system = true;
            }
            else if (sid.Equals(SecureDirectory.AdministratorsSid) && common.AccessMask == FullControlMask && !administrators)
            {
                administrators = true;
            }
            else if (sid.Equals(UsersSid) && common.AccessMask == UsersReadMask && !users)
            {
                users = true;
            }
            else
            {
                return false;
            }
        }

        return system && administrators && users;
    }

    private static bool HasTrustedOwner(RawSecurityDescriptor descriptor)
        => descriptor.Owner is { } owner && IsAdministratorsOrSystem(owner);

    private static bool IsAdministratorsOrSystem(SecurityIdentifier sid)
        => sid.Equals(SecureDirectory.AdministratorsSid) || sid.Equals(SecureDirectory.SystemSid);
}
