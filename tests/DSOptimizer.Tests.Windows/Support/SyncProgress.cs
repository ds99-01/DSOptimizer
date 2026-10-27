using System;
using System.Collections.Generic;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>IProgress wywoływany z wątków czytających stdout/stderr — dlatego z blokadą.</summary>
public sealed class SyncProgress : IProgress<string>
{
    private readonly object _gate = new();
    private readonly List<string> _items = new();

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    public void Report(string value)
    {
        lock (_gate)
        {
            _items.Add(value);
        }
    }
}
