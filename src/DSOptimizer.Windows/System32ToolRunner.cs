using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace DSOptimizer.Windows;

/// <summary>
/// Uruchamia narzędzie z <c>%SystemRoot%\System32</c> (pełna ścieżka, nigdy PATH ani katalog bieżący) przez <c>CreateProcessW</c>.
/// Stdout i stderr idą do jednego potoku (kolejność jak w konsoli), stdin to NUL. Proces startuje wstrzymany i dopiero po objęciu
/// Job Objectem jest wznawiany, więc timeout/anulowanie zabijają całe drzewo (wzorzec <see cref="PowerShellScriptRunner"/>).
/// <c>bInheritHandles=TRUE</c> tylko z listą dziedziczonych uchwytów (<c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>): koniec zapisu
/// potoku i NUL, żaden inny uchwyt procesu nie trafia do dziecka (<see cref="RedirectedProcess"/>). Wyjście jest dekodowane stroną
/// kodową OEM konsoli (<c>GetOEMCP</c>). Na koniec danych z potoku czekamy najwyżej <see cref="ReaderLimit"/>: koniec zapisu może
/// trzymać proces spoza drzewa (np. odziedziczył uchwyt w wyścigu z <c>Process.Start</c>), więc wtedy czytanie jest przerywane.
/// </summary>
public sealed class System32ToolRunner : IToolRunner
{
    /// <summary>Najdłuższe czekanie na koniec wątku czytającego po zakończeniu narzędzia.</summary>
    internal static readonly TimeSpan ReaderLimit = TimeSpan.FromSeconds(5);

    private const int PollMilliseconds = 100;

    public ToolResult Run(ToolRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationException(OperationErrorKind.Cancelled, L.T("Anulowano przed uruchomieniem narzędzia."));
        }

        var systemDirectory = Environment.SystemDirectory;
        var toolPath = Path.Combine(systemDirectory, request.FileName);
        if (!File.Exists(toolPath))
        {
            throw new OperationException(OperationErrorKind.NotFound, L.F("Nie znaleziono pliku {0}.", toolPath));
        }

        using var job = CreateJob();
        var (exitCode, bytes) = Execute(toolPath, request, systemDirectory, job, cancellationToken);
        return new ToolResult { ExitCode = exitCode, Output = SplitLines(RedirectedProcess.OemEncoding().GetString(bytes)) };
    }

    private static JobObject CreateJob()
    {
        try
        {
            return new JobObject();
        }
        catch (Win32Exception ex)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można utworzyć Job Objectu: {0}", ex.Message), ex);
        }
    }

    private static (int ExitCode, byte[] Output) Execute(
        string toolPath,
        ToolRequest request,
        string workingDirectory,
        JobObject job,
        CancellationToken cancellationToken)
    {
        var (readPipe, writePipe) = RedirectedProcess.CreateOutputPipe();
        var nul = IntPtr.Zero;
        var process = IntPtr.Zero;
        var thread = IntPtr.Zero;
        PipeReader? reader = null;
        try
        {
            nul = RedirectedProcess.OpenNul();
            (process, thread) = RedirectedProcess.Start(toolPath, request, workingDirectory, nul, writePipe, NativeMethods.CreateSuspended);

            // Dziecko ma własne kopie uchwytów; nasze końce zapisu zamykamy, żeby koniec procesów dawał EOF na potoku.
            NativeMethods.CloseHandle(writePipe);
            writePipe = IntPtr.Zero;
            NativeMethods.CloseHandle(nul);
            nul = IntPtr.Zero;

            try
            {
                job.Assign(process);
            }
            catch (Win32Exception ex)
            {
                NativeMethods.TerminateProcess(process, 1);
                throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można objąć {0} Job Objectem: {1}", request.FileName, ex.Message), ex);
            }

            if (NativeMethods.ResumeThread(thread) == uint.MaxValue)
            {
                var error = Marshal.GetLastWin32Error();
                job.Terminate();
                throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można wznowić {0}: {1}", request.FileName, new Win32Exception(error).Message));
            }

            var stream = new FileStream(new SafeFileHandle(readPipe, ownsHandle: true), FileAccess.Read, 4096, isAsync: false);
            readPipe = IntPtr.Zero;
            var buffer = new MemoryStream();
            reader = new PipeReader(
                stream,
                (chunk, count) =>
                {
                    lock (buffer)
                    {
                        buffer.Write(chunk, 0, count);
                    }
                },
                "ToolRunner stdout");
            reader.Start();

            WaitForExit(process, job, request, cancellationToken);

            // Kod wyjścia czytamy przed zabiciem drzewa; potomkowie z uchwytem potoku mogliby blokować EOF, więc job zabija resztę.
            if (!NativeMethods.GetExitCodeProcess(process, out var code))
            {
                throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można odczytać kodu wyjścia {0}.", request.FileName));
            }

            job.Terminate();
            if (!reader.WaitForEnd(ReaderLimit))
            {
                reader.Stop(ReaderLimit);
            }

            lock (buffer)
            {
                return (unchecked((int)code), buffer.ToArray());
            }
        }
        finally
        {
            // Zamknięcie Job Objectu (KILL_ON_JOB_CLOSE) i tak zabiłoby drzewo; Terminate przed czekaniem na czytnik odblokowuje EOF.
            job.Terminate();
            if (reader is not null)
            {
                if (!reader.WaitForEnd(TimeSpan.Zero))
                {
                    reader.Stop(ReaderLimit);
                }

                reader.Dispose();
            }

            RedirectedProcess.CloseIfSet(readPipe);
            RedirectedProcess.CloseIfSet(writePipe);
            RedirectedProcess.CloseIfSet(nul);
            RedirectedProcess.CloseIfSet(thread);
            RedirectedProcess.CloseIfSet(process);
        }
    }

    private static void WaitForExit(IntPtr process, JobObject job, ToolRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var wait = NativeMethods.WaitForSingleObject(process, PollMilliseconds);
            if (wait == NativeMethods.WaitObject0)
            {
                return;
            }

            if (wait != NativeMethods.WaitTimeout)
            {
                var error = Marshal.GetLastWin32Error();
                job.Terminate();
                throw new OperationException(OperationErrorKind.Unexpected, L.F("Błąd oczekiwania na {0}: {1}", request.FileName, new Win32Exception(error).Message));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Kill(process, job);
                throw new OperationException(OperationErrorKind.Cancelled, L.F("Narzędzie {0} anulowane.", request.FileName));
            }

            if (stopwatch.Elapsed >= request.Timeout)
            {
                Kill(process, job);
                throw new OperationException(
                    OperationErrorKind.Timeout,
                    L.F("Narzędzie {0} przekroczyło limit {1:0.#} s i zostało zatrzymane.", request.FileName, request.Timeout.TotalSeconds));
            }
        }
    }

    private static void Kill(IntPtr process, JobObject job)
    {
        job.Terminate();
        _ = NativeMethods.WaitForSingleObject(process, 5000);
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        using var lineReader = new StringReader(text);
        string? line;
        while ((line = lineReader.ReadLine()) is not null)
        {
            lines.Add(line);
        }

        return lines;
    }
}
