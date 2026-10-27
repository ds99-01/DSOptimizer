using System;
using System.Collections.Generic;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>Synchroniczny IProgress (Progress&lt;T&gt; raportuje asynchronicznie, co psuje asercje).</summary>
public sealed class ListProgress : IProgress<string>
{
    public List<string> Items { get; } = new();

    public void Report(string value) => Items.Add(value);
}

/// <summary>Synchroniczny IProgress&lt;T&gt; z opcjonalnym wywołaniem zwrotnym (do symulowania zdarzeń w trakcie serii).</summary>
public sealed class ListProgress<T> : IProgress<T>
{
    public List<T> Items { get; } = new();

    public Action<T>? OnReport { get; set; }

    public void Report(T value)
    {
        Items.Add(value);
        OnReport?.Invoke(value);
    }
}