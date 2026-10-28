using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Apps;

/// <summary>Wynik odczytu strony „Aplikacje”: sklasyfikowane wiersze i ostrzeżenia odczytu (reszta listy się wyświetla).</summary>
public sealed record AppsPage(IReadOnlyList<AppRow> Rows, IReadOnlyList<string> Warnings);

/// <summary>
/// Logika strony „Aplikacje” bez WPF: odczyt i klasyfikacja, treść okna potwierdzenia „Usuń (N)” i seria usuwania.
/// Usuwanie biegnie pod tą samą blokadą co seria tweaków (<see cref="GuiSession.TryRunExclusive{T}"/>): zajęta = odmowa, nic nie rusza.
/// Wszystkie metody wołać poza wątkiem UI (odczyt AppX to PowerShell, nazwy kont to LSA).
/// </summary>
public sealed class AppsSession
{
    private const string ReportId = "usuwanie";

    private readonly GuiSession _session;
    private readonly IInstalledAppsReader _reader;
    private readonly AppRemovalEngine _engine;
    private readonly AppsBloatCatalog _catalog;
    private readonly ISystemProtectionReader _protection;
    private readonly IAccountNames _names;
    private readonly IUserContext _user;
    private readonly string _processSid;
    private readonly Func<string, bool> _fileExists;
    private readonly string _system32;
    private readonly IClock _clock;

    public AppsSession(
        GuiSession session,
        IInstalledAppsReader reader,
        AppRemovalEngine engine,
        AppsBloatCatalog catalog,
        ISystemProtectionReader protection,
        IAccountNames names,
        IUserContext user,
        string processSid,
        Func<string, bool> fileExists,
        string system32,
        IClock clock)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _names = names ?? throw new ArgumentNullException(nameof(names));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _processSid = processSid ?? throw new ArgumentNullException(nameof(processSid));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _system32 = system32 ?? throw new ArgumentNullException(nameof(system32));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>SID zalogowanego użytkownika (konto aplikacji ze Sklepu na liście).</summary>
    public string UserSid => _user.Sid;

    /// <summary>Zakres usuwania aplikacji ze Sklepu: <see cref="StoreRemovalScope.AllUsers"/>, gdy SID procesu ≠ SID zalogowanego (R8).</summary>
    public StoreRemovalScope StoreScope => string.Equals(_processSid, _user.Sid, StringComparison.OrdinalIgnoreCase)
        ? StoreRemovalScope.CurrentUser
        : StoreRemovalScope.AllUsers;

    /// <summary>Świeży odczyt aplikacji zalogowanego użytkownika i klasyfikacja (ukryte pominięte). Błąd źródła = ostrzeżenie.</summary>
    public AppsPage Load()
    {
        var reading = _reader.Read(_user.Sid);
        var rows = AppClassifier.Classify(
            reading.Apps, _catalog, _user.Sid, _user.IsCertain, _processSid, _names.NameOf, _fileExists, _system32);
        return new AppsPage(rows, reading.Warnings);
    }

