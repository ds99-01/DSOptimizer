using System;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows.Apps;

/// <summary>
/// Rozwija napis pośredni przez <c>SHLoadIndirectString</c>: <c>@%SystemRoot%\system32\mstsc.exe,-4000</c> (Win32 <c>DisplayName_Localized</c>)
/// albo <c>@{&lt;PackageFullName&gt;?ms-resource://&lt;Name&gt;/Resources/&lt;klucz&gt;}</c> (AppX). Język zależy od konta procesu.
/// </summary>
public static class IndirectStringLoader
{
    private const uint BufferChars = 1024;

    /// <summary>Rozwinięty tekst albo null (to nie napis pośredni, brak zasobu, błąd API).</summary>
    public static string? TryLoad(string source)
    {
        // Zwykły tekst API zwraca bez zmian, a to nie jest rozwinięcie: przyjmujemy tylko napisy pośrednie (zaczynają się od „@”).
        if (string.IsNullOrWhiteSpace(source) || source[0] != '@')
        {
            return null;
        }

        var buffer = new char[BufferChars];
        try
        {
            var hresult = NativeMethods.SHLoadIndirectString(source, buffer, BufferChars, IntPtr.Zero);
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
}
