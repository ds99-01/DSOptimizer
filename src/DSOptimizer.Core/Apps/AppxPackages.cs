using System;
using System.Collections.Generic;
using System.Globalization;
using DSOptimizer.Core.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Apps;

/// <summary>Rekord pakietu AppX z wyniku <see cref="AppxScript"/> (przed złożeniem <see cref="InstalledApp"/>).</summary>
public sealed record AppxRecord(string Name, string PackageFullName, string PackageFamilyName);

/// <summary>Parser wyniku skryptu AppX i rozwijanie nazwy wyświetlanej pakietu (bez Win32: ładowanie przekazuje wołający).</summary>
public static class AppxPackages
{
    private const string ResourcePrefix = "ms-resource:";

    /// <summary>
    /// Składa aplikacje z JSON-a skryptu. <paramref name="displayName"/> dostaje rekord i surową nazwę z manifestu
    /// (null = brak) i zwraca nazwę do pokazania; pusta odpowiedź = <c>Name</c> pakietu. <paramref name="publisherName"/> dostaje
    /// surowe <c>Properties/PublisherDisplayName</c> z manifestu i zwraca czytelnego wydawcę; pusta odpowiedź (albo brak funkcji)
    /// = nazwa wyróżniająca certyfikatu (<c>Publisher</c>). Zły JSON: <see cref="FormatException"/>.
    /// </summary>
    public static IReadOnlyList<InstalledApp> Parse(
        string json,
        string userSid,
        Func<AppxRecord, string?, string?> displayName,
        Func<AppxRecord, string?, string?>? publisherName = null)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        if (displayName is null)
        {
            throw new ArgumentNullException(nameof(displayName));
        }

        var items = ReadItems(json);
        var apps = new List<InstalledApp>(items.Count);
        foreach (var item in items)
        {
            var record = new AppxRecord(
                RequiredText(item, "Name"),
                RequiredText(item, "PackageFullName"),
                RequiredText(item, "PackageFamilyName"));
            var chosen = displayName(record, OptionalText(item, "DisplayName"));
            var publisher = publisherName?.Invoke(record, OptionalText(item, "PublisherDisplayName"));
            var appx = new AppxInfo(
                record.PackageFullName,
                record.PackageFamilyName,
                record.Name,
                Flag(item, "IsFramework"),
                Flag(item, "IsResourcePackage"),
                Flag(item, "NonRemovable"),
                OptionalText(item, "SignatureKind"),
                Flag(item, "IsProvisioned"));
            apps.Add(new InstalledApp(
                "appx:" + record.PackageFamilyName,
                AppKind.Store,
                string.IsNullOrWhiteSpace(chosen) ? record.Name : chosen!,
                string.IsNullOrWhiteSpace(publisher) ? OptionalText(item, "Publisher") : publisher,
                OptionalText(item, "Version"),
                AppScope.User,
                userSid,
                appx,
                null));
        }

        return apps;
    }

    /// <summary>
    /// Nazwa do pokazania: surowa wartość z manifestu, a dla <c>ms-resource:</c> wynik <paramref name="loadIndirect"/>
    /// (np. <c>SHLoadIndirectString</c>). Pusta, nieudana albo nadal surowa odpowiedź daje <paramref name="packageName"/>.
    /// </summary>
    public static string ResolveDisplayName(string packageName, string packageFullName, string? raw, Func<string, string?> loadIndirect)
        => Resolve(packageName, packageFullName, raw, loadIndirect) ?? packageName;

    /// <summary>
    /// Wydawca do pokazania z surowego <c>Properties/PublisherDisplayName</c>, rozwijany tą samą drogą co nazwa
    /// (<see cref="ResolveDisplayName"/>). Null = nic użytecznego; wtedy wołający zostaje przy nazwie wyróżniającej certyfikatu.
    /// </summary>
    public static string? ResolvePublisherName(string packageName, string packageFullName, string? raw, Func<string, string?> loadIndirect)
        => Resolve(packageName, packageFullName, raw, loadIndirect);

    private static string? Resolve(string packageName, string packageFullName, string? raw, Func<string, string?> loadIndirect)
    {
        if (loadIndirect is null)
        {
            throw new ArgumentNullException(nameof(loadIndirect));
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var source = IndirectSource(packageName, packageFullName, raw!);
        if (source is null)
        {
            return raw!;
        }

        var loaded = loadIndirect(source);
        return string.IsNullOrWhiteSpace(loaded)
            || loaded!.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase)
            || loaded.StartsWith("@{", StringComparison.Ordinal)
                ? null
                : loaded;
    }

    /// <summary>
    /// Źródło dla <c>SHLoadIndirectString</c>: <c>@{&lt;PackageFullName&gt;?ms-resource://&lt;Name&gt;/Resources/&lt;klucz&gt;}</c>.
    /// <c>ms-resource://…</c> bez zmian; <c>ms-resource:/x</c> to <c>ms-resource://&lt;Name&gt;/x</c>; goły klucz <c>ms-resource:Klucz</c>
    /// to <c>ms-resource://&lt;Name&gt;/Resources/Klucz</c>. Null = to nie jest odwołanie do zasobu.
    /// </summary>
    public static string? IndirectSource(string packageName, string packageFullName, string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = text.Substring(ResourcePrefix.Length);
        string uri;
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            uri = text;
        }
        else if (rest.StartsWith("/", StringComparison.Ordinal))
        {
            uri = "ms-resource://" + packageName + rest;
        }
        else
        {
            uri = "ms-resource://" + packageName + "/Resources/" + rest;
        }

        return "@{" + packageFullName + "?" + uri + "}";
    }

    private static List<JObject> ReadItems(string json)
    {
        JToken root;
        try
        {
            using var reader = new JsonTextReader(new System.IO.StringReader(json))
            {
                DateParseHandling = DateParseHandling.None,
                Culture = CultureInfo.InvariantCulture,
            };
            root = JToken.ReadFrom(reader);
        }
        catch (JsonException ex)
        {
            throw new FormatException(L.F("Wynik odczytu AppX nie jest poprawnym JSON-em: {0}", ex.Message), ex);
        }

        var items = new List<JObject>();
        if (root is JObject single)
        {
            items.Add(single);
            return items;
        }

        if (root is JArray array)
        {
            foreach (var token in array)
            {
                items.Add(token as JObject ?? throw new FormatException(L.T("Wynik odczytu AppX zawiera element, który nie jest obiektem.")));
            }

            return items;
        }

        throw new FormatException(L.T("Wynik odczytu AppX nie jest tablicą ani obiektem."));
    }

    private static string RequiredText(JObject item, string name)
        => OptionalText(item, name) is { Length: > 0 } value
            ? value
            : throw new FormatException(L.F("W wyniku odczytu AppX brakuje pola {0}.", name));

    private static string? OptionalText(JObject item, string name)
        => item.TryGetValue(name, StringComparison.Ordinal, out var token)
            && token.Type == JTokenType.String
            && token.Value<string>() is { } text
            && !string.IsNullOrWhiteSpace(text)
                ? text
                : null;

    private static bool Flag(JObject item, string name)
        => item.TryGetValue(name, StringComparison.Ordinal, out var token) && token.Type == JTokenType.Boolean && token.Value<bool>();
}
