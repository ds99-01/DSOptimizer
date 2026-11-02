using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using DSOptimizer.Windows.Autostart;
using Microsoft.Win32;
using Xunit;

namespace DSOptimizer.Tests.Windows.Autostart;

/// <summary>
/// Skaner autostartu na prawdziwym rejestrze i plikach, ale wyłącznie w piaskownicy: miejsca konta, maszyny i klas mają korzenie
/// w <c>HKCU\Software\DSOptimizer.Tests\&lt;guid&gt;</c>, foldery Autostart i pliki pomiaru są w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>, a zadania
/// Sklepu podaje atrapa. Prawdziwy autostart hosta nie jest czytany ani zmieniany.
/// </summary>
[Trait("Category", "Windows")]
public sealed class AutostartScannerSandboxTests : IDisposable
{
    private const string Sid = "S-1-5-21-0-0-0-1000";
    private const string TerminalFamily = "Microsoft.WindowsTerminal_8wekyb3d8bbwe";
    private const string TerminalTask = "StartTerminalOnLoginTask";
    private const string UserCurrentVersion = @"user\Software\Microsoft\Windows\CurrentVersion";
    private const string MachineCurrentVersion = @"machine\SOFTWARE\Microsoft\Windows\CurrentVersion";

    private static readonly DateTimeOffset DisabledAt = new(2026, 11, 2, 8, 37, 53, TimeSpan.Zero);

    private readonly RegistrySandbox _sandbox = new();
    private readonly TempDirectory _temp = new();
    private readonly FakeStartupTaskReader _tasks = new();

