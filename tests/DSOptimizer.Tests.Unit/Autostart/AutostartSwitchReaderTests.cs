using System;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Model;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartSwitchReaderTests
{
    private const string ApprovedKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string TaskKey = @"HKU\S-1-5-21-1-2-3-1001_Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\Microsoft.WindowsTerminal_8wekyb3d8bbwe\StartTerminalOnLoginTask";

    private readonly FakeRegistry _registry = new();

    private static byte[] Bytes(byte first) => new byte[] { first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    [Fact]
    public void StartupApproved_ValueAbsent_EnabledAbsent()
        => Assert.Equal(
            new AutostartSwitch(AutostartSwitchKind.StartupApproved, ApprovedKey, "SecurityHealth", AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null),
            AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, ApprovedKey, "SecurityHealth", enabledInManifest: false));

    [Fact]
    public void StartupApproved_ReadDenied_Unreadable()
    {
        _registry.DenyRead(ApprovedKey, "SecurityHealth");

        var read = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, ApprovedKey, "SecurityHealth", enabledInManifest: false);

        Assert.Equal(AutostartSwitchFormat.Unreadable, read.Format);
        Assert.Equal(AutostartState.Unknown, read.State);
    }

    [Fact]
    public void StartupTask_KeyDenied_Unreadable()
    {
        _registry.CreateKey(TaskKey);
        _registry.DenyKey(TaskKey);

        var read = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupTask, TaskKey, "State", enabledInManifest: false);

        Assert.Equal(AutostartSwitchFormat.Unreadable, read.Format);
        Assert.Equal(AutostartState.Unknown, read.State);
    }

    [Fact]
    public void StartupTask_State1_DisabledWithDate_KeyAndManifestCarried()
    {
        _registry.Set(TaskKey, "State", RegValue.FromDWord(1));
        _registry.Set(TaskKey, "LastDisabledTime", RegValue.FromQWord(1_793_609_249));

        Assert.Equal(
            new AutostartSwitch(
                AutostartSwitchKind.StartupTask,
                TaskKey,
                "State",
                AutostartSwitchFormat.Valid,
                AutostartState.Disabled,
                new DateTimeOffset(2026, 11, 2, 8, 47, 29, TimeSpan.Zero),
                1u,
                KeyExists: true,
                EnabledInManifest: true),
            AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupTask, TaskKey, "State", enabledInManifest: true));
    }

    [Fact]
    public void Reread_SameSwitchAfterExternalChange_FreshState()
    {
        _registry.Set(ApprovedKey, "SecurityHealth", RegValue.FromBinary(Bytes(0x06)));
        var first = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, ApprovedKey, "SecurityHealth", enabledInManifest: false);
        _registry.Set(ApprovedKey, "SecurityHealth", RegValue.FromBinary(Bytes(0x07)));

        var fresh = AutostartSwitchReader.Reread(_registry, first);

        Assert.Equal(AutostartState.Enabled, first.State);
        Assert.Equal(first with { State = AutostartState.Disabled }, fresh);
    }

    [Fact]
    public void Reread_StartupTask_KeepsManifestState()
    {
        var first = AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupTask, TaskKey, "State", enabledInManifest: false);
        _registry.Set(TaskKey, "State", RegValue.FromDWord(0));

        var fresh = AutostartSwitchReader.Reread(_registry, first);

        Assert.False(first.KeyExists);
        Assert.Equal(AutostartState.Disabled, fresh.State);
        Assert.Equal(0u, fresh.TaskState);
        Assert.True(fresh.KeyExists);
        Assert.False(fresh.EnabledInManifest);
    }

    [Fact]
    public void StartupTask_OtherValueName_Throws()
        => Assert.Throws<ArgumentException>(() => AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupTask, TaskKey, "ShowNotification", false));

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => AutostartSwitchReader.Read(null!, AutostartSwitchKind.StartupApproved, ApprovedKey, "x", false));
        Assert.Throws<ArgumentNullException>(() => AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, null!, "x", false));
        Assert.Throws<ArgumentNullException>(() => AutostartSwitchReader.Read(_registry, AutostartSwitchKind.StartupApproved, ApprovedKey, null!, false));
        Assert.Throws<ArgumentNullException>(() => AutostartSwitchReader.Reread(_registry, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutostartSwitchReader.Read(_registry, (AutostartSwitchKind)7, ApprovedKey, "x", false));
    }
}
