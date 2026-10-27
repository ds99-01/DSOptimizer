using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows;

/// <summary>
/// Wątek czytający potok wyjścia narzędzia (runnery <see cref="System32ToolRunner"/> i <see cref="ServicingToolRunner"/>).
/// Dwa sposoby zakończenia:
/// <list type="bullet">
/// <item><see cref="Stop"/> (narzędzie w Job Objecie, już zabite): blokujący <c>ReadFile</c> jest przerywany przez
/// <c>CancelSynchronousIo</c>, więc czekanie zawsze ma limit – także gdy koniec zapisu trzyma proces spoza drzewa (np. odziedziczył
/// uchwyt w wyścigu z <c>Process.Start</c> bez listy uchwytów). Potem <see cref="Dispose"/> zamyka nasz koniec potoku.</item>
/// <item><see cref="Detach"/> (narzędzie, którego nie wolno przerwać): wątek tła czyta dalej aż do EOF, a dopiero potem sam zamyka
/// swój koniec potoku. Narzędzie nigdy nie dostaje błędu zerwanego potoku ani nie blokuje się na pełnym potoku.</item>
/// </list>
/// Wyjątek odbiorcy danych trafia do <see cref="Failure"/>; od tej chwili dane nie idą już do odbiorcy, ale potok jest dalej opróżniany.
/// </summary>
internal sealed class PipeReader : IDisposable
{
    private const int ChunkSize = 4096;
    private const int CancelRetryMilliseconds = 50;

    private readonly Stream _source;
    private readonly Action<byte[], int> _onData;
    private readonly Thread _thread;
    private readonly object _sync = new();
    private IntPtr _nativeThread;
    private volatile bool _stopping;
    private volatile Exception? _failure;
    private bool _ended;
    private bool _detached;

    public PipeReader(Stream source, Action<byte[], int> onData, string name)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _onData = onData ?? throw new ArgumentNullException(nameof(onData));
        _thread = new Thread(Loop) { IsBackground = true, Name = name };
    }

    /// <summary>Wyjątek odbiorcy danych; <c>null</c> = brak.</summary>
    public Exception? Failure => _failure;

    /// <summary>Startuje wątek i czeka, aż będzie gotowy do przerwania przez <see cref="Stop"/>.</summary>
    public void Start()
    {
        using var ready = new ManualResetEventSlim(initialState: false);
        _thread.Start(ready);
        ready.Wait();
    }

    /// <summary>Czeka najwyżej <paramref name="limit"/> na naturalny koniec (EOF); true = wątek zakończony.</summary>
    public bool WaitForEnd(TimeSpan limit) => _thread.Join(limit);

    /// <summary>Przerywa czytanie i czeka najwyżej <paramref name="limit"/>; true = wątek zakończony.</summary>
    public bool Stop(TimeSpan limit)
    {
        _stopping = true;
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            lock (_sync)
            {
                // Gdy wątek nie jest akurat w ReadFile, wywołanie nic nie robi; dlatego ponawiamy do skutku albo limitu.
                if (_nativeThread != IntPtr.Zero)
                {
                    NativeMethods.CancelSynchronousIo(_nativeThread);
                }
            }

            if (_thread.Join(CancelRetryMilliseconds))
            {
                return true;
            }

            if (stopwatch.Elapsed >= limit)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Odłącza wątek: czyta dalej w tle aż do EOF i wtedy sam zamyka strumień (nasz koniec potoku). Bez czekania i bez przerywania.
    /// Po <see cref="Detach"/> nie wolno już wołać <see cref="Dispose"/>.
    /// </summary>
    public void Detach()
    {
        lock (_sync)
        {
            if (!_ended)
            {
                _detached = true;
                return;
            }
        }

        _source.Dispose();
    }

    /// <summary>Zamyka strumień (uchwyt potoku zamyka się najpóźniej po powrocie trwającego odczytu).</summary>
    public void Dispose() => _source.Dispose();

    private void Loop(object? ready)
    {
        lock (_sync)
        {
            _nativeThread = NativeMethods.OpenThread(NativeMethods.ThreadTerminate, inheritHandle: false, NativeMethods.GetCurrentThreadId());
        }

        ((ManualResetEventSlim)ready!).Set();
        try
        {
            ReadUntilEnd();
        }
        finally
        {
            bool closeSource;
            lock (_sync)
            {
                if (_nativeThread != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(_nativeThread);
                    _nativeThread = IntPtr.Zero;
                }

                _ended = true;
                closeSource = _detached;
            }

            if (closeSource)
            {
                _source.Dispose();
            }
        }
    }

    private void ReadUntilEnd()
    {
        var chunk = new byte[ChunkSize];
        while (!_stopping)
        {
            int read;
            try
            {
                read = _source.Read(chunk, 0, chunk.Length);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // Potok zamknięty po drugiej stronie albo odczyt przerwany przez Stop = koniec danych.
                return;
            }

            if (read == 0 || _stopping)
            {
                return;
            }

            if (_failure is not null)
            {
                continue;
            }

            try
            {
                _onData(chunk, read);
            }
            catch (Exception ex)
            {
                // Wyjątek w wątku tła zamknąłby cały proces; runner przekazuje go wywołującemu, a potok dalej opróżniamy.
                _failure = ex;
            }
        }
    }
}
