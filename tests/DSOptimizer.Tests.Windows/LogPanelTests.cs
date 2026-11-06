using System.Linq;
using System.Threading;
using System.Windows.Threading;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Gui;
using DSOptimizer.Tests.Windows.Support;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Dziennik zwinięty do jednego paska: start zwinięty, nigdy nie rozwija się sam (także przy błędzie), pasek pokazuje
/// ostatnią linię w jednej linii i poziom licznika. Stan nie jest zapamiętywany.
/// </summary>
[Trait("Category", "Windows")]
public class LogPanelTests
{
    [Fact]
    public void Log_StartsCollapsed()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);

            Assert.False(vm.IsLogExpanded);
            Assert.Equal(string.Empty, vm.LastLogText);
            Assert.Equal(GuiLogLevel.Info, vm.LogSummaryLevel);
        });
    }

    [Fact]
    public void ErrorLine_KeepsLogCollapsed_UpdatesLastLineAndLevel()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            UiLogSink? sink = null;
            var vm = MainViewModelTests.Create(temp, sinkReceiver: s => sink = s);

            sink!.Add(GuiLogLevel.Error, "x");
            Pump();

            Assert.False(vm.IsLogExpanded);
            Assert.EndsWith("x", vm.LastLogText);
            Assert.Equal(GuiLogLevel.Error, vm.LogSummaryLevel);
        });
    }

    [Fact]
    public void WarningOnly_LevelWarning()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            UiLogSink? sink = null;
            var vm = MainViewModelTests.Create(temp, sinkReceiver: s => sink = s);

            sink!.Add(GuiLogLevel.Warning, "uwaga");
            sink.Add(GuiLogLevel.Info, "info");
            Pump();
            Assert.Equal(GuiLogLevel.Warning, vm.LogSummaryLevel);

            sink.Add(GuiLogLevel.Error, "błąd");
            Pump();
            Assert.Equal(GuiLogLevel.Error, vm.LogSummaryLevel);
        });
    }

    [Fact]
    public void ToggleLog_FlipsState()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var vm = MainViewModelTests.Create(temp);

            vm.ToggleLogCommand.Execute(null);
            Assert.True(vm.IsLogExpanded);
            vm.ToggleLogCommand.Execute(null);
            Assert.False(vm.IsLogExpanded);
        });
    }

    [Fact]
    public void LastLogText_MultilineBecomesOneLine()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            UiLogSink? sink = null;
            var vm = MainViewModelTests.Create(temp, sinkReceiver: s => sink = s);

            sink!.Add(GuiLogLevel.Error, "pierwsza\r\ndruga\n" + new string('z', 600));
            Pump();

            Assert.DoesNotContain("\n", vm.LastLogText);
            Assert.DoesNotContain("\r", vm.LastLogText);
            Assert.Contains("pierwsza | druga | ", vm.LastLogText);
            Assert.EndsWith("…", vm.LastLogText);
            Assert.Contains("pierwsza\r\ndruga", vm.LastLogToolTip);
            Assert.False(vm.IsLogExpanded);
        });
    }

    [Fact]
    public void LastLogText_LineWithPath_ShowsTextAndPath()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            UiLogSink? sink = null;
            var vm = MainViewModelTests.Create(temp, sinkReceiver: s => sink = s);
            const string path = @"C:\Windows\Temp\raport.txt";

            sink!.Add(GuiLogLevel.Info, "Zapisano raport: " + path, path);
            Pump();

            Assert.EndsWith("Zapisano raport: " + path, vm.LastLogText);
            Assert.Equal("Zapisano raport: " + path, vm.LastLogToolTip);
        });
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new System.Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
