using System;

namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Pobieranie HTTPS dla dwóch wyjątków od „program sam niczego nie pobiera” (RustDesk i WinUtil z GitHuba). Implementacja nie zna reguł
/// źródła: adres i każdy skok przekierowania ocenia wywołujący (<c>RustDeskSource</c>, <c>WinUtilSource</c>). Błędy sieci i HTTP to wynik, nie wyjątek;
/// wyjątek tylko przy błędnych argumentach albo bezpieczniku testów.
/// </summary>
public interface IWebDownloader
{
    /// <summary>
    /// GET tekstu (np. JSON API) z nagłówkiem <c>Accept</c> = <paramref name="accept"/>. Nie podąża za przekierowaniem
    /// (3xx = <see cref="WebFailure.HttpStatus"/>), ciało najwyżej 1 MB (więcej = <see cref="WebFailure.TooLarge"/>).
    /// </summary>
    WebText GetText(Uri url, string accept, TimeSpan timeout);

    /// <summary>
    /// GET pliku do <paramref name="targetPath"/> (plik nie może istnieć). Każdy skok przekierowania sprawdza
    /// <paramref name="allowRedirect"/> przed żądaniem (<c>false</c> = <see cref="WebFailure.RedirectRefused"/> z
    /// <see cref="WebFile.RefusedUrl"/>), najwyżej 5 skoków. Więcej niż <paramref name="maxBytes"/> = <see cref="WebFailure.TooLarge"/>.
    /// <paramref name="onBytes"/> dostaje łączną liczbę zapisanych bajtów; <paramref name="timeout"/> to jeden termin na całe wywołanie.
    /// </summary>
    WebFile Download(Uri url, Func<Uri, bool> allowRedirect, long maxBytes, string targetPath, TimeSpan timeout, Action<long>? onBytes);
}

/// <summary>Rodzaj porażki pobierania; <see cref="None"/> = sukces (kod 200, ciało odczytane w całości i w limicie).</summary>
public enum WebFailure
{
    None,

    /// <summary>DNS, połączenie, TLS, zerwane połączenie.</summary>
    Network,

    TimedOut,

    /// <summary>Odpowiedź z kodem innym niż 200 (w <see cref="IWebDownloader.GetText"/> także przekierowanie).</summary>
    HttpStatus,

    TooLarge,

    /// <summary>Przekierowanie na adres odrzucony predykatem albo za dużo skoków.</summary>
    RedirectRefused,
}

/// <summary>Wynik <see cref="IWebDownloader.GetText"/>: kod HTTP (gdy była odpowiedź), ciało przy sukcesie, opis błędu.</summary>
public sealed record WebText(WebFailure Failure, int? StatusCode, string? Body, string? Error);

/// <summary>Wynik <see cref="IWebDownloader.Download"/>: kod HTTP, zapisane bajty, odrzucony adres przekierowania, opis błędu.</summary>
public sealed record WebFile(WebFailure Failure, int? StatusCode, long Bytes, Uri? RefusedUrl, string? Error);
