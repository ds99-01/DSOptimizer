using System.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Support;

/// <summary>
/// Bezpieczniki harnessu: polecenia zmieniające system hosta rzucają wyjątek, dopóki test ich nie zamockuje.
/// Testy używają tylko poleceń bez skutków ubocznych (Get-Command, /?): nawet gdy bezpiecznika zabraknie, nic nie zmieniają.
/// </summary>
public class PowerShellHarnessTests
{
    public static readonly string[] GuardedCommands =
    {
        "powercfg", "powercfg.exe", "fsutil", "fsutil.exe", "dism", "dism.exe", "bcdedit", "bcdedit.exe", "sc.exe", "reg.exe",
        "Set-Service", "Stop-Service", "Start-Service", "Restart-Service", "Set-WindowsReservedStorageState", "Restart-Computer",
        "Set-SmbClientConfiguration", "Set-SmbServerConfiguration", "Set-NetConnectionProfile", "Disable-NetAdapterBinding", "Enable-NetAdapterBinding",
        "Get-DsoLastAccessValue", "Set-DsoLastAccessValue", "Remove-DsoLastAccessValue", "Get-DsoSecurityServicesRunning", "Get-DsoHvciLocked", "Get-DsoSmbClientSetting", "Get-SmbClientConfiguration",
        "Set-DsoSmbClientSetting", "Get-NetConnectionProfile", "Get-DsoNetConnectionProfile", "Set-DsoNetConnectionProfileCategory",
        "Enable-BitLocker", "Disable-BitLocker", "Suspend-BitLocker", "Clear-BitLockerAutoUnlock", "manage-bde", "manage-bde.exe",
        "Invoke-CimMethod", "Invoke-WmiMethod", "takeown", "takeown.exe", "icacls", "icacls.exe",
        "Get-DsoVolumeConversionStatus", "Invoke-DsoVolumeDecrypt",
        "Stop-Process", "Start-Process", "taskkill", "taskkill.exe", "control", "control.exe", "explorer", "explorer.exe",
        "rundll32", "rundll32.exe", "wsreset", "wsreset.exe", "Get-AppxPackage", "Add-AppxPackage", "Remove-AppxPackage",
        "Invoke-RestMethod", "Invoke-WebRequest", "Invoke-Expression",
        "Get-DsoAutoRestartShell", "Get-DsoUserShellProcess", "Stop-DsoShellProcess", "Get-DsoStorePackage", "Register-DsoStorePackage",
        "netsh", "netsh.exe", "Clear-DnsClientCache", "Get-DnsClientCache", "Get-NetIPConfiguration", "Get-DsoHostsText",
        "Enable-ComputerRestore", "Disable-ComputerRestore", "Get-DsoSystemVolumeId", "Get-DsoProtectionClients",
        "Get-DsoAppInstallerPackage", "Invoke-DsoWingetVersion", "Register-DsoAppInstaller",
        "Delete-DeliveryOptimizationCache", "Get-DeliveryOptimizationStatus", "Test-DsoDeliveryOptimizationCmdlet",
        "winget", "winget.exe",
    };

    /// <summary>Aliasy mają pierwszeństwo przed funkcjami, ale alias na cmdlet z funkcją-bezpiecznikiem rozwiązuje się do tej funkcji.</summary>
    public static readonly string[] GuardedAliases = { "kill", "spps", "saps", "start", "irm", "iwr", "iex", "curl", "wget" };

