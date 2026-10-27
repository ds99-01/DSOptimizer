using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>msiexec w pamięci (nigdy prawdziwy proces): zapisuje wywołania, woła <see cref="OnInstall"/> i zwraca <see cref="Result"/>.</summary>
public sealed class FakeMsiInstaller : IMsiInstaller
{
    public List<(string MsiPath, string LogPath, TimeSpan WaitLimit)> Calls { get; } = new();

    public List<string>? Trace { get; set; }

    public MsiRun Result { get; set; } = new(0, false);

    public Exception? Throws { get; set; }

    /// <summary>Wołane przy instalacji (np. dopisanie wpisu RustDesk do <see cref="FakeInstalledAppsReader"/>).</summary>
    public Action? OnInstall { get; set; }

    public MsiRun Install(string msiPath, string logPath, TimeSpan waitLimit)
    {
        Calls.Add((msiPath, logPath, waitLimit));
        Trace?.Add("Msi " + msiPath);
        if (Throws is not null)
        {
            throw Throws;
        }

        OnInstall?.Invoke();
        return Result;
    }
}
