using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DSOptimizer.Core.Apps;

/// <summary>Sposób uruchomienia deinstalatora: po cichu, przez msiexec albo z oknem deinstalatora.</summary>
public enum UninstallMode
{
    Quiet,
    Msi,
    Interactive,
}

/// <summary>Plan uruchomienia: pełna ścieżka istniejącego pliku, argumenty i tryb. Start wyłącznie przez CreateProcess.</summary>
public sealed record UninstallPlan(string FileName, string Arguments, UninstallMode Mode);

/// <summary>
/// Zamienia wpis Uninstall w bezpieczny plan uruchomienia albo odmawia. Kolejność: <c>QuietUninstallString</c>,
/// MSI (<c>msiexec /x {kod} /qn /norestart</c>), <c>UninstallString</c> interaktywnie. Nic nie przechodzi przez powłokę.
/// Każde wywołanie msiexec jest sprowadzane do cichego usunięcia kodu produktu (nigdy naprawa ani instalacja).
/// </summary>
public static class UninstallCommand
{
    private const string MsiexecFile = "msiexec.exe";

    // Interpretery, powłoki i narzędzia systemowe, które nigdy nie są deinstalatorem (po nazwie pliku, w dowolnym katalogu).
    private static readonly HashSet<string> RefusedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "powershell_ise.exe", "wscript.exe", "cscript.exe", "mshta.exe",
        "rundll32.exe", "regsvr32.exe", "explorer.exe", "conhost.exe", "wmic.exe", "bitsadmin.exe", "certutil.exe",
        "schtasks.exe", "reg.exe", "sc.exe", "msbuild.exe", "installutil.exe",
    };

    private static readonly Regex ProductCode = new(
        @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex DrivePath = new(@"^[A-Za-z]:\\", RegexOptions.CultureInvariant);

    /// <summary>Plan uruchomienia deinstalatora albo <c>(null, powód)</c>. Ścieżki sprawdza <paramref name="fileExists"/>.</summary>
    public static (UninstallPlan? Plan, string? Reason) Plan(Win32Info info, Func<string, bool> fileExists, string system32)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        if (fileExists is null)
        {
            throw new ArgumentNullException(nameof(fileExists));
        }

        var plan = FromCommand(info.QuietUninstallString, UninstallMode.Quiet, fileExists, system32)
            ?? FromMsiKey(info, fileExists, system32)
            ?? FromCommand(info.UninstallString, UninstallMode.Interactive, fileExists, system32);
        return plan is null ? (null, AppsText.NoSafeCommand) : (plan, null);
    }

    private static UninstallPlan? FromCommand(string? command, UninstallMode mode, Func<string, bool> fileExists, string system32)
    {
        var parsed = Parse(command, fileExists, system32);
        if (parsed is null)
        {
            return null;
        }

        if (!parsed.Value.Msiexec)
        {
            return new UninstallPlan(parsed.Value.File, parsed.Value.Arguments, mode);
        }

        // MsiExec.exe /I{GUID} otwiera ekran konserwacji (a /i naprawia lub instaluje); zamieniamy na ciche usunięcie tego samego kodu.
        var match = ProductCode.Match(parsed.Value.Arguments);
        return match.Success ? MsiPlan(match.Value, fileExists, system32) : null;
    }

    private static UninstallPlan? FromMsiKey(Win32Info info, Func<string, bool> fileExists, string system32)
    {
        if (!info.WindowsInstaller)
        {
            return null;
        }

        var keyName = info.KeyPath.Substring(info.KeyPath.LastIndexOf('\\') + 1);
        var code = ProductCode.Match(keyName);
        return code.Success && code.Length == keyName.Length ? MsiPlan(keyName, fileExists, system32) : null;
    }

    private static UninstallPlan? MsiPlan(string productCode, Func<string, bool> fileExists, string system32)
    {
        var msiexec = Path.Combine(system32, MsiexecFile);
        return fileExists(msiexec)
            ? new UninstallPlan(msiexec, "/x " + productCode + " /qn /norestart", UninstallMode.Msi)
            : null;
    }

    private static (string File, string Arguments, bool Msiexec)? Parse(string? command, Func<string, bool> fileExists, string system32)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command!.Trim();
        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            return close < 0 ? null : Resolve(text.Substring(1, close - 1), text.Substring(close + 1).Trim(), fileExists, system32);
        }

        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ' ')
            {
                continue;
            }

            var candidate = text.Substring(0, i);
            var resolved = Resolve(candidate, i < text.Length ? text.Substring(i + 1).Trim() : string.Empty, fileExists, system32);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static (string File, string Arguments, bool Msiexec)? Resolve(string file, string arguments, Func<string, bool> fileExists, string system32)
    {
        if (IsMsiexec(file, system32))
        {
            // Zawsze System32, także gdy wpis wskazuje SysWOW64 albo gołą nazwę.
            return (Path.Combine(system32, MsiexecFile), arguments, true);
        }

        var safe = DrivePath.IsMatch(file)
            && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && file.IndexOf("..", StringComparison.Ordinal) < 0
            && file.IndexOf('%') < 0
            && !RefusedFiles.Contains(file.Substring(file.LastIndexOf('\\') + 1))
            && fileExists(file);
        return safe ? (file, arguments, false) : null;
    }

    private static bool IsMsiexec(string file, string system32)
    {
        if (string.Equals(file, "msiexec", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file, MsiexecFile, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parent = Path.GetDirectoryName(system32.TrimEnd('\\'));
        return string.Equals(file, Path.Combine(system32, MsiexecFile), StringComparison.OrdinalIgnoreCase)
            || (parent is not null && string.Equals(file, Path.Combine(parent, "SysWOW64", MsiexecFile), StringComparison.OrdinalIgnoreCase));
    }
}
