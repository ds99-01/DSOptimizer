using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Install;

/// <summary>Kategoria listy instalacji; kolejność wartości = kolejność w UI.</summary>
public enum InstallCategory
{
    Browsers,
    Messengers,
    Multimedia,
    Documents,
    Tools,
    RemoteHelp,
    Games,
    Runtimes,
}

/// <summary>
/// Pozycja <c>apps.json</c>: aplikacja instalowana przez winget po dokładnym <see cref="WingetId"/> albo – tylko pozycje z
/// <see cref="GitHubSources"/> – z wydania repozytorium <see cref="GitHub"/> (dokładnie jedno z nich). <see cref="Match"/> to reguła
/// dopasowania do listy zainstalowanych (ten sam format co <c>apps-bloat.json</c>), <see cref="Source"/> = dowód oficjalnego pakietu.
/// </summary>
public sealed record InstallEntry(
    string Id,
    string? WingetId,
    string Title,
    string Summary,
    InstallCategory Category,
    AppMatch Match,
    string? Warning,
    string Source,
    string? GitHub = null)
{
    /// <summary>Źródło instalatora: winget, gdy jest <see cref="WingetId"/>, inaczej GitHub.</summary>
    public InstallOrigin Origin => GitHubSources.OriginOf(WingetId);

    /// <summary>Klucz pakietu: <see cref="WingetId"/> albo <c>github:&lt;repo&gt;</c>.</summary>
    public string PackageKey => GitHubSources.PackageKey(WingetId, GitHub);
}

/// <summary>Błąd pliku listy instalacji: id wpisu i pole, którego dotyczy.</summary>
public sealed class InstallCatalogException : Exception
{
    public InstallCatalogException(string entryId, string field, string message)
        : base(Format(entryId, field, message))
    {
        EntryId = entryId;
        Field = field;
    }

    public InstallCatalogException(string entryId, string field, string message, Exception innerException)
        : base(Format(entryId, field, message), innerException)
    {
        EntryId = entryId;
        Field = field;
    }

    public string EntryId { get; }

    public string Field { get; }

    private static string Format(string entryId, string field, string message)
        => L.F("apps: {0}, pole {1}: {2}", entryId, field, message);
}

