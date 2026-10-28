using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Localization;

/// <summary>
/// Tabela tłumaczeń polski → angielski. Format pliku: <c>{"language":"en","entries":[{"pl":"…","en":"…"}]}</c>, opcjonalnie
/// z <c>"data": true</c> (teksty z danych – katalog, listy aplikacji): wtedy zamiast reguł napisu formatującego obowiązują te same
/// grupy <c>{…}</c> w obu językach (np. <c>shell:::{GUID}</c>, <c>{0:X8}</c>) i ta sama liczba nawiasów klamrowych.
/// Klucz to dokładny tekst polski. Ładowanie sprawdza wszystkie reguły i zgłasza wszystkie błędy naraz.
/// </summary>
public sealed class TranslationTable
{
    private static readonly Regex Placeholder = new(
        @"\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BraceGroup = new(@"\{[^{}]*\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly char[] Braces = { '{', '}' };

    private static readonly string[] RequiredKeys = { "en", "pl" };

    private readonly Dictionary<string, string> english;

    private TranslationTable(Dictionary<string, string> english)
    {
        this.english = english;
    }

    public static TranslationTable Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public int Count => english.Count;

    /// <summary>Wczytuje pliki tłumaczeń; błędy (wszystkie, z nazwą pliku) zgłasza jako <see cref="TranslationFormatException"/>.</summary>
    public static TranslationTable Load(IReadOnlyList<TranslationSource> sources)
    {
        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        var errors = new List<string>();
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        var origin = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            LoadSource(source, table, origin, errors);
        }

        if (errors.Count > 0)
        {
            throw new TranslationFormatException(errors);
        }

        return new TranslationTable(table);
    }

    /// <summary>Angielski odpowiednik polskiego tekstu albo <c>null</c>. Dla języka polskiego zawsze <c>null</c>.</summary>
    public string? Find(UiLanguage language, string polish)
    {
        if (polish is null)
        {
            throw new ArgumentNullException(nameof(polish));
        }

        if (language != UiLanguage.English)
        {
            return null;
        }

        return english.TryGetValue(polish, out var value) ? value : null;
    }

    private static void LoadSource(
        TranslationSource source,
        Dictionary<string, string> table,
        Dictionary<string, string> origin,
        List<string> errors)
    {
        var name = source.Name;
        JObject root;
        try
        {
            using var reader = new JsonTextReader(new StringReader(source.Json))
            {
                DateParseHandling = DateParseHandling.None,
                Culture = CultureInfo.InvariantCulture,
            };
            if (JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }) is not JObject parsed)
            {
                errors.Add(name + ": korzeń pliku musi być obiektem JSON.");
                return;
            }

            if (reader.Read())
            {
                errors.Add(name + ": dane po zamknięciu dokumentu JSON.");
                return;
            }

            root = parsed;
        }
        catch (JsonException ex)
        {
            errors.Add(name + ": niepoprawny JSON (" + ex.Message + ")");
            return;
        }

        if (root["language"] is not JValue { Type: JTokenType.String } language
            || !string.Equals((string?)language.Value, UiLanguageCodes.EnglishCode, StringComparison.Ordinal))
        {
            errors.Add(name + ": pole \"language\" musi mieć dokładnie wartość \"en\".");
        }

        var data = false;
        if (root["data"] is { } dataToken)
        {
            if (dataToken is JValue { Type: JTokenType.Boolean } flag)
            {
                data = (bool)flag.Value!;
            }
            else
            {
                errors.Add(name + ": pole \"data\" musi mieć wartość true albo false.");
            }
        }

