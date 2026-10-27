using System;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Windows.Support;

public sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);
}
