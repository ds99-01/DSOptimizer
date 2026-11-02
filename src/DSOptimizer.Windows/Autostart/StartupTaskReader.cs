using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Windows.Apps;

namespace DSOptimizer.Windows.Autostart;

/// <summary>
/// Zadania startowe aplikacji ze Sklepu konta zalogowanego: <see cref="StartupTaskScript"/> przez <see cref="IScriptRunner"/> (limit 60 s),
/// nazwy i wydawcy rozwijani jak na stronie „Aplikacje” (<see cref="AppxPackages.ResolveDisplayName"/>,
/// <see cref="AppxPackages.ResolvePublisherName"/>). Kod wyjścia ≠ 0, brak wyniku albo zły JSON = <see cref="OperationException"/>;
/// pakiet pominięty przez skrypt = ostrzeżenie odczytu.
/// </summary>
public sealed class StartupTaskReader : IStartupTaskReader
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(60);

    private readonly IScriptRunner _runner;
    private readonly Func<string, string?> _loadIndirect;

    public StartupTaskReader(IScriptRunner runner, Func<string, string?>? loadIndirect = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _loadIndirect = loadIndirect ?? IndirectStringLoader.TryLoad;
    }

    public StartupTaskReading Read(string userSid)
    {
        if (string.IsNullOrEmpty(userSid))
        {
            throw new ArgumentException("SID", nameof(userSid));
        }

        var result = _runner.Run(
            new ScriptRequest { Body = StartupTaskScript.Body, UserSid = userSid, Timeout = ScriptTimeout },
            null,
            CancellationToken.None);
        if (result.ExitCode != 0)
        {
            var reason = string.Join(" ", result.Errors.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()));
            throw new OperationException(
                OperationErrorKind.ScriptFailed,
                reason.Length > 0 ? reason : L.F("Skrypt odczytu zadań startowych zakończył się kodem {0}.", result.ExitCode));
        }

        var line = result.LastLine
            ?? throw new OperationException(OperationErrorKind.ScriptFailed, L.T("Skrypt odczytu zadań startowych nie zwrócił wyniku."));
        var skipped = new List<SkippedStartupPackage>();
        IReadOnlyList<StartupTaskManifest> raw;
        try
        {
            raw = StartupTaskScript.Parse(line, skipped);
        }
        catch (FormatException ex)
        {
            throw new OperationException(OperationErrorKind.ScriptFailed, ex.Message, ex);
        }

        var tasks = raw
            .Select(task => task with
            {
                DisplayName = AppxPackages.ResolveDisplayName(task.PackageName, task.PackageFullName, task.DisplayName, _loadIndirect),
                PublisherDisplayName = AppxPackages.ResolvePublisherName(task.PackageName, task.PackageFullName, task.PublisherDisplayName, _loadIndirect),
            })
            .ToArray();
        var warnings = skipped
            .Select(skip => L.F("Pominięto pakiet {0} przy odczycie zadań startowych: {1}", skip.PackageFullName, skip.Reason))
            .ToArray();
        return new StartupTaskReading(tasks, warnings);
    }
}
