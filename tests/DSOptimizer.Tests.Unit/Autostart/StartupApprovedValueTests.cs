using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class StartupApprovedValueTests
{
    private static readonly DateTimeOffset DisabledAt = new(2026, 11, 2, 8, 37, 53, 977, TimeSpan.Zero);

    private static byte[] Bytes(byte first, DateTimeOffset? at = null)
    {
        var bytes = new byte[12];
        bytes[0] = first;
        if (at is { } moment)
        {
            Array.Copy(BitConverter.GetBytes(moment.UtcDateTime.ToFileTimeUtc()), 0, bytes, 4, 8);
        }

        return bytes;
    }

    private static StartupApprovedRead Binary(byte[] bytes) => StartupApprovedValue.Interpret(RegistryReadResult.Found(RegValue.FromBinary(bytes)));

    [Fact]
    public void Absent_EnabledAbsent()
        => Assert.Equal(
            new StartupApprovedRead(AutostartSwitchFormat.Absent, AutostartState.Enabled, null, null),
            StartupApprovedValue.Interpret(RegistryReadResult.NotFound()));

    [Fact]
    public void Bytes02Zeros_Enabled()
    {
        var read = Binary(Bytes(0x02));

        Assert.Equal(AutostartSwitchFormat.Valid, read.Format);
        Assert.Equal(AutostartState.Enabled, read.State);
        Assert.Null(read.DisabledAt);
        Assert.Equal(Bytes(0x02), read.Raw);
    }

    [Fact]
    public void Bytes06_EnabledValid()
    {
        var read = Binary(Bytes(0x06));

        Assert.Equal(AutostartSwitchFormat.Valid, read.Format);
        Assert.Equal(AutostartState.Enabled, read.State);
    }

    [Fact]
    public void Bytes03Filetime_DisabledWithDate()
    {
        var read = Binary(Bytes(0x03, DisabledAt));

        Assert.Equal(AutostartSwitchFormat.Valid, read.Format);
        Assert.Equal(AutostartState.Disabled, read.State);
        Assert.Equal(DisabledAt, read.DisabledAt);
    }

    [Fact]
    public void Bytes03ZeroFiletime_DisabledNoDate()
    {
        var read = Binary(Bytes(0x03));

        Assert.Equal(AutostartState.Disabled, read.State);
        Assert.Null(read.DisabledAt);
    }

    [Fact]
    public void FiletimeOutOfRange_NoDate()
    {
        var bytes = Bytes(0x03);
        for (var i = 4; i < 12; i++)
        {
            bytes[i] = 0xFF;
        }

        var read = Binary(bytes);

        Assert.Equal(AutostartState.Disabled, read.State);
        Assert.Null(read.DisabledAt);
    }

    [Fact]
    public void Bytes07_Disabled()
        => Assert.Equal(AutostartState.Disabled, Binary(Bytes(0x07, DisabledAt)).State);

    [Fact]
    public void Length8_UnknownFormat()
    {
        var read = Binary(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0 });

        Assert.Equal(AutostartSwitchFormat.UnknownFormat, read.Format);
        Assert.Equal(AutostartState.Disabled, read.State);
        Assert.Null(read.DisabledAt);
        Assert.Equal(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0 }, read.Raw);
    }

    [Fact]
    public void EmptyBinary_UnknownFormatUnknownState()
    {
        var read = Binary(Array.Empty<byte>());

        Assert.Equal(AutostartSwitchFormat.UnknownFormat, read.Format);
        Assert.Equal(AutostartState.Unknown, read.State);
    }

    [Fact]
    public void DWord_UnknownFormat()
    {
        var read = StartupApprovedValue.Interpret(RegistryReadResult.Found(RegValue.FromDWord(2)));

        Assert.Equal(new StartupApprovedRead(AutostartSwitchFormat.UnknownFormat, AutostartState.Unknown, null, null), read);
    }

    [Fact]
    public void AccessDenied_Unreadable()
    {
        Assert.Equal(
            new StartupApprovedRead(AutostartSwitchFormat.Unreadable, AutostartState.Unknown, null, null),
            StartupApprovedValue.Interpret(RegistryReadResult.AccessDenied("odmowa")));
        Assert.Equal(AutostartSwitchFormat.Unreadable, StartupApprovedValue.Interpret(RegistryReadResult.Failed("błąd")).Format);
    }

    [Fact]
    public void Disabled_FromNull_03PlusFiletime()
        => Assert.Equal(Bytes(0x03, DisabledAt), StartupApprovedValue.Disabled(null, DisabledAt));

    [Fact]
    public void Disabled_From06_07KeepsBytes1To3()
    {
        var current = Bytes(0x06);
        current[1] = 0x11;
        current[2] = 0x22;
        current[3] = 0x33;

        var result = StartupApprovedValue.Disabled(current, DisabledAt.ToOffset(TimeSpan.FromHours(1)));

        var expected = Bytes(0x07, DisabledAt);
        expected[1] = 0x11;
        expected[2] = 0x22;
        expected[3] = 0x33;
        Assert.Equal(expected, result);
        Assert.Equal(0x06, current[0]);
        Assert.Equal(DisabledAt, Binary(result).DisabledAt);
    }

    [Fact]
    public void Enabled_From07_06Zeros()
    {
        var current = Bytes(0x07, DisabledAt);

        var result = StartupApprovedValue.Enabled(current);

        Assert.Equal(Bytes(0x06), result);
        Assert.Equal(0x07, current[0]);
    }

    [Fact]
    public void Disabled_Length11_Throws()
    {
        Assert.Throws<ArgumentException>(() => StartupApprovedValue.Disabled(new byte[11], DisabledAt));
        Assert.Throws<ArgumentException>(() => StartupApprovedValue.Enabled(new byte[13]));
        Assert.Throws<ArgumentNullException>(() => StartupApprovedValue.Enabled(null!));
        Assert.Equal(12, StartupApprovedValue.Length);
    }
}
