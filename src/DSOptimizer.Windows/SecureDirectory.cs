using System;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Windows;

/// <summary>
/// Katalog roboczy programu: zapis tylko Administratorzy i SYSTEM, odczyt Users. Właściciel = Administratorzy, DACL chroniony
/// (bez dziedziczenia z rodzica), Users mają dziedziczony odczyt, więc Eksplorator bez podniesienia uprawnień otwiera folder
/// bez pytania o dostęp. Regułę zaufania liczy <see cref="AclTrust"/>. Katalog o niezaufanym pochodzeniu nie jest przejmowany
/// w miejscu, tylko odkładany na bok (stare uchwyty atakującego z WRITE_DAC nie przeżywają przejęcia).
/// </summary>
internal static class SecureDirectory
{
    public static SecurityIdentifier AdministratorsSid { get; } = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static SecurityIdentifier SystemSid { get; } = new(WellKnownSidType.LocalSystemSid, null);

    /// <summary>
    /// Nie istnieje → tworzony od razu z bieżącym deskryptorem. Istnieje i zaufany → zostaje w miejscu; gdy nie ma bieżącego
    /// deskryptora (folder z wersji 4.0.7 lub starszej), deskryptor jest przepisywany w miejscu, a błąd przepisania zostawia
    /// stary, zaufany deskryptor (kolejne wywołanie próbuje ponownie). Istnieje i niezaufany (obcy właściciel/ACE, prawo zapisu
    /// dla Users) → odkładany na bok jako <c>.untrusted-*</c> i tworzony od nowa.
    /// Reparse point lub niemożność odłożenia → <see cref="IOException"/> (fail-safe).
    /// Nieudane przepisanie trafia do <paramref name="onRewriteFailed"/> (ścieżka, komunikat błędu), żeby trwały błąd był widoczny.
    /// Zwraca ścieżkę odłożonego katalogu albo <c>null</c>, gdy nic nie odłożono.
    /// </summary>
    public static string? Ensure(string path, Action<string, string>? onRewriteFailed = null)
    {
        path = NormalizePath(path);
        string? aside = null;
        if (Directory.Exists(path))
        {
            if (IsReparsePoint(path))
            {
                throw new IOException(L.F("Katalog {0} jest punktem ponownej analizy (junction/link) - odmowa użycia.", path));
            }

            var descriptor = TryReadDirectoryDescriptor(path);
            if (descriptor is not null && AclTrust.IsTrustedDirectory(descriptor))
            {
                if (!AclTrust.HasCurrentDirectoryDescriptor(descriptor) && !TryRewrite(path, out var reason))
                {
                    onRewriteFailed?.Invoke(path, reason);
                }

                return null;
            }

            aside = SetAside(path);
        }

        Directory.CreateDirectory(path, CreateTrustedDirectorySecurity());
        if (IsReparsePoint(path))
        {
            throw new IOException(L.F("Katalog {0} jest punktem ponownej analizy (junction/link) - odmowa użycia.", path));
        }

        if (!IsTrustedDirectory(path))
        {
            throw new IOException(L.F("Katalog {0} nie ma zaufanego właściciela/uprawnień po utworzeniu - odmowa użycia.", path));
        }

        return aside;
    }

    public static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// Plik zaufany: nie jest reparse pointem, właściciel to Administratorzy/SYSTEM i KAŻDE ACE w DACL dotyczy wyłącznie
    /// Administratorów/SYSTEM (<see cref="AclTrust.IsTrustedFile"/>). Nie da się odczytać deskryptora → niezaufany.
    /// </summary>
    public static bool IsTrustedFile(string filePath)
        => IsTrustedPath(filePath, ReadFileSecurity, AclTrust.IsTrustedFile);

    /// <summary>
    /// Katalog zaufany: nie jest reparse pointem i spełnia <see cref="AclTrust.IsTrustedDirectory"/> (zapis tylko Administratorzy
    /// i SYSTEM, Users najwyżej odczyt). Nie da się odczytać deskryptora → niezaufany.
    /// </summary>
    public static bool IsTrustedDirectory(string directoryPath)
        => IsTrustedPath(directoryPath, ReadDirectorySecurity, AclTrust.IsTrustedDirectory);