/// <summary>Plik <c>apps.json</c> (obiekt <c>{ "entries": [ … ] }</c>): lista aplikacji do instalacji, kolejność ma znaczenie.</summary>
public sealed class AppsInstallCatalog
{
    private const int MaxTitle = 60;
    private const int MaxSummary = 160;
    private static readonly Regex IdPattern = new(@"^install\.[a-z0-9-]+\z", RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, InstallCategory> Categories = new Dictionary<string, InstallCategory>(StringComparer.Ordinal)
    {
        ["browsers"] = InstallCategory.Browsers,
        ["messengers"] = InstallCategory.Messengers,
        ["multimedia"] = InstallCategory.Multimedia,
        ["documents"] = InstallCategory.Documents,
        ["tools"] = InstallCategory.Tools,
        ["remote-help"] = InstallCategory.RemoteHelp,
        ["games"] = InstallCategory.Games,
        ["runtimes"] = InstallCategory.Runtimes,
    };

    private AppsInstallCatalog(IReadOnlyList<InstallEntry> entries) => Entries = entries;

    private static string FileLabel => L.T("(plik)");

    private static string GitHubNotAllowed
        => L.T("pole github jest dozwolone tylko dla pozycji z listy w kodzie (install.rustdesk → rustdesk/rustdesk)");

    public IReadOnlyList<InstallEntry> Entries { get; }

    /// <summary>Wpis po <c>id</c> (<c>install.&lt;nazwa&gt;</c>, dokładnie); brak = <c>null</c>.</summary>
    public InstallEntry? Find(string id) => Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    /// <summary>Wpis po identyfikatorze winget (bez rozróżniania wielkości liter, jak winget); brak = <c>null</c>. Wpisy bez <c>wingetId</c> pomija.</summary>
    public InstallEntry? FindByWingetId(string wingetId)
        => Entries.FirstOrDefault(e => e.WingetId is not null && string.Equals(e.WingetId, wingetId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Czyta i waliduje plik; błąd to <see cref="InstallCatalogException"/> z id wpisu i polem.</summary>
    public static AppsInstallCatalog Load(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonReaderException ex)
        {
            throw new InstallCatalogException(FileLabel, "json", L.F("błędny JSON – {0}", ex.Message), ex);
        }

        if (root["entries"] is not JArray items)
        {
            throw new InstallCatalogException(FileLabel, "entries", L.F("brak tablicy „{0}”", "entries"));
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wingetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<InstallEntry>(items.Count);
        var index = 0;
        foreach (var item in items)
        {
            index++;
            var label = L.F("(wpis {0})", index.ToString(CultureInfo.InvariantCulture));
            if (item is not JObject obj)
            {
                throw new InstallCatalogException(label, "entries", L.T("element musi być obiektem"));
            }

            entries.Add(ParseEntry(obj, label, ids, wingetIds));
        }

        return new AppsInstallCatalog(entries);
    }

    /// <summary>Id pozycji listy: <c>install.&lt;nazwa&gt;</c> (małe litery, cyfry, „-”); ta sama reguła w <c>apps.json</c> i w CLI.</summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern.IsMatch(id);

    private static InstallEntry ParseEntry(JObject obj, string label, HashSet<string> ids, HashSet<string> wingetIds)
    {
        var id = Text(obj, "id", label) ?? throw new InstallCatalogException(label, "id", L.T("id jest wymagane"));
        if (!IsValidId(id))
        {
            throw new InstallCatalogException(id, "id", L.T("id musi mieć postać install.<nazwa> (małe litery, cyfry, „-”)"));
        }

        if (!ids.Add(id))
        {
            throw new InstallCatalogException(id, "id", L.T("id występuje więcej niż raz"));
        }

        var wingetId = Text(obj, "wingetId", id);
        var gitHub = Text(obj, "github", id);
        if (wingetId is not null && gitHub is not null)
        {
            throw new InstallCatalogException(id, "github", L.T("podaj dokładnie jedno z pól wingetId albo github"));
        }

        if (gitHub is not null)
        {
            if (!GitHubSources.IsAllowed(id, gitHub))
            {
                throw new InstallCatalogException(id, "github", GitHubNotAllowed);
            }
        }
        else if (wingetId is null)
        {
            throw new InstallCatalogException(id, "wingetId", L.T("pole jest wymagane i niepuste (albo github dla pozycji z listy w kodzie)"));
        }
        else if (!WingetArguments.IsValidWingetId(wingetId))
        {
            throw new InstallCatalogException(id, "wingetId", L.T("nieprawidłowy identyfikator winget"));
        }
        else if (!wingetIds.Add(wingetId))
        {
            throw new InstallCatalogException(id, "wingetId", L.T("identyfikator winget występuje więcej niż raz"));
        }

        var title = Limited(obj, id, "title", MaxTitle);
        var summary = Limited(obj, id, "summary", MaxSummary);
        var categoryName = Required(obj, id, "category");
        if (!Categories.TryGetValue(categoryName, out var category))
        {
            throw new InstallCatalogException(id, "category", L.F("dozwolone: {0}", string.Join(", ", Categories.Keys)));
        }

        var match = AppMatchJson.Parse(obj, id, Fail);
        var warning = Text(obj, "warning", id);
        var source = Required(obj, id, "source");
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InstallCatalogException(id, "source", L.T("adres musi być bezwzględnym https"));
        }

        return new InstallEntry(id, wingetId, title, summary, category, match, warning, source, gitHub);
    }

    private static InstallCatalogException Fail(string id, string field, string message) => new(id, field, message);

    private static string Limited(JObject obj, string id, string field, int max)
    {
        var value = Required(obj, id, field);
        return value.Length > max
            ? throw new InstallCatalogException(id, field, L.F("najwyżej {0} znaków", max.ToString(CultureInfo.InvariantCulture)))
            : value;
    }

    private static string Required(JObject obj, string id, string field)
        => Text(obj, field, id) ?? throw new InstallCatalogException(id, field, L.T("pole jest wymagane i niepuste"));

    private static string? Text(JObject obj, string field, string id) => AppMatchJson.Text(obj, field, id, Fail);
}
