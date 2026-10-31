using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Scripts;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows;

/// <summary>
/// Uruchamia skrypt w powershell.exe 5.1 (64-bit) z pliku w katalogu z ACL tylko dla adminów.
/// Parametry idą przez zmienne środowiskowe, cały proces z dziećmi siedzi w Job Object (timeout/anulowanie zabija drzewo).
/// Plik skryptu jest tworzony z ACL tylko Administratorzy/SYSTEM i trzymany otwarty (share Read) przez cały czas
/// działania procesu, więc nie da się go podmienić ani usunąć.
/// </summary>
public sealed class PowerShellScriptRunner : IScriptRunner
{
    private const string Component = "PowerShell";
    private const int PollMilliseconds = 100;

    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly SecureRoot _root;
    private readonly string _runDirectory;
    private readonly ILog _log;

    public PowerShellScriptRunner(SecureRoot root, ILog log)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _runDirectory = root.RunDirectory;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public static string PowerShellPath => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    public ScriptResult Run(ScriptRequest request, IProgress<string>? output, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationException(OperationErrorKind.Cancelled, L.T("Anulowano przed uruchomieniem skryptu."));
        }

        var scriptPath = Path.Combine(_runDirectory, Guid.NewGuid().ToString("N") + ".ps1");
        FileStream scriptStream;
        try
        {
            _root.EnsureLeaf(_runDirectory);
            scriptStream = CreateScriptFile(scriptPath, ScriptWrapper.Wrap(request.Body));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można zapisać skryptu: {0}", ex.Message), ex);
        }

        try
        {
            return Execute(scriptPath, request, output, cancellationToken);
        }
        finally
        {
            scriptStream.Dispose();
            TryDelete(scriptPath);
        }
    }

    /// <summary>
    /// Tworzy plik z zaufanym ACL, zapisuje treść, zamyka uchwyt zapisu i otwiera plik ponownie tylko do odczytu
    /// (share Read, bez Write/Delete) — powershell.exe nie otworzy pliku, który ma otwarty uchwyt z prawem zapisu.
    /// Po ponownym otwarciu treść jest weryfikowana bajt po bajcie.
    /// </summary>
    private static FileStream CreateScriptFile(string scriptPath, string content)
    {
        var preamble = Utf8WithBom.GetPreamble();
        var body = Utf8WithBom.GetBytes(content);
        var expected = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, expected, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, expected, preamble.Length, body.Length);

        using (var writer = new FileStream(
            scriptPath,
            FileMode.CreateNew,
            FileSystemRights.Write,
            FileShare.Read,
            4096,
            FileOptions.None,
            SecureDirectory.CreateTrustedFileSecurity()))
        {
            writer.Write(expected, 0, expected.Length);
            writer.Flush();
        }

        var holder = new FileStream(scriptPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var actual = new byte[expected.Length + 1];
            var read = 0;
            int chunk;
            while (read < actual.Length && (chunk = holder.Read(actual, read, actual.Length - read)) > 0)
            {
                read += chunk;
            }

            if (read != expected.Length || !PrefixEquals(actual, expected))
            {
                throw new IOException(L.T("Zawartość pliku skryptu zmieniła się przed uruchomieniem."));
            }

            return holder;
        }
        catch
        {
            holder.Dispose();
            throw;
        }
    }
    private static bool PrefixEquals(byte[] actual, byte[] expected)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }

    private static ScriptResult Execute(string scriptPath, ScriptRequest request, IProgress<string>? output, CancellationToken cancellationToken)
    {
        var gate = new object();
        var stdout = new List<string>();
        var stderr = new List<string>();
        var startInfo = new ProcessStartInfo(PowerShellPath, $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };
        startInfo.EnvironmentVariables[ScriptWrapper.UserSidVariable] = request.UserSid;
        startInfo.EnvironmentVariables[ScriptWrapper.SnapshotVariable] = request.Snapshot;
        startInfo.EnvironmentVariables[PowerShellEnvironment.ModulePathVariable] = PowerShellEnvironment.ModulePath;

        using var job = new JobObject();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (gate)
            {
                stdout.Add(e.Data);
            }

            output?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (gate)
            {
                stderr.Add(e.Data);
            }

            output?.Report("[stderr] " + e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można uruchomić powershell.exe: {0}", ex.Message), ex);
        }

        try
        {
            job.Assign(process);
        }
        catch (Win32Exception ex)
        {
            process.Kill();
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można objąć procesu PowerShell Job Objectem: {0}", ex.Message), ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var stopwatch = Stopwatch.StartNew();
        while (!process.WaitForExit(PollMilliseconds))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                job.Terminate();
                process.WaitForExit();
                throw new OperationException(OperationErrorKind.Cancelled, L.T("Skrypt anulowany."));
            }

            if (request.Timeout is { } limit && stopwatch.Elapsed > limit)
            {
                job.Terminate();
                process.WaitForExit();
                throw new OperationException(
                    OperationErrorKind.Timeout,
                    L.F("Skrypt przekroczył limit {0:0} s i został zatrzymany.", limit.TotalSeconds));
            }
        }

        // Główny proces już wyszedł; potomkowie z odziedziczonymi uchwytami stdout/stderr mogą żyć dalej i blokować EOF
        // potoków. Kod wyjścia czytamy przed Terminate, a job zabija resztę drzewa, zanim zaczekamy na czytniki.
        var exitCode = process.ExitCode;
        job.Terminate();

        // Bezparametrowe WaitForExit czeka też na opróżnienie asynchronicznych czytników stdout/stderr.
        process.WaitForExit();
        lock (gate)
        {
            return new ScriptResult { ExitCode = exitCode, Output = stdout.ToArray(), Errors = stderr.ToArray() };
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _log.Warn(Component, L.F("Nie udało się usunąć pliku skryptu {0}: {1}", path, ex.Message));
        }
    }
}
