using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Wolne miejsce z kolejki odczytów (kolejne wywołania dostają kolejne wartości, ostatnia się powtarza).</summary>
public sealed class FakeDiskSpace : IDiskSpace
{
    public Queue<long?> Readings { get; } = new();

    public List<string> Calls { get; } = new();

    private long? _last;

    public long? FreeBytes(string root)
    {
        Calls.Add(root);
        if (Readings.Count > 0)
        {
            _last = Readings.Dequeue();
        }

        return _last;
    }
}
