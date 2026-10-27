using DSOptimizer.Core.Engine;
using Newtonsoft.Json;

namespace DSOptimizer.Core.Cli;

/// <summary>
/// Wynik jednej pozycji w raporcie <c>--install-apps</c> (spec 5.5). Pozycja z GitHuba (RustDesk): <see cref="Source"/> = <c>github</c>,
/// repozytorium w <see cref="GitHub"/>, kod <c>msiexec</c> w <see cref="MsiCode"/>, bez pola <c>wingetId</c> i z pustym
/// <see cref="WingetCode"/> (decyzja planu 10).
/// </summary>
public sealed class CliInstallItem
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

    public TweakOutcome Outcome { get; init; }

    /// <summary>Kod wyjścia winget szesnastkowo (<c>0x8A150107</c>); <c>null</c>, gdy winget nie ruszył albo się nie zakończył.</summary>
    public string? WingetCode { get; init; }

    /// <summary>Kod wyjścia <c>msiexec</c> dziesiętnie (0, 3010, 1603…); <c>null</c> przy pozycji winget albo gdy msiexec nie ruszył.</summary>
    public int? MsiCode { get; init; }

    public required string Message { get; init; }

    /// <summary>Plik logu winget (<c>--log</c>); <c>null</c>, gdy instalacja nie wystartowała.</summary>
    public string? LogPath { get; init; }

    public bool RequiresReboot { get; init; }

    /// <summary>Wersja z linii „Found … Version …” winget; <c>null</c>, gdy jej nie podał.</summary>
    public string? Version { get; init; }
}
