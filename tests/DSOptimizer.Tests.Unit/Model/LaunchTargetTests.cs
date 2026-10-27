using System;
using System.Collections.Generic;
using System.IO;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Model;

/// <summary>Review Focus 2: launch uruchamia wyłącznie pliki z System32 (i stałą ścieżkę powershell.exe) albo shell:::{GUID}.</summary>
public class LaunchTargetTests
{
    private const string System32 = @"C:\Windows\System32";
    private const string PowerShell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    private const string Explorer = @"C:\Windows\explorer.exe";
    private const string DevicesAndPrinters = "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}";

    public static IEnumerable<object[]> Allowed() => new[]
    {
        new object[] { "compmgmt.msc" },
        new object[] { "control.exe" },
        new object[] { "main.cpl" },
        new object[] { "SystemPropertiesPerformance.exe" },
        new object[] { "FIREWALL.CPL" },
        new object[] { "WF.MsC" },
        new object[] { "msinfo32.exe" },
        new object[] { "powershell.exe" },
        new object[] { DevicesAndPrinters },
        new object[] { "shell:::{a8a91a66-3a7d-4424-8d24-04e180695c7a}" },
    };

    public static IEnumerable<object[]> Forbidden() => new[]
    {
        new object[] { string.Empty },
        new object[] { " " },
        new object[] { @"C:\Windows\System32\control.exe" },
        new object[] { @"System32\control.exe" },
        new object[] { "System32/control.exe" },
        new object[] { @".\control.exe" },
        new object[] { "./control.exe" },
        new object[] { @"..\control.exe" },
        new object[] { "../control.exe" },
        new object[] { ".." },
        new object[] { "." },
        new object[] { @"E:\evil.exe" },
        new object[] { "E:evil.exe" },
        new object[] { @"\\serwer\udzial\evil.exe" },
        new object[] { @"\\?\E:\evil.exe" },
        new object[] { "control" },
        new object[] { "explorer.exe" },
        new object[] { "EXPLORER.EXE" },
        new object[] { "Explorer.Exe" },
        new object[] { "control.bat" },
        new object[] { "control.cmd" },
        new object[] { "control.com" },
        new object[] { "control.ps1" },
        new object[] { "control.lnk" },
        new object[] { "control.scr" },
        new object[] { "control.vbs" },
        new object[] { "control.dll" },
        new object[] { "control.exe.lnk" },
        new object[] { "control..exe" },
        new object[] { "control.exe." },
        new object[] { "control.exe " },
        new object[] { " control.exe" },
        new object[] { "con trol.exe" },
        new object[] { "control.exe\n" },
        new object[] { "control.exe\0" },
        new object[] { "\"control.exe\"" },
        new object[] { "'control.exe'" },
        new object[] { "*.exe" },
        new object[] { "contr?l.exe" },
        new object[] { "control.exe:strumien" },
        new object[] { "control.exe::$DATA" },
        new object[] { "-control.exe" },
        new object[] { "contról.exe" },
        new object[] { "\u212Aontrol.exe" },
        new object[] { "ｃontrol.exe" },
        new object[] { "control.exe;evil.exe" },
        new object[] { "control.exe evil.exe" },
        new object[] { "ms-settings:display" },
        new object[] { "http://example.com/evil.exe" },
        new object[] { "file:///E:/evil.exe" },
        new object[] { "shell:Downloads" },
        new object[] { "shell:::" },
        new object[] { "shell:::A8A91A66-3A7D-4424-8D24-04E180695C7A" },
        new object[] { "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7}" },
        new object[] { "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7AA}" },
        new object[] { "shell:::{G8A91A66-3A7D-4424-8D24-04E180695C7A}" },
        new object[] { DevicesAndPrinters + "x" },
        new object[] { DevicesAndPrinters + "\n" },
        new object[] { DevicesAndPrinters + @"\..\evil.exe" },
        new object[] { "SHELL:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}" },
        new object[] { " " + DevicesAndPrinters },
        new object[] { "shell::{A8A91A66-3A7D-4424-8D24-04E180695C7A}" },
        new object[] { "::{A8A91A66-3A7D-4424-8D24-04E180695C7A}" },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void IsAllowed_SystemFileNameOrShellGuid(string file)
        => Assert.True(LaunchTarget.IsAllowed(file));

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void IsAllowed_RejectsPathsSeparatorsSchemesAndOtherExtensions(string file)
        => Assert.False(LaunchTarget.IsAllowed(file));

    [Fact]
    public void IsAllowed_Null_IsFalse()
        => Assert.False(LaunchTarget.IsAllowed(null));

    [Theory]
    [InlineData("compmgmt.msc")]
    [InlineData("SystemPropertiesPerformance.exe")]
    [InlineData("main.cpl")]
    public void Resolve_FileName_IsAlwaysInSystemDirectory(string file)
    {
        var asked = new List<string>();

        var resolved = LaunchTarget.Resolve(file, System32, PowerShell, Explorer, path => { asked.Add(path); return true; });

        Assert.Equal(Path.Combine(System32, file), resolved);
        Assert.Equal(new[] { resolved }, asked);
    }

    [Theory]
    [InlineData("powershell.exe")]
    [InlineData("PowerShell.EXE")]
    public void Resolve_PowerShell_UsesFixedPowerShellPath(string file)
    {
        var asked = new List<string>();

        Assert.Equal(PowerShell, LaunchTarget.Resolve(file, System32, PowerShell, Explorer, path => { asked.Add(path); return true; }));
        Assert.Equal(new[] { PowerShell }, asked);
    }

    [Fact]
    public void Resolve_ShellGuid_IsFixedExplorerPathFromWindowsDirectory()
    {
        var asked = new List<string>();

        Assert.Equal(Explorer, LaunchTarget.Resolve(DevicesAndPrinters, System32, PowerShell, Explorer, path => { asked.Add(path); return true; }));
        Assert.Equal(new[] { Explorer }, asked);
    }

    [Fact]
    public void Resolve_ShellGuid_MissingExplorer_ThrowsNotFound()
    {
        var ex = Assert.Throws<OperationException>(() => LaunchTarget.Resolve(DevicesAndPrinters, System32, PowerShell, Explorer, _ => false));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
        Assert.Contains(Explorer, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("explorer.exe")]
    [InlineData(@"Windows\explorer.exe")]
    [InlineData(@"\serwer\udzial\explorer.exe")]
    public void Resolve_RelativeExplorerPath_ThrowsWithoutLookingAtDisk(string explorerPath)
    {
        var asked = new List<string>();

        Assert.Throws<OperationException>(() => LaunchTarget.Resolve(DevicesAndPrinters, System32, PowerShell, explorerPath, path => { asked.Add(path); return true; }));
        Assert.Throws<OperationException>(() => LaunchTarget.Resolve("control.exe", System32, PowerShell, explorerPath, path => { asked.Add(path); return true; }));
        Assert.Empty(asked);
    }

    [Fact]
    public void Resolve_MissingFile_ThrowsNotFoundWithPath()
    {
        var ex = Assert.Throws<OperationException>(() => LaunchTarget.Resolve("brak-dso.exe", System32, PowerShell, Explorer, _ => false));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
        Assert.Contains(Path.Combine(System32, "brak-dso.exe"), ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void Resolve_ForbiddenFile_ThrowsWithoutLookingAtDisk(string file)
    {
        var asked = new List<string>();

        var ex = Assert.Throws<OperationException>(() => LaunchTarget.Resolve(file, System32, PowerShell, Explorer, path => { asked.Add(path); return true; }));

        Assert.Equal(OperationErrorKind.Unexpected, ex.Kind);
        Assert.Empty(asked);
    }

    [Theory]
    [InlineData("", PowerShell)]
    [InlineData(@"Windows\System32", PowerShell)]
    [InlineData(@"\Windows\System32", PowerShell)]
    [InlineData("C:", PowerShell)]
    [InlineData(System32, "")]
    [InlineData(System32, "powershell.exe")]
    [InlineData(System32, @"WindowsPowerShell\v1.0\powershell.exe")]
    public void Resolve_RelativeSystemOrPowerShellPath_Throws(string systemDirectory, string powerShellPath)
    {
        var asked = new List<string>();

        Assert.Throws<OperationException>(() => LaunchTarget.Resolve("control.exe", systemDirectory, powerShellPath, Explorer, path => { asked.Add(path); return true; }));
        Assert.Throws<OperationException>(() => LaunchTarget.Resolve("powershell.exe", systemDirectory, powerShellPath, Explorer, path => { asked.Add(path); return true; }));
        Assert.Empty(asked);
    }

    private static LaunchCommand Command(string file, string arguments, List<string> asked, Func<string, bool>? exists = null)
        => LaunchTarget.ToCommand(
            new LaunchSpec { File = file, Arguments = arguments },
            System32,
            PowerShell,
            Explorer,
            path => { asked.Add(path); return exists?.Invoke(path) ?? true; });

    [Theory]
    [InlineData("control.exe", "/name Microsoft.PowerOptions")]
    [InlineData("SystemPropertiesPerformance.exe", "")]
    public void ToCommand_Exe_CreateProcessOfSystem32Path(string file, string arguments)
    {
        var asked = new List<string>();

        var command = Command(file, arguments, asked);

        Assert.Equal(Path.Combine(System32, file), command.FileName);
        Assert.Equal(arguments, command.Arguments);
        Assert.Equal(new[] { Path.Combine(System32, file) }, asked);
    }

    [Fact]
    public void ToCommand_PowerShell_CreateProcessOfFixedPath()
    {
        var command = Command("powershell.exe", "-NoProfile", new List<string>());

        Assert.Equal(PowerShell, command.FileName);
        Assert.Equal("-NoProfile", command.Arguments);
    }

    [Theory]
    [InlineData("compmgmt.msc", "", "")]
    [InlineData("WF.MsC", "/s", " /s")]
    public void ToCommand_Msc_MmcFromSystem32WithQuotedFullPath_NoShellAssociation(string file, string arguments, string suffix)
    {
        var asked = new List<string>();

        var command = Command(file, arguments, asked);

        Assert.Equal(System32 + @"\mmc.exe", command.FileName);
        Assert.Equal("\"" + Path.Combine(System32, file) + "\"" + suffix, command.Arguments);
        Assert.Equal(new[] { Path.Combine(System32, file), System32 + @"\mmc.exe" }, asked);
    }

    [Theory]
    [InlineData("main.cpl", "", "")]
    [InlineData("FIREWALL.CPL", "@1", " @1")]
    public void ToCommand_Cpl_ControlFromSystem32WithQuotedFullPath_NoShellAssociation(string file, string arguments, string suffix)
    {
        var asked = new List<string>();

        var command = Command(file, arguments, asked);

        Assert.Equal(System32 + @"\control.exe", command.FileName);
        Assert.Equal("\"" + Path.Combine(System32, file) + "\"" + suffix, command.Arguments);
        Assert.Equal(new[] { Path.Combine(System32, file), System32 + @"\control.exe" }, asked);
    }

    [Fact]
    public void ToCommand_ShellGuid_IsCreateProcessOfExplorerWithNamespaceArgument()
    {
        var asked = new List<string>();

        var command = Command(DevicesAndPrinters, string.Empty, asked);

        Assert.Equal(Explorer, command.FileName);
        Assert.Equal(DevicesAndPrinters, command.Arguments);
        Assert.True(command.RequiresUserShell);
        Assert.Equal(new[] { Explorer }, asked);
    }

    [Fact]
    public void ToCommand_ShellGuid_AppendsCatalogArgumentsAfterNamespace()
    {
        var command = Command(DevicesAndPrinters, "/select", new List<string>());

        Assert.Equal(DevicesAndPrinters + " /select", command.Arguments);
    }

    [Theory]
    [InlineData("control.exe")]
    [InlineData("compmgmt.msc")]
    [InlineData("main.cpl")]
    [InlineData("powershell.exe")]
    public void ToCommand_NonShellFiles_DoNotRequireUserShell(string file)
        => Assert.False(Command(file, string.Empty, new List<string>()).RequiresUserShell);

    [Theory]
    [InlineData("compmgmt.msc", "mmc.exe")]
    [InlineData("main.cpl", "control.exe")]
    public void ToCommand_MissingHost_ThrowsNotFound(string file, string host)
    {
        var ex = Assert.Throws<OperationException>(() => Command(file, string.Empty, new List<string>(), path => !path.EndsWith(@"\" + host, StringComparison.Ordinal)));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
        Assert.Contains(System32 + @"\" + host, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("compmgmt.msc")]
    [InlineData("main.cpl")]
    [InlineData("control.exe")]
    public void ToCommand_MissingFile_ThrowsNotFoundBeforeLookingForHost(string file)
    {
        var asked = new List<string>();

        var ex = Assert.Throws<OperationException>(() => Command(file, string.Empty, asked, _ => false));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
        Assert.Equal(new[] { Path.Combine(System32, file) }, asked);
    }

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void ToCommand_ForbiddenFile_ThrowsWithoutLookingAtDisk(string file)
    {
        var asked = new List<string>();

        var ex = Assert.Throws<OperationException>(() => Command(file, string.Empty, asked));

        Assert.Equal(OperationErrorKind.Unexpected, ex.Kind);
        Assert.Empty(asked);
    }
}