    [Fact]
    public void EveryGuardedCommand_ResolvesToFunction_NotToProgramOrCmdlet()
    {
        var names = string.Join(",", GuardedCommands.Select(n => $"'{n}'"));
        var run = PowerShellHarness.Run(
            $"foreach ($n in @({names})) {{ Write-Output ($n + '=' + (Get-Command $n -ErrorAction Stop | Select-Object -First 1).CommandType) }}",
            mocks: string.Empty,
            snapshot: string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(GuardedCommands.Length, run.Output.Count);
        foreach (var name in GuardedCommands)
        {
            Assert.Contains($"{name}=Function", run.Output);
        }
    }

    [Fact]
    public void EveryGuardedAlias_ResolvesToGuardFunction_NotToCmdlet()
    {
        var names = string.Join(",", GuardedAliases.Select(n => $"'{n}'"));
        var run = PowerShellHarness.Run(
            $"foreach ($n in @({names})) {{ $c = Get-Command $n -ErrorAction Stop | Select-Object -First 1; Write-Output ($n + '=' + $c.CommandType + '>' + $c.ResolvedCommand.CommandType) }}",
            mocks: string.Empty,
            snapshot: string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(GuardedAliases.Length, run.Output.Count);
        foreach (var name in GuardedAliases)
        {
            Assert.Contains($"{name}=Alias>Function", run.Output);
        }
    }

    [Theory]
    [InlineData("powercfg /?")]
    [InlineData("powercfg.exe /?")]
    [InlineData("fsutil.exe /?")]
    [InlineData("dism.exe /?")]
    [InlineData("bcdedit.exe /?")]
    [InlineData("reg.exe /?")]
    [InlineData("sc.exe /?")]
    [InlineData("winget --version")]
    [InlineData("winget.exe search x")]
    public void UnmockedProgram_HitsGuard_InsteadOfRunning(string call)
    {
        var run = PowerShellHarness.Run(call, mocks: string.Empty, snapshot: string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error);
    }

    [Theory]
    [InlineData("Set-SmbClientConfiguration")]
    [InlineData("Set-SmbServerConfiguration")]
    [InlineData("Get-SmbClientConfiguration")]
    [InlineData("Set-NetConnectionProfile")]
    [InlineData("Get-NetConnectionProfile")]
    [InlineData("Disable-NetAdapterBinding")]
    [InlineData("Enable-NetAdapterBinding")]
    [InlineData("Enable-BitLocker")]
    [InlineData("Disable-BitLocker")]
    [InlineData("Suspend-BitLocker")]
    [InlineData("Clear-BitLockerAutoUnlock")]
    [InlineData("Invoke-CimMethod")]
    [InlineData("Invoke-WmiMethod")]
    public void UnmockedSystemCmdlet_HitsGuard_AndRealCmdletCouldNeverRun(string name)
    {
        // Sonda z nieistniejącym parametrem: prawdziwy cmdlet padłby na wiązaniu parametrów, zanim cokolwiek zmieni (żadnych argumentów zmieniających system).
        // Bezpiecznik (funkcja bez parametrów) przyjmuje -DsoHarnessProbe jako $args i rzuca swój wyjątek.
        var script =
            $"Write-Output ('type=' + (Get-Command '{name}' -ErrorAction Stop | Select-Object -First 1).CommandType)\n" +
            $"try {{ {name} -DsoHarnessProbe }} catch {{ Write-Output ('err=' + $_.Exception.Message) }}";
        var run = PowerShellHarness.Run(script, mocks: string.Empty, snapshot: string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("type=Function", run.Output);
        Assert.Contains(run.Output, line => line.StartsWith("err=", System.StringComparison.Ordinal) && line.Contains("Bezpiecznik harnessu"));
    }

    [Theory]
    [InlineData("Get-DsoLastAccessValue")]
    [InlineData("Set-DsoLastAccessValue 1")]
    [InlineData("Remove-DsoLastAccessValue")]
    [InlineData("Get-DsoSecurityServicesRunning")]
    [InlineData("Get-DsoHvciLocked")]
    [InlineData("Get-DsoSmbClientSetting -Name x")]
    [InlineData("Set-DsoSmbClientSetting $false $true")]
    [InlineData("Get-DsoNetConnectionProfile")]
    [InlineData("Set-DsoNetConnectionProfileCategory $null Private")]
    [InlineData("Get-DsoVolumeConversionStatus")]
    [InlineData("Invoke-DsoVolumeDecrypt")]
    public void UnmockedRegistryOrCimHelper_HitsGuard_InsteadOfTouchingHost(string call)
    {
        var run = PowerShellHarness.Run(call, mocks: string.Empty, snapshot: string.Empty);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error);
    }

    [Theory]
    [InlineData("Stop-Process -Id 2147483646")]
    [InlineData("kill 2147483646")]
    [InlineData("spps -Id 2147483646")]
    [InlineData("Start-Process -FilePath dso-nieistniejacy-program.exe")]
    [InlineData("start dso-nieistniejacy-program.exe")]
    [InlineData("saps dso-nieistniejacy-program.exe")]
    [InlineData("taskkill /PID 2147483646")]
    [InlineData("taskkill.exe /PID 2147483646")]
    [InlineData("control dso-nieistniejacy-panel")]
    [InlineData("control.exe dso-nieistniejacy-panel")]
    [InlineData("explorer dso-nieistniejacy-katalog")]
    [InlineData("explorer.exe dso-nieistniejacy-katalog")]
    [InlineData("rundll32 dso-nieistniejaca.dll,Nic")]
    [InlineData("rundll32.exe dso-nieistniejaca.dll,Nic")]
    [InlineData("wsreset")]
    [InlineData("wsreset.exe")]
    [InlineData("Get-AppxPackage -Name Dso.Nieistniejacy")]
    [InlineData("Add-AppxPackage -Path dso-nieistniejacy.appx")]
    [InlineData("Remove-AppxPackage -Package Dso.Nieistniejacy")]
    [InlineData("Get-AppxProvisionedPackage -Online")]
    [InlineData("Remove-AppxProvisionedPackage -Online -PackageName Dso.Nieistniejacy")]
    [InlineData("Invoke-RestMethod -Uri http://dso.invalid/")]
    [InlineData("irm http://dso.invalid/")]
    [InlineData("Invoke-WebRequest -Uri http://dso.invalid/")]
    [InlineData("iwr http://dso.invalid/")]
    [InlineData("Invoke-Expression 'Write-Output dso'")]
    [InlineData("iex 'Write-Output dso'")]
    [InlineData("curl http://dso.invalid/")]
    [InlineData("wget http://dso.invalid/")]
    [InlineData("Get-DsoAutoRestartShell")]
    [InlineData("Get-DsoUserShellProcess")]
    [InlineData("Stop-DsoShellProcess 2147483646")]
    [InlineData("Get-DsoStorePackage")]
    [InlineData("Register-DsoStorePackage")]
    [InlineData("netsh winsock show catalog")]
    [InlineData("netsh.exe winsock show catalog")]
    [InlineData("Clear-DnsClientCache")]
    [InlineData("Get-DnsClientCache")]
    [InlineData("Get-NetIPConfiguration")]
    [InlineData("Get-DsoHostsText")]
    [InlineData("Enable-ComputerRestore -Drive 'C:\'")]
    [InlineData("Disable-ComputerRestore -Drive 'C:\'")]
    [InlineData("Get-DsoSystemVolumeId")]
    [InlineData("Get-DsoProtectionClients")]
    [InlineData("Get-DsoAppInstallerPackage")]
    [InlineData("Invoke-DsoWingetVersion")]
    [InlineData("Register-DsoAppInstaller")]
    public void UnmockedProcessAppxNetworkCommandOrAlias_HitsGuard_InsteadOfRunning(string call)
    {
        // Argumenty są nieszkodliwe (nieistniejący PID, fikcyjne nazwy, domena .invalid), na wypadek braku bezpiecznika.
        // Bramka: gdy nazwa (po rozwiązaniu aliasu) nie jest funkcją-bezpiecznikiem, skrypt kończy się kodem 97 BEZ wywołania,
        // więc po usunięciu bezpiecznika test nie uruchomi prawdziwego polecenia na hoście.
        var run = PowerShellHarness.Run(WithGuardGate(call), mocks: string.Empty, snapshot: string.Empty);

        Assert.NotEqual(GateExitCode, run.ExitCode);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Bezpiecznik harnessu", run.Error);
    }

    [Fact]
    public void GuardGate_WithoutGuardFunction_StopsBeforeCall()
    {
        // Dowód, że bramka działa: usuwamy bezpiecznik nieistniejącego w systemie polecenia; wywołanie nie może się odbyć.
        var run = PowerShellHarness.Run(
            "Remove-Item function:Get-DsoStorePackage\n" + WithGuardGate("Get-DsoStorePackage; Write-Output 'WYWOLANO'"),
            mocks: string.Empty,
            snapshot: string.Empty);

        Assert.Equal(GateExitCode, run.ExitCode);
        Assert.DoesNotContain("WYWOLANO", run.Output);
    }

    [Fact]
    public void GuardGate_WithAliasWhoseTargetGuardWasRemoved_StopsBeforeCall()
    {
        // Alias saps wskazuje na Start-Process; bez funkcji-bezpiecznika rozwiązuje się do cmdletu, więc bramka musi zatrzymać skrypt.
        var run = PowerShellHarness.Run(
            "Remove-Item function:Start-Process\n" + WithGuardGate("saps -DsoHarnessProbe; Write-Output 'WYWOLANO'"),
            mocks: string.Empty,
            snapshot: string.Empty);

        Assert.Equal(GateExitCode, run.ExitCode);
        Assert.DoesNotContain("WYWOLANO", run.Output);
    }

    private const int GateExitCode = 97;

    private static string WithGuardGate(string call)
    {
        var name = call.Split(' ', ';')[0];
        return
            $"$dsoGateCommand = Get-Command '{name}' -ErrorAction SilentlyContinue | Select-Object -First 1\n" +
            "$dsoGateTarget = if ($null -ne $dsoGateCommand -and $dsoGateCommand.CommandType -eq 'Alias') { $dsoGateCommand.ResolvedCommand } else { $dsoGateCommand }\n" +
            $"if ($null -eq $dsoGateTarget -or $dsoGateTarget.CommandType -ne 'Function') {{ exit {GateExitCode} }}\n" +
            call;
    }

    [Fact]
    public void Mock_DefinedByTest_OverridesGuard()
    {
        var run = PowerShellHarness.Run(
            "powercfg.exe /h off",
            mocks: "function powercfg.exe { Write-Output ('mock ' + ($args -join ' ')) }",
            snapshot: string.Empty);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("mock /h off", run.LastLine);
    }
}
