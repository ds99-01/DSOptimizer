using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Wątek czytający potok runnerów: kończy się z limitem także wtedy, gdy koniec zapisu trzyma ktoś inny (np. proces, który
/// odziedziczył uchwyt w wyścigu z <c>Process.Start</c>) – bez <c>Join()</c> bez limitu.
/// </summary>
[Trait("Category", "Windows")]
public class PipeReaderTests
{
    [Fact]
    public void WriterClosed_DataDeliveredAndEndsNaturally()
    {
        var received = new StringBuilder();
        using var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        using var readerStream = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        using var reader = new PipeReader(readerStream, (buffer, count) => received.Append(Encoding.ASCII.GetString(buffer, 0, count)), "test");
        reader.Start();

        var bytes = Encoding.ASCII.GetBytes("abc\r\n");
        writer.Write(bytes, 0, bytes.Length);
        writer.Dispose();

        Assert.True(reader.WaitForEnd(TimeSpan.FromSeconds(5)));
        Assert.Equal("abc\r\n", received.ToString());
        Assert.Null(reader.Failure);
    }

    [Fact]
    public void Stop_WriterStillOpen_EndsWithinLimit()
    {
        using var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        using var readerStream = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        using var reader = new PipeReader(readerStream, (_, _) => { }, "test");
        reader.Start();

        Assert.False(reader.WaitForEnd(TimeSpan.FromMilliseconds(300)));
        var stopwatch = Stopwatch.StartNew();
        Assert.True(reader.Stop(TimeSpan.FromSeconds(5)));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Zatrzymanie trwało {stopwatch.Elapsed}.");
    }

    [Fact]
    public void OnDataThrows_FailureKeptAndThreadEnds()
    {
        using var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        using var readerStream = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        using var reader = new PipeReader(readerStream, (_, _) => throw new InvalidOperationException("odbiorca"), "test");
        reader.Start();

        writer.Write(new byte[] { 65 }, 0, 1);
        writer.Write(new byte[64 * 1024], 0, 64 * 1024); // po błędzie odbiorcy potok dalej jest opróżniany (zapis nie wisi)
        writer.Dispose();

        Assert.True(reader.WaitForEnd(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(reader.Failure);
    }

    [Fact]
    public void Detach_KeepsDrainingUntilEof_ThenClosesItsEnd()
    {
        var received = 0;
        var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        var readerStream = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        var reader = new PipeReader(readerStream, (_, count) => Interlocked.Add(ref received, count), "test");
        reader.Start();
        var first = Encoding.ASCII.GetBytes("przed\r\n");
        writer.Write(first, 0, first.Length);

        reader.Detach();

        // Narzędzie pisze dalej (więcej niż bufor potoku): bez błędu zerwanego potoku i bez blokady.
        var chunk = new byte[64 * 1024];
        for (var i = 0; i < 4; i++)
        {
            writer.Write(chunk, 0, chunk.Length);
        }

        writer.Dispose();
        Assert.True(reader.WaitForEnd(TimeSpan.FromSeconds(5)));
        // Wątek tła po EOF sam zamknął swój koniec potoku (strumień zamknięty, nie samo EOF = -1).
        Assert.Throws<ObjectDisposedException>(() => readerStream.ReadByte());
        Assert.True(Volatile.Read(ref received) >= first.Length);
    }
}
