using System;
using System.IO;
using System.Runtime.InteropServices;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows;

/// <summary>
/// Linki na ścieżce zapisu: junction i dowiązanie symboliczne (znacznik punktu ponownej analizy z bitem name surrogate)
/// skierowałyby zapis programu działającego jako admin do dowolnego folderu. Inne punkty ponownej analizy (placeholdery OneDrive,
/// aliasy aplikacji) nie są linkami. Atrybut punktu ponownej analizy bez odczytanego znacznika = link (bezpieczniej odmówić).
/// </summary>
internal static class ReparseLinks
{
    /// <summary>Bit name surrogate w znaczniku punktu ponownej analizy: element wskazuje inną nazwę w przestrzeni nazw.</summary>
    public const uint NameSurrogateBit = 0x20000000;

    public static bool IsNameSurrogate(uint reparseTag) => (reparseTag & NameSurrogateBit) != 0;

    /// <summary>
    /// True, gdy istniejący plik albo folder jest punktem ponownej analizy ze znacznikiem name surrogate albo nieodczytanym.
    /// Brak elementu = false. Otwarcie bez podążania za linkiem nie pobiera placeholdera z chmury.
    /// </summary>
    public static bool IsLink(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }

        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        using var handle = NativeMethods.CreateFileHandle(
            path,
            NativeMethods.FileReadAttributes,
            NativeMethods.FileShareAll,
            IntPtr.Zero,
            NativeMethods.OpenExistingDisposition,
            NativeMethods.FileFlagOpenReparsePoint | NativeMethods.FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid
            || !NativeMethods.GetFileInformationByHandleEx(
                handle,
                NativeMethods.FileAttributeTagInfoClass,
                out var info,
                (uint)Marshal.SizeOf<NativeMethods.FileAttributeTagInfo>()))
        {
            return true;
        }

        return IsNameSurrogate(info.ReparseTag);
    }

    /// <summary>Pierwszy link (pełna ścieżka) na drodze od elementu przez wszystkich przodków; <c>null</c>, gdy żadnego nie ma.</summary>
    public static string? FirstLink(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (IsLink(current.FullName))
            {
                return current.FullName;
            }
        }

        return null;
    }
}
