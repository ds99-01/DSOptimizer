using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartClassifierTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private const string AdminSid = "S-1-5-21-1-2-3-500";
    private const string TaskKey = @"HKU\" + Sid + @"_Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\Microsoft.WindowsTerminal_8wekyb3d8bbwe\StartTerminalOnLoginTask";

    private const string ProtectedEntry = """
        { "id": "windows.security-health",
          "match": { "file": "SecurityHealthSystray.exe", "folders": ["%SystemRoot%\\System32\\"], "publishers": ["Microsoft Corporation"] },
          "verdict": "protected", "summary": "Ikona Zabezpieczeń Windows.", "source": ["https://learn.microsoft.com/windows/security"],
          "confirmed": true }
        """;

    private const string CanDisableEntry = """
        { "id": "steam",
          "match": { "file": "steam.exe", "folders": ["%ProgramFiles(x86)%\\Steam\\"], "publishers": ["Valve*"] },
          "verdict": "can-disable", "summary": "Klient Steam.", "source": ["docs/zrodla/2026-11-02-autostart-pomiar.md", "https://help.steampowered.com/x"],
          "confirmed": true }
        """;

    private const string SecurityHealth = @"C:\Windows\System32\SecurityHealthSystray.exe";
    private const string Steam = @"C:\Program Files (x86)\Steam\steam.exe";

    private static readonly AutostartKnownCatalog Catalog = AutostartKnownCatalog.Load(AutostartKnownCatalogTests.Wrap(ProtectedEntry, CanDisableEntry));

    private static readonly UserEnvironment Environment = UserEnvironment.FromVariables(new Dictionary<string, string>
    {
        ["SystemRoot"] = @"C:\Windows",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
    });

    private static readonly AutostartSource[] AllApproved =
    {
        AutostartSource.RunUser, AutostartSource.RunMachine, AutostartSource.Run32, AutostartSource.FolderUser, AutostartSource.FolderCommon,
    };

    [Fact]
    public void Protected_LockNoCheckbox()
    {
        var row = Single(Run(AutostartSource.RunMachine, "SecurityHealth", SecurityHealth, "Microsoft Corporation"));

        Assert.Equal(AutostartBlocker.Protected, row.Blocker);
        Assert.Equal(AutostartVerdict.Protected, row.Verdict);
        Assert.Equal("windows.security-health", row.Known?.Id);
        Assert.False(row.CanToggle);
        Assert.False(row.Suggested);
        Assert.Equal(AutostartText.ProtectedTooltip, row.BlockedReason);
    }

    [Fact]
    public void UserUncertain_UserAndStoreReadOnly_MachineToggles()
    {
        var rows = Classify(
            Context() with { UserCertain = false },
            Run(AutostartSource.RunUser, "A", valid: true),
            Task(Switch(AutostartSwitchFormat.Valid, AutostartState.Enabled, taskState: 2, keyExists: true)),
            Run(AutostartSource.RunMachine, "B", valid: true),
            Run(AutostartSource.FolderUser, "C.lnk", valid: true));

        Assert.Equal(
            new[] { AutostartBlocker.UserUncertain, AutostartBlocker.UserUncertain, AutostartBlocker.None, AutostartBlocker.UserUncertain },
            rows.Select(r => r.Blocker));
        Assert.Equal(new[] { false, false, true, false }, rows.Select(r => r.CanToggle));
        Assert.Equal("Nie udało się ustalić zalogowanego użytkownika.", rows[0].BlockedReason);
    }

    [Fact]
    public void OtherProcessAndNoUserHive_ReadOnly()
    {
        var entries = new[]
        {
            Run(AutostartSource.RunUser, "A", valid: true),
            Task(Switch(AutostartSwitchFormat.Valid, AutostartState.Enabled, taskState: 2, keyExists: true)),
            Run(AutostartSource.Run32, "B", valid: true),
        };

        var none = Classify(Context() with { ProcessSid = AdminSid, UserHiveLoaded = false, ClassesHiveLoaded = false }, entries);
        var classesOnly = Classify(Context() with { ProcessSid = AdminSid, UserHiveLoaded = false, ClassesHiveLoaded = true }, entries);
        var userOnly = Classify(Context() with { ProcessSid = AdminSid, UserHiveLoaded = true, ClassesHiveLoaded = false }, entries);

        Assert.Equal(new[] { AutostartBlocker.UserHiveMissing, AutostartBlocker.UserHiveMissing, AutostartBlocker.None }, none.Select(r => r.Blocker));
        Assert.Equal(new[] { AutostartBlocker.UserHiveMissing, AutostartBlocker.None, AutostartBlocker.None }, classesOnly.Select(r => r.Blocker));
        Assert.Equal(new[] { AutostartBlocker.None, AutostartBlocker.UserHiveMissing, AutostartBlocker.None }, userOnly.Select(r => r.Blocker));
        Assert.Equal(AutostartText.Blocked(AutostartBlocker.UserHiveMissing), none[0].BlockedReason);
    }

    [Fact]
    public void OtherProcessWithUserHive_Toggles()
    {
        var entries = new[]
        {
            Run(AutostartSource.RunUser, "A", valid: true),
            Task(Switch(AutostartSwitchFormat.Valid, AutostartState.Disabled, taskState: 1, keyExists: true)),
        };

        var other = Classify(Context() with { ProcessSid = AdminSid }, entries);
        var sameAccountOtherCase = Classify(Context() with { ProcessSid = Sid.ToLowerInvariant(), UserHiveLoaded = false, ClassesHiveLoaded = false }, entries);

        Assert.All(other, r => Assert.True(r.CanToggle));
        Assert.All(sameAccountOtherCase, r => Assert.Equal(AutostartBlocker.None, r.Blocker));
    }

    [Fact]
    public void UnknownFormat_ReadOnly()
    {
        var row = Single(Run(AutostartSource.RunMachine, "A") with { Switch = ApprovedSwitch(AutostartSwitchFormat.UnknownFormat, AutostartState.Unknown) });

        Assert.Equal(AutostartBlocker.UnknownFormat, row.Blocker);
        Assert.False(row.CanToggle);
        Assert.Equal(AutostartText.Blocked(AutostartBlocker.UnknownFormat), row.BlockedReason);
    }

    [Fact]
    public void Unreadable_ReadOnly()
    {
        var row = Single(Run(AutostartSource.RunMachine, "A") with { Switch = ApprovedSwitch(AutostartSwitchFormat.Unreadable, AutostartState.Unknown) });

        Assert.Equal(AutostartBlocker.Unreadable, row.Blocker);
        Assert.False(row.CanToggle);
    }

    [Fact]
    public void TaskPolicy_ReadOnly()
    {
        var row = Single(Task(Switch(AutostartSwitchFormat.Policy, AutostartState.Unknown, taskState: 4, keyExists: true)));

        Assert.Equal(AutostartBlocker.TaskStateNotSupported, row.Blocker);
        Assert.False(row.CanToggle);
    }

    [Fact]
    public void AbsentValue_SourceNotConfirmed_ReadOnly()
    {
        var context = Context() with { WriteFromZeroConfirmed = new[] { AutostartSource.RunMachine } };

        var rows = Classify(context, Run(AutostartSource.RunUser, "A"), Run(AutostartSource.RunUser, "B", valid: true), Run(AutostartSource.FolderCommon, "C.lnk"));

        Assert.Equal(
            new[] { AutostartBlocker.WriteFromZeroNotMeasured, AutostartBlocker.None, AutostartBlocker.WriteFromZeroNotMeasured },
            rows.Select(r => r.Blocker));
        Assert.False(rows[0].CanToggle);
        Assert.Equal(AutostartText.Blocked(AutostartBlocker.WriteFromZeroNotMeasured), rows[0].BlockedReason);
    }

    [Fact]
    public void AbsentValue_SourceConfirmed_Toggles()
    {
        var rows = Classify(Context(), AllApproved.Select(source => Run(source, "A")).ToArray());

        Assert.All(rows, r => Assert.Equal(AutostartBlocker.None, r.Blocker));
        Assert.All(rows, r => Assert.True(r.CanToggle));
    }

    [Fact]
    public void TaskKeyAbsent_NotConfirmed_ReadOnly()
    {
        var task = Task(Switch(AutostartSwitchFormat.Absent, AutostartState.Disabled, taskState: null, keyExists: false));

        Assert.Equal(AutostartBlocker.WriteFromZeroNotMeasured, Single(Context(keyAbsent: false, state0: true), task).Blocker);
        Assert.Equal(AutostartBlocker.None, Single(Context(keyAbsent: true, state0: false), task).Blocker);
        Assert.True(Single(Context(keyAbsent: true, state0: false), task).CanToggle);
    }

    [Fact]
    public void TaskAbsent_ManifestEnabled_ReadOnly()
    {
        var keyAbsent = Task(Switch(AutostartSwitchFormat.Absent, AutostartState.Enabled, taskState: null, keyExists: false, manifestEnabled: true));
        var state0 = Task(Switch(AutostartSwitchFormat.Absent, AutostartState.Enabled, taskState: 0, keyExists: true, manifestEnabled: true));

        var rows = Classify(Context(keyAbsent: true, state0: true), keyAbsent, state0);

        Assert.All(rows, r => Assert.Equal(AutostartBlocker.WriteFromZeroNotMeasured, r.Blocker));
        Assert.All(rows, r => Assert.False(r.CanToggle));
    }

    [Fact]
    public void TaskState0_OnlyWhenState0Confirmed()
    {
        var task = Task(Switch(AutostartSwitchFormat.Absent, AutostartState.Disabled, taskState: 0, keyExists: true));

        Assert.Equal(AutostartBlocker.WriteFromZeroNotMeasured, Single(Context(keyAbsent: true, state0: false), task).Blocker);
        Assert.Equal(AutostartBlocker.None, Single(Context(keyAbsent: false, state0: true), task).Blocker);
    }

    [Fact]
    public void TaskKeyWithoutState_ReadOnly()
    {
        var task = Task(Switch(AutostartSwitchFormat.Absent, AutostartState.Disabled, taskState: null, keyExists: true));

        Assert.Equal(AutostartBlocker.WriteFromZeroNotMeasured, Single(Context(keyAbsent: true, state0: true), task).Blocker);
    }

    [Fact]
    public void WriteFromZeroAllowed_OnlyAbsentNeedsEvidence()
    {
        var none = Context() with { WriteFromZeroConfirmed = Array.Empty<AutostartSource>(), StartupTaskKeyAbsentEnable = false, StartupTaskState0Enable = false };

        Assert.True(AutostartClassifier.WriteFromZeroAllowed(ApprovedSwitch(AutostartSwitchFormat.Valid, AutostartState.Disabled), AutostartSource.RunUser, none));
        Assert.True(AutostartClassifier.WriteFromZeroAllowed(
            Switch(AutostartSwitchFormat.Valid, AutostartState.Enabled, taskState: 2, keyExists: true, manifestEnabled: true), AutostartSource.StartupTask, none));
        Assert.False(AutostartClassifier.WriteFromZeroAllowed(ApprovedSwitch(AutostartSwitchFormat.Absent, AutostartState.Enabled), AutostartSource.RunUser, none));
        Assert.True(AutostartClassifier.WriteFromZeroAllowed(ApprovedSwitch(AutostartSwitchFormat.Absent, AutostartState.Enabled), AutostartSource.RunUser, Context()));
        Assert.Throws<ArgumentNullException>(() => AutostartClassifier.WriteFromZeroAllowed(null!, AutostartSource.RunUser, none));
        Assert.Throws<ArgumentNullException>(() => AutostartClassifier.WriteFromZeroAllowed(ApprovedSwitch(AutostartSwitchFormat.Valid, AutostartState.Enabled), AutostartSource.RunUser, null!));
    }

    [Fact]
    public void EmptyName_ReadOnly()
    {
        var row = Single(Run(AutostartSource.RunMachine, string.Empty, valid: true));

        Assert.Equal(AutostartBlocker.NoName, row.Blocker);
        Assert.False(row.CanToggle);
        Assert.Equal(AutostartText.Blocked(AutostartBlocker.NoName), row.BlockedReason);
    }

    [Fact]
    public void CanToggle_ProtectedEvenWithBlockerNone_False()
    {
        var row = Single(Run(AutostartSource.RunMachine, "SecurityHealth", SecurityHealth, "Microsoft Corporation"));
        var unknown = Single(Run(AutostartSource.RunMachine, "A", valid: true));

        Assert.False((row with { Blocker = AutostartBlocker.None }).CanToggle);
        Assert.False((unknown with { Entry = unknown.Entry with { Switch = ApprovedSwitch(AutostartSwitchFormat.UnknownFormat, AutostartState.Enabled) } }).CanToggle);
        Assert.False((unknown with { Entry = unknown.Entry with { Switch = ApprovedSwitch(AutostartSwitchFormat.Valid, AutostartState.Unknown) } }).CanToggle);
        Assert.True(unknown.CanToggle);
        Assert.Null(unknown.BlockedReason);
    }

    [Fact]
    public void Group_MissingWinsOverState()
    {
        var row = Single(Run(AutostartSource.RunMachine, "A", valid: true, disabled: true) with { FileStatus = AutostartFileStatus.Missing });

        Assert.True(row.MissingFile);
        Assert.Equal(AutostartGroup.MissingFile, row.Group);
        Assert.Equal(AutostartGroup.Disabled, Single(Run(AutostartSource.RunMachine, "A", valid: true, disabled: true)).Group);
    }

    [Fact]
    public void Group_UnknownInEnabled()
    {
        var row = Single(Run(AutostartSource.RunMachine, "A") with { Switch = ApprovedSwitch(AutostartSwitchFormat.UnknownFormat, AutostartState.Unknown) });

        Assert.Equal(AutostartState.Unknown, row.State);
        Assert.Equal(AutostartGroup.Enabled, row.Group);
    }

    [Fact]
    public void Suggested_CanDisableEnabled()
    {
        var row = Single(Run(AutostartSource.RunMachine, "Steam", Steam, "Valve Corporation"));

        Assert.Equal(AutostartVerdict.CanDisable, row.Verdict);
        Assert.True(row.CanToggle);
        Assert.True(row.Suggested);
    }

    [Fact]
    public void Suggested_MissingEnabledWithoutVerdict()
    {
        var row = Single(Run(AutostartSource.RunUser, "Old", @"C:\Users\dso\AppData\Local\Old\old.exe", null) with { FileStatus = AutostartFileStatus.Missing });

        Assert.Equal(AutostartVerdict.None, row.Verdict);
        Assert.True(row.Suggested);
    }

    [Fact]
    public void NotSuggested_NoVerdict()
    {
        var row = Single(Run(AutostartSource.RunUser, "Other", @"C:\Tools\other.exe", "Contoso"));

        Assert.True(row.CanToggle);
        Assert.False(row.Suggested);
    }

    [Fact]
    public void NotSuggested_Disabled()
    {
        var row = Single(Run(AutostartSource.RunMachine, "Steam", Steam, "Valve Corporation", valid: true, disabled: true));

        Assert.Equal(AutostartVerdict.CanDisable, row.Verdict);
        Assert.True(row.CanToggle);
        Assert.False(row.Suggested);
    }

    [Fact]
    public void NotSuggested_CanDisableButReadOnly()
    {
        var rows = Classify(Context() with { UserCertain = false }, Run(AutostartSource.RunUser, "Steam", Steam, "Valve Corporation"));

        Assert.Equal(AutostartVerdict.CanDisable, rows[0].Verdict);
        Assert.False(rows[0].CanToggle);
        Assert.False(rows[0].Suggested);
    }

    [Fact]
    public void NotSuggested_ProtectedEvenMissing()
    {
        var row = Single(Run(AutostartSource.RunMachine, "SecurityHealth", SecurityHealth, "Microsoft Corporation") with { FileStatus = AutostartFileStatus.Missing });

        Assert.Equal(AutostartGroup.MissingFile, row.Group);
        Assert.False(row.Suggested);
    }

    [Fact]
    public void UnquotedNoCandidate_NotMissing_NotSuggested()
    {
        var java = Run(AutostartSource.Run32, "SunJavaUpdateSched", null, null) with
        {
            FileStatus = AutostartFileStatus.NotDetermined,
            Command = @"C:\Program Files (x86)\Common Files\Java\Java Update\jusched.exe",
        };

        var row = Single(java);

        Assert.False(row.MissingFile);
        Assert.Equal(AutostartGroup.Enabled, row.Group);
        Assert.Equal(AutostartVerdict.None, row.Verdict);
        Assert.False(row.Suggested);
    }

    [Fact]
    public void Order_MeasuredDescThenNamesAlphabetical()
    {
        var rows = Classify(
            Context(),
            Named("run:user:z", "Zoom", null),
            Named("run:user:b", "beta", 300_000),
            Named("run:user:c2", "Ćma", null),
            Named("run:user:a", "Alfa", 1_200_000),
            Named("run:user:c1", "ćma", null),
            Named("run:user:e", "Edge", null));

        Assert.Equal(
            new[] { "run:user:a", "run:user:b", "run:user:c1", "run:user:c2", "run:user:e", "run:user:z" },
            AutostartClassifier.Order(rows).Select(r => r.Id));
    }

    [Fact]
    public void Classify_NullArguments_Throw()
    {
        var reading = new AutostartReading(Array.Empty<AutostartEntry>(), Environment, null, Array.Empty<string>());

        Assert.Throws<ArgumentNullException>(() => AutostartClassifier.Classify(null!, Catalog, Context()));
        Assert.Throws<ArgumentNullException>(() => AutostartClassifier.Classify(reading, null!, Context()));
        Assert.Throws<ArgumentNullException>(() => AutostartClassifier.Classify(reading, Catalog, null!));
        Assert.Throws<ArgumentNullException>(() => AutostartClassifier.Order(null!));
    }

    internal static AutostartContext Context(bool keyAbsent = true, bool state0 = true)
        => new(Sid, true, Sid, true, true, AutostartLocations.ForUser(Sid), AllApproved, keyAbsent, state0);

    private static AutostartEntry Named(string id, string name, long? cpu)
        => Run(AutostartSource.RunUser, id, valid: true) with
        {
            Id = id,
            Name = name,
            Measurement = cpu is { } value ? new StartupMeasurement(value, 0, new DateTimeOffset(2026, 11, 2, 8, 20, 5, TimeSpan.Zero), false) : null,
        };

    private static AutostartRow Single(AutostartEntry entry) => Single(Context(), entry);

    private static AutostartRow Single(AutostartContext context, AutostartEntry entry) => Assert.Single(Classify(context, entry));

    private static IReadOnlyList<AutostartRow> Classify(AutostartContext context, params AutostartEntry[] entries)
        => AutostartClassifier.Classify(new AutostartReading(entries, Environment, null, Array.Empty<string>()), Catalog, context);

    private static AutostartEntry Run(
        AutostartSource source,
        string name,
        string? filePath = null,
        string? publisher = null,
        bool valid = false,
        bool disabled = false)
        => new()
        {
            Id = AutostartIds.For(source, name),
            Source = source,
            EntryName = name,
            Name = name.Length == 0 ? AutostartText.NoName : name,
            Location = "loc",
            FileStatus = filePath is null ? AutostartFileStatus.NotDetermined : AutostartFileStatus.Exists,
            Switch = ApprovedSwitch(valid ? AutostartSwitchFormat.Valid : AutostartSwitchFormat.Absent, disabled ? AutostartState.Disabled : AutostartState.Enabled),
            Publisher = publisher,
            FilePath = filePath,
        };

    private static AutostartEntry Task(AutostartSwitch taskSwitch) => new()
    {
        Id = AutostartIds.For(AutostartSource.StartupTask, "StartTerminalOnLoginTask", "Microsoft.WindowsTerminal_8wekyb3d8bbwe"),
        Source = AutostartSource.StartupTask,
        EntryName = "StartTerminalOnLoginTask",
        Name = "Terminal",
        Location = "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
        FileStatus = AutostartFileStatus.Exists,
        Switch = taskSwitch,
        Publisher = "Microsoft Corporation",
        FilePath = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe",
        PackageFamily = "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
    };

    private static AutostartSwitch ApprovedSwitch(AutostartSwitchFormat format, AutostartState state)
        => new(AutostartSwitchKind.StartupApproved, @"HKLM\X\StartupApproved\Run", "A", format, state, null, null);

    private static AutostartSwitch Switch(AutostartSwitchFormat format, AutostartState state, uint? taskState, bool keyExists, bool manifestEnabled = false)
        => new(AutostartSwitchKind.StartupTask, TaskKey, "State", format, state, null, taskState, keyExists, manifestEnabled);
}
