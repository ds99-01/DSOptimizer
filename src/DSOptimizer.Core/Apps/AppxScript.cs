using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Apps;

/// <summary>
/// Treść skryptu PowerShell odczytu pakietów AppX (uruchamiana przez <c>IScriptRunner</c>, jak skrypty katalogu).
/// Mechanizm z pomiaru na VM 26H2: <c>Get-AppxPackage -User $UserSid</c> (konto zalogowane, nie konto procesu) i
/// <c>Get-AppxProvisionedPackage -Online</c> (<c>IsProvisioned</c> = istnieje wpis z <c>DisplayName -eq Name</c>).
/// Wejście: <c>$UserSid</c>. Obecność jednego pakietu (weryfikacja usunięcia) sprawdza osobny <see cref="PresenceBody"/>.
/// Wyjście: jedna linia JSON, tablica obiektów (puste = <c>[]</c>). <c>DisplayName</c> i <c>PublisherDisplayName</c> to SUROWE wartości
/// <c>Properties/DisplayName</c> i <c>Properties/PublisherDisplayName</c> z <c>AppxManifest.xml</c> (mogą być <c>ms-resource:</c>);
/// rozwija je kod C# (<see cref="AppxPackages.ResolveDisplayName"/>, <see cref="AppxPackages.ResolvePublisherName"/>).
/// </summary>
public static class AppxScript
{
    public const string Body = """
        function Get-DsoManifestProperties {
            param([string]$InstallLocation)
            $empty = @{ DisplayName = $null; PublisherDisplayName = $null }
            if ([string]::IsNullOrWhiteSpace($InstallLocation)) { return $empty }
            $manifest = Join-Path $InstallLocation 'AppxManifest.xml'
            if (-not (Test-Path -LiteralPath $manifest)) { return $empty }
            try {
                $xml = New-Object System.Xml.XmlDocument
                $xml.Load($manifest)
                $properties = "//*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='{0}']"
                $result = @{ DisplayName = $null; PublisherDisplayName = $null }
                foreach ($key in @('DisplayName', 'PublisherDisplayName')) {
                    $node = $xml.SelectSingleNode(($properties -f $key))
                    if ($null -ne $node) { $result[$key] = [string]$node.InnerText }
                }
                return $result
            }
            catch { return $empty }
        }

        $found = @(Get-AppxPackage -User $UserSid)

        $provisioned = @{}
        foreach ($item in @(Get-AppxProvisionedPackage -Online)) {
            if ($item.DisplayName) { $provisioned[[string]$item.DisplayName] = $true }
        }

        $result = @(foreach ($package in $found) {
            $manifest = Get-DsoManifestProperties ([string]$package.InstallLocation)
            [pscustomobject][ordered]@{
                Name              = [string]$package.Name
                PackageFullName   = [string]$package.PackageFullName
                PackageFamilyName = [string]$package.PackageFamilyName
                Publisher         = [string]$package.Publisher
                Version           = [string]$package.Version
                IsFramework       = [bool]$package.IsFramework
                IsResourcePackage = [bool]$package.IsResourcePackage
                NonRemovable      = [bool]$package.NonRemovable
                SignatureKind     = [string]$package.SignatureKind
                IsProvisioned     = $provisioned.ContainsKey([string]$package.Name)
                DisplayName          = $manifest.DisplayName
                PublisherDisplayName = $manifest.PublisherDisplayName
            }
        })
        ConvertTo-Json -InputObject $result -Compress
        """;

    /// <summary>
    /// Obecność jednego pakietu po dokładnej nazwie (<c>Name</c>). Żądanie w <c>$Snapshot</c> (JSON z <see cref="PresenceRequest"/>):
    /// <c>AllUsers</c> = <c>Get-AppxPackage -AllUsers</c> zamiast <c>-User $UserSid</c> (Ruling R8), <c>Image</c> = także
    /// <c>Get-AppxProvisionedPackage -Online</c> (<c>DisplayName -eq Name</c>). Obraz jest czytany tylko na żądanie (wolny odczyt DISM).
    /// Wyjście: jedna linia JSON <c>{"Installed":bool,"Provisioned":bool}</c> (<see cref="ParsePresence"/>).
    /// </summary>
    public const string PresenceBody = """
        $request = ConvertFrom-Json -InputObject ([string]$Snapshot)
        $name = [string]$request.Name
        if ([string]::IsNullOrWhiteSpace($name)) { throw 'Brak nazwy pakietu w żądaniu.' }

        if ($request.AllUsers) {
            $installed = @(@(Get-AppxPackage -AllUsers -Name $name) | Where-Object { $_.Name -eq $name })
        }
        else {
            $installed = @(@(Get-AppxPackage -User $UserSid -Name $name) | Where-Object { $_.Name -eq $name })
        }

        $provisioned = @()
        if ($request.Image) {
            $provisioned = @(@(Get-AppxProvisionedPackage -Online) | Where-Object { $_.DisplayName -eq $name })
        }

        $result = [pscustomobject][ordered]@{ Installed = ($installed.Count -gt 0); Provisioned = ($provisioned.Count -gt 0) }
        ConvertTo-Json -InputObject $result -Compress
        """;

    /// <summary>Żądanie dla <see cref="PresenceBody"/> (do <c>ScriptRequest.Snapshot</c>; dane, nie tekst skryptu).</summary>
    public static string PresenceRequest(string packageName, AppPresenceScope scope)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException("Brak nazwy pakietu.", nameof(packageName));
        }

        var (allUsers, image) = scope switch
        {
            AppPresenceScope.User => (false, false),
            AppPresenceScope.UserAndImage => (false, true),
            AppPresenceScope.AllUsersAndImage => (true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };
        return new JObject { ["Name"] = packageName, ["AllUsers"] = allUsers, ["Image"] = image }.ToString(Formatting.None);
    }

    /// <summary>
    /// Wynik <see cref="PresenceBody"/>: pakiet dla konta (kont) = <see cref="AppPresence.Present"/>, tylko w obrazie =
    /// <see cref="AppPresence.ImageOnly"/>, nigdzie = <see cref="AppPresence.Absent"/>. Zły JSON: <see cref="FormatException"/>.
    /// </summary>
    public static AppPresence ParsePresence(string json)
    {
        JObject result;
        try
        {
            result = JObject.Parse(json ?? string.Empty);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Zły wynik odczytu obecności AppX: " + ex.Message, ex);
        }

        var installed = Flag(result, "Installed");
        var provisioned = Flag(result, "Provisioned");
        return installed ? AppPresence.Present
            : provisioned ? AppPresence.ImageOnly
            : AppPresence.Absent;
    }

    private static bool Flag(JObject result, string name)
        => result[name] is { Type: JTokenType.Boolean } token
            ? token.Value<bool>()
            : throw new FormatException("Brak pola " + name + " w wyniku odczytu obecności AppX.");
}
