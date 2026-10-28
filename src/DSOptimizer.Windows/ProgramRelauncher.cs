using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Windows;

/// <summary>
/// Ponowne uruchomienie DSOptimizera (np. po zmianie języka): własny plik exe, bez argumentów, przez
/// <see cref="NativeProcessStarter"/> (CreateProcess, <c>bInheritHandles=FALSE</c>). Proces podniesiony dziedziczy token, więc bez UAC.
/// Nie zamyka bieżącej instancji. Testy używają wyłącznie konstruktora wewnętrznego z fałszywym <c>start</c>.
/// </summary>
public sealed class ProgramRelauncher : IProgramRelauncher
{
    private const string Component = "Uruchomienie";

    private readonly ILog _log;
    private readonly Func<string> _exePath;
    private readonly Action<string, string, string> _start;

    public ProgramRelauncher(ILog log)
        : this(log, CurrentExePath, NativeProcessStarter.Start)
    {
    }

    /// <summary><paramref name="start"/> dostaje: plik, argumenty, katalog roboczy; błąd startu = <see cref="Win32Exception"/>.</summary>
    internal ProgramRelauncher(ILog log, Func<string> exePath, Action<string, string, string> start)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
        _start = start ?? throw new ArgumentNullException(nameof(start));
    }

    public void Relaunch()
    {
        try
        {
            var exe = _exePath();
            if (string.IsNullOrWhiteSpace(exe))
            {
                throw new OperationException(OperationErrorKind.Unexpected, L.T("Nie znaleziono pliku programu."));
            }

            var directory = Path.GetDirectoryName(exe) ?? string.Empty;
            _log.Info(Component, "Ponowne uruchomienie programu: " + exe);
            _start(exe, string.Empty, directory);
        }
        catch (Win32Exception ex)
        {
            Fail(ex);
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex);
        }
        catch (ArgumentException ex)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception ex)
    {
        var message = L.F("Nie udało się ponownie uruchomić programu: {0}", ex.Message);
        _log.Warn(Component, "Ponowne uruchomienie nie powiodło się: " + ex.Message);
        throw new OperationException(OperationErrorKind.Unexpected, message, ex);
    }

    private static string CurrentExePath()
    {
        using var process = Process.GetCurrentProcess();
        return process.MainModule?.FileName ?? string.Empty;
    }
}
