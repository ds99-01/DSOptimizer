using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Storage;
using DSOptimizer.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Windows;

/// <summary>
/// Wynik <see cref="ProtectedFileReader.TryRead"/>: treść i czas zapisu z uchwytu albo powód odrzucenia
/// (<see cref="Bytes"/> = <c>null</c>, <see cref="Detail"/> wg <see cref="SalvageRejection"/>). <see cref="Transient"/> = plik
/// chwilowo zajęty przez inny proces (naruszenie współdzielenia albo blokady), kolejna próba może się udać.
/// </summary>
internal sealed record ProtectedFileRead(byte[]? Bytes, DateTime LastWriteUtc, SalvageRejection? Rejection, string? Detail, bool Transient = false);

/// <summary>
/// Odczyt pliku, który może leżeć w folderze z prawem zapisu dla obcego konta. Wszystkie kontrole i odczyt idą przez jeden
/// uchwyt otwarty bez podążania za linkiem w ostatnim członie i bez współdzielenia zapisu, więc nikt nie podmieni ani nie
/// zmieni pliku między kontrolą a odczytem. Przechodzi tylko zwykły plik pod żądaną ścieżką (bez linku także w członach
/// pośrednich: ostateczna ścieżka uchwytu = żądana), z jednym dowiązaniem, z właścicielem i DACL dowodzącymi zapisu przez
/// administratora (<see cref="AclTrust.IsProtectedAdminOnlyFile"/>) i w limicie rozmiaru.
/// </summary>
internal static class ProtectedFileReader
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const uint NotRegularAttributes = (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory);

    /// <summary><c>null</c> = elementu nie ma (brak pliku albo folderu po drodze).</summary>
    public static ProtectedFileRead? TryRead(string path, long maxBytes)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        using var handle = NativeMethods.CreateFileHandle(
            path,
            NativeMethods.GenericRead,
            NativeMethods.FileShareRead,
            IntPtr.Zero,
            NativeMethods.OpenExistingDisposition,
            NativeMethods.FileFlagOpenReparsePoint | NativeMethods.FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            return error is ErrorFileNotFound or ErrorPathNotFound ? null : OpenFailed(error);
        }

        if (NativeMethods.GetFileType(handle) != NativeMethods.FileTypeDisk)
        {
            return Rejected(SalvageRejection.NotRegularFile);
        }

        if (!NativeMethods.GetFileInformationByHandleEx(
                handle, NativeMethods.FileAttributeTagInfoClass, out NativeMethods.FileAttributeTagInfo tag, (uint)Marshal.SizeOf<NativeMethods.FileAttributeTagInfo>()))
        {
            return OpenFailed(Marshal.GetLastWin32Error());
        }

        if ((tag.FileAttributes & NotRegularAttributes) != 0)
        {
            return Rejected(SalvageRejection.NotRegularFile);
        }

        // Link w członie pośrednim (folder odłożony albo journal jako junction, np. do pendrive'a z plikiem o właścicielu BA
        // albo do kopii w tle) prowadzi uchwyt gdzie indziej: ostateczna ścieżka ≠ żądana. Brak ostatecznej ścieżki = odrzucenie.
        if (!HandlePaths.IsAtRequestedPath(handle, path))
        {
            return Rejected(SalvageRejection.NotRegularFile);
        }

        if (!NativeMethods.GetFileInformationByHandleEx(
                handle, NativeMethods.FileStandardInfoClass, out NativeMethods.FileStandardInfo standard, (uint)Marshal.SizeOf<NativeMethods.FileStandardInfo>()))
        {
            return OpenFailed(Marshal.GetLastWin32Error());
        }

        if (standard.Directory != 0)
        {
            return Rejected(SalvageRejection.NotRegularFile);
        }

        if (standard.NumberOfLinks != 1)
        {
            return Rejected(SalvageRejection.MultipleLinks);
        }

        // Właściciel i DACL przed rozmiarem: TooLarge (powód na treści, z oknem utraty) tylko dla pliku zapisanego przez administratora.
        if (!IsProtectedAdminOnly(handle))
        {
            return Rejected(SalvageRejection.UntrustedAcl);
        }

        if (standard.EndOfFile > maxBytes)
        {
            return Rejected(SalvageRejection.TooLarge, standard.EndOfFile.ToString(CultureInfo.InvariantCulture));
        }

        if (!NativeMethods.GetFileInformationByHandleEx(
                handle, NativeMethods.FileBasicInfoClass, out NativeMethods.FileBasicInfo basic, (uint)Marshal.SizeOf<NativeMethods.FileBasicInfo>()))
        {
            return OpenFailed(Marshal.GetLastWin32Error());
        }

        var lastWriteUtc = DateTime.FromFileTimeUtc(basic.LastWriteTime);
        var bytes = new byte[standard.EndOfFile];
        try
        {
            // FileStream przejmuje uchwyt i zamyka go przy swoim Dispose. Zewnętrzny using jest tylko zabezpieczeniem na ścieżki
            // wyjścia przed utworzeniem strumienia (albo gdy konstruktor rzuci); ponowne Dispose zamkniętego SafeHandle nic nie robi.
            using var stream = new FileStream(handle, FileAccess.Read);
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            if (offset != bytes.Length || stream.Read(new byte[1], 0, 1) != 0)
            {
                return Rejected(SalvageRejection.OpenFailed, SizeChanged);
            }
        }
        catch (IOException ex)
        {
            return new ProtectedFileRead(null, default, SalvageRejection.OpenFailed, ex.Message, IsBusy(ex.HResult & 0xFFFF));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Rejected(SalvageRejection.OpenFailed, ex.Message);
        }

        return new ProtectedFileRead(bytes, lastWriteUtc, null, null);
    }

    private static string SizeChanged => L.T("Rozmiar pliku zmienił się w trakcie odczytu.");

    /// <summary>Właściciel i DACL z deskryptora tego samego uchwytu; każdy błąd odczytu = niezaufany.</summary>
    private static bool IsProtectedAdminOnly(SafeFileHandle handle)
    {
        var error = NativeMethods.GetSecurityInfo(
            handle,
            NativeMethods.SeFileObject,
            NativeMethods.OwnerSecurityInformation | NativeMethods.DaclSecurityInformation,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            out var descriptor);
        try
        {
            if (error != 0 || descriptor == IntPtr.Zero)
            {
                return false;
            }

            var length = (int)NativeMethods.GetSecurityDescriptorLength(descriptor);
            var binary = new byte[length];
            Marshal.Copy(descriptor, binary, 0, length);
            return AclTrust.IsProtectedAdminOnlyFile(new RawSecurityDescriptor(binary, 0));
        }
        catch (ArgumentException)
        {
            return false;
        }
        finally
        {
            if (descriptor != IntPtr.Zero)
            {
                _ = NativeMethods.LocalFree(descriptor);
            }
        }
    }

    private static ProtectedFileRead OpenFailed(int error)
        => new(null, default, SalvageRejection.OpenFailed, new Win32Exception(error).Message, IsBusy(error));

    private static bool IsBusy(int error) => error is ErrorSharingViolation or ErrorLockViolation;

    private static ProtectedFileRead Rejected(SalvageRejection rejection, string? detail = null) => new(null, default, rejection, detail);
}
