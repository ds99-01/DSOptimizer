using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Repair;
using DSOptimizer.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Windows;

/// <summary>
/// Wspólny przebieg procesu, którego nigdy nie zabijamy (<see cref="ServicingToolRunner"/>, <see cref="WingetRunner"/>):
/// <c>CreateProcessW</c> pełnej ścieżki przez <see cref="RedirectedProcess.Start"/> (stdout i stderr jednym potokiem, stdin = NUL,
/// dziedziczone tylko te dwa uchwyty), **bez Job Objectu**. Po <c>waitLimit</c> zwraca od razu <c>TimedOut=true</c> i
/// <c>ExitCode=null</c>, a proces działa dalej (przeżywa też zamknięcie DSOptimizera). Potoku nie zamykamy pod działającym procesem
/// (Ruling R16: błąd zapisu na stdout mógłby przerwać narzędzie albo instalator w trakcie pracy): wątek czytający przechodzi w tło
/// (<see cref="PipeReader.Detach"/>), opróżnia potok do EOF, odrzuca dane i dopiero wtedy zamyka swój koniec. Tak samo, gdy proces
/// się skończył, a koniec zapisu trzyma jeszcze jego potomek dłużej niż <see cref="System32ToolRunner.ReaderLimit"/>. Bajty z potoku
/// idą na bieżąco do <see cref="ConsoleLineSplitter"/> z podanym kodowaniem (UTF-16LE wykrywane z pierwszych bajtów), a gotowe linie
/// do <c>onLine</c> (na wątku czytającym) i do <see cref="ServicingToolRun.Output"/> – tylko do powrotu z <see cref="Run"/>.
/// Wyjątek z <c>onLine</c> trafia do wywołującego.
/// </summary>
internal static class UnkilledProcess
{
    /// <param name="executablePath">Pełna ścieżka programu (nigdy <c>PATH</c> ani katalog bieżący).</param>
    /// <param name="request">Nazwa pliku do komunikatów i linia argumentów.</param>
    /// <param name="workingDirectory">Katalog roboczy procesu.</param>
    /// <param name="encoding">Kodowanie wyjścia (strona OEM narzędzi albo UTF-8 winget).</param>
    /// <param name="waitLimit">Jak długo najwyżej czekać na koniec procesu (&gt; 0).</param>
    /// <param name="onLine">Odbiorca linii (postęp), może być <c>null</c>.</param>
    public static ServicingToolRun Run(
        string executablePath,
        ToolRequest request,
        string workingDirectory,
        Encoding encoding,
        TimeSpan waitLimit,
        Action<string>? onLine)
    {
        if (executablePath is null)
        {
            throw new ArgumentNullException(nameof(executablePath));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (workingDirectory is null)
        {
            throw new ArgumentNullException(nameof(workingDirectory));
        }

        if (encoding is null)
        {
            throw new ArgumentNullException(nameof(encoding));
        }

        if (waitLimit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitLimit), waitLimit, "Limit czekania musi być dodatni.");
        }

        if (!File.Exists(executablePath))
        {
            throw new OperationException(OperationErrorKind.NotFound, $"Nie znaleziono pliku {executablePath}.");
        }

        var collector = new LineCollector(encoding, onLine);
        var result = Execute(executablePath, request, workingDirectory, waitLimit, collector);
        if (collector.Failure is not null)
        {
            ExceptionDispatchInfo.Capture(collector.Failure).Throw();
        }

