using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Restart bez restartowania hosta: start procesu podmieniony (wyłącznie konstruktor wewnętrzny z fałszywym <c>run</c>),
/// sprawdzamy ProcessStartInfo, limit czasu, komunikaty błędów i ślad w pliku logu (Info przed startem, Info z przyczyną przy błędzie).
/// </summary>
[Trait("Category", "Windows")]
public class ShutdownRestarterTests
{
    private const string StartLine = "Info Restart: shutdown.exe /r /t 0 zlecony przez użytkownika.";

    /// <summary>Wspólna oś zdarzeń: wpisy logu i moment wywołania <c>run</c>, żeby sprawdzić kolejność.</summary>
    private readonly List<string> _events = new();
    private ProcessStartInfo? _info;
    private TimeSpan _timeout;

    private ShutdownRestarter Restarter(Func<int?> result)
        => new(new RecordingLog(_events), (info, timeout) =>
        {
            _events.Add("run");
            _info = info;
            _timeout = timeout;
            return result();
        });

    [Fact]
    public void Restart_StartsShutdownWithExactArguments()
    {
        Restarter(() => 0).Restart();

        Assert.NotNull(_info);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), _info!.FileName);
        Assert.Equal("/r /t 0", _info.Arguments);
        Assert.False(_info.UseShellExecute);
        Assert.True(_info.CreateNoWindow);
        Assert.Equal(Environment.SystemDirectory, _info.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(10), _timeout);
    }

    [Fact]
    public void Restart_Success_LogsStartBeforeRun()
    {
        Restarter(() => 0).Restart();

        Assert.Equal(new[] { StartLine, "run" }, _events);
    }

    [Fact]
    public void Restart_NonZeroExit_Throws()
    {
        var ex = Assert.Throws<OperationException>(() => Restarter(() => 1190).Restart());

        Assert.Equal("Nie udało się uruchomić ponownie komputera: shutdown.exe zakończył się kodem 1190.", ex.Message);
        Assert.Equal(new[] { StartLine, "run", "Info Restart: " + ex.Message }, _events);
    }

    [Fact]
    public void Restart_StartFails_Throws()
    {
        var ex = Assert.Throws<OperationException>(() => Restarter(() => throw new Win32Exception("odmowa")).Restart());

        Assert.Equal("Nie udało się uruchomić ponownie komputera: odmowa", ex.Message);
        Assert.IsType<Win32Exception>(ex.InnerException);
        Assert.Equal(new[] { StartLine, "run", "Info Restart: " + ex.Message }, _events);
    }

    [Fact]
    public void Restart_Timeout_Throws()
    {
        var ex = Assert.Throws<OperationException>(() => Restarter(() => null).Restart());

        Assert.Equal("Nie udało się uruchomić ponownie komputera: shutdown.exe nie zakończył się w ciągu 10 s.", ex.Message);
        Assert.Equal(new[] { StartLine, "run", "Info Restart: " + ex.Message }, _events);
    }

    [Fact]
    public void Constructor_NullLog_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ShutdownRestarter(null!, (_, _) => 0));

    /// <summary>Zapisuje wpisy jako „Poziom Komponent: komunikat” na wspólnej osi zdarzeń.</summary>
    private sealed class RecordingLog : ILog
    {
        private readonly List<string> _events;

        public RecordingLog(List<string> events) => _events = events;

        public void Info(string component, string message) => _events.Add($"Info {component}: {message}");

        public void Warn(string component, string message) => _events.Add($"Warn {component}: {message}");

        public void Fail(string component, string message, Exception? exception = null) => _events.Add($"Fail {component}: {message}");
    }
}
