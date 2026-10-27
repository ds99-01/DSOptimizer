using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

public sealed class FakeSystemInfo : ISystemInfo
{
    public int Build { get; set; } = 26300;

    public int Revision { get; set; }

    public string Edition { get; set; } = "Professional";

    public string MachineName { get; set; } = "TEST-PC";
}
