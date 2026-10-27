using System.Collections.Generic;
using DSOptimizer.Core.Gui;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Gui;

public sealed class SpecificationTextTests
{
    private const char Nbsp = '\u00A0';

    [Fact]
    public void BreakOnlyAfterCommas_SpacesInsideMembersBecomeNonBreaking()
    {
        var text = SpecificationText.BreakOnlyAfterCommas("Windows 11 Pro 26H2, build 26300.9457, zainstalowany 2026-09-02");

        Assert.Equal($"Windows{Nbsp}11{Nbsp}Pro{Nbsp}26H2, build{Nbsp}26300.9457, zainstalowany{Nbsp}2026-09-02", text);
    }

    [Fact]
    public void BreakOnlyAfterCommas_MemberLongerThanLimit_KeepsNormalSpacesSoItWrapsInsteadOfClipping()
    {
        var text = SpecificationText.BreakOnlyAfterCommas("Micro-Star International Co., Ltd. MAG B650 TOMAHAWK WIFI (MS-7D75), BIOS 1.F0 (2025-03-14), UEFI");

        Assert.Equal($"Micro-Star International Co., Ltd. MAG B650 TOMAHAWK WIFI (MS-7D75), BIOS{Nbsp}1.F0{Nbsp}(2025-03-14), UEFI", text);
    }

    [Fact]
    public void BreakOnlyAfterCommas_MemberAtLimit_StaysTogether()
    {
        var member = new string('x', SpecificationText.MaxUnbreakableLength - 2) + " y";

        Assert.Equal(member.Replace(' ', Nbsp), SpecificationText.BreakOnlyAfterCommas(member));
        Assert.Equal(member + "z", SpecificationText.BreakOnlyAfterCommas(member + "z"));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("brak danych", "brak\u00A0danych")]
    [InlineData("Włączony", "Włączony")]
    [InlineData("2.0, włączony", "2.0, włączony")]
    [InlineData("a,b c", "a,b\u00A0c")]
    public void BreakOnlyAfterCommas_EdgeCases(string value, string expected)
        => Assert.Equal(expected, SpecificationText.BreakOnlyAfterCommas(value));

    [Fact]
    public void LogSaveResult_Saved_InfoWithPath()
    {
        var log = new ListLog();

        SpecificationText.LogSaveResult(log, @"C:\ProgramData\DSOptimizer\reports\specyfikacja-2026-10-19_1042.txt", true, null);

        Assert.Equal(new[] { @"INFO [Specyfikacja] Zapisano raport: C:\ProgramData\DSOptimizer\reports\specyfikacja-2026-10-19_1042.txt" }, log.Lines);
    }

    [Fact]
    public void LogSaveResult_NotSaved_WarnWithPathAndReason()
    {
        var log = new ListLog();

        SpecificationText.LogSaveResult(log, @"E:\logs\KLIENT-PC\specyfikacja-2026-10-19_1042.txt", false, "Urządzenie nie jest gotowe.");

        Assert.Equal(new[] { @"WARN [Specyfikacja] Nie zapisano raportu w E:\logs\KLIENT-PC\specyfikacja-2026-10-19_1042.txt: Urządzenie nie jest gotowe." }, log.Lines);
    }

    [Fact]
    public void LogSaveResult_Saved_AlsoShowsInfoLineInPanel()
    {
        var log = new ListLog();
        var panel = new List<(GuiLogLevel Level, string Text)>();

        SpecificationText.LogSaveResult(log, @"C:\ProgramData\DSOptimizer\reports\a.txt", true, null, (level, text, _) => panel.Add((level, text)));

        Assert.Equal(new[] { @"INFO [Specyfikacja] Zapisano raport: C:\ProgramData\DSOptimizer\reports\a.txt" }, log.Lines);
        Assert.Equal(new[] { (GuiLogLevel.Info, @"Zapisano raport: C:\ProgramData\DSOptimizer\reports\a.txt") }, panel);
    }

    [Fact]
    public void LogSaveResult_NotSaved_WarnOnlyInLogNotDuplicatedInPanel()
    {
        var log = new ListLog();
        var panel = new List<(GuiLogLevel Level, string Text)>();

        SpecificationText.LogSaveResult(log, @"E:\x.txt", false, "brak", (level, text, _) => panel.Add((level, text)));

        Assert.Single(log.Lines);
        Assert.Empty(panel);
    }

    [Fact]
    public void LogSaveResult_Saved_PassesPathToPanel()
    {
        var log = new ListLog();
        var panel = new List<(GuiLogLevel Level, string Text, string? Path)>();

        SpecificationText.LogSaveResult(log, @"E:\logs\X.txt", true, null, (level, text, path) => panel.Add((level, text, path)));

        Assert.Equal(new[] { (GuiLogLevel.Info, @"Zapisano raport: E:\logs\X.txt", (string?)@"E:\logs\X.txt") }, panel);
    }

    [Fact]
    public void LogSaveResult_Failed_NoPathForPanel()
    {
        var log = new ListLog();
        var panel = new List<(GuiLogLevel Level, string Text, string? Path)>();

        SpecificationText.LogSaveResult(log, @"E:\x.txt", false, "brak", (level, text, path) => panel.Add((level, text, path)));

        Assert.Empty(panel);
    }

    [Fact]
    public void LogCopied_InfoInLogAndPanel()
    {
        var log = new ListLog();
        var panel = new List<(GuiLogLevel Level, string Text)>();

        SpecificationText.LogCopied(log, (level, text, _) => panel.Add((level, text)));

        Assert.Equal(new[] { "INFO [Specyfikacja] Skopiowano specyfikację do schowka." }, log.Lines);
        Assert.Equal(new[] { (GuiLogLevel.Info, "Skopiowano specyfikację do schowka.") }, panel);
    }

    [Fact]
    public void CopyTexts_Pinned()
    {
        Assert.Equal("Skopiowano specyfikację do schowka.", SpecificationText.CopiedMessage);
        Assert.Equal("Nie skopiowano specyfikacji do schowka: schowek zajęty", SpecificationText.CopyFailedMessage("schowek zajęty"));
        Assert.Equal("Specyfikacja", SpecificationText.Component);
    }
}
