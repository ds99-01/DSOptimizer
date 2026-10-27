using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Install;

/// <summary>Skąd program bierze instalator pozycji: winget (domyślnie) albo wydanie z GitHuba (tylko pozycje z <see cref="GitHubSources"/>).</summary>
public enum InstallOrigin
{
    Winget,
    GitHub,
}

/// <summary>
/// Jedyne pozycje, które mogą instalować się z GitHuba (spec 1: jedna pozycja, jedno repo, reguły w kodzie, nie w JSON).
/// Pole <c>github</c> w <c>apps.json</c> spoza tej listy = błąd pliku.
/// </summary>
public static class GitHubSources
{
    /// <summary>Prefiks klucza pakietu pozycji z GitHuba (<c>github:&lt;repo&gt;</c>), obok identyfikatora winget.</summary>
    public const string KeyPrefix = "github:";

    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        [RustDeskSource.EntryId] = RustDeskSource.Repository,
    };

    /// <summary>
    /// Id winget pozycji z GitHuba → Id pozycji listy (bez wielkości liter). Wynik wyszukiwania z takim Id nie jest osobnym pakietem:
    /// pozycja jest już na liście, a drugiej drogi instalacji RustDeska nie ma (spec 11.2).
    /// </summary>
    public static IReadOnlyDictionary<string, string> WingetAliases { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RustDesk.RustDesk"] = RustDeskSource.EntryId,
        };

    /// <summary>Czy pozycja <paramref name="id"/> może instalować się z repozytorium <paramref name="repository"/> (porównanie dokładne).</summary>
    public static bool IsAllowed(string id, string repository)
        => id is not null
           && repository is not null
           && Allowed.TryGetValue(id, out var allowed)
           && string.Equals(allowed, repository, StringComparison.Ordinal);

    /// <summary>Klucz pakietu: identyfikator winget albo <c>github:&lt;repo&gt;</c> (duplikaty w serii, CLI i zaznaczeniu).</summary>
    internal static string PackageKey(string? wingetId, string? gitHub) => wingetId ?? KeyPrefix + gitHub;

    internal static InstallOrigin OriginOf(string? wingetId) => wingetId is null ? InstallOrigin.GitHub : InstallOrigin.Winget;
}

