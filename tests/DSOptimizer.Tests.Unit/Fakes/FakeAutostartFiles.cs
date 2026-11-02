using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Errors;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Pliki autostartu w pamięci: foldery z nazwami plików, skróty, wersje plików i zbiór istniejących plików (ścieżki bez wielkości liter).</summary>
public sealed class FakeAutostartFiles : IAutostartFiles
{
    private readonly Dictionary<string, List<string>> _folders = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ShortcutInfo?> Shortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ThrowingShortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, FileDetails> Details { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ścieżki, dla których <see cref="Probe"/> zwraca <see cref="FileProbe.Unknown"/> (np. brak dostępu).</summary>
    public HashSet<string> Unknown { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> DeniedFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ListedFolders { get; } = new();

    public List<string> Probed { get; } = new();

    public List<string> DetailsRequested { get; } = new();

    /// <summary>Dodaje plik do folderu (bez tworzenia wpisu w <see cref="Existing"/>).</summary>
    public void AddToFolder(string folder, string name)
    {
        if (!_folders.TryGetValue(folder, out var names))
        {
            names = new List<string>();
            _folders[folder] = names;
        }

        names.Add(name);
    }

    public IReadOnlyList<string> ListFolder(string folder)
    {
        ListedFolders.Add(folder);
        if (DeniedFolders.Contains(folder))
        {
            throw new OperationException(OperationErrorKind.AccessDenied, "Folder denied (fake): " + folder);
        }

        return _folders.TryGetValue(folder, out var names) ? names.ToArray() : Array.Empty<string>();
    }

    public ShortcutInfo? ReadShortcut(string path)
    {
        if (ThrowingShortcuts.Contains(path))
        {
            throw new OperationException(OperationErrorKind.Unexpected, "Shortcut unreadable (fake): " + path);
        }

        return Shortcuts.TryGetValue(path, out var shortcut) ? shortcut : null;
    }

    public FileProbe Probe(string fullPath)
    {
        Probed.Add(fullPath);
        if (Unknown.Contains(fullPath))
        {
            return FileProbe.Unknown;
        }

        return Existing.Contains(fullPath) ? FileProbe.Exists : FileProbe.Missing;
    }

    FileDetails? IAutostartFiles.Details(string fullPath)
    {
        DetailsRequested.Add(fullPath);
        return Details.TryGetValue(fullPath, out var details) ? details : null;
    }
}