        return result;
    }

    private static ServicingToolRun Execute(string executablePath, ToolRequest request, string workingDirectory, TimeSpan waitLimit, LineCollector collector)
    {
        var (readPipe, writePipe) = RedirectedProcess.CreateOutputPipe();
        var nul = IntPtr.Zero;
        var process = IntPtr.Zero;
        var thread = IntPtr.Zero;
        PipeReader? reader = null;
        try
        {
            nul = RedirectedProcess.OpenNul();
            (process, thread) = RedirectedProcess.Start(executablePath, request, workingDirectory, nul, writePipe, extraFlags: 0);

            // Dziecko ma własne kopie; nasze końce zapisu zamykamy, żeby koniec procesu dawał EOF na potoku.
            NativeMethods.CloseHandle(writePipe);
            writePipe = IntPtr.Zero;
            NativeMethods.CloseHandle(nul);
            nul = IntPtr.Zero;

            var stream = new FileStream(new SafeFileHandle(readPipe, ownsHandle: true), FileAccess.Read, 4096, isAsync: false);
            readPipe = IntPtr.Zero;
            reader = new PipeReader(stream, collector.Write, $"UnkilledProcess {request.FileName} stdout");
            reader.Start();

            if (!WaitForExit(process, waitLimit))
            {
                // Bez zabijania i bez zamykania potoku: wątek tła czyta dalej i odrzuca dane (finally: Complete + Detach).
                return new ServicingToolRun(null, TimedOut: true, collector.Complete());
            }

            if (!NativeMethods.GetExitCodeProcess(process, out var code))
            {
                throw new OperationException(OperationErrorKind.Unexpected, $"Nie można odczytać kodu wyjścia {request.FileName}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            // Ostatnie dane procesu; gdy potomek dalej trzyma koniec zapisu, resztę opróżni wątek tła.
            reader.WaitForEnd(System32ToolRunner.ReaderLimit);
            return new ServicingToolRun(unchecked((int)code), TimedOut: false, collector.Complete());
        }
        finally
        {
            // Po powrocie nic już nie trafia do wyniku ani do onLine.
            collector.Complete();
            if (reader is not null)
            {
                if (reader.WaitForEnd(TimeSpan.Zero))
                {
                    reader.Dispose();
                }
                else
                {
                    reader.Detach();
                }
            }

            // Zamknięcie uchwytów procesu i wątku nie kończy procesu.
            RedirectedProcess.CloseIfSet(readPipe);
            RedirectedProcess.CloseIfSet(writePipe);
            RedirectedProcess.CloseIfSet(nul);
            RedirectedProcess.CloseIfSet(thread);
            RedirectedProcess.CloseIfSet(process);
        }
    }

    /// <summary>true = proces się zakończył; false = minął limit (proces dalej działa).</summary>
    private static bool WaitForExit(IntPtr process, TimeSpan waitLimit)
    {
        using var handle = new ProcessWaitHandle(process);
        var milliseconds = waitLimit.TotalMilliseconds >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(waitLimit.TotalMilliseconds);
        return handle.WaitOne(milliseconds);
    }

    /// <summary>Uchwyt procesu jako <see cref="WaitHandle"/> bez przejęcia własności (zamyka go <see cref="Execute"/>).</summary>
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(IntPtr handle)
        {
            SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: false);
        }
    }

    /// <summary>
    /// Składa linie z bajtów potoku (wątek czytający) i oddaje je odbiorcy. Po <see cref="Complete"/> spóźnione dane wątku,
    /// którego nie dało się zatrzymać, nie trafiają już ani do wyniku, ani do <c>onLine</c>.
    /// </summary>
    private sealed class LineCollector
    {
        private readonly object _sync = new();
        private readonly List<string> _lines = new();
        private readonly ConsoleLineSplitter _splitter;
        private readonly Action<string>? _onLine;
        private bool _completed;

        public LineCollector(Encoding encoding, Action<string>? onLine)
        {
            _splitter = new ConsoleLineSplitter(encoding);
            _onLine = onLine;
            _splitter.Line += OnSplitterLine;
        }

        public Exception? Failure { get; private set; }

        public void Write(byte[] buffer, int count)
        {
            lock (_sync)
            {
                if (!_completed)
                {
                    _splitter.Write(buffer, count);
                }
            }
        }

        /// <summary>Emituje ostatnią linię bez końca linii i zwraca zebrane linie.</summary>
        public string[] Complete()
        {
            lock (_sync)
            {
                if (!_completed)
                {
                    _splitter.Flush();
                    _completed = true;
                }

                return _lines.ToArray();
            }
        }

        private void OnSplitterLine(string line)
        {
            _lines.Add(line);
            if (_onLine is null || Failure is not null)
            {
                return;
            }

            try
            {
                _onLine(line);
            }
            catch (Exception ex)
            {
                // Błąd odbiorcy nie może zatrzymać czytania potoku (proces pisałby w pełny potok); wraca do wywołującego.
                Failure = ex;
            }
        }
    }
}
