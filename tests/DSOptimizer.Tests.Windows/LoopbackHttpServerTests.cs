using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using DSOptimizer.Tests.Windows.Support;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Pomocnik serwera testowego (Task 5, D4): port zajęty między sondą a startem <see cref="HttpListener"/> nie wywraca testu – serwer
/// próbuje na nowym porcie. Wyłącznie pętla zwrotna; zajęty port trzyma w teście zwykłe gniazdo, nikt się z nim nie łączy.
/// </summary>
[Trait("Category", "Windows")]
public class LoopbackHttpServerTests
{
    private static TcpListener Occupy()
    {
        var blocker = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        blocker.Start();
        return blocker;
    }

    private static int PortOf(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    [Fact]
    public void StartOnFreePort_PortTaken_RetriesOnNextPort()
    {
        var blocker = Occupy();
        try
        {
            var taken = PortOf(blocker);
            var ports = new Queue<int>(new[] { taken });
            var asked = 0;

            var (listener, port) = LoopbackHttpServer.StartOnFreePort(() =>
            {
                asked++;
                return ports.Count > 0 ? ports.Dequeue() : LoopbackHttpServer.FreePort();
            });

            using (listener)
            {
                Assert.True(listener.IsListening);
                Assert.NotEqual(taken, port);
                Assert.Equal(2, asked);
            }
        }
        finally
        {
            blocker.Stop();
        }
    }

    [Fact]
    public void StartOnFreePort_AlwaysTaken_FailsAfterLimit()
    {
        var blocker = Occupy();
        try
        {
            var asked = 0;

            Assert.Throws<HttpListenerException>(() => LoopbackHttpServer.StartOnFreePort(() =>
            {
                asked++;
                return PortOf(blocker);
            }));
            Assert.Equal(LoopbackHttpServer.StartAttempts, asked);
        }
        finally
        {
            blocker.Stop();
        }
    }
}
