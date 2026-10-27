using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

public sealed class FakeServicingActivity : IServicingActivity
{
    /// <summary>Nazwa trwającego procesu serwisowania; <c>null</c> = nic nie trwa.</summary>
    public string? Active { get; set; }

    public int Calls { get; private set; }

    public string? ActiveOperation()
    {
        Calls++;
        return Active;
    }
}
