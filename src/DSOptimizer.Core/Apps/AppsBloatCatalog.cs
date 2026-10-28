using System;
using System.Collections.Generic;
using DSOptimizer.Core.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Apps;

/// <summary>Błąd pliku klasyfikacji aplikacji: id wpisu i pole, którego dotyczy.</summary>
public sealed class AppsCatalogException : Exception
{
    public AppsCatalogException(string entryId, string field, string message)
        : base(L.F("apps-bloat: {0}, pole {1}: {2}", entryId, field, message))
    {
        EntryId = entryId;
        Field = field;
    }

    public AppsCatalogException(string entryId, string field, string message, Exception innerException)
        : base(L.F("apps-bloat: {0}, pole {1}: {2}", entryId, field, message), innerException)
    {
        EntryId = entryId;
        Field = field;
    }

    public string EntryId { get; }

    public string Field { get; }
}

/// <summary>
/// Reguła dopasowania z wzorcami z <c>*</c> (bez regex, bez rozróżniania wielkości liter). <see cref="PackageName"/> dotyczy tylko
/// AppX, <see cref="DisplayName"/> i <see cref="Publisher"/> tylko programów Win32 (gdy podane oba, muszą pasować oba).
/// </summary>
public sealed record AppMatch(string? PackageName, string? DisplayName, string? Publisher)
{
    /// <summary>Czy reguła pasuje do aplikacji tego rodzaju.</summary>
    public bool Matches(InstalledApp app)
    {
        if (app is null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        if (app.Kind == AppKind.Store)
        {
            return PackageName is not null && app.Appx is not null && Wildcard(PackageName, app.Appx.PackageName);
        }

        if (PackageName is not null || (DisplayName is null && Publisher is null))
        {
            return false;
        }

        return (DisplayName is null || Wildcard(DisplayName, app.Name))
            && (Publisher is null || (app.Publisher is not null && Wildcard(Publisher, app.Publisher)));
    }

    private static bool Wildcard(string pattern, string text)
    {
        // Dopasowanie z gwiazdkami: kolejne fragmenty wzorca muszą wystąpić w tekście w tej kolejności.
        var parts = pattern.Split('*');
        var position = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i == 0)
            {
                if (!text.StartsWith(part, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                position = part.Length;
                continue;
            }

            if (i == parts.Length - 1)
            {
                return text.Length - position >= part.Length && text.EndsWith(part, StringComparison.OrdinalIgnoreCase);
            }

            var found = text.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            position = found + part.Length;
        }

        return position == text.Length;
    }
}

/// <summary>Wpis o śmieciu. <see cref="Group"/> to <c>microsoft</c>, <c>producent</c> albo <c>trial</c>.</summary>
public sealed record BloatEntry(
    string Id,
    AppMatch Match,
    string Group,
    bool Suggested,
    string Summary,
    string? Warning,
    string Source,
    string? CleanupUrl);

/// <summary>Wpis listy chronionej: nigdy nie trafia do serii usuwania.</summary>
public sealed record ProtectedEntry(string Id, AppMatch Match, string Reason);

/// <summary>Plik <c>apps-bloat.json</c>: wpisy o śmieciach (<see cref="Entries"/>, kolejność ma znaczenie) i lista chroniona.</summary>
public sealed class AppsBloatCatalog
{
    private const int MaxSummary = 160;

    private static readonly string[] Groups = { "microsoft", "producent", "trial" };

    private static string FileLabel => L.T("(plik)");

    private AppsBloatCatalog(IReadOnlyList<BloatEntry> entries, IReadOnlyList<ProtectedEntry> protectedEntries)
    {
        Entries = entries;
        Protected = protectedEntries;
    }

    public IReadOnlyList<BloatEntry> Entries { get; }

    public IReadOnlyList<ProtectedEntry> Protected { get; }

