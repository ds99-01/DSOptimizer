using System;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Pozycja serii instalacji: wpis listy <c>apps.json</c> (<see cref="Match"/> i <see cref="Category"/> z listy) albo wynik
/// wyszukiwania winget (<c>Id = "winget:" + WingetId</c>, bez kategorii i bez reguły dopasowania). Pozycja listy z GitHuba ma
/// <see cref="WingetId"/> = <c>null</c> i repozytorium w <see cref="GitHub"/>.
/// </summary>
public sealed record InstallTarget(
    string Id,
    string? WingetId,
    string Title,
    InstallCategory? Category,
    AppMatch? Match,
    string? Warning,
    string? GitHub = null)
{
    /// <summary>Prefiks Id pozycji spoza listy.</summary>
    public const string SearchPrefix = "winget:";

    /// <summary>Źródło instalatora: winget, gdy jest <see cref="WingetId"/>, inaczej GitHub.</summary>
    public InstallOrigin Origin => GitHubSources.OriginOf(WingetId);

    /// <summary>Klucz pakietu: <see cref="WingetId"/> albo <c>github:&lt;repo&gt;</c> (duplikaty w serii, CLI i zaznaczeniu).</summary>
    public string PackageKey => GitHubSources.PackageKey(WingetId, GitHub);

    public static InstallTarget FromEntry(InstallEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        return new InstallTarget(entry.Id, entry.WingetId, entry.Title, entry.Category, entry.Match, entry.Warning, entry.GitHub);
    }

    /// <summary>Wynik wyszukiwania; Id obcięte „…” albo nieprawidłowe = <see cref="ArgumentException"/>. Pusta nazwa = tytuł z Id.</summary>
    public static InstallTarget FromSearch(string wingetId, string name)
    {
        if (!WingetArguments.IsValidWingetId(wingetId))
        {
            throw new ArgumentException("Nieprawidłowy identyfikator winget: " + wingetId, nameof(wingetId));
        }

        var title = string.IsNullOrWhiteSpace(name) ? wingetId : name.Trim();
        return new InstallTarget(SearchPrefix + wingetId, wingetId, title, null, null, null);
    }
}
