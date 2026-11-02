using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class StartupTaskValueTests
{
    private static readonly RegistryReadResult Missing = RegistryReadResult.NotFound();

    private static RegistryReadResult DWord(uint value) => RegistryReadResult.Found(RegValue.FromDWord(value));

    [Fact]
    public void Names_AsWrittenBySettings()
    {
        Assert.Equal("State", StartupTaskValue.State);
        Assert.Equal("LastDisabledTime", StartupTaskValue.LastDisabledTime);
        Assert.Equal("UserEnabledStartupOnce", StartupTaskValue.UserEnabledStartupOnce);
        Assert.Equal("ShowNotification", StartupTaskValue.ShowNotification);
    }

    [Fact]
    public void KeyAbsent_ManifestFalse_DisabledAbsent()
        => Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Absent, AutostartState.Disabled, null, null),
            StartupTaskValue.Interpret(ReadStatus.NotFound, Missing, Missing, enabledInManifest: false));

    [Fact]
    public void KeyAbsent_ManifestTrue_EnabledAbsent()
        => Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null),
            StartupTaskValue.Interpret(ReadStatus.NotFound, Missing, Missing, enabledInManifest: true));

    [Fact]
    public void State0_UsesManifest()
    {
        Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Absent, AutostartState.Disabled, null, 0u),
            StartupTaskValue.Interpret(ReadStatus.Found, DWord(0), Missing, enabledInManifest: false));
        Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Absent, AutostartState.Enabled, null, 0u),
            StartupTaskValue.Interpret(ReadStatus.Found, DWord(0), Missing, enabledInManifest: true));
    }

    [Fact]
    public void KeyWithoutState_UsesManifest()
        => Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null),
            StartupTaskValue.Interpret(ReadStatus.Found, Missing, Missing, enabledInManifest: true));

    [Fact]
    public void State1_DisabledWithLastDisabledTime()
    {
        var read = StartupTaskValue.Interpret(ReadStatus.Found, DWord(1), RegistryReadResult.Found(RegValue.FromQWord(1793609249)), false);

        Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Valid, AutostartState.Disabled, new DateTimeOffset(2026, 11, 2, 8, 47, 29, TimeSpan.Zero), 1u),
            read);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    public void State1_ZeroOrOutOfRangeTime_NoDate(ulong seconds)
        => Assert.Null(StartupTaskValue.Interpret(ReadStatus.Found, DWord(1), RegistryReadResult.Found(RegValue.FromQWord(seconds)), false).DisabledAt);

    [Fact]
    public void State1_TimeNotQWord_NoDate()
        => Assert.Null(StartupTaskValue.Interpret(ReadStatus.Found, DWord(1), DWord(5), false).DisabledAt);

    [Fact]
    public void State2_Enabled()
        => Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Valid, AutostartState.Enabled, null, 2u),
            StartupTaskValue.Interpret(ReadStatus.Found, DWord(2), RegistryReadResult.Found(RegValue.FromQWord(1793609249)), false));

    [Theory]
    [InlineData(3u)]
    [InlineData(4u)]
    [InlineData(7u)]
    public void OtherState_PolicyUnknown(uint state)
        => Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Policy, AutostartState.Unknown, null, state),
            StartupTaskValue.Interpret(ReadStatus.Found, DWord(state), Missing, true));

    [Fact]
    public void StateString_UnknownFormat()
        => Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.UnknownFormat, AutostartState.Unknown, null, null),
            StartupTaskValue.Interpret(ReadStatus.Found, RegistryReadResult.Found(RegValue.FromString("2")), Missing, true));

    [Theory]
    [InlineData(ReadStatus.AccessDenied)]
    [InlineData(ReadStatus.Error)]
    public void KeyDenied_Unreadable(ReadStatus keyStatus)
        => Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Unreadable, AutostartState.Unknown, null, null),
            StartupTaskValue.Interpret(keyStatus, DWord(2), Missing, true));

    [Fact]
    public void StateValueDenied_Unreadable()
        => Assert.Equal(
            new StartupTaskRead(AutostartSwitchFormat.Unreadable, AutostartState.Unknown, null, null),
            StartupTaskValue.Interpret(ReadStatus.Found, RegistryReadResult.AccessDenied("odmowa"), Missing, true));
}
