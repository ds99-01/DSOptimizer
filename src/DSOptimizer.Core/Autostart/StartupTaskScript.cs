using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Core.Autostart;

/// <summary>Pakiet (albo jedno zadanie pakietu) pominięty przy odczycie zadań startowych: pełna nazwa pakietu i surowy powód.</summary>
public sealed record SkippedStartupPackage(string PackageFullName, string Reason);

/// <summary>
/// Skrypt PowerShell odczytu zadań startowych aplikacji ze Sklepu (uruchamiany przez <c>IScriptRunner</c>, jak <see cref="AppxScript"/>).
/// Wejście: <c>$UserSid</c>; <c>Get-AppxPackage -User $UserSid</c> (konto zalogowane, nie konto procesu). Dla pakietów z
/// <c>InstallLocation</c>, których <c>AppxManifest.xml</c> zawiera <c>windows.startupTask</c>, każdy węzeł
/// <c>Extension[@Category='windows.startupTask']</c> daje zadanie: <c>StartupTask</c> (<c>TaskId</c>, <c>Enabled</c>, <c>DisplayName</c>),
/// <c>Executable</c> z rozszerzenia, a bez niego z nadrzędnej <c>Application</c>, i <c>Properties/PublisherDisplayName</c> pakietu.
/// Manifest czytany tylko do odczytu (<c>FileShare</c> ReadWrite, Delete), najwyżej 4 MB, przez <c>System.Xml.XmlDocument</c> z zakazem DTD
/// i bez rozwiązywania zasobów zewnętrznych. Pakiet z nieczytelnym manifestem = obiekt <c>{PackageFullName, Error}</c>, pozostałe są czytane.
/// Wyjście: jedna linia JSON, tablica obiektów (pusta = <c>[]</c>); nazwy SUROWE (mogą być <c>ms-resource:</c>), rozwija je kod C#.
/// </summary>
public static class StartupTaskScript
{
    public const string Body = """
        $maxManifestBytes = 4MB
        $taskXPath = "//*[local-name()='Extension' and @Category='windows.startupTask']"
        $publisherXPath = "/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='PublisherDisplayName']"

        function Read-DsoManifestText {
            param([string]$Path)
            $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]'ReadWrite, Delete')
            try {
                if ($stream.Length -gt $maxManifestBytes) { throw ('AppxManifest.xml: {0} B' -f $stream.Length) }
                $reader = New-Object System.IO.StreamReader($stream, $true)
                try { return $reader.ReadToEnd() }
                finally { $reader.Dispose() }
            }
            finally { $stream.Dispose() }
        }

        function Get-DsoStartupTasks {
            param($Package)
            $location = [string]$Package.InstallLocation
            if ([string]::IsNullOrWhiteSpace($location)) { return }
            $manifest = Join-Path $location 'AppxManifest.xml'
            if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { return }
            $text = Read-DsoManifestText $manifest
            if ($text.IndexOf('windows.startupTask', [System.StringComparison]::OrdinalIgnoreCase) -lt 0) { return }

            $settings = New-Object System.Xml.XmlReaderSettings
            $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $xml = New-Object System.Xml.XmlDocument
            $xml.XmlResolver = $null
            $reader = [System.Xml.XmlReader]::Create((New-Object System.IO.StringReader($text)), $settings)
            try { $xml.Load($reader) }
            finally { $reader.Dispose() }

            $publisherNode = $xml.SelectSingleNode($publisherXPath)
            $publisher = $null
            if ($null -ne $publisherNode) { $publisher = [string]$publisherNode.InnerText }

            foreach ($extension in @($xml.SelectNodes($taskXPath))) {
                $task = $extension.SelectSingleNode("*[local-name()='StartupTask']")
                if ($null -eq $task) { continue }
                $executable = $extension.GetAttribute('Executable')
                if ([string]::IsNullOrWhiteSpace($executable)) {
                    $executable = $null
                    $application = $extension.SelectSingleNode("ancestor::*[local-name()='Application'][1]")
                    if ($null -ne $application) { $executable = $application.GetAttribute('Executable') }
                }

                [pscustomobject][ordered]@{
                    PackageName          = [string]$Package.Name
                    PackageFullName      = [string]$Package.PackageFullName
                    PackageFamilyName    = [string]$Package.PackageFamilyName
                    InstallLocation      = $location
                    TaskId               = $task.GetAttribute('TaskId')
                    Enabled              = $task.GetAttribute('Enabled')
                    DisplayName          = $task.GetAttribute('DisplayName')
                    PublisherDisplayName = $publisher
                    Executable           = $executable
                }
            }
        }

        $result = @(foreach ($package in @(Get-AppxPackage -User $UserSid)) {
            try { Get-DsoStartupTasks $package }
            catch {
                [pscustomobject][ordered]@{ PackageFullName = [string]$package.PackageFullName; Error = [string]$_.Exception.Message }
            }
        })
        ConvertTo-Json -InputObject $result -Compress
        """;

