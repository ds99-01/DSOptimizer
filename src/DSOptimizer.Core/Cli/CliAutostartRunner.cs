using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Cli;

/// <summary>
/// Polecenia autostartu: <c>--list-autostart</c> (wiersze listy, kod 0, ostrzeżenia odczytu w raporcie), <c>--set-autostart</c> (wiersz po id
/// bez wielkości liter; brak = kod 1 z błędem, wiersz bez <see cref="AutostartRow.CanToggle"/> = <see cref="TweakOutcome.Refused"/> bez
/// zapisu, inaczej <see cref="AutostartEngine.Set"/>) i <c>--undo-autostart</c> (<see cref="AutostartEngine.UndoActive"/>). Konto
/// z zalogowanego użytkownika (<see cref="AutostartContext.Create"/>), nie z procesu.
/// </summary>
internal static class CliAutostartRunner
{
    public static CliReport Run(CliOptions options, CliCatalogResult catalog, EngineServices services, CliAutostartServices autostart,
                                DateTimeOffset startedAt, List<string> warnings, CancellationToken cancellationToken)
    {
        var context = autostart.Context ?? AutostartContext.Create(services.User, autostart.ProcessSid, services.Registry);
        var engine = new AutostartEngine(services.Registry, services.Journal, services.Log, services.Clock);
        if (options.Command == CliCommand.UndoAutostart)
        {
            // Cofnięcie nie potrzebuje listy: pozycja mogła z niej zniknąć, a wpis w Historii zmian nadal da się cofnąć.
            var undo = engine.UndoActive(options.TweakId, context);
            return CliRunner.Build(options, catalog, services, startedAt, CliExitCodes.FromOutcome(undo.Outcome), undo.TweakId, warnings, result: undo);
        }

        var list = AutostartList.Load(autostart.Reader, autostart.Catalog, context);
        warnings.AddRange(list.Warnings);
        switch (options.Command)
        {
            case CliCommand.ListAutostart:
                return CliRunner.Build(options, catalog, services, startedAt, CliExitCode.Success, null, warnings,
                    autostartRows: list.Rows.Select(CliAutostartRow.From).ToList());
            case CliCommand.SetAutostart:
                return Set(options, catalog, services, startedAt, warnings, list, engine, context, cancellationToken);
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Command, "To nie jest polecenie autostartu.");
        }
    }

    private static CliReport Set(CliOptions options, CliCatalogResult catalog, EngineServices services, DateTimeOffset startedAt, List<string> warnings,
                                 AutostartList list, AutostartEngine engine, AutostartContext context, CancellationToken cancellationToken)
    {
        var target = options.AutostartTarget ?? throw new ArgumentException("Polecenie --set-autostart wymaga stanu docelowego.", nameof(options));
        var row = list.Rows.FirstOrDefault(r => string.Equals(r.Id, options.TweakId, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return CliRunner.Build(options, catalog, services, startedAt, CliExitCode.OperationFailed, options.TweakId, warnings,
                errors: new[] { L.F("Nie ma pozycji '{0}' na liście autostartu.", options.TweakId) });
        }

        // Ta sama reguła co pole wyboru na stronie i strażnik silnika (AutostartRow.CanToggle): odmowa, zanim cokolwiek zostanie zapisane.
        if (!row.CanToggle)
        {
            var refused = new TweakResult { TweakId = row.Id, Outcome = TweakOutcome.Refused, Message = RefusalText(row) };
            return CliRunner.Build(options, catalog, services, startedAt, CliExitCodes.FromOutcome(refused.Outcome), row.Id, warnings, result: refused);
        }

        var batch = engine.Set(new[] { row }, target, context, new AutostartRunOptions(), cancellationToken);
        var result = batch.Results.Single();
        return CliRunner.Build(options, catalog, services, startedAt, CliExitCodes.FromOutcome(result.Outcome), row.Id, warnings, result: result, batch: batch);
    }

    /// <summary>
    /// Powód odmowy z blokady wiersza; bez blokady (wiersz złożony poza klasyfikatorem: kłódka albo format i stan nie do zapisu) – tekst kłódki
    /// albo nieznanego zapisu stanu.
    /// </summary>
    private static string RefusalText(AutostartRow row)
        => row.BlockedReason
           ?? AutostartText.Blocked(row.Verdict == AutostartVerdict.Protected ? AutostartBlocker.Protected : AutostartBlocker.UnknownFormat)!;
}
