using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Cli;

/// <summary>
/// Wykonuje detect/apply/undo/execute jednego tweaka na silniku, <c>--list-apps</c>/<c>--remove-app</c> (usługi
/// <see cref="CliAppServices"/>) albo polecenia instalacji przez winget (<see cref="CliInstallRunner"/>, usługi
/// <see cref="CliInstallServices"/>) i zwraca raport CLI.
/// </summary>
public static class CliRunner
{
    public static CliReport Run(CliOptions options, CliCatalogResult catalog, EngineServices services,
                                IReadOnlyList<string> startupWarnings, CancellationToken cancellationToken = default)
        => Run(options, catalog, services, startupWarnings, null, cancellationToken);

    public static CliReport Run(CliOptions options, CliCatalogResult catalog, EngineServices services,
                                IReadOnlyList<string> startupWarnings, CliAppServices? apps, CancellationToken cancellationToken = default)
        => Run(options, catalog, services, startupWarnings, apps, null, cancellationToken);

    public static CliReport Run(CliOptions options, CliCatalogResult catalog, EngineServices services,
                                IReadOnlyList<string> startupWarnings, CliAppServices? apps, CliInstallServices? install,
                                CancellationToken cancellationToken = default)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (catalog.Catalog is null)
        {
            throw new ArgumentException("Katalog zawiera błędy; host nie wywołuje Run w takim przypadku.", nameof(catalog));
        }

        var loaded = catalog.Catalog;
        var startedAt = services.Clock.UtcNow;
        var warnings = new List<string>(startupWarnings ?? Array.Empty<string>());
        if (!string.Equals(catalog.Source, CliCatalog.BuiltInSource, StringComparison.Ordinal))
        {
            warnings.Add(L.F("Użyto zewnętrznego katalogu: {0} ({1}).", catalog.Source, loaded.Hash));
        }

        if (options.Command is CliCommand.ListApps or CliCommand.RemoveApp)
        {
            if (apps is null)
            {
                throw new ArgumentNullException(nameof(apps), "Polecenia aplikacji wymagają usług aplikacji.");
            }

            return RunApps(options, catalog, services, apps, startedAt, warnings, cancellationToken);
        }

        if (CliParser.IsInstallCommand(options.Command))
        {
            if (install is null)
            {
                throw new ArgumentNullException(nameof(install), "Polecenia instalacji wymagają usług instalacji.");
            }

            return CliInstallRunner.Run(options, catalog, services, install, startedAt, warnings, cancellationToken);
        }

        var tweak = Find(loaded.Definitions, options.TweakId);
        if (tweak is null)
        {
            return Build(options, catalog, services, startedAt, CliExitCode.CatalogError, options.TweakId, warnings,
                errors: new[] { L.F("Nieznany tweak '{0}' w katalogu {1}.", options.TweakId, catalog.Source) });
        }

        var kind = tweak.Kind.ToString().ToLowerInvariant();
        if (options.Command == CliCommand.Execute && tweak.Kind == TweakKind.Tweak)
        {
            return Build(options, catalog, services, startedAt, CliExitCode.UsageError, tweak.Id, warnings,
                errors: new[] { L.F("Polecenie execute dotyczy tylko akcji i paneli ('{0}' ma kind={1}).", tweak.Id, kind) });
        }

        if (options.Command == CliCommand.Apply && tweak.Kind != TweakKind.Tweak)
        {
            return Build(options, catalog, services, startedAt, CliExitCode.UsageError, tweak.Id, warnings,
                errors: new[] { L.F("Polecenie apply dotyczy tylko kind=tweak ('{0}' ma kind={1}) – użyj --execute.", tweak.Id, kind) });
        }

        if (options.Command is CliCommand.Detect or CliCommand.Undo && tweak.Kind != TweakKind.Tweak)
        {
            return Build(options, catalog, services, startedAt, CliExitCode.UsageError, tweak.Id, warnings,
                errors: new[] { L.F("Polecenie {0} dotyczy tylko pozycji kind=tweak ('{1}' ma kind={2}).", options.Command.ToString().ToLowerInvariant(), tweak.Id, kind) });
        }

