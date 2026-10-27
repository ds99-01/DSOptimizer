namespace DSOptimizer.Core.Apps;

/// <summary>Rodzaj zainstalowanej aplikacji: pakiet AppX (Sklep) albo program z wpisem Uninstall.</summary>
public enum AppKind
{
    Store,
    Program,
}

/// <summary>Zakres instalacji: cały komputer albo jedno konto.</summary>
public enum AppScope
{
    Machine,
    User,
}

/// <summary>Dane pakietu AppX potrzebne do klasyfikacji i usuwania.</summary>
public sealed record AppxInfo(
    string PackageFullName,
    string PackageFamilyName,
    string PackageName,
    bool IsFramework,
    bool IsResourcePackage,
    bool NonRemovable,
    string? SignatureKind,
    bool IsProvisioned);

/// <summary>Dane wpisu Uninstall programu Win32. <see cref="KeyPath"/> to pełna ścieżka <c>HKLM\…</c> albo <c>HKU\&lt;SID&gt;\…</c>.</summary>
public sealed record Win32Info(
    string KeyPath,
    string? UninstallString,
    string? QuietUninstallString,
    bool WindowsInstaller,
    string? ParentKeyName,
    string? ReleaseType,
    bool SystemComponent);

/// <summary>Zainstalowana aplikacja. Id: <c>appx:&lt;PackageFamilyName&gt;</c> albo <c>win32:&lt;KeyPath&gt;</c>.</summary>
public sealed record InstalledApp(
    string Id,
    AppKind Kind,
    string Name,
    string? Publisher,
    string? Version,
    AppScope Scope,
    string? OwnerSid,
    AppxInfo? Appx,
    Win32Info? Win32);
