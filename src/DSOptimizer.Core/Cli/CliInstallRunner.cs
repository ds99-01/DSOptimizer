using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Cli;

/// <summary>
/// Polecenia instalacji przez winget (spec 5.5): <c>--list-install</c> (pozycje listy ze statusem, źródłem <c>winget</c>/<c>github</c>
/// i stanem winget, kod 0 przy udanym odczycie), <c>--install-apps</c> (seria <see cref="AppInstallEngine"/> bez pytania – z usługami
/// GitHuba <see cref="CliInstallServices.GitHub"/> dla RustDeska – kod z <see cref="CliExitCodes.ForInstall"/>)
/// i <c>--search-apps</c> (wiersze <c>winget search</c>, kod 0 także bez wyników, 1 przy odmowie, braku sieci i błędzie). Inne konto niż
/// zalogowane, niepewny użytkownik i błędny argument kończą się, zanim winget zostanie uruchomiony.
/// </summary>
internal static class CliInstallRunner
{
    private const string Component = "CLI";

    public static CliReport Run(CliOptions options, CliCatalogResult catalog, EngineServices services, CliInstallServices install,
                                DateTimeOffset startedAt, List<string> warnings, CancellationToken cancellationToken)
        => options.Command switch
        {
            CliCommand.ListInstall => List(options, catalog, services, install, startedAt, warnings),
            CliCommand.InstallApps => Install(options, catalog, services, install, startedAt, warnings, cancellationToken),
            CliCommand.SearchApps => Search(options, catalog, services, install, startedAt, warnings),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Command, "To nie jest polecenie instalacji."),
        };

    private static CliReport List(CliOptions options, CliCatalogResult catalog, EngineServices services, CliInstallServices install,
                                  DateTimeOffset startedAt, List<string> warnings)
    {
        var reading = Read(services, install, warnings, out var failure);
        if (reading is null)
        {
            return ReadFailure(options, catalog, services, startedAt, warnings, failure!.Value);
        }

        var winget = WingetAvailability.Check(services.User, install.ProcessSid, reading.Apps, install.Runner, install.AccountName);
        var rows = install.Catalog.Entries
            .Select(entry => new CliInstallRow
            {
                Id = entry.Id,
                Source = CliInstallSource.Of(entry.Origin),
                WingetId = entry.WingetId,
                GitHub = entry.GitHub,
                Title = entry.Title,
                Category = entry.Category,
                Installed = InstallStatus.IsInstalled(entry, reading.Apps),
            })
            .ToList();
        return CliRunner.Build(options, catalog, services, startedAt, CliExitCode.Success, null, warnings,
            install: new CliInstallReportPart(NotReadyText(winget), Info(winget), Rows: rows));
    }

    private static CliReport Install(CliOptions options, CliCatalogResult catalog, EngineServices services, CliInstallServices install,
                                     DateTimeOffset startedAt, List<string> warnings, CancellationToken cancellationToken)
    {
        var error = Resolve(options, install.Catalog, out var targets);
        if (error is not null)
        {
            return CliRunner.Build(options, catalog, services, startedAt, CliExitCode.UsageError, null, warnings, errors: new[] { error });
        }

        var engine = new AppInstallEngine(
            install.Runner, install.Logs, install.Reader, services.Journal, services.User, install.ProcessSid, install.AccountName, services.Log, services.Clock, install.Sleep,
            install.GitHub);
        var batch = engine.Install(InstallOrder.Sort(targets, install.Catalog), new InstallOptions(), cancellationToken);
        var items = batch.Items.Select(ToItem).ToList();
        return CliRunner.Build(options, catalog, services, startedAt, CliExitCodes.ForInstall(batch.Items), null, warnings, batch: batch.Batch,
            install: new CliInstallReportPart(NotReadyText(batch.Winget), Info(batch.Winget), Items: items, RequiresReboot: items.Any(i => i.RequiresReboot)));
    }

    private static CliReport Search(CliOptions options, CliCatalogResult catalog, EngineServices services, CliInstallServices install,
                                    DateTimeOffset startedAt, List<string> warnings)
    {
        var query = options.SearchQuery;
        if (!WingetArguments.TryValidateQuery(query, out var queryError))
        {
            return CliRunner.Build(options, catalog, services, startedAt, CliExitCode.UsageError, null, warnings,
                errors: new[] { L.F("Nieprawidłowe zapytanie --search-apps: {0}", queryError) });
        }

        var reading = Read(services, install, warnings, out var failure);
        if (reading is null)
        {
            return ReadFailure(options, catalog, services, startedAt, warnings, failure!.Value);
        }

        var winget = WingetAvailability.Check(services.User, install.ProcessSid, reading.Apps, install.Runner, install.AccountName);
        if (winget.State != WingetState.Ready)
        {
            return Searched(winget.Text, null);
        }

        WingetRun run;
        try
        {
            run = install.Runner.Run(WingetArguments.Search(query!), InstallTimeouts.Search, null);
        }
        catch (Exception ex)
        {
            services.Log.Fail(Component, L.T("winget search nie wystartował."), ex);
            return Searched(InstallText.SearchStartFailed(ex.Message), null);
        }

        if (run.TimedOut || run.ExitCode is null)
        {
            return Searched(InstallText.SearchTimedOut(InstallTimeouts.Search), null);
        }

        var code = run.ExitCode.Value;
        if (code == 0)
        {
            var rows = WingetSearchParser.Parse(run.Output)
                .Select(row => new CliSearchRow { Name = row.Name, WingetId = row.WingetId, Version = row.Version, Installable = row.Installable })
                .ToList();
            return Searched(rows.Count == 0 ? InstallText.SearchNoResults : null, rows);
        }

        if (WingetExitCodes.Classify(code) == WingetVerdict.NotFound)
        {
            return Searched(InstallText.SearchNoResults, Array.Empty<CliSearchRow>());
        }

        return Searched(WingetExitCodes.IsNetwork(code) ? InstallText.SearchNoNetwork : InstallText.SearchFailed(code), null);

        // Wiersze null = wyszukiwanie się nie odbyło albo nie powiodło (kod 1); pusta lista = brak wyników (kod 0).
        CliReport Searched(string? message, IReadOnlyList<CliSearchRow>? rows)
            => CliRunner.Build(options, catalog, services, startedAt, rows is null ? CliExitCode.OperationFailed : CliExitCode.Success, null, warnings,
                install: new CliInstallReportPart(message, Info(winget), SearchRows: rows));
    }

    /// <summary>Jeden odczyt listy zainstalowanych dla zalogowanego konta; błąd = <c>null</c> i stan winget (odmowa konta wygrywa).</summary>
    private static InstalledAppsReading? Read(EngineServices services, CliInstallServices install, List<string> warnings, out (WingetStatus Status, string Error)? failure)
    {
        try
        {
            var reading = install.Reader.Read(services.User.Sid);
            warnings.AddRange(reading.Warnings);
            failure = null;
            return reading;
        }
        catch (Exception ex)
        {
            services.Log.Fail(Component, L.T("Odczyt listy zainstalowanych aplikacji nie powiódł się."), ex);
            var error = InstallText.ReadFailed(ex.Message);
            var status = WingetAvailability.CheckAccount(services.User, install.ProcessSid, install.AccountName)
                ?? new WingetStatus(WingetState.NotWorking, null, error);
            failure = (status, error);
            return null;
        }
    }

    private static CliReport ReadFailure(CliOptions options, CliCatalogResult catalog, EngineServices services, DateTimeOffset startedAt,
                                         List<string> warnings, (WingetStatus Status, string Error) failure)
        => CliRunner.Build(options, catalog, services, startedAt, CliExitCode.OperationFailed, null, warnings, errors: new[] { failure.Error },
            install: new CliInstallReportPart(failure.Status.Text, Info(failure.Status)));

    /// <summary>
    /// Id z wiersza poleceń → pozycje serii. <c>install.*</c> spoza listy = błąd argumentu (kod 2, decyzja planu). <c>winget:&lt;id&gt;</c>
    /// pozycji z listy bierze wpis listy (z regułą <c>match</c>: zainstalowana inną drogą = „Już zainstalowane” bez winget). Ten sam pakiet
    /// dwa razy (klucz <see cref="InstallTarget.PackageKey"/>: identyfikator winget albo <c>github:&lt;repo&gt;</c>; także jako <c>install.*</c>
    /// i <c>winget:</c>), Id obcięte „…” i alias winget pozycji z GitHuba (<see cref="GitHubSources.WingetAliases"/>, np.
    /// <c>winget:RustDesk.RustDesk</c>) = błąd argumentu. Zwraca komunikat błędu albo <c>null</c>.
    /// </summary>
    private static string? Resolve(CliOptions options, AppsInstallCatalog list, out List<InstallTarget> targets)
    {
        targets = new List<InstallTarget>(options.InstallIds.Count);
        if (options.InstallIds.Count == 0)
        {
            return L.T("Polecenie --install-apps wymaga co najmniej jednego id.");
        }

        var source = options.AppsCatalogPath ?? L.T("wbudowana lista");
        var packageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in options.InstallIds)
        {
            InstallTarget target;
            if (id.StartsWith(InstallTarget.SearchPrefix, StringComparison.Ordinal))
            {
                var wingetId = id.Substring(InstallTarget.SearchPrefix.Length);
                if (!WingetArguments.IsValidWingetId(wingetId))
                {
                    return L.F("Nieprawidłowy identyfikator aplikacji '{0}': po winget: musi być pełny identyfikator pakietu, nie skrócony „…” ani pusty.", id);
                }

                var entry = list.FindByWingetId(wingetId);
                if (entry is null && GitHubSources.WingetAliases.TryGetValue(wingetId, out var listId))
                {
                    // Spec 11.2 (Ruling R5): pakiet winget pozycji z GitHuba to nie druga droga jej instalacji.
                    return InstallText.CliGitHubAlias(list.Find(listId)?.Title ?? wingetId, listId);
                }

                target = entry is null ? InstallTarget.FromSearch(wingetId, wingetId) : InstallTarget.FromEntry(entry);
            }
            else
            {
                var entry = list.Find(id);
                if (entry is null)
                {
                    return L.F("Nie ma pozycji '{0}' na liście aplikacji do instalacji ({1}).", id, source);
                }

                target = InstallTarget.FromEntry(entry);
            }

            if (!packageKeys.Add(target.PackageKey))
            {
                return L.F("Pakiet {0} podany w --install-apps więcej niż raz ('{1}').", target.PackageKey, id);
            }

            targets.Add(target);
        }

        return null;
    }

    private static string? NotReadyText(WingetStatus status) => status.State == WingetState.Ready ? null : status.Text;

    private static CliWingetInfo Info(WingetStatus status) => new() { State = status.State, Version = status.Version, Message = status.Text };

    private static CliInstallItem ToItem(InstallItemReport item) => new()
    {
        Id = item.Id,
        Source = CliInstallSource.Of(item.Origin),
        WingetId = item.WingetId,
        GitHub = item.GitHub,
        Title = item.Title,
        Outcome = item.Outcome,
        WingetCode = item.WingetExitCode is { } code ? WingetExitCodes.Hex(code) : null,
        MsiCode = item.MsiExitCode,
        Message = item.Message,
        LogPath = item.LogPath,
        RequiresReboot = item.RequiresReboot,
        Version = item.Version,
    };
}

/// <summary>Pola raportu poleceń instalacji: komunikat, stan winget i jeden z zestawów wierszy.</summary>
internal sealed record CliInstallReportPart(
    string? Message,
    CliWingetInfo? Winget,
    IReadOnlyList<CliInstallItem>? Items = null,
    IReadOnlyList<CliInstallRow>? Rows = null,
    IReadOnlyList<CliSearchRow>? SearchRows = null,
    bool RequiresReboot = false);
