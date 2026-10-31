using System;
using System.Text.RegularExpressions;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows.Apps;

/// <summary>
/// Rozwija napis pośredni przez <c>SHLoadIndirectString</c>: <c>@%SystemRoot%\system32\mstsc.exe,-4000</c> (Win32 <c>DisplayName_Localized</c>)
/// albo <c>@{&lt;PackageFullName&gt;?ms-resource://&lt;Name&gt;/Resources/&lt;klucz&gt;}</c> (AppX). Język zależy od konta procesu.
/// </summary>
public static class IndirectStringLoader
{
    private const uint BufferChars = 1024;

    private static readonly Regex PackageResource = new(@"^@\{[A-Za-z0-9._~+-]+\?ms-resource:[^\\]*\}$", RegexOptions.CultureInvariant);

    private static readonly Regex ResourceSuffix = new(@",-[0-9]+(;.*)?$", RegexOptions.CultureInvariant);

    /// <summary>Rozwinięty tekst albo null (to nie napis pośredni, źródło poza dozwolonymi, brak zasobu, błąd API).</summary>
    public static string? TryLoad(string source)
    {
        var allowed = ResolveSource(
            source,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        if (allowed is null)
        {
            return null;
        }

        var buffer = new char[BufferChars];
        try
        {
            var hresult = NativeMethods.SHLoadIndirectString(allowed, buffer, BufferChars, IntPtr.Zero);
            if (hresult < 0)
            {
                return null;
            }

            var end = Array.IndexOf(buffer, (char)0);
            var text = new string(buffer, 0, end < 0 ? buffer.Length : end);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.SEHException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Napis do API albo null. <c>SHLoadIndirectString</c> otwiera też plik z UNC (połączenie SMB poświadczeniami admina), a wartość
    /// <c>DisplayName_Localized</c> z rejestru konta może ustawić zwykły użytkownik. Dozwolone są tylko zasób pakietu (nazwa, nie ścieżka)
    /// i zasób pliku pod chronionymi folderami. Zmienne rozwijamy sami ze stałych ścieżek, bo środowisko procesu pochodzi też z konta użytkownika.
    /// </summary>
    internal static string? ResolveSource(string source, string windows, string programFiles, string programFilesX86)
    {
        if (string.IsNullOrWhiteSpace(source) || source[0] != '@')
        {
            return null;
        }

        if (source.StartsWith("@{", StringComparison.Ordinal))
        {
            return PackageResource.IsMatch(source) ? source : null;
        }

        var resolved = source
            .ReplaceIgnoreCase("%SystemRoot%", windows)
            .ReplaceIgnoreCase("%windir%", windows)
            .ReplaceIgnoreCase("%ProgramFiles%", programFiles)
            .ReplaceIgnoreCase("%ProgramFiles(x86)%", programFilesX86);
        var suffix = ResourceSuffix.Match(resolved);
        if (!suffix.Success)
        {
            return null;
        }

        var file = resolved.Substring(1, suffix.Index - 1);
        var clean = file.IndexOf('%') < 0
            && file.IndexOf('/') < 0
            && file.LastIndexOf(':') <= 1
            && file.IndexOf(@"\\", StringComparison.Ordinal) < 0
            && file.IndexOf("..", StringComparison.Ordinal) < 0;
        return clean && (IsUnder(file, windows) || IsUnder(file, programFiles) || IsUnder(file, programFilesX86)) ? resolved : null;
    }

    private static bool IsUnder(string file, string folder)
        => folder.Length > 0 && file.StartsWith(folder.TrimEnd('\\') + @"\", StringComparison.OrdinalIgnoreCase);

    private static string ReplaceIgnoreCase(this string text, string variable, string value)
    {
        var index = text.IndexOf(variable, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? text : text.Substring(0, index) + value + text.Substring(index + variable.Length);
    }
}
