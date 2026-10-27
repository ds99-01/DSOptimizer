using DSOptimizer.Core.Repair;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>
/// Werdykt SFC /scannow ze znaczników <c>[SR]</c> segmentu CBS.log z tego przebiegu (pomiar Task 2, pkt 2r). „Brak naruszeń”
/// zmierzony na VM; znaczniki naprawy z KB 928228 (niezmierzone, R8); „czeka na restart” = klucz RebootPending, który
/// pojawił się w trakcie SFC, razem ze śladem naprawy w [SR].
/// </summary>
public class SfcOutcomeTests
{
    private const string Prefix = "2026-10-23 10:00:00, Info                  CSI    00000038 ";

    private static string Sr(params string[] lines)
    {
        var text = "2026-10-23 10:00:00, Info                  CBS    TiWorker: Client requests SFP repair object.\r\n";
        foreach (var line in lines)
        {
            text += Prefix + line + "\r\n";
        }

        return text;
    }

    private static readonly string[] HealthyGroup =
    {
        "[SR] Verifying 100 components",
        "[SR] Beginning Verify and Repair transaction",
        "[SR] Verify complete",
    };

    [Theory]
    [InlineData("sfc-scannow-noviolations-home.cbs.log")]
    [InlineData("sfc-scannow-noviolations-pro.cbs.log")]
    public void ExitZero_RealHealthySegment_IsNoViolations(string sample)
    {
        var outcome = SfcOutcome.Classify(0, RepairSamples.Text(sample), rebootPendingAppeared: false);

        Assert.Equal(SfcVerdict.NoViolations, outcome.Verdict);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void RealHealthySegment_RebootPendingFromSomethingElse_StaysNoViolations()
        => Assert.Equal(SfcVerdict.NoViolations, SfcOutcome.Classify(0, RepairSamples.Text("sfc-scannow-noviolations-home.cbs.log"), rebootPendingAppeared: true).Verdict);

    [Fact]
    public void RealHealthySegment_NonZeroExit_IsUnknown()
    {
        var outcome = SfcOutcome.Classify(1, RepairSamples.Text("sfc-scannow-noviolations-pro.cbs.log"), rebootPendingAppeared: false);

        Assert.Equal(SfcVerdict.Unknown, outcome.Verdict);
        Assert.False(outcome.IsSuccess);
    }

    [Theory]
    [InlineData("[SR] Repairing corrupted file \\??\\C:\\Windows\\System32\\calc.exe from store")]
    [InlineData("[SR] Repaired file \\SystemRoot\\WinSxS\\Manifests\\x.manifest by copying from backup")]
    public void RepairMarkers_AreRepaired(string marker)
    {
        var segment = Sr(HealthyGroup[0], HealthyGroup[1], marker, HealthyGroup[2], "[SR] Repairing 1 components", "[SR] Repair complete");

        var outcome = SfcOutcome.Classify(0, segment, rebootPendingAppeared: false);

        Assert.Equal(SfcVerdict.Repaired, outcome.Verdict);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void RepairMarker_WithRebootPendingAppeared_IsRepairPendingReboot()
    {
        var segment = Sr("[SR] Repairing corrupted file \\??\\C:\\Windows\\System32\\x.dll from store", "[SR] Verify complete");

        Assert.Equal(SfcVerdict.RepairPendingReboot, SfcOutcome.Classify(0, segment, rebootPendingAppeared: true).Verdict);
    }

    [Fact]
    public void RepairingNonZeroComponents_WithoutFileMarkers_AndRebootPendingAppeared_IsRepairPendingReboot()
    {
        var segment = Sr(HealthyGroup[0], HealthyGroup[1], HealthyGroup[2], "[SR] Repairing 2 components", "[SR] Beginning Verify and Repair transaction");

        var outcome = SfcOutcome.Classify(0, segment, rebootPendingAppeared: true);

        Assert.Equal(SfcVerdict.RepairPendingReboot, outcome.Verdict);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void RepairingNonZeroComponents_WithoutFileMarkersOrReboot_IsUnknown()
    {
        var segment = Sr(HealthyGroup[0], HealthyGroup[1], HealthyGroup[2], "[SR] Repairing 2 components", "[SR] Repair complete");

        Assert.Equal(SfcVerdict.Unknown, SfcOutcome.Classify(0, segment, rebootPendingAppeared: false).Verdict);
    }

    [Fact]
    public void CannotRepair_WinsOverRepairedFiles_IsNotAllRepaired()
    {
        var segment = Sr(
            "[SR] Repairing corrupted file \\??\\C:\\Windows\\System32\\a.dll from store",
            "[SR] Cannot repair member file [l:10]\"b.dll\" of Microsoft-Windows-X, version 10.0.26300.1",
            "[SR] Verify complete");

        var outcome = SfcOutcome.Classify(0, segment, rebootPendingAppeared: true);

        Assert.Equal(SfcVerdict.NotAllRepaired, outcome.Verdict);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void CannotRepair_WithNonZeroExit_IsStillNotAllRepaired()
        => Assert.Equal(SfcVerdict.NotAllRepaired, SfcOutcome.Classify(5, Sr("[SR] Cannot repair member file x"), false).Verdict);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-10-23 10:00:00, Info CBS    TiWorker: Client requests SFP repair object.\r\n")]
    public void NoSrLines_IsUnknown(string? segment)
    {
        var outcome = SfcOutcome.Classify(0, segment, rebootPendingAppeared: false);

        Assert.Equal(SfcVerdict.Unknown, outcome.Verdict);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void SrLinesWithoutVerifyComplete_IsUnknown()
        => Assert.Equal(SfcVerdict.Unknown, SfcOutcome.Classify(0, Sr("[SR] Verifying 100 components", "[SR] Beginning Verify and Repair transaction"), false).Verdict);

    [Fact]
    public void ExitCode_IsKept()
        => Assert.Equal(7, SfcOutcome.Classify(7, null, false).ExitCode);
}
