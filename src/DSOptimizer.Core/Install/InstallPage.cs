using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Wiersz strony „Instalacja”. <see cref="CanSelect"/> = jedyna reguła pola wyboru; <see cref="IsInstalled"/> = status
/// „Zainstalowana” (rysuje go widok); <see cref="Note"/> = notatka pod wierszem (Id skrócone w wynikach wyszukiwania).
/// Opis pozycji listy: <c>AppsInstallCatalog.Find(Target.Id)?.Summary</c>.
/// </summary>
public sealed record InstallRow(InstallTarget Target, bool IsInstalled, bool CanSelect, string? Note);

/// <summary>Rodzaj pozycji listy kategorii strony „Instalacja”.</summary>
public enum InstallGroupKind
{
    /// <summary>„Wszystkie” – cała lista z nagłówkami kategorii (D1).</summary>
    All,

    Category,

    /// <summary>"Zaznaczone": zaznaczenie zna tylko ViewModel, wiec Contains zawsze mowi "nie".</summary>
    Selected,

    /// <summary>„Wyniki wyszukiwania” – wiersze z <c>winget search</c>.</summary>
    Search,
}

/// <summary>
/// Pozycja listy kategorii strony (wariant A): „Wszystkie”, jedna kategoria listy albo „Wyniki wyszukiwania”.
/// <see cref="Contains"/> = jedyna reguła, które wiersze pokazuje lista po wybraniu tej pozycji.
/// </summary>
public sealed record InstallGroup
{
    private InstallGroup(InstallGroupKind kind, InstallCategory? category)
    {
        Kind = kind;
        Category = category;
    }

    public static InstallGroup All { get; } = new(InstallGroupKind.All, null);

    public static InstallGroup Search { get; } = new(InstallGroupKind.Search, null);

    public static InstallGroup Selected { get; } = new(InstallGroupKind.Selected, null);

    public InstallGroupKind Kind { get; }

    /// <summary>Kategoria (tylko <see cref="InstallGroupKind.Category"/>).</summary>
    public InstallCategory? Category { get; }

    public string Name => Kind switch
    {
        InstallGroupKind.All => InstallText.AllCategoriesName,
        InstallGroupKind.Search => InstallText.SearchGroupName,
        InstallGroupKind.Selected => InstallText.SelectedGroupName,
        _ => InstallText.CategoryName(Category!.Value),
    };

    /// <summary>Lista pokazuje nagłówki kategorii nad wierszami („Wszystkie” i „Zaznaczone”).</summary>
    public bool ShowsCategoryHeaders => Kind is InstallGroupKind.All or InstallGroupKind.Selected;

    public static InstallGroup Of(InstallCategory category) => new(InstallGroupKind.Category, category);

    public bool Contains(InstallRow row)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        return Kind switch
        {
            InstallGroupKind.All => row.Target.Category is not null,
            InstallGroupKind.Search => row.Target.Category is null,
            InstallGroupKind.Selected => false,
            _ => row.Target.Category == Category,
        };
    }
}

/// <summary>Wiersze strony „Instalacja” (bez WPF): lista z kategoriami i wyniki wyszukiwania winget.</summary>
public static class InstallPage
{
    /// <summary>
    /// Pozycje listy w kolejności kategorii (<see cref="InstallCategory"/>), w kategorii w kolejności pliku. Pole wyboru tylko, gdy
    /// lista zainstalowanych została odczytana (<paramref name="listRead"/>), pozycja nie jest zainstalowana (<paramref name="raw"/> =
    /// surowy odczyt, jak w <see cref="InstallStatus"/>) i jej źródło może ruszyć: pozycja winget – winget gotowy, pozycja z GitHuba –
    /// <see cref="WingetAvailability.AllowsGitHub"/> (także przy niedziałającym winget, decyzja planu 1; odmowa konta blokuje).
    /// </summary>
    public static IReadOnlyList<InstallRow> CatalogRows(AppsInstallCatalog catalog, IReadOnlyList<InstalledApp> raw, WingetStatus winget, bool listRead = true)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (raw is null)
        {
            throw new ArgumentNullException(nameof(raw));
        }

        if (winget is null)
        {
            throw new ArgumentNullException(nameof(winget));
        }

