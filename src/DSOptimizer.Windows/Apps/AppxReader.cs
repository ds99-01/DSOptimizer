using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Windows.Apps;

/// <summary>
/// Odczyt pakietów AppX konta zalogowanego przez <see cref="AppxScript"/> uruchamiany z <see cref="IScriptRunner"/> (jak skrypty katalogu).
/// Błędy skryptu i złego JSON-a to wyjątki: <see cref="OperationException"/>, <see cref="InvalidOperationException"/>, <see cref="FormatException"/>.
/// </summary>
public sealed class AppxReader
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(60);

    private readonly IScriptRunner _runner;
    private readonly Func<string, string?> _loadIndirect;

    public AppxReader(IScriptRunner runner, Func<string, string?>? loadIndirect = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _loadIndirect = loadIndirect ?? IndirectStringLoader.TryLoad;
    }

    /// <summary>Wszystkie pakiety konta <paramref name="userSid"/> z nazwami wyświetlanymi i wydawcami rozwiniętymi z manifestu.</summary>
    public IReadOnlyList<InstalledApp> Read(string userSid)
        => AppxPackages.Parse(
            Run(AppxScript.Body, userSid, string.Empty),
            userSid,
            (record, raw) => AppxPackages.ResolveDisplayName(record.Name, record.PackageFullName, raw, _loadIndirect),
            (record, raw) => AppxPackages.ResolvePublisherName(record.Name, record.PackageFullName, raw, _loadIndirect));

    /// <summary>
    /// Świeży odczyt jednego pakietu po <c>Name</c> (<see cref="AppxScript.PresenceBody"/>): konto zalogowane albo wszystkie konta
    /// i ewentualnie obraz systemu, według <paramref name="scope"/>. Gdy odczytu nie da się wykonać (limit czasu, błąd skryptu,
    /// zły wynik): <see cref="AppPresence.Unknown"/> – brak dowodu zniknięcia pakietu to nie „usunięty”.
    /// </summary>
    public AppPresence ReadPresence(string packageName, string userSid, AppPresenceScope scope)
    {
        try
        {
            return AppxScript.ParsePresence(Run(AppxScript.PresenceBody, userSid, AppxScript.PresenceRequest(packageName, scope)));
        }
        catch (Exception ex) when (ex is OperationException or InvalidOperationException or FormatException)
        {
            return AppPresence.Unknown;
        }
    }

    private string Run(string body, string userSid, string request)
    {
        var result = _runner.Run(
            new ScriptRequest { Body = body, UserSid = userSid, Snapshot = request, Timeout = ScriptTimeout },
            null,
            CancellationToken.None);
        if (result.ExitCode != 0)
        {
            var reason = string.Join(" ", result.Errors.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()));
            throw new InvalidOperationException(reason.Length > 0 ? reason : L.F("Skrypt odczytu AppX zakończył się kodem {0}.", result.ExitCode));
        }

        return result.LastLine ?? throw new InvalidOperationException(L.T("Skrypt odczytu AppX nie zwrócił wyniku."));
    }
}
