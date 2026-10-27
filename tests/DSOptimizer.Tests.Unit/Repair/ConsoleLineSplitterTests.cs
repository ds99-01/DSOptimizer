using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DSOptimizer.Core.Repair;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>Dane syntetyczne odwzorowujące format DISM (OEM 852, pasek postępu) i SFC (UTF-16LE, postęp przez \r bez \n).</summary>
public class ConsoleLineSplitterTests
{
    private static readonly Encoding Oem = Encoding.GetEncoding(852);

    private static (ConsoleLineSplitter Splitter, List<string> Lines) Create()
    {
        var splitter = new ConsoleLineSplitter(Oem);
        var lines = new List<string>();
        splitter.Line += lines.Add;
        return (splitter, lines);
    }

    private static void Feed(ConsoleLineSplitter splitter, byte[] bytes, int chunk)
    {
        for (var offset = 0; offset < bytes.Length; offset += chunk)
        {
            var part = bytes.Skip(offset).Take(chunk).ToArray();
            splitter.Write(part, part.Length);
        }
    }

    private static byte[] Utf16(string text, bool bom)
    {
        var body = Encoding.Unicode.GetBytes(text);
        return bom ? Encoding.Unicode.GetPreamble().Concat(body).ToArray() : body;
    }

    private const string SfcText = "\r\nRozpoczynanie skanowania systemu.\r\nTo może chwilę potrwać.\r\n\r\n"
        + "Weryfikacja 10% ukończona.\rWeryfikacja 40% ukończona.\rWeryfikacja 70% ukończona.\rWeryfikacja 100% ukończona.\r\n"
        + "Ochrona zasobów systemu Windows nie znalazła żadnych naruszeń integralności.\r\n";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Utf16Sfc_ProgressWithCarriageReturns(bool bom)
    {
        var (splitter, lines) = Create();
        var bytes = Utf16(SfcText, bom);

        splitter.Write(bytes, bytes.Length);
        splitter.Flush();

        Assert.Equal(
            new[]
            {
                "Rozpoczynanie skanowania systemu.",
                "To może chwilę potrwać.",
                "Weryfikacja 10% ukończona.",
                "Weryfikacja 40% ukończona.",
                "Weryfikacja 70% ukończona.",
                "Weryfikacja 100% ukończona.",
                "Ochrona zasobów systemu Windows nie znalazła żadnych naruszeń integralności.",
            },
            lines);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void Utf16Sfc_SplitInsideCharacters_SameLinesAsWhole(int chunk)
    {
        var whole = Create();
        var bytes = Utf16(SfcText, true);
        whole.Splitter.Write(bytes, bytes.Length);
        whole.Splitter.Flush();
        var split = Create();

        Feed(split.Splitter, bytes, chunk);
        split.Splitter.Flush();

        Assert.Equal(whole.Lines, split.Lines);
        Assert.DoesNotContain(split.Lines, l => l.Contains('\uFFFD'));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Utf16WithoutBom_OneByteChunks_SameLinesAsWhole(int chunk)
    {
        var whole = Create();
        var bytes = Utf16(SfcText, false);
        whole.Splitter.Write(bytes, bytes.Length);
        whole.Splitter.Flush();
        var split = Create();

        Feed(split.Splitter, bytes, chunk);
        split.Splitter.Flush();

        Assert.Equal(7, whole.Lines.Count);
        Assert.Equal(whole.Lines, split.Lines);
        Assert.DoesNotContain(split.Lines, l => l.Contains('�') || l.Contains('\0'));
    }

    [Fact]
    public void Utf16_SurrogatePairSplitBetweenBuffers_IsAssembled()
    {
        var (splitter, lines) = Create();
        var bytes = Utf16("a😀b\r\n", false);

        Feed(splitter, bytes, 3);
        splitter.Flush();

        Assert.Equal(new[] { "a😀b" }, lines);
    }

    private const string DismText = "\r\nNarzędzie do obsługi i zarządzania obrazami wdrażania\r\nWersja: 10.0.26100.1\r\n\r\n"
        + "Obraz: C:\\\r\n[==========================  45.0%                           ]\r"
        + "[==========================100.0%==========================] \r\n"
        + "Operacja przywracania kondycji zakończyła się pomyślnie. Użyto uszkodzeń źródła: łączą żółć.\r\n";

    private static readonly string[] DismExpected =
    {
        "Narzędzie do obsługi i zarządzania obrazami wdrażania",
        "Wersja: 10.0.26100.1",
        "Obraz: C:\\",
        "[==========================  45.0%                           ]",
        "[==========================100.0%==========================] ",
        "Operacja przywracania kondycji zakończyła się pomyślnie. Użyto uszkodzeń źródła: łączą żółć.",
    };

    [Fact]
    public void Oem_PolishCharactersDecoded()
    {
        var (splitter, lines) = Create();
        var bytes = Oem.GetBytes(DismText);

        splitter.Write(bytes, bytes.Length);
        splitter.Flush();

        Assert.Equal(DismExpected, lines);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(64)]
    public void OemDism_SplitAcrossChunks(int chunk)
    {
        var (splitter, lines) = Create();

        Feed(splitter, Oem.GetBytes(DismText), chunk);
        splitter.Flush();

        Assert.Equal(DismExpected, lines);
    }

    [Fact]
    public void CarriageReturnLineFeedSplitAcrossBuffers_NoEmptyLine()
    {
        var (splitter, lines) = Create();
        var a = Oem.GetBytes("pierwsza\r");
        var b = Oem.GetBytes("\ndruga\r\n");

        splitter.Write(a, a.Length);
        splitter.Write(b, b.Length);
        splitter.Flush();

        Assert.Equal(new[] { "pierwsza", "druga" }, lines);
    }

    [Fact]
    public void FlushEmitsLastLineWithoutNewline()
    {
        var (splitter, lines) = Create();
        var bytes = Oem.GetBytes("gotowa\r\nostatnia bez końca");

        splitter.Write(bytes, bytes.Length);
        Assert.Equal(new[] { "gotowa" }, lines);
        splitter.Flush();

        Assert.Equal(new[] { "gotowa", "ostatnia bez końca" }, lines);
    }

    [Fact]
    public void Flush_Twice_DoesNotRepeatLine()
    {
        var (splitter, lines) = Create();
        var bytes = Oem.GetBytes("x");

        splitter.Write(bytes, bytes.Length);
        splitter.Flush();
        splitter.Flush();

        Assert.Equal(new[] { "x" }, lines);
    }

    [Fact]
    public void Utf16_FlushEmitsLastLineWithoutNewline()
    {
        var (splitter, lines) = Create();
        var bytes = Utf16("Weryfikacja 70% ukończona.", false);

        splitter.Write(bytes, bytes.Length);
        splitter.Flush();

        Assert.Equal(new[] { "Weryfikacja 70% ukończona." }, lines);
    }

    [Fact]
    public void WriteUsesOnlyCountBytes()
    {
        var (splitter, lines) = Create();
        var buffer = Oem.GetBytes("ab\r\nZZZZZZ");

        splitter.Write(buffer, 4);
        splitter.Flush();

        Assert.Equal(new[] { "ab" }, lines);
    }

    [Fact]
    public void SingleByteThenFlush_IsOem()
    {
        var (splitter, lines) = Create();

        splitter.Write(new byte[] { (byte)'z' }, 1);
        splitter.Flush();

        Assert.Equal(new[] { "z" }, lines);
    }

    [Fact]
    public void NoSubscriber_DoesNotThrow()
    {
        var splitter = new ConsoleLineSplitter(Oem);
        var bytes = Oem.GetBytes("a\r\n");

        splitter.Write(bytes, bytes.Length);
        splitter.Flush();
    }

    [Fact]
    public void InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ConsoleLineSplitter(null!));
        var splitter = new ConsoleLineSplitter(Oem);
        Assert.Throws<ArgumentNullException>(() => splitter.Write(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.Write(new byte[2], 3));
    }
}
