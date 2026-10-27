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

[Trait("Category", "Windows")]
[Collection(ToolProcessIsolation.Name)]
public class System32ToolRunnerTests
{
    private readonly System32ToolRunner _runner = new();

    [Fact]
    public void Run_Hostname_ReturnsZeroExitAndComputerName()
    {
        var result = _runner.Run(new ToolRequest { FileName = "hostname.exe" }, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Output, line => string.Equals(line.Trim(), Environment.MachineName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_NonZeroExit_IsResultNotException()
    {
        // hostname.exe z nieznanym przełącznikiem kończy się kodem niezerowym i komunikatem (stderr scalony z wyjściem).
        var result = _runner.Run(new ToolRequest { FileName = "hostname.exe", Arguments = "/nie-ma-takiego" }, CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.Output);
    }

    [Theory]
    [InlineData(@"C:\x\a.exe")]
    [InlineData(@"..\a.exe")]
    [InlineData("hostname")]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("pwsh.exe")]
    [InlineData("wscript.exe")]
    [InlineData("cscript.exe")]
    [InlineData("mshta.exe")]
    [InlineData("rundll32.exe")]
    [InlineData("regsvr32.exe")]
    [InlineData("wmic.exe")]
    [InlineData("certutil.exe")]
    public void ToolRequest_InvalidFileName_ThrowsArgumentExceptionWithoutStartingProcess(string file)
    {
        Assert.Throws<ArgumentException>(() => _runner.Run(new ToolRequest { FileName = file }, CancellationToken.None));
    }

    [Fact]
    public void Run_Timeout_ThrowsTimeoutAndKillsProcess()
    {
        var before = ChildPings.Current();
        var stopwatch = Stopwatch.StartNew();

        var ex = Assert.Throws<OperationException>(() => _runner.Run(
            new ToolRequest { FileName = "ping.exe", Arguments = "-n 30 127.0.0.1", Timeout = TimeSpan.FromSeconds(1) },
            CancellationToken.None));

        Assert.Equal(OperationErrorKind.Timeout, ex.Kind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Zatrzymanie trwało {stopwatch.Elapsed}.");
        Assert.Empty(StartedPings(before));
    }

    [Fact]
    public void Run_Cancellation_ThrowsCancelledAndKillsProcess()
    {
        var before = ChildPings.Current();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var ex = Assert.Throws<OperationException>(() => _runner.Run(
            new ToolRequest { FileName = "ping.exe", Arguments = "-n 30 127.0.0.1", Timeout = TimeSpan.FromSeconds(30) },
            cts.Token));

        Assert.Equal(OperationErrorKind.Cancelled, ex.Kind);
        Assert.Empty(StartedPings(before));
    }

    [Fact]
    public void Run_AlreadyCancelled_ThrowsCancelledWithoutStarting()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = Assert.Throws<OperationException>(() => _runner.Run(new ToolRequest { FileName = "hostname.exe" }, cts.Token));

        Assert.Equal(OperationErrorKind.Cancelled, ex.Kind);
    }

    [Fact]
    public void Run_MissingTool_ThrowsNotFound()
    {
        var ex = Assert.Throws<OperationException>(() => _runner.Run(new ToolRequest { FileName = "dso-nie-ma-takiego.exe" }, CancellationToken.None));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public void Run_StdinIsNul_ToolReadingInputEndsImmediately()
    {
        // findstr bez plików czyta stdin; z podpiętym NUL kończy od razu (kod 1 = brak dopasowania), nie wisi na konsoli.
        var result = _runner.Run(new ToolRequest { FileName = "findstr.exe", Arguments = "x", Timeout = TimeSpan.FromSeconds(10) }, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
    }

    /// <summary>Żywe procesy <c>ping</c> uruchomione przez proces testów od <paramref name="before"/> (cudze <c>ping</c> się nie liczą).</summary>
    private static int[] StartedPings(HashSet<int> before)
        => ChildPings.Current().Except(before).Where(ChildPings.IsAlive).ToArray();
}
