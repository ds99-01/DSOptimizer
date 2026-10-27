using System;
using System.Collections.Generic;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Żądanie uruchomienia narzędzia z System32 (np. <c>reg.exe</c>, <c>dism.exe</c>). <see cref="FileName"/> to sama nazwa
/// <c>*.exe</c> (reguły jak <see cref="LaunchTarget"/>: bez ścieżki, <c>..</c>, spacji i cudzysłowów); interpretery, hosty
/// skryptów i narzędzia do pobierania albo uruchamiania cudzego kodu są odrzucane według tej samej listy co w
/// <see cref="Apps.UninstallCommand"/> (<c>cmd</c>, <c>powershell</c>, <c>pwsh</c>, <c>wscript</c>, <c>cscript</c>, <c>mshta</c>,
/// <c>rundll32</c>, <c>regsvr32</c>, <c>explorer</c>, <c>conhost</c>, <c>wmic</c>, <c>bitsadmin</c>, <c>certutil</c>,
/// <c>schtasks</c>, <c>sc</c>, <c>msbuild</c>, <c>installutil</c>) – z jednym wyjątkiem: <c>reg.exe</c> zostaje dozwolony, bo
/// rozszerzony reset Windows Update eksportuje nim polityki (<c>reg.exe export</c>). <c>takeown</c>, <c>icacls</c>, <c>dism</c>,
/// <c>sfc</c> i <c>cleanmgr</c> są dozwolone. Niedozwolona nazwa albo <see cref="Timeout"/> ≤ 0 = <see cref="ArgumentException"/>
/// już przy ustawianiu, więc żaden proces nie powstaje.
/// </summary>
public sealed class ToolRequest
{
    private static readonly HashSet<string> RefusedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "powershell_ise.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe",
        "rundll32.exe", "regsvr32.exe", "explorer.exe", "conhost.exe", "wmic.exe", "bitsadmin.exe", "certutil.exe",
        "schtasks.exe", "sc.exe", "msbuild.exe", "installutil.exe",
    };

    private readonly string _fileName = string.Empty;
    private readonly TimeSpan _timeout = DefaultTimeout;

    /// <summary>Domyślny limit czasu narzędzia.</summary>
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(60);

    /// <summary>Sama nazwa pliku <c>*.exe</c> z System32.</summary>
    public required string FileName
    {
        get => _fileName;
        init
        {
            if (!IsAllowedFileName(value))
            {
                throw new ArgumentException(
                    $"Niedozwolone narzędzie '{value}': dozwolona jest tylko nazwa pliku .exe z System32, bez interpreterów i hostów skryptów.",
                    nameof(value));
            }

            _fileName = value;
        }
    }

    /// <summary>Linia argumentów (po nazwie pliku w linii poleceń).</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Limit czasu (&gt; 0); po jego przekroczeniu drzewo procesów jest zabijane.</summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentException($"Limit czasu narzędzia musi być dodatni (podano {value}).", nameof(value));
            }

            _timeout = value;
        }
    }

    /// <summary>Czy nazwa jest dozwoloną nazwą narzędzia (bez wyjątku).</summary>
    public static bool IsAllowedFileName(string? file)
        => LaunchTarget.IsSystemFileName(file)
            && file!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !RefusedFiles.Contains(file);
}
