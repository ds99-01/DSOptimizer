using System;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Windows.Support;

public sealed class NullLog : ILog
{
    public void Info(string component, string message)
    {
    }

    public void Warn(string component, string message)
    {
    }

    public void Fail(string component, string message, Exception? exception = null)
    {
    }
}
