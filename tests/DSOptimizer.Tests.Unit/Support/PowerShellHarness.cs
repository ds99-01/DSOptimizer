using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Core.Scripts;

namespace DSOptimizer.Tests.Unit.Support;

/// <summary>Wynik uruchomienia skryptu: kod wyjścia, linie stdout i cały stderr.</summary>
public sealed record ScriptRun(int ExitCode, IReadOnlyList<string> Output, string Error)
{
    public string LastLine => Output.Count == 0 ? string.Empty : Output[Output.Count - 1];
}

/// <summary>
/// Uruchamia treść skryptu tweaka tak jak produkcja (<see cref="ScriptWrapper"/>, plik UTF-8 z BOM, <c>-File</c>, snapshot w env),
/// ale z zamockowanymi funkcjami PowerShell (parametr <c>mocks</c>) zamiast prawdziwych cmdletów i narzędzi.
/// To NIE jest pełna piaskownica. Polecenia zmieniające system hosta (usługi, powercfg, fsutil, dism, bcdedit, sc.exe,
/// reg.exe, Stop-Process, Start-Process, taskkill, control, explorer, rundll32, wsreset (też z .exe i aliasy kill/spps/saps/start), Get-/Add-/Remove-AppxPackage, Get-/Remove-AppxProvisionedPackage, Invoke-RestMethod/WebRequest/Expression (aliasy irm/iwr/iex/curl/wget), Set-WindowsReservedStorageState, restart, cmdlety sieciowe Set-Smb*Configuration, Get-SmbClientConfiguration, Get-/Set-NetConnectionProfile, Enable/Disable-NetAdapterBinding, BitLocker: Enable/Disable/Suspend-BitLocker, Clear-BitLockerAutoUnlock, manage-bde; WMI/CIM: Invoke-CimMethod, Invoke-WmiMethod; uprawnienia: takeown, icacls; optymalizacja dostarczania: Delete-DeliveryOptimizationCache, Get-DeliveryOptimizationStatus; winget, winget.exe) mają domyślnie funkcje-bezpieczniki, które rzucają wyjątek;
/// mock testu jest definiowany po nich i je nadpisuje. Bezpieczniki działają tylko na wywołanie PO NAZWIE i NIE łapią:
/// pełnej ścieżki (<c>&amp; "$env:SystemRoot\System32\powercfg.exe"</c>), <c>cmd /c</c>,
/// <c>[Diagnostics.Process]::Start</c>, nazw kwalifikowanych modułem (<c>Microsoft.PowerShell.Management\Stop-Service</c>),
/// <c>Set-ItemProperty</c>/<c>New-ItemProperty</c>/<c>Remove-Item</c> na <c>HKLM:</c>, <c>schtasks</c>, <c>Stop-Computer</c>
/// bezpośrednich wywołań .NET rejestru (<c>[Microsoft.Win32.Registry]</c>, <c>RegistryKey.OpenBaseKey(...).SetValue</c>) ani metod wołanych na obiektach WMI
/// (<c>(Get-WmiObject ...).Decrypt()</c>, <c>[wmiclass]</c>/<c>[wmi]</c>, <c>CimSession.InvokeMethod</c>).
/// Skrypty katalogu mają więc wołać narzędzia po nazwie, a dostęp do rejestru realizować wyłącznie funkcjami pomocniczymi,
/// które testy mockują (testy idą często z sesji admina).
/// </summary>
public static class PowerShellHarness
{
    private static readonly string[] GuardedCommands =
    {
        "powercfg", "powercfg.exe", "fsutil", "fsutil.exe", "dism", "dism.exe", "bcdedit", "bcdedit.exe", "sc.exe", "reg.exe",
        "Set-Service", "Stop-Service", "Start-Service", "Restart-Service", "Set-WindowsReservedStorageState", "Restart-Computer",
        "Set-SmbClientConfiguration", "Set-SmbServerConfiguration", "Set-NetConnectionProfile", "Disable-NetAdapterBinding", "Enable-NetAdapterBinding",
        // Funkcje pomocnicze skryptów katalogu (rejestr .NET, CIM): skrypt produkcyjny pomija własną definicję, gdy nazwa już istnieje,
        // więc test bez mocka dostaje wyjątek zamiast dostępu do HKLM/CIM hosta.
        "Get-DsoLastAccessValue", "Set-DsoLastAccessValue", "Remove-DsoLastAccessValue", "Get-DsoSecurityServicesRunning", "Get-DsoHvciLocked", "Get-DsoSmbClientSetting", "Get-SmbClientConfiguration",
        "Set-DsoSmbClientSetting", "Get-NetConnectionProfile", "Get-DsoNetConnectionProfile", "Set-DsoNetConnectionProfileCategory",
        "Enable-BitLocker", "Disable-BitLocker", "Suspend-BitLocker", "Clear-BitLockerAutoUnlock", "manage-bde", "manage-bde.exe",
        "Invoke-CimMethod", "Invoke-WmiMethod", "takeown", "takeown.exe", "icacls", "icacls.exe",
        "Get-DsoVolumeConversionStatus", "Invoke-DsoVolumeDecrypt",
        // Procesy, panele, Appx i sieć (partia C5). Aliasy (kill, spps, saps, start, irm, iwr, iex, curl, wget) rozwiązują się do tych funkcji,
        // bo funkcja o nazwie docelowej przesłania cmdlet w rozwiązywaniu aliasu (sprawdza to PowerShellHarnessTests).
        "Stop-Process", "Start-Process", "taskkill", "taskkill.exe", "control", "control.exe", "explorer", "explorer.exe",
        "rundll32", "rundll32.exe", "wsreset", "wsreset.exe", "Get-AppxPackage", "Add-AppxPackage", "Remove-AppxPackage",
        "Get-AppxProvisionedPackage", "Remove-AppxProvisionedPackage",
        "Invoke-RestMethod", "Invoke-WebRequest", "Invoke-Expression",
        "Get-DsoAutoRestartShell", "Get-DsoUserShellProcess", "Stop-DsoShellProcess", "Get-DsoStorePackage", "Register-DsoStorePackage",
        // Naprawy (Etap 4b partia 1): DNS i reset sieci. netsh było dotąd poza bezpiecznikami; od partii 1 także wywołane po nazwie rzuca.
        "netsh", "netsh.exe", "Clear-DnsClientCache", "Get-DnsClientCache", "Get-NetIPConfiguration", "Get-DsoHostsText",
        // Naprawy (Task 7): ochrona systemu. Enable/Disable-ComputerRestore zmieniają hosta; helpery czytają CIM i HKLM.
        "Enable-ComputerRestore", "Disable-ComputerRestore", "Get-DsoSystemVolumeId", "Get-DsoProtectionClients",
        // Naprawy (Task 8): winget. Rejestracja zmienia konto, a odczyt pakietu i uruchomienie aliasu dotykają hosta.
        "Get-DsoAppInstallerPackage", "Invoke-DsoWingetVersion", "Register-DsoAppInstaller",
        // Naprawy (partia 2, Task 7): optymalizacja dostarczania. Usunięcie pamięci DO zmienia hosta, odczyt stanu i helper
        // sprawdzający obecność polecenia dotykają hosta.
        "Delete-DeliveryOptimizationCache", "Get-DeliveryOptimizationStatus", "Test-DsoDeliveryOptimizationCmdlet",
        // Etap 4c (Task 4): winget po nazwie nigdy nie startuje na hoście (instalacja, wyszukiwanie, source update, --version).
        "winget", "winget.exe",
    };

    private static readonly string Guards = string.Join(
        "\r\n",
        GuardedCommands.Select(name => $"function {name} {{ throw 'Bezpiecznik harnessu: {name} wywołane bez mocka w teście.' }}"));

    public static ScriptRun Run(string body, string mocks, string snapshot)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dso-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "script.ps1");
            File.WriteAllText(path, ScriptWrapper.Wrap(Guards + "\r\n" + mocks + "\r\n" + body), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var startInfo = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{path}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.EnvironmentVariables[ScriptWrapper.SnapshotVariable] = snapshot;
            startInfo.EnvironmentVariables[ScriptWrapper.UserSidVariable] = "S-1-5-21-0-0-0-1000";
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Nie uruchomiono powershell.exe.");
            var error = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(60000))
            {
                process.Kill();
                throw new TimeoutException("Skrypt testowy nie zakończył się w 60 s.");
            }

            var output = outputTask.Result;

            var lines = new List<string>(output.Replace("\r\n", "\n").Split('\n'));
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return new ScriptRun(process.ExitCode, lines, error.Result);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
