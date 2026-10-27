using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

public sealed class FakeDomainInfo : IDomainInfo
{
    public bool? Value { get; set; } = false;

    public int Calls { get; private set; }

    public bool? IsDomainJoined()
    {
        Calls++;
        return Value;
    }
}
