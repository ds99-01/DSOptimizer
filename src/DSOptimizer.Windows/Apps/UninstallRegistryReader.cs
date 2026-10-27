using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using Microsoft.Win32;

namespace DSOptimizer.Windows.Apps;

/// <summary>
/// Czyta wpisy <c>Uninstall</c> programów Win32 z podanych korzeni rejestru. Tylko odczyt.
/// Korzeń to (ścieżka podklucza, gałąź, widok); dla gałęzi <see cref="RegistryHive.Users"/> ścieżka zaczyna się od SID konta.
/// </summary>
public sealed class UninstallRegistryReader
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UserUninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly (string Root, RegistryHive Hive, RegistryView View)[] _roots;
    private readonly Func<string, string?> _loadIndirect;

    public UninstallRegistryReader(IEnumerable<(string Root, RegistryHive Hive, RegistryView View)> roots, Func<string, string?>? loadIndirect = null)
    {
        _roots = (roots ?? throw new ArgumentNullException(nameof(roots))).ToArray();
        _loadIndirect = loadIndirect ?? IndirectStringLoader.TryLoad;
    }

    /// <summary>
    /// Produkcyjne korzenie: <c>HKLM</c> w widoku 64- i 32-bitowym (ten drugi to <c>WOW6432Node</c>) oraz <c>HKU\&lt;SID&gt;</c> zalogowanego konta.
    /// Gałąź użytkownika tylko w widoku 64-bitowym (przekierowanie WOW64 nie dotyczy <c>HKU\&lt;SID&gt;\Software</c>).
    /// </summary>
    public static UninstallRegistryReader ForUser(string userSid)
    {
        // Poprawny SID chroni przed zbudowaniem ścieżki z dowolnego tekstu.
        var sid = new SecurityIdentifier(userSid).Value;
        return new UninstallRegistryReader(new[]
        {
            (UninstallPath, RegistryHive.LocalMachine, RegistryView.Registry64),
            (UninstallPath, RegistryHive.LocalMachine, RegistryView.Registry32),
            (sid + @"\" + UserUninstallPath, RegistryHive.Users, RegistryView.Registry64),
        });
    }

    /// <summary>
    /// Czy klucz o pełnej ścieżce (<c>HKLM\…</c>, <c>HKU\…</c>, <c>HKCU\…</c>, widok 64-bit) istnieje. Brak dostępu albo błąd odczytu
    /// = null (nie wiadomo; to nie jest dowód zniknięcia).
    /// </summary>
    public static bool? KeyExists(string keyPath)
    {
        if (keyPath is null)
        {
            throw new ArgumentNullException(nameof(keyPath));
        }

        var (hive, subKey) = SplitKeyPath(keyPath);
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            return key is not null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Pełna ścieżka klucza z rzeczywistego korzenia. Widok 32-bit gałęzi HKLM\SOFTWARE to realna ścieżka <c>SOFTWARE\WOW6432Node\…</c> (otwieralna w widoku 64-bit).</summary>
    internal static string KeyPathFor(RegistryHive hive, RegistryView view, string root, string keyName)
    {
        var prefix = hive switch
        {
            RegistryHive.LocalMachine => @"HKLM\",
            RegistryHive.Users => @"HKU\",
            RegistryHive.CurrentUser => @"HKCU\",
            _ => throw new ArgumentException("Nieobsługiwana gałąź rejestru: " + hive, nameof(hive)),
        };

        var path = root;
        if (hive == RegistryHive.LocalMachine && view == RegistryView.Registry32)
        {
            var slash = root.IndexOf('\\');
            if (slash > 0 && root.Substring(0, slash).Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase)
                && !root.Substring(slash + 1).StartsWith("WOW6432Node", StringComparison.OrdinalIgnoreCase))
            {
                path = root.Substring(0, slash) + @"\WOW6432Node" + root.Substring(slash);
            }
        }

        return prefix + path + @"\" + keyName;
    }

    public InstalledAppsReading Read()
    {
        var apps = new List<InstalledApp>();
        var warnings = new List<string>();
        var skipped = 0;
        foreach (var (root, hive, view) in _roots)
        {
            try
            {
                skipped += ReadRoot(root, hive, view, apps);
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                warnings.Add(AppsText.RegistryReadFailed(ex.Message));
            }
        }

        if (skipped > 0)
        {
            warnings.Add(AppsText.RegistryKeysSkipped(skipped));
        }

        return new InstalledAppsReading(apps, warnings);
    }

    private static (RegistryHive Hive, string SubKey) SplitKeyPath(string keyPath)
    {
        foreach (var (prefix, hive) in new[] { (@"HKLM\", RegistryHive.LocalMachine), (@"HKU\", RegistryHive.Users), (@"HKCU\", RegistryHive.CurrentUser) })
        {
            if (keyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (hive, keyPath.Substring(prefix.Length));
            }
        }

        throw new ArgumentException("Nieobsługiwany korzeń ścieżki rejestru: " + keyPath, nameof(keyPath));
    }

    private int ReadRoot(string root, RegistryHive hive, RegistryView view, List<InstalledApp> apps)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var uninstall = baseKey.OpenSubKey(root, writable: false);
        if (uninstall is null)
        {
            return 0;
        }

        var (scope, ownerSid) = OwnerOf(root, hive);
        var skipped = 0;
        foreach (var keyName in uninstall.GetSubKeyNames())
        {
            try
            {
                using var key = uninstall.OpenSubKey(keyName, writable: false);
                if (key is not null && ReadApp(key, KeyPathFor(hive, view, root, keyName), scope, ownerSid) is { } app)
                {
                    apps.Add(app);
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                skipped++;
            }
        }

        return skipped;
    }

    private static (AppScope Scope, string? OwnerSid) OwnerOf(string root, RegistryHive hive)
        => hive switch
        {
            RegistryHive.Users => (AppScope.User, root.Split('\\')[0]),
            RegistryHive.CurrentUser => (AppScope.User, WindowsIdentity.GetCurrent().User?.Value),
            _ => (AppScope.Machine, null),
        };

    private InstalledApp? ReadApp(RegistryKey key, string keyPath, AppScope scope, string? ownerSid)
    {
        var name = Text(key, "DisplayName");
        if (name is null)
        {
            // Wpisy bez DisplayName (np. mstsc) mają tylko DisplayName_Localized w postaci napisu pośredniego.
            var localized = Text(key, "DisplayName_Localized");
            name = localized is null ? null : Clean(_loadIndirect(localized));
        }

        if (name is null)
        {
            return null;
        }

        var win32 = new Win32Info(
            keyPath,
            Text(key, "UninstallString"),
            Text(key, "QuietUninstallString"),
            Flag(key, "WindowsInstaller"),
            Text(key, "ParentKeyName"),
            Text(key, "ReleaseType"),
            Flag(key, "SystemComponent"));
        return new InstalledApp(
            "win32:" + keyPath,
            AppKind.Program,
            name,
            Text(key, "Publisher"),
            Text(key, "DisplayVersion"),
            scope,
            ownerSid,
            null,
            win32);
    }

    // GetValue domyślnie rozwija REG_EXPAND_SZ (zmienne środowiska procesu); inne typy niż napis są ignorowane.
    private static string? Text(RegistryKey key, string name)
        => Clean(key.GetValue(name) as string);

    private static string? Clean(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

    private static bool Flag(RegistryKey key, string name)
        => key.GetValue(name) switch
        {
            int number => number == 1,
            string text => text.Trim() == "1",
            _ => false,
        };
}
