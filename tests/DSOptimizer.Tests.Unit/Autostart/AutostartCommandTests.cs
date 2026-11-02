using System;
using System.Collections.Generic;
using DSOptimizer.Core.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartCommandTests
{
    private static readonly UserEnvironment Environment = UserEnvironment.FromVariables(new Dictionary<string, string>
    {
        ["SystemRoot"] = @"C:\Windows",
        ["windir"] = @"C:\Windows",
        ["APPDATA"] = @"C:\Users\dso\AppData\Roaming",
    });

    private readonly List<string> _probed = new();

    private Func<string, FileProbe> Probe(params string[] existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return path =>
        {
            _probed.Add(path);
            return set.Contains(path) ? FileProbe.Exists : FileProbe.Missing;
        };
    }

    [Fact]
    public void Quoted_PathAndArguments()
    {
        const string Steam = @"C:\Program Files (x86)\Steam\steam.exe";

        var target = AutostartCommand.Parse("\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", Environment, Probe(Steam));

        Assert.Equal(new CommandTarget(Steam, FileProbe.Exists, Definite: true, IsInterpreter: false, "-silent"), target);
    }

    [Fact]
    public void Quoted_Missing_DefiniteNoArguments()
    {
        var target = AutostartCommand.Parse("  \"C:\\dsotest\\Brak.exe\"  ", Environment, Probe());

        Assert.Equal(new CommandTarget(@"C:\dsotest\Brak.exe", FileProbe.Missing, Definite: true, IsInterpreter: false, null), target);
    }

    [Fact]
    public void Quoted_Unclosed_RestIsFile()
    {
        var target = AutostartCommand.Parse("\"C:\\a b\\x.exe", Environment, Probe(@"C:\a b\x.exe"));

        Assert.Equal(@"C:\a b\x.exe", target.FilePath);
        Assert.Equal(FileProbe.Exists, target.Probe);
        Assert.Null(target.Arguments);
    }

    [Fact]
    public void UnquotedSingleToken_MissingDefinite()
    {
        var target = AutostartCommand.Parse(@"C:\dsotest\DsoMissing.exe", Environment, Probe());

        Assert.Equal(new CommandTarget(@"C:\dsotest\DsoMissing.exe", FileProbe.Missing, Definite: true, IsInterpreter: false, null), target);
    }

    [Fact]
    public void Unquoted_ProgramFilesWithSpaces_ResolvesExisting()
    {
        const string Java = @"C:\Program Files (x86)\Common Files\Java\Java Update\jusched.exe";

        var target = AutostartCommand.Parse(Java, Environment, Probe(Java));

        Assert.Equal(new CommandTarget(Java, FileProbe.Exists, Definite: true, IsInterpreter: false, null), target);
        Assert.Equal(@"C:\Program", _probed[0]);
        Assert.Equal(@"C:\Program.exe", _probed[1]);
    }

    [Fact]
    public void Unquoted_WithSpaces_ArgumentsAfterResolvedPrefix()
    {
        const string App = @"C:\Program Files\App\app.exe";

        var target = AutostartCommand.Parse(App + "  --tray  -x", Environment, Probe(App));

        Assert.Equal(App, target.FilePath);
        Assert.Equal("--tray  -x", target.Arguments);
        Assert.True(target.Definite);
    }

    [Fact]
    public void Unquoted_WithSpaces_NoCandidate_NotDefinite()
    {
        var target = AutostartCommand.Parse(@"C:\Program Files (x86)\Common Files\Java\Java Update\jusched.exe", Environment, Probe());

        Assert.Equal(new CommandTarget(null, FileProbe.Unknown, Definite: false, IsInterpreter: false, null), target);
    }

    [Fact]
    public void Unquoted_CandidateWithoutExtension_AddsExe()
    {
        var target = AutostartCommand.Parse(@"C:\Tools\app -x", Environment, Probe(@"C:\Tools\app.exe"));

        Assert.Equal(new CommandTarget(@"C:\Tools\app.exe", FileProbe.Exists, Definite: true, IsInterpreter: false, "-x"), target);
    }

    [Fact]
    public void ExpandsUserVariables()
    {
        var target = AutostartCommand.Parse(@"%windir%\system32\SecurityHealthSystray.exe", Environment, Probe(@"C:\Windows\system32\SecurityHealthSystray.exe"));

        Assert.Equal(new CommandTarget(@"C:\Windows\system32\SecurityHealthSystray.exe", FileProbe.Exists, Definite: true, IsInterpreter: false, null), target);
    }

    [Fact]
    public void ExpandsVariablesInQuotedPath()
    {
        var target = AutostartCommand.Parse("\"%APPDATA%\\Spotify\\Spotify.exe\" --minimized", Environment, Probe());

        Assert.Equal(new CommandTarget(@"C:\Users\dso\AppData\Roaming\Spotify\Spotify.exe", FileProbe.Missing, Definite: true, IsInterpreter: false, "--minimized"), target);
    }

    [Theory]
    [InlineData(@"%NOPE%\app.exe")]
    [InlineData("\"%NOPE%\\app.exe\" -x")]
    public void UnknownVariable_NotDefinite(string command)
    {
        var target = AutostartCommand.Parse(command, Environment, Probe(@"%NOPE%\app.exe"));

        Assert.False(target.Definite);
        Assert.Equal(FileProbe.Unknown, target.Probe);
        Assert.Equal(@"%NOPE%\app.exe", target.FilePath);
        Assert.Empty(_probed);
    }

    [Fact]
    public void UnknownVariable_WithSpaces_NoCandidate()
    {
        var target = AutostartCommand.Parse(@"%NOPE%\a b.exe -x", Environment, Probe());

        Assert.Equal(new CommandTarget(null, FileProbe.Unknown, Definite: false, IsInterpreter: false, null), target);
        Assert.Empty(_probed);
    }

    [Theory]
    [InlineData(@"tools\app.exe")]
    [InlineData(@"C:app.exe")]
    [InlineData(@"\app.exe")]
    public void RelativePath_NotDefinite(string command)
    {
        var target = AutostartCommand.Parse(command, Environment, Probe(command));

        Assert.Equal(new CommandTarget(command, FileProbe.Unknown, Definite: false, IsInterpreter: false, null), target);
        Assert.Empty(_probed);
    }

    [Theory]
    [InlineData("rundll32.exe x.dll,Entry", @"C:\Windows\System32\rundll32.exe", "x.dll,Entry")]
    [InlineData("RUNDLL32 x.dll,Entry", @"C:\Windows\System32\RUNDLL32.exe", "x.dll,Entry")]
    [InlineData("cmd /c start x", @"C:\Windows\System32\cmd.exe", "/c start x")]
    [InlineData("mshta.exe", @"C:\Windows\System32\mshta.exe", null)]
    [InlineData("\"wscript.exe\" a.vbs", @"C:\Windows\System32\wscript.exe", "a.vbs")]
    public void BareInterpreter_System32(string command, string file, string? arguments)
    {
        var target = AutostartCommand.Parse(command, Environment, Probe());

        Assert.Equal(new CommandTarget(file, FileProbe.Exists, Definite: true, IsInterpreter: true, arguments), target);
        Assert.Empty(_probed);
    }

    [Fact]
    public void BareInterpreter_PowerShell_WindowsPowerShellFolder()
    {
        var target = AutostartCommand.Parse("powershell.exe -NoProfile -File x.ps1", Environment, Probe());

        Assert.Equal(
            new CommandTarget(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", FileProbe.Exists, Definite: true, IsInterpreter: true, "-NoProfile -File x.ps1"),
            target);
    }

    [Fact]
    public void BareInterpreter_Pwsh_NotDefinite()
    {
        var target = AutostartCommand.Parse("pwsh.exe -File x.ps1", Environment, Probe());

        Assert.Equal(new CommandTarget("pwsh.exe", FileProbe.Unknown, Definite: false, IsInterpreter: true, "-File x.ps1"), target);
    }

    [Fact]
    public void BareInterpreter_NoSystemRoot_NotDefinite()
    {
        var target = AutostartCommand.Parse("cmd.exe /c x", UserEnvironment.FromVariables(new Dictionary<string, string>()), Probe());

        Assert.Equal(new CommandTarget("cmd.exe", FileProbe.Unknown, Definite: false, IsInterpreter: true, "/c x"), target);
    }

    [Fact]
    public void BareOther_NotDefinite()
    {
        var target = AutostartCommand.Parse("notepad.exe", Environment, Probe("notepad.exe"));

        Assert.Equal(new CommandTarget("notepad.exe", FileProbe.Unknown, Definite: false, IsInterpreter: false, null), target);
        Assert.Empty(_probed);
    }

    [Fact]
    public void QuotedInterpreterPath_IsInterpreter_Probed()
    {
        var target = AutostartCommand.Parse("\"C:\\Windows\\System32\\cmd.exe\" /c x", Environment, Probe(@"C:\Windows\System32\cmd.exe"));

        Assert.Equal(new CommandTarget(@"C:\Windows\System32\cmd.exe", FileProbe.Exists, Definite: true, IsInterpreter: true, "/c x"), target);
        Assert.Single(_probed);
    }

    [Fact]
    public void UncProbeUnknown_NotMissing()
    {
        var target = AutostartCommand.Parse(@"\\srv\share\app.exe", Environment, _ => FileProbe.Unknown);

        Assert.Equal(@"\\srv\share\app.exe", target.FilePath);
        Assert.Equal(FileProbe.Unknown, target.Probe);
        Assert.NotEqual(FileProbe.Missing, target.Probe);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\" -x")]
    public void Empty_Unknown(string? command)
    {
        var target = AutostartCommand.Parse(command, Environment, Probe());

        Assert.Null(target.FilePath);
        Assert.Equal(FileProbe.Unknown, target.Probe);
        Assert.False(target.Definite);
        Assert.False(target.IsInterpreter);
    }

    [Fact]
    public void Shortcut_Advertised_NotDefinite()
    {
        var target = AutostartCommand.FromShortcut(@"C:\Windows\Installer\{1}\app.exe", "-x", advertised: true, Environment, Probe(@"C:\Windows\Installer\{1}\app.exe"));

        Assert.Equal(new CommandTarget(null, FileProbe.Unknown, Definite: false, IsInterpreter: false, "-x"), target);
        Assert.Empty(_probed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Shortcut_EmptyTarget_NotDefinite(string? rawTarget)
    {
        var target = AutostartCommand.FromShortcut(rawTarget, null, advertised: false, Environment, Probe());

        Assert.Equal(new CommandTarget(null, FileProbe.Unknown, Definite: false, IsInterpreter: false, null), target);
    }

    [Fact]
    public void Shortcut_ExplicitTarget_Expanded()
    {
        var target = AutostartCommand.FromShortcut(@"%SystemRoot%\System32\notepad.exe", "  a.txt ", advertised: false, Environment, Probe(@"C:\Windows\System32\notepad.exe"));

        Assert.Equal(new CommandTarget(@"C:\Windows\System32\notepad.exe", FileProbe.Exists, Definite: true, IsInterpreter: false, "a.txt"), target);
    }

    [Fact]
    public void Shortcut_Interpreter_UnknownVariable_Relative()
    {
        Assert.True(AutostartCommand.FromShortcut(@"C:\Windows\System32\wscript.exe", "a.vbs", false, Environment, Probe()).IsInterpreter);
        _probed.Clear();

        var unknown = AutostartCommand.FromShortcut(@"%NOPE%\x.exe", null, false, Environment, Probe());
        Assert.Equal(new CommandTarget(@"%NOPE%\x.exe", FileProbe.Unknown, Definite: false, IsInterpreter: false, null), unknown);

        var relative = AutostartCommand.FromShortcut("x.exe", null, false, Environment, Probe());
        Assert.Equal(new CommandTarget("x.exe", FileProbe.Unknown, Definite: false, IsInterpreter: false, null), relative);
        Assert.Empty(_probed);
    }

    [Fact]
    public void Interpreters_List()
        => Assert.Equal(
            new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe" },
            AutostartCommand.Interpreters);

    [Fact]
    public void NormalizeCommandLine_StartupInfoForm_EqualsRunForm()
    {
        Assert.Equal(
            AutostartCommand.NormalizeCommandLine(@"C:\dsotest\DsoRunUser.exe", Environment),
            AutostartCommand.NormalizeCommandLine("\"C:\\dsotest\\DsoRunUser.exe\" ", Environment));
        Assert.Equal(@"c:\dsotest\dsorunuser.exe", AutostartCommand.NormalizeCommandLine("\"C:\\dsotest\\DsoRunUser.exe\" ", Environment));
    }

    [Fact]
    public void NormalizeCommandLine_VariablesWhitespaceCase()
    {
        Assert.Equal(
            "c:\\windows\\system32\\rundll32.exe x.dll,entry \"a b\"",
            AutostartCommand.NormalizeCommandLine("  \"%windir%\\System32\\rundll32.exe\"   x.dll,Entry\t \"a b\"  ", Environment));
        Assert.Equal(string.Empty, AutostartCommand.NormalizeCommandLine(null, Environment));
        Assert.Equal(string.Empty, AutostartCommand.NormalizeCommandLine("   ", Environment));
    }
}