        if (root["entries"] is not JArray entries)
        {
            errors.Add(name + ": brak tablicy \"entries\".");
            return;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            ValidateEntry(name, i, entries[i], data, table, origin, errors);
        }
    }

    private static void ValidateEntry(
        string file,
        int index,
        JToken token,
        bool data,
        Dictionary<string, string> table,
        Dictionary<string, string> origin,
        List<string> errors)
    {
        var where = file + ", wpis " + index.ToString(CultureInfo.InvariantCulture);
        if (token is not JObject entry)
        {
            errors.Add(where + ": wpis musi być obiektem.");
            return;
        }

        var keys = entry.Properties().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (!keys.SequenceEqual(RequiredKeys, StringComparer.Ordinal))
        {
            errors.Add(where + ": wpis musi mieć dokładnie klucze \"pl\" i \"en\" (jest: " + string.Join(", ", keys) + ").");
            return;
        }

        var pl = TextOf(entry["pl"]);
        var en = TextOf(entry["en"]);
        if (pl is null || en is null || pl.Length == 0 || en.Length == 0)
        {
            errors.Add(where + ": \"pl\" i \"en\" muszą być niepustymi napisami.");
            return;
        }

        var ok = data ? ValidateDataBraces(where, pl, en, errors) : ValidateFormat(where, pl, en, errors);
        if (!string.Equals(Leading(pl), Leading(en), StringComparison.Ordinal)
            || !string.Equals(Trailing(pl), Trailing(en), StringComparison.Ordinal))
        {
            errors.Add(where + ": początkowe lub końcowe białe znaki \"en\" różnią się od \"pl\" (" + Shorten(pl) + ").");
            ok = false;
        }

        if (origin.TryGetValue(pl, out var firstFile))
        {
            errors.Add(where + ": ten sam tekst polski już jest w pliku " + firstFile + " (" + Shorten(pl) + ").");
            return;
        }

        origin[pl] = file;
        if (ok)
        {
            table[pl] = en;
        }
    }

    /// <summary>Reguły tabeli kodu: ten sam zbiór symboli zastępczych i poprawny napis formatujący.</summary>
    private static bool ValidateFormat(string where, string pl, string en, List<string> errors)
    {
        if (!PlaceholderNumbers(pl).SetEquals(PlaceholderNumbers(en)))
        {
            errors.Add(where + ": inny zbiór symboli zastępczych w \"pl\" i \"en\" (" + Shorten(pl) + ").");
            return false;
        }

        if ((HasBrace(pl) || HasBrace(en)) && (!IsValidFormat(pl) || !IsValidFormat(en)))
        {
            errors.Add(where + ": \"pl\" lub \"en\" nie jest poprawnym napisem formatującym (zabłąkany nawias klamrowy) (" + Shorten(pl) + ").");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reguła tabeli danych: grupy <c>{…}</c> (bez zagnieżdżeń) w „pl” i „en” równe jako multizbiory (porównanie dokładne), a liczba
    /// znaków <c>{</c> i <c>}</c> taka sama w obu językach – tłumacz nie zgubi ani nie zmieni GUID-u ani symbolu zastępczego.
    /// </summary>
    private static bool ValidateDataBraces(string where, string pl, string en, List<string> errors)
    {
        if (!BraceGroups(pl).SequenceEqual(BraceGroups(en), StringComparer.Ordinal)
            || CountOf(pl, '{') != CountOf(en, '{')
            || CountOf(pl, '}') != CountOf(en, '}'))
        {
            errors.Add(where + ": inne grupy w nawiasach klamrowych w \"pl\" i \"en\" (" + Shorten(pl) + ").");
            return false;
        }

        return true;
    }

    private static List<string> BraceGroups(string text) =>
        BraceGroup.Matches(text).Cast<Match>().Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToList();

    private static int CountOf(string text, char c) => text.Count(x => x == c);

    private static bool HasBrace(string text) => text.IndexOfAny(Braces) >= 0;

    private static bool IsValidFormat(string text)
    {
        var numbers = PlaceholderNumbers(text);
        var max = numbers.Count == 0 ? -1 : numbers.Max(n => int.Parse(n, CultureInfo.InvariantCulture));
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, text, new object[max + 1]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? TextOf(JToken? token) =>
        token is JValue { Type: JTokenType.String } value ? (string?)value.Value : null;

    private static HashSet<string> PlaceholderNumbers(string text)
    {
        var stripped = text.Replace("{{", string.Empty).Replace("}}", string.Empty);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Placeholder.Matches(stripped))
        {
            var number = match.Groups[1].Value.TrimStart('0');
            result.Add(number.Length == 0 ? "0" : number);
        }

        return result;
    }

    private static string Leading(string text)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return text.Substring(0, i);
    }

    private static string Trailing(string text)
    {
        var i = text.Length;
        while (i > 0 && char.IsWhiteSpace(text[i - 1]))
        {
            i--;
        }

        return text.Substring(i);
    }

    private static string Shorten(string text) =>
        "\"" + (text.Length > 50 ? text.Substring(0, 50) + "…" : text) + "\"";
}
