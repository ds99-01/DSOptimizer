using System;

namespace DSOptimizer.Core.Abstractions;

public interface ILog
{
    void Info(string component, string message);

    void Warn(string component, string message);

    void Fail(string component, string message, Exception? exception = null);
}
