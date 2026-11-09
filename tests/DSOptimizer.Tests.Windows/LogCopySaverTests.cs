using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DSOptimizer.App.Gui;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// „Zapisz kopię…” w dzienniku: plik z folderu programu kopiowany w miejsce z okna zapisu (fałszywe okno, prawdziwe nigdy się
/// nie otwiera), odmowa dla folderu programu tylko w pliku logu.
/// </summary>
[Trait("Category", "Windows")]
public class LogCopySaverTests
{
    [Fact]
    public async Task Cancelled_NothingLogged()
    {
        using var temp = new TempDirectory();
        var parts = Create(temp, null);

        await parts.Saver.SaveAsync(parts.Source);

        Assert.Empty(parts.Panel);
        Assert.Empty(parts.Log.Lines);
    }

    [Fact]
    public async Task AsksWithSourceFileName()
    {
        using var temp = new TempDirectory();
        var parts = Create(temp, null);

        await parts.Saver.SaveAsync(parts.Source);

        Assert.Equal(new[] { "x.log" }, parts.Dialog.Asked);
    }

    [Fact]
    public async Task Saved_InfoLineWithLink()
    {
        using var temp = new TempDirectory();
        var pulpit = temp.Sub("pulpit");
        Directory.CreateDirectory(pulpit);
        var target = Path.Combine(pulpit, "x.log");
        var parts = Create(temp, target);

        await parts.Saver.SaveAsync(parts.Source);

        Assert.Equal(new[] { (GuiLogLevel.Info, LogLineText.CopySaved(target), (string?)target) }, parts.Panel);
        Assert.Equal(File.ReadAllBytes(parts.Source), File.ReadAllBytes(target));
    }

    [Fact]
    public async Task InsideProgramFolder_WarnOnly()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.AppRoot.FullPath, "kopia.log");
        var parts = Create(temp, target);

        await parts.Saver.SaveAsync(parts.Source);

        Assert.Empty(parts.Panel);
        Assert.Contains(("WARN", "Gui", LogLineText.CopyNotSaved(target, ReportFileWriter.ProtectedLocationReason)), parts.Log.Lines);
        Assert.False(File.Exists(target));
    }

    private static Parts Create(TempDirectory temp, string? answer)
    {
        var source = temp.Sub(@"app\logs\x.log");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "log winget");
        var log = new CollectingLog();
        var dialog = new FakeSaveFileDialog(answer);
        var panel = new List<(GuiLogLevel, string, string?)>();
        var saver = new LogCopySaver(dialog, new ReportFileWriter(temp.AppRoot, null), log, (level, text, path) => panel.Add((level, text, path)));
        return new Parts(saver, source, dialog, panel, log);
    }

    private sealed record Parts(
        LogCopySaver Saver, string Source, FakeSaveFileDialog Dialog, List<(GuiLogLevel, string, string?)> Panel, CollectingLog Log);

    private sealed class CollectingLog : ILog
    {
        public List<(string Level, string Component, string Message)> Lines { get; } = new();

        public void Info(string component, string message) => Lines.Add(("INFO", component, message));

        public void Warn(string component, string message) => Lines.Add(("WARN", component, message));

        public void Fail(string component, string message, Exception? exception = null) => Lines.Add(("FAIL", component, message));
    }
}
