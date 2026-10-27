using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Review Focus 2 na prawdziwym systemie plików, ale bez uruchamiania czegokolwiek: start procesu jest podmieniony,
/// sprawdzamy tylko, co trafiłoby do <see cref="NativeProcessStarter.Start"/> (CreateProcess pełnej ścieżki z System32 dla exe/msc/cpl,
/// <c>%SystemRoot%\explorer.exe shell:::{GUID}</c> za strażnikiem powłoki; katalog roboczy, argumenty). ShellExecute nie istnieje.
/// </summary>
[Trait("Category", "Windows")]
public class ShellProcessLauncherTests
{
    private const string DevicesAndPrinters = "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}";

    private const int Session = 1;
    private const string Me = "S-1-5-21-1-2-3-1000";
    private const string Other = "S-1-5-21-1-2-3-500";

    private readonly List<StartedProcess> _started = new();
    private readonly FakeExplorerQuery _explorers = new();

    private ShellProcessLauncher Launcher(Exception? failure = null)
        => new(
            (fileName, arguments, workingDirectory) =>
            {
                _started.Add(new StartedProcess(fileName, arguments, workingDirectory));
                if (failure is not null)
                {
                    throw failure;
                }
            },
            _explorers,
            Session,
            Me);

    private sealed class FakeExplorerQuery : IExplorerProcessQuery
    {
        public List<ExplorerProcessInfo> Processes { get; } = new();

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public IReadOnlyList<ExplorerProcessInfo> GetExplorerProcesses()
        {
            Calls++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Processes;
        }
    }

    private static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static string System32(string name) => Path.Combine(Environment.SystemDirectory, name);

    [Theory]
    [InlineData("control.exe")]
    [InlineData("SystemPropertiesPerformance.exe")]
    public void Launch_Exe_StartsFullSystem32PathThroughCreateProcess(string file)
    {
        Launcher().Launch(new LaunchSpec { File = file, Arguments = "/x y" });

        var info = Assert.Single(_started);
        Assert.Equal(System32(file), info.FileName);
        Assert.Equal("/x y", info.Arguments);
        Assert.Equal(Environment.SystemDirectory, info.WorkingDirectory);
    }

    [Theory]
    [InlineData("compmgmt.msc", "", "")]
    [InlineData("compmgmt.msc", "/s", " /s")]
    public void Launch_Msc_StartsSystem32MmcWithQuotedFullPath(string file, string arguments, string suffix)
    {
        Launcher().Launch(new LaunchSpec { File = file, Arguments = arguments });

        var info = Assert.Single(_started);
        Assert.Equal(System32("mmc.exe"), info.FileName);
        Assert.Equal("\"" + System32(file) + "\"" + suffix, info.Arguments);
        Assert.Equal(Environment.SystemDirectory, info.WorkingDirectory);
    }

    [Theory]
    [InlineData("main.cpl", "", "")]
    [InlineData("TIMEDATE.CPL", "@1", " @1")]
    public void Launch_Cpl_StartsSystem32ControlWithQuotedFullPath(string file, string arguments, string suffix)
    {
        Launcher().Launch(new LaunchSpec { File = file, Arguments = arguments });

        var info = Assert.Single(_started);
        Assert.Equal(System32("control.exe"), info.FileName);
        Assert.Equal("\"" + System32(file) + "\"" + suffix, info.Arguments);
        Assert.Equal(Environment.SystemDirectory, info.WorkingDirectory);
    }

    [Fact]
    public void Launch_PowerShell_UsesRunnerPowerShellPath()
    {
        Launcher().Launch(new LaunchSpec { File = "powershell.exe" });

        var info = Assert.Single(_started);
        Assert.Equal(PowerShellScriptRunner.PowerShellPath, info.FileName);
        Assert.Equal(string.Empty, info.Arguments);
        Assert.True(File.Exists(info.FileName));
    }

    [Fact]
    public void Launch_ShellGuid_WithOwnExplorerInSession_StartsExplorerFromWindowsDirectoryThroughCreateProcess()
    {
        _explorers.Processes.Add(new ExplorerProcessInfo(Session, Me, @"C:\Windows\Explorer.EXE"));

        Launcher().Launch(new LaunchSpec { File = DevicesAndPrinters });

        var info = Assert.Single(_started);
        Assert.Equal(Explorer, info.FileName);
        Assert.Equal(DevicesAndPrinters, info.Arguments);
        Assert.Equal(Environment.SystemDirectory, info.WorkingDirectory);
        Assert.NotEqual(Path.Combine(Environment.SystemDirectory, "explorer.exe"), info.FileName);
    }

    [Fact]
    public void Launch_ShellGuid_NoExplorerAtAll_RefusesWithoutStarting()
    {
        var ex = Assert.Throws<OperationException>(() => Launcher().Launch(new LaunchSpec { File = DevicesAndPrinters }));

        Assert.Equal(OperationErrorKind.Unexpected, ex.Kind);
        Assert.Equal(ShellSessionGuard.RefusalMessage, ex.Message);
        Assert.Empty(_started);
    }

