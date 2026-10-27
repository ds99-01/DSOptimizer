using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

public sealed class FakeUserContext : IUserContext
{
    public string Sid { get; set; } = "S-1-5-21-1000";

    public bool IsCertain { get; set; } = true;
}
