using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows.Autostart;

/// <summary>
/// Pliki autostartu na Windows – wyłącznie odczyt. Foldery: <see cref="Directory.EnumerateFiles(string)"/> bez podfolderów. Skróty: nagłówek
/// <c>.lnk</c> czytany z <c>FileShare.ReadWrite | FileShare.Delete</c>, potem COM <c>ShellLink</c> w procesie (<c>IPersistFile.Load</c> z
/// <c>STGM_READ</c>, ścieżka surowa <c>SLGP_RAWPATH</c>, bez <c>Resolve</c>). Sprawdzenie pliku: tylko lokalny dysk stały, przez atrybuty
/// (<c>GetFileAttributesExW</c>), bez otwierania pliku. Wersja: <see cref="FileVersionInfo"/>.
/// </summary>
public sealed class AutostartFiles : IAutostartFiles
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    /// <summary><c>MAX_PATH</c>: dłuższej ścieżki bez prefiksu <c>\\?\</c> API nie sprawdzi wiarygodnie, więc wynik jest nieznany.</summary>
    private const int MaxPath = 260;

    private const int TextChars = 32768;

    /// <summary>Nagłówek pliku skrótu: <c>HeaderSize</c> 0x4C i <c>LinkCLSID</c> {00021401-0000-0000-C000-000000000046}.</summary>
    private static readonly byte[] LinkHeader =
    {
        0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
    };

    private readonly Func<string, uint> _driveType;
    private readonly Func<string, (bool Found, uint Attributes, int Error)> _attributes;

    public AutostartFiles()
        : this(NativeMethods.GetDriveType, NativeAttributes)
    {
    }

    /// <summary>Szew testów: typ dysku (korzeń <c>X:\</c>) i atrybuty pliku zamiast wywołań systemu.</summary>
    internal AutostartFiles(Func<string, uint> driveType, Func<string, (bool Found, uint Attributes, int Error)> attributes)
    {
        _driveType = driveType ?? throw new ArgumentNullException(nameof(driveType));
        _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public IReadOnlyList<string> ListFolder(string folder)
    {
        if (folder is null)
        {
            throw new ArgumentNullException(nameof(folder));
        }

        try
        {
            return Directory.EnumerateFiles(folder).Select(Path.GetFileName).ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień, by wylistować folder {0}: {1}", folder, ex.Message), ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można wylistować folderu {0}: {1}", folder, ex.Message), ex);
        }
    }

    public ShortcutInfo? ReadShortcut(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (!HasLinkHeader(path))
        {
            return null;
        }

        IShellLinkW? link = null;
        try
        {
            link = ShellLinkInterop.Create();
            ((IPersistFile)link).Load(path, ShellLinkInterop.StgmRead);
            var target = new StringBuilder(TextChars);
            link.GetPath(target, target.Capacity, IntPtr.Zero, ShellLinkInterop.SlgpRawPath);
            var arguments = new StringBuilder(TextChars);
            link.GetArguments(arguments, arguments.Capacity);
            ((IShellLinkDataList)link).GetFlags(out var flags);
            return new ShortcutInfo(Text(target), Text(arguments), (flags & ShellLinkInterop.SldfHasDarwinId) != 0);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień, by odczytać plik {0}: {1}", path, ex.Message), ex);
        }
        catch (Exception ex) when (ex is COMException or IOException or InvalidCastException or ArgumentException)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można odczytać skrótu {0}: {1}", path, ex.Message), ex);
        }
        finally
        {
            if (link is not null)
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }

    public FileProbe Probe(string fullPath)
    {
        if (!IsLocalAbsolute(fullPath))
        {
            return FileProbe.Unknown;
        }

        if (_driveType(fullPath.Substring(0, 3)) != NativeMethods.DriveFixed)
        {
            return FileProbe.Unknown;
        }

        var (found, attributes, error) = _attributes(fullPath);
        if (found)
        {
            return (attributes & NativeMethods.FileAttributeDirectory) != 0 ? FileProbe.Unknown : FileProbe.Exists;
        }

        return error is ErrorFileNotFound or ErrorPathNotFound ? FileProbe.Missing : FileProbe.Unknown;
    }

    public FileDetails? Details(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return null;
        }

        FileVersionInfo info;
        try
        {
            info = FileVersionInfo.GetVersionInfo(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        var company = Clean(info.CompanyName);
        var description = Clean(info.FileDescription);
        var product = Clean(info.ProductName);
        return company is null && description is null && product is null ? null : new FileDetails(company, description, product);
    }

    /// <summary>Ścieżka <c>X:\…</c> krótsza niż <c>MAX_PATH</c>; UNC, <c>\\?\</c>, ścieżka względna i <c>X:plik</c> – nie.</summary>
    private static bool IsLocalAbsolute(string? path)
        => path is { Length: >= 3 and < MaxPath }
            && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
            && path[1] == ':'
            && path[2] == '\\'
            && path.IndexOf('\0') < 0;

    private static (bool Found, uint Attributes, int Error) NativeAttributes(string path)
        => NativeMethods.GetFileAttributesEx(path, NativeMethods.FileExInfoStandard, out var data)
            ? (true, data.FileAttributes, 0)
            : (false, 0u, Marshal.GetLastWin32Error());

    /// <summary>Czy plik zaczyna się nagłówkiem skrótu; plik krótszy albo inny = nie skrót. Błąd otwarcia = <see cref="OperationException"/>.</summary>
    private static bool HasLinkHeader(string path)
    {
        var header = new byte[LinkHeader.Length];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var read = 0;
            while (read < header.Length)
            {
                var count = stream.Read(header, read, header.Length - read);
                if (count == 0)
                {
                    return false;
                }

                read += count;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień, by odczytać plik {0}: {1}", path, ex.Message), ex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new OperationException(OperationErrorKind.NotFound, L.F("Nie można odczytać plik {0}: {1}", path, ex.Message), ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można odczytać plik {0}: {1}", path, ex.Message), ex);
        }

        return header.SequenceEqual(LinkHeader);
    }

    private static string? Text(StringBuilder builder) => Clean(builder.ToString());

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
