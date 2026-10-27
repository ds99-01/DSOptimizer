using System;
using System.Security.Principal;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Review Focus 1: proces uruchomiony ze skryptu ginie razem z Job Objectem runnera, a proces z <c>launch</c> przeżywa Execute.
/// Jedyny proces, jaki te testy uruchamiają, to ukryty powershell.exe ze Start-Sleep (ruling R6), zatrzymywany w Dispose.
/// </summary>
[Trait("Category", "Windows")]
public class ProcessLifetimeTests
{
    private static string ScriptStartingMarkedProcess(MarkedTestProcess marked)
        => $"Start-Process -FilePath \"$env:SystemRoot\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -ArgumentList '{marked.Arguments}'\n"
            + "$deadline = (Get-Date).AddSeconds(10)\n"
            + "do {\n"
            + $"    $found = @(Get-CimInstance Win32_Process -Filter \"Name = 'powershell.exe'\" | Where-Object {{ $_.CommandLine -like '*{marked.Marker}*' }}).Count -gt 0\n"
            + "    if (-not $found) { Start-Sleep -Milliseconds 200 }\n"
            + "} while (-not $found -and (Get-Date) -lt $deadline)\n"
            + "if ($found) { exit 0 } else { exit 2 }";

    private static TweakEngine Engine(TempDirectory temp)
    {
        var log = new NullLog();
        var registry = new WindowsRegistry();
        return new TweakEngine(
            new EngineServices
            {
                Registry = registry,
                Services = new WindowsServiceManager(registry),
                Scripts = new PowerShellScriptRunner(temp.AppRoot, log),
                Launcher = new ShellProcessLauncher(),
                Journal = new JsonFileJournalStore(temp.AppRoot, Environment.MachineName, new SystemClock()),
                RestorePoints = new NoRestorePoints(),
                SystemInfo = new WindowsSystemInfo(registry),
                User = new CurrentUser(),
                Log = log,
                Clock = new SystemClock(),
            },
            "sha256:lifetime");
    }

    private static TweakDefinition Item(TweakKind kind) => new()
    {
        Id = "test.lifetime",
        Category = "test",
        Kind = kind,
        Title = "Proces testowy",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.Machine,
        Timeout = TimeSpan.FromSeconds(60),
    };

    [Fact]
    public void ScriptRunner_ProcessStartedFromScript_DoesNotSurviveRun()
    {
        using var temp = new TempDirectory();
        using var marked = new MarkedTestProcess();

        var result = new PowerShellScriptRunner(temp.AppRoot, new NullLog())
            .Run(new ScriptRequest { Body = ScriptStartingMarkedProcess(marked), Timeout = TimeSpan.FromSeconds(60) }, null, default);

        Assert.Equal(0, result.ExitCode); // 2 = proces testowy nie wystartował, więc test nic by nie udowodnił
        Assert.True(marked.WaitUntilGone(TimeSpan.FromSeconds(5)), "Proces uruchomiony ze skryptu przeżył koniec Run (Job Object go nie zabił).");
    }

    [Fact]
    public void Execute_ActionWithScript_ProcessStartedFromScriptDoesNotSurvive()
    {
        using var temp = new TempDirectory();
        using var marked = new MarkedTestProcess();
        var action = Item(TweakKind.Action) with { Script = new ScriptSet { Apply = ScriptStartingMarkedProcess(marked) } };

        var result = Engine(temp).Execute(action);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.True(marked.WaitUntilGone(TimeSpan.FromSeconds(5)), "Proces uruchomiony ze skryptu akcji przeżył Execute.");
    }

    [Fact]
    public void Execute_PanelWithLaunch_ProcessSurvivesExecute()
    {
        using var temp = new TempDirectory();
        using var marked = new MarkedTestProcess();
        var panel = Item(TweakKind.Panel) with { Launch = new LaunchSpec { File = "powershell.exe", Arguments = marked.Arguments } };

        var result = Engine(temp).Execute(panel);

        Assert.Equal(TweakOutcome.Executed, result.Outcome);
        Assert.Equal("Uruchomiono powershell.exe", result.Message);
        Assert.True(marked.WaitUntilRunning(TimeSpan.FromSeconds(15)), "Proces z launch nie pojawił się po Execute.");
        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
        Assert.NotEmpty(marked.FindProcessIds());
    }

    private sealed class NoRestorePoints : IRestorePointService
    {
        public RestorePointResult Create(string description) => new(RestorePointStatus.Failed, "wyłączone w teście");
    }

    private sealed class CurrentUser : IUserContext
    {
        public string Sid { get; } = WindowsIdentity.GetCurrent().User!.Value;

        public bool IsCertain => true;
    }
}
