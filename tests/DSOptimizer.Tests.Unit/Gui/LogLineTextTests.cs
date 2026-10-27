using System;
using DSOptimizer.Core.Gui;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public class LogLineTextTests
{
    private const string Root = @"C:\ProgramData\DSOptimizer";

    private static readonly DateTimeOffset At = new(new DateTime(2026, 10, 20, 10, 41, 52, DateTimeKind.Local));

    [Fact]
    public void Time_IsLocalHoursMinutesSeconds()
    {
        Assert.Equal("10:41:52", LogLineText.Time(At));
    }

    [Theory]
    [InlineData(GuiLogLevel.Info, "10:41:52 [Info] Tekst")]
    [InlineData(GuiLogLevel.Warning, "10:41:52 [Ostrzeżenie] Tekst")]
    [InlineData(GuiLogLevel.Error, "10:41:52 [Błąd] Tekst")]
    public void ForClipboard_Line_HasTimeLevelAndText(GuiLogLevel level, string expected)
    {
        Assert.Equal(expected, LogLineText.ForClipboard(new GuiLogLine(At, level, "Tekst")));
    }

    [Fact]
    public void ForClipboard_Lines_JoinedWithNewLine()
    {
        var lines = new[]
        {
            new GuiLogLine(At, GuiLogLevel.Info, "Pierwsza"),
            new GuiLogLine(At.AddSeconds(1), GuiLogLevel.Error, "Druga"),
        };

        Assert.Equal("10:41:52 [Info] Pierwsza" + Environment.NewLine + "10:41:53 [Błąd] Druga", LogLineText.ForClipboard(lines));
    }

    [Fact]
    public void ForClipboard_NoLines_Empty()
    {
        Assert.Equal(string.Empty, LogLineText.ForClipboard(Array.Empty<GuiLogLine>()));
    }

    [Fact]
    public void Split_NoPath_WholeTextBefore()
    {
        var parts = LogLineText.Split(new GuiLogLine(At, GuiLogLevel.Info, "Zastosowano"));

        Assert.Equal(new LogLineParts("Zastosowano", null, string.Empty), parts);
    }

    [Fact]
    public void Split_PathAtEnd_TextBeforeKeepsSeparator()
    {
        var parts = LogLineText.Split(new GuiLogLine(At, GuiLogLevel.Info, @"Zapisano raport: E:\logs\r.txt", @"E:\logs\r.txt"));

        Assert.Equal(new LogLineParts("Zapisano raport: ", @"E:\logs\r.txt", string.Empty), parts);
    }

    [Fact]
    public void Split_PathInMiddle_TextAfterKept()
    {
        var parts = LogLineText.Split(new GuiLogLine(At, GuiLogLevel.Info, @"Plik E:\r.txt gotowy", @"E:\r.txt"));

        Assert.Equal(new LogLineParts("Plik ", @"E:\r.txt", " gotowy"), parts);
    }

    [Fact]
    public void Split_PathTwiceInText_LastOccurrenceIsPath()
    {
        var parts = LogLineText.Split(new GuiLogLine(At, GuiLogLevel.Info, @"E:\r.txt -> E:\r.txt", @"E:\r.txt"));

        Assert.Equal(new LogLineParts(@"E:\r.txt -> ", @"E:\r.txt", string.Empty), parts);
    }

    [Fact]
    public void Split_PathNotInText_PathAppendedAfterSpace()
    {
        var parts = LogLineText.Split(new GuiLogLine(At, GuiLogLevel.Info, "Zapisano raport", @"E:\r.txt"));

        Assert.Equal(new LogLineParts("Zapisano raport ", @"E:\r.txt", string.Empty), parts);
    }

    [Fact]
    public void Split_EmptyPath_TreatedAsNoPath()
    {
        var parts = LogLineText.Split(new GuiLogLine(At, GuiLogLevel.Info, "Tekst", string.Empty));

        Assert.Equal(new LogLineParts("Tekst", null, string.Empty), parts);
    }

    [Fact]
    public void PathMode_NoPath_None()
    {
        Assert.Equal(LogPathMode.None, LogLineText.PathMode(null, Root, _ => true));
    }

    [Fact]
    public void PathMode_OutsideProtectedRootAndExists_Reveal()
    {
        Assert.Equal(LogPathMode.Reveal, LogLineText.PathMode(@"E:\logs\r.txt", Root, _ => true));
    }

    [Fact]
    public void PathMode_OutsideProtectedRootMissing_RevealMissing()
    {
        Assert.Equal(LogPathMode.RevealMissing, LogLineText.PathMode(@"E:\logs\r.txt", Root, _ => false));
    }

    [Fact]
    public void PathMode_InsideProtectedRoot_CopyOnlyWithoutCheckingFile()
    {
        var checkedFile = false;

        var mode = LogLineText.PathMode(@"C:\ProgramData\DSOptimizer\reports\r.txt", Root, _ => checkedFile = true);

        Assert.Equal(LogPathMode.CopyOnly, mode);
        Assert.False(checkedFile);
    }

    [Fact]
    public void PathMode_RelativePath_CopyOnly()
    {
        Assert.Equal(LogPathMode.CopyOnly, LogLineText.PathMode(@"reports\r.txt", Root, _ => true));
    }

    [Fact]
    public void PathMode_NullFileExists_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LogLineText.PathMode(@"E:\r.txt", Root, null!));
    }

    [Theory]
    [InlineData(true, "Eksplorator niedostępny. Skopiowano ścieżkę.")]
    [InlineData(false, "Eksplorator niedostępny.")]
    public void RevealFailed_AddsCopiedNoteOnlyWhenCopied(bool copied, string expected)
    {
        Assert.Equal(expected, LogLineText.RevealFailed("Eksplorator niedostępny.", copied));
    }

    [Fact]
    public void RevealFailed_MessageWithoutFinalDot_DotAddedBeforeNote()
    {
        Assert.Equal(@"Plik nie istnieje: E:\r.txt. Skopiowano ścieżkę.", LogLineText.RevealFailed(@"Plik nie istnieje: E:\r.txt", true));
    }

    [Fact]
    public void ForClipboard_NullLine_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LogLineText.ForClipboard((GuiLogLine)null!));
    }

    [Fact]
    public void Split_NullLine_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LogLineText.Split(null!));
    }
}
