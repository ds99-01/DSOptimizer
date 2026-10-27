using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Prep;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Prep;

public sealed class PrepLoaderTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static int NotPending(PrepSnapshot s)
    {
        var states = new[] { s.Board.State, s.Product.State, s.BitLocker.State, s.Activation.State, s.Graphics.State, s.Devices.State };
        return states.Count(x => x != ReadingState.Pending);
    }

    [Fact]
    public async Task LoadAsync_AllPartsDone_SnapshotComplete()
    {
        var log = new ListLog();
        var loader = new PrepLoader(new FakeHardwareReader(), new FakePrepReader(), log, Generous);

        var snapshot = await loader.LoadAsync(_ => { });

        Assert.True(snapshot.IsComplete);
        Assert.Equal(6, NotPending(snapshot));
        Assert.Equal(ReadingState.Done, snapshot.Product.State);
        Assert.Equal("SN123", snapshot.Product.Value!.SerialNumber);
        Assert.Equal(1u, snapshot.Activation.Value!.LicenseStatus);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task LoadAsync_PartThrows_OnlyThatPartFailed_WarnLogged()
    {
        var log = new ListLog();
        var prep = new FakePrepReader { Activation = () => throw new InvalidOperationException("SLS") };
        var loader = new PrepLoader(new FakeHardwareReader(), prep, log, Generous);

        var snapshot = await loader.LoadAsync(_ => { });

        Assert.Equal(ReadingState.Failed, snapshot.Activation.State);
        Assert.Equal(6, NotPending(snapshot));
        Assert.Equal(ReadingState.Done, snapshot.Product.State);
        Assert.True(snapshot.IsComplete);
        var line = Assert.Single(log.Lines);
        Assert.StartsWith("WARN [Przygotowanie]", line, StringComparison.Ordinal);
        Assert.Contains("Aktywacja", line, StringComparison.Ordinal);
        Assert.Contains("SLS", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_PartHangs_FailedAfterTimeout_OthersDone()
    {
        var log = new ListLog();
        using var gate = new ManualResetEventSlim(false);
        var prep = new FakePrepReader
        {
            Devices = () =>
            {
                gate.Wait();
                return Array.Empty<ProblemDevice>();
            },
        };
        var loader = new PrepLoader(new FakeHardwareReader(), prep, log, TimeSpan.FromMilliseconds(200));
        try
        {
            var snapshot = await loader.LoadAsync(_ => { });

            Assert.Equal(ReadingState.Failed, snapshot.Devices.State);
            Assert.Equal(ReadingState.Done, snapshot.Board.State);
            Assert.Equal(ReadingState.Done, snapshot.Graphics.State);
            Assert.True(snapshot.IsComplete);
            var line = Assert.Single(log.Lines);
            Assert.StartsWith("WARN [Przygotowanie]", line, StringComparison.Ordinal);
            Assert.Contains("Urządzenia", line, StringComparison.Ordinal);
            Assert.Contains("brak danych", line, StringComparison.Ordinal);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task LoadAsync_OnUpdate_ReceivesCumulativeSnapshots()
    {
        var loader = new PrepLoader(new FakeHardwareReader(), new FakePrepReader(), new ListLog(), Generous);
        var updates = new List<PrepSnapshot>();

        var result = await loader.LoadAsync(updates.Add);

        Assert.Equal(6, updates.Count);
        var counts = updates.Select(NotPending).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, counts);
        Assert.Same(updates[^1], result);
    }

    [Fact]
    public async Task LoadAsync_UsesHardwareReaderForBoardAndBitLocker()
    {
        var hardware = new FakeHardwareReader
        {
            Board = () => new BoardInfo("MSI", "MAG", "2.0", null, FirmwareKind.Uefi, null, null),
            BitLocker = () => new[] { new BitLockerVolume("D:", 1, 0) },
        };
        var loader = new PrepLoader(hardware, new FakePrepReader(), new ListLog(), Generous);

        var snapshot = await loader.LoadAsync(_ => { });

        Assert.Equal("MSI", snapshot.Board.Value!.Manufacturer);
        Assert.Equal("D:", Assert.Single(snapshot.BitLocker.Value!).DriveLetter);
    }

    [Fact]
    public void DefaultTimeout_IsTenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), PrepLoader.DefaultTimeout);
    }
}
