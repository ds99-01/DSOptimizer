using System;
using System.Collections.Generic;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Errors;
using DSOptimizer.Windows.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Windows.Autostart;

/// <summary>Czytnik zadań startowych z atrapą uruchamiacza skryptów – prawdziwy skrypt AppX na hoście nie startuje.</summary>
[Trait("Category", "Windows")]
public class StartupTaskReaderTests
{
    private const string Sid = "S-1-5-21-0-0-0-1000";

    private const string TerminalJson =
        """[{"PackageName":"Microsoft.WindowsTerminal","PackageFullName":"Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe","PackageFamilyName":"Microsoft.WindowsTerminal_8wekyb3d8bbwe","InstallLocation":"C:\\Program Files\\WindowsApps\\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe","TaskId":"StartTerminalOnLoginTask","Enabled":"false","DisplayName":"ms-resource:AppName","PublisherDisplayName":"ms-resource:PublisherName","Executable":"WindowsTerminal.exe"},{"PackageFullName":"Zepsuty_1_x64__x","Error":"Unexpected end of file."}]""";

    private sealed class FakeRunner : IScriptRunner
    {
        public List<ScriptRequest> Requests { get; } = new();

        public Func<ScriptRequest, ScriptResult> Respond { get; set; } = _ => new ScriptResult { ExitCode = 0, Output = new[] { TerminalJson } };

        public ScriptResult Run(ScriptRequest request, IProgress<string>? output, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Respond(request);
        }
    }

    [Fact]
    public void Read_ScriptJson_ResolvesNames()
    {
        var runner = new FakeRunner();
        var sources = new List<string>();
        var reader = new StartupTaskReader(runner, source =>
        {
            sources.Add(source);
            return source.EndsWith("/Resources/AppName}", StringComparison.Ordinal) ? "Terminal" : "Microsoft Corporation";
        });

        var reading = reader.Read(Sid);

        var request = Assert.Single(runner.Requests);
        Assert.Equal(StartupTaskScript.Body, request.Body);
        Assert.Equal(Sid, request.UserSid);
        Assert.Equal(TimeSpan.FromSeconds(60), request.Timeout);
        var task = Assert.Single(reading.Tasks);
        Assert.Equal("Terminal", task.DisplayName);
        Assert.Equal("Microsoft Corporation", task.PublisherDisplayName);
        Assert.Equal("StartTerminalOnLoginTask", task.TaskId);
        Assert.False(task.EnabledInManifest);
        Assert.Equal("WindowsTerminal.exe", task.Executable);
        Assert.Contains(
            "@{Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe?ms-resource://Microsoft.WindowsTerminal/Resources/AppName}",
            sources);
        var warning = Assert.Single(reading.Warnings);
        Assert.Contains("Zepsuty_1_x64__x", warning, StringComparison.Ordinal);
        Assert.Contains("Unexpected end of file.", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_UnresolvedResource_FallsBackToPackageName()
    {
        var reader = new StartupTaskReader(new FakeRunner(), _ => null);

        var task = Assert.Single(reader.Read(Sid).Tasks);

        Assert.Equal("Microsoft.WindowsTerminal", task.DisplayName);
        Assert.Null(task.PublisherDisplayName);
    }

    [Fact]
    public void ScriptExitCode1_Throws()
    {
        var runner = new FakeRunner { Respond = _ => new ScriptResult { ExitCode = 1, Errors = new[] { "Get-AppxPackage: odmowa" } } };

        var ex = Assert.Throws<OperationException>(() => new StartupTaskReader(runner, _ => null).Read(Sid));

        Assert.Equal(OperationErrorKind.ScriptFailed, ex.Kind);
        Assert.Contains("Get-AppxPackage: odmowa", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptExitCode1_NoErrors_ThrowsWithCode()
    {
        var runner = new FakeRunner { Respond = _ => new ScriptResult { ExitCode = 7 } };

        var ex = Assert.Throws<OperationException>(() => new StartupTaskReader(runner, _ => null).Read(Sid));

        Assert.Contains("7", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nie json")]
    [InlineData(null)]
    public void BadJson_Throws(string? line)
    {
        var runner = new FakeRunner
        {
            Respond = _ => new ScriptResult { ExitCode = 0, Output = line is null ? Array.Empty<string>() : new[] { line } },
        };

        var ex = Assert.Throws<OperationException>(() => new StartupTaskReader(runner, _ => null).Read(Sid));

        Assert.Equal(OperationErrorKind.ScriptFailed, ex.Kind);
    }
}
