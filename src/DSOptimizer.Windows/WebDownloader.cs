using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Windows;

/// <summary>
/// HTTPS dla jedynego wyjątku od „program sam niczego nie pobiera” (RustDesk z GitHuba). <see cref="HttpWebRequest"/>; TLS: przy
/// domyślnym <c>SystemDefault</c> wersję wybiera system (1.2/1.3), przy jawnie ustawionej liście protokołów dodawany jest TLS 1.2
/// (<see cref="EnsureTls12"/>). Proxy systemowe z poświadczeniami domyślnymi (tylko dla proxy – serwer nie dostaje poświadczeń), bez
/// dekompresji i bez automatycznych przekierowań: każdy skok ocenia predykat wywołującego przed kolejnym żądaniem, a żądanie idzie dokładnie tym obiektem
/// <see cref="Uri"/>, który predykat ocenił (adres nigdy nie jest składany ponownie z tekstu ani przekodowywany – warunek review Tasku 1;
/// pierwszy adres to obiekt podany przez wywołującego, zbudowany z surowego <c>browser_download_url</c>). Jeden termin
/// (<see cref="Stopwatch"/>) na całe wywołanie: <c>Timeout</c>/<c>ReadWriteTimeout</c> żądania i limit każdego odczytu = pozostały czas.
/// Ciało krótsze niż <c>Content-Length</c> (zerwane połączenie) = <see cref="WebFailure.Network"/>.
/// Błędy sieci i HTTP to wynik (<see cref="WebFailure"/>); wyjątek tylko przy błędnych argumentach, bezpieczniku testów i błędzie zapisu
/// pliku docelowego (np. plik już istnieje – <see cref="IOException"/>).
/// Bezpiecznik testów: przy zmiennej <c>DSO_BLOCK_REAL_NETWORK=1</c> adres spoza pętli zwrotnej (<see cref="Uri.IsLoopback"/>), także na
/// skoku przekierowania, = <see cref="InvalidOperationException"/> „Bezpiecznik testów: …” przed żądaniem – ustawia ją proces <c>Tests.Windows</c>.
/// </summary>
public sealed class WebDownloader : IWebDownloader
{
    /// <summary>Zmienna środowiskowa bezpiecznika testów; wartość <c>1</c> dopuszcza wyłącznie adresy pętli zwrotnej.</summary>
    public const string BlockRealNetworkVariable = "DSO_BLOCK_REAL_NETWORK";

    private const int MaxRedirects = 5;
    private const int BufferSize = 81920;
    private const long MaxTextBytes = 1024 * 1024;
    private const string FileAccept = "application/octet-stream";

    private readonly string _userAgent;

    static WebDownloader()
    {
        EnsureTls12();
    }

    /// <summary>
    /// <c>SystemDefault</c> (domyślne na net481) zostaje bez zmian – wersję TLS wybiera system (1.2/1.3); każda inna wartość
    /// (proces z jawnie ustawioną listą protokołów) dostaje bit TLS 1.2.
    /// </summary>
    internal static void EnsureTls12()
    {
        if (ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
    }

    public WebDownloader(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            throw new ArgumentException("Pusty User-Agent.", nameof(userAgent));
        }

        _userAgent = userAgent;
    }

    public WebText GetText(Uri url, string accept, TimeSpan timeout)
    {
        ValidateUrl(url);
        if (string.IsNullOrWhiteSpace(accept))
        {
            throw new ArgumentException("Pusty nagłówek Accept.", nameof(accept));
        }

        ValidateTimeout(timeout);
        GuardRealNetwork(url);

        var clock = Stopwatch.StartNew();
        var exchange = Send(url, accept, timeout, clock);
        using (exchange)
        {
            if (exchange.Failure is { } failure)
            {
                return new WebText(failure.Kind, null, null, failure.Error);
            }

            var status = (int)exchange.Response!.StatusCode;
            if (status != 200)
            {
                return new WebText(WebFailure.HttpStatus, status, null, $"HTTP {status}");
            }

            if (exchange.Response.ContentLength > MaxTextBytes)
            {
                return new WebText(WebFailure.TooLarge, status, null, TooLargeError(MaxTextBytes));
            }

            using var body = new MemoryStream();
            var read = ReadBody(exchange.Response, body, MaxTextBytes, timeout, clock, null);
            if (read.Kind != WebFailure.None)
            {
                return new WebText(read.Kind, status, null, read.Error);
            }

            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(body.GetBuffer(), 0, (int)body.Length);
            return new WebText(WebFailure.None, status, text.TrimStart('﻿'), null);
        }
    }

