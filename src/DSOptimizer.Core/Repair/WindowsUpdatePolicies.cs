using System.Collections.Generic;

namespace DSOptimizer.Core.Repair;

/// <summary>
/// Polityki Windows Update z pomiaru (Task 2, sekcja 4, VM 26H2 Home i Pro, 2026-10-22). Wariant podstawowy tylko je wypisuje,
/// rozszerzony eksportuje cały klucz (<c>reg export</c> obejmuje podklucz <c>AU</c>) i usuwa go z podkluczami.
/// </summary>
public static class WindowsUpdatePolicies
{
    public const string KeyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    public const string AuKeyPath = KeyPath + @"\AU";

    /// <summary>Wartości diagnozowane jako „mogą być przyczyną” (skutek widoczny zmierzono tylko dla <c>SetDisableUXWUAccess</c> na Pro).</summary>
    public static IReadOnlyList<(string Path, string Name)> DiagnosedValues { get; } = new[]
    {
        (KeyPath, "WUServer"),
        (KeyPath, "WUStatusServer"),
        (KeyPath, "DisableWindowsUpdateAccess"),
        (KeyPath, "SetDisableUXWUAccess"),
        (AuKeyPath, "NoAutoUpdate"),
        (AuKeyPath, "UseWUServer"),
    };
}
