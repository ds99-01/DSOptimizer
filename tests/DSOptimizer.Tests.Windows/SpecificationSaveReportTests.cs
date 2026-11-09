using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DSOptimizer.App.Gui;
using DSOptimizer.App.ViewModels;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Hardware;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using DSOptimizer.Windows.Hardware;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// „Zapisz raport” na stronie „Specyfikacja”: ścieżka z okna zapisu (fałszywe okno, prawdziwe nigdy się nie otwiera),
/// zapis w katalogu tymczasowym, odmowa dla folderu programu. Sprzęt nie jest czytany (bez <c>LoadAsync</c>).
/// </summary>
[Trait("Category", "Windows")]
public class SpecificationSaveReportTests
{
    [Fact]
    public async Task SaveReport_Cancelled_NothingWrittenNoLine()
    {
        using var temp = new TempDirectory();
        var parts = Create(temp, null);

        await parts.Vm.SaveReportCommand.ExecuteAsync(null);

        Assert.Equal(new[] { SpecificationFormatter.FileName(parts.Clock.UtcNow.ToLocalTime().DateTime) }, parts.Dialog.Asked);
        Assert.Empty(parts.Panel);
        Assert.Empty(parts.Log.Lines);
    }

    [Fact]
    public async Task SaveReport_ChosenPath_WritesReportAndLinkLine()
    {
        using var temp = new TempDirectory();
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var target = Path.Combine(pulpit, "r.txt");
        var parts = Create(temp, target);

        await parts.Vm.SaveReportCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(target).Take(3).ToArray());
        Assert.Equal(new[] { (GuiLogLevel.Info, "Zapisano raport: " + target, (string?)target) }, parts.Panel);
    }

    [Fact]
    public async Task SaveReport_InsideProgramFolder_WarnsNothingCreated()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.AppRoot.FullPath, "r.txt");
        var parts = Create(temp, target);

        await parts.Vm.SaveReportCommand.ExecuteAsync(null);

        Assert.False(File.Exists(target));
        Assert.Empty(parts.Panel);
        Assert.Contains(
            ("WARN", SpecificationText.Component, "Nie zapisano raportu w " + target + ": " + ReportFileWriter.ProtectedLocationReason),
            parts.Log.Lines);
    }

    private static Parts Create(TempDirectory temp, string? answer)
    {
        var log = new CollectingLog();
        var clock = new FixedClock();
        var dialog = new FakeSaveFileDialog(answer);
        var panel = new List<(GuiLogLevel, string, string?)>();
        var shell = new ShellActions(new ShellFileRevealer(new WindowsExplorerProcessQuery(), temp.AppRoot.FullPath), null, log);
        var vm = new SpecificationViewModel(
            new SpecificationLoader(new WindowsHardwareReader(), log, SpecificationLoader.DefaultTimeout),
            new ReportFileWriter(temp.AppRoot, null),
            dialog,
            log,
            clock,
            "TEST",
            (level, text, path) => panel.Add((level, text, path)),
            shell);
        return new Parts(vm, dialog, clock, panel, log);
    }

    private sealed record Parts(
        SpecificationViewModel Vm, FakeSaveFileDialog Dialog, FixedClock Clock, List<(GuiLogLevel, string, string?)> Panel, CollectingLog Log);

    private sealed class CollectingLog : ILog
    {
        public List<(string Level, string Component, string Message)> Lines { get; } = new();

        public void Info(string component, string message) => Lines.Add(("INFO", component, message));

        public void Warn(string component, string message) => Lines.Add(("WARN", component, message));

        public void Fail(string component, string message, Exception? exception = null) => Lines.Add(("FAIL", component, message));
    }
}