    public WebFile Download(Uri url, Func<Uri, bool> allowRedirect, long maxBytes, string targetPath, TimeSpan timeout, Action<long>? onBytes)
    {
        ValidateUrl(url);
        if (allowRedirect is null)
        {
            throw new ArgumentNullException(nameof(allowRedirect));
        }

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Limit rozmiaru musi być dodatni.");
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Pusta ścieżka pliku docelowego.", nameof(targetPath));
        }

        ValidateTimeout(timeout);
        GuardRealNetwork(url);

        var clock = Stopwatch.StartNew();
        var current = url;
        for (var redirects = 0; ; redirects++)
        {
            using var exchange = Send(current, FileAccept, timeout, clock);
            if (exchange.Failure is { } failure)
            {
                return new WebFile(failure.Kind, null, 0, null, failure.Error);
            }

            var response = exchange.Response!;
            var status = (int)response.StatusCode;
            if (IsRedirect(status))
            {
                var location = response.Headers[HttpResponseHeader.Location];
                if (string.IsNullOrEmpty(location) || !Uri.TryCreate(current, location, out var next))
                {
                    return new WebFile(WebFailure.RedirectRefused, status, 0, null, "Przekierowanie bez poprawnego adresu docelowego.");
                }

                if (redirects >= MaxRedirects)
                {
                    return new WebFile(WebFailure.RedirectRefused, status, 0, next, $"Za dużo przekierowań (więcej niż {MaxRedirects}).");
                }

                if (!IsHttp(next) || !allowRedirect(next))
                {
                    return new WebFile(WebFailure.RedirectRefused, status, 0, next, "Przekierowanie na niedozwolony adres: " + next.AbsoluteUri);
                }

                GuardRealNetwork(next);

                // Kolejne żądanie idzie tym samym obiektem Uri, który ocenił predykat.
                current = next;
                continue;
            }

            if (status != 200)
            {
                return new WebFile(WebFailure.HttpStatus, status, 0, null, $"HTTP {status}");
            }

            if (response.ContentLength > maxBytes)
            {
                return new WebFile(WebFailure.TooLarge, status, 0, null, TooLargeError(maxBytes));
            }

            using var file = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var read = ReadBody(response, file, maxBytes, timeout, clock, onBytes);
            file.Flush();
            return new WebFile(read.Kind, status, read.Bytes, null, read.Error);
        }
    }

    /// <summary>
    /// Żądanie GET bez automatycznych przekierowań. <paramref name="url"/> trafia do żądania jako ten sam obiekt
    /// (<see cref="HttpWebRequest.RequestUri"/>), bez ponownego parsowania.
    /// </summary>
    internal static HttpWebRequest CreateRequest(Uri url, string accept, string userAgent, TimeSpan timeout)
    {
        var request = WebRequest.CreateHttp(url);
        request.Method = "GET";
        request.AllowAutoRedirect = false;
        request.UserAgent = userAgent;
        request.Accept = accept;
        var proxy = WebRequest.GetSystemWebProxy();
        proxy.Credentials = CredentialCache.DefaultCredentials;
        request.Proxy = proxy;
        request.AutomaticDecompression = DecompressionMethods.None;
        var milliseconds = Milliseconds(timeout);
        request.Timeout = milliseconds;
        request.ReadWriteTimeout = milliseconds;
        return request;
    }

    private Exchange Send(Uri url, string accept, TimeSpan timeout, Stopwatch clock)
    {
        var remaining = timeout - clock.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return Exchange.Failed(WebFailure.TimedOut, "Przekroczono limit czasu.");
        }

        var request = CreateRequest(url, accept, _userAgent, remaining);
        try
        {
            return new Exchange(request, (HttpWebResponse)request.GetResponse(), null);
        }
        catch (WebException ex) when (ex.Status == WebExceptionStatus.ProtocolError && ex.Response is HttpWebResponse response)
        {
            return new Exchange(request, response, null);
        }
        catch (WebException ex)
        {
            ex.Response?.Dispose();
            request.Abort();
            return Exchange.Failed(Classify(ex.Status == WebExceptionStatus.Timeout, timeout, clock), Describe(ex));
        }
        catch (IOException ex)
        {
            request.Abort();
            return Exchange.Failed(Classify(false, timeout, clock), ex.Message);
        }
    }

    /// <summary>Ciało do <paramref name="sink"/> buforem 81920 B; limit każdego odczytu = pozostały czas całego wywołania.</summary>
    private static BodyRead ReadBody(HttpWebResponse response, Stream sink, long maxBytes, TimeSpan timeout, Stopwatch clock, Action<long>? onBytes)
    {
        var buffer = new byte[BufferSize];
        long total = 0;

        // Strumień zamyka Exchange.Dispose (po Abort), żeby przerwanie w połowie nie doczytywało reszty ciała.
        var body = response.GetResponseStream();
        while (true)
        {
            var remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return new BodyRead(WebFailure.TimedOut, total, "Przekroczono limit czasu.");
            }

            int read;
            try
            {
                body.ReadTimeout = Milliseconds(remaining);
                read = body.Read(buffer, 0, buffer.Length);
            }
            catch (WebException ex)
            {
                return new BodyRead(Classify(ex.Status == WebExceptionStatus.Timeout, timeout, clock), total, Describe(ex));
            }
            catch (IOException ex)
            {
                return new BodyRead(Classify(false, timeout, clock), total, ex.Message);
            }
            catch (ObjectDisposedException ex)
            {
                return new BodyRead(Classify(false, timeout, clock), total, ex.Message);
            }

            if (read == 0)
            {
                // Serwer zamknął połączenie przed końcem zapowiedzianego ciała: .NET Framework zwraca wtedy 0 bez wyjątku.
                return response.ContentLength >= 0 && total != response.ContentLength
                    ? new BodyRead(WebFailure.Network, total, $"Połączenie zerwane: odebrano {total} z {response.ContentLength} B.")
                    : new BodyRead(WebFailure.None, total, null);
            }

            if (total + read > maxBytes)
            {
                return new BodyRead(WebFailure.TooLarge, total, TooLargeError(maxBytes));
            }

            sink.Write(buffer, 0, read);
            total += read;
            onBytes?.Invoke(total);
        }
    }

    private static WebFailure Classify(bool timedOut, TimeSpan timeout, Stopwatch clock)
        => timedOut || clock.Elapsed >= timeout ? WebFailure.TimedOut : WebFailure.Network;

    private static string Describe(WebException ex) => $"{ex.Status}: {ex.Message}";

    private static string TooLargeError(long maxBytes) => $"Odpowiedź większa niż {maxBytes} B.";

    private static bool IsRedirect(int status) => status is 301 or 302 or 303 or 307 or 308;

    private static bool IsHttp(Uri url)
        => url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);

    private static int Milliseconds(TimeSpan timeout)
        => (int)Math.Max(1, Math.Min(int.MaxValue, Math.Ceiling(timeout.TotalMilliseconds)));

    private static void ValidateUrl(Uri url)
    {
        if (url is null)
        {
            throw new ArgumentNullException(nameof(url));
        }

        if (!IsHttp(url))
        {
            throw new ArgumentException("Dozwolony jest tylko bezwzględny adres http/https: " + url.OriginalString, nameof(url));
        }
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Limit czasu musi być dodatni.");
        }
    }

    private static void GuardRealNetwork(Uri url)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(BlockRealNetworkVariable), "1", StringComparison.Ordinal) && !url.IsLoopback)
        {
            throw new InvalidOperationException(
                $"Bezpiecznik testów: {BlockRealNetworkVariable}=1, połączenie z {url.Host} zablokowane (testy łączą się tylko z pętlą zwrotną).");
        }
    }

    private readonly record struct BodyRead(WebFailure Kind, long Bytes, string? Error);

    private readonly record struct FailureInfo(WebFailure Kind, string Error);

    /// <summary>
    /// Jedna wymiana żądanie–odpowiedź. <see cref="Dispose"/> najpierw przerywa połączenie (<see cref="HttpWebRequest.Abort"/>), potem
    /// zamyka odpowiedź: .NET Framework przy zamknięciu nieprzeczytanego ciała próbowałby je doczytać (np. po <c>TooLarge</c> albo
    /// przekierowaniu z dużym ciałem), a tak połączenie po prostu znika.
    /// </summary>
    private sealed class Exchange : IDisposable
    {
        public Exchange(HttpWebRequest? request, HttpWebResponse? response, FailureInfo? failure)
        {
            Request = request;
            Response = response;
            Failure = failure;
        }

        public HttpWebRequest? Request { get; }

        public HttpWebResponse? Response { get; }

        public FailureInfo? Failure { get; }

        public static Exchange Failed(WebFailure kind, string error) => new(null, null, new FailureInfo(kind, error));

        public void Dispose()
        {
            Request?.Abort();
            try
            {
                Response?.Dispose();
            }
            catch (Exception ex) when (ex is WebException or IOException or ObjectDisposedException)
            {
                // Połączenie już przerwane – nic do zamknięcia.
            }
        }
    }
}