    /// <summary>
    /// Treść okna „Usuń zaznaczone”: fakty (punkt przywracania wg reguły silnika i stanu ochrony systemu, restart, cofanie),
    /// ostrzeżenia (ochrona systemu, inne konto procesu – R8, brak cofania) i pozycje z trybem usunięcia.
    /// Wiersz chroniony albo zablokowany = <see cref="ArgumentException"/> (do serii trafiają tylko <see cref="AppRemovability.Removable"/>).
    /// </summary>
    public SeriesConfirmation ConfirmationFor(IReadOnlyList<AppRow> rows, bool restorePointSetting)
    {
        if (rows is null || rows.Count == 0)
        {
            throw new ArgumentException("Seria usuwania musi zawierać co najmniej jedną aplikację.", nameof(rows));
        }

        if (rows.Any(row => !IsRemovable(row)))
        {
            throw new ArgumentException(AppsPageText.BlockedRefusal, nameof(rows));
        }

        var hasProgram = rows.Any(row => row.App.Kind == AppKind.Program);
        var hasStore = rows.Any(row => row.App.Kind == AppKind.Store);
        var withRestorePoint = hasProgram || restorePointSetting;
        var allUsers = StoreScope == StoreRemovalScope.AllUsers;
        var userName = _names.NameOf(_user.Sid);

        var facts = new List<SeriesFact>();
        var warnings = new List<string>();
        facts.Add(new SeriesFact(AppsPageText.RestorePointFact, RestorePointFact(hasProgram, withRestorePoint, warnings)));
        facts.Add(new SeriesFact(AppsPageText.RestartFact, hasProgram ? AppsPageText.RestartByUninstaller : AppsPageText.RestartNotNeeded));
        facts.Add(new SeriesFact(AppsPageText.UndoFact, AppsPageText.Irreversible));
        if (allUsers && hasStore)
        {
            warnings.Add(AppsPageText.RunsAsOtherAccount(_names.NameOf(_processSid) ?? _processSid, userName ?? _user.Sid));
        }

        warnings.Add(AppsText.IrreversibleWarning);
        var items = rows.Select(row => Item(row, userName, allUsers)).ToList();
        return new SeriesConfirmation(
            GuiOperation.RemoveApps,
            AppsPageText.ConfirmTitle,
            L.P("{0} aplikacja", "{0} aplikacje", "{0} aplikacji", rows.Count),
            facts,
            items,
            L.F("Usuń ({0})", rows.Count),
            withRestorePoint)
        {
            Warnings = warnings,
            RestorePointRequired = hasProgram,
        };
    }

    /// <summary>
    /// Seria usuwania pod blokadą sesji (ta sama co seria tweaków). Zajęta blokada albo wiersz chroniony/zablokowany = odmowa:
    /// jedna linia Warning, <see cref="GuiOperationReport.Batch"/> = null, nic nie zostało uruchomione.
    /// </summary>
    public GuiOperationReport Remove(IReadOnlyList<AppRow> rows, AppRemovalOptions options, CancellationToken ct = default)
    {
        if (rows is null || rows.Count == 0)
        {
            throw new ArgumentException("Seria usuwania musi zawierać co najmniej jedną aplikację.", nameof(rows));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (rows.Any(row => !IsRemovable(row)))
        {
            return Refusal(AppsPageText.BlockedRefusal);
        }

        return _session.TryRunExclusive(() => RemoveLocked(rows, options, ct), out var report)
            ? report
            : Refusal(L.T("Trwa inna operacja – nic nie zmieniono."));
    }

    private GuiOperationReport RemoveLocked(IReadOnlyList<AppRow> rows, AppRemovalOptions options, CancellationToken ct)
    {
        var batch = _engine.Remove(rows, options, ct);
        var lines = new List<GuiLogLine>();
        foreach (var warning in batch.Warnings)
        {
            lines.Add(Line(GuiLogLevel.Warning, warning));
        }

        if (batch.Aborted)
        {
            lines.Add(Line(GuiLogLevel.Error, batch.AbortReason ?? L.T("Seria przerwana.")));
        }

        var names = rows.GroupBy(row => row.App.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().App.Name, StringComparer.Ordinal);
        foreach (var result in batch.Results)
        {
            AddResultLines(lines, names.TryGetValue(result.TweakId, out var name) ? name : result.TweakId, result);
        }

        var removed = batch.Results.Where(result => result.Outcome == TweakOutcome.Removed).Select(result => result.TweakId).ToList();
        lines.Add(Line(GuiLogLevel.Info, AppsPageText.BatchSummary(removed.Count, rows.Count)));
        return new GuiOperationReport
        {
            TweakId = ReportId,
            Operation = GuiOperation.RemoveApps,
            Lines = lines,
            RequiresReboot = batch.Results.Any(result => result.RequiresReboot),
            SucceededIds = removed,
            Batch = batch,
        };
    }

    private void AddResultLines(List<GuiLogLine> lines, string name, TweakResult result)
    {
        var level = result.Outcome switch
        {
            TweakOutcome.Removed => GuiLogLevel.Info,
            TweakOutcome.NotConfirmed or TweakOutcome.Skipped or TweakOutcome.Refused => GuiLogLevel.Warning,
            _ => GuiLogLevel.Error,
        };
        var text = $"{name}: {AppsPageText.OutcomeName(result.Outcome)}";
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            var message = result.Outcome == TweakOutcome.Failed ? AppsPageText.FailureDetail(result.Message!) : result.Message!;
            text += " – " + GuiText.OneLine(message, GuiText.MaxResultMessageLength);
        }

        lines.Add(Line(level, text));
        foreach (var warning in result.Warnings)
        {
            lines.Add(Line(GuiLogLevel.Warning, warning));
        }

        if (result.RequiresReboot)
        {
            lines.Add(Line(GuiLogLevel.Warning, AppsPageText.RebootLine));
        }
    }

