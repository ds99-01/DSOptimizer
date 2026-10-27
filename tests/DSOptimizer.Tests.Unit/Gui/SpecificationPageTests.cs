using System.Collections.Generic;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public sealed class SpecificationPageTests
{
    private static HardwareSnapshot Partial() => new() { Processor = Reading<CpuInfo>.Done(new CpuInfo("AMD Ryzen 5 7600", 6, 12)) };

    private static HardwareSnapshot Complete() => new()
    {
        Processor = Reading<CpuInfo>.Done(new CpuInfo("AMD Ryzen 5 7600", 6, 12)),
        Memory = Reading<MemoryInfo>.Failed,
        Board = Reading<BoardInfo>.Failed,
        Graphics = Reading<IReadOnlyList<GpuInfo>>.Failed,
        Displays = Reading<IReadOnlyList<DisplayInfo>>.Failed,
        Sound = Reading<IReadOnlyList<string>>.Failed,
        Disks = Reading<IReadOnlyList<DiskInfo>>.Failed,
        Network = Reading<IReadOnlyList<NetworkAdapterInfo>>.Failed,
        Windows = Reading<WindowsInfo>.Failed,
        Tpm = Reading<TpmInfo?>.Done(null),
        SecureBoot = Reading<SecureBootState>.Failed,
        Vbs = Reading<VbsInfo>.Failed,
        BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Failed,
    };

    [Fact]
    public void New_NothingRead_AllPendingNotComplete()
    {
        var page = new SpecificationPage();

        Assert.False(page.IsComplete);
        Assert.Equal(ReadingState.Pending, page.Snapshot.Processor.State);
    }

    [Fact]
    public void Accept_CurrentLoad_ReplacesSnapshot()
    {
        var page = new SpecificationPage();
        var load = page.BeginLoad();
        var partial = Partial();

        Assert.True(page.Accept(load, partial));
        Assert.Same(partial, page.Snapshot);
        Assert.False(page.IsComplete);

        var complete = Complete();
        Assert.True(page.Accept(load, complete));
        Assert.Same(complete, page.Snapshot);
        Assert.True(page.IsComplete);
    }

    [Fact]
    public void Accept_SupersededLoad_DropsUpdateAndResult()
    {
        var page = new SpecificationPage();
        var first = page.BeginLoad();
        var second = page.BeginLoad();
        var current = Partial();
        Assert.True(page.Accept(second, current));

        Assert.False(page.Accept(first, Partial()));
        Assert.False(page.Accept(first, Complete()));

        Assert.Same(current, page.Snapshot);
        Assert.False(page.IsComplete);
    }

    [Fact]
    public void Accept_AfterCompleteSnapshot_LateUpdateOfSameLoadIgnored()
    {
        var page = new SpecificationPage();
        var load = page.BeginLoad();
        var complete = Complete();
        Assert.True(page.Accept(load, complete));

        Assert.False(page.Accept(load, Partial()));

        Assert.Same(complete, page.Snapshot);
        Assert.True(page.IsComplete);
    }

    [Fact]
    public void BeginLoad_AfterComplete_ResetsToPendingSoReportIsDisabled()
    {
        var page = new SpecificationPage();
        Assert.True(page.Accept(page.BeginLoad(), Complete()));

        page.BeginLoad();

        Assert.False(page.IsComplete);
        Assert.Equal(ReadingState.Pending, page.Snapshot.Processor.State);
    }
}
