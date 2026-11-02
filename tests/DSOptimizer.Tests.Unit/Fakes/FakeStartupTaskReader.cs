using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Zadania startowe aplikacji ze Sklepu z listy; opcjonalny wyjątek zamiast odczytu.</summary>
public sealed class FakeStartupTaskReader : IStartupTaskReader
{
    public List<StartupTaskManifest> Tasks { get; } = new();

    public List<string> Warnings { get; } = new();

    public Exception? Failure { get; set; }

    public List<string> RequestedSids { get; } = new();

    public int ReadCount => RequestedSids.Count;

    public StartupTaskReading Read(string userSid)
    {
        RequestedSids.Add(userSid);
        if (Failure is not null)
        {
            throw Failure;
        }

        return new StartupTaskReading(Tasks.ToArray(), Warnings.ToArray());
    }
}
