using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using Xunit;

namespace DSOptimizer.Tests.Unit.Abstractions;

public class TeeLogTests
{
    [Fact]
    public void Info_Warn_Fail_ForwardToBothInOrder()
    {
        var order = new List<string>();
        var primary = new RecordingLog("P", order);
        var secondary = new RecordingLog("S", order);
        var tee = new TeeLog(primary, secondary);
        var ex = new InvalidOperationException("boom");

        tee.Info("K", "info");
        tee.Warn("K", "warn");
        tee.Fail("K", "fail", ex);

        Assert.Equal(new[] { "P:Info", "S:Info", "P:Warn", "S:Warn", "P:Fail", "S:Fail" }, order);
        foreach (var log in new[] { primary, secondary })
        {
            Assert.Equal(
                new[] { ("Info", "K", "info", (Exception?)null), ("Warn", "K", "warn", null), ("Fail", "K", "fail", ex) },
                log.Entries);
        }
    }

    [Fact]
    public void SecondaryThrows_PrimaryStillWritten_NoException()
    {
        var order = new List<string>();
        var primary = new RecordingLog("P", order);
        var tee = new TeeLog(primary, new ThrowingLog());

        tee.Info("K", "a");
        tee.Warn("K", "b");
        tee.Fail("K", "c", new InvalidOperationException("x"));

        Assert.Equal(3, primary.Entries.Count);
    }

    [Fact]
    public void PrimaryThrows_ExceptionPropagates()
    {
        var order = new List<string>();
        var secondary = new RecordingLog("S", order);
        var tee = new TeeLog(new ThrowingLog(), secondary);

        Assert.Throws<InvalidOperationException>(() => tee.Info("K", "a"));
        Assert.Throws<InvalidOperationException>(() => tee.Warn("K", "a"));
        Assert.Throws<InvalidOperationException>(() => tee.Fail("K", "a"));
    }

    private sealed class RecordingLog : ILog
    {
        private readonly string _name;
        private readonly List<string> _order;

        public RecordingLog(string name, List<string> order)
        {
            _name = name;
            _order = order;
        }

        public List<(string Level, string Component, string Message, Exception? Exception)> Entries { get; } = new();

        public void Info(string component, string message) => Add("Info", component, message, null);

        public void Warn(string component, string message) => Add("Warn", component, message, null);

        public void Fail(string component, string message, Exception? exception = null) => Add("Fail", component, message, exception);

        private void Add(string level, string component, string message, Exception? exception)
        {
            _order.Add($"{_name}:{level}");
            Entries.Add((level, component, message, exception));
        }
    }

    private sealed class ThrowingLog : ILog
    {
        public void Info(string component, string message) => throw new InvalidOperationException("log");

        public void Warn(string component, string message) => throw new InvalidOperationException("log");

        public void Fail(string component, string message, Exception? exception = null) => throw new InvalidOperationException("log");
    }
}
