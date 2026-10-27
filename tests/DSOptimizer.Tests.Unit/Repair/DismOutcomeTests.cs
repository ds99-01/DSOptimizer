using DSOptimizer.Core.Repair;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>
/// Werdykt DISM /RestoreHealth z kodu wyjścia i bloku podsumowania w segmencie CBS.log (pomiar Task 2, pkt 1z i 1b):
/// zdania DISM bywają zlokalizowane, więc nie są źródłem werdyktu.
/// </summary>
public class DismOutcomeTests
{
    private static string Summary(int detected, int repaired, string result = "0x0")
        => "2026-10-23 10:00:00, Info                  CBS    Checking System Update Readiness.\r\n"
           + "2026-10-23 10:00:00, Info                  CBS    Summary:\r\n"
           + "2026-10-23 10:00:00, Info                  CBS    Operation: Detect and Repair \r\n"
           + $"2026-10-23 10:00:00, Info                  CBS    Operation result: {result}\r\n"
           + $"2026-10-23 10:00:00, Info                  CBS    Total Detected Corruption:\t{detected}\r\n"
           + "2026-10-23 10:00:00, Info                  CBS    \tCBS Manifest Corruption:\t0\r\n"
           + $"2026-10-23 10:00:00, Info                  CBS    Total Repaired Corruption:\t{repaired}\r\n"
           + "2026-10-23 10:00:00, Info                  CBS    \tCBS Manifest Repaired:\t0\r\n";

    [Theory]
    [InlineData("dism-restorehealth-healthy-home.cbs.log")]
    [InlineData("dism-restorehealth-healthy-pro.cbs.log")]
    public void ExitZero_RealHealthySegment_IsHealthy(string sample)
    {
        var outcome = DismOutcome.Classify(0, RepairSamples.Text(sample));

        Assert.Equal(DismVerdict.Healthy, outcome.Verdict);
        Assert.Equal(0, outcome.Detected);
        Assert.Equal(0, outcome.Repaired);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void ExitZero_SummaryInsideLongSegment_UsesTheLastSummary()
    {
        var segment = "szum\r\n" + Summary(3, 0) + new string('x', 5000) + "\r\n" + Summary(2, 2) + "Exec: Processing complete\r\n";

        var outcome = DismOutcome.Classify(0, segment);

        Assert.Equal(DismVerdict.Repaired, outcome.Verdict);
        Assert.Equal(2, outcome.Detected);
        Assert.Equal(2, outcome.Repaired);
    }

    [Fact]
    public void ExitZero_DetectedAndAllRepaired_IsRepaired()
        => Assert.Equal(DismVerdict.Repaired, DismOutcome.Classify(0, Summary(5, 5)).Verdict);

    [Fact]
    public void ExitZero_DetectedButNotAllRepaired_IsFailed_NotSuccess()
    {
        var outcome = DismOutcome.Classify(0, Summary(4, 1));

        Assert.Equal(DismVerdict.Failed, outcome.Verdict);
        Assert.False(outcome.IsSuccess);
        Assert.Equal(4, outcome.Detected);
        Assert.Equal(1, outcome.Repaired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-10-23, Info CBS    Exec: Processing complete\r\n")]
    [InlineData("Checking System Update Readiness.\r\nSummary:\r\nOperation result: 0x0\r\n")]
    public void ExitZero_WithoutReadableSummary_IsHealthyOrRepaired(string? segment)
    {
        var outcome = DismOutcome.Classify(0, segment);

        Assert.Equal(DismVerdict.HealthyOrRepaired, outcome.Verdict);
        Assert.Null(outcome.Detected);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void Exit3010_IsRebootRequired_AndSuccess()
    {
        var outcome = DismOutcome.Classify(3010, null);

        Assert.Equal(DismVerdict.RebootRequired, outcome.Verdict);
        Assert.True(outcome.IsSuccess);
    }

    [Theory]
    [InlineData(0x800F081Fu)]
    [InlineData(0x800F0906u)]
    [InlineData(0x800F0907u)]
    public void SourceMissingCodes_AreSourceMissing(uint code)
    {
        var outcome = DismOutcome.Classify(unchecked((int)code), Summary(2, 0, "0x800f081f"));

        Assert.Equal(DismVerdict.SourceMissing, outcome.Verdict);
        Assert.False(outcome.IsSuccess);
    }

    [Theory]
    [InlineData(0x800F0950u)]
    [InlineData(87u)]
    [InlineData(1u)]
    [InlineData(0x80070005u)]
    public void OtherCodes_AreFailed(uint code)
    {
        var outcome = DismOutcome.Classify(unchecked((int)code), Summary(0, 0));

        Assert.Equal(DismVerdict.Failed, outcome.Verdict);
        Assert.False(outcome.IsSuccess);
        Assert.Equal(unchecked((int)code), outcome.ExitCode);
    }

    [Theory]
    [InlineData(0x800F081Fu, "0x800F081F")]
    [InlineData(0x800F0950u, "0x800F0950")]
    [InlineData(87u, "0x00000057")]
    [InlineData(0u, "0x00000000")]
    public void Hex_FormatsAsEightDigitHresult(uint code, string expected)
        => Assert.Equal(expected, DismOutcome.Hex(unchecked((int)code)));

    [Fact]
    public void NonAsciiDigitsInCounts_AreNotParsed()
        => Assert.Equal(DismVerdict.HealthyOrRepaired, DismOutcome.Classify(0, Summary(0, 0).Replace("Corruption:\t0", "Corruption:\t٤")).Verdict);
}