    [Theory]
    [InlineData(0, Me)]
    [InlineData(2, Me)]
    [InlineData(Session, Other)]
    public void Launch_ShellGuid_ExplorerOfOtherSessionOrAccount_RefusesWithoutStarting(int session, string sid)
    {
        _explorers.Processes.Add(new ExplorerProcessInfo(session, sid, @"C:\Windows\Explorer.EXE"));

        var ex = Assert.Throws<OperationException>(() => Launcher().Launch(new LaunchSpec { File = DevicesAndPrinters }));

        Assert.Equal(ShellSessionGuard.RefusalMessage, ex.Message);
        Assert.Empty(_started);
    }

    [Fact]
    public void Launch_ShellGuid_OnlyFactoryExplorerOfThisAccount_RefusesWithoutStarting()
    {
        _explorers.Processes.Add(new ExplorerProcessInfo(Session, Me, @"C:\Windows\explorer.exe /factory,{5BD95610-9434-43C2-886C-57852CC8A120} -Embedding"));

        var ex = Assert.Throws<OperationException>(() => Launcher().Launch(new LaunchSpec { File = DevicesAndPrinters }));

        Assert.Equal(ShellSessionGuard.RefusalMessage, ex.Message);
        Assert.Empty(_started);
    }

    [Fact]
    public void Launch_ShellGuid_QueryFails_RefusesWithoutStarting()
    {
        var failure = new InvalidOperationException("WMI niedostępne");
        _explorers.Failure = failure;

        var ex = Assert.Throws<OperationException>(() => Launcher().Launch(new LaunchSpec { File = DevicesAndPrinters }));

        Assert.Equal(OperationErrorKind.Unexpected, ex.Kind);
        Assert.Contains("WMI niedostępne", ex.Message, StringComparison.Ordinal);
        Assert.Same(failure, ex.InnerException);
        Assert.Empty(_started);
    }

    [Theory]
    [InlineData("control.exe")]
    [InlineData("compmgmt.msc")]
    [InlineData("main.cpl")]
    public void Launch_NonShellFile_DoesNotAskAboutExplorersEvenWithoutOne(string file)
    {
        Launcher().Launch(new LaunchSpec { File = file });

        Assert.Single(_started);
        Assert.Equal(0, _explorers.Calls);
    }

    [Fact]
    public void Launch_MissingSystemFile_FailsWithoutStarting()
    {
        var ex = Assert.Throws<OperationException>(() => Launcher().Launch(new LaunchSpec { File = "brak-dso-" + Guid.NewGuid().ToString("N") + ".exe" }));

        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
        Assert.Empty(_started);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\control.exe")]
    [InlineData(@"..\control.exe")]
    [InlineData("control.bat")]
    [InlineData("ms-settings:display")]
    public void Launch_ForbiddenFile_FailsWithoutStarting(string file)
    {
        Assert.Throws<OperationException>(() => Launcher().Launch(new LaunchSpec { File = file }));
        Assert.Empty(_started);
    }

    [Fact]
    public void Launch_Win32Error_BecomesOperationExceptionWithWin32Message()
    {
        var win32 = new Win32Exception(5);

        var ex = Assert.Throws<OperationException>(() => Launcher(win32).Launch(new LaunchSpec { File = "control.exe" }));

        Assert.Equal(OperationErrorKind.Unexpected, ex.Kind);
        Assert.Contains("control.exe", ex.Message, StringComparison.Ordinal);
        Assert.Contains(win32.Message, ex.Message, StringComparison.Ordinal);
        Assert.Same(win32, ex.InnerException);
    }
}

/// <summary>Zmienia katalog bieżący procesu, więc nie może biec równolegle z innymi testami.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CurrentDirectoryIsolation
{
    public const string Name = "Katalog bieżący procesu";
}

/// <summary>Scenariusz pendrive'a: plik o dozwolonej nazwie leży w katalogu bieżącym, ale nie w System32.</summary>
[Trait("Category", "Windows")]
[Collection(CurrentDirectoryIsolation.Name)]
public class ShellProcessLauncherWorkingDirectoryTests
{
    [Fact]
    public void Launch_FileInWorkingDirectoryButNotInSystem32_FailsWithoutStarting()
    {
        var started = new List<string>();
        var name = "dso-pendrive-" + Guid.NewGuid().ToString("N") + ".exe";
        var previous = Environment.CurrentDirectory;
        var directory = Path.Combine(Path.GetTempPath(), "dso-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), string.Empty);
        try
        {
            Environment.CurrentDirectory = directory;

            var ex = Assert.Throws<OperationException>(() => new ShellProcessLauncher((fileName, _, _) => started.Add(fileName), new NoExplorers(), 1, "S-1-5-18").Launch(new LaunchSpec { File = name }));

            Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
            Assert.Empty(started);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            Directory.Delete(directory, recursive: true);
        }
    }
}

internal sealed record StartedProcess(string FileName, string Arguments, string WorkingDirectory);

internal sealed class NoExplorers : IExplorerProcessQuery
{
    public IReadOnlyList<ExplorerProcessInfo> GetExplorerProcesses() => Array.Empty<ExplorerProcessInfo>();
}
