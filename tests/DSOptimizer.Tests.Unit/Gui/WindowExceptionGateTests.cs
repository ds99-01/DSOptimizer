using System;
using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class WindowExceptionGateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Next_FiveInWindow_Panel()
    {
        var gate = new WindowExceptionGate();
        for (var i = 0; i < WindowExceptionGate.Limit; i++)
        {
            Assert.Equal(WindowExceptionRoute.Panel, gate.Next(T0.AddMilliseconds(i * 100)));
        }
    }

    [Fact]
    public void Next_Sixth_NoticeOnce_ThenFileOnly()
    {
        var gate = new WindowExceptionGate();
        for (var i = 0; i < 5; i++)
        {
            gate.Next(T0.AddMilliseconds(i));
        }

        Assert.Equal(WindowExceptionRoute.FileOnlyWithNotice, gate.Next(T0.AddMilliseconds(5)));
        Assert.Equal(WindowExceptionRoute.FileOnly, gate.Next(T0.AddMilliseconds(6)));
        Assert.Equal(WindowExceptionRoute.FileOnly, gate.Next(T0.AddSeconds(5)));
    }

    [Fact]
    public void Next_QuietPeriod_Resets()
    {
        var gate = new WindowExceptionGate();
        for (var i = 0; i < 7; i++)
        {
            gate.Next(T0.AddMilliseconds(i));
        }

        var later = T0.AddSeconds(10.006);
        Assert.Equal(WindowExceptionRoute.Panel, gate.Next(later));
        for (var i = 1; i < 5; i++)
        {
            Assert.Equal(WindowExceptionRoute.Panel, gate.Next(later.AddMilliseconds(i)));
        }

        Assert.Equal(WindowExceptionRoute.FileOnlyWithNotice, gate.Next(later.AddMilliseconds(5)));
    }

    [Fact]
    public void Next_SlidingWindow_OldDropOut()
    {
        var gate = new WindowExceptionGate();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(WindowExceptionRoute.Panel, gate.Next(T0.AddSeconds(i * 3)));
        }

        // Przy 15 s wypadły wpisy z 0 s i 3 s (starsze niż 10 s) – zostają 6, 9, 12 + bieżący = 4.
        Assert.Equal(WindowExceptionRoute.Panel, gate.Next(T0.AddSeconds(15)));
    }
}
