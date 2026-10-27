using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Install;

/// <summary>Wydanie RustDesk wybrane z odpowiedzi API: tag, nazwa i adres MSI, rozmiar i SHA256 (64 małe znaki hex) z pola <c>digest</c>.</summary>
public sealed record RustDeskRelease(string Tag, string FileName, Uri DownloadUrl, long Size, string Sha256);

/// <summary>
/// Stałe reguły źródła RustDesk (spec 3.1–3.3): jedno repo, jedno API, dokładny adres pobrania, dwa hosty przekierowań i podmiot
/// podpisu wydawcy. Czyste funkcje bez sieci; o każdym bajcie z internetu decydują wyłącznie te reguły, nie <c>apps.json</c>.
/// </summary>
public static class RustDeskSource
{
    public const string EntryId = "install.rustdesk";

    public const string Repository = "rustdesk/rustdesk";

    public const string ApiAccept = "application/vnd.github+json";

    public const string UserAgent = "DSOptimizer";

    /// <summary>Najwięcej bajtów pliku MSI (200 MB).</summary>
    public const long MaxBytes = 200 * 1024 * 1024;

    /// <summary>Klucz pliku logu MSI w folderze logów instalacji (<c>IWingetLogs.NewLogPath</c>).</summary>
    public const string MsiLogKey = "rustdesk-msi";

    public static readonly Uri ReleasesLatestUrl = new("https://api.github.com/repos/rustdesk/rustdesk/releases/latest");

    public static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    private const string RefusedPrerelease = "wydanie testowe (prerelease)";
    private const string RefusedDraft = "szkic wydania";
    private const string RefusedTag = "nieoczekiwany numer wersji";
    private const string RefusedDigest = "brak skrótu SHA256";
    internal const string RefusedSize = "plik większy niż 200 MB";
    internal const string RefusedResponse = "błędna odpowiedź GitHuba";
    private const string DownloadPathPrefix = "/rustdesk/rustdesk/releases/download/";
    private const string DownloadHost = "github.com";

    private static readonly Regex TagPattern = new(@"^[0-9]+\.[0-9]+\.[0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly char[] KeyForbidden = { ',', '"', '+' };
    private static readonly char[] UnquotedForbidden = { '"', '=', '+', '\\' };

    /// <summary>
    /// Wybiera MSI z odpowiedzi <c>releases/latest</c>. <c>true</c> = <paramref name="release"/> gotowe do pobrania. <c>false</c> z
    /// <paramref name="refusal"/> = odmowa z powodem (prerelease, szkic, tag spoza <c>X.Y.Z</c>, brak skrótu, rozmiar, zła odpowiedź).
    /// <c>false</c> bez powodu = wydanie nie ma assetu <c>rustdesk-&lt;tag&gt;-x86_64.msi</c> (tekst składa wywołujący).
    /// Adresu nie ocenia – to robi <see cref="IsAllowedDownloadUrl"/>.
    /// </summary>
    public static bool TryParseRelease(string json, out RustDeskRelease? release, out string? refusal)
    {
        refusal = Select(json, out release);
        return release is not null;
    }

    /// <summary>
    /// Adres pobrania MSI: <c>https</c>, host dokładnie <c>github.com</c>, port domyślny, bez danych logowania, zapytania i fragmentu,
    /// ścieżka dokładnie <c>/rustdesk/rustdesk/releases/download/&lt;tag&gt;/&lt;plik&gt;</c> wydania. Adres z jakimkolwiek kodowaniem
    /// <c>%</c> jest odrzucany: ścieżka wydania go nie potrzebuje, a <c>Uri</c> na net481 dekoduje <c>%2E%2E</c> i zwija je jak <c>..</c>.
    /// </summary>
    public static bool IsAllowedDownloadUrl(Uri url, RustDeskRelease release)
        => release is not null
           && GitHubReleaseRules.IsPlainHttps(url)
           && string.Equals(url.Host, DownloadHost, StringComparison.Ordinal)
           && url.Query.Length == 0
           && url.Fragment.Length == 0
           && url.OriginalString.IndexOf('%') < 0
           && string.Equals(url.AbsolutePath, DownloadPathPrefix + release.Tag + "/" + release.FileName, StringComparison.Ordinal);

    /// <summary>Skok przekierowania: <c>https</c>, port domyślny, bez danych logowania, host dokładnie jednym z dwóch serwerów plików GitHuba.</summary>
    public static bool IsAllowedRedirect(Uri url)
        => GitHubReleaseRules.IsAllowedRedirect(url);

    /// <summary>
    /// Podmiot certyfikatu podpisu (format <c>X509Certificate2.Subject</c>) wydawcy RustDesk: <c>O=PURSLANE</c>, <c>C=SG</c>,
    /// <c>SERIALNUMBER=53481265A</c>, każdy klucz dokładnie raz (klucz bez wielkości liter, wartość dokładnie). Wartości w cudzysłowie
    /// (z <c>""</c> wewnątrz) są jedną wartością; nieujęty w cudzysłów <c>+</c> (RDN wielowartościowy), <c>\</c> (escape RFC 4514,
    /// którego format CryptoAPI nie używa) albo błąd składni = <c>false</c>.
    /// </summary>
    public static bool IsTrustedSigner(string? subject)
    {
        var attributes = ParseDistinguishedName(subject);
        if (attributes is null)
        {
            return false;
        }

        return Single(attributes, "O") == "PURSLANE"
               && Single(attributes, "C") == "SG"
               && Single(attributes, "SERIALNUMBER") == "53481265A";
    }

    private static string? Select(string json, out RustDeskRelease? release)
    {
        release = null;
        var root = GitHubReleaseRules.ParseObject(json);
        if (root is null
            || !GitHubReleaseRules.TryString(root["tag_name"], out var tag)
            || !GitHubReleaseRules.TryBool(root["prerelease"], out var prerelease)
            || !GitHubReleaseRules.TryBool(root["draft"], out var draft)
            || root["assets"] is not JArray assets)
        {
            return RefusedResponse;
        }

        if (prerelease)
        {
            return RefusedPrerelease;
        }

        if (draft)
        {
            return RefusedDraft;
        }

        if (!TagPattern.IsMatch(tag))
        {
            return RefusedTag;
        }

        var fileName = "rustdesk-" + tag + "-x86_64.msi";
        JObject? msi = null;
        foreach (var item in assets)
        {
            if (item is not JObject asset)
            {
                return RefusedResponse;
            }

            if (GitHubReleaseRules.TryString(asset["name"], out var name) && string.Equals(name, fileName, StringComparison.Ordinal))
            {
                if (msi is not null)
                {
                    return RefusedResponse;
                }

                msi = asset;
            }
        }

        if (msi is null)
        {
            return null;
        }

        if (msi["size"] is not JValue { Value: long size }
            || !GitHubReleaseRules.TryString(msi["browser_download_url"], out var urlText)
            || !Uri.TryCreate(urlText, UriKind.Absolute, out var url))
        {
            return RefusedResponse;
        }

        if (!GitHubReleaseRules.TryString(msi["digest"], out var digest)
            || !digest.StartsWith(GitHubReleaseRules.DigestPrefix, StringComparison.Ordinal)
            || !GitHubReleaseRules.HexPattern.IsMatch(digest.Substring(GitHubReleaseRules.DigestPrefix.Length)))
        {
            return RefusedDigest;
        }

        if (size <= 0 || size > MaxBytes)
        {
            return RefusedSize;
        }

        release = new RustDeskRelease(tag, fileName, url, size, digest.Substring(GitHubReleaseRules.DigestPrefix.Length).ToLowerInvariant());
        return null;
    }

    private static string? Single(List<KeyValuePair<string, string>> attributes, string key)
    {
        string? found = null;
        foreach (var pair in attributes)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                if (found is not null)
                {
                    return null;
                }

                found = pair.Value;
            }
        }