        var engine = new TweakEngine(services, loaded.Hash);
        switch (options.Command)
        {
            case CliCommand.Detect:
                return Build(options, catalog, services, startedAt, CliExitCode.Success, tweak.Id, warnings,
                    state: engine.Detect(tweak, cancellationToken));
            case CliCommand.Apply:
                return RunApply(options, catalog, services, startedAt, tweak, engine, warnings, cancellationToken);
            case CliCommand.Undo:
                var undo = engine.Undo(tweak, null, cancellationToken);
                return Build(options, catalog, services, startedAt, CliExitCodes.FromOutcome(undo.Outcome), tweak.Id, warnings,
                    result: undo);
            case CliCommand.Execute:
                var executed = engine.Execute(tweak, null, cancellationToken);
                return Build(options, catalog, services, startedAt, CliExitCodes.FromOutcome(executed.Outcome), tweak.Id, warnings,
                    result: executed);
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Command, "Nieobsługiwane polecenie.");
        }
    }

    public static CliReport Failure(CliExitCode code, CliOptions? options, IReadOnlyList<string> errors,
                                    IReadOnlyList<string> warnings, DateTimeOffset startedAt, DateTimeOffset finishedAt)
        => new()
        {
            Command = options?.Command,
            TweakId = options?.TweakId,
            Result = code,
            ExitCode = (int)code,
            Errors = errors ?? Array.Empty<string>(),
            Warnings = warnings ?? Array.Empty<string>(),
            StartedAt = startedAt,
            FinishedAt = finishedAt,
        };

    private static CliReport RunApply(CliOptions options, CliCatalogResult catalog, EngineServices services, DateTimeOffset startedAt,
                                      TweakDefinition tweak, TweakEngine engine, List<string> warnings, CancellationToken cancellationToken)
    {
        var batch = engine.ApplyBatch(
            new[] { tweak },
            new BatchOptions
            {
                RestorePointPolicy = options.RestorePointPolicy,
                RestorePointDescription = L.F("DSOptimizer – przed {0}", tweak.Id),
            },
            null,
            cancellationToken);
        var result = batch.Results.Single();
        var code = batch.Aborted ? CliExitCode.OperationFailed : CliExitCodes.FromOutcome(result.Outcome);
        return Build(options, catalog, services, startedAt, code, tweak.Id, warnings, result: result, batch: batch);
    }

    /// <summary>
    /// Lista: odczyt dla zalogowanego konta i klasyfikacja (widoczne wiersze). Usunięcie: ten sam odczyt, wiersz po Id; wiersz
    /// chroniony albo zablokowany = <see cref="TweakOutcome.Refused"/> (kod 1) zanim cokolwiek się uruchomi, inaczej
    /// <see cref="AppRemovalEngine"/> z odpowiedzią na „Kontynuować bez punktu przywracania?” wynikającą z <c>--restore-point</c> (R15; <c>required</c> wymaga punktu także dla samych AppX, R20).
    /// </summary>
    private static CliReport RunApps(CliOptions options, CliCatalogResult catalog, EngineServices services, CliAppServices apps,
                                     DateTimeOffset startedAt, List<string> warnings, CancellationToken cancellationToken)
    {
        var reading = apps.Reader.Read(services.User.Sid);
        warnings.AddRange(reading.Warnings);
        var rows = apps.Classify?.Invoke(reading) ?? AppClassifier.Classify(
            reading.Apps, apps.Catalog, services.User.Sid, services.User.IsCertain, apps.ProcessSid, apps.AccountName, apps.FileExists, apps.System32);
        if (options.Command == CliCommand.ListApps)
        {
            return Build(options, catalog, services, startedAt, CliExitCode.Success, null, warnings, apps: rows.Select(ToInfo).ToList());
        }

        var row = rows.FirstOrDefault(r => string.Equals(r.App.Id, options.TweakId, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return Build(options, catalog, services, startedAt, CliExitCode.OperationFailed, options.TweakId, warnings,
                errors: new[] { L.F("Nie ma aplikacji '{0}' na liście aplikacji (nie jest zainstalowana albo jest ukryta).", options.TweakId) });
        }

        // Ta sama reguła co pole wyboru na stronie i strażnik silnika (AppRow.CanRemove).
        if (!row.CanRemove)
        {
            var refused = new TweakResult
            {
                TweakId = row.App.Id,
                Outcome = TweakOutcome.Refused,
                Message = string.IsNullOrWhiteSpace(row.BlockedReason)
                    ? L.F("Tej aplikacji nie wolno usunąć ({0}, grupa {1}).", row.Removability, row.Group)
                    : L.F("Tej aplikacji nie wolno usunąć ({0}): {1}", row.Removability, row.BlockedReason),
            };
            return Build(options, catalog, services, startedAt, CliExitCodes.FromOutcome(refused.Outcome), row.App.Id, warnings, result: refused);
        }

        var policy = options.RestorePointPolicy;
        var engine = new AppRemovalEngine(
            apps.Reader, apps.Remover, services.Journal, services.RestorePoints, services.User, apps.ProcessSid, services.Log, services.Clock, apps.Sleep);
        var batch = engine.Remove(
            new[] { row },
            new AppRemovalOptions
            {
                RestorePointSetting = policy != RestorePointPolicy.Skip,
                // R20: `required` znaczy to samo co przy --apply – także seria samych AppX bez punktu nic nie usuwa.
                RestorePointRequired = policy == RestorePointPolicy.Required,
                ContinueWithoutRestorePoint = _ => policy != RestorePointPolicy.Required,
            },
            cancellationToken);
        var result = batch.Results.Single();
        var code = batch.Aborted ? CliExitCode.OperationFailed : CliExitCodes.FromOutcome(result.Outcome);
        return Build(options, catalog, services, startedAt, code, row.App.Id, warnings, result: result, batch: batch);
    }

    private static CliAppInfo ToInfo(AppRow row) => new()
    {
        Id = row.App.Id,
        Kind = row.App.Kind,
        Name = row.App.Name,
        Publisher = row.App.Publisher,
        Group = row.Group,
        Removability = row.Removability,
    };

    private static TweakDefinition? Find(IReadOnlyList<TweakDefinition> definitions, string id)
        => definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? definitions.FirstOrDefault(d => d.Aliases.Any(alias => string.Equals(alias, id, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Raport z polami wspólnymi; <paramref name="install"/> = pola poleceń instalacji (komunikat, stan winget, wiersze).</summary>
    internal static CliReport Build(CliOptions options, CliCatalogResult catalog, EngineServices services, DateTimeOffset startedAt,
                                    CliExitCode code, string? tweakId, List<string> warnings, DetectionState? state = null,
                                    TweakResult? result = null, BatchResult? batch = null, IReadOnlyList<string>? errors = null,
                                    IReadOnlyList<CliAppInfo>? apps = null, CliInstallReportPart? install = null)
    {
        var all = new List<string>(warnings);
        if (batch is not null)
        {
            all.AddRange(batch.Warnings);
        }

        if (result is not null)
        {
            all.AddRange(result.Warnings);
        }

        var restorePoint = batch?.RestorePoint;
        return new CliReport
        {
            Command = options.Command,
            TweakId = tweakId,
            Result = code,
            ExitCode = (int)code,
            State = state,
            Outcome = result?.Outcome,
            FinalState = result?.FinalState,
            Message = install is not null
                ? install.Message
                : batch?.Aborted == true ? batch.AbortReason ?? result?.Message : result?.Message,
            Warnings = all.Distinct(StringComparer.Ordinal).ToList(),
            NotRestored = result?.NotRestored ?? Array.Empty<string>(),
            Errors = errors ?? Array.Empty<string>(),
            RequiresReboot = install?.RequiresReboot ?? result?.RequiresReboot ?? false,
            Aborted = batch?.Aborted ?? false,
            RestorePoint = restorePoint is null
                ? null
                : new CliRestorePointInfo { Status = restorePoint.Status, Code = restorePoint.Code, Message = restorePoint.Message },
            Catalog = new CliCatalogInfo { Source = catalog.Source, Hash = catalog.Catalog!.Hash },
            Machine = new CliMachineInfo
            {
                Build = services.SystemInfo.Build,
                Revision = services.SystemInfo.Revision,
                Edition = services.SystemInfo.Edition,
                MachineName = services.SystemInfo.MachineName,
            },
            User = new CliUserInfo { Sid = services.User.Sid, IsCertain = services.User.IsCertain },
            Apps = apps,
            Winget = install?.Winget,
            InstallItems = install?.Items,
            InstallRows = install?.Rows,
            SearchRows = install?.SearchRows,
            StartedAt = startedAt,
            FinishedAt = services.Clock.UtcNow,
        };
    }
}
