using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Reguły wspólne źródeł z wydań GitHuba (RustDesk, WinUtil): adres zwykły https, dwa hosty plików przekierowań, ścisły odczyt JSON
/// odpowiedzi <c>releases/latest</c> i skrót <c>sha256:</c>. Czyste funkcje bez sieci.
/// </summary>
internal static class GitHubReleaseRules
{
    internal const string DigestPrefix = "sha256:";

    internal static readonly Regex HexPattern = new(@"^[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);

    private static readonly JsonLoadSettings LoadSettings = new() { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
    private static readonly string[] RedirectHosts = { "release-assets.githubusercontent.com", "objects.githubusercontent.com" };

    /// <summary>Skok przekierowania: <c>https</c>, port domyślny, bez danych logowania, host dokładnie jednym z dwóch serwerów plików GitHuba.</summary>
    internal static bool IsAllowedRedirect(Uri url)
        => IsPlainHttps(url) && Array.IndexOf(RedirectHosts, url.Host) >= 0;

    internal static bool IsPlainHttps(Uri url)
        => url is not null
           && url.IsAbsoluteUri
           && url.Scheme == Uri.UriSchemeHttps
           && url.IsDefaultPort
           && url.UserInfo.Length == 0;

    /// <summary>Jeden obiekt JSON bez śmieci za nim; duplikat klucza, błąd składni albo inny korzeń = <c>null</c>.</summary>
    internal static JObject? ParseObject(string json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            var token = JToken.ReadFrom(reader, LoadSettings);
            return reader.Read() ? null : token as JObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool TryString(JToken? token, out string value)
    {
        value = token is { Type: JTokenType.String } ? token.Value<string>() ?? string.Empty : string.Empty;
        return token is { Type: JTokenType.String };
    }

    internal static bool TryBool(JToken? token, out bool value)
    {
        value = token is { Type: JTokenType.Boolean } && token.Value<bool>();
        return token is { Type: JTokenType.Boolean };
    }
}
