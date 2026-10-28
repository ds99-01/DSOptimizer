using System;
using System.Collections.Generic;
using System.ComponentModel;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Ponowne uruchomienie bez startu prawdziwego procesu: wyłącznie konstruktor wewnętrzny z fałszywym <c>start</c>.</summary>
[Trait("Category", "Windows")]
public class ProgramRelauncherTests
{
    private readonly List<string> _events = new();

    [Fact]
    public void Relaunch_StartsOwnExeWithoutArguments()
    {
        var relauncher = new ProgramRelauncher(
            new RecordingLog(_events),
            () => @"C:\Narzedzia\DSOptimizer\DSOptimizer.exe",
            (file, args, dir) => _events.Add($"start {file}|{args}|{dir}"));

        relauncher.Relaunch();

        Assert.Equal(2, _events.Count);
        Assert.StartsWith("Info ", _events[0], StringComparison.Ordinal);
        Assert.Equal(@"start C:\Narzedzia\DSOptimizer\DSOptimizer.exe||C:\Narzedzia\DSOptimizer", _events[1]);
    }

    [Fact]
    public void Relaunch_StartFails_ThrowsOperationException()
    {
        var relauncher = new ProgramRelauncher(
            new RecordingLog(_events),
            () => @"C:\x\DSOptimizer.exe",
            (_, _, _) => throw new Win32Exception("Odmowa dostępu"));

        var ex = Assert.Throws<OperationException>(() => relauncher.Relaunch());

        Assert.Equal("Nie udało się ponownie uruchomić programu: Odmowa dostępu", ex.Message);
        Assert.IsType<Win32Exception>(ex.InnerException);
    }

    [Fact]
    public void Relaunch_EmptyExePath_ThrowsOperationExceptionWithoutStart()
    {
        var started = false;
        var relauncher = new ProgramRelauncher(new RecordingLog(_events), () => string.Empty, (_, _, _) => started = true);

        Assert.Throws<OperationException>(() => relauncher.Relaunch());
        Assert.False(started);
    }

    [Fact]
    public void Constructor_NullLog_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ProgramRelauncher(null!, () => "x", (_, _, _) => { }));

    private sealed class RecordingLog : ILog
    {
        private readonly List<string> _events;

        public RecordingLog(List<string> events) => _events = events;

        public void Info(string component, string message) => _events.Add($"Info {component}: {message}");

        public void Warn(string component, string message) => _events.Add($"Warn {component}: {message}");

        public void Fail(string component, string message, Exception? exception = null) => _events.Add($"Fail {component}: {message}");
    }
}
