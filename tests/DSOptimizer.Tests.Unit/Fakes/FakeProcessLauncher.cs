using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Zapisuje wywołania zamiast uruchamiać cokolwiek; <see cref="Failure"/> rzucany przy każdym wywołaniu.</summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    public List<LaunchSpec> Calls { get; } = new();

    /// <summary>Wspólny ślad kolejności wywołań wszystkich fake'ów (opcjonalny): <c>Launch &lt;plik&gt;</c>.</summary>
    public List<string>? Trace { get; set; }

    public Exception? Failure { get; set; }

    public void Launch(LaunchSpec launch)
    {
        Calls.Add(launch);
        Trace?.Add("Launch " + launch.File);
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