        return found;
    }

    /// <summary>
    /// Nazwa wyróżniająca → pary klucz/wartość. Separator to przecinek poza cudzysłowem; wartość w cudzysłowie kończy się cudzysłowem
    /// (<c>""</c> = jeden znak <c>"</c>), po którym mogą być tylko spacje. Wartość bez cudzysłowu nie może zawierać <c>"</c>, <c>=</c>,
    /// <c>+</c> ani <c>\</c>. Pusty element, brak <c>=</c>, pusty klucz albo niedomknięty cudzysłów = <c>null</c>.
    /// </summary>
    private static List<KeyValuePair<string, string>>? ParseDistinguishedName(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var result = new List<KeyValuePair<string, string>>();
        var text = subject!;
        var position = 0;
        while (true)
        {
            var equals = text.IndexOf('=', position);
            if (equals < 0)
            {
                return null;
            }

            var key = text.Substring(position, equals - position).Trim();
            if (key.Length == 0 || key.IndexOfAny(KeyForbidden) >= 0)
            {
                return null;
            }

            position = SkipSpaces(text, equals + 1);
            string value;
            if (position < text.Length && text[position] == '"')
            {
                var quoted = ReadQuoted(text, position + 1, out position);
                if (quoted is null)
                {
                    return null;
                }

                value = quoted;
                position = SkipSpaces(text, position);
                if (position < text.Length && text[position] != ',')
                {
                    return null;
                }
            }
            else
            {
                var comma = text.IndexOf(',', position);
                var end = comma < 0 ? text.Length : comma;
                value = text.Substring(position, end - position).Trim();
                if (value.IndexOfAny(UnquotedForbidden) >= 0)
                {
                    return null;
                }

                position = end;
            }

            result.Add(new KeyValuePair<string, string>(key, value));
            if (position >= text.Length)
            {
                return result;
            }

            // text[position] == ',': następny element musi istnieć.
            position++;
            if (text.Substring(position).Trim().Length == 0)
            {
                return null;
            }
        }
    }

    /// <summary>Treść wartości w cudzysłowie od <paramref name="start"/> (za otwierającym <c>"</c>); <paramref name="next"/> = za zamykającym.</summary>
    private static string? ReadQuoted(string text, int start, out int next)
    {
        var value = new StringBuilder();
        var i = start;
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    value.Append('"');
                    i += 2;
                    continue;
                }

                next = i + 1;
                return value.ToString();
            }

            value.Append(text[i]);
            i++;
        }

        next = text.Length;
        return null;
    }

    private static int SkipSpaces(string text, int position)
    {
        while (position < text.Length && text[position] == ' ')
        {
            position++;
        }

        return position;
    }
}
