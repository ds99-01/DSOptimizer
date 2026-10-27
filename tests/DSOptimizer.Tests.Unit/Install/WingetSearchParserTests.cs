using System;
using System.Linq;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class WingetSearchParserTests
{
    [Fact]
    public void Notepad_Sample_AllRowsWithColumnsFromHeader()
    {
        // Pomiar: 36 wierszy (--count 50), kolumna Match bywa pusta, nazwy ze spacjami.
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("spike-net-search-notepad.bin"));

        Assert.Equal(36, rows.Count);
        Assert.Equal(new WingetSearchRow("AkelPad", "AkelPad.AkelPad", "4.9.9", true), rows[0]);
        Assert.Equal(new WingetSearchRow("NoteTab Light", "FookesHolding.NoteTabLight", "7.2", true), rows[6]);
        Assert.Equal(new WingetSearchRow("Notepad++", "Notepad++.Notepad++", "8.9.8.1", true), rows.Single(r => r.Name == "Notepad++"));
        Assert.Equal(new WingetSearchRow("Programmer's Notepad", "SimonSteele.ProgrammersNotepad", "2.4.2", true), rows.Single(r => r.Name == "Programmer's Notepad"));
        Assert.Equal(new WingetSearchRow("Markpad", "alecdotdev.Markpad", "2.8.4", true), rows[rows.Count - 1]);
        Assert.All(rows, r => Assert.True(r.Installable, r.WingetId));
    }

    [Fact]
    public void Vlc_Sample_TenRows()
    {
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("spike-net-search-vlc.bin"));

        Assert.Equal(10, rows.Count);
        Assert.Equal(new WingetSearchRow("VLC media player", "VideoLAN.VLC", "3.0.24", true), rows[0]);
        Assert.Equal(new WingetSearchRow("VLC media player (Nightly)", "VideoLAN.VLC.Nightly", "4.0.0.0-nightly20260509", true), rows[8]);
    }

    [Fact]
    public void List_Sample_SpinnerBeforeHeaderSkipped()
    {
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("spike-net-list-7zip-after.bin"));

        Assert.Equal(new WingetSearchRow("7-Zip 26.05 (x64)", "7zip.7zip", "26.05", true), Assert.Single(rows));
    }

    [Fact]
    public void NoHeader_Empty()
    {
        Assert.Empty(WingetSearchParser.Parse(InstallSamples.Lines("spike-off-search-vlc.bin")));
        Assert.Empty(WingetSearchParser.Parse(Array.Empty<string>()));
        Assert.Empty(WingetSearchParser.Parse(new[] { "Name Id", "---------" }));
    }

    [Fact]
    public void TruncatedId_NotInstallable_Synthetic()
    {
        // Review Focus 1: Id obcięte „…” albo puste = wiersz widoczny, ale nie do instalacji.
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("synthetic-search-truncated.txt"));

        Assert.Equal(4, rows.Count);
        Assert.True(rows[0].Installable);
        Assert.Equal("Microsoft.VisualStudioCode.Ins…", rows[1].WingetId);
        Assert.False(rows[1].Installable);
        Assert.Equal(new WingetSearchRow("Krotka", string.Empty, "1.0", false), rows[3]);
    }

    [Fact]
    public void LongName_ColumnsFromHeader_Synthetic()
    {
        // Nagłówek po polsku (parser nie czyta słów) i nazwa wypełniająca kolumnę do samego Id, bez spacji między nimi. Kolumny
        // dzieli się nadal z nagłówka, ale prawdziwy winget zawsze stawia spację między kolumnami (wszystkie próbki e4c-*), więc
        // od M5 taki wiersz jest traktowany jak przesunięty: widoczny, bez instalacji (zachowawczo).
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("synthetic-search-truncated.txt"));

        Assert.Equal(new WingetSearchRow("Visual Studio Code", "Microsoft.VisualStudioCode", "1.95.0", true), rows[0]);
        Assert.Equal(new WingetSearchRow("Visual Studio Code - Insiders …", "Microsoft.VisualStudioCode.Ins…", "1.96.0", false), rows[1]);
        Assert.Equal(new WingetSearchRow("Bardzo dluga nazwa aplikacji XY…", "Vendor.LongApp", "2.0", false), rows[2]);
    }

    [Fact]
    public void WideCharsInName_NotInstallable_Synthetic()
    {
        // Ruling R8: winget wyrównuje kolumny po szerokości wyświetlania, parser po znakach UTF-16. Znak szeroki (CJK, pełna
        // szerokość) albo para zastępcza przed kolumną Id = wiersz zachowawczo nie do instalacji, bo Id może być ucięte z przodu
        // („腾讯QQ” przesuwa wiersz o 2 znaki: wycięte „ncent.QQ” wygląda na poprawny identyfikator).
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("synthetic-search-wide.txt"));

        Assert.Equal(4, rows.Count);
        Assert.Equal("ncent.QQ", rows[0].WingetId);
        Assert.False(rows[0].Installable);
        Assert.False(rows[1].Installable);
        Assert.Equal("Vendor.Emoji", rows[2].WingetId);
        Assert.False(rows[2].Installable);
        Assert.Equal(new WingetSearchRow("Plain App", "Vendor.Plain", "3.0", true), rows[3]);
    }

    [Theory]
    [InlineData('ᄀ')]
    [InlineData('ᅟ')]
    [InlineData('⺀')]
    [InlineData('꓏')]
    [InlineData('가')]
    [InlineData('힣')]
    [InlineData('豈')]
    [InlineData('﫿')]
    [InlineData('︰')]
    [InlineData('﹏')]
    [InlineData('＀')]
    [InlineData('｠')]
    [InlineData('￠')]
    [InlineData('￦')]
    public void WideCharRangeBounds_NotInstallable(char wide)
    {
        var rows = WingetSearchParser.Parse(new[] { "Name    Id", "----------", "A" + wide + "      Vendor.App" });

        Assert.False(Assert.Single(rows).Installable);
    }

    [Theory]
    [InlineData('ჿ')]
    [InlineData('ᅠ')]
    [InlineData('⹿')]
    [InlineData('ꓐ')]
    [InlineData('힤')]
    [InlineData('︯')]
    [InlineData('﹐')]
    [InlineData('｡')]
    [InlineData('￟')]
    [InlineData('￧')]
    [InlineData('ó')]
    public void NarrowCharsOutsideRanges_StayInstallable(char narrow)
    {
        var rows = WingetSearchParser.Parse(new[] { "Name    Id", "----------", "A" + narrow + "      Vendor.App" });

        Assert.Equal(new WingetSearchRow("A" + narrow, "Vendor.App", null, true), Assert.Single(rows));
    }

    [Fact]
    public void WideCharAfterIdColumnStart_DoesNotBlock()
    {
        var rows = WingetSearchParser.Parse(new[] { "Name    Id         Match", "----------", "App     Vendor.App Tag: 腾讯" });

        Assert.True(Assert.Single(rows).Installable);
    }

    // M5 (final review): winget wyrównuje kolumny po szerokości wyświetlania. Znak zerowej szerokości (łączący U+0301) przesuwa
    // wiersz w prawo o +1 jednostkę UTF-16, znak szeroki spoza zakresów R8 (emoji z BMP, U+2615) – w lewo. Strażnik: znak tuż
    // przed kolumną Id i tuż przed kolumną Version musi być biały.
    private const string ShiftHeader = "Name      Id          Version";

    [Fact]
    public void CombiningCharInName_IdFillsColumn_NotInstallable()
    {
        // Id (11 znaków) wypełnia kolumnę (12 = Id + spacja); po przesunięciu jego ostatni znak stoi tuż przed kolumną Version.
        var one = WingetSearchParser.Parse(new[] { ShiftHeader, "----------", "Café      Foo.Bar.Baz 1.0" });
        Assert.False(Assert.Single(one).Installable);

        // Dwa znaki łączące: ostatni znak Id przechodzi do kolumny Version, a obcięte „Foo.Bar.Ba” wygląda na poprawny Id.
        var two = Assert.Single(WingetSearchParser.Parse(new[] { ShiftHeader, "----------", "Café́      Foo.Bar.Baz 1.0" }));
        Assert.Equal("Foo.Bar.Ba", two.WingetId);
        Assert.True(WingetArguments.IsValidWingetId(two.WingetId));
        Assert.False(two.Installable);
    }

    [Fact]
    public void CombiningCharInName_ShortId_StaysInstallable()
    {
        // Id krótsze od kolumny: po przesunięciu o 1 jest całe, a przed kolumnami Id i Version stoją spacje.
        var rows = WingetSearchParser.Parse(new[] { ShiftHeader, "----------", "Café      Foo.Bar     1.0" });

        Assert.Equal(new WingetSearchRow("Café", "Foo.Bar", "1.0", true), Assert.Single(rows));
    }

    [Fact]
    public void BmpEmojiInName_NotInstallable()
    {
        // „☕” (U+2615) ma szerokość 2, a w UTF-16 jeden znak: wiersz krótszy o 1, Id ucięte z przodu („endor.Tea”). Wiersz bez
        // wersji – inaczej pierwszy znak wersji wpada do kolumny Id i Id ze spacją i tak odpada.
        var row = Assert.Single(WingetSearchParser.Parse(new[] { ShiftHeader, "----------", "Tea☕     Vendor.Tea" }));

        Assert.Equal("endor.Tea", row.WingetId);
        Assert.True(WingetArguments.IsValidWingetId(row.WingetId));
        Assert.False(row.Installable);
    }

    [Fact]
    public void HeaderWithTabSeparator_ColumnStartFound()
    {
        // Początek kolumny także po tabulatorze w nagłówku (char.IsWhiteSpace zamiast samej spacji).
        var rows = WingetSearchParser.Parse(new[] { "Name\tId", "----------", "Abcd\tVendor.Abc" });

        Assert.Equal(new WingetSearchRow("Abcd", "Vendor.Abc", null, true), Assert.Single(rows));
    }

    [Fact]
    public void TrailingTruncationNote_NotARow_Synthetic()
    {
        // Linia po tabeli zaczynająca się od „<” (np. „<additional entries truncated due to result limit>”) to komunikat, nie wiersz.
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("synthetic-search-note.txt"));

        Assert.Equal(new WingetSearchRow("Plain App", "Vendor.Plain", "3.0", true), Assert.Single(rows));
    }

    [Fact]
    public void ShortRowsAndBlankLines_Tolerated()
    {
        var rows = WingetSearchParser.Parse(new[]
        {
            "Name   Id",
            "  ",
            "----------",
            "Abc    Vendor.Abc",
            "   ",
            "Xy",
        });

        Assert.Equal(
            new[] { new WingetSearchRow("Abc", "Vendor.Abc", null, true), new WingetSearchRow("Xy", string.Empty, null, false) },
            rows);
    }

    [Fact]
    public void HeaderWithSingleColumn_Empty()
    {
        Assert.Empty(WingetSearchParser.Parse(new[] { "Name", "----------", "Abc" }));
    }

    [Fact]
    public void Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WingetSearchParser.Parse(null!));
    }

    // ---- Próbki e4c-* z pomiaru VM Etapu 4c (Home, winget v1.11.510) ----

    [Fact]
    public void E4c_CodecPack_FourRowsWithoutMatchColumn()
    {
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("e4c-search-codec-pack-home.bin"));

        Assert.Equal(
            new[]
            {
                new WingetSearchRow("K-Lite Codec Pack Basic", "CodecGuide.K-LiteCodecPack.Basic", "20.1.0", true),
                new WingetSearchRow("K-Lite Codec Pack Full", "CodecGuide.K-LiteCodecPack.Full", "20.1.0", true),
                new WingetSearchRow("K-Lite Mega Codec Pack", "CodecGuide.K-LiteCodecPack.Mega", "20.1.0", true),
                new WingetSearchRow("K-Lite Codec Pack Standard", "CodecGuide.K-LiteCodecPack.Standard", "20.1.0", true),
            },
            rows);
    }

    [Fact]
    public void E4c_Putty_MatchColumnAndLongIdFillingColumn()
    {
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("e4c-search-putty-match-home.bin"));

        Assert.Equal(10, rows.Count);
        Assert.Equal(new WingetSearchRow("PuTTY", "PuTTY.PuTTY", "0.85.0.0", true), rows[0]);
        Assert.Equal(new WingetSearchRow("Avash", "AdrienCros.Avash", "0.13.2", true), rows[1]);
        Assert.Equal(
            new WingetSearchRow("Input Typer for Command Palette", "CoreyHayward.InputTyperforCommandPalette", "0.0.2.0", true),
            rows[8]);
        Assert.Equal(new WingetSearchRow("anySCP", "macnev2013.anySCP", "0.11.1", true), rows[9]);
    }

    [Fact]
    public void E4c_Redistributable_16TruncatedNamesAnd1TruncatedId()
    {
        // Research: 17 wierszy, 16 obciętych nazw i 1 obcięty Id; obcięty Id = wiersz bez instalacji.
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("e4c-search-truncated-redistributable-home.bin"));

        Assert.Equal(17, rows.Count);
        Assert.Equal(16, rows.Count(r => r.Name.EndsWith("…", StringComparison.Ordinal)));
        var truncated = Assert.Single(rows, r => !r.Installable);
        Assert.Equal(new WingetSearchRow("Microsoft .NET Framework Version 1.1 Red…", "Microsoft.DotNet.Framework.Redistributab…", "1.1", false), truncated);
        Assert.Equal(new WingetSearchRow("Microsoft Visual C++ v14 Redistributable…", "Microsoft.VCRedist.2015+.x64", "14.51.36247.0", true), rows[6]);
        Assert.Equal(new WingetSearchRow("Visual C++ Redistributable AIO", "abbodi1406.vcredist", "0.104.0", true), rows[16]);
    }

    [Fact]
    public void E4c_Microsoft_50RowsTwoTruncatedIdsAndNoteSkipped()
    {
        // --count 50: 50 wierszy, potem „<additional entries truncated due to result limit>” (nie wiersz); 6 nazw i 2 Id z „…”.
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("e4c-search-truncated-microsoft-home.bin"));

        Assert.Equal(50, rows.Count);
        Assert.Equal(6, rows.Count(r => r.Name.EndsWith("…", StringComparison.Ordinal)));
        Assert.Equal(
            new[] { "nkdAgility.AzureDevOpsMigrationTools.P…", "Microsoft.Azure.ArtifactSigningClientT…" },
            rows.Where(r => !r.Installable).Select(r => r.WingetId));
        Assert.Equal(new WingetSearchRow("Codium", "Alex313031.Codium", "1.93.1.24277", true), rows[0]);
        Assert.DoesNotContain(rows, r => r.Name.StartsWith("<", StringComparison.Ordinal));
    }

    [Fact]
    public void E4c_OfflineCachedSearch_RowsFromCache()
    {
        // Offline z pobranym źródłem winget zwraca 0 i wyniki z pamięci (research) – parser widzi zwykłą tabelę.
        var rows = WingetSearchParser.Parse(InstallSamples.Lines("e4c-search-offline-cached-home.bin"));

        Assert.Equal(10, rows.Count);
        Assert.Equal(new WingetSearchRow("VLC media player", "VideoLAN.VLC", "3.0.24", true), rows[0]);
        Assert.Equal(new WingetSearchRow("VLC media player (Nightly)", "VideoLAN.VLC.Nightly", "4.0.0.0-nightly20260509", true), rows[8]);
        Assert.All(rows, r => Assert.True(r.Installable, r.WingetId));
    }

    [Theory]
    [InlineData("e4c-search-noresults-home.bin")]
    [InlineData("e4c-offline-fresh-search-home.bin")]
    [InlineData("e4c-offline-fresh-list-source-home.bin")]
    [InlineData("e4c-offline-fresh-list-plain-home.bin")]
    public void E4c_NoTable_Empty(string sample)
    {
        Assert.Empty(WingetSearchParser.Parse(InstallSamples.Lines(sample)));
    }

    [Theory]
    [InlineData("e4c-list-installed-source-home.bin", "7-Zip 26.05 (x64)", "7zip.7zip", "26.05")]
    [InlineData("e4c-list-installed-plain-home.bin", "7-Zip 26.05 (x64)", "7zip.7zip", "26.05")]
    [InlineData("e4c-list-peruser-discord-home.bin", "Discord", "Discord.Discord", "1.0.9265")]
    public void E4c_List_SingleRow(string sample, string name, string id, string version)
    {
        // Wariant bez --source ma linię „Failed when searching source…: msstore” przed tabelą i kolumnę Source.
        Assert.Equal(new WingetSearchRow(name, id, version, true), Assert.Single(WingetSearchParser.Parse(InstallSamples.Lines(sample))));
    }
}
