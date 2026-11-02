using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Windows.Autostart;

/// <summary>
/// Pliki pomiaru startu, które Windows zapisuje po zalogowaniu (<c>%SystemRoot%\System32\wdi\LogFiles\StartupInfo</c>) – tylko odczyt.
/// Pliki konta: <c>&lt;SID&gt;_StartupInfo&lt;N&gt;.xml</c> (porównanie bez rozróżniania kultury, SID dosłownie), czas = <c>LastWriteTimeUtc</c>.
/// Treść czytana z <c>FileShare.ReadWrite | FileShare.Delete</c> (Windows może pisać plik w tej chwili), UTF-16 albo kodowanie z BOM,
/// najwyżej <see cref="DefaultMaxBytes"/> – plik leży w miejscu, na które wpływa konto użytkownika.
/// </summary>
public sealed class StartupInfoFolder : IStartupInfoSource
{
    public const long DefaultMaxBytes = 16L * 1024 * 1024;

    private const int BufferChars = 4096;

    private readonly long _maxBytes;

    public StartupInfoFolder(string directory)
        : this(directory, DefaultMaxBytes)
    {
    }

    /// <summary>Szew testów: niższy limit rozmiaru pliku.</summary>
    internal StartupInfoFolder(string directory, long maxBytes)
    {
        FolderPath = directory ?? throw new ArgumentNullException(nameof(directory));
        _maxBytes = maxBytes;
    }

    public string FolderPath { get; }

    public static StartupInfoFolder ForSystem()
        => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\wdi\LogFiles\StartupInfo"));

    public IReadOnlyList<StartupInfoFile> Files(string userSid)
    {
        if (string.IsNullOrEmpty(userSid))
        {
            throw new ArgumentException("SID", nameof(userSid));
        }

        var pattern = new Regex("^" + Regex.Escape(userSid) + @"_StartupInfo([0-9]{1,9})\.xml$", RegexOptions.CultureInvariant);
        var files = new List<StartupInfoFile>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(FolderPath))
            {
                var match = pattern.Match(Path.GetFileName(path));
                if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Exists)
                {
                    files.Add(new StartupInfoFile(path, number, new DateTimeOffset(info.LastWriteTimeUtc)));
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<StartupInfoFile>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień, by wylistować folder {0}: {1}", FolderPath, ex.Message), ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można wylistować folderu {0}: {1}", FolderPath, ex.Message), ex);
        }

        return files;
    }

    public string Read(StartupInfoFile file)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        try
        {
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > _maxBytes)
            {
                throw TooLarge(file.Path);
            }

            using var reader = new StreamReader(stream, Encoding.Unicode, detectEncodingFromByteOrderMarks: true);
            var text = new StringBuilder();
            var buffer = new char[BufferChars];
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                text.Append(buffer, 0, count);
                if (text.Length > _maxBytes)
                {
                    throw TooLarge(file.Path);
                }
            }

            return text.ToString();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, L.F("Brak uprawnień, by odczytać plik {0}: {1}", file.Path, ex.Message), ex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new OperationException(OperationErrorKind.NotFound, L.F("Nie można odczytać plik {0}: {1}", file.Path, ex.Message), ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or DecoderFallbackException)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można odczytać plik {0}: {1}", file.Path, ex.Message), ex);
        }
    }

    private OperationException TooLarge(string path)
        => new(OperationErrorKind.Unexpected, L.F("Plik pomiaru Windows {0} jest większy niż {1} B – pominięto.", path, _maxBytes));
}