    /// <summary>
    /// Zadania z JSON-a skryptu (nazwy surowe). Obiekt <c>{PackageFullName, Error}</c> i zadanie bez <c>TaskId</c> albo z <c>Enabled</c> innym
    /// niż <c>true</c>/<c>false</c>/<c>1</c>/<c>0</c> trafiają do <paramref name="skipped"/>, nie do wyniku. Zły JSON albo brak pola pakietu
    /// (<c>PackageName</c>, <c>PackageFullName</c>, <c>PackageFamilyName</c>, <c>InstallLocation</c>) = <see cref="FormatException"/>.
    /// </summary>
    public static IReadOnlyList<StartupTaskManifest> Parse(string json, ICollection<SkippedStartupPackage>? skipped = null)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        var tasks = new List<StartupTaskManifest>();
        foreach (var item in AppxPackages.ReadItems(json))
        {
            if (AppxPackages.OptionalText(item, "Error") is { } error)
            {
                skipped?.Add(new SkippedStartupPackage(AppxPackages.OptionalText(item, "PackageFullName") ?? string.Empty, error));
                continue;
            }

            var fullName = AppxPackages.RequiredText(item, "PackageFullName");
            var name = AppxPackages.RequiredText(item, "PackageName");
            var family = AppxPackages.RequiredText(item, "PackageFamilyName");
            var location = AppxPackages.RequiredText(item, "InstallLocation");
            var taskId = AppxPackages.OptionalText(item, "TaskId");
            if (taskId is null)
            {
                skipped?.Add(new SkippedStartupPackage(fullName, "TaskId"));
                continue;
            }

            var enabledText = AppxPackages.OptionalText(item, "Enabled");
            if (!TryParseEnabled(enabledText, out var enabled))
            {
                skipped?.Add(new SkippedStartupPackage(fullName, taskId + ": Enabled=\"" + enabledText + "\""));
                continue;
            }

            tasks.Add(new StartupTaskManifest(
                name,
                fullName,
                family,
                location,
                taskId,
                enabled,
                AppxPackages.OptionalText(item, "DisplayName"),
                AppxPackages.OptionalText(item, "PublisherDisplayName"),
                AppxPackages.OptionalText(item, "Executable")));
        }

        return tasks;
    }

    /// <summary>Atrybut <c>Enabled</c> jako <c>xs:boolean</c>: <c>true</c>/<c>1</c> albo <c>false</c>/<c>0</c>; inna wartość = nieznana.</summary>
    private static bool TryParseEnabled(string? text, out bool enabled)
    {
        switch (text?.Trim())
        {
            case "true":
            case "1":
                enabled = true;
                return true;
            case "false":
            case "0":
                enabled = false;
                return true;
            default:
                enabled = false;
                return false;
        }
    }
}
