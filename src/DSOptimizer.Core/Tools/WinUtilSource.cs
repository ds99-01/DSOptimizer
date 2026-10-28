using System;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using Newtonsoft.Json.Linq;

namespace DSOptimizer.Core.Tools;

/// <summary>Wydanie WinUtil wybrane z odpowiedzi API: tag, adres <c>winutil.ps1</c>, rozmiar i SHA256 (64 małe znaki hex) z pola <c>digest</c>.</summary>
public sealed record WinUtilRelease(string Tag, Uri DownloadUrl, long Size, string Sha256);

/// <summary>
/// Stałe reguły źródła WinUtil (ChrisTitusTech/winutil): jedno repo, jedno API, jeden plik <c>winutil.ps1</c> z ostatniego wydania,
/// dokładny adres pobrania i dwa hosty przekierowań. Czyste funkcje bez sieci; wspólne reguły GitHuba w <see cref="GitHubReleaseRules"/>.
/// </summary>
public static class WinUtilSource
{
    public const string Repository = "ChrisTitusTech/winutil";

    public const string FileName = "winutil.ps1";

    /// <summary>Polityka wykonywania skryptu przy starcie (decyzja z pomiaru na VM, Etap 5 Task 1).</summary>
    public const string ExecutionPolicy = "RemoteSigned";

    /// <summary>Najwięcej bajtów pliku skryptu (5 MB).</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    public static readonly Uri ReleasesLatestUrl = new("https://api.github.com/repos/ChrisTitusTech/winutil/releases/latest");

    public static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(2);

    private const string DownloadPathPrefix = "/ChrisTitusTech/winutil/releases/download/";
    private const string DownloadHost = "github.com";

    private static readonly Regex TagPattern = new(@"^[0-9]+(\.[0-9]+){1,3}\z", RegexOptions.CultureInvariant);

    private static string RefusedPrerelease => L.T("wydanie testowe (prerelease)");

    private static string RefusedDraft => L.T("szkic wydania");

    private static string RefusedTag => L.T("nieoczekiwany numer wersji");

    private static string RefusedDigest => L.T("brak skrótu SHA256");

    private static string RefusedSize => L.T("plik większy niż 5 MB");

    private static string RefusedResponse => L.T("błędna odpowiedź GitHuba");

    /// <summary>
    /// Wybiera <c>winutil.ps1</c> z odpowiedzi <c>releases/latest</c>. <c>true</c> = <paramref name="release"/> gotowe do pobrania.
    /// <c>false</c> z <paramref name="refusal"/> = odmowa z powodem. <c>false</c> bez powodu = wydanie nie ma assetu <c>winutil.ps1</c>.
    /// Adresu nie ocenia – to robi <see cref="IsAllowedDownloadUrl"/>.
    /// </summary>
    public static bool TryParseRelease(string json, out WinUtilRelease? release, out string? refusal)
    {
        refusal = Select(json, out release);
        return release is not null;
    }

    /// <summary>
    /// Adres pobrania: <c>https</c>, host dokładnie <c>github.com</c>, bez <c>%</c>, zapytania i fragmentu, ścieżka dokładnie
    /// <c>/ChrisTitusTech/winutil/releases/download/&lt;tag&gt;/winutil.ps1</c>.
    /// </summary>
    public static bool IsAllowedDownloadUrl(Uri url, WinUtilRelease release)
        => release is not null
           && GitHubReleaseRules.IsPlainHttps(url)
           && string.Equals(url.Host, DownloadHost, StringComparison.Ordinal)
           && url.Query.Length == 0
           && url.Fragment.Length == 0
           && url.OriginalString.IndexOf('%') < 0
           && string.Equals(url.AbsolutePath, DownloadPathPrefix + release.Tag + "/" + FileName, StringComparison.Ordinal);

    /// <summary>Skok przekierowania: tylko dwa serwery plików GitHuba przez https.</summary>
    public static bool IsAllowedRedirect(Uri url) => GitHubReleaseRules.IsAllowedRedirect(url);

    /// <summary>
    /// Argumenty <c>powershell.exe</c>: <c>-NoExit -NoProfile -ExecutionPolicy &lt;polityka&gt; -File "&lt;skrypt&gt;"</c>. Ścieżka musi być lokalna
    /// i bezwzględna (<c>X:\…</c>), bez cudzysłowu, znaków sterujących i końcowego <c>\</c>; inaczej <see cref="ArgumentException"/>.
    /// </summary>
    public static string LaunchArguments(string scriptPath)
    {
        var ok = scriptPath is not null
                 && scriptPath.Length > 3
                 && IsAsciiLetter(scriptPath[0])
                 && scriptPath[1] == ':'
                 && scriptPath[2] == '\\'
                 && scriptPath[scriptPath.Length - 1] != '\\'
                 && scriptPath.IndexOf('"') < 0
                 && !scriptPath.Any(char.IsControl);
        if (!ok)
        {
            throw new ArgumentException("Ścieżka skryptu musi być lokalna i bezwzględna (X:\\…), bez cudzysłowu: " + scriptPath, nameof(scriptPath));
        }

        return "-NoExit -NoProfile -ExecutionPolicy " + ExecutionPolicy + " -File \"" + scriptPath + "\"";
    }

    private static string? Select(string json, out WinUtilRelease? release)
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

        JObject? script = null;
        foreach (var item in assets)
        {
            if (item is not JObject asset)
            {
                return RefusedResponse;
            }

            if (GitHubReleaseRules.TryString(asset["name"], out var name) && string.Equals(name, FileName, StringComparison.Ordinal))
            {
                if (script is not null)
                {
                    return RefusedResponse;
                }

                script = asset;
            }
        }

        if (script is null)
        {
            return null;
        }

        if (script["size"] is not JValue { Value: long size }
            || !GitHubReleaseRules.TryString(script["browser_download_url"], out var urlText)
            || !Uri.TryCreate(urlText, UriKind.Absolute, out var url))
        {
            return RefusedResponse;
        }

        if (!GitHubReleaseRules.TryString(script["digest"], out var digest)
            || !digest.StartsWith(GitHubReleaseRules.DigestPrefix, StringComparison.Ordinal)
            || !GitHubReleaseRules.HexPattern.IsMatch(digest.Substring(GitHubReleaseRules.DigestPrefix.Length)))
        {
            return RefusedDigest;
        }

        if (size <= 0 || size > MaxBytes)
        {
            return RefusedSize;
        }

        release = new WinUtilRelease(tag, url, size, digest.Substring(GitHubReleaseRules.DigestPrefix.Length).ToLowerInvariant());
        return null;
    }

    private static bool IsAsciiLetter(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
}
