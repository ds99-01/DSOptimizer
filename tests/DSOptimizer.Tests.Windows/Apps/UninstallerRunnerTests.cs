using System;
using System.IO;
using System.Threading;
using DSOptimizer.Core.Apps;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using DSOptimizer.Windows.Apps;
using Xunit;

namespace DSOptimizer.Tests.Windows.Apps;

/// <summary>
/// Start deinstalatora z czekaniem i limitem – wyłącznie na znakowanym, ukrytym <c>powershell.exe</c> (Start-Sleep / exit),
/// który test sam zamyka (<see cref="MarkedTestProcess"/>). Żaden prawdziwy deinstalator nie jest uruchamiany.
/// </summary>
[Trait("Category", "Windows")]
public class UninstallerRunnerTests
{
    private static UninstallPlan Plan(string arguments) => new(PowerShellScriptRunner.PowerShellPath, arguments, UninstallMode.Quiet);

    [Fact]
    public void Run_ProcessExits_ReturnsItsExitCode()
    {
        using var marked = new MarkedTestProcess();

        var run = UninstallerRunner.Run(
            Plan($"-NoProfile -WindowStyle Hidden -Command \"Start-Sleep -Milliseconds 300; exit 7 # {marked.Marker}\""),
            TimeSpan.FromSeconds(60),
            CancellationToken.None);

        Assert.Equal(new Core.Abstractions.UninstallerRun(7, false), run);
        Assert.Empty(marked.FindProcessIds());
    }

    [Fact]
    public void Run_TimeLimit_StopsWaiting_AndProcessKeepsRunning()
    {
        using var marked = new MarkedTestProcess();
        var started = DateTime.UtcNow;

        var run = UninstallerRunner.Run(Plan(marked.Arguments), TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.True(run.TimedOut);
        Assert.Null(run.ExitCode);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30), "Runner czekał dłużej niż limit.");
        Thread.Sleep(TimeSpan.FromSeconds(1));
        Assert.NotEmpty(marked.FindProcessIds()); // nie zabity po limicie; Dispose zamyka go na końcu testu
    }

    [Fact]
    public void Run_Cancelled_StopsWaiting_AndProcessKeepsRunning()
    {
        using var marked = new MarkedTestProcess();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var run = UninstallerRunner.Run(Plan(marked.Arguments), TimeSpan.FromMinutes(5), cts.Token);

        Assert.True(run.TimedOut);
        Assert.NotEmpty(marked.FindProcessIds());
    }

    [Fact]
    public void Run_MissingFile_ThrowsWithoutStart()
    {
        var missing = Path.Combine(Path.GetTempPath(), "dso-tests", "brak-" + Guid.NewGuid().ToString("N"), "unins000.exe");

        var ex = Assert.Throws<FileNotFoundException>(
            () => UninstallerRunner.Run(new UninstallPlan(missing, "/S", UninstallMode.Quiet), TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal(missing, ex.FileName);
    }

    [Theory]
    [InlineData("unins000.exe")]
    [InlineData(@"bin\unins000.exe")]
    [InlineData(@"\\serwer\udzial\unins000.exe")]
    [InlineData(@"\dso-tests-brak\unins000.exe")] // względem bieżącego dysku (np. pendrive'a); plik nie istnieje, więc nic nie startuje
    [InlineData(@"C:dso-tests-brak\unins000.exe")] // względem katalogu bieżącego na C:
    public void Run_RelativeOrUncPath_Throws(string fileName)
        => Assert.Throws<ArgumentException>(
            () => UninstallerRunner.Run(new UninstallPlan(fileName, "/S", UninstallMode.Quiet), TimeSpan.FromSeconds(5), CancellationToken.None));
}
