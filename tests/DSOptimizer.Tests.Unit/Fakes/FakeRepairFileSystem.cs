using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>System plików napraw w pamięci: same foldery (płasko, bez zawartości) i pliki tekstowe; błędy wstrzykiwane po ścieżce.</summary>
public sealed class FakeRepairFileSystem : IRepairFileSystem
{
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _failingMoves = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _failingDeletes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failingWrites = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zmieniające wywołania w kolejności: Move/Delete/Write/DeleteOld.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>Dostaje każde zmieniające wywołanie (wspólny dziennik kolejności z innymi fake'ami).</summary>
    public Action<string>? Observer { get; set; }

    /// <summary>Wywoływany po udanym przeniesieniu (np. symulacja folderu, który zniknął).</summary>
    public Action<string, string>? AfterMove { get; set; }

    public bool ListFails { get; set; }

    public void AddDirectory(string path) => _directories.Add(path);

    public void RemoveDirectory(string path) => _directories.Remove(path);

    public void AddFile(string path, string content) => _files[path] = content;

    public string? FileText(string path) => _files.TryGetValue(path, out var text) ? text : null;

    public void RemoveFile(string path) => _files.Remove(path);

    public IReadOnlyCollection<string> Directories => _directories;

    public IReadOnlyCollection<string> Files => _files.Keys;

    /// <summary>Przeniesienie z <paramref name="source"/> rzuca (domyślnie OperationException „plik zablokowany”).</summary>
    public void FailMove(string source, Exception? exception = null)
        => _failingMoves[source] = exception ?? new OperationException(OperationErrorKind.Unexpected, $"Plik w {source} jest używany (fake).");

    public void FailDelete(string path) => _failingDeletes[path] = new OperationException(OperationErrorKind.AccessDenied, $"Usunięcie {path} odrzucone (fake).");

    public void FailWrite(string path) => _failingWrites.Add(path);

    /// <summary>Ścieżki pytane przez <see cref="DirectoryExists"/> (tylko odczyt, poza <see cref="Calls"/>).</summary>
    public List<string> ExistenceQueries { get; } = new();

    public bool DirectoryExists(string path)
    {
        ExistenceQueries.Add(path);
        return _directories.Contains(path);
    }

    /// <summary>Błąd <see cref="ProbeDirectory"/> dla ścieżki (null = brak błędu); można przełączać w trakcie przebiegu.</summary>
    public Func<string, Exception?>? ProbeFailure { get; set; }

    /// <summary>Pytania <see cref="ProbeDirectory"/> też trafiają do <see cref="ExistenceQueries"/>.</summary>
    public bool ProbeDirectory(string path)
    {
        ExistenceQueries.Add(path);
        if (ProbeFailure?.Invoke(path) is { } failure)
        {
            throw failure;
        }

        return _directories.Contains(path);
    }

    public void MoveDirectory(string source, string destination)
    {
        Record($"Move:{source}->{destination}");
        if (_failingMoves.TryGetValue(source, out var failure))
        {
            throw failure;
        }

        if (!_directories.Contains(source))
        {
            throw new OperationException(OperationErrorKind.NotFound, $"Brak folderu {source} (fake).");
        }

        if (_directories.Contains(destination) || _files.ContainsKey(destination))
        {
            throw new OperationException(OperationErrorKind.Unexpected, $"Cel już istnieje: {destination} (fake).");
        }

        _directories.Remove(source);
        _directories.Add(destination);
        AfterMove?.Invoke(source, destination);
    }

    public void DeleteDirectoryTree(string path)
    {
        Record($"Delete:{path}");
        if (_failingDeletes.TryGetValue(path, out var failure))
        {
            throw failure;
        }

        _directories.Remove(path);
    }

    public IReadOnlyList<string> ListSiblingDirectories(string path, string prefix)
    {
        if (ListFails)
        {
            throw new OperationException(OperationErrorKind.AccessDenied, "Lista folderów odrzucona (fake).");
        }

        var parent = Path.GetDirectoryName(path.TrimEnd('\\'));
        return _directories
            .Where(d => string.Equals(Path.GetDirectoryName(d), parent, StringComparison.OrdinalIgnoreCase))
            .Where(d => !string.Equals(d, path, StringComparison.OrdinalIgnoreCase))
            .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void WriteAllText(string path, string content)
    {
        Record($"Write:{path}");
        if (_failingWrites.Contains(path))
        {
            throw new OperationException(OperationErrorKind.AccessDenied, $"Zapis {path} odrzucony (fake).");
        }

        _files[path] = content;
    }

    public long? FileLength(string path) => _files.TryGetValue(path, out var text) ? text.Length : null;

    /// <summary>Wynik <see cref="DeleteOldFiles"/> po folderze; brak wpisu = <see cref="OldFilesCleanup.None"/>.</summary>
    public Dictionary<string, Func<DateTime, OldFilesCleanup>> OldFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Wynik <see cref="DeleteFilesByName"/> po folderze; brak wpisu = <see cref="OldFilesCleanup.None"/>.</summary>
    public Dictionary<string, Func<OldFilesCleanup>> NamedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Wyjątek rzucany przez <see cref="DeleteOldFiles"/>/<see cref="DeleteFilesByName"/> dla folderu (np. odmowa dostępu).</summary>
    public Dictionary<string, Exception> FailingCleanups { get; } = new(StringComparer.OrdinalIgnoreCase);

    public OldFilesCleanup DeleteFilesByName(string directory, string prefix, string extension)
    {
        Record($"DeleteNamed:{directory}|{prefix}*{extension}");
        if (FailingCleanups.TryGetValue(directory, out var failure))
        {
            throw failure;
        }

        return NamedFiles.TryGetValue(directory, out var result) ? result() : OldFilesCleanup.None;
    }

    /// <summary>Wywołania <see cref="ReadSharedFrom"/>: ścieżka i offset.</summary>
    public List<(string Path, long Offset)> SharedReads { get; } = new();

    public OldFilesCleanup DeleteOldFiles(string root, DateTime olderThanUtc)
    {
        Record($"DeleteOld:{root}");
        if (FailingCleanups.TryGetValue(root, out var failure))
        {
            throw failure;
        }

        return OldFiles.TryGetValue(root, out var result) ? result(olderThanUtc) : OldFilesCleanup.None;
    }

    /// <summary>Tekst pliku od offsetu (znaki = bajty w fake'u); offset za końcem = cały tekst, brak pliku = null.</summary>
    public string? ReadSharedFrom(string path, long offset)
    {
        SharedReads.Add((path, offset));
        if (!_files.TryGetValue(path, out var text))
        {
            return null;
        }

        return offset > text.Length ? text : text.Substring((int)offset);
    }

    private void Record(string call)
    {
        Calls.Add(call);
        Observer?.Invoke(call);
    }
}
