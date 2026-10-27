using System;
using System.Collections.Generic;
using System.Globalization;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>
/// Folder pobrań bez dysku: ścieżki <c>C:\dso-downloads\&lt;n&gt;&lt;rozszerzenie&gt;</c>, skrót z <see cref="Hash"/> (albo wyjątek),
/// każda próba usunięcia zapisana w <see cref="Deleted"/> z wynikiem <see cref="DeleteResult"/>.
/// </summary>
public sealed class FakeDownloadFiles : IDownloadFiles
{
    public List<string> Paths { get; } = new();

    public List<string> HashCalls { get; } = new();

    public List<string> Deleted { get; } = new();

    public List<string>? Trace { get; set; }

    public string Hash { get; set; } = string.Empty;

    public Exception? HashThrows { get; set; }

    public bool DeleteResult { get; set; } = true;

    /// <summary>Wyjątek z <see cref="TryDelete"/> (poza kontraktem – implementacja nie powinna rzucać).</summary>
    public Exception? DeleteThrows { get; set; }

    public string NewDownloadPath(string extension)
    {
        var path = @"C:\dso-downloads\" + (Paths.Count + 1).ToString(CultureInfo.InvariantCulture) + extension;
        Paths.Add(path);
        return path;
    }

    public string Sha256(string path)
    {
        HashCalls.Add(path);
        Trace?.Add("Sha256 " + path);
        if (HashThrows is not null)
        {
            throw HashThrows;
        }

        return Hash;
    }

    public bool TryDelete(string path)
    {
        Deleted.Add(path);
        Trace?.Add("Delete " + path);
        if (DeleteThrows is not null)
        {
            throw DeleteThrows;
        }

        return DeleteResult;
    }
}
