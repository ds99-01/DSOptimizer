using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

public sealed class ListLog : ILog
{
    public List<string> Lines { get; } = new();

    public void Info(string component, string message) => Lines.Add($"INFO [{component}] {message}");

    public void Warn(string component, string message) => Lines.Add($"WARN [{component}] {message}");

    public void Fail(string component, string message, Exception? exception = null)
        => Lines.Add($"ERROR [{component}] {message} {exception?.Message}");
}
