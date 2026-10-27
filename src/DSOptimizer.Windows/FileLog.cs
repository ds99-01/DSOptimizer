using System;
using System.Globalization;
using System.IO;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Storage;

namespace DSOptimizer.Windows;

/// <summary>
/// Log do pliku dziennego w <see cref="SecureRoot.LogsDirectory"/> (domyślnie %ProgramData%\DSOptimizer\logs) + lustrzana kopia obok exe, ale tylko gdy exe nie leży na dysku systemowym (np. pendrive; zob. <see cref="LogMirrorPolicy"/>).
/// Awaria kopii lustrzanej wyłącza ją, ale nie zatrzymuje logowania. Wyjątki są zapisywane z pełnym stack trace.
/// </summary>
public sealed class FileLog : ILog
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly SecureRoot _root;
    private readonly IClock _clock;
    private string? _mirrorDirectory;
    private bool _primaryReady;

    public FileLog(SecureRoot root, string? mirrorDirectory, IClock clock)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _mirrorDirectory = mirrorDirectory;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Kopia lustrzana obok exe (<c>logs\NAZWA_KOMPUTERA</c>) tylko gdy exe nie leży na dysku systemowym; inaczej <c>null</c>.
    /// </summary>
    public static string? DefaultMirrorDirectory => LogMirrorPolicy.MirrorDirectory(
        AppDomain.CurrentDomain.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.MachineName);

    public void Info(string component, string message) => Write("INFO", component, message, null);

    public void Warn(string component, string message) => Write("WARN", component, message, null);

    public void Fail(string component, string message, Exception? exception = null) => Write("ERROR", component, message, exception);

    private void Write(string level, string component, string message, Exception? exception)
    {
        var now = _clock.UtcNow.ToLocalTime();
        var line = string.Format(CultureInfo.InvariantCulture, "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [{2}] {3}", now, level, component, message);
        if (exception is not null)
        {
            // Exception.ToString() zawiera typ, message, inner exceptions i stack trace.
            line += Environment.NewLine + exception;
        }

        var fileName = string.Format(CultureInfo.InvariantCulture, "dsoptimizer-{0:yyyyMMdd}.log", now);
        lock (_gate)
        {
            if (!_primaryReady)
            {
                _primaryReady = TryRun(() => _root.EnsureLeaf(_root.LogsDirectory));
            }

            // Niezabezpieczony primary może kontrolować nie-admin - nie dopisujemy do niego (ponowna próba przy kolejnej linii).
            if (_primaryReady)
            {
                TryRun(() => Append(_root.LogsDirectory, fileName, line));
            }

            if (_mirrorDirectory is { } mirror
                && !TryRun(() =>
                {
                    Directory.CreateDirectory(mirror);
                    Append(mirror, fileName, line);
                }))
            {
                _mirrorDirectory = null;
            }
        }
    }

    private static void Append(string directory, string fileName, string line)
        => File.AppendAllText(Path.Combine(directory, fileName), line + Environment.NewLine, Utf8NoBom);

    private static bool TryRun(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception)
        {
            // Kontrakt ILog: log nigdy nie rzuca (silnik loguje w catch/rollbacku). Granica logu - łapiemy wszystko.
            return false;
        }
    }
}
