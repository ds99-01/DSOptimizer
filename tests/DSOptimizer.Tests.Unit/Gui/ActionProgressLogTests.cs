using System;
using System.Collections.Generic;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class ActionProgressLogTests
{
    private const string Title = "Sprawdź i napraw pliki systemowe";

    private static List<string> Feed(ActionProgressLog log, params ActionProgress[] events)
    {
        var lines = new List<string>();
        foreach (var e in events)
        {
            lines.AddRange(log.OnProgress(e));
        }

        return lines;
    }

    [Fact]
    public void StageChange_OneLinePerStage()
    {
        var lines = Feed(new ActionProgressLog(Title),
            new ActionProgress("DISM", 0, 0, 2),
            new ActionProgress("DISM", 3, 0, 2),
            new ActionProgress("SFC", 0, 1, 2));

        Assert.Equal(new[] { $"{Title}: DISM", $"{Title}: SFC" }, lines);
    }

    [Fact]
    public void Tens_OneLineAfterEachTenCrossed()
    {
        var lines = Feed(new ActionProgressLog(Title),
            new ActionProgress("DISM", 9, 0, 2),
            new ActionProgress("DISM", 10, 0, 2),
            new ActionProgress("DISM", 15, 0, 2),
            new ActionProgress("DISM", 21, 0, 2),
            new ActionProgress("DISM", 100, 0, 2));

        Assert.Equal(new[] { $"{Title}: DISM", $"{Title}: DISM 10%", $"{Title}: DISM 20%", $"{Title}: DISM 100%" }, lines);
    }

    [Fact]
    public void Jump_OnlyHighestTenWithoutFlood()
    {
        var lines = Feed(new ActionProgressLog(Title),
            new ActionProgress("DISM", 2, 0, 2),
            new ActionProgress("DISM", 47, 0, 2));

        Assert.Equal(new[] { $"{Title}: DISM", $"{Title}: DISM 40%" }, lines);
    }

    [Fact]
    public void Drop_NoLine_AndClimbBackNoRepeat()
    {
        var lines = Feed(new ActionProgressLog(Title),
            new ActionProgress("DISM", 35, 0, 2),
            new ActionProgress("DISM", 5, 0, 2),
            new ActionProgress("DISM", 32, 0, 2),
            new ActionProgress("DISM", 41, 0, 2));

        Assert.Equal(new[] { $"{Title}: DISM", $"{Title}: DISM 30%", $"{Title}: DISM 40%" }, lines);
    }

    [Fact]
    public void SameLineNeverTwice_EvenWhenStageReturns()
    {
        var lines = Feed(new ActionProgressLog(Title),
            new ActionProgress("DISM", 10, 0, 3),
            new ActionProgress("SFC", 10, 1, 3),
            new ActionProgress("DISM", 10, 2, 3));

        Assert.Equal(new[] { $"{Title}: DISM", $"{Title}: DISM 10%", $"{Title}: SFC", $"{Title}: SFC 10%" }, lines);
    }

    [Fact]
    public void IndeterminatePercent_OnlyStageLines()
    {
        var lines = Feed(new ActionProgressLog(Title),
            new ActionProgress("Pliki tymczasowe", null, 0, 2),
            new ActionProgress("Pliki tymczasowe", null, 0, 2),
            new ActionProgress("Miniatury", null, 1, 2));

        Assert.Equal(new[] { $"{Title}: Pliki tymczasowe", $"{Title}: Miniatury" }, lines);
    }

    [Fact]
    public void PercentAbove100_CountsAs100()
    {
        var lines = Feed(new ActionProgressLog(Title), new ActionProgress("DISM", 140, 0, 1));

        Assert.Equal(new[] { $"{Title}: DISM", $"{Title}: DISM 100%" }, lines);
    }

    [Fact]
    public void Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ActionProgressLog(Title).OnProgress(null!));
}
