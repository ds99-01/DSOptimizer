using System;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Install;

namespace DSOptimizer.Core.Updates;

/// <summary>Wynik odczytu odpowiedzi <c>releases/latest</c>: stabilne wydanie, wydanie do pominięcia (wersja próbna lub szkic) albo odpowiedź nie do przyjęcia.</summary>
public enum ReleaseReading
{
    Stable,

    Skipped,

    Invalid,
}

/// <summary>
/// Stałe reguły sprawdzania nowej wersji programu: jeden adres API GitHuba i ścisły odczyt odpowiedzi. Odpowiedź jest niezaufana:
/// z całego JSON-a wychodzi wyłącznie zwalidowany numer wersji. Czyste funkcje bez sieci.
/// </summary>
public static class UpdateSource
{
    public static readonly Uri ReleasesLatestUrl = new("https://api.github.com/repos/ds99-01/DSOptimizer/releases/latest");

    public static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(15);

    // [0-9], nie \d: w .NET \d przyjmuje cyfry Unicode; \z, nie $: $ przepuszcza nową linię na końcu.
    private static readonly Regex TagPattern = new(@"^v[0-9]+\.[0-9]+\.[0-9]+\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Odczyt JSON-a: obiekt z <c>tag_name</c> (tekst), <c>prerelease</c> i <c>draft</c> (wartości logiczne), inaczej <see cref="ReleaseReading.Invalid"/>.
    /// Wersja próbna albo szkic = <see cref="ReleaseReading.Skipped"/> (tag wtedy nie jest oceniany). Tag musi mieć postać <c>vX.Y.Z</c>.
    /// </summary>
    public static ReleaseReading Read(string? json, out Version? version)
    {
        version = null;
        if (json is null)
        {
            return ReleaseReading.Invalid;
        }

        var root = GitHubReleaseRules.ParseObject(json);
        if (root is null
            || !GitHubReleaseRules.TryString(root["tag_name"], out var tag)
            || !GitHubReleaseRules.TryBool(root["prerelease"], out var prerelease)
            || !GitHubReleaseRules.TryBool(root["draft"], out var draft))
        {
            return ReleaseReading.Invalid;
        }

        if (prerelease || draft)
        {
            return ReleaseReading.Skipped;
        }

        if (!TagPattern.IsMatch(tag) || !Version.TryParse(tag.Substring(1), out var parsed))
        {
            return ReleaseReading.Invalid;
        }

        version = parsed;
        return ReleaseReading.Stable;
    }
}