using System.Linq;
using DSOptimizer.Core.Repair;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>
/// Splitter i parser postępu na prawdziwych bajtach z VM 26H2 (Task 2): SFC = UTF-16LE bez BOM, postęp przez samo <c>\r</c>,
/// zdania kończone <c>\r\r\n</c>, potok blokami po 4096 B; DISM = OEM 852, każda aktualizacja paska jako <c>\r[…]\r\n</c>.
/// </summary>
public class ConsoleLineSplitterSampleTests
{
    [Theory]
    [InlineData("sfc-scannow-noviolations-home.bin", 4096)]
    [InlineData("sfc-scannow-noviolations-pro.bin", 4096)]
    [InlineData("sfc-scannow-noviolations-home.bin", 1)]
    [InlineData("sfc-scannow-noviolations-pro.bin", 1)]
    [InlineData("sfc-scannow-noviolations-home.bin", 7)]
    public void Utf16Sfc_RealSample_LinesWithoutGarbageOrEmptyLines(string sample, int chunk)
    {
        var lines = RepairSamples.Lines(sample, chunk);

        Assert.Equal("Beginning system scan.  This process will take some time.", lines[0]);
        Assert.Equal("Beginning verification phase of system scan.", lines[1]);
        Assert.Equal("Windows Resource Protection did not find any integrity violations.", lines[lines.Count - 1]);
        Assert.Equal("Verification 100% complete.", lines[lines.Count - 2]);
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.DoesNotContain(lines, l => l.Contains('�') || l.Contains('\0') || l.Contains('\r') || l.Contains('\n'));
    }

    [Theory]
    [InlineData("sfc-scannow-noviolations-home.bin")]
    [InlineData("sfc-scannow-noviolations-pro.bin")]
    public void Utf16Sfc_RealSample_PercentsRiseFromZeroToHundred(string sample)
    {
        var percents = RepairSamples.Lines(sample)
            .Select(ServicingProgressParser.TryParsePercent)
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .ToList();

        Assert.Equal(0, percents[0]);
        Assert.Equal(100, percents[percents.Count - 1]);
        Assert.True(percents.Zip(percents.Skip(1), (a, b) => b >= a).All(x => x), "procenty SFC nie maleją");
        Assert.Equal(101, percents.Distinct().Count());
    }

    [Fact]
    public void Utf16Sfc_RealSample_FirstBytesAreCarriageReturnInUtf16WithoutBom()
    {
        var bytes = RepairSamples.Bytes("sfc-scannow-noviolations-home.bin");

        Assert.Equal(new byte[] { 0x0D, 0x00, 0x0D, 0x00, 0x0A, 0x00 }, bytes.Take(6).ToArray());
    }

    [Theory]
    [InlineData("sfc-verifyfile-concurrent-home.bin", 1)]
    [InlineData("sfc-verifyfile-concurrent-pro.bin", 3)]
    public void Utf16Sfc_ShortRealSample_IsDecodedAsUtf16(string sample, int chunk)
    {
        var lines = RepairSamples.Lines(sample, chunk);

        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.DoesNotContain('\0', l));
        Assert.Contains(lines, l => l.Contains("Windows Resource Protection"));
    }

    [Theory]
    [InlineData("dism-restorehealth-healthy-home.bin", 4096)]
    [InlineData("dism-restorehealth-healthy-pro.bin", 4096)]
    [InlineData("dism-restorehealth-healthy-home.bin", 1)]
    [InlineData("dism-restorehealth-healthy-pro.bin", 13)]
    public void OemDism_RealSample_HeaderProgressAndFinalLines(string sample, int chunk)
    {
        var lines = RepairSamples.Lines(sample, chunk);

        Assert.Equal("Deployment Image Servicing and Management tool", lines[0]);
        Assert.StartsWith("Version: ", lines[1]);
        Assert.StartsWith("Image Version: ", lines[2]);
        Assert.Equal("The restore operation completed successfully.", lines[lines.Count - 2]);
        Assert.Equal("The operation completed successfully.", lines[lines.Count - 1]);
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.DoesNotContain(lines, l => l.Contains('�') || l.Contains('\r') || l.Contains('\n'));
    }

    [Theory]
    [InlineData("dism-restorehealth-healthy-home.bin")]
    [InlineData("dism-restorehealth-healthy-pro.bin")]
    [InlineData("dism-scanhealth-healthy-home.bin")]
    [InlineData("dism-startcomponentcleanup-pro.bin")]
    public void OemDism_RealSample_PercentsOnlyFromProgressBarAndEndAtHundred(string sample)
    {
        var lines = RepairSamples.Lines(sample);
        var withPercent = lines.Where(l => ServicingProgressParser.TryParsePercent(l) is not null).ToList();
        var percents = withPercent.Select(l => ServicingProgressParser.TryParsePercent(l)!.Value).ToList();

        Assert.All(withPercent, l => Assert.StartsWith("[", l));
        Assert.Equal(100, percents[percents.Count - 1]);
        Assert.True(percents.Zip(percents.Skip(1), (a, b) => b >= a).All(x => x), "procenty DISM nie maleją");
        Assert.Null(ServicingProgressParser.TryParsePercent(lines[2]));
    }
}