    /// <summary>Czyta i waliduje plik; błąd to <see cref="AppsCatalogException"/> z id wpisu i polem.</summary>
    public static AppsBloatCatalog Load(string json)
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
            throw new AppsCatalogException(FileLabel, "json", L.F("błędny JSON – {0}", ex.Message), ex);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<BloatEntry>();
        var index = 0;
        foreach (var item in Items(root, "entries"))
        {
            index++;
            var obj = AsObject(item, index);
            entries.Add(ParseEntry(obj, Id(obj, index), ids));
        }

        var protectedEntries = new List<ProtectedEntry>();
        index = 0;
        foreach (var item in Items(root, "protected"))
        {
            index++;
            var obj = AsObject(item, index);
            protectedEntries.Add(ParseProtected(obj, Id(obj, index), ids));
        }

        return new AppsBloatCatalog(entries, protectedEntries);
    }

    private static JArray Items(JObject root, string name)
        => root[name] as JArray
            ?? throw new AppsCatalogException(FileLabel, name, L.F("brak tablicy „{0}”", name));

    private static JObject AsObject(JToken item, int index)
        => item as JObject
            ?? throw new AppsCatalogException(Entry(index), "entries", L.T("element musi być obiektem"));

    /// <summary>Etykieta wpisu bez id, np. „(wpis 3)”.</summary>
    private static string Entry(int index) => L.F("(wpis {0})", index);

    private static string Id(JObject obj, int index)
    {
        var id = Text(obj, "id", Entry(index));
        return string.IsNullOrWhiteSpace(id)
            ? throw new AppsCatalogException(Entry(index), "id", L.T("id jest wymagane"))
            : id!;
    }

    private static BloatEntry ParseEntry(JObject obj, string id, HashSet<string> ids)
    {
        CheckUnique(id, ids);
        var match = ParseMatch(obj, id);
        var group = Required(obj, id, "group");
        if (Array.IndexOf(Groups, group) < 0)
        {
            throw new AppsCatalogException(id, "group", L.F("dozwolone: {0}", string.Join(", ", Groups)));
        }

        if (obj["suggested"]?.Type != JTokenType.Boolean)
        {
            throw new AppsCatalogException(id, "suggested", L.T("wymagana wartość true/false"));
        }

        var summary = Required(obj, id, "summary");
        if (summary.Length > MaxSummary)
        {
            throw new AppsCatalogException(id, "summary", L.F("najwyżej {0} znaków", MaxSummary));
        }

        var source = Required(obj, id, "source");
        var url = Text(obj, "cleanupUrl", id);
        if (url is not null
            && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AppsCatalogException(id, "cleanupUrl", L.T("adres musi być bezwzględnym https"));
        }

        // Teksty dla człowieka w języku interfejsu dopiero po walidacji: limit długości dotyczy polskiego oryginału.
        var warning = Text(obj, "warning", id);
        return new BloatEntry(
            id,
            match,
            group,
            obj["suggested"]!.Value<bool>(),
            L.Data(summary),
            warning is null ? null : L.Data(warning),
            source,
            url);
    }

    private static ProtectedEntry ParseProtected(JObject obj, string id, HashSet<string> ids)
    {
        CheckUnique(id, ids);
        var match = ParseMatch(obj, id);
        return new ProtectedEntry(id, match, L.Data(Required(obj, id, "reason")));
    }

    private static void CheckUnique(string id, HashSet<string> ids)
    {
        if (!ids.Add(id))
        {
            throw new AppsCatalogException(id, "id", L.T("id występuje więcej niż raz"));
        }
    }

    private static AppsCatalogException Fail(string id, string field, string message) => new(id, field, message);

    private static AppMatch ParseMatch(JObject obj, string id) => AppMatchJson.Parse(obj, id, Fail);

    private static string Required(JObject obj, string id, string field)
    {
        var value = Text(obj, field, id);
        return value ?? throw new AppsCatalogException(id, field, L.T("pole jest wymagane i niepuste"));
    }

    private static string? Text(JObject obj, string field, string id) => AppMatchJson.Text(obj, field, id, Fail);
}
