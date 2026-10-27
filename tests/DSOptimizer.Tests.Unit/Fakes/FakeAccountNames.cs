using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>SID → nazwa konta ze słownika; brak wpisu = null (jak nieudane tłumaczenie).</summary>
public sealed class FakeAccountNames : IAccountNames
{
    public Dictionary<string, string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = new();

    public string? NameOf(string sid)
    {
        Calls.Add(sid);
        return Names.TryGetValue(sid, out var name) ? name : null;
    }
}
