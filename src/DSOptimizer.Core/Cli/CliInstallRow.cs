using DSOptimizer.Core.Install;
using Newtonsoft.Json;

namespace DSOptimizer.Core.Cli;

/// <summary>
/// Pozycja listy <c>apps.json</c> w raporcie <c>--list-install</c> ze statusem „zainstalowana” (reguła <c>match</c>) i źródłem
/// instalatora: <see cref="Source"/> = <c>winget</c> (z <see cref="WingetId"/>) albo <c>github</c> (z repozytorium <see cref="GitHub"/>,
/// bez pola <c>wingetId</c> w JSON).
/// </summary>
public sealed class CliInstallRow
{
    public required string Id { get; init; }

    /// <summary>Źródło instalatora: <see cref="CliInstallSource.Winget"/> albo <see cref="CliInstallSource.GitHub"/>.</summary>
    public required string Source { get; init; }

    /// <summary>Identyfikator pakietu winget; <c>null</c> (pole pominięte w JSON) przy pozycji z GitHuba.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public required string? WingetId { get; init; }

    /// <summary>Repozytorium GitHuba (<c>rustdesk/rustdesk</c>); <c>null</c> przy pozycji winget.</summary>
    public string? GitHub { get; init; }

    public required string Title { get; init; }

    public InstallCategory Category { get; init; }

    public bool Installed { get; init; }
}
