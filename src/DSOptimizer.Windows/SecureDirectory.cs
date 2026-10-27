using System;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DSOptimizer.Windows;

/// <summary>
/// Katalog roboczy dostępny wyłącznie dla Administratorów i SYSTEM: właściciel = Administratorzy,
/// DACL chroniony (bez dziedziczenia). Katalog o niezaufanym pochodzeniu nie jest przejmowany w miejscu,
/// tylko odkładany na bok (stare uchwyty atakującego z WRITE_DAC nie przeżywają przejęcia).
/// </summary>
internal static class SecureDirectory
{
    public static SecurityIdentifier AdministratorsSid { get; } = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static SecurityIdentifier SystemSid { get; } = new(WellKnownSidType.LocalSystemSid, null);

    /// <summary>
    /// Nie istnieje → tworzony od razu z zaufanym deskryptorem. Istnieje i zaufany → bez zmian. Istnieje i niezaufany
    /// (obcy właściciel/ACE) → odkładany na bok jako <c>.untrusted-*</c> i tworzony od nowa.
    /// Reparse point lub niemożność odłożenia → <see cref="IOException"/> (fail-safe).
    /// Zwraca ścieżkę odłożonego katalogu albo <c>null</c>, gdy nic nie odłożono.
    /// </summary>
    public static string? Ensure(string path)
    {
        path = NormalizePath(path);
        string? aside = null;
        if (Directory.Exists(path))
        {
            if (IsReparsePoint(path))
            {
                throw new IOException($"Katalog {path} jest punktem ponownej analizy (junction/link) - odmowa użycia.");
            }

            if (IsTrustedDirectory(path))
            {
                return null;
            }

            aside = SetAside(path);
        }

        Directory.CreateDirectory(path, CreateTrustedDirectorySecurity());
        if (IsReparsePoint(path))
        {
            throw new IOException($"Katalog {path} jest punktem ponownej analizy (junction/link) - odmowa użycia.");
        }

        if (!IsTrustedDirectory(path))
        {
            throw new IOException($"Katalog {path} nie ma zaufanego właściciela/uprawnień po utworzeniu - odmowa użycia.");
        }

        return aside;
    }

    public static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// Plik zaufany: nie jest reparse pointem, właściciel to Administratorzy/SYSTEM i KAŻDE ACE w DACL dotyczy wyłącznie
    /// Administratorów/SYSTEM. Nie da się odczytać deskryptora → niezaufany.
    /// </summary>
    public static bool IsTrustedFile(string filePath)
        => IsTrustedPath(filePath, path => File.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access));

    /// <summary>Katalog zaufany wg tej samej reguły co <see cref="IsTrustedFile"/>.</summary>
    public static bool IsTrustedDirectory(string directoryPath)
        => IsTrustedPath(directoryPath, path => Directory.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access));

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

    private static bool IsTrustedPath(string path, Func<string, FileSystemSecurity> readSecurity)
    {
        try
        {
            if (IsReparsePoint(path))
            {
                return false;
            }

            var security = readSecurity(path);
            if (!(security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner) || !IsTrusted(owner))
            {
                return false;
            }

            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (!(rule.IdentityReference is SecurityIdentifier sid) || !IsTrusted(sid))
                {
                    return false;
                }
            }

            return true;
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
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"Katalog {path} ma niezaufanego właściciela/uprawnienia i nie udało się go odłożyć: {ex.Message}", ex);
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

        return security;
    }

    internal static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length ? full.TrimEnd('\\') : full;
    }

    private static bool IsTrusted(SecurityIdentifier sid) => sid.Equals(AdministratorsSid) || sid.Equals(SystemSid);
}
