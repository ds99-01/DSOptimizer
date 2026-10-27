using System;
using DSOptimizer.Core.Scripts;
using Xunit;

namespace DSOptimizer.Tests.Unit.Scripts;

public class ScriptWrapperTests
{
    private static string[] Lines(string body) => ScriptWrapper.Wrap(body).Split(new[] { "\r\n" }, StringSplitOptions.None);

    [Fact]
    public void Wrap_DeclaresParametersFromEnvironment()
        => Assert.Equal("param([string]$UserSid = $env:DSO_USER_SID, [string]$Snapshot = $env:DSO_SNAPSHOT)", Lines("x")[0]);

    [Fact]
    public void Wrap_MakesErrorsTerminatingAndOutputUtf8()
    {
        var lines = Lines("x");
        Assert.Contains("$ErrorActionPreference = 'Stop'", lines);
        Assert.Contains("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8", lines);
        Assert.Contains("trap { [Console]::Error.WriteLine($_.ToString()); exit 1 }", lines);
    }

    [Fact]
    public void Wrap_PutsBodyAfterTrapAndBeforeExit()
    {
        var lines = Lines("Write-Output 'x'");
        var trap = Array.FindIndex(lines, line => line.StartsWith("trap", StringComparison.Ordinal));
        var body = Array.IndexOf(lines, "Write-Output 'x'");
        Assert.True(body > trap);
        Assert.Equal("if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }", lines[lines.Length - 2]);
        Assert.Equal("exit 0", lines[lines.Length - 1]);
    }

    [Fact]
    public void Wrap_NullBody_Throws()
        => Assert.Throws<ArgumentNullException>(() => ScriptWrapper.Wrap(null!));
}
