using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using DSOptimizer.App.Gui;
using DSOptimizer.Core.Gui;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Linia dziennika ze ścieżką w folderze programu: istniejący plik ma tylko „Zapisz kopię…” (bez „Kopiuj ścieżkę”), brakujący
/// plik albo folder to sam tekst. Okno zapisu jest fałszywe.
/// </summary>
[Trait("Category", "Windows")]
public class LogViewTests
{
    [Fact]
    public void ProtectedExistingFile_SaveCopyButtonOnly()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var parts = Create(temp, createFile: true, new FakeSaveFileDialog(null));

            var button = Assert.Single(Buttons(parts.Box));
            Assert.Equal(LogLineText.SaveCopyLabel, AutomationProperties.GetName(button));
            Assert.DoesNotContain(Buttons(parts.Box), b => AutomationProperties.GetName(b) == "Kopiuj ścieżkę");
        });
    }

    [Fact]
    public void ProtectedMissingFile_PlainTextNoButtons()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var parts = Create(temp, createFile: false, new FakeSaveFileDialog(null));

            Assert.Empty(Buttons(parts.Box));
            var paragraph = Assert.Single(parts.Box.Document.Blocks.OfType<Paragraph>());
            Assert.Contains(parts.Path, new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SaveCopyClick_AsksForSourceFileName()
    {
        MainViewModelTests.RunOnSta(() =>
        {
            using var temp = new TempDirectory();
            var dialog = new FakeSaveFileDialog(null);
            var parts = Create(temp, createFile: true, dialog);

            Assert.Single(Buttons(parts.Box)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            MainViewModelTests.PumpUntil(() => dialog.Asked.Count > 0, "pytanie o miejsce zapisu");

            Assert.Equal(new[] { "VideoLAN.VLC-1.log" }, dialog.Asked);
        });
    }

    private static Parts Create(TempDirectory temp, bool createFile, FakeSaveFileDialog dialog)
    {
        var path = Path.Combine(temp.AppRoot.FullPath, @"logs\winget\VideoLAN.VLC-1.log");
        if (createFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "log winget");
        }

        var log = new NullLog();
        var box = new RichTextBox();
        var lines = new ObservableCollection<GuiLogLine>
        {
            new(DateTimeOffset.Now, GuiLogLevel.Error, "VLC: Nie wykonano. Log winget: " + path, path),
        };
        var shell = new ShellActions(new ShellFileRevealer(new WindowsExplorerProcessQuery(), temp.AppRoot.FullPath), null, log);
        var saver = new LogCopySaver(dialog, new ReportFileWriter(temp.AppRoot, null), log, (_, _, _) => { });
        _ = new LogView(box, lines, shell, temp.AppRoot.FullPath, log, saver);
        return new Parts(box, path);
    }

    /// <summary>Przyciski w tekście dziennika: rekurencja po akapitach i spanach do <see cref="InlineUIContainer"/>.</summary>
    private static List<Button> Buttons(RichTextBox box)
    {
        var found = new List<Button>();
        foreach (var paragraph in box.Document.Blocks.OfType<Paragraph>())
        {
            Collect(paragraph.Inlines, found);
        }

        return found;
    }

    private static void Collect(InlineCollection inlines, List<Button> found)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                Collect(span.Inlines, found);
            }
            else if (inline is InlineUIContainer { Child: Button button })
            {
                found.Add(button);
            }
        }
    }

    private sealed record Parts(RichTextBox Box, string Path);
}