        var ready = winget.State == WingetState.Ready;
        var gitHub = WingetAvailability.AllowsGitHub(winget);
        return catalog.Entries
            .OrderBy(entry => (int)entry.Category)
            .Select(entry =>
            {
                var target = InstallTarget.FromEntry(entry);
                var installed = InstallStatus.IsInstalled(entry, raw);
                var sourceReady = target.Origin == InstallOrigin.GitHub ? gitHub : ready;
                return new InstallRow(target, installed, listRead && !installed && sourceReady, null);
            })
            .ToList();
    }

    /// <summary>
    /// Wyniki <c>winget search</c> w kolejności winget. Wynik z <c>wingetId</c> pozycji listy (albo powtórzony) jest pominięty,
    /// bez rozróżniania wielkości liter: pozycja listy pokazuje się raz, w swojej kategorii. Wiersz nie do instalacji
    /// (<see cref="WingetSearchRow.Installable"/> = false) jest widoczny bez pola wyboru i z notatką <see cref="InstallText.TruncatedIdNote"/>.
    /// </summary>
    public static IReadOnlyList<InstallRow> SearchRows(IReadOnlyList<WingetSearchRow> found, AppsInstallCatalog catalog, WingetStatus winget)
    {
        if (found is null)
        {
            throw new ArgumentNullException(nameof(found));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (winget is null)
        {
            throw new ArgumentNullException(nameof(winget));
        }

        var ready = winget.State == WingetState.Ready;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<InstallRow>();
        foreach (var row in found)
        {
            if (row.WingetId.Length > 0 && (catalog.FindByWingetId(row.WingetId) is not null || GitHubSources.WingetAliases.ContainsKey(row.WingetId) || !seen.Add(row.WingetId)))
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(row.Name) ? row.WingetId : row.Name.Trim();

            // Bez InstallTarget.FromSearch: ten odrzuca Id obcięte, a wiersz nie do instalacji ma być widoczny (spec 7).
            var target = new InstallTarget(InstallTarget.SearchPrefix + row.WingetId, row.WingetId, title, null, null, null);
            rows.Add(row.Installable
                ? new InstallRow(target, false, ready, null)
                : new InstallRow(target, false, false, InstallText.TruncatedIdNote));
        }

        return rows;
    }

    /// <summary>
    /// Id pozycji listy (<c>install.*</c>) trafionych przez wynik winget: po <c>wingetId</c> pozycji albo po aliasie
    /// <see cref="GitHubSources.WingetAliases"/>, bez powtórzeń, w kolejności winget.
    /// </summary>
    public static IReadOnlyList<string> CatalogIdsIn(IReadOnlyList<WingetSearchRow> found, AppsInstallCatalog catalog)
    {
        if (found is null)
        {
            throw new ArgumentNullException(nameof(found));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        var ids = new List<string>();
        foreach (var row in found)
        {
            if (row.WingetId.Length == 0)
            {
                continue;
            }

            var id = catalog.FindByWingetId(row.WingetId)?.Id
                ?? (GitHubSources.WingetAliases.TryGetValue(row.WingetId, out var alias) ? alias : null);
            if (id is not null && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Lista kategorii strony (D1, Ruling P3): „Wszystkie” na górze, potem kategorie, które mają pozycje, w kolejności
    /// <see cref="InstallCategory"/>. Grupa „Wyniki wyszukiwania” (<see cref="InstallGroup.Search"/>) jest osobno, na końcu.
    /// </summary>
    public static IReadOnlyList<InstallGroup> Groups(AppsInstallCatalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        return new[] { InstallGroup.All, InstallGroup.Selected }
            .Concat(catalog.Entries.Select(entry => entry.Category).Distinct().OrderBy(category => (int)category).Select(InstallGroup.Of))
            .ToList();
    }

    /// <summary>
    /// Wiersz, który pokazuje link „Pokaż RustDesk” w komunikacie „winget nie działa” (M5): pierwsza pozycja z GitHuba z polem wyboru;
    /// zainstalowana albo zablokowana (konto, brak odczytu) = <c>null</c> – link i dopisek o RustDesk znikają.
    /// </summary>
    public static InstallRow? GitHubRowToShow(IReadOnlyList<InstallRow> rows)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        return rows.FirstOrDefault(row => row.Target.Origin == InstallOrigin.GitHub && row.CanSelect);
    }

    /// <summary>Nagłówek wiersza na liście „Wszystkie”: nazwa kategorii pozycji albo „Wyniki wyszukiwania”.</summary>
    public static string CategoryHeader(InstallRow row)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        return row.Target.Category is { } category ? InstallText.CategoryName(category) : InstallText.SearchGroupName;
    }

    /// <summary>Wynik pozycji, po którym aplikacja jest na komputerze (podsumowanie „N z M zainstalowane”, odznaczenie).</summary>
    public static bool CountsAsInstalled(TweakOutcome outcome)
        => outcome is TweakOutcome.Installed or TweakOutcome.AlreadyInstalled or TweakOutcome.InstalledUnverified;
}

