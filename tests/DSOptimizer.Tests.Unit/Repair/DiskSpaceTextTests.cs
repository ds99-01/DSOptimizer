using System;
using DSOptimizer.Core.Repair;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

public class DiskSpaceTextTests
{
    private const long Mb = 1024L * 1024;
    private const long Gb = 1024L * Mb;

    [Fact]
    public void NegativeDelta_NoFakeNumber()
    {
        var text = DiskSpaceText.Freed(10 * Gb, 9 * Gb);

        Assert.Equal("nie da się uczciwie podać zwolnionego miejsca (inne programy zapisywały na dysku)", text);
        Assert.DoesNotContain("MB", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GB", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 5L)]
    [InlineData(5L, null)]
    [InlineData(null, null)]
    public void MissingReading_ReportsUnreadable(long? before, long? after)
        => Assert.Equal("nie udało się odczytać wolnego miejsca", DiskSpaceText.Freed(before, after));

    [Theory]
    [InlineData(0L, "mniej niż 1 MB")]
    [InlineData(Mb - 1, "mniej niż 1 MB")]
    [InlineData(Mb, "1 MB")]
    [InlineData(Mb + Mb / 2, "1 MB")]
    [InlineData(512 * Mb, "512 MB")]
    [InlineData(Gb - 1, "1023 MB")]
    [InlineData(Gb, "1,0 GB")]
    [InlineData(Gb + (Gb / 2), "1,5 GB")]
    [InlineData(Gb + (Gb * 9 / 10) + 1, "1,9 GB")]
    [InlineData(Gb + (Gb / 10 * 9) + 1, "1,8 GB")]
    [InlineData((12 * Gb) + (99 * Mb), "12,0 GB")]
    [InlineData((2 * Gb) - 1, "1,9 GB")]
    [InlineData((10 * Gb) - 1, "9,9 GB")]
    public void Delta_FormattedWithThresholds(long delta, string expected)
        => Assert.Equal(expected, DiskSpaceText.Freed(100 * Gb, (100 * Gb) + delta));

    /// <summary>Rozmiar kroku (pliki usunięte przez krok) tymi samymi progami co „zwolniono”: nigdy w górę.</summary>
    [Theory]
    [InlineData(0L, "mniej niż 1 MB")]
    [InlineData(3 * Mb, "3 MB")]
    [InlineData((2 * Gb) - 1, "1,9 GB")]
    public void Size_SameThresholdsAsFreed(long bytes, string expected)
        => Assert.Equal(expected, DiskSpaceText.Size(bytes));
}
