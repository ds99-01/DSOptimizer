using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Pliki pomiaru Windows (<c>StartupInfo</c>) z treścią XML; liczy odczyty każdego pliku.</summary>
public sealed class FakeStartupInfoSource : IStartupInfoSource
{
    private readonly List<(StartupInfoFile File, string Xml)> _files = new();

    public Exception? FilesFailure { get; set; }

    public HashSet<string> ThrowingReads { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> ReadCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> RequestedSids { get; } = new();

    public StartupInfoFile Add(int number, DateTimeOffset writtenAt, string xml, string sid = "S-1-5-21-1-2-3-1001")
    {
        var file = new StartupInfoFile(@"C:\Windows\System32\wdi\LogFiles\StartupInfo\" + sid + "_StartupInfo" + number + ".xml", number, writtenAt);
        _files.Add((file, xml));
        return file;
    }

    public IReadOnlyList<StartupInfoFile> Files(string userSid)
    {
        RequestedSids.Add(userSid);
        if (FilesFailure is not null)
        {
            throw FilesFailure;
        }

        return _files.ConvertAll(f => f.File);
    }

    public string Read(StartupInfoFile file)
    {
        ReadCounts[file.Path] = ReadCounts.TryGetValue(file.Path, out var count) ? count + 1 : 1;
        if (ThrowingReads.Contains(file.Path))
        {
            throw new OperationException(OperationErrorKind.AccessDenied, "Read denied (fake): " + file.Path);
        }

        foreach (var (candidate, xml) in _files)
        {
            if (candidate == file)
            {
                return xml;
            }
        }

        throw new InvalidOperationException("Unknown file (fake): " + file.Path);
    }
}
