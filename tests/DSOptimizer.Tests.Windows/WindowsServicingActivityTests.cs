using System.Diagnostics;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>Strażnik serwisowania na prawdziwej liście procesów (bez uruchamiania DISM/SFC/cleanmgr – nazwy podmienione).</summary>
[Trait("Category", "Windows")]
public class WindowsServicingActivityTests
{
    [Fact]
    public void ProcessNames_AreMeasuredList()
        => Assert.Equal(new[] { "dism", "sfc", "cleanmgr" }, WindowsServicingActivity.ProcessNames);

    [Fact]
    public void ActiveOperation_RunningProcessOnList_ReturnsExeName()
    {
        using var self = Process.GetCurrentProcess();
        var activity = new WindowsServicingActivity(new[] { "dso-nie-ma-takiego", self.ProcessName });

        Assert.Equal(self.ProcessName + ".exe", activity.ActiveOperation());
    }

    [Fact]
    public void ActiveOperation_NothingRunning_IsNull()
        => Assert.Null(new WindowsServicingActivity(new[] { "dso-nie-ma-takiego", "dso-tez-nie" }).ActiveOperation());
}
