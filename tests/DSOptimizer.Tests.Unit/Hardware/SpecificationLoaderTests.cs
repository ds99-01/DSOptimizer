using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Hardware;

public sealed class SpecificationLoaderTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static int NotPending(HardwareSnapshot s)
    {
        var states = new[]
        {
            s.Processor.State, s.Memory.State, s.Board.State, s.Graphics.State, s.Displays.State, s.Sound.State,
            s.Disks.State, s.Network.State, s.Windows.State, s.Tpm.State, s.SecureBoot.State, s.Vbs.State, s.BitLocker.State,
        };
        return states.Count(x => x != ReadingState.Pending);
    }

    [Fact]
    public async Task LoadAsync_AllPartsSucceed_CompleteSnapshotNoWarnings()
    {
        var log = new ListLog();
        var loader = new SpecificationLoader(new FakeHardwareReader(), log, Generous);

        var snapshot = await loader.LoadAsync(_ => { });

        Assert.True(snapshot.IsComplete);
        Assert.Equal(13, NotPending(snapshot));
        Assert.DoesNotContain(new[]
        {
            snapshot.Processor.State, snapshot.Memory.State, snapshot.Board.State, snapshot.Graphics.State,
            snapshot.Displays.State, snapshot.Sound.State, snapshot.Disks.State, snapshot.Network.State,
            snapshot.Windows.State, snapshot.Tpm.State, snapshot.SecureBoot.State, snapshot.Vbs.State,
            snapshot.BitLocker.State,
        }, x => x != ReadingState.Done);
        Assert.Equal("Ryzen 7 9800X3D", snapshot.Processor.Value!.Name);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task LoadAsync_HangingPart_FailsAfterTimeoutOthersComplete()
    {
        var log = new ListLog();
        using var gate = new ManualResetEventSlim(false);
        var reader = new FakeHardwareReader
        {
            Disks = () =>
            {
                gate.Wait();
                return Array.Empty<DiskInfo>();
            },
        };
        var loader = new SpecificationLoader(reader, log, TimeSpan.FromSeconds(1));
        var updates = new List<HardwareSnapshot>();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var snapshot = await loader.LoadAsync(updates.Add);

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
            Assert.Equal(ReadingState.Failed, snapshot.Disks.State);
            Assert.Equal(ReadingState.Done, snapshot.Processor.State);
            Assert.Equal(ReadingState.Done, snapshot.BitLocker.State);
            Assert.Equal(13, NotPending(snapshot));
            Assert.True(snapshot.IsComplete);
            var line = Assert.Single(log.Lines);
            Assert.StartsWith("WARN [Specyfikacja]", line, StringComparison.Ordinal);
            Assert.Contains("Dyski", line, StringComparison.Ordinal);
            Assert.Contains("brak danych", line, StringComparison.Ordinal);
            Assert.DoesNotContain(log.Lines, l => l.StartsWith("ERROR", StringComparison.Ordinal));
        }
        finally
        {
            gate.Set();
        }

        // Późny wynik po zwolnieniu nie może nic zmienić ani wywołać onUpdate.
        var countAfterLoad = updates.Count;
        await Task.Delay(100);
        Assert.Equal(countAfterLoad, updates.Count);
        Assert.Single(log.Lines);
    }

    [Fact]
    public async Task LoadAsync_OverlappingCalls_AreIndependent()
    {
        var log = new ListLog();
        using var gate = new ManualResetEventSlim(false);
        using var firstEntered = new ManualResetEventSlim(false);
        var calls = 0;
        var reader = new FakeHardwareReader
        {
            Disks = () =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstEntered.Set();
                    gate.Wait();
                }

                return Array.Empty<DiskInfo>();
            },
        };
        var loader = new SpecificationLoader(reader, log, TimeSpan.FromSeconds(1));
        var firstUpdates = new List<HardwareSnapshot>();
        var secondUpdates = new List<HardwareSnapshot>();
        try
        {
            var first = loader.LoadAsync(firstUpdates.Add);
            Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5)));

            var secondResult = await loader.LoadAsync(secondUpdates.Add);
            var firstResult = await first;

            Assert.Equal(ReadingState.Done, secondResult.Disks.State);
            Assert.True(secondResult.IsComplete);
            Assert.Equal(13, secondUpdates.Count);
            Assert.All(secondUpdates, s => Assert.NotEqual(ReadingState.Failed, s.Disks.State));
            Assert.Same(secondUpdates[^1], secondResult);

            Assert.Equal(ReadingState.Failed, firstResult.Disks.State);
            Assert.True(firstResult.IsComplete);
            Assert.Equal(13, firstUpdates.Count);
            Assert.Same(firstUpdates[^1], firstResult);
            Assert.NotSame(firstResult, secondResult);

            var line = Assert.Single(log.Lines);
            Assert.Contains("Dyski", line, StringComparison.Ordinal);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task LoadAsync_ThrowingPart_FailedWithWarn()
    {
        var log = new ListLog();
        var reader = new FakeHardwareReader { Tpm = () => throw new InvalidOperationException("WMI") };
        var loader = new SpecificationLoader(reader, log, Generous);

        var snapshot = await loader.LoadAsync(_ => { });

        Assert.Equal(ReadingState.Failed, snapshot.Tpm.State);
        Assert.True(snapshot.IsComplete);
        var line = Assert.Single(log.Lines);
        Assert.StartsWith("WARN [Specyfikacja]", line, StringComparison.Ordinal);
        Assert.Contains("TPM", line, StringComparison.Ordinal);
        Assert.Contains("WMI", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ReportsProgressMonotonically()
    {
        var loader = new SpecificationLoader(new FakeHardwareReader(), new ListLog(), Generous);
        var updates = new List<HardwareSnapshot>();

        var result = await loader.LoadAsync(updates.Add);

        Assert.Equal(13, updates.Count);
        var counts = updates.Select(NotPending).ToList();
        for (var i = 1; i < counts.Count; i++)
        {
            Assert.True(counts[i] >= counts[i - 1]);
        }

        Assert.Equal(13, counts[^1]);
        Assert.Same(updates[^1], result);
    }

    [Fact]
    public async Task LoadAsync_TpmAbsent_DoneWithNull()
    {
        var reader = new FakeHardwareReader { Tpm = () => null };
        var loader = new SpecificationLoader(reader, new ListLog(), Generous);

        var snapshot = await loader.LoadAsync(_ => { });

        Assert.Equal(ReadingState.Done, snapshot.Tpm.State);
        Assert.Null(snapshot.Tpm.Value);
    }

    [Fact]
    public async Task LoadAsync_OnUpdateThrows_DoesNotThrowAndWarnsOnce()
    {
        var log = new ListLog();
        var loader = new SpecificationLoader(new FakeHardwareReader(), log, Generous);

        var snapshot = await loader.LoadAsync(_ => throw new InvalidOperationException("UI"));

        Assert.True(snapshot.IsComplete);
        var line = Assert.Single(log.Lines);
        Assert.StartsWith("WARN [Specyfikacja]", line, StringComparison.Ordinal);
        Assert.Contains("UI", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HardwarePart.Processor, "Procesor")]
    [InlineData(HardwarePart.Memory, "Pamięć")]
    [InlineData(HardwarePart.Board, "Płyta główna")]
    [InlineData(HardwarePart.Graphics, "Karta graficzna")]
    [InlineData(HardwarePart.Displays, "Monitory")]
    [InlineData(HardwarePart.Sound, "Dźwięk")]
    [InlineData(HardwarePart.Disks, "Dyski")]
    [InlineData(HardwarePart.Network, "Sieć")]
    [InlineData(HardwarePart.Windows, "Windows")]
    [InlineData(HardwarePart.Tpm, "TPM")]
    [InlineData(HardwarePart.SecureBoot, "Secure Boot")]
    [InlineData(HardwarePart.Vbs, "VBS i HVCI")]
    [InlineData(HardwarePart.BitLocker, "BitLocker")]
    public void PartName_ReturnsPolishLabel(HardwarePart part, string expected)
    {
        Assert.Equal(expected, SpecificationLoader.PartName(part));
    }

    [Fact]
    public void DefaultTimeout_IsTenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), SpecificationLoader.DefaultTimeout);
    }
}
