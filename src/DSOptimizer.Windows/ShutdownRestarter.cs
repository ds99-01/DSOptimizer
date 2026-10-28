using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Windows;

/// <summary>
/// Restart komputera przez <c>%SystemRoot%\System32\shutdown.exe /r /t 0</c> (CreateProcess, nie ShellExecute).
/// Narzędzie samo włącza SeShutdownPrivilege. Przy czasie 0 nie ma /f (MS Learn „shutdown”: /f jest domyślne tylko
/// przy czasie &gt; 0), więc programy użytkownika mogą jeszcze zablokować zamknięcie. Kod 0 = restart zlecony.
/// Ślad w pliku logu (audyt na wizycie u klienta): Info przed startem procesu i Info z przyczyną przy każdym błędzie.
/// Poziom Info celowo: w GUI log to TeeLog (plik + panel), a panel pokazuje Warn/Fail – błąd w panelu dopisuje już MainViewModel
/// (jedna linia Error), więc Warn dałby tam drugą linię. Info trafia wyłącznie do pliku.
/// Testy używają wyłącznie konstruktora wewnętrznego z fałszywym <c>run</c> – nigdy nie restartują hosta.
/// </summary>
public sealed class ShutdownRestarter : IRestarter
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private const string Component = "Restart";

    private readonly ILog _log;
    private readonly Func<ProcessStartInfo, TimeSpan, int?> _run;

    public ShutdownRestarter(ILog log)
        : this(log, RunProcess)
    {
    }

    /// <summary>
    /// <paramref name="run"/> startuje proces, czeka najwyżej podany czas i zwraca kod wyjścia albo null przy przekroczeniu czasu;
    /// błąd startu rzuca <see cref="Win32Exception"/>.
    /// </summary>
    internal ShutdownRestarter(ILog log, Func<ProcessStartInfo, TimeSpan, int?> run)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _run = run ?? throw new ArgumentNullException(nameof(run));
    }

    public void Restart()
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
            Arguments = "/r /t 0",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory,
        };

        _log.Info(Component, L.T("shutdown.exe /r /t 0 zlecony przez użytkownika."));
        int? exitCode;
        try
        {
            exitCode = _run(info, Limit);
        }
        catch (Win32Exception ex)
        {
            throw Failure(OperationErrorKind.Unexpected, L.F("Nie udało się uruchomić ponownie komputera: {0}", ex.Message), ex);
        }

        if (exitCode is null)
        {
            throw Failure(OperationErrorKind.Timeout, L.T("Nie udało się uruchomić ponownie komputera: shutdown.exe nie zakończył się w ciągu 10 s."));
        }

        if (exitCode != 0)
        {
            throw Failure(OperationErrorKind.Unexpected, L.F("Nie udało się uruchomić ponownie komputera: shutdown.exe zakończył się kodem {0}.", exitCode));
        }
    }

    /// <summary>Przyczyna do pliku logu, potem wyjątek z tym samym komunikatem (panel dostaje go od MainViewModel).</summary>
    private OperationException Failure(OperationErrorKind kind, string message, Exception? inner = null)
    {
        _log.Info(Component, message);
        return new OperationException(kind, message, inner);
    }

    private static int? RunProcess(ProcessStartInfo info, TimeSpan timeout)
    {
        using var process = Process.Start(info) ?? throw new Win32Exception(L.T("Nie udało się uruchomić shutdown.exe."));
        return process.WaitForExit((int)timeout.TotalMilliseconds) ? process.ExitCode : null;
    }
}
