using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Runner długich narzędzi na prawdziwym systemie. Uruchamiane wyłącznie <c>hostname.exe</c>, <c>findstr.exe</c> (stdin = NUL)
/// i <c>ping.exe -n 30 127.0.0.1</c>; DISM, SFC ani cleanmgr nigdy nie startują na hoście.
/// </summary>
[Trait("Category", "Windows")]
[Collection(ToolProcessIsolation.Name)]
public class ServicingToolRunnerTests
{
    private readonly ServicingToolRunner _runner = new();

    [Fact]
    public void Run_Hostname_ExitZero_ComputerNameInOutputAndOnLine()
    {
        var lines = new List<string>();

        var result = _runner.Run(new ToolRequest { FileName = "hostname.exe" }, TimeSpan.FromSeconds(30), lines.Add);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(result.Output, line => string.Equals(line.Trim(), Environment.MachineName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.Output, lines);
    }

    [Fact]
    public void Run_NullOnLine_StillCollectsOutput()
    {
        var result = _runner.Run(new ToolRequest { FileName = "hostname.exe" }, TimeSpan.FromSeconds(30), null);

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.Output);
    }

    [Fact]
    public void Run_StdinIsNul_ToolReadingInputEndsImmediately()
    {
        var result = _runner.Run(new ToolRequest { FileName = "findstr.exe", Arguments = "x" }, TimeSpan.FromSeconds(10), null);

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void Timeout_ProcessStaysAlive()
    {
        var before = ChildPings.Current();
        var stopwatch = Stopwatch.StartNew();
        var started = new List<int>();
        try
        {
            var result = _runner.Run(
                new ToolRequest { FileName = "ping.exe", Arguments = "-n 30 127.0.0.1" },
                TimeSpan.FromSeconds(1),
                null);
            started.AddRange(ChildPings.Current().Except(before));

            Assert.True(result.TimedOut);
            Assert.Null(result.ExitCode);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Zwrot po limicie trwał {stopwatch.Elapsed}.");
            var ping = Assert.Single(started);
            Assert.True(ChildPings.IsAlive(ping), "ping zginął zaraz po limicie czasu.");

            // ping pisze co sekundę na stdout: po powrocie runnera narzędzie dalej działa (potok czyta wątek tła).
            Thread.Sleep(TimeSpan.FromSeconds(2.5));
            Assert.True(ChildPings.IsAlive(ping), "ping zginął po powrocie runnera.");
        }
        finally
        {
            foreach (var id in started.Count > 0 ? started : ChildPings.Current().Except(before).ToList())
            {
                ChildPings.Kill(id);
            }
        }
    }

    [Fact]
    public void Timeout_PipeKeptOpen_ToolFinishesWithExitZero_NoLinesAfterReturn()
    {
        var before = ChildPings.Current();
        var lines = 0;
        Process? ping = null;
        try
        {
            var result = _runner.Run(
                new ToolRequest { FileName = "ping.exe", Arguments = "-n 4 127.0.0.1" },
                TimeSpan.FromSeconds(1),
                _ => Interlocked.Increment(ref lines));
            var linesAtReturn = Volatile.Read(ref lines);
            var id = Assert.Single(ChildPings.Current().Except(before));
            ping = Process.GetProcessById(id);
            _ = ping.Handle; // uchwyt otwarty przed końcem procesu, żeby odczytać kod wyjścia

            Assert.True(result.TimedOut);
            Assert.Null(result.ExitCode);
            var outputAtReturn = result.Output.Count;

            // ping pisze kolejne odpowiedzi na stdout; z potokiem dalej czytanym w tle kończy się sam i bez błędu zapisu.
            Assert.True(ping.WaitForExit(15000), "ping nie zakończył się sam.");
            Assert.Equal(0, ping.ExitCode);
            Thread.Sleep(300);
            Assert.Equal(linesAtReturn, Volatile.Read(ref lines));
            Assert.Equal(outputAtReturn, result.Output.Count);
        }
        finally
        {
            if (ping is not null && !ping.HasExited)
            {
                ping.Kill();
            }

            ping?.Dispose();
        }
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("wmic.exe")]
    [InlineData(@"C:\x\dism.exe")]
    [InlineData("dism")]
    public void InvalidFileName_ThrowsArgumentExceptionWithoutStartingProcess(string file)
    {
        // Wyjątek leci już z ToolRequest, więc Run nie dostaje żądania i żaden proces nie powstaje.
        Assert.Throws<ArgumentException>(() => _runner.Run(new ToolRequest { FileName = file }, TimeSpan.FromSeconds(5), null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Run_NonPositiveWaitLimit_ThrowsWithoutStarting(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.Run(new ToolRequest { FileName = "hostname.exe" }, TimeSpan.FromSeconds(seconds), null));
    }

    [Fact]
    public void Run_MissingTool_ThrowsNotFound()
    {
        var ex = Assert.Throws<OperationException>(() => _runner.Run(new ToolRequest { FileName = "dso-nie-ma-takiego.exe" }, TimeSpan.FromSeconds(5), null));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public void Run_OnLineThrows_ExceptionReachesCaller()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _runner.Run(
            new ToolRequest { FileName = "hostname.exe" },
            TimeSpan.FromSeconds(30),
            _ => throw new InvalidOperationException("błąd odbiorcy")));

        Assert.Equal("błąd odbiorcy", ex.Message);
    }
}
