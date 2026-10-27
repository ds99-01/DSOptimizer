using System;

namespace DSOptimizer.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