    /// <summary>Deskryptor pliku: właściciel Administratorzy, chroniony DACL tylko Administratorzy + SYSTEM (FullControl).</summary>
    public static FileSecurity CreateTrustedFileSecurity()
    {
        var security = new FileSecurity();
        security.SetOwner(AdministratorsSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { AdministratorsSid, SystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return security;
    }

    /// <summary>Nakłada <see cref="CreateTrustedFileSecurity"/> na istniejący plik.</summary>
    public static void SetTrustedOwnerAndProtectedAcl(string filePath)
        => File.SetAccessControl(filePath, CreateTrustedFileSecurity());

    private static bool IsTrustedPath(
        string path,
        Func<string, FileSystemSecurity> readSecurity,
        Func<RawSecurityDescriptor, bool> isTrusted)
    {
        try
        {
            return !IsReparsePoint(path) && isTrusted(ToRaw(readSecurity(path)));
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Deskryptor istniejącego katalogu albo <c>null</c>, gdy nie da się go odczytać (wtedy katalog jest niezaufany).</summary>
    private static RawSecurityDescriptor? TryReadDirectoryDescriptor(string path)
    {
        try
        {
            return ToRaw(ReadDirectorySecurity(path));
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Przepisuje zaufany katalog na bieżący deskryptor. System przenosi dziedziczone ACE na pliki i podkatalogi, które
    /// dziedziczą; pliki z chronionym DACL (journal, ustawienia, skrypty) zostają bez zmian. Błąd = <c>false</c> z komunikatem
    /// w <paramref name="reason"/>; zostaje stary, zaufany deskryptor, a kolejne <see cref="Ensure"/> próbuje ponownie.
    /// </summary>
    private static bool TryRewrite(string path, out string reason)
    {
        try
        {
            Directory.SetAccessControl(path, CreateTrustedDirectorySecurity());
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Kandydat na dawny odłożony folder <c>&lt;korzeń&gt;.untrusted-*</c> obok korzenia: zwykły folder (nie link) z właścicielem
    /// Administratorzy albo SYSTEM. Zwykłe konto może zakładać foldery w <c>C:\ProgramData</c>, ale nie z takim właścicielem.
    /// Błąd odczytu = nie jest kandydatem.
    /// </summary>
    public static bool IsAdminOwnedPlainFolder(string path)
    {
        try
        {
            if (IsReparsePoint(path))
            {
                return false;
            }

            var owner = Directory.GetAccessControl(path, AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
            return owner is not null && (owner.Equals(AdministratorsSid) || owner.Equals(SystemSid));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static FileSystemSecurity ReadFileSecurity(string path)
        => File.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access);

    private static FileSystemSecurity ReadDirectorySecurity(string path)
        => Directory.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access);

    private static RawSecurityDescriptor ToRaw(FileSystemSecurity security)
        => new(security.GetSecurityDescriptorBinaryForm(), 0);

    private static string SetAside(string path)
    {
        var aside = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.untrusted-{1:yyyyMMddHHmmssfff}-{2}",
            path,
            DateTime.Now,
            Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            Directory.Move(path, aside);
            return aside;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // .NET Framework zgłasza odmowę dostępu przy Directory.Move jako IOException, więc oba typy dostają ten sam opis.
            throw new IOException(L.F("Folder {0} miał zmienione uprawnienia i nie udało się go odłożyć na bok: {1}", path, ex.Message), ex);
        }
    }

    private static DirectorySecurity CreateTrustedDirectorySecurity()
    {
        var security = new DirectorySecurity();
        security.SetOwner(AdministratorsSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { AdministratorsSid, SystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        security.AddAccessRule(new FileSystemAccessRule(
            AclTrust.UsersSid,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    internal static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length ? full.TrimEnd('\\') : full;
    }
}
