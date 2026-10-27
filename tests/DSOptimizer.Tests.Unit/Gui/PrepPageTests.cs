using System.Collections.Generic;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Core.Prep;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public sealed class PrepPageTests
{
    private static PrepSnapshot Partial() => new() { Activation = Reading<ActivationInfo>.Done(new ActivationInfo(1, "Windows 11 Pro")) };

    private static PrepSnapshot Complete() => new()
    {
        Board = Reading<BoardInfo>.Failed,
        Product = Reading<PrepProduct>.Failed,
        BitLocker = Reading<IReadOnlyList<BitLockerVolume>>.Failed,
        Activation = Reading<ActivationInfo>.Done(new ActivationInfo(1, "Windows 11 Pro")),
        Graphics = Reading<IReadOnlyList<PrepGpu>>.Failed,
        Devices = Reading<IReadOnlyList<ProblemDevice>>.Failed,
    };

    [Fact]
    public void New_NothingRead_AllPending()
    {
        var page = new PrepPage();

        Assert.False(page.Snapshot.IsComplete);
        Assert.Equal(ReadingState.Pending, page.Snapshot.Activation.State);
    }

    [Fact]
    public void Accept_CurrentLoad_Replaces()
    {
        var page = new PrepPage();
        var load = page.BeginLoad();
        var partial = Partial();

        Assert.True(page.Accept(load, partial));
        Assert.Same(partial, page.Snapshot);

        var complete = Complete();
        Assert.True(page.Accept(load, complete));
        Assert.Same(complete, page.Snapshot);
    }

    [Fact]
    public void Accept_StaleLoad_Rejected()
    {
        var page = new PrepPage();
        var first = page.BeginLoad();
        var second = page.BeginLoad();
        var current = Partial();
        Assert.True(page.Accept(second, current));

        Assert.False(page.Accept(first, Partial()));
        Assert.False(page.Accept(first, Complete()));

        Assert.Same(current, page.Snapshot);
    }

    [Fact]
    public void Accept_AfterCompleteSnapshot_LateUpdateOfSameLoadIgnored()
    {
        var page = new PrepPage();
        var load = page.BeginLoad();
        var complete = Complete();
        Assert.True(page.Accept(load, complete));

        Assert.False(page.Accept(load, Partial()));

        Assert.Same(complete, page.Snapshot);
    }

    [Fact]
    public void BeginLoad_AfterComplete_ResetsToPending()
    {
        var page = new PrepPage();
        Assert.True(page.Accept(page.BeginLoad(), Complete()));

        page.BeginLoad();

        Assert.False(page.Snapshot.IsComplete);
        Assert.Equal(ReadingState.Pending, page.Snapshot.Activation.State);
    }
}