    /// <summary>Fakt „Punkt przywracania” według reguły silnika; ostrzeżenie dopisuje, gdy ochrona systemu jest wyłączona albo nieznana.</summary>
    private string RestorePointFact(bool hasProgram, bool withRestorePoint, List<string> warnings)
    {
        if (!withRestorePoint)
        {
            return AppsPageText.RestorePointSettingOff;
        }

        bool? enabled;
        try
        {
            enabled = _protection.IsEnabled();
        }
        catch (Exception)
        {
            // Odczyt ochrony to tylko zapowiedź w oknie: wyjątek = „nie udało się sprawdzić”, seria i tak spróbuje utworzyć punkt.
            enabled = null;
        }

        switch (enabled)
        {
            case false:
                warnings.Add(hasProgram ? AppsPageText.ProtectionOffWithQuestionWarning : AppsPageText.ProtectionOffWarning);
                return AppsPageText.RestorePointProtectionOff;
            case null:
                if (hasProgram)
                {
                    warnings.Add(AppsPageText.ProtectionUnknownWarning);
                }

                return AppsPageText.RestorePointUnknown;
            default:
                return hasProgram ? AppsPageText.RestorePointRequired : AppsPageText.RestorePointBySetting;
        }
    }

    private static SeriesItem Item(AppRow row, string? userName, bool allUsers)
    {
        var app = row.App;
        var note = app.Kind == AppKind.Store
            ? AppsPageText.StoreRemoval(userName, IsMicrosoft(row), allUsers)
            : AppsPageText.ProgramRemoval((row.Plan ?? throw new InvalidOperationException(L.F("Program {0} nie ma planu deinstalacji.", app.Id))).Mode);
        var warnings = string.IsNullOrWhiteSpace(row.Warning) ? Array.Empty<string>() : new[] { row.Warning! };
        return new SeriesItem(
            app.Id,
            app.Name,
            RiskLevel.Safe,
            AppsPageText.Summary(app.Kind, PublisherName.Readable(app.Publisher)),
            new[] { note },
            warnings,
            Array.Empty<SeriesFact>());
    }

    /// <summary>Do serii trafia tylko wiersz z <see cref="AppRow.CanRemove"/> (ta sama reguła co pole wyboru na stronie).</summary>
    private static bool IsRemovable(AppRow row) => row is not null && row.CanRemove;

    private static bool IsMicrosoft(AppRow row) => string.Equals(row.BloatGroup, "microsoft", StringComparison.Ordinal);

    private GuiOperationReport Refusal(string text) => new()
    {
        TweakId = ReportId,
        Operation = GuiOperation.RemoveApps,
        Lines = new[] { Line(GuiLogLevel.Warning, text) },
    };

    private GuiLogLine Line(GuiLogLevel level, string text) => new(_clock.UtcNow, level, text);
}
