using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>Żądanie widziane przez <see cref="LoopbackHttpServer"/>: ścieżka i nagłówki.</summary>
public sealed record SeenRequest(string Method, string Path, string? UserAgent, string? Accept);

/// <summary>
/// Serwer testowy <see cref="HttpListener"/> wyłącznie na <c>http://localhost:&lt;wolny port&gt;/</c> (pętla zwrotna, nigdy internet).
/// Każde żądanie obsługuje <c>handler</c> na osobnym zadaniu; serwer zapisuje żądania. <see cref="Release"/> (sygnalizowany też
/// w <see cref="Dispose"/>) zwalnia handlery, które celowo „wiszą” (test limitu czasu).
/// Start odporny na wyścig o port (Task 5, D4): wolny port z <see cref="FreePort"/> może zająć inny proces albo http.sys może jeszcze
/// trzymać prefiks („plik używany przez inny proces”) – wtedy nowy <see cref="HttpListener"/> na nowym porcie, najwyżej
/// <see cref="StartAttempts"/> prób.
/// </summary>
public sealed class LoopbackHttpServer : IDisposable
{
    /// <summary>Ile razy serwer próbuje wystartować na nowym porcie, zanim test padnie.</summary>
    public const int StartAttempts = 10;

    private readonly HttpListener _listener;
    private readonly Action<HttpListenerContext, LoopbackHttpServer> _handler;
    private readonly ConcurrentQueue<SeenRequest> _requests = new();
    private readonly Thread _acceptThread;

    public LoopbackHttpServer(Action<HttpListenerContext, LoopbackHttpServer> handler)
    {
        _handler = handler;
        (_listener, Port) = StartOnFreePort();
        BaseUrl = new Uri($"http://localhost:{Port}/");
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "LoopbackHttpServer" };
        _acceptThread.Start();
    }

    public int Port { get; }

    public Uri BaseUrl { get; }

    public ManualResetEventSlim Release { get; } = new(false);

    public IReadOnlyList<SeenRequest> Requests => _requests.ToList();

    public Uri Url(string pathAndQuery) => new(BaseUrl, pathAndQuery);

    /// <summary>
    /// <see cref="HttpListener"/> na <c>http://localhost:&lt;port&gt;/</c> z ponowieniem na nowym porcie przy
    /// <see cref="HttpListenerException"/>/<see cref="SocketException"/> (port zajęty między sondą a startem).
    /// </summary>
    internal static (HttpListener Listener, int Port) StartOnFreePort(Func<int>? nextPort = null)
    {
        nextPort ??= FreePort;
        for (var attempt = 1; ; attempt++)
        {
            var port = nextPort();
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();
                return (listener, port);
            }
            catch (Exception ex) when ((ex is HttpListenerException or SocketException) && attempt < StartAttempts)
            {
                listener.Close();
            }
            catch
            {
                listener.Close();
                throw;
            }
        }
    }

    /// <summary>Wolny port na pętli zwrotnej, na którym nic nie słucha (po zwróceniu).</summary>
    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    public static void Respond(HttpListenerContext context, int status, byte[]? body = null, string? location = null)
    {
        context.Response.StatusCode = status;
        if (location is not null)
        {
            context.Response.RedirectLocation = location;
        }

        body ??= Array.Empty<byte>();
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body, 0, body.Length);
        context.Response.Close();
    }

    public void Dispose()
    {
        Release.Set();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Już zamknięty.
        }

        _acceptThread.Join(TimeSpan.FromSeconds(5));
    }

    private void AcceptLoop()
    {
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = _listener.GetContext();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _requests.Enqueue(new SeenRequest(
                context.Request.HttpMethod,
                context.Request.Url.AbsolutePath,
                context.Request.UserAgent,
                context.Request.Headers["Accept"]));
            Task.Run(() =>
            {
                try
                {
                    _handler(context, this);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException
                                               or System.IO.IOException)
                {
                    // Klient zerwał połączenie (np. TooLarge, limit czasu) – oczekiwane w testach.
                }
                finally
                {
                    try
                    {
                        context.Response.Abort();
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
                    {
                        // Już zamknięta.
                    }
                }
            });
        }
    }
}
