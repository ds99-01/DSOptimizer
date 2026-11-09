using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Storage;

namespace DSOptimizer.Windows;

/// <summary>Wynik zapisu jednej kopii raportu.</summary>
public sealed record ReportSaveResult(string Path, bool Saved, string? Error);

/// <summary>
/// Zapis raportu tekstowego w miejscu wybranym w oknie zapisu, a gdy podano <c>mirrorDirectory</c> (exe poza dyskiem systemowym)
/// także kopii o tej samej nazwie obok exe. UTF-8 z BOM, końce linii CRLF. Odmowa (nic nie powstaje, także kopia obok exe):
/// miejsce w folderze programu albo inna ścieżka, której <see cref="LogPathPolicy.CanReveal"/> nie pozwala pokazać, oraz ścieżka
/// prowadząca przez link (<see cref="ReparseLinks"/>). Awaria jednej kopii (np. wyjęty pendrive) nie przerywa zapisu drugiej
/// ani nie rzuca wyjątku. <see cref="SaveCopy"/> (dziennik, „Zapisz kopię…”) kopiuje istniejący plik z tymi samymi odmowami.
/// </summary>
public sealed class ReportFileWriter
{
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly SecureRoot _root;
    private readonly string? _mirrorDirectory;

    /// <param name="root">Folder programu; służy tylko do odmowy zapisu w nim.</param>
    /// <param name="mirrorDirectory">Folder kopii obok exe albo <c>null</c>.</param>
    public ReportFileWriter(SecureRoot root, string? mirrorDirectory)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _mirrorDirectory = mirrorDirectory;
    }

    /// <summary>Powód odmowy zapisu w folderze programu (albo innym miejscu chronionym).</summary>
    internal static string ProtectedLocationReason
        => L.T("w tym miejscu program nie zapisuje plików (folder programu jest dostępny tylko z uprawnieniami administratora) – wybierz inne miejsce, np. Pulpit");

    /// <summary>Powód odmowy zapisu przez link <paramref name="link"/>.</summary>
    internal static string LinkReason(string link)
        => L.F("ścieżka prowadzi przez link (junction albo dowiązanie symboliczne): {0} – wybierz inne miejsce", link);

    /// <summary>
    /// Wyniki: najpierw plik z okna zapisu, potem kopia obok exe (gdy jest). Istniejący plik jest nadpisywany w całości.
    /// Odmowa = jeden wynik z powodem.
    /// </summary>
    public IReadOnlyList<ReportSaveResult> Save(string targetPath, string text)
    {
        if (targetPath is null)
        {
            throw new ArgumentNullException(nameof(targetPath));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Ścieżka raportu nie może być pusta.", nameof(targetPath));
        }

        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var content = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        string? refusal;
        try
        {
            refusal = Refusal(targetPath);
        }
        catch (Exception ex) when (IsWriteFailure(ex))
        {
            return new[] { new ReportSaveResult(targetPath, false, ex.Message) };
        }

        if (refusal is not null)
        {
            return new[] { new ReportSaveResult(targetPath, false, refusal) };
        }

        var results = new List<ReportSaveResult> { WriteCopy(targetPath, content, () => { }) };
        if (_mirrorDirectory is not null)
        {
            var mirrorFile = Path.Combine(_mirrorDirectory, Path.GetFileName(targetPath));
            results.Add(WriteCopy(mirrorFile, content, () =>
            {
                MirrorPathGuard.EnsureDirectory(_mirrorDirectory);
                MirrorPathGuard.EnsureFile(mirrorFile);
            }));
        }

        return results;
    }

    /// <summary>
    /// „Zapisz kopię…” w dzienniku: kopia pliku <paramref name="sourcePath"/> (np. logu winget z folderu programu, który winget może
    /// jeszcze trzymać otwartym) w <paramref name="targetPath"/>, bajt w bajt. Te same odmowy co <see cref="Save"/>; bez kopii obok
    /// exe. Istniejący plik jest nadpisywany w całości. Nie rzuca wyjątku przy błędzie zapisu ani odczytu.
    /// </summary>
    public ReportSaveResult SaveCopy(string sourcePath, string targetPath)
    {
        if (sourcePath is null)
        {
            throw new ArgumentNullException(nameof(sourcePath));
        }

        if (targetPath is null)
        {
            throw new ArgumentNullException(nameof(targetPath));
        }

        try
        {
            if (Refusal(targetPath) is { } refusal)
            {
                return new ReportSaveResult(targetPath, false, refusal);
            }

            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
            }

            return new ReportSaveResult(targetPath, true, null);
        }
        catch (Exception ex) when (IsWriteFailure(ex) || ex is ArgumentException)
        {
            return new ReportSaveResult(targetPath, false, ex.Message);
        }
    }

    /// <summary>
    /// <c>null</c> = wolno zapisać w <paramref name="targetPath"/>, inaczej powód odmowy. Miejsce chronione sprawdzane
    /// leksykalnie, bez dotykania dysku; potem linki na ścieżce (sam element i każdy przodek).
    /// </summary>
    internal string? Refusal(string targetPath)
    {
        if (!LogPathPolicy.CanReveal(targetPath, _root.FullPath))
        {
            return ProtectedLocationReason;
        }

        var link = ReparseLinks.FirstLink(targetPath);
        return link is null ? null : LinkReason(link);
    }

    private static ReportSaveResult WriteCopy(string path, string content, Action prepare)
    {
        try
        {
            prepare();
            File.WriteAllText(path, content, Utf8Bom);
            return new ReportSaveResult(path, true, null);
        }
        catch (Exception ex) when (IsWriteFailure(ex))
        {
            return new ReportSaveResult(path, false, ex.Message);
        }
    }

    private static bool IsWriteFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException;
}
