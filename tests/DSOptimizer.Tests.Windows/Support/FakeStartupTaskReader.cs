using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>Zadania startowe aplikacji ze Sklepu z listy – zamiast skryptu AppX, który na hoście nie startuje.</summary>
public sealed class FakeStartupTaskReader : IStartupTaskReader
{
    public List<StartupTaskManifest> Tasks { get; } = new();

    public List<string> RequestedSids { get; } = new();

    public StartupTaskReading Read(string userSid)
    {
        RequestedSids.Add(userSid);
        return new StartupTaskReading(Tasks.ToArray(), Array.Empty<string>());
    }
}
