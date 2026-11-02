using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class StartupMeasurementsTests
{
    private const string Probe = @"C:\dsotest\DsoRunUser.exe";
    private const string Rundll = @"C:\Windows\System32\rundll32.exe";

    private static readonly DateTimeOffset WrittenAt = new(2026, 11, 2, 8, 45, 10, TimeSpan.Zero);

    private static readonly StartupInfoFile File = new(@"C:\Windows\System32\wdi\LogFiles\StartupInfo\S-1-5-21-1-2-3-1001_StartupInfo4.xml", 4, WrittenAt);

    private static readonly UserEnvironment Environment = UserEnvironment.FromVariables(new Dictionary<string, string>
    {
        ["SystemRoot"] = @"C:\Windows",
        ["LOCALAPPDATA"] = @"C:\Users\dso\AppData\Local",
    });

    private static AutostartEntry Entry(string? filePath, AutostartSource source = AutostartSource.RunUser, string? command = null, bool interpreter = false)
        => new()
        {
            Id = AutostartIds.For(source, "Entry", source == AutostartSource.StartupTask ? "Family_8wekyb3d8bbwe" : null),
            Source = source,
            EntryName = "Entry",
            Name = "Entry",
            Location = "HKLM",
            FileStatus = AutostartFileStatus.Exists,
            Switch = new AutostartSwitch(AutostartSwitchKind.StartupApproved, "HKLM", "Entry", AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null),
            FilePath = filePath,
            Command = command,
            IsInterpreter = interpreter,
        };

    private static StartupProcess Process(string name, string? parent, long cpu, long disk = 1000, string? commandLine = null, DateTime? start = null)
        => new(name, commandLine, cpu, disk, parent, start ?? new DateTime(2026, 11, 2, 8, 43, 45));

    [Fact]
    public void SamePathParentExplorer_Found()
    {
        var measurement = StartupMeasurements.Find(
            Entry(Probe),
            new[] { Process(@"C:\Windows\explorer.exe", "userinit.exe", 5), Process(@"c:\DSOTEST\dsorunuser.EXE", "explorer.exe", 1_534_474, 3_002_368) },
            File,
            fromEarlierStart: false,
            Environment);

        Assert.Equal(new StartupMeasurement(1_534_474, 3_002_368, WrittenAt, FromEarlierStart: false), measurement);
    }

    [Fact]
    public void ParentRunonce_ForRun32_Found()
    {
        var measurement = StartupMeasurements.Find(
            Entry(@"C:\dsotest\DsoRunWow.exe", AutostartSource.Run32),
            new[] { Process(@"C:\dsotest\DsoRunWow.exe", "runonce.exe", 700) },
            File,
            fromEarlierStart: true,
            Environment);

        Assert.Equal(new StartupMeasurement(700, 1000, WrittenAt, FromEarlierStart: true), measurement);
    }

    [Fact]
    public void ParentSihost_ForStartupTask_Found()
    {
        const string Terminal = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe";

        var measurement = StartupMeasurements.Find(
            Entry(Terminal, AutostartSource.StartupTask),
            new[] { Process(Terminal, "sihost.exe", 2_500_000) },
            File,
            fromEarlierStart: false,
            Environment);

        Assert.Equal(2_500_000, measurement?.CpuMicroseconds);
    }

    [Fact]
    public void ParentNameWithFolder_ComparedByFileName()
        => Assert.NotNull(StartupMeasurements.Find(
            Entry(Probe),
            new[] { Process(Probe, @"C:\Windows\Explorer.EXE", 1) },
            File,
            fromEarlierStart: false,
            Environment));

    [Fact]
    public void ChildSamePath_ParentNotLauncher_Ignored()
    {
        var measurement = StartupMeasurements.Find(
            Entry(Probe),
            new[] { Process(Probe, "DsoRunUser.exe", 9_000_000), Process(Probe, null, 8_000_000) },
            File,
            fromEarlierStart: false,
            Environment);

        Assert.Null(measurement);
    }

    [Fact]
    public void Several_EarliestStartTime()
    {
        var measurement = StartupMeasurements.Find(
            Entry(Probe),
            new[]
            {
                Process(Probe, "explorer.exe", 300, start: new DateTime(2026, 11, 2, 8, 43, 50)),
                Process(Probe, "explorer.exe", 100, start: new DateTime(2026, 11, 2, 8, 43, 40)),
                Process(Probe, "explorer.exe", 400, start: null),
                Process(Probe, "explorer.exe", 200, start: new DateTime(2026, 11, 2, 8, 43, 45)),
            },
            File,
            fromEarlierStart: false,
            Environment);

        Assert.Equal(100, measurement?.CpuMicroseconds);
    }

    [Fact]
    public void ProcessWithoutCpuOrDisk_NotUsed()
    {
        var withoutCpu = new StartupProcess(Probe, null, null, 10, "explorer.exe", new DateTime(2026, 11, 2, 8, 43, 40));
        var withoutDisk = new StartupProcess(Probe, null, 10, null, "explorer.exe", new DateTime(2026, 11, 2, 8, 43, 41));
        var complete = Process(Probe, "explorer.exe", 77, start: new DateTime(2026, 11, 2, 8, 43, 42));

        Assert.Null(StartupMeasurements.Find(Entry(Probe), new[] { withoutCpu, withoutDisk }, File, false, Environment));
        Assert.Equal(77, StartupMeasurements.Find(Entry(Probe), new[] { withoutCpu, withoutDisk, complete }, File, false, Environment)?.CpuMicroseconds);
    }

    [Fact]
    public void Interpreter_RequiresNormalizedCommandLine()
    {
        var entry = Entry(Rundll, command: @"""%SystemRoot%\System32\rundll32.exe"" C:\Tools\Helper.dll,Start", interpreter: true);

        var measurement = StartupMeasurements.Find(
            entry,
            new[]
            {
                Process(Rundll, "explorer.exe", 900, commandLine: @"rundll32.exe C:\Tools\Other.dll,Start", start: new DateTime(2026, 11, 2, 8, 43, 40)),
                Process(Rundll, "explorer.exe", 450, commandLine: "\"C:\\Windows\\System32\\RUNDLL32.exe\"  C:\\Tools\\Helper.dll,Start ", start: new DateTime(2026, 11, 2, 8, 43, 41)),
            },
            File,
            fromEarlierStart: false,
            Environment);

        Assert.Equal(450, measurement?.CpuMicroseconds);
    }

    [Fact]
    public void Interpreter_CommandDiffers_NotMeasured()
    {
        var entry = Entry(Rundll, command: @"rundll32.exe C:\Tools\Helper.dll,Start", interpreter: true);

        Assert.Null(StartupMeasurements.Find(
            entry,
            new[] { Process(Rundll, "explorer.exe", 900, commandLine: @"rundll32.exe C:\Tools\Helper.dll,Stop") },
            File,
            fromEarlierStart: false,
            Environment));
    }

    [Fact]
    public void Interpreter_NoCommand_NotMeasured()
        => Assert.Null(StartupMeasurements.Find(
            Entry(Rundll, interpreter: true),
            new[] { Process(Rundll, "explorer.exe", 900, commandLine: "rundll32.exe") },
            File,
            fromEarlierStart: false,
            Environment));

    [Fact]
    public void AliasWindowsApps_NotMeasured()
    {
        var entry = Entry(@"C:\Users\dso\AppData\Local\Microsoft\WindowsApps\MSTeams_8wekyb3d8bbwe\ms-teams.exe");

        Assert.Null(StartupMeasurements.Find(
            entry,
            new[] { Process(@"C:\Program Files\WindowsApps\MSTeams_25290.205.3165.3466_x64__8wekyb3d8bbwe\ms-teams.exe", "explorer.exe", 1_200_000) },
            File,
            fromEarlierStart: false,
            Environment));
    }

    [Fact]
    public void NoFilePath_Null()
        => Assert.Null(StartupMeasurements.Find(
            Entry(null),
            new[] { Process(Probe, "explorer.exe", 1) },
            File,
            fromEarlierStart: false,
            Environment));

    [Fact]
    public void NullArguments_Throw()
    {
        var processes = Array.Empty<StartupProcess>();
        Assert.Throws<ArgumentNullException>(() => StartupMeasurements.Find(null!, processes, File, false, Environment));
        Assert.Throws<ArgumentNullException>(() => StartupMeasurements.Find(Entry(Probe), null!, File, false, Environment));
        Assert.Throws<ArgumentNullException>(() => StartupMeasurements.Find(Entry(Probe), processes, null!, false, Environment));
        Assert.Throws<ArgumentNullException>(() => StartupMeasurements.Find(Entry(Probe), processes, File, false, null!));
    }
}
