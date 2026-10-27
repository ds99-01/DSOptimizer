using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Pobieranie HTTP wyłącznie z serwera <see cref="LoopbackHttpServer"/> na <c>http://localhost:&lt;port&gt;/</c> (RISK: testy nigdy
/// nie łączą się z internetem – bezpiecznik <c>DSO_BLOCK_REAL_NETWORK=1</c> z <see cref="TestGuards"/>), pliki w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>.
/// </summary>
[Trait("Category", "Windows")]
public class WebDownloaderTests
{
    private const string Json = "application/vnd.github+json";
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(20);

    [Fact]
    public void GetText_200_BodyUserAgentAccept()
    {
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 200, Encoding.UTF8.GetBytes("{\"tag_name\":\"1.5.0\"} zażółć")));

        var result = new WebDownloader("DSOptimizer").GetText(server.Url("/api"), Json, Generous);

        Assert.Equal(new WebText(WebFailure.None, 200, "{\"tag_name\":\"1.5.0\"} zażółć", null), result);
        var seen = Assert.Single(server.Requests);
        Assert.Equal(new SeenRequest("GET", "/api", "DSOptimizer", Json), seen);
    }

    [Fact]
    public void GetText_403_HttpStatus()
    {
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 403, Encoding.UTF8.GetBytes("{\"message\":\"rate limit\"}")));

        var result = new WebDownloader("DSOptimizer").GetText(server.Url("/api"), Json, Generous);

        Assert.Equal(WebFailure.HttpStatus, result.Failure);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Body);
    }

    [Fact]
    public void GetText_Redirect_NotFollowed()
    {
        using var server = new LoopbackHttpServer((c, s) =>
        {
            if (c.Request.Url.AbsolutePath == "/a")
            {
                LoopbackHttpServer.Respond(c, 302, location: s.Url("/b").AbsoluteUri);
            }
            else
            {
                LoopbackHttpServer.Respond(c, 200, Encoding.UTF8.GetBytes("b"));
            }
        });

        var result = new WebDownloader("DSOptimizer").GetText(server.Url("/a"), Json, Generous);

        Assert.Equal(WebFailure.HttpStatus, result.Failure);
        Assert.Equal(302, result.StatusCode);
        Assert.Null(result.Body);
        Assert.Equal(new[] { "/a" }, server.Requests.Select(r => r.Path));
    }

    [Fact]
    public void GetText_BodyOver1MB_TooLarge()
    {
        using var server = new LoopbackHttpServer((c, _) =>
        {
            c.Response.StatusCode = 200;
            c.Response.SendChunked = true;
            var chunk = new byte[64 * 1024];
            for (var i = 0; i < 17; i++)
            {
                c.Response.OutputStream.Write(chunk, 0, chunk.Length);
            }

            c.Response.Close();
        });

        var result = new WebDownloader("DSOptimizer").GetText(server.Url("/api"), Json, Generous);

        Assert.Equal(WebFailure.TooLarge, result.Failure);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Body);
    }

    [Fact]
    public void GetText_ContentLengthOver1MB_TooLarge()
    {
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 200, new byte[(1024 * 1024) + 1]));

        var result = new WebDownloader("DSOptimizer").GetText(server.Url("/api"), Json, Generous);

        Assert.Equal(WebFailure.TooLarge, result.Failure);
    }

    [Fact]
    public void GetText_Exactly1MB_Accepted()
    {
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 200, Enumerable.Repeat((byte)'x', 1024 * 1024).ToArray()));

        var result = new WebDownloader("DSOptimizer").GetText(server.Url("/api"), Json, Generous);

        Assert.Equal(WebFailure.None, result.Failure);
        Assert.Equal(1024 * 1024, result.Body!.Length);
    }

    [Fact]
    public void Download_RedirectAllowed_WritesFileAndProgress()
    {
        var payload = RandomBytes(300 * 1024 + 7);
        using var files = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 200, payload));
        using var api = new LoopbackHttpServer((c, s) =>
        {
            if (c.Request.Url.AbsolutePath == "/start")
            {
                LoopbackHttpServer.Respond(c, 302, location: "/mid");
            }
            else
            {
                LoopbackHttpServer.Respond(c, 307, location: files.Url("/file.msi").AbsoluteUri);
            }
        });
        using var temp = new TempDirectory();
        var target = temp.Sub("out.msi");
        var hops = new List<Uri>();
        var progress = new List<long>();

        var result = new WebDownloader("DSOptimizer").Download(
            api.Url("/start"), u => { hops.Add(u); return true; }, 1024 * 1024, target, Generous, progress.Add);

        Assert.Equal(new WebFile(WebFailure.None, 200, payload.Length, null, null), result);
        Assert.Equal(new[] { api.Url("/mid"), files.Url("/file.msi") }, hops);
        Assert.Equal(payload, File.ReadAllBytes(target));
        Assert.Equal(payload.Length, progress.Last());
        Assert.Equal(progress.OrderBy(p => p), progress);
        Assert.Equal(new[] { "/start", "/mid" }, api.Requests.Select(r => r.Path));
        Assert.Equal(new SeenRequest("GET", "/file.msi", "DSOptimizer", "application/octet-stream"), Assert.Single(files.Requests));
    }

    // Review Focus 4: odrzucony skok = drugi serwer nie dostaje żadnego żądania, plik nie powstaje.
    [Fact]
    public void Download_RedirectRefused_SecondHopNotRequested()
    {
        using var second = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 200, new byte[10]));
        using var first = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 302, location: second.Url("/evil.msi").AbsoluteUri));
        using var temp = new TempDirectory();
        var target = temp.Sub("out.msi");
        var progress = new List<long>();

        var result = new WebDownloader("DSOptimizer").Download(first.Url("/start"), _ => false, 1024, target, Generous, progress.Add);

        Assert.Equal(WebFailure.RedirectRefused, result.Failure);
        Assert.Equal(302, result.StatusCode);
        Assert.Equal(second.Url("/evil.msi"), result.RefusedUrl);
        Assert.Equal(0, result.Bytes);
        Assert.Empty(second.Requests);
        Assert.Single(first.Requests);
        Assert.False(File.Exists(target));
        Assert.Empty(progress);
    }

    [Fact]
    public void Download_RedirectLoop_Refused()
    {
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 301, location: "/loop"));
        using var temp = new TempDirectory();
        var asked = 0;

        var result = new WebDownloader("DSOptimizer").Download(
            server.Url("/loop"), _ => { asked++; return true; }, 1024, temp.Sub("out.msi"), Generous, null);

        Assert.Equal(WebFailure.RedirectRefused, result.Failure);
        Assert.Equal(301, result.StatusCode);
        Assert.Equal(server.Url("/loop"), result.RefusedUrl);
        Assert.Equal(5, asked);
        Assert.Equal(6, server.Requests.Count);
        Assert.False(File.Exists(temp.Sub("out.msi")));
    }

    [Fact]
    public void Download_ContentLengthOverLimit_TooLarge_NoBodyRead()
    {
        // Serwer wysyła nagłówki i jeden bajt, potem wisi: gdyby downloader czytał ciało, skończyłby się limitem czasu.
        using var server = new LoopbackHttpServer((c, s) =>
        {
            c.Response.StatusCode = 200;
            c.Response.ContentLength64 = 2048;
            c.Response.OutputStream.Write(new byte[1], 0, 1);
            c.Response.OutputStream.Flush();
            s.Release.Wait(TimeSpan.FromSeconds(30));
        });
        using var temp = new TempDirectory();
        var target = temp.Sub("out.msi");
        var progress = new List<long>();
        var clock = Stopwatch.StartNew();

        var result = new WebDownloader("DSOptimizer").Download(server.Url("/f"), _ => true, 1024, target, TimeSpan.FromSeconds(10), progress.Add);

        Assert.Equal(new WebFile(WebFailure.TooLarge, 200, 0, null, result.Error), result);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), clock.Elapsed.ToString());
        Assert.False(File.Exists(target));
        Assert.Empty(progress);
    }

    [Fact]
    public void Download_ChunkedOverLimit_TooLarge()
    {
        using var server = new LoopbackHttpServer((c, _) =>
        {
            c.Response.StatusCode = 200;
            c.Response.SendChunked = true;
            var chunk = new byte[16 * 1024];
            for (var i = 0; i < 8; i++)
            {
                c.Response.OutputStream.Write(chunk, 0, chunk.Length);
            }

            c.Response.Close();
        });
        using var temp = new TempDirectory();
        var progress = new List<long>();

        var result = new WebDownloader("DSOptimizer").Download(server.Url("/f"), _ => true, 100 * 1024, temp.Sub("out.msi"), Generous, progress.Add);

        Assert.Equal(WebFailure.TooLarge, result.Failure);
        Assert.Equal(200, result.StatusCode);
        Assert.InRange(result.Bytes, 0, 100 * 1024);
        Assert.All(progress, p => Assert.True(p <= 100 * 1024));
        Assert.True(new FileInfo(temp.Sub("out.msi")).Length <= 100 * 1024);
    }

    [Fact]
    public void Download_ExactlyMaxBytes_Accepted()
    {
        var payload = RandomBytes(4096);
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 200, payload));
        using var temp = new TempDirectory();

        var result = new WebDownloader("DSOptimizer").Download(server.Url("/f"), _ => true, 4096, temp.Sub("out.msi"), Generous, null);

        Assert.Equal(new WebFile(WebFailure.None, 200, 4096, null, null), result);
    }

    [Fact]
    public void Download_SlowServer_TimedOut()
    {
        // Nagłówki i część ciała, potem cisza: jeden termin na całe wywołanie (1 s), nie na pojedynczy odczyt.
        using var server = new LoopbackHttpServer((c, s) =>
        {
            c.Response.StatusCode = 200;
            c.Response.ContentLength64 = 1000;
            c.Response.OutputStream.Write(new byte[100], 0, 100);
            c.Response.OutputStream.Flush();
            s.Release.Wait(TimeSpan.FromSeconds(30));
        });
        using var temp = new TempDirectory();
        var clock = Stopwatch.StartNew();

        var result = new WebDownloader("DSOptimizer").Download(server.Url("/f"), _ => true, 4096, temp.Sub("out.msi"), TimeSpan.FromSeconds(1), null);

        Assert.Equal(WebFailure.TimedOut, result.Failure);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), clock.Elapsed.ToString());
    }

    [Fact]
    public void Download_NoResponseHeaders_TimedOut()
    {
        using var server = new LoopbackHttpServer((c, s) => s.Release.Wait(TimeSpan.FromSeconds(30)));
        using var temp = new TempDirectory();
        var clock = Stopwatch.StartNew();

        var result = new WebDownloader("DSOptimizer").Download(server.Url("/f"), _ => true, 4096, temp.Sub("out.msi"), TimeSpan.FromSeconds(1), null);

        Assert.Equal(WebFailure.TimedOut, result.Failure);
        Assert.Null(result.StatusCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), clock.Elapsed.ToString());
        Assert.False(File.Exists(temp.Sub("out.msi")));
    }

    [Fact]
    public void Download_NothingListening_Network()
    {
        using var temp = new TempDirectory();
        var url = new Uri($"http://localhost:{LoopbackHttpServer.FreePort()}/f.msi");

        var result = new WebDownloader("DSOptimizer").Download(url, _ => true, 4096, temp.Sub("out.msi"), Generous, null);

        Assert.Equal(WebFailure.Network, result.Failure);
        Assert.Null(result.StatusCode);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void GetText_NothingListening_Network()
    {
        var url = new Uri($"http://localhost:{LoopbackHttpServer.FreePort()}/api");

        var result = new WebDownloader("DSOptimizer").GetText(url, Json, Generous);

        Assert.Equal(WebFailure.Network, result.Failure);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void Download_404_HttpStatus_NoFile()
    {
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 404));
        using var temp = new TempDirectory();

        var result = new WebDownloader("DSOptimizer").Download(server.Url("/f"), _ => true, 4096, temp.Sub("out.msi"), Generous, null);

        Assert.Equal(WebFailure.HttpStatus, result.Failure);
        Assert.Equal(404, result.StatusCode);
        Assert.False(File.Exists(temp.Sub("out.msi")));
    }

    [Fact]
    public void Download_RedirectToNonHttpScheme_Refused_PredicateNotAsked()
    {
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 302, location: "file:///C:/Windows/win.ini"));
        using var temp = new TempDirectory();
        var asked = 0;

        var result = new WebDownloader("DSOptimizer").Download(server.Url("/f"), _ => { asked++; return true; }, 4096, temp.Sub("out.msi"), Generous, null);

        Assert.Equal(WebFailure.RedirectRefused, result.Failure);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Download_TargetExists_ThrowsIOException_FileUntouched()
    {
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 200, new byte[] { 1, 2, 3 }));
        using var temp = new TempDirectory();
        var target = temp.Sub("out.msi");
        File.WriteAllText(target, "stary");

        Assert.ThrowsAny<IOException>(() =>
            new WebDownloader("DSOptimizer").Download(server.Url("/f"), _ => true, 4096, target, Generous, null));
        Assert.Equal("stary", File.ReadAllText(target));
    }

    // RISK: bezpiecznik – adres spoza pętli zwrotnej (także na skoku przekierowania) = wyjątek przed żądaniem, zero połączeń.
    [Fact]
    public void RealHostBlockedInTests()
    {
        var downloader = new WebDownloader("DSOptimizer");
        using var temp = new TempDirectory();

        var text = Assert.Throws<InvalidOperationException>(() =>
            downloader.GetText(new Uri("https://api.github.com/repos/rustdesk/rustdesk/releases/latest"), Json, Generous));
        Assert.StartsWith("Bezpiecznik testów: ", text.Message, StringComparison.Ordinal);

        var file = Assert.Throws<InvalidOperationException>(() =>
            downloader.Download(new Uri("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi"), _ => true, 4096, temp.Sub("a.msi"), Generous, null));
        Assert.StartsWith("Bezpiecznik testów: ", file.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(temp.Sub("a.msi")));
    }

    // Adres dokumentacyjny TEST-NET-1 (nigdy nie routowany): nawet zepsuty bezpiecznik nie wyjdzie do prawdziwego hosta.
    [Fact]
    public void RealHostBlockedInTests_NonLoopbackAddress()
    {
        using var temp = new TempDirectory();
        var downloader = new WebDownloader("DSOptimizer");

        Assert.Throws<InvalidOperationException>(() => downloader.GetText(new Uri("http://192.0.2.1/api"), Json, Generous));
        Assert.Throws<InvalidOperationException>(() =>
            downloader.Download(new Uri("http://192.0.2.1/x.msi"), _ => true, 4096, temp.Sub("x.msi"), Generous, null));
        Assert.False(File.Exists(temp.Sub("x.msi")));
    }

    [Fact]
    public void RealHostBlockedInTests_OnRedirectHop()
    {
        using var server = new LoopbackHttpServer((c, _) => LoopbackHttpServer.Respond(c, 302, location: "https://192.0.2.1/x.msi"));
        using var temp = new TempDirectory();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new WebDownloader("DSOptimizer").Download(server.Url("/f"), _ => true, 4096, temp.Sub("out.msi"), Generous, null));

        Assert.StartsWith("Bezpiecznik testów: ", ex.Message, StringComparison.Ordinal);
        Assert.Single(server.Requests);
    }

    // R4: SystemDefault (domyślne na net481) zostaje – system sam wybiera TLS 1.2/1.3; inna wartość dostaje bit Tls12.
    [Theory]
    [InlineData(SecurityProtocolType.SystemDefault, SecurityProtocolType.SystemDefault)]
    [InlineData(SecurityProtocolType.Tls13, SecurityProtocolType.Tls13 | SecurityProtocolType.Tls12)]
    [InlineData(SecurityProtocolType.Tls12, SecurityProtocolType.Tls12)]
    public void Tls12Policy_SystemDefaultUntouched_OtherGetsTls12(SecurityProtocolType before, SecurityProtocolType expected)
    {
        var global = ServicePointManager.SecurityProtocol;
        try
        {
            ServicePointManager.SecurityProtocol = before;

            WebDownloader.EnsureTls12();

            Assert.Equal(expected, ServicePointManager.SecurityProtocol);
        }
        finally
        {
            ServicePointManager.SecurityProtocol = global;
        }
    }

    [Fact]
    public void Tls12Enabled_AfterConstruction()
    {
        _ = new WebDownloader("DSOptimizer");

        var protocol = ServicePointManager.SecurityProtocol;
        Assert.True(protocol == SecurityProtocolType.SystemDefault || (protocol & SecurityProtocolType.Tls12) == SecurityProtocolType.Tls12);
    }

    // I1: serwer zapowiada 1000 B, wysyła 100 B i zamyka połączenie – zerwane pobranie to Network, nie sukces.
    [Fact]
    public void Download_BodyShorterThanContentLength_Network()
    {
        using var server = new RawHttpServer("HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n", new byte[100]);
        using var temp = new TempDirectory();

        var result = new WebDownloader("DSOptimizer").Download(server.Url, _ => true, 4096, temp.Sub("out.msi"), Generous, null);

        Assert.Equal(WebFailure.Network, result.Failure);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(100, result.Bytes);
        Assert.Contains("100", result.Error, StringComparison.Ordinal);
        Assert.Contains("1000", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void GetText_BodyShorterThanContentLength_Network()
    {
        using var server = new RawHttpServer("HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n", Encoding.ASCII.GetBytes("{\"tag_name\":"));

        var result = new WebDownloader("DSOptimizer").GetText(server.Url, Json, Generous);

        Assert.Equal(WebFailure.Network, result.Failure);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Body);
    }

    [Fact]
    public void Download_ContentLengthMatches_RawServer_None()
    {
        using var server = new RawHttpServer("HTTP/1.1 200 OK\r\nContent-Length: 100\r\nConnection: close\r\n\r\n", new byte[100]);
        using var temp = new TempDirectory();

        var result = new WebDownloader("DSOptimizer").Download(server.Url, _ => true, 4096, temp.Sub("out.msi"), Generous, null);

        Assert.Equal(new WebFile(WebFailure.None, 200, 100, null, null), result);
    }

    // M1: skok bez poprawnego adresu docelowego = odmowa bez pytania predykatu, jedno połączenie.
    [Theory]
    [InlineData("HTTP/1.1 302 Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")]
    [InlineData("HTTP/1.1 302 Found\r\nLocation: \r\nContent-Length: 0\r\nConnection: close\r\n\r\n")]
    [InlineData("HTTP/1.1 302 Found\r\nLocation: http://localhost:99999/x.msi\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")]
    [InlineData("HTTP/1.1 302 Found\r\nLocation: http://[::1/x.msi\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")]
    public void Download_RedirectWithoutValidLocation_Refused(string headers)
    {
        using var server = new RawHttpServer(headers);
        using var temp = new TempDirectory();
        var asked = 0;

        var result = new WebDownloader("DSOptimizer").Download(server.Url, _ => { asked++; return true; }, 4096, temp.Sub("out.msi"), Generous, null);

        Assert.Equal(new WebFile(WebFailure.RedirectRefused, 302, 0, null, result.Error), result);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Equal(0, asked);
        Assert.Equal(1, server.Connections);
        Assert.False(File.Exists(temp.Sub("out.msi")));
    }

    [Fact]
    public void CreateRequest_NoAutoRedirect_SystemProxyDefaultCredentials()
    {
        var url = new Uri("https://github.com/rustdesk/rustdesk/releases/download/1.5.0/rustdesk-1.5.0-x86_64.msi");

        var request = WebDownloader.CreateRequest(url, "application/octet-stream", "DSOptimizer", TimeSpan.FromSeconds(3));

        // Warunek z review Tasku 1: żądanie niesie dokładnie ten obiekt Uri, który ocenił predykat.
        Assert.Same(url, request.RequestUri);
        Assert.Equal("GET", request.Method);
        Assert.False(request.AllowAutoRedirect);
        Assert.Equal("DSOptimizer", request.UserAgent);
        Assert.Equal("application/octet-stream", request.Accept);
        Assert.NotNull(request.Proxy);
        Assert.Equal(WebRequest.GetSystemWebProxy().GetType(), request.Proxy.GetType());
        Assert.Same(CredentialCache.DefaultCredentials, request.Proxy.Credentials);
        Assert.False(request.UseDefaultCredentials);
        Assert.Equal(DecompressionMethods.None, request.AutomaticDecompression);
        Assert.Equal(3000, request.Timeout);
        Assert.Equal(3000, request.ReadWriteTimeout);
    }

    [Theory]
    [InlineData("ftp://localhost/x")]
    [InlineData("file:///C:/x.msi")]
    public void NonHttpUrl_ArgumentException(string url)
    {
        using var temp = new TempDirectory();
        var downloader = new WebDownloader("DSOptimizer");

        Assert.Throws<ArgumentException>(() => downloader.GetText(new Uri(url), Json, Generous));
        Assert.Throws<ArgumentException>(() => downloader.Download(new Uri(url), _ => true, 1, temp.Sub("x.msi"), Generous, null));
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }
}
