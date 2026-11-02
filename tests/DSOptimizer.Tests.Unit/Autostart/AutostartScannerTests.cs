using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartScannerTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private const string UserRoot = @"HKU\" + Sid;
    private const string ClassesRoot = @"HKU\" + Sid + "_Classes";
    private const string RunUserKey = UserRoot + @"\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunMachineKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedUserRun = UserRoot + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedUserFolder = UserRoot + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string ApprovedMachineRun = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32 = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedMachineFolder = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string VolatileKey = UserRoot + @"\Volatile Environment";
    private const string UserFolder = @"C:\Users\dso\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup";
    private const string CommonFolder = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp";
    private const string TerminalFamily = "Microsoft.WindowsTerminal_8wekyb3d8bbwe";
    private const string TerminalTask = "StartTerminalOnLoginTask";
    private const string TerminalKey = ClassesRoot + @"\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\" + TerminalFamily + @"\" + TerminalTask;
    private const string TerminalFolder = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe";
    private const string SecurityHealth = @"C:\Windows\system32\SecurityHealthSystray.exe";
    private const string OneDrive = @"C:\Users\dso\AppData\Local\Microsoft\OneDrive\OneDrive.exe";

    private static readonly DateTimeOffset DisabledAt = new(2026, 11, 2, 8, 37, 53, 977, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstBoot = new(2026, 11, 2, 8, 20, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondBoot = new(2026, 11, 2, 8, 45, 10, TimeSpan.Zero);

    private readonly FakeRegistry _registry = new();
    private readonly FakeAutostartFiles _files = new();
    private readonly FakeStartupTaskReader _tasks = new();
    private readonly FakeStartupInfoSource _startupInfo = new();
    private readonly ListLog _log = new();

    private readonly Dictionary<string, string> _machine = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = @"C:\Windows",
        ["windir"] = @"C:\Windows",
        ["SystemDrive"] = "C:",
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
        ["ProgramData"] = @"C:\ProgramData",
    };

    public AutostartScannerTests()
    {
        _registry.CreateKey(UserRoot);
        _registry.CreateKey(ClassesRoot);
        _registry.Set(VolatileKey, "APPDATA", RegValue.FromString(@"C:\Users\dso\AppData\Roaming"));
        _registry.Set(VolatileKey, "LOCALAPPDATA", RegValue.FromString(@"C:\Users\dso\AppData\Local"));
        _registry.Set(VolatileKey, "USERPROFILE", RegValue.FromString(@"C:\Users\dso"));
    }

    private static AutostartContext Context(bool userHive = true, bool classesHive = true)
        => new(Sid, UserCertain: true, Sid, userHive, classesHive, AutostartLocations.ForUser(Sid), AutostartEvidence.WriteFromZeroConfirmed,
            AutostartEvidence.StartupTaskKeyAbsentEnable, AutostartEvidence.StartupTaskState0Enable);

    private AutostartScanner Scanner(IRegistry? registry = null)
        => new(registry ?? _registry, _files, _tasks, _startupInfo, _machine, _log);

    private AutostartReading Read(bool userHive = true, bool classesHive = true) => Scanner().Read(Context(userHive, classesHive));

    private static AutostartEntry Entry(AutostartReading reading, string id) => Assert.Single(reading.Entries, e => e.Id == id);

    private static byte[] Approved(byte first, DateTimeOffset? disabledAt = null)
    {
        var bytes = new byte[12];
        bytes[0] = first;
        if (disabledAt is { } at)
        {
            BitConverter.GetBytes(at.UtcDateTime.ToFileTimeUtc()).CopyTo(bytes, 4);
        }

        return bytes;
    }

    private static StartupTaskManifest Terminal(bool enabled = false)
        => new(
            "Microsoft.WindowsTerminal",
            "Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe",
            TerminalFamily,
            TerminalFolder,
            TerminalTask,
            enabled,
            "Terminal",
            "Microsoft Corporation",
            "WindowsTerminal.exe");

    private void AddProbe(string key, string name, string file = "")
    {
        var path = file.Length == 0 ? @"C:\dsotest\" + name + ".exe" : file;
        _registry.Set(key, name, RegValue.FromString("\"" + path + "\""));
        _files.Existing.Add(path);
    }

    private void SetCleanSystem()
    {
        _registry.Set(RunMachineKey, "SecurityHealth", RegValue.FromExpandString(@"%windir%\system32\SecurityHealthSystray.exe"));
        _registry.Set(ApprovedMachineRun, "SecurityHealth", RegValue.FromBinary(Approved(0x04)));
        _files.Existing.Add(SecurityHealth);
        _files.Details[SecurityHealth] = new FileDetails("Microsoft Corporation", "Windows Security notification icon", "Microsoft® Windows® Operating System");
        _registry.CreateKey(Run32Key);
        _registry.Set(RunUserKey, "OneDrive", RegValue.FromString("\"" + OneDrive + "\" /background"));
        _files.Existing.Add(OneDrive);
        _files.Details[OneDrive] = new FileDetails("Microsoft Corporation", "Microsoft OneDrive", "Microsoft OneDrive");
        _files.AddToFolder(UserFolder, "desktop.ini");
        _files.AddToFolder(CommonFolder, "desktop.ini");
        _tasks.Tasks.Add(Terminal());
        _registry.Set(TerminalKey, "State", RegValue.FromDWord(0));
        _registry.Set(TerminalKey, "ShowNotification", RegValue.FromDWord(1));
        _registry.Set(TerminalKey, "UserEnabledStartupOnce", RegValue.FromDWord(0));
    }

    [Fact]
    public void CleanSystem_SecurityHealthAndOneDrive_EnabledFormatAbsentOrValid()
    {
        SetCleanSystem();

        var reading = Read();

        Assert.Equal(
            new[] { "run:user:OneDrive", "run:machine:SecurityHealth", "appx:" + TerminalFamily + "!" + TerminalTask },
            reading.Entries.Select(e => e.Id));

        var oneDrive = reading.Entries[0];
        Assert.Equal(AutostartSource.RunUser, oneDrive.Source);
        Assert.Equal(AutostartScope.User, oneDrive.Scope);
        Assert.Equal("Microsoft OneDrive", oneDrive.Name);
        Assert.Equal("Microsoft Corporation", oneDrive.Publisher);
        Assert.Equal(RunUserKey, oneDrive.Location);
        Assert.Equal(OneDrive, oneDrive.FilePath);
        Assert.Equal("\"" + OneDrive + "\" /background", oneDrive.Command);
        Assert.Equal(AutostartFileStatus.Exists, oneDrive.FileStatus);
        Assert.Equal(
            new AutostartSwitch(AutostartSwitchKind.StartupApproved, ApprovedUserRun, "OneDrive", AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null),
            oneDrive.Switch);

        var security = reading.Entries[1];
        Assert.Equal("Windows Security notification icon", security.Name);
        Assert.Equal(AutostartScope.Machine, security.Scope);
        Assert.Equal(SecurityHealth, security.FilePath);
        Assert.Equal(AutostartSwitchFormat.Valid, security.Switch.Format);
        Assert.Equal(AutostartState.Enabled, security.Switch.State);

        var terminal = reading.Entries[2];
        Assert.Equal(AutostartScope.Store, terminal.Scope);
        Assert.Equal(AutostartSwitchFormat.Absent, terminal.Switch.Format);
        Assert.Equal(AutostartState.Disabled, terminal.Switch.State);
        Assert.Equal(0u, terminal.Switch.TaskState);
        Assert.True(terminal.Switch.KeyExists);

        Assert.Equal(new[] { AutostartText.NoMeasurement }, reading.Warnings);
        Assert.Null(reading.MeasuredAt);
        Assert.Equal(@"C:\Users\dso\AppData\Roaming", reading.Environment.Get("APPDATA"));
    }

    [Fact]
    public void RunUser_03Filetime_DisabledWithDate()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        _registry.Set(ApprovedUserRun, "DsoRunUser", RegValue.FromBinary(Approved(0x03, DisabledAt)));

        var entry = Entry(Read(), "run:user:DsoRunUser");

        Assert.Equal(
            new AutostartSwitch(AutostartSwitchKind.StartupApproved, ApprovedUserRun, "DsoRunUser", AutostartSwitchFormat.Valid, AutostartState.Disabled, DisabledAt, null),
            entry.Switch);
        Assert.Equal("DsoRunUser", entry.EntryName);
        Assert.Equal(@"C:\dsotest\DsoRunUser.exe", entry.FilePath);
    }

    [Fact]
    public void RunMachine_SecurityHealth04_EnabledValid()
    {
        SetCleanSystem();

        var entry = Entry(Read(), "run:machine:SecurityHealth");

        Assert.Equal(
            new AutostartSwitch(AutostartSwitchKind.StartupApproved, ApprovedMachineRun, "SecurityHealth", AutostartSwitchFormat.Valid, AutostartState.Enabled, null, null),
            entry.Switch);
        Assert.Equal(RunMachineKey, entry.Location);
        Assert.Equal(@"%windir%\system32\SecurityHealthSystray.exe", entry.Command);
    }

    [Fact]
    public void Run32_FromWow6432Node_SwitchRun32Key()
    {
        AddProbe(Run32Key, "DsoRunWow");

        var entry = Entry(Read(), "run32:DsoRunWow");

        Assert.Equal(AutostartSource.Run32, entry.Source);
        Assert.Equal(Run32Key, entry.Location);
        Assert.Equal(ApprovedRun32, entry.Switch.KeyPath);
        Assert.Equal("DsoRunWow", entry.Switch.ValueName);
        Assert.Equal(AutostartSwitchKind.StartupApproved, entry.Switch.Kind);
    }

    [Fact]
    public void FolderUser_LnkTarget_FromUserAppData()
    {
        const string Lnk = UserFolder + @"\DsoLnkUser.lnk";
        const string Target = @"C:\Users\dso\AppData\Roaming\DsoTest\DsoLnkUser.exe";
        _files.AddToFolder(UserFolder, "DsoLnkUser.lnk");
        _files.Shortcuts[Lnk] = new ShortcutInfo(@"%APPDATA%\DsoTest\DsoLnkUser.exe", "--probe", Advertised: false);
        _files.Existing.Add(Target);

        var entry = Entry(Read(), "folder:user:DsoLnkUser.lnk");

        Assert.Equal(AutostartSource.FolderUser, entry.Source);
        Assert.Equal("DsoLnkUser.lnk", entry.EntryName);
        Assert.Equal("DsoLnkUser", entry.Name);
        Assert.Equal(UserFolder, entry.Location);
        Assert.Equal(Target, entry.FilePath);
        Assert.Equal(@"""%APPDATA%\DsoTest\DsoLnkUser.exe"" --probe", entry.Command);
        Assert.Equal(AutostartFileStatus.Exists, entry.FileStatus);
        Assert.Equal(
            new AutostartSwitch(AutostartSwitchKind.StartupApproved, ApprovedUserFolder, "DsoLnkUser.lnk", AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null),
            entry.Switch);
    }

    [Fact]
    public void UserFolder_FromVolatileAppData()
    {
        _machine["APPDATA"] = @"C:\Users\Administrator\AppData\Roaming";
        _machine["USERPROFILE"] = @"C:\Users\Administrator";
        _files.AddToFolder(UserFolder, "Tool.exe");

        var reading = Read();

        Assert.Equal(new[] { UserFolder, CommonFolder }, _files.ListedFolders);
        Assert.Equal(UserFolder + @"\Tool.exe", Entry(reading, "folder:user:Tool.exe").FilePath);
    }

    [Theory]
    [InlineData(@"\Environment")]
    [InlineData(@"\Volatile Environment")]
    public void CommonFolder_AccountRedefinesProgramData_FolderFromMachineValue(string environmentKey)
    {
        const string AccountFolder = @"C:\Users\dso\Fake\Microsoft\Windows\Start Menu\Programs\StartUp";
        _registry.Set(UserRoot + environmentKey, "ProgramData", RegValue.FromString(@"C:\Users\dso\Fake"));
        _files.AddToFolder(CommonFolder, "Tool.exe");
        _files.AddToFolder(AccountFolder, "Planted.exe");

        var reading = Read();

        Assert.Equal(new[] { UserFolder, CommonFolder }, _files.ListedFolders);
        Assert.Equal(CommonFolder, Entry(reading, "folder:common:Tool.exe").Location);
        Assert.DoesNotContain(reading.Entries, e => e.EntryName == "Planted.exe");
    }

    [Fact]
    public void CommonFolder_MachineProgramDataUnknown_FolderSkipped()
    {
        _machine.Remove("ProgramData");
        _registry.Set(UserRoot + @"\Environment", "ProgramData", RegValue.FromString(@"C:\ProgramData"));

        Read();

        Assert.Equal(new[] { UserFolder }, _files.ListedFolders);
    }

    [Fact]
    public void UserFolder_AppDataUnknown_FolderSkipped()
    {
        _registry.Set(VolatileKey, "APPDATA", RegValue.Absent);

        Read();

        Assert.Equal(new[] { CommonFolder }, _files.ListedFolders);
    }

    [Fact]
    public void Folder_DesktopIniSkipped()
    {
        _files.AddToFolder(CommonFolder, "desktop.ini");
        _files.AddToFolder(CommonFolder, "DESKTOP.INI");
        _files.AddToFolder(UserFolder, "desktop.ini");

        Assert.Empty(Read().Entries);
    }

    [Fact]
    public void Folder_PlainExeFile_Exists()
    {
        const string Path = CommonFolder + @"\Tool.exe";
        _files.AddToFolder(CommonFolder, "Tool.exe");
        _files.Details[Path] = new FileDetails(" Tool Corp ", " ", "Tool Suite");

        var entry = Entry(Read(), "folder:common:Tool.exe");

        Assert.Equal(AutostartFileStatus.Exists, entry.FileStatus);
        Assert.Equal(Path, entry.FilePath);
        Assert.Null(entry.Command);
        Assert.Equal("Tool Suite", entry.Name);
        Assert.Equal("Tool Corp", entry.Publisher);
        Assert.Equal(CommonFolder, entry.Location);
        Assert.Equal(new AutostartSwitch(AutostartSwitchKind.StartupApproved, ApprovedMachineFolder, "Tool.exe", AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null), entry.Switch);
        Assert.DoesNotContain(Path, _files.Probed);
    }

    [Fact]
    public void Lnk_Advertised_NotDetermined()
    {
        const string Lnk = CommonFolder + @"\Office.lnk";
        _files.AddToFolder(CommonFolder, "Office.lnk");
        _files.Shortcuts[Lnk] = new ShortcutInfo(null, null, Advertised: true);

        var entry = Entry(Read(), "folder:common:Office.lnk");

        Assert.Equal(AutostartFileStatus.NotDetermined, entry.FileStatus);
        Assert.Null(entry.FilePath);
        Assert.Null(entry.Command);
        Assert.Equal("Office", entry.Name);
    }

    [Fact]
    public void Lnk_TargetMissing_Missing()
    {
        const string Lnk = UserFolder + @"\Gone.lnk";
        _files.AddToFolder(UserFolder, "Gone.lnk");
        _files.Shortcuts[Lnk] = new ShortcutInfo(@"C:\Gone\gone.exe", null, Advertised: false);

        var entry = Entry(Read(), "folder:user:Gone.lnk");

        Assert.Equal(AutostartFileStatus.Missing, entry.FileStatus);
        Assert.Equal(@"C:\Gone\gone.exe", entry.FilePath);
        Assert.Equal(@"""C:\Gone\gone.exe""", entry.Command);
        Assert.DoesNotContain(@"C:\Gone\gone.exe", _files.DetailsRequested);
    }

    [Fact]
    public void Lnk_ReadThrows_NotDetermined()
    {
        const string Lnk = UserFolder + @"\Broken.lnk";
        _files.AddToFolder(UserFolder, "Broken.lnk");
        _files.ThrowingShortcuts.Add(Lnk);

        var entry = Entry(Read(), "folder:user:Broken.lnk");

        Assert.Equal(AutostartFileStatus.NotDetermined, entry.FileStatus);
        Assert.Null(entry.FilePath);
        Assert.Contains(_log.Lines, l => l.StartsWith("WARN", StringComparison.Ordinal) && l.Contains(Lnk));
    }

    [Fact]
    public void Lnk_NotAShortcut_NotDetermined()
    {
        _files.AddToFolder(UserFolder, "Fake.lnk");

        Assert.Equal(AutostartFileStatus.NotDetermined, Entry(Read(), "folder:user:Fake.lnk").FileStatus);
    }

    [Fact]
    public void Folder_Denied_OneWarning_OtherSourcesRead()
    {
        _files.DeniedFolders.Add(UserFolder);
        _files.AddToFolder(CommonFolder, "Tool.exe");

        var reading = Read();

        Assert.Single(reading.Entries);
        Assert.Single(reading.Warnings, w => w.StartsWith(AutostartText.SourceUnreadable(UserFolder, string.Empty), StringComparison.Ordinal));
    }

    [Fact]
    public void RunValue_MissingFile_Missing()
    {
        _registry.Set(RunUserKey, "DsoMissing", RegValue.FromString(@"C:\dsotest\DsoMissing.exe"));

        var entry = Entry(Read(), "run:user:DsoMissing");

        Assert.Equal(AutostartFileStatus.Missing, entry.FileStatus);
        Assert.Equal(@"C:\dsotest\DsoMissing.exe", entry.FilePath);
        Assert.Equal("DsoMissing", entry.Name);
        Assert.Null(entry.Publisher);
        Assert.Empty(_files.DetailsRequested);
    }

    [Fact]
    public void RunValue_ProbeUnknown_NotDetermined()
    {
        _registry.Set(RunUserKey, "Net", RegValue.FromString(@"\\server\share\tool.exe"));
        _files.Unknown.Add(@"\\server\share\tool.exe");

        var entry = Entry(Read(), "run:user:Net");

        Assert.Equal(AutostartFileStatus.NotDetermined, entry.FileStatus);
        Assert.Equal(@"\\server\share\tool.exe", entry.FilePath);
    }

    [Fact]
    public void RunWithoutExtension_ResolvesExe()
    {
        _registry.Set(RunUserKey, "Tool", RegValue.FromString("\"C:\\Tools\\app\" --min"));
        _files.Existing.Add(@"C:\Tools\app.exe");

        var entry = Entry(Read(), "run:user:Tool");

        Assert.Equal(AutostartFileStatus.Exists, entry.FileStatus);
        Assert.Equal(@"C:\Tools\app.exe", entry.FilePath);
    }

    [Theory]
    [InlineData(@"C:\Tools\app")]
    [InlineData("\"C:\\Tools\\app\" --min")]
    public void RunWithoutExtension_NothingExists_NotDetermined_NotSuggested(string command)
    {
        _registry.Set(RunUserKey, "Tool", RegValue.FromString(command));

        var reading = Read();
        var entry = Entry(reading, "run:user:Tool");
        var row = Assert.Single(
            AutostartClassifier.Classify(reading, AutostartKnownCatalog.Load(AutostartKnownCatalogTests.Wrap()), AutostartClassifierTests.Context()),
            r => r.Entry.Id == "run:user:Tool");

        Assert.Equal(AutostartFileStatus.NotDetermined, entry.FileStatus);
        Assert.Null(entry.FilePath);
        Assert.False(row.MissingFile);
        Assert.Equal(AutostartGroup.Enabled, row.Group);
        Assert.False(row.Suggested);
    }

    [Fact]
    public void RunWithoutExtension_OnlyFileWithoutExe_NotDetermined_NotSuggested()
    {
        _registry.Set(RunUserKey, "Tool", RegValue.FromString(@"C:\Tools\app"));
        _files.Existing.Add(@"C:\Tools\app");

        var reading = Read();
        var row = Assert.Single(
            AutostartClassifier.Classify(reading, AutostartKnownCatalog.Load(AutostartKnownCatalogTests.Wrap()), AutostartClassifierTests.Context()),
            r => r.Entry.Id == "run:user:Tool");

        Assert.Equal(AutostartFileStatus.NotDetermined, row.Entry.FileStatus);
        Assert.Null(row.Entry.FilePath);
        Assert.False(row.MissingFile);
        Assert.False(row.Suggested);
    }

    [Fact]
    public void RunWithExtension_Absent_Missing_Suggested()
    {
        _registry.Set(RunUserKey, "Old", RegValue.FromString("\"C:\\Tools\\old.exe\" --min"));

        var reading = Read();
        var row = Assert.Single(
            AutostartClassifier.Classify(reading, AutostartKnownCatalog.Load(AutostartKnownCatalogTests.Wrap()), AutostartClassifierTests.Context()),
            r => r.Entry.Id == "run:user:Old");

        Assert.Equal(AutostartFileStatus.Missing, row.Entry.FileStatus);
        Assert.Equal(@"C:\Tools\old.exe", row.Entry.FilePath);
        Assert.Equal(AutostartGroup.MissingFile, row.Group);
        Assert.True(row.Suggested);
    }

    [Fact]
    public void UnquotedNoCandidate_NotDetermined()
    {
        _registry.Set(RunMachineKey, "SunJavaUpdateSched", RegValue.FromString(@"C:\Program Files (x86)\Common Files\Java\Java Update\jusched.exe"));

        var entry = Entry(Read(), "run:machine:SunJavaUpdateSched");

        Assert.Equal(AutostartFileStatus.NotDetermined, entry.FileStatus);
        Assert.Null(entry.FilePath);
        Assert.Equal("SunJavaUpdateSched", entry.Name);
    }

    [Fact]
    public void UnexpandedVariable_NotDetermined_NoPath()
    {
        _registry.Set(RunUserKey, "Odd", RegValue.FromExpandString(@"%NOPE%\odd.exe"));

        var entry = Entry(Read(), "run:user:Odd");

        Assert.Equal(AutostartFileStatus.NotDetermined, entry.FileStatus);
        Assert.Null(entry.FilePath);
    }

    [Fact]
    public void RunValue_NonString_SkippedWithWarning()
    {
        _registry.Set(RunUserKey, "Number", RegValue.FromDWord(1));
        AddProbe(RunUserKey, "DsoRunUser");

        var reading = Read();

        Assert.Equal(new[] { "run:user:DsoRunUser" }, reading.Entries.Select(e => e.Id));
        Assert.Contains(AutostartText.ValueSkipped(RunUserKey, "Number"), reading.Warnings);
    }

    [Fact]
    public void RunValue_ReadDenied_SkippedWithWarning()
    {
        AddProbe(RunUserKey, "Secret");
        _registry.DenyRead(RunUserKey, "Secret");

        var reading = Read();

        Assert.Empty(reading.Entries);
        Assert.Contains(reading.Warnings, w => w.StartsWith(AutostartText.SourceUnreadable(RunUserKey + @"\Secret", string.Empty), StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyValueName_EntryNameEmpty_NameNoName()
    {
        _registry.Set(RunUserKey, string.Empty, RegValue.FromString(@"C:\dsotest\Default.exe"));

        var entry = Assert.Single(Read().Entries);

        Assert.Equal(string.Empty, entry.EntryName);
        Assert.Equal(AutostartText.NoName, entry.Name);
        Assert.Equal("run:user:", entry.Id);
        Assert.Equal(string.Empty, entry.Switch.ValueName);
    }

    [Fact]
    public void Name_FromFileDescription_KeepsU2024()
    {
        const string File = @"C:\Program Files (x86)\Battle.net\Battle.net Launcher.exe";
        _registry.Set(RunMachineKey, "Battle.net", RegValue.FromString("\"" + File + "\" --autostarted"));
        _files.Existing.Add(File);
        _files.Details[File] = new FileDetails("Blizzard Entertainment", "Battle\u2024net Launcher", "Battle.net");

        var entry = Entry(Read(), "run:machine:Battle.net");

        Assert.Equal("Battle\u2024net Launcher", entry.Name);
        Assert.Equal("Blizzard Entertainment", entry.Publisher);
    }

    [Fact]
    public void Name_FromProductName_WhenNoDescription()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        _files.Details[@"C:\dsotest\DsoRunUser.exe"] = new FileDetails(null, null, "DsoTest Suite");

        Assert.Equal("DsoTest Suite", Entry(Read(), "run:user:DsoRunUser").Name);
    }

    [Fact]
    public void Name_FallbackToValueName_WhenNoDetails()
    {
        AddProbe(RunUserKey, "DsoRunUser");

        var entry = Entry(Read(), "run:user:DsoRunUser");

        Assert.Equal("DsoRunUser", entry.Name);
        Assert.Null(entry.Publisher);
        Assert.Equal(new[] { @"C:\dsotest\DsoRunUser.exe" }, _files.DetailsRequested);
    }

    [Fact]
    public void Interpreter_NoFileDetails_NameFromValue()
    {
        _registry.Set(RunUserKey, "Helper", RegValue.FromString(@"rundll32.exe C:\Tools\Helper.dll,Start"));
        _files.Details[@"C:\Windows\System32\rundll32.exe"] = new FileDetails("Microsoft Corporation", "Windows host process (Rundll32)", "Windows");

        var entry = Entry(Read(), "run:user:Helper");

        Assert.True(entry.IsInterpreter);
        Assert.Equal(@"C:\Windows\System32\rundll32.exe", entry.FilePath);
        Assert.Equal("Helper", entry.Name);
        Assert.Null(entry.Publisher);
        Assert.Empty(_files.DetailsRequested);
    }

    [Fact]
    public void UserHiveNotLoaded_NoUserRowsNoUserFolder()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        AddProbe(RunMachineKey, "DsoRunHklm");
        _files.AddToFolder(UserFolder, "Tool.exe");
        var recording = new RecordingRegistry(_registry);

        var reading = Scanner(recording).Read(Context(userHive: false));

        Assert.Equal(new[] { "run:machine:DsoRunHklm" }, reading.Entries.Select(e => e.Id));
        Assert.Equal(new[] { CommonFolder }, _files.ListedFolders);
        Assert.DoesNotContain("ValueNames:" + RunUserKey, recording.Calls);
    }

    [Fact]
    public void ClassesHiveNotLoaded_NoStoreRows()
    {
        _tasks.Tasks.Add(Terminal());

        var reading = Read(classesHive: false);

        Assert.Empty(reading.Entries);
        Assert.Equal(0, _tasks.ReadCount);
    }

    [Fact]
    public void RunKeyDenied_OneWarning_OtherSourcesRead()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        AddProbe(RunMachineKey, "DsoRunHklm");
        AddProbe(Run32Key, "DsoRunWow");
        _registry.DenyKey(RunMachineKey);

        var reading = Read();

        Assert.Equal(new[] { "run:user:DsoRunUser", "run32:DsoRunWow" }, reading.Entries.Select(e => e.Id));
        var warning = Assert.Single(reading.Warnings, w => w != AutostartText.NoMeasurement);
        Assert.StartsWith(AutostartText.SourceUnreadable(RunMachineKey, string.Empty), warning, StringComparison.Ordinal);
        Assert.Contains(_log.Lines, l => l.StartsWith("WARN", StringComparison.Ordinal) && l.Contains(warning));
    }

    [Fact]
    public void StartupTask_Terminal_KeyAbsent_DisabledAbsentFromManifest()
    {
        _tasks.Tasks.Add(Terminal());
        _files.Existing.Add(TerminalFolder + @"\WindowsTerminal.exe");

        var entry = Entry(Read(), "appx:" + TerminalFamily + "!" + TerminalTask);

        Assert.Equal(AutostartSource.StartupTask, entry.Source);
        Assert.Equal(TerminalTask, entry.EntryName);
        Assert.Equal("Terminal", entry.Name);
        Assert.Equal("Microsoft Corporation", entry.Publisher);
        Assert.Equal(TerminalFamily, entry.Location);
        Assert.Equal(TerminalFamily, entry.PackageFamily);
        Assert.Equal(TerminalFolder + @"\WindowsTerminal.exe", entry.FilePath);
        Assert.Equal(AutostartFileStatus.Exists, entry.FileStatus);
        Assert.Null(entry.Command);
        Assert.Equal(
            new AutostartSwitch(AutostartSwitchKind.StartupTask, TerminalKey, "State", AutostartSwitchFormat.Absent, AutostartState.Disabled, null, null, KeyExists: false, EnabledInManifest: false),
            entry.Switch);
        Assert.Equal(new[] { Sid }, _tasks.RequestedSids);
    }

    [Fact]
    public void StartupTask_State2_EnabledValid()
    {
        _tasks.Tasks.Add(Terminal());
        _registry.Set(TerminalKey, "State", RegValue.FromDWord(2));
        _registry.Set(TerminalKey, "UserEnabledStartupOnce", RegValue.FromDWord(1));

        var entry = Entry(Read(), "appx:" + TerminalFamily + "!" + TerminalTask);

        Assert.Equal(AutostartSwitchFormat.Valid, entry.Switch.Format);
        Assert.Equal(AutostartState.Enabled, entry.Switch.State);
        Assert.Equal(2u, entry.Switch.TaskState);
        Assert.True(entry.Switch.KeyExists);
        Assert.Equal(AutostartFileStatus.NotDetermined, entry.FileStatus);
    }

    [Fact]
    public void StartupTask_NoDisplayNameNoExecutable_PackageName()
    {
        _tasks.Tasks.Add(Terminal() with { DisplayName = " ", Executable = null, PublisherDisplayName = null });

        var entry = Assert.Single(Read().Entries);

        Assert.Equal("Microsoft.WindowsTerminal", entry.Name);
        Assert.Null(entry.FilePath);
        Assert.Null(entry.Publisher);
        Assert.Equal(AutostartFileStatus.NotDetermined, entry.FileStatus);
        Assert.Empty(_files.Probed);
    }

    [Fact]
    public void StartupTask_InvalidTaskId_SkippedAndLogged()
    {
        _tasks.Tasks.Add(Terminal() with { TaskId = @"..\Other" });
        _tasks.Tasks.Add(Terminal());

        var reading = Read();

        Assert.Single(reading.Entries);
        Assert.Contains(_log.Lines, l => l.StartsWith("WARN", StringComparison.Ordinal) && l.Contains(@"..\Other"));
    }

    [Fact]
    public void StartupTask_ReaderWarnings_Passed()
    {
        _tasks.Warnings.Add("manifest warning");

        Assert.Contains("manifest warning", Read().Warnings);
    }

    [Fact]
    public void StartupTask_ReaderThrows_Warning()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        _tasks.Failure = new OperationException(OperationErrorKind.Timeout, "Get-AppxPackage timeout");

        var reading = Read();

        Assert.Equal(new[] { "run:user:DsoRunUser" }, reading.Entries.Select(e => e.Id));
        Assert.Contains(AutostartText.StoreUnreadable("Get-AppxPackage timeout"), reading.Warnings);
    }

    [Fact]
    public void Read_NeverWrites()
    {
        SetCleanSystem();
        AddProbe(RunUserKey, "DsoRunUser");
        AddProbe(Run32Key, "DsoRunWow");
        _registry.Set(ApprovedUserRun, "DsoRunUser", RegValue.FromBinary(Approved(0x03, DisabledAt)));
        _files.AddToFolder(UserFolder, "DsoLnkUser.lnk");
        _files.Shortcuts[UserFolder + @"\DsoLnkUser.lnk"] = new ShortcutInfo(@"C:\dsotest\DsoLnkUser.exe", null, Advertised: false);
        _startupInfo.Add(1, FirstBoot, StartupInfoXml.Of(new XmlProcess(@"C:\dsotest\DsoRunUser.exe", "explorer.exe", 1, 1)));
        var recording = new RecordingRegistry(_registry);

        var reading = Scanner(recording).Read(Context());

        Assert.Equal(6, reading.Entries.Count);
        Assert.Empty(_registry.WriteLog);
        Assert.Empty(_registry.DeletedKeys);
        Assert.NotEmpty(recording.Calls);
        Assert.All(recording.Calls, call => Assert.True(
            call.StartsWith("Read:", StringComparison.Ordinal)
            || call.StartsWith("ValueNames:", StringComparison.Ordinal)
            || call.StartsWith("KeyStatus:", StringComparison.Ordinal)
            || call.StartsWith("SubKeyNames:", StringComparison.Ordinal),
            call));
    }

    [Fact]
    public void Measurement_EnabledFromNewestFile()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        _startupInfo.Add(2, SecondBoot, StartupInfoXml.Of(new XmlProcess(@"C:\dsotest\DsoRunUser.exe", "explorer.exe", 1_534_474, 3_002_368)));
        _startupInfo.Add(1, FirstBoot, StartupInfoXml.Of(new XmlProcess(@"C:\dsotest\DsoRunUser.exe", "explorer.exe", 999, 999)));

        var reading = Read();

        Assert.Equal(new StartupMeasurement(1_534_474, 3_002_368, SecondBoot, FromEarlierStart: false), Entry(reading, "run:user:DsoRunUser").Measurement);
        Assert.Equal(SecondBoot, reading.MeasuredAt);
        Assert.Empty(reading.Warnings);
        Assert.Equal(new[] { Sid }, _startupInfo.RequestedSids);
    }

    [Fact]
    public void Measurement_EnabledNotInNewestFile_NotMeasured()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        _startupInfo.Add(1, FirstBoot, StartupInfoXml.Of(new XmlProcess(@"C:\dsotest\DsoRunUser.exe", "explorer.exe", 999, 999)));
        _startupInfo.Add(2, SecondBoot, StartupInfoXml.Of());

        var reading = Read();

        Assert.Null(Entry(reading, "run:user:DsoRunUser").Measurement);
        Assert.Equal(SecondBoot, reading.MeasuredAt);
    }

    [Fact]
    public void Measurement_DisabledFromEarlierStart_FlagTrue()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        _registry.Set(ApprovedUserRun, "DsoRunUser", RegValue.FromBinary(Approved(0x03, DisabledAt)));
        _startupInfo.Add(1, FirstBoot, StartupInfoXml.Of(new XmlProcess(@"C:\dsotest\DsoRunUser.exe", "explorer.exe", 1_400_000, 20_000)));
        _startupInfo.Add(3, SecondBoot, StartupInfoXml.Of(new XmlProcess(@"C:\Windows\System32\SecurityHealthSystray.exe", "explorer.exe", 19_000, 550_912)));
        _startupInfo.Add(2, FirstBoot.AddMinutes(5), StartupInfoXml.Of(new XmlProcess(@"C:\dsotest\DsoRunUser.exe", "explorer.exe", 1_500_000, 30_000)));

        var reading = Read();

        Assert.Equal(new StartupMeasurement(1_500_000, 30_000, FirstBoot.AddMinutes(5), FromEarlierStart: true), Entry(reading, "run:user:DsoRunUser").Measurement);
        Assert.Equal(SecondBoot, reading.MeasuredAt);
    }

    [Fact]
    public void Measurement_DisabledInNewestFile_FlagTrue()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        _registry.Set(ApprovedUserRun, "DsoRunUser", RegValue.FromBinary(Approved(0x03, DisabledAt)));
        _startupInfo.Add(1, SecondBoot, StartupInfoXml.Of(new XmlProcess(@"C:\dsotest\DsoRunUser.exe", "explorer.exe", 5, 6)));

        Assert.Equal(new StartupMeasurement(5, 6, SecondBoot, FromEarlierStart: true), Entry(Read(), "run:user:DsoRunUser").Measurement);
    }

    [Fact]
    public void Measurement_NoFiles_NotMeasured_OneWarning()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        AddProbe(RunMachineKey, "DsoRunHklm");

        var reading = Read();

        Assert.All(reading.Entries, e => Assert.Null(e.Measurement));
        Assert.Equal(new[] { AutostartText.NoMeasurement }, reading.Warnings);
        Assert.Null(reading.MeasuredAt);
    }

    [Fact]
    public void Measurement_SourceThrows_OneWarning()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        _startupInfo.FilesFailure = new OperationException(OperationErrorKind.AccessDenied, "StartupInfo denied");

        var reading = Read();

        Assert.Null(Entry(reading, "run:user:DsoRunUser").Measurement);
        Assert.Equal(new[] { AutostartText.NoMeasurement }, reading.Warnings);
        Assert.Null(reading.MeasuredAt);
        Assert.Contains(_log.Lines, l => l.Contains("StartupInfo denied"));
    }

    [Fact]
    public void Measurement_BadXmlFile_SkippedOneWarning()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        AddProbe(RunMachineKey, "DsoRunHklm");
        AddProbe(Run32Key, "DsoRunWow");
        _registry.Set(ApprovedUserRun, "DsoRunUser", RegValue.FromBinary(Approved(0x03, DisabledAt)));
        _registry.Set(ApprovedRun32, "DsoRunWow", RegValue.FromBinary(Approved(0x03, DisabledAt)));
        var bad = _startupInfo.Add(3, SecondBoot, "<StartupData><Process");
        var good = _startupInfo.Add(2, FirstBoot, StartupInfoXml.Of(
            new XmlProcess(@"C:\dsotest\DsoRunHklm.exe", "explorer.exe", 10, 20),
            new XmlProcess(@"C:\dsotest\DsoRunUser.exe", "explorer.exe", 30, 40),
            new XmlProcess(@"C:\dsotest\DsoRunWow.exe", "runonce.exe", 50, 60)));

        var reading = Read();

        Assert.Equal(new[] { AutostartText.MeasurementFileSkipped(bad.Path) }, reading.Warnings);
        Assert.Equal(new StartupMeasurement(10, 20, FirstBoot, FromEarlierStart: false), Entry(reading, "run:machine:DsoRunHklm").Measurement);
        Assert.Equal(new StartupMeasurement(30, 40, FirstBoot, FromEarlierStart: true), Entry(reading, "run:user:DsoRunUser").Measurement);
        Assert.Equal(new StartupMeasurement(50, 60, FirstBoot, FromEarlierStart: true), Entry(reading, "run32:DsoRunWow").Measurement);
        Assert.Equal(FirstBoot, reading.MeasuredAt);
        Assert.Equal(1, _startupInfo.ReadCounts[bad.Path]);
        Assert.Equal(1, _startupInfo.ReadCounts[good.Path]);
    }

    [Fact]
    public void Measurement_FileReadDenied_SkippedOneWarning()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        var denied = _startupInfo.Add(2, SecondBoot, StartupInfoXml.Of());
        _startupInfo.Add(1, FirstBoot, StartupInfoXml.Of(new XmlProcess(@"C:\dsotest\DsoRunUser.exe", "explorer.exe", 1, 2)));
        _startupInfo.ThrowingReads.Add(denied.Path);

        var reading = Read();

        var warning = Assert.Single(reading.Warnings);
        Assert.StartsWith(AutostartText.SourceUnreadable(denied.Path, string.Empty), warning, StringComparison.Ordinal);
        Assert.Equal(FirstBoot, Entry(reading, "run:user:DsoRunUser").Measurement?.MeasuredAt);
    }

    [Fact]
    public void Measurement_AllFilesBad_NotMeasured()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        var bad = _startupInfo.Add(1, FirstBoot, "<Other/>");

        var reading = Read();

        Assert.Null(reading.MeasuredAt);
        Assert.Null(Entry(reading, "run:user:DsoRunUser").Measurement);
        Assert.Equal(new[] { AutostartText.MeasurementFileSkipped(bad.Path) }, reading.Warnings);
    }

    [Fact]
    public void Measurement_OlderFilesReadOnlyWhenNeeded()
    {
        AddProbe(RunUserKey, "DsoRunUser");
        var old = _startupInfo.Add(1, FirstBoot, StartupInfoXml.Of());
        _startupInfo.Add(2, SecondBoot, StartupInfoXml.Of());

        Read();

        Assert.False(_startupInfo.ReadCounts.ContainsKey(old.Path));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new AutostartScanner(null!, _files, _tasks, _startupInfo, _machine, _log));
        Assert.Throws<ArgumentNullException>(() => new AutostartScanner(_registry, null!, _tasks, _startupInfo, _machine, _log));
        Assert.Throws<ArgumentNullException>(() => new AutostartScanner(_registry, _files, null!, _startupInfo, _machine, _log));
        Assert.Throws<ArgumentNullException>(() => new AutostartScanner(_registry, _files, _tasks, null!, _machine, _log));
        Assert.Throws<ArgumentNullException>(() => new AutostartScanner(_registry, _files, _tasks, _startupInfo, null!, _log));
        Assert.Throws<ArgumentNullException>(() => new AutostartScanner(_registry, _files, _tasks, _startupInfo, _machine, null!));
        Assert.Throws<ArgumentNullException>(() => Scanner().Read(null!));
    }

    /// <summary>Rejestr przepuszczający wywołania do <see cref="FakeRegistry"/> i zapisujący nazwę każdej metody (także zapisów i usunięć).</summary>
    private sealed class RecordingRegistry : IRegistry
    {
        private readonly IRegistry _inner;

        public RecordingRegistry(IRegistry inner) => _inner = inner;

        public List<string> Calls { get; } = new();

        public RegistryReadResult Read(string path, string name)
        {
            Calls.Add("Read:" + path + "|" + name);
            return _inner.Read(path, name);
        }

        public void Write(string path, string name, RegValue value)
        {
            Calls.Add("Write:" + path + "|" + name);
            _inner.Write(path, name, value);
        }

        public ReadStatus KeyStatus(string path)
        {
            Calls.Add("KeyStatus:" + path);
            return _inner.KeyStatus(path);
        }

        public IReadOnlyList<string> SubKeyNames(string path)
        {
            Calls.Add("SubKeyNames:" + path);
            return _inner.SubKeyNames(path);
        }

        public IReadOnlyList<string> ValueNames(string path)
        {
            Calls.Add("ValueNames:" + path);
            return _inner.ValueNames(path);
        }

        public bool DeleteKeyIfEmpty(string path)
        {
            Calls.Add("DeleteKeyIfEmpty:" + path);
            return _inner.DeleteKeyIfEmpty(path);
        }

        public void DeleteKeyTree(string path)
        {
            Calls.Add("DeleteKeyTree:" + path);
            _inner.DeleteKeyTree(path);
        }
    }
}
