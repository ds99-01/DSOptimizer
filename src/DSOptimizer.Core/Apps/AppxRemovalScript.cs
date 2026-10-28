using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Apps;

/// <summary>
/// Skrypt usuwania pakietu AppX (uruchamiany przez <c>IScriptRunner</c>, <c>-File</c>). Mechanizm z pomiaru VM 26H2 i Ruling R8:
/// <list type="bullet">
/// <item><see cref="StoreRemovalScope.CurrentUser"/>: <c>Remove-AppxPackage -Package &lt;PackageFullName&gt; -User $UserSid</c>
/// (tylko gdy SID procesu = SID zalogowanego);</item>
/// <item><see cref="StoreRemovalScope.AllUsers"/>: <c>Remove-AppxPackage -Package &lt;PackageFullName&gt; -AllUsers</c>
/// (wszystkie konta i obraz). <c>-User &lt;inny SID&gt;</c> nigdy: zostawia „pending removal”;</item>
/// <item><c>Deprovision</c>: potem <c>Remove-AppxProvisionedPackage -Online -PackageName &lt;PackageName&gt;</c> dla wpisów obrazu
/// z <c>DisplayName -eq Name</c>; gdy pakietu nie ma w obrazie, krok jest pomijany.</item>
/// </list>
/// <c>PackageFullName</c> jest brany ze świeżego <c>Get-AppxPackage</c> po dokładnej nazwie (pakiet mógł się zaktualizować od odczytu
/// listy). Żądanie przychodzi w <c>$Snapshot</c> jako JSON z <see cref="Request"/> (dane, nie tekst skryptu). Skrypt nic nie wypisuje:
/// o wyniku decyduje niezależny odczyt obecności (<see cref="AppxScript.PresenceBody"/>). Błąd cmdletu = niezerowy kod wyjścia.
/// </summary>
public static class AppxRemovalScript
{
    public const string Body = """
        $request = ConvertFrom-Json -InputObject ([string]$Snapshot)
        $name = [string]$request.Name
        """ + "\r\n" + AppxScript.RequireNameLine + "\r\n" + """

        if ($request.AllUsers) {
            foreach ($package in @(@(Get-AppxPackage -AllUsers -Name $name) | Where-Object { $_.Name -eq $name })) {
                Remove-AppxPackage -Package ([string]$package.PackageFullName) -AllUsers
            }
        }
        else {
            foreach ($package in @(@(Get-AppxPackage -User $UserSid -Name $name) | Where-Object { $_.Name -eq $name })) {
                Remove-AppxPackage -Package ([string]$package.PackageFullName) -User $UserSid
            }
        }

        if ($request.Deprovision) {
            foreach ($image in @(@(Get-AppxProvisionedPackage -Online) | Where-Object { $_.DisplayName -eq $name })) {
                Remove-AppxProvisionedPackage -Online -PackageName ([string]$image.PackageName) | Out-Null
            }
        }
        """;

    /// <summary>Żądanie dla <see cref="Body"/> (do <c>ScriptRequest.Snapshot</c>).</summary>
    public static string Request(string packageName, StoreRemovalScope scope, bool deprovision)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException("Brak nazwy pakietu.", nameof(packageName));
        }

        var allUsers = scope switch
        {
            StoreRemovalScope.CurrentUser => false,
            StoreRemovalScope.AllUsers => true,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };
        return new JObject { ["Name"] = packageName, ["AllUsers"] = allUsers, ["Deprovision"] = deprovision }.ToString(Formatting.None);
    }
}
