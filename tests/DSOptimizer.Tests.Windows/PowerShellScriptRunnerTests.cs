using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class PowerShellScriptRunnerTests
{
    private static ScriptResult Run(TempDirectory temp, ScriptRequest request, IProgress<string>? progress = null, CancellationToken token = default)
        => new PowerShellScriptRunner(temp.AppRoot, new NullLog()).Run(request, progress, token);

    [Fact]
    public void Run_ReturnsStdoutLinesAndZeroExit()
    {
        using var temp = new TempDirectory();
        var result = Run(temp, new ScriptRequest { Body = "Write-Output 'a'\nWrite-Output 'b'" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new[] { "a", "b" }, result.Output);
    }

    [Fact]
    public void Run_PolishCharactersRoundTrip()
    {
        using var temp = new TempDirectory();
        var result = Run(temp, new ScriptRequest { Body = "Write-Output 'Zażółć gęślą jaźń'\nWrite-Output $Snapshot", Snapshot = "żółw" });

        Assert.Equal(new[] { "Zażółć gęślą jaźń", "żółw" }, result.Output);
    }

    [Fact]
    public void Run_PassesUserSid()
    {
        using var temp = new TempDirectory();
        Assert.Equal("S-1-5-21-1", Run(temp, new ScriptRequest { Body = "Write-Output $UserSid", UserSid = "S-1-5-21-1" }).LastLine);
    }

    [Fact]
    public void Run_Uses64BitPowerShell()
    {
        using var temp = new TempDirectory();
        Assert.Equal("True", Run(temp, new ScriptRequest { Body = "[Environment]::Is64BitProcess" }).LastLine);
    }

    [Fact]
    public void Run_ThrowingScript_ExitsOneWithStderr()
    {
        using var temp = new TempDirectory();
        var result = Run(temp, new ScriptRequest { Body = "throw 'boom'" });

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Errors, line => line.Contains("boom"));
    }

    [Fact]
    public void Run_NonTerminatingErrorBecomesFailure()
    {
        using var temp = new TempDirectory();
        Assert.Equal(1, Run(temp, new ScriptRequest { Body = @"Get-Item 'C:\nie-istnieje-dso-12345'" }).ExitCode);
    }

    [Fact]
    public void Run_ExitCodes_ArePropagated()
    {
        using var temp = new TempDirectory();
        Assert.Equal(7, Run(temp, new ScriptRequest { Body = "exit 7" }).ExitCode);
        Assert.Equal(3, Run(temp, new ScriptRequest { Body = "cmd.exe /c exit 3" }).ExitCode);
    }

    [Fact]
    public void Run_Timeout_KillsWholeProcessTree()
    {
        using var temp = new TempDirectory();
        var progress = new SyncProgress();
        const string body = "$child = Start-Process -FilePath ping.exe -ArgumentList '-n','120','127.0.0.1' -WindowStyle Hidden -PassThru\nWrite-Output $child.Id\nStart-Sleep -Seconds 120";

        var ex = Assert.Throws<OperationException>(() => Run(temp, new ScriptRequest { Body = body, Timeout = TimeSpan.FromSeconds(8) }, progress));

        Assert.Equal(OperationErrorKind.Timeout, ex.Kind);
        var childId = int.Parse(progress.Items.First(line => !line.StartsWith("[stderr]", StringComparison.Ordinal)), CultureInfo.InvariantCulture);
        Assert.True(WaitForExit(childId, TimeSpan.FromSeconds(5)), "Proces potomny (ping) przeżył timeout.");
    }

    [Fact]
    public void Run_Cancellation_StopsScript()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();

        var ex = Assert.Throws<OperationException>(() => Run(temp, new ScriptRequest { Body = "Start-Sleep -Seconds 60", Timeout = null }, token: cts.Token));

        Assert.Equal(OperationErrorKind.Cancelled, ex.Kind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Run_AsFirstComponent_CreatesTrustedParentDirectory()
    {
        using var temp = new TempDirectory();
        Run(temp, new ScriptRequest { Body = "Write-Output 'x'" });

        Assert.True(SecureDirectory.IsTrustedDirectory(temp.Sub("app")));
        Assert.True(SecureDirectory.IsTrustedDirectory(temp.AppRoot.RunDirectory));
    }

    [Fact]
    public void Run_DeletesScriptFileAfterwards()
    {
        using var temp = new TempDirectory();
        Run(temp, new ScriptRequest { Body = "Write-Output 'x'" });

        Assert.Empty(Directory.GetFiles(temp.AppRoot.RunDirectory, "*.ps1"));
    }

    [Fact]
    public void Run_ReportsOutputLinesToProgress()
    {
        using var temp = new TempDirectory();
        var progress = new SyncProgress();
        Run(temp, new ScriptRequest { Body = "Write-Output 'linia'" }, progress);

        Assert.Contains("linia", progress.Items);
    }

    [Fact]
    public void Run_OutputBeginsWithoutByteOrderMark()
    {
        using var temp = new TempDirectory();
        var result = Run(temp, new ScriptRequest { Body = "Write-Output 'Applied'" });

        Assert.Equal(new[] { "Applied" }, result.Output);
        Assert.Equal("Applied", result.LastLine);
    }

    [Fact]
    public void Run_ReadsSnapshotAndSidFromEnvironment()
    {
        using var temp = new TempDirectory();
        var result = Run(temp, new ScriptRequest
        {
            Body = "Write-Output $env:DSO_USER_SID\nWrite-Output $env:DSO_SNAPSHOT",
            UserSid = "S-1-5-21-9",
            Snapshot = "snap",
        });

        Assert.Equal(new[] { "S-1-5-21-9", "snap" }, result.Output);
    }

    [Fact]
    public async System.Threading.Tasks.Task Run_ScriptFileIsLockedAndAdminOnlyWhileRunning()
    {
        using var temp = new TempDirectory();
        var runDirectory = temp.AppRoot.RunDirectory;
        var task = System.Threading.Tasks.Task.Run(() => Run(temp, new ScriptRequest { Body = "Start-Sleep -Seconds 8" }));

        string? scriptPath = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (scriptPath is null && DateTime.UtcNow < deadline)
        {
            scriptPath = Directory.Exists(runDirectory) ? Directory.GetFiles(runDirectory, "*.ps1").FirstOrDefault() : null;
            await System.Threading.Tasks.Task.Delay(50);
        }

        Assert.NotNull(scriptPath);
        Assert.Throws<IOException>(() => File.Delete(scriptPath!));
        Assert.Throws<IOException>(() => File.Move(scriptPath!, scriptPath + ".moved"));
        Assert.Throws<IOException>(() => File.WriteAllText(scriptPath!, "evil"));

        var security = File.GetAccessControl(scriptPath!, AccessControlSections.Owner | AccessControlSections.Access);
        Assert.True(security.AreAccessRulesProtected);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        Assert.Equal(admins, security.GetOwner(typeof(SecurityIdentifier)));
        var sids = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .Distinct()
            .ToArray();
        Assert.Equal(2, sids.Length);
        Assert.Contains(admins, sids);
        Assert.Contains(system, sids);

        Assert.Equal(0, (await task).ExitCode);
        Assert.Empty(Directory.GetFiles(runDirectory, "*.ps1"));
    }
    [Fact]
    public void Run_LongLivedChildInheritingStdout_DoesNotBlockAfterMainProcessExits()
    {
        using var temp = new TempDirectory();
        var progress = new SyncProgress();
        const string body = "$child = Start-Process -FilePath \"$env:SystemRoot\\System32\\PING.EXE\" -ArgumentList '-n','30','127.0.0.1' -NoNewWindow -PassThru\nWrite-Output $child.Id\nexit 0";
        var stopwatch = Stopwatch.StartNew();

        var result = Run(temp, new ScriptRequest { Body = body, Timeout = TimeSpan.FromSeconds(60) }, progress);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Run trwał {stopwatch.Elapsed.TotalSeconds:0.0} s - czekał na potomka.");
        Assert.Equal(0, result.ExitCode);
        var childId = progress.Items.Select(line => int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).First(id => id > 0);
        Assert.True(WaitForExit(childId, TimeSpan.FromSeconds(5)), "Proces potomny (ping) przeżył zakończenie skryptu.");
    }
    private static bool WaitForExit(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            Thread.Sleep(200);
        }

        return false;
    }
}
