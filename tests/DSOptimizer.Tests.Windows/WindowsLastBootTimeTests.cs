using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Odczyt czasu ostatniego startu systemu z CIM (tylko do odczytu) na prawdziwym hoście.</summary>
[Trait("Category", "Windows")]
public class WindowsLastBootTimeTests
{
    private sealed class NullLog : ILog
    {
        public void Info(string component, string message) { }

        public void Warn(string component, string message) { }

        public void Fail(string component, string message, Exception? exception = null) { }
    }

    [Fact]
    public void Read_Host_IsPastAndRecent()
    {
        var boot = new WindowsLastBootTime(new NullLog()).Read();

        Assert.NotNull(boot);
        Assert.True(boot!.Value < DateTimeOffset.UtcNow);
        Assert.True(boot.Value > DateTimeOffset.UtcNow.AddDays(-400));
    }
}
