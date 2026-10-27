using System;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Stan ochrony systemu: true/false/null (nie udało się sprawdzić) albo wyjątek.</summary>
public sealed class FakeSystemProtection : ISystemProtectionReader
{
    public bool? Enabled { get; set; } = true;

    public Exception? Throw { get; set; }

    public int Calls { get; private set; }

    public bool? IsEnabled()
    {
        Calls++;
        if (Throw is not null)
        {
            throw Throw;
        }

        return Enabled;
    }
}
