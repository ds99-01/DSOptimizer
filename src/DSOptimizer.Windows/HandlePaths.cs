using System;
using System.IO;
using DSOptimizer.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Windows;

/// <summary>
/// Ostateczna ścieżka otwartego uchwytu (<c>GetFinalPathNameByHandleW</c>) i jej porównanie z żądaną ścieżką. Różnica oznacza
/// link (junction albo dowiązanie symboliczne) w którymś członie pośrednim: uchwyt wskazuje inny folder niż żądany.
/// </summary>
internal static class HandlePaths
{
    /// <summary>
    /// <c>true</c> tylko, gdy ostateczna ścieżka uchwytu (bez prefiksu <c>\\?\</c>) jest równa żądanej (pełnej, z członami 8.3
    /// rozwiniętymi, bez rozróżniania wielkości liter). Brak ostatecznej ścieżki (np. wolumin bez litery dysku) = <c>false</c>.
    /// </summary>
    public static bool IsAtRequestedPath(SafeFileHandle handle, string path)
    {
        var final = FinalPath(handle);
        return final is not null
            && string.Equals(WithoutDevicePrefix(final).TrimEnd('\\'), RequestedPath(path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ostateczna ścieżka uchwytu w postaci DOS z prefiksem <c>\\?\</c>; <c>null</c>, gdy nie da się jej odczytać.</summary>
    public static string? FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        while (true)
        {
            var length = NativeMethods.GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Length)
            {
                return new string(buffer, 0, (int)length);
            }

            buffer = new char[length + 1];
        }
    }

    /// <summary><c>\\?\C:\x</c> → <c>C:\x</c>, <c>\\?\UNC\serwer\udział</c> → <c>\\serwer\udział</c> (postać z <c>GetFinalPathNameByHandleW</c>).</summary>
    public static string WithoutDevicePrefix(string path)
    {
        const string Device = @"\\?\";
        const string DeviceUnc = @"\\?\UNC\";
        if (path.StartsWith(DeviceUnc, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path.Substring(DeviceUnc.Length);
        }

        return path.StartsWith(Device, StringComparison.Ordinal) ? path.Substring(Device.Length) : path;
    }

    /// <summary>Żądana ścieżka: pełna, bez końcowego separatora, z członami 8.3 rozwiniętymi do długich nazw (bez rozwiązywania linków).</summary>
    public static string RequestedPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.IndexOf('~') >= 0)
        {
            var buffer = new char[512];
            var length = NativeMethods.GetLongPathName(full, buffer, (uint)buffer.Length);
            if (length >= buffer.Length)
            {
                buffer = new char[length + 1];
                length = NativeMethods.GetLongPathName(full, buffer, (uint)buffer.Length);
            }

            if (length > 0 && length < buffer.Length)
            {
                full = new string(buffer, 0, (int)length);
            }
        }

        return full.TrimEnd('\\');
    }
}
