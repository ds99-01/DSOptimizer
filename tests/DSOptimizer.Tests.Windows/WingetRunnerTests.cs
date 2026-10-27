using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DSOptimizer.Core.Errors;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Runner winget na prawdziwym systemie, ale **bez winget**: prawdziwy winget nigdy nie startuje na hoście (żadne polecenie).
/// Runner dostaje pełną ścieżkę <c>hostname.exe</c>, <c>findstr.exe</c> (plik w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>) albo
/// <c>ping.exe -n N 127.0.0.1</c> z System32. Plik <c>winget.exe</c> pojawia się tylko jako pusty plik w katalogu testu,
/// a bezpiecznik <c>DSO_BLOCK_REAL_WINGET</c> (<see cref="TestGuards"/>) odmawia jego startu.
/// </summary>
[Trait("Category", "Windows")]
[Collection(ToolProcessIsolation.Name)]
public class WingetRunnerTests
{
    private static string SystemTool(string file) => Path.Combine(Environment.SystemDirectory, file);

    [Fact]
    public void Hostname_ExitZero_OutputAndOnLine()
    {
        var lines = new List<string>();

        var result = new WingetRunner(SystemTool("hostname.exe")).Run(string.Empty, TimeSpan.FromSeconds(30), lines.Add);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(result.Output, line => string.Equals(line.Trim(), Environment.MachineName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.Output, lines);
    }

    [Fact]
    public void Utf8Output_Decoded()
    {
        using var temp = new TempDirectory();
        var file = temp.Sub("utf8.txt");
        const string line = "zażółć gęślą x";
        File.WriteAllText(file, line + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var result = new WingetRunner(SystemTool("findstr.exe")).Run($"x \"{file}\"", TimeSpan.FromSeconds(30), null);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new[] { line }, result.Output);
    }

    [Fact]
    public void Timeout_ProcessStaysAlive()
    {
        var before = ChildPings.Current();
        var stopwatch = Stopwatch.StartNew();
        var started = new List<int>();
        try
        {
            var result = new WingetRunner(SystemTool("ping.exe")).Run("-n 30 127.0.0.1", TimeSpan.FromSeconds(1), null);
            started.AddRange(ChildPings.Current().Except(before));

            Assert.True(result.TimedOut);
            Assert.Null(result.ExitCode);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Zwrot po limicie trwał {stopwatch.Elapsed}.");
            var ping = Assert.Single(started);
            Assert.True(ChildPings.IsAlive(ping), "ping zginął zaraz po limicie czasu.");

            // ping pisze co sekundę na stdout: po powrocie runnera proces dalej działa (potok czyta wątek tła, nikt go nie zabija).
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
    public void MissingFile_NotFound()
    {
        using var temp = new TempDirectory();
        var runner = new WingetRunner(temp.Sub("dso-nie-ma-takiego.exe"));

        var ex = Assert.Throws<OperationException>(() => runner.Run("--version", TimeSpan.FromSeconds(5), null));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public void RealWingetBlockedInTests()
    {
        // Pusty plik o nazwie winget.exe: bez bezpiecznika CreateProcessW i tak by go nie uruchomił (zły format), więc wyjątek inny
        // niż InvalidOperationException znaczy brak bezpiecznika, a prawdziwy winget nie startuje nawet wtedy.
        Assert.Equal("1", Environment.GetEnvironmentVariable(TestGuards.BlockRealWinget));
        using var temp = new TempDirectory();
        var fake = temp.Sub("winget.exe");
        File.WriteAllBytes(fake, Array.Empty<byte>());
        var lines = new List<string>();

        var ex = Assert.Throws<InvalidOperationException>(() => new WingetRunner(fake).Run("--version", TimeSpan.FromSeconds(5), lines.Add));

        Assert.StartsWith("Bezpiecznik testów:", ex.Message, StringComparison.Ordinal);
        Assert.Empty(lines);
    }

    [Fact]
    public void AliasPath_UnderLocalAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "winget.exe");

        Assert.Equal(expected, WingetRunner.AliasPath());
        Assert.True(Path.IsPathRooted(WingetRunner.AliasPath()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveWaitLimit_ThrowsWithoutStarting(int seconds)
    {
        var runner = new WingetRunner(SystemTool("hostname.exe"));

        Assert.Throws<ArgumentOutOfRangeException>(() => runner.Run(string.Empty, TimeSpan.FromSeconds(seconds), null));
    }
}
