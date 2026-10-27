using System;
using DSOptimizer.Core.Repair;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>
/// Skrypt optymalizacji dostarczania (<see cref="DiskCleanupScripts.DeliveryOptimization"/>) w harnessie. Każdy test mockuje
/// <c>Delete-DeliveryOptimizationCache</c> (także tam, gdzie nie powinien być wywołany – wtedy mock rzuca), a helper sprawdzający
/// obecność polecenia jest bezpiecznikiem harnessu: nic nie czyści pamięci DO hosta.
/// </summary>
public class DiskCleanupScriptsTests
{
    private const string DeleteRecorder = "function Delete-DeliveryOptimizationCache { param([switch]$Force) Write-Host ('CALL delete force=' + $Force.IsPresent) }\r\n";
    private const string DeleteForbidden = "function Delete-DeliveryOptimizationCache { throw 'CALL delete – nie powinno paść' }\r\n";

    private static ScriptRun Run(string mocks) => PowerShellHarness.Run(DiskCleanupScripts.DeliveryOptimization, mocks, snapshot: string.Empty);

    [Fact]
    public void CmdletPresent_DeletesWithForce_PrintsDoneMarker()
    {
        var run = Run("function Test-DsoDeliveryOptimizationCmdlet { $true }\r\n" + DeleteRecorder);

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Error);
        Assert.Contains("CALL delete force=True", run.Output);
        Assert.Equal(DiskCleanupScripts.DoneMarker, run.LastLine);
    }

    [Fact]
    public void CmdletMissing_PrintsMissingMarker_DoesNotDelete()
    {
        var run = Run("function Test-DsoDeliveryOptimizationCmdlet { $false }\r\n" + DeleteForbidden);

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Error);
        Assert.Equal(DiskCleanupScripts.MissingMarker, run.LastLine);
        Assert.DoesNotContain(DiskCleanupScripts.DoneMarker, run.Output);
    }

    [Fact]
    public void CmdletThrows_NonZeroExit_ErrorOnStderr_NoDoneMarker()
    {
        var run = Run("function Test-DsoDeliveryOptimizationCmdlet { $true }\r\n" +
                      "function Delete-DeliveryOptimizationCache { param([switch]$Force) throw 'Odmowa dostępu do pamięci DO' }\r\n");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Odmowa dostępu do pamięci DO", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(DiskCleanupScripts.DoneMarker, run.Output);
        Assert.DoesNotContain(DiskCleanupScripts.MissingMarker, run.Output);
    }

    [Fact]
    public void UnmockedPresenceHelper_HitsHarnessGuard()
    {
        var run = Run(DeleteForbidden);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu: Test-DsoDeliveryOptimizationCmdlet", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnmockedDeleteCmdlet_HitsHarnessGuard()
    {
        var run = Run("function Test-DsoDeliveryOptimizationCmdlet { $true }\r\n");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu: Delete-DeliveryOptimizationCache", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesOnlyDeliveryOptimizationCache_WithForce()
    {
        var body = DiskCleanupScripts.DeliveryOptimization;
        Assert.Contains("Delete-DeliveryOptimizationCache -Force", body, StringComparison.Ordinal);
        Assert.Contains("Test-DsoDeliveryOptimizationCmdlet", body, StringComparison.Ordinal);
        Assert.DoesNotContain("HKCU:", body, StringComparison.OrdinalIgnoreCase);
        foreach (var forbidden in new[] { "Remove-Item", "dism", "cleanmgr", "Recycle", "Downloads", "Set-DeliveryOptimization" })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
