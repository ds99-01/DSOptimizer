using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Install;

namespace DSOptimizer.Core.Cli;

/// <summary>
/// Usługi poleceń <c>--list-install</c>/<c>--install-apps</c>/<c>--search-apps</c> (składa je host; testy podają fałszywe, nigdy
/// prawdziwy winget).
/// </summary>
public sealed class CliInstallServices
{
    public required IWingetRunner Runner { get; init; }

    public required IWingetLogs Logs { get; init; }

    public required IInstalledAppsReader Reader { get; init; }

    /// <summary>Lista <c>apps.json</c>: wbudowana albo z <c>--apps-catalog</c>.</summary>
    public required AppsInstallCatalog Catalog { get; init; }

    /// <summary>SID konta procesu DSOptimizera (instalacja działa tylko, gdy to konto zalogowanego użytkownika).</summary>
    public required string ProcessSid { get; init; }

    /// <summary>SID → nazwa konta (do tekstu odmowy); null = nieznana.</summary>
    public required Func<string, string?> AccountName { get; init; }

    /// <summary>Uśpienie między odczytami zwrotnymi po instalacji (R16); null = <c>Thread.Sleep</c>.</summary>
    public Action<TimeSpan>? Sleep { get; init; }

    /// <summary>
    /// Usługi instalacji z GitHuba (RustDesk): host podaje <c>AppComposition.GitHub</c>; <c>null</c> = pozycja z GitHuba kończy się
    /// „Nie wykonano – instalacja z GitHuba nie jest dostępna w tym uruchomieniu” bez żadnego połączenia.
    /// </summary>
    public GitHubInstallServices? GitHub { get; init; }
}
