using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;
using DSOptimizer.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Windows;

/// <summary>
/// Czyszczenie starych plików dla <see cref="WindowsRepairFileSystem.DeleteOldFiles"/>, odporne na podmianę ścieżki w trakcie
/// (TOCTOU: zwykły użytkownik może w swoim <c>%TEMP%</c> przemianować folder i założyć w jego miejsce junction). Wyliczenie
/// folderu służy tylko do wyboru kandydatów i oceny wieku; każde usunięcie idzie przez uchwyt:
/// <list type="number">
/// <item><c>CreateFileW</c> z <c>FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS</c> (link w ostatnim członie otwierany
/// jako link), plik z <c>share 0</c> – otwarty przez kogokolwiek innego (także z <c>FILE_SHARE_DELETE</c>) = „w użyciu”;</item>
/// <item>typ z uchwytu (<c>FileAttributeTagInfo</c>): reparse point = link, usuwany jako link, bez wchodzenia;</item>
/// <item>ostateczna ścieżka z uchwytu (<c>GetFinalPathNameByHandleW</c>) musi leżeć pod ostateczną ścieżką korzenia ustaloną raz
/// na starcie – inaczej element jest pomijany (junction w członie pośrednim), nigdy usuwany;</item>
/// <item>usunięcie dyspozycją (<c>FileDispositionInfo</c>) na tym samym uchwycie.</item>
/// </list>
/// Korzeń: sam będący linkiem albo osiągany przez link w członie pośrednim (ostateczna ścieżka uchwytu ≠ żądana ścieżka, np.
/// junction w <c>&lt;profil&gt;\AppData</c>) = <see cref="OldFilesCleanup.Skipped"/> z powodem – nic nie jest czyszczone.
/// Podfolder przed wyliczeniem jest otwierany uchwytem z tą samą kontrolą (link albo ścieżka spoza korzenia = nie wchodzimy);
/// zostaje tylko wąskie okno między tą kontrolą a wyliczeniem (skutek: najwyżej liczniki, usunięcia i tak blokuje pkt 3).
/// Puste stare podfoldery tak samo (dyspozycja na niepustym folderze się nie uda i folder zostaje). Element znikający w trakcie
/// = pominięcie bez liczenia. Klasa na jedno wywołanie.
/// </summary>
internal sealed class OldFilesCleaner
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const uint FileAttributeNormal = 0x80;

    internal static string RootIsLinkReason => L.T("folder jest linkiem (junction albo dowiązanie symboliczne) – program nie wchodzi do jego celu");

    private readonly DateTime _olderThanUtc;
    private readonly Action<string>? _afterDirectoryEnumerated;
    private string _rootPrefix = string.Empty;
    private int _deleted;
    private int _skippedInUse;
    private int _skippedYoung;
    private long _freedBytes;

    /// <summary>Filtr nazw <see cref="RunNamed"/> (bez podfolderów); <c>null</c> = całe drzewo według wieku.</summary>
    private string? _namePrefix;
    private string? _nameExtension;

    /// <param name="afterDirectoryEnumerated">Hak testowy: wołany po wyliczeniu każdego folderu, przed jego czyszczeniem.</param>
    public OldFilesCleaner(DateTime olderThanUtc, Action<string>? afterDirectoryEnumerated)
    {
        _olderThanUtc = olderThanUtc;
        _afterDirectoryEnumerated = afterDirectoryEnumerated;
    }

    private enum Expected
    {
        File,
        Link,
    }

    /// <summary>
    /// Błędy samego korzenia lecą jako wyjątki .NET (zamienia je wywołujący); brak korzenia = <see cref="OldFilesCleanup.None"/>,
    /// korzeń-link albo korzeń za linkiem w członie pośrednim = <see cref="OldFilesCleanup.Skipped"/>.
    /// </summary>
    public OldFilesCleanup Run(string root)
    {
        using (var handle = Open(root, NativeMethods.FileReadAttributes, NativeMethods.FileShareAll, out var error))
        {
            if (handle is null)
            {
                if (IsGone(error))
                {
                    return OldFilesCleanup.None;
                }

                throw error == ErrorAccessDenied
                    ? new UnauthorizedAccessException(L.F("Brak dostępu do {0}.", root))
                    : new IOException(L.F("Nie można otworzyć {0}: {1}", root, new Win32Exception(error).Message));
            }

            if (!TryTagInfo(handle, out var info))
            {
                throw new IOException(L.F("Nie można odczytać atrybutów {0}.", root));
            }

            if (!Has(info, FileAttributes.Directory))
            {
                throw new IOException(L.F("To nie jest folder: {0}", root));
            }

            // Sam korzeń będący linkiem: nie wchodzimy (cel może być np. folderem Dokumenty konta).
            if (Has(info, FileAttributes.ReparsePoint))
            {
                return OldFilesCleanup.Skipped(RootIsLinkReason);
            }

            var final = FinalPath(handle);
            if (final is null || final.Length < 2)
            {
                throw new IOException(L.F("Nie można ustalić ostatecznej ścieżki {0}.", root));
            }

            // Link w członie pośrednim (np. junction w <profil>\AppData) przekierowuje cały korzeń do innego folderu.
            var finalPlain = WithoutDevicePrefix(final).TrimEnd('\\');
            if (!string.Equals(finalPlain, RequestedPath(root), StringComparison.OrdinalIgnoreCase))
            {
                return OldFilesCleanup.Skipped(
                    L.F("ścieżka prowadzi przez link (junction albo dowiązanie symboliczne) do innego folderu ({0}) – program go nie czyści", finalPlain));
            }

            _rootPrefix = final.TrimEnd('\\') + "\\";
        }

        FileSystemInfo[] entries;
        try
        {
            entries = new DirectoryInfo(root).GetFileSystemInfos();
        }
        catch (DirectoryNotFoundException)
        {
            // Folder zniknął między sprawdzeniem a wyliczeniem (np. Windows Update sprząta sam).
            return OldFilesCleanup.None;
        }

        _afterDirectoryEnumerated?.Invoke(root);
        CleanEntries(entries);
        return new OldFilesCleanup(_deleted, _skippedInUse, _skippedYoung, _freedBytes);
    }

    /// <summary>
    /// Tylko elementy leżące bezpośrednio w <paramref name="root"/>, z nazwą od <paramref name="prefix"/> i z końcówką
    /// <paramref name="extension"/> (bez rozróżniania wielkości liter); podfoldery pomijane. Próg wieku podaje wywołujący
    /// (dla miniatur <see cref="DateTime.MaxValue"/>). Usuwanie tak samo jak w <see cref="Run"/> – przez uchwyt.
    /// </summary>
    public OldFilesCleanup RunNamed(string root, string prefix, string extension)
    {
        _namePrefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        _nameExtension = extension ?? throw new ArgumentNullException(nameof(extension));
        return Run(root);
    }

    private bool NameMatches(string name)
        => _namePrefix is null
           || (name.Length >= _namePrefix.Length + _nameExtension!.Length
               && name.StartsWith(_namePrefix, StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(_nameExtension, StringComparison.OrdinalIgnoreCase));

    private static bool Has(NativeMethods.FileAttributeTagInfo info, FileAttributes flag) => (info.FileAttributes & (uint)flag) != 0;

    private static bool IsGone(int error) => error is ErrorFileNotFound or ErrorPathNotFound;

    /// <summary>Otwiera element bez podążania za linkiem w ostatnim członie; <c>null</c> + kod błędu Win32, gdy się nie udało.</summary>
    private static SafeFileHandle? Open(string path, uint access, uint share, out int error)
    {
        var handle = NativeMethods.CreateFileHandle(
            path,
            access,
            share,
            IntPtr.Zero,
            NativeMethods.OpenExistingDisposition,
            NativeMethods.FileFlagOpenReparsePoint | NativeMethods.FileFlagBackupSemantics,
            IntPtr.Zero);
        error = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        if (!handle.IsInvalid)
        {
            return handle;
        }

        handle.Dispose();
        return null;
    }

    private static bool TryTagInfo(SafeFileHandle handle, out NativeMethods.FileAttributeTagInfo info)
        => NativeMethods.GetFileInformationByHandleEx(
            handle,
            NativeMethods.FileAttributeTagInfoClass,
            out info,
            (uint)Marshal.SizeOf<NativeMethods.FileAttributeTagInfo>());

    private static string? FinalPath(SafeFileHandle handle)
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
    private static string WithoutDevicePrefix(string path)
    {
        const string Device = @"\\?\";
        const string DeviceUnc = @"\\?\UNC\";
        if (path.StartsWith(DeviceUnc, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path.Substring(DeviceUnc.Length);
        }

        return path.StartsWith(Device, StringComparison.Ordinal) ? path.Substring(Device.Length) : path;
    }

    /// <summary>Żądana ścieżka korzenia: pełna, bez końcowego separatora, z członami 8.3 rozwiniętymi do długich nazw (bez rozwiązywania linków).</summary>
    private static string RequestedPath(string root)
    {
        var full = Path.GetFullPath(root);
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

    private static bool MarkForDeletion(SafeFileHandle handle)
    {
        var disposition = new NativeMethods.FileDispositionInfo { DeleteFile = 1 };
        return NativeMethods.SetFileInformationByHandle(
            handle,
            NativeMethods.FileDispositionInfoClass,
            ref disposition,
            (uint)Marshal.SizeOf<NativeMethods.FileDispositionInfo>());
    }

    private static bool SetAttributes(SafeFileHandle handle, uint attributes)
    {
        var basic = new NativeMethods.FileBasicInfo { FileAttributes = attributes == 0 ? FileAttributeNormal : attributes };
        return NativeMethods.SetFileInformationByHandle(
            handle,
            NativeMethods.FileBasicInfoClass,
            ref basic,
            (uint)Marshal.SizeOf<NativeMethods.FileBasicInfo>());
    }

    /// <summary>Czyści jeden poziom; typ i wiek z danych wyliczenia (dane samego elementu, także linku) – tylko do wyboru kandydatów.</summary>
    private void CleanEntries(FileSystemInfo[] entries)
    {
        foreach (var entry in entries)
        {
            if (!NameMatches(entry.Name))
            {
                continue;
            }

            var attributes = entry.Attributes;
            var old = entry.LastWriteTimeUtc < _olderThanUtc;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (old)
                {
                    DeleteEntry(entry.FullName, Expected.Link, attributes, 0);
                }
                else
                {
                    _skippedYoung++;
                }
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                // RunNamed: bez podfolderów.
                if (_namePrefix is null)
                {
                    CleanSubdirectory(entry.FullName, old);
                }
            }
            else if (old)
            {
                DeleteEntry(entry.FullName, Expected.File, attributes, ((FileInfo)entry).Length);
            }
            else
            {
                _skippedYoung++;
            }
        }
    }

    /// <summary>Podfolder: najpierw zawartość, potem sam folder, gdy jest pusty i był stary przed czyszczeniem (nie liczony).</summary>
    private void CleanSubdirectory(string path, bool wasOld)
    {
        // Przed wyliczeniem po ścieżce tekstowej: uchwyt bez podążania za linkiem. Folder podmieniony na junction albo
        // dowiązanie (także w członie pośrednim – ścieżka końcowa spoza korzenia) nie jest wyliczany, więc drzewo celu nie
        // jest przechodzone i nie zawyża liczników. Podmieniony stary folder znika jako sam link (bez liczenia).
        var swappedForLink = false;
        using (var handle = Open(path, NativeMethods.FileReadAttributes, NativeMethods.FileShareAll, out _))
        {
            if (handle is null || !TryTagInfo(handle, out var info) || !IsInsideRoot(handle))
            {
                return;
            }

            if (Has(info, FileAttributes.ReparsePoint))
            {
                swappedForLink = true;
            }
            else if (!Has(info, FileAttributes.Directory))
            {
                return;
            }
        }

        if (swappedForLink)
        {
            if (wasOld)
            {
                RemoveEmptyDirectory(path);
            }

            return;
        }

        FileSystemInfo[] entries;
        try
        {
            entries = new DirectoryInfo(path).GetFileSystemInfos();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Folder zniknął w trakcie albo nie da się go wylistować: pomijamy bez liczenia.
            return;
        }

        _afterDirectoryEnumerated?.Invoke(path);
        CleanEntries(entries);
        if (wasOld)
        {
            RemoveEmptyDirectory(path);
        }
    }

    private void RemoveEmptyDirectory(string path)
    {
        using var handle = Open(path, NativeMethods.Delete | NativeMethods.FileReadAttributes, NativeMethods.FileShareAll, out _);
        if (handle is null || !TryTagInfo(handle, out var info) || !IsInsideRoot(handle))
        {
            return;
        }

        // Folder podmieniony na link: usuwany sam link (cel zostaje); zwykły folder – dyspozycja nie uda się, gdy nie jest pusty.
        if (Has(info, FileAttributes.Directory) || Has(info, FileAttributes.ReparsePoint))
        {
            MarkForDeletion(handle);
        }
    }

    /// <summary>
    /// Stary plik albo link. „W użyciu” (share 0 się nie otwiera, brak dostępu, nieudana dyspozycja) = <c>SkippedInUse</c>;
    /// zniknięcie, ścieżka spoza korzenia albo podmiana na zwykły folder = pominięcie bez liczenia.
    /// </summary>
    private void DeleteEntry(string path, Expected expected, FileAttributes enumerated, long length)
    {
        var readOnly = (enumerated & FileAttributes.ReadOnly) != 0;
        var access = NativeMethods.Delete | NativeMethods.FileReadAttributes | (readOnly ? NativeMethods.FileWriteAttributes : 0);
        var share = expected == Expected.File ? NativeMethods.FileShareNone : NativeMethods.FileShareAll;
        using var handle = Open(path, access, share, out var error);
        if (handle is null)
        {
            if (!IsGone(error))
            {
                _skippedInUse++;
            }

            return;
        }

        if (!TryTagInfo(handle, out var info))
        {
            _skippedInUse++;
            return;
        }

        // Junction albo dowiązanie w członie pośrednim (podmiana w trakcie): cel spoza drzewa nigdy nie jest usuwany.
        if (!IsInsideRoot(handle))
        {
            return;
        }

        var isLink = Has(info, FileAttributes.ReparsePoint);
        if (!isLink && (expected == Expected.Link || Has(info, FileAttributes.Directory)))
        {
            // Typ zmienił się od wyliczenia na zwykły folder albo plik w miejscu linku: nie zgadujemy, pomijamy.
            return;
        }

        var clearReadOnly = Has(info, FileAttributes.ReadOnly);
        if (clearReadOnly && !SetAttributes(handle, info.FileAttributes & ~(uint)FileAttributes.ReadOnly))
        {
            _skippedInUse++;
            return;
        }

        if (!MarkForDeletion(handle))
        {
            if (clearReadOnly)
            {
                SetAttributes(handle, info.FileAttributes);
            }

            _skippedInUse++;
            return;
        }

        _deleted++;
        _freedBytes += isLink ? 0 : length;
    }

    private bool IsInsideRoot(SafeFileHandle handle)
    {
        var final = FinalPath(handle);
        return final is not null
            && final.Length > _rootPrefix.Length
            && final.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
