using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Surowy serwer TCP wyłącznie na <see cref="IPAddress.Loopback"/> (127.0.0.1, nigdy internet) do odpowiedzi, których
/// <see cref="System.Net.HttpListener"/> nie wyśle: ciało krótsze niż <c>Content-Length</c>, pusty albo niepoprawny <c>Location</c>.
/// Na każde połączenie czyta nagłówki żądania, wysyła <c>response</c> bajt w bajt i zamyka połączenie.
/// </summary>
public sealed class RawHttpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly byte[] _response;
    private readonly Thread _thread;
    private int _connections;

    public RawHttpServer(string headers, byte[]? body = null)
    {
        var head = Encoding.ASCII.GetBytes(headers);
        body ??= Array.Empty<byte>();
        _response = new byte[head.Length + body.Length];
        Buffer.BlockCopy(head, 0, _response, 0, head.Length);
        Buffer.BlockCopy(body, 0, _response, head.Length, body.Length);
        _listener.Start();
        Url = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/f");
        _thread = new Thread(Serve) { IsBackground = true, Name = "RawHttpServer" };
        _thread.Start();
    }

    public Uri Url { get; }

    public int Connections => Volatile.Read(ref _connections);

    public void Dispose()
    {
        _listener.Stop();
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private void Serve()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            Interlocked.Increment(ref _connections);
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    ReadHeaders(stream);
                    stream.Write(_response, 0, _response.Length);
                    stream.Flush();
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    // Klient rozłączył się wcześniej – bez znaczenia dla testu.
                }
            }
        }
    }

    private static void ReadHeaders(NetworkStream stream)
    {
        var tail = 0;
        while (tail < 4)
        {
            var b = stream.ReadByte();
            if (b < 0)
            {
                return;
            }

            tail = (tail, b) switch
            {
                (0, '\r') or (2, '\r') => tail + 1,
                (1, '\n') or (3, '\n') => tail + 1,
                (_, '\r') => 1,
                _ => 0,
            };
        }
    }
}