    public AutostartScannerSandboxTests()
    {
        Directory.CreateDirectory(_temp.Sub("bin"));
        Directory.CreateDirectory(UserFolder);
        Directory.CreateDirectory(CommonFolder);
        Directory.CreateDirectory(_temp.Sub("wdi"));
        Directory.CreateDirectory(_temp.Sub("pakiet"));
        foreach (var name in new[] { "run-user.exe", "run-machine.exe", "run32.exe", "folder-user.exe" })
        {
            File.WriteAllText(Bin(name), "x");
        }

        File.WriteAllText(Path.Combine(_temp.Sub("pakiet"), "WindowsTerminal.exe"), "x");

        SetString(@"user\Volatile Environment", "APPDATA", _temp.Sub("appdata"));
        SetString(UserCurrentVersion + @"\Run", "SondaKonta", "\"" + Bin("run-user.exe") + "\" /min");
        SetString(UserCurrentVersion + @"\Run", "Brak", "\"" + Bin("nie-ma.exe") + "\"");
        SetBinary(UserCurrentVersion + @"\Explorer\StartupApproved\Run", "SondaKonta", Approved(0x03, DisabledAt));
        SetString(MachineCurrentVersion + @"\Run", "SondaMaszyny", Bin("run-machine.exe"));
        SetBinary(MachineCurrentVersion + @"\Explorer\StartupApproved\Run", "SondaMaszyny", Approved(0x02));
        SetString(@"machine\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Sonda32", Bin("run32.exe"));

        ShortcutWriter.Save(Path.Combine(UserFolder, "Sonda.lnk"), Bin("folder-user.exe"), "--tray");
        SetBinary(UserCurrentVersion + @"\Explorer\StartupApproved\StartupFolder", "Sonda.lnk", Approved(0x03, DisabledAt));
        File.WriteAllText(Path.Combine(CommonFolder, "sonda.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(CommonFolder, "desktop.ini"), "[.ShellClassInfo]");

        _tasks.Tasks.Add(new StartupTaskManifest(
            "Microsoft.WindowsTerminal",
            "Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe",
            TerminalFamily,
            _temp.Sub("pakiet"),
            TerminalTask,
            EnabledInManifest: false,
            "Terminal",
            "Microsoft Corporation",
            "WindowsTerminal.exe"));
        SetDword(@"classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\" + TerminalFamily + @"\" + TerminalTask, "State", 2);

        var xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<StartupData>"
            + "<Process Name=\"" + Bin("run-machine.exe") + "\" PID=\"1004\"><StartTime>2026/11/02:08:43:45.0360860</StartTime>"
            + "<CommandLine><![CDATA[\"" + Bin("run-machine.exe") + "\" ]]></CommandLine><DiskUsage Units=\"bytes\">4194304</DiskUsage>"
            + "<CpuUsage Units=\"us\">1500000</CpuUsage><ParentName>explorer.exe</ParentName></Process></StartupData>";
        File.WriteAllText(Path.Combine(_temp.Sub("wdi"), Sid + "_StartupInfo1.xml"), xml, Encoding.Unicode);
    }

    private string UserFolder => Path.Combine(_temp.Sub("appdata"), @"Microsoft\Windows\Start Menu\Programs\Startup");

    private string CommonFolder => Path.Combine(_temp.Sub("programdata"), @"Microsoft\Windows\Start Menu\Programs\StartUp");

    public void Dispose()
    {
        _sandbox.Dispose();
        _temp.Dispose();
    }

    private string Bin(string name) => Path.Combine(_temp.Sub("bin"), name);

    private string Root(string part) => _sandbox.HkcuPath + @"\" + part;

    private void SetString(string key, string name, string value) => Set(key, k => k.SetValue(name, value, RegistryValueKind.String));

    private void SetBinary(string key, string name, byte[] value) => Set(key, k => k.SetValue(name, value, RegistryValueKind.Binary));

    private void SetDword(string key, string name, int value) => Set(key, k => k.SetValue(name, value, RegistryValueKind.DWord));

    private void Set(string key, Action<RegistryKey> write)
    {
        using var root = _sandbox.OpenOrCreate();
        using var sub = root.CreateSubKey(key, writable: true)!;
        write(sub);
    }

    private static byte[] Approved(byte first, DateTimeOffset? disabledAt = null)
    {
        var bytes = new byte[12];
        bytes[0] = first;
        if (disabledAt is { } at)
        {
            BitConverter.GetBytes(at.ToFileTime()).CopyTo(bytes, 4);
        }

        return bytes;
    }

    private AutostartReading Read()
    {
        var locations = new AutostartLocations(
            Root("user"),
            Root("machine"),
            Root("classes"),
            @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup",
            @"%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp");
        var context = new AutostartContext(Sid, UserCertain: true, Sid, UserHiveLoaded: true, ClassesHiveLoaded: true, locations, AutostartEvidence.WriteFromZeroConfirmed, AutostartEvidence.StartupTaskKeyAbsentEnable, AutostartEvidence.StartupTaskState0Enable);
        var machine = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ["ProgramData"] = _temp.Sub("programdata"),
        };
        var scanner = new AutostartScanner(new WindowsRegistry(), new AutostartFiles(), _tasks, new StartupInfoFolder(_temp.Sub("wdi")), machine, new NullLog());
        return scanner.Read(context);
    }

    private static AutostartEntry Entry(AutostartReading reading, AutostartSource source, string name, string? family = null)
        => Assert.Single(reading.Entries, e => e.Id == AutostartIds.For(source, name, family));

    // Pięć źródeł rejestru i folderów zapisanych w piaskownicy (plus zadanie Sklepu z atrapy) – stany i pliki tak, jak je zapisano.
    [Fact]
    public void Sandbox_FiveSources_StatesAsWritten()
    {
        var reading = Read();

        Assert.Equal(7, reading.Entries.Count);

        var user = Entry(reading, AutostartSource.RunUser, "SondaKonta");
        Assert.Equal(AutostartState.Disabled, user.Switch.State);
        Assert.Equal(DisabledAt, user.Switch.DisabledAt);
        Assert.Equal(AutostartFileStatus.Exists, user.FileStatus);
        Assert.Equal(Bin("run-user.exe"), user.FilePath, ignoreCase: true);

        var missing = Entry(reading, AutostartSource.RunUser, "Brak");
        Assert.Equal(AutostartState.Enabled, missing.Switch.State);
        Assert.Equal(AutostartFileStatus.Missing, missing.FileStatus);

        var machine = Entry(reading, AutostartSource.RunMachine, "SondaMaszyny");
        Assert.Equal(AutostartState.Enabled, machine.Switch.State);
        Assert.Equal(AutostartFileStatus.Exists, machine.FileStatus);
        Assert.NotNull(machine.Measurement);
        Assert.Equal(1500000, machine.Measurement!.CpuMicroseconds);
        Assert.Equal(4194304, machine.Measurement.DiskBytes);

        var run32 = Entry(reading, AutostartSource.Run32, "Sonda32");
        Assert.Equal(AutostartState.Enabled, run32.Switch.State);
        Assert.Equal(AutostartFileStatus.Exists, run32.FileStatus);

        var shortcut = Entry(reading, AutostartSource.FolderUser, "Sonda.lnk");
        Assert.Equal(AutostartState.Disabled, shortcut.Switch.State);
        Assert.Equal(AutostartFileStatus.Exists, shortcut.FileStatus);
        Assert.Equal(Bin("folder-user.exe"), shortcut.FilePath, ignoreCase: true);
        Assert.Equal("Sonda", shortcut.Name);
        Assert.EndsWith("--tray", shortcut.Command, StringComparison.Ordinal);

        var common = Entry(reading, AutostartSource.FolderCommon, "sonda.cmd");
        Assert.Equal(AutostartState.Enabled, common.Switch.State);
        Assert.Equal(AutostartFileStatus.Exists, common.FileStatus);
        Assert.DoesNotContain(reading.Entries, e => e.EntryName == "desktop.ini");

        var terminal = Entry(reading, AutostartSource.StartupTask, TerminalTask, TerminalFamily);
        Assert.Equal(AutostartState.Enabled, terminal.Switch.State);
        Assert.Equal(AutostartFileStatus.Exists, terminal.FileStatus);
        Assert.Equal("Terminal", terminal.Name);
        Assert.Equal(new[] { Sid }, _tasks.RequestedSids);
        Assert.NotNull(reading.MeasuredAt);
    }

    // Odczyt niczego nie zapisuje: zrzut drzewa piaskownicy (klucze, wartości, typy, dane) i plików przed i po odczycie identyczny.
    [Fact]
    public void Sandbox_ReadNeverWrites()
    {
        var registryBefore = DumpRegistry();
        var filesBefore = DumpFiles();

        Read();
        Read();

        Assert.Equal(registryBefore, DumpRegistry());
        Assert.Equal(filesBefore, DumpFiles());
    }

    private List<string> DumpRegistry()
    {
        var lines = new List<string>();
        using var root = _sandbox.OpenOrCreate();
        Dump(root, string.Empty, lines);
        return lines;
    }

    private static void Dump(RegistryKey key, string path, List<string> lines)
    {
        lines.Add("K " + path);
        foreach (var name in key.GetValueNames().OrderBy(n => n, StringComparer.Ordinal))
        {
            var kind = key.GetValueKind(name);
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var data = value is byte[] bytes ? BitConverter.ToString(bytes) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            lines.Add("V " + path + "|" + name + "|" + kind + "|" + data);
        }

        foreach (var name in key.GetSubKeyNames().OrderBy(n => n, StringComparer.Ordinal))
        {
            using var sub = key.OpenSubKey(name)!;
            Dump(sub, path + @"\" + name, lines);
        }
    }

    private List<string> DumpFiles()
        => Directory.EnumerateFileSystemEntries(_temp.Root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => File.Exists(p)
                ? p + "|" + new FileInfo(p).Length + "|" + File.GetLastWriteTimeUtc(p).Ticks
                : p + "|dir|" + Directory.GetLastWriteTimeUtc(p).Ticks)
            .ToList();
}
