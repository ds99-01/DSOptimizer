using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;

namespace DSOptimizer.Windows;

/// <summary>Wynik zapisu jednej kopii raportu.</summary>
public sealed record ReportSaveResult(string Path, bool Saved, string? Error);

/// <summary>
/// Zapis raportu tekstowego: zawsze na komputerze (<see cref="SecureRoot.ReportsDirectory"/>), a gdy podano
/// <c>mirrorDirectory</c> (exe poza dyskiem systemowym) także obok exe. UTF-8 z BOM, końce linii CRLF.
/// Awaria jednej kopii (np. wyjęty pendrive) nie przerywa zapisu drugiej ani nie rzuca wyjątku.
/// </summary>
public sealed class ReportFileWriter
{
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly SecureRoot _root;
    private readonly string? _mirrorDirectory;

    public ReportFileWriter(SecureRoot root, string? mirrorDirectory)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _mirrorDirectory = mirrorDirectory;
    }

    /// <summary>Wyniki: najpierw komputer, potem kopia obok exe (gdy jest). Ta sama nazwa nadpisuje plik.</summary>
    public IReadOnlyList<ReportSaveResult> Save(string fileName, string text)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new ArgumentException("Nazwa pliku raportu nie może zawierać ścieżki.", nameof(fileName));
        }

        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var content = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var results = new List<ReportSaveResult>
        {
            WriteCopy(Path.Combine(_root.ReportsDirectory, fileName), content, () => _root.EnsureLeaf(_root.ReportsDirectory)),
        };

        if (_mirrorDirectory is not null)
        {
            var mirrorFile = Path.Combine(_mirrorDirectory, fileName);
            results.Add(WriteCopy(mirrorFile, content, () =>
            {
                MirrorPathGuard.EnsureDirectory(_mirrorDirectory);
                MirrorPathGuard.EnsureFile(mirrorFile);
            }));
        }

        return results;
    }

    private static ReportSaveResult WriteCopy(string path, string content, Action prepare)
    {
        try
        {
            prepare();
            File.WriteAllText(path, content, Utf8Bom);
            return new ReportSaveResult(path, true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException)
        {
            return new ReportSaveResult(path, false, ex.Message);
        }
    }
}
