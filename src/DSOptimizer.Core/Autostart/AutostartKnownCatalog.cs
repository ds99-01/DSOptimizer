using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Autostart;

/// <summary>Ocena pozycji z bazy znanych pozycji: brak oceny, chroniona (kłódka) albo można wyłączyć (sugestia).</summary>
public enum AutostartVerdict
{
    None,
    Protected,
    CanDisable,
}

/// <summary>Błąd pliku bazy znanych pozycji autostartu: id wpisu i pole, którego dotyczy.</summary>
public sealed class AutostartCatalogException : Exception
{
    public AutostartCatalogException(string entryId, string field, string message)
        : base(L.F("autostart-known: {0}, pole {1}: {2}", entryId, field, message))
    {
        EntryId = entryId;
        Field = field;
    }

    public AutostartCatalogException(string entryId, string field, string message, Exception innerException)
        : base(L.F("autostart-known: {0}, pole {1}: {2}", entryId, field, message), innerException)
    {
        EntryId = entryId;
        Field = field;
    }

    public string EntryId { get; }

    public string Field { get; }
}

/// <summary>
/// Reguła wpisu bazy. Pasuje tylko pozycja z ustalonym plikiem (<see cref="AutostartEntry.FilePath"/>) i wydawcą: nazwa pliku pasuje do
/// <see cref="File"/>, folder pliku (z <c>\</c> na końcu) do któregoś wzorca z <see cref="Folders"/> (nierozwinięty = brak dopasowania;
/// zmienne maszyny wyłącznie z procesu, <c>%LOCALAPPDATA%</c>/<c>%APPDATA%</c> z konta – <see cref="UserEnvironment.TryExpandMachineRooted"/>), wydawca do któregoś wzorca z <see cref="Publishers"/>, a podane <see cref="ValueName"/> i
/// <see cref="PackageFamily"/> są równe nazwie wpisu i rodzinie pakietu. Wzorce z <c>*</c> (<see cref="WildcardText"/>), wszystko bez
/// rozróżniania wielkości liter. Ścieżka z <c>/</c> albo z segmentem <c>.</c>/<c>..</c> nie pasuje nigdy: folder z bazy nie może być
/// obejściem przez ścieżkę względną.
/// </summary>
public sealed record AutostartMatchRule(
    string File,
    IReadOnlyList<string> Folders,
    IReadOnlyList<string> Publishers,
    string? ValueName,
    string? PackageFamily)
{
    public bool Matches(AutostartEntry entry, UserEnvironment environment)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (environment is null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        var publisher = entry.Publisher?.Trim();
        if (string.IsNullOrEmpty(publisher) || !TrySplit(entry.FilePath, out var folder, out var fileName))
        {
            return false;
        }

        return WildcardText.Matches(File, fileName)
            && Folders.Any(pattern => environment.TryExpandMachineRooted(pattern, out var expanded) && WildcardText.Matches(expanded, folder))
            && Publishers.Any(pattern => WildcardText.Matches(pattern.Trim(), publisher!))
            && (ValueName is null || string.Equals(ValueName, entry.EntryName, StringComparison.OrdinalIgnoreCase))
            && (PackageFamily is null || string.Equals(PackageFamily, entry.PackageFamily, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Folder z <c>\</c> na końcu i nazwa pliku; false dla ścieżki bez folderu, z <c>/</c> albo z segmentem <c>.</c>/<c>..</c>.</summary>
    private static bool TrySplit(string? path, out string folder, out string fileName)
    {
        folder = string.Empty;
        fileName = string.Empty;
        if (string.IsNullOrEmpty(path) || path!.IndexOf('/') >= 0)
        {
            return false;
        }

        var cut = path.LastIndexOf('\\');
        if (cut <= 0 || cut == path.Length - 1)
        {
            return false;
        }

        foreach (var segment in path.Split('\\'))
        {
            if (segment is "." or "..")
            {
                return false;
            }
        }

        folder = path.Substring(0, cut + 1);
        fileName = path.Substring(cut + 1);
        return true;
    }
}

/// <summary>Wpis bazy znanych pozycji. <see cref="Summary"/> w języku interfejsu; <see cref="Sources"/> = adresy https i dokumenty pomiaru.</summary>
public sealed record AutostartKnownEntry(
    string Id,
    AutostartMatchRule Match,
    AutostartVerdict Verdict,
    string Summary,
    IReadOnlyList<string> Sources,
    bool Confirmed);

/// <summary>
/// Plik <c>autostart-known.json</c>: <c>{ "entries": [ { id, match { file, folders, publishers, valueName?, packageFamily? }, verdict,
/// summary, source, confirmed } ] }</c>. Dopasowanie: najpierw wpisy chronione, potem „można wyłączyć”, w obrębie kolejność pliku – ochrona
/// wygrywa z każdą sugestią.
/// </summary>
public sealed class AutostartKnownCatalog
{
    private const int MaxSummary = 160;
    private const string VerdictProtected = "protected";
    private const string VerdictCanDisable = "can-disable";

    private static readonly string[] FolderRoots =
    {
        "%SystemRoot%", "%ProgramFiles%", "%ProgramFiles(x86)%", "%ProgramData%", "%LOCALAPPDATA%", "%APPDATA%",
    };

    /// <summary>Korzenie zapisywalne bez administratora (w <c>C:\ProgramData</c> Users tworzą pliki i foldery) – nie dla wpisów chronionych.</summary>
    private static readonly string[] UserWritableRoots = { "%ProgramData%", "%LOCALAPPDATA%", "%APPDATA%" };

    private static readonly Regex MeasurementDocument = new("^docs/zrodla/[0-9a-z.-]+\\.md$", RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<AutostartKnownEntry> _matchOrder;

    private AutostartKnownCatalog(IReadOnlyList<AutostartKnownEntry> entries)
    {
        Entries = entries;
        _matchOrder = entries.Where(e => e.Verdict == AutostartVerdict.Protected)
            .Concat(entries.Where(e => e.Verdict != AutostartVerdict.Protected))
            .ToList();
    }

    /// <summary>Wpisy w kolejności pliku.</summary>
    public IReadOnlyList<AutostartKnownEntry> Entries { get; }

    private static string FileLabel => L.T("(plik)");

    /// <summary>Czyta i waliduje plik; błąd to <see cref="AutostartCatalogException"/> z id wpisu i polem.</summary>
    public static AutostartKnownCatalog Load(string json)
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
            throw new AutostartCatalogException(FileLabel, "json", L.F("błędny JSON – {0}", ex.Message), ex);
        }

        if (root["entries"] is not JArray items)
        {
            throw new AutostartCatalogException(FileLabel, "entries", L.F("brak tablicy „{0}”", "entries"));
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<AutostartKnownEntry>();
        var index = 0;
        foreach (var item in items)
        {
            index++;
            if (item is not JObject obj)
            {
                throw new AutostartCatalogException(EntryLabel(index), "entries", L.T("element musi być obiektem"));
            }

            entries.Add(ParseEntry(obj, index, ids));
        }

        return new AutostartKnownCatalog(entries);
    }

    /// <summary>Pierwszy pasujący wpis: chronione przed „można wyłączyć”; null = bez oceny.</summary>
    public AutostartKnownEntry? Match(AutostartEntry entry, UserEnvironment environment)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (environment is null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        return _matchOrder.FirstOrDefault(known => known.Match.Matches(entry, environment));
    }

    /// <summary>Etykieta wpisu bez id, np. „(wpis 3)”.</summary>
    private static string EntryLabel(int index) => L.F("(wpis {0})", index);

    private static AutostartKnownEntry ParseEntry(JObject obj, int index, HashSet<string> ids)
    {
        var id = Text(obj, "id", EntryLabel(index))
            ?? throw new AutostartCatalogException(EntryLabel(index), "id", L.T("id jest wymagane"));
        if (!ids.Add(id))
        {
            throw new AutostartCatalogException(id, "id", L.T("id występuje więcej niż raz"));
        }

        var match = ParseMatch(obj, id);
        var verdict = Required(obj, id, "verdict") switch
        {
            VerdictProtected => AutostartVerdict.Protected,
            VerdictCanDisable => AutostartVerdict.CanDisable,
            _ => throw new AutostartCatalogException(id, "verdict", L.F("dozwolone: {0}", VerdictProtected + ", " + VerdictCanDisable)),
        };

        if (verdict == AutostartVerdict.Protected
            && match.Folders.Any(folder => UserWritableRoots.Any(root => folder.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))))
        {
            throw new AutostartCatalogException(
                id,
                "match.folders",
                L.F("wpis chroniony nie może mieć folderu zapisywalnego bez administratora ({0})", string.Join(", ", UserWritableRoots)));
        }

        var summary = Required(obj, id, "summary");
        if (summary.Length > MaxSummary)
        {
            throw new AutostartCatalogException(id, "summary", L.F("najwyżej {0} znaków", MaxSummary));
        }

        var sources = Strings(obj, id, "source", "source");
        if (sources.Any(source => !IsHttps(source) && !MeasurementDocument.IsMatch(source)))
        {
            throw new AutostartCatalogException(id, "source", L.T("źródło to bezwzględny adres https albo dokument docs/zrodla/<nazwa>.md"));
        }

        if (obj["confirmed"]?.Type != JTokenType.Boolean)
        {
            throw new AutostartCatalogException(id, "confirmed", L.T("wymagana wartość true/false"));
        }

        var confirmed = obj["confirmed"]!.Value<bool>();
        if (verdict == AutostartVerdict.CanDisable)
        {
            if (!confirmed)
            {
                throw new AutostartCatalogException(id, "confirmed", L.T("werdykt can-disable wymaga confirmed: true"));
            }

            if (!sources.Any(MeasurementDocument.IsMatch))
            {
                throw new AutostartCatalogException(id, "source", L.T("werdykt can-disable wymaga dokumentu pomiaru z docs/zrodla/"));
            }

            if (!sources.Any(IsHttps))
            {
                throw new AutostartCatalogException(id, "source", L.T("werdykt can-disable wymaga adresu https ze źródłem skutku"));
            }
        }

        // Tekst dla człowieka w języku interfejsu dopiero po walidacji: limit długości dotyczy polskiego oryginału.
        return new AutostartKnownEntry(id, match, verdict, L.Data(summary), sources, confirmed);
    }

    private static AutostartMatchRule ParseMatch(JObject obj, string id)
    {
        if (obj["match"] is not JObject match)
        {
            throw new AutostartCatalogException(id, "match", L.T("wymagany obiekt z regułą dopasowania"));
        }

        var file = Text(match, "file", id, "match.file")
            ?? throw new AutostartCatalogException(id, "match.file", L.T("pole jest wymagane i niepuste"));
        if (file.IndexOf('\\') >= 0)
        {
            throw new AutostartCatalogException(id, "match.file", L.T("sama nazwa pliku, bez folderu"));
        }

        if (OnlyStars(file))
        {
            throw new AutostartCatalogException(id, "match.file", L.T("wzorzec z samych gwiazdek pasuje do wszystkiego"));
        }

        var folders = Strings(match, id, "folders", "match.folders");
        foreach (var folder in folders)
        {
            // Zmienna i od razu „\”: „%ProgramFiles%*” objąłby też „C:\Program Files Inny\”, folder spoza zapisu tylko dla administratora.
            var rooted = FolderRoots.Any(root => folder.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));
            var ended = folder.EndsWith("\\", StringComparison.Ordinal) || folder.EndsWith("*", StringComparison.Ordinal);
            if (!rooted || !ended)
            {
                throw new AutostartCatalogException(
                    id,
                    "match.folders",
                    L.F("wzorzec folderu: jedna ze zmiennych {0}, po niej \\, na końcu \\ albo *", string.Join(", ", FolderRoots)));
            }
        }

        var publishers = Strings(match, id, "publishers", "match.publishers");
        if (publishers.Any(OnlyStars))
        {
            throw new AutostartCatalogException(id, "match.publishers", L.T("wzorzec z samych gwiazdek pasuje do wszystkiego"));
        }
        return new AutostartMatchRule(
            file,
            folders,
            publishers,
            Text(match, "valueName", id, "match.valueName"),
            Text(match, "packageFamily", id, "match.packageFamily"));
    }

    /// <summary>Niepusta tablica niepustych tekstów (po <c>Trim</c>).</summary>
    private static List<string> Strings(JObject obj, string id, string name, string field)
    {
        if (obj[name] is not JArray array || array.Count == 0
            || array.Any(item => item.Type != JTokenType.String || string.IsNullOrWhiteSpace(item.Value<string>())))
        {
            throw new AutostartCatalogException(id, field, L.T("wymagana niepusta tablica niepustych tekstów"));
        }

        return array.Select(item => item.Value<string>()!.Trim()).ToList();
    }

    private static bool OnlyStars(string pattern) => pattern.Trim('*').Length == 0;

    private static bool IsHttps(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string Required(JObject obj, string id, string field)
        => Text(obj, field, id) ?? throw new AutostartCatalogException(id, field, L.T("pole jest wymagane i niepuste"));

    private static string? Text(JObject obj, string name, string id, string? field = null)
        => AppMatchJson.Text(obj, name, id, (entryId, _, message) => new AutostartCatalogException(entryId, field ?? name, message));
}
