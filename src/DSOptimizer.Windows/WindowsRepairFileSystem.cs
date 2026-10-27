using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;

namespace DSOptimizer.Windows;

/// <summary>
/// Operacje na folderach i plikach dla napraw. Błędy systemu plików są zamieniane na <see cref="OperationException"/>.
/// Usuwanie drzewa i starych plików sprawdza <see cref="FileAttributes.ReparsePoint"/> przy każdym elemencie: junction
/// i dowiązania symboliczne są usuwane jako same linki, bez wchodzenia do celu.
/// </summary>
public sealed class WindowsRepairFileSystem : IRepairFileSystem
{
    /// <summary>Najwyżej tyle ostatnich bajtów czyta <see cref="ReadSharedFrom"/> (segment CBS.log po SFC mieści się z zapasem).</summary>
    internal const long MaxSharedRead = 16L * 1024 * 1024;

    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public bool DirectoryExists(string path)
        => Guard(() => Directory.Exists(path), $"sprawdzić folder {path}");

    /// <summary>
    /// <see cref="File.GetAttributes"/> zamiast <see cref="Directory.Exists"/> (to drugie zwraca <c>false</c> przy każdym błędzie):
    /// brak = tylko <see cref="FileNotFoundException"/>/<see cref="DirectoryNotFoundException"/> (Win32 2 i 3), plik = <c>false</c>,
    /// reszta przez <c>Guard</c> jako <see cref="OperationException"/>.
    /// </summary>
    public bool ProbeDirectory(string path)
        => Guard(
            () =>
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

                return (attributes & FileAttributes.Directory) != 0;
            },
            $"sprawdzić folder {path}");

    public void MoveDirectory(string source, string destination)
        => Guard(
            () =>
            {
                if (!Directory.Exists(source))
                {
                    throw new DirectoryNotFoundException($"Folder źródłowy nie istnieje: {source}");
                }

                if (Directory.Exists(destination) || File.Exists(destination))
                {
                    throw new IOException($"Cel już istnieje: {destination}");
                }

                Directory.Move(source, destination);
                return true;
            },
            $"przenieść folder {source} do {destination}");

    public void DeleteDirectoryTree(string path)
        => Guard(
            () =>
            {
                DeleteTree(path);
                return true;
            },
            $"usunąć folder {path}");

    public IReadOnlyList<string> ListSiblingDirectories(string path, string prefix)
        => Guard<IReadOnlyList<string>>(
            () =>
            {
                var full = Path.GetFullPath(path).TrimEnd('\\');
                var parent = Path.GetDirectoryName(full);
                if (parent is null || !Directory.Exists(parent))
                {
                    return new List<string>();
                }

                var found = new List<string>();
                foreach (var directory in Directory.EnumerateDirectories(parent))
                {
                    if (Path.GetFileName(directory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(directory, full, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(directory);
                    }
                }

                found.Sort(StringComparer.OrdinalIgnoreCase);
                return found;
            },
            $"wylistować foldery obok {path}");

    public void WriteAllText(string path, string content)
        => Guard(
            () =>
            {
                if (content is null)
                {
                    throw new ArgumentNullException(nameof(content));
                }

                // Nowy plik od razu z zaufanym ACL (bez okna z odziedziczonymi uprawnieniami); istniejący (także link) jest usuwany.
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }

                var preamble = Utf8WithBom.GetPreamble();
                var body = Utf8WithBom.GetBytes(content);
                using var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileSystemRights.Write,
                    FileShare.None,
                    4096,
                    FileOptions.None,
                    SecureDirectory.CreateTrustedFileSecurity());
                stream.Write(preamble, 0, preamble.Length);
                stream.Write(body, 0, body.Length);
                return true;
            },
            $"zapisać plik {path}");

    public long? FileLength(string path)
        => Guard<long?>(
            () =>
            {
                var info = new FileInfo(path);
                return info.Exists ? info.Length : null;
            },
            $"odczytać rozmiar pliku {path}");

    /// <summary>Hak testowy: wołany po wyliczeniu każdego folderu w <see cref="DeleteOldFiles"/> (symulacja podmiany w trakcie).</summary>
    internal Action<string>? AfterDirectoryEnumerated { get; set; }

    /// <summary>Usuwanie przez uchwyt z kontrolą ostatecznej ścieżki – szczegóły w <see cref="OldFilesCleaner"/>.</summary>
    public OldFilesCleanup DeleteOldFiles(string root, DateTime olderThanUtc)
        => Guard(() => new OldFilesCleaner(olderThanUtc, AfterDirectoryEnumerated).Run(root), $"wyczyścić folder {root}");

    /// <summary>Jak <see cref="DeleteOldFiles"/> (przez uchwyt), ale tylko pliki z nazwą pasującą do prefiksu i rozszerzenia, bez podfolderów.</summary>
    public OldFilesCleanup DeleteFilesByName(string directory, string prefix, string extension)
        => Guard(() => new OldFilesCleaner(DateTime.MaxValue, AfterDirectoryEnumerated).RunNamed(directory, prefix, extension), $"wyczyścić folder {directory}");

    public string? ReadSharedFrom(string path, long offset)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset nie może być ujemny.");
        }

        return Guard(
            () =>
            {
                FileStream stream;
                try
                {
                    stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    return null;
                }

                using (stream)
                {
                    var length = stream.Length;
                    var start = offset > length ? 0 : offset;
                    var truncated = length - start > MaxSharedRead;
                    if (truncated)
                    {
                        start = length - MaxSharedRead;
                    }

                    stream.Position = start;
                    var bytes = new byte[length - start];
                    var count = 0;
                    int read;
                    while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) > 0)
                    {
                        count += read;
                    }

                    var index = 0;
                    if (truncated)
                    {
                        // Obcięty początek: od pierwszej pełnej linii (bez urwanego znaku i połowy wpisu).
                        var newline = Array.IndexOf(bytes, (byte)'\n', 0, count);
                        index = newline < 0 ? 0 : newline + 1;
                    }
                    else if (start == 0 && count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                    {
                        index = 3;
                    }

                    return (string?)Utf8.GetString(bytes, index, count - index);
                }
            },
            $"odczytać plik {path}");
    }

    private static void DeleteTree(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException($"To nie jest folder: {path}");
        }

        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            foreach (var entry in new DirectoryInfo(path).GetFileSystemInfos())
            {
                var entryAttributes = File.GetAttributes(entry.FullName);
                if ((entryAttributes & FileAttributes.Directory) != 0)
                {
                    DeleteTree(entry.FullName);
                }
                else
                {
                    ClearReadOnly(entry.FullName, entryAttributes);
                    File.Delete(entry.FullName);
                }
            }
        }

        // Dla junction/dowiązania RemoveDirectory usuwa sam link; cel i jego zawartość zostają.
        ClearReadOnly(path, attributes);
        Directory.Delete(path, recursive: false);
    }

    private static void ClearReadOnly(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private static T Guard<T>(Func<T> action, string what)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, $"Brak uprawnień, by {what}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new OperationException(OperationErrorKind.NotFound, $"Nie można {what}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            throw new OperationException(OperationErrorKind.Unexpected, $"Nie można {what}: {ex.Message}", ex);
        }
    }
}
