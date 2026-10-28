using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Stan strony „Instalacja”: wiersze listy, stan winget i ostrzeżenia odczytu (reszta listy się wyświetla).
/// <see cref="ListReadFailed"/> = odczyt listy zainstalowanych się nie udał: wtedy <c>Winget.Text</c> to błąd odczytu
/// (<see cref="InstallText.ReadFailed"/>), a nie usterka winget, więc bez podpowiedzi „Napraw winget” (stan <c>NotWorking</c>,
/// chyba że konto procesu wygrywa: <c>OtherAccount</c>/<c>UserUncertain</c>).
/// </summary>
public sealed record InstallPageState(IReadOnlyList<InstallRow> Rows, WingetStatus Winget, IReadOnlyList<string> Warnings)
{
    public bool ListReadFailed { get; init; }
}

/// <summary>
/// Wynik wyszukiwania: wiersze spoza listy, komunikat w miejscu wyników (brak, błąd) albo <c>null</c> i Id pozycji listy
/// (<see cref="InstallPage.CatalogIdsIn"/>) trafionych przez winget. Puste <see cref="Rows"/> przy niepustych <see cref="OnListIds"/> =
/// <see cref="InstallText.SearchAllOnList"/>.
/// </summary>
public sealed record InstallSearchResult(IReadOnlyList<InstallRow> Rows, string? Message, IReadOnlyList<string> OnListIds);

/// <summary>
/// To, czego strona „Instalacja” potrzebuje od sesji poza serią (seria idzie przez okno serii): lista, odczyt i wyszukiwanie.
/// Implementuje <see cref="InstallSession"/>; testy modelu strony podają fałszywą sesję.
/// </summary>
public interface IInstallPageSession
{
    /// <summary>Lista <c>apps.json</c> strony (kategorie i opisy pozycji).</summary>
    AppsInstallCatalog Catalog { get; }

    /// <summary>Świeży odczyt listy zainstalowanych i stanu winget (poza wątkiem UI).</summary>
    InstallPageState Load();

    /// <summary><c>winget search</c> pod blokadą sesji (poza wątkiem UI).</summary>
    InstallSearchResult Search(string query);
}

/// <summary>
/// Logika strony „Instalacja” bez WPF (wzorzec <see cref="AppsSession"/>): odczyt listy i stanu winget, wyszukiwanie, treść okna
/// „Zainstaluj (N)” i seria instalacji. Wyszukiwanie i seria biegną pod blokadą <see cref="GuiSession.TryRunExclusive{T}"/>
/// (ta sama co seria tweaków i usuwanie): zajęta = komunikat albo odmowa, winget nie rusza. Wszystkie metody wołać poza wątkiem UI.
/// </summary>
public sealed class InstallSession : IInstallPageSession
{
    private const string ReportId = "instalacja";

    private static readonly IReadOnlyList<InstallRow> NoRows = Array.Empty<InstallRow>();

    private static readonly IReadOnlyList<string> NoIds = Array.Empty<string>();

    private readonly GuiSession _session;
    private readonly AppInstallEngine _engine;
    private readonly AppsInstallCatalog _catalog;
    private readonly IInstalledAppsReader _reader;
    private readonly IWingetRunner _runner;
    private readonly IUserContext _user;
    private readonly string _processSid;
    private readonly IAccountNames _names;
    private readonly IClock _clock;
    private volatile WingetStatus? _winget;

    public InstallSession(
        GuiSession session,
        AppInstallEngine engine,
        AppsInstallCatalog catalog,
        IInstalledAppsReader reader,
        IWingetRunner runner,
        IUserContext user,
        string processSid,
        IAccountNames names,
        IClock clock)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _processSid = processSid ?? throw new ArgumentNullException(nameof(processSid));
        _names = names ?? throw new ArgumentNullException(nameof(names));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Lista <c>apps.json</c> strony (kategorie i opisy pozycji: <c>Catalog.Find(row.Target.Id)?.Summary</c>).</summary>
    public AppsInstallCatalog Catalog => _catalog;

    /// <summary>
    /// Świeży surowy odczyt listy zainstalowanych (status „Zainstalowana”) i stan winget (<see cref="WingetAvailability"/>: przy
    /// innym koncie i niepewnym użytkowniku winget nie startuje). Ostrzeżenia odczytu przechodzą dalej. Błąd odczytu = odmowa konta,
    /// a gdy konto w porządku – <see cref="WingetState.NotWorking"/> z tekstem odczytu i <see cref="InstallPageState.ListReadFailed"/>.
    /// </summary>
    public InstallPageState Load()
    {
        IReadOnlyList<InstalledApp> raw;
        IReadOnlyList<string> warnings;
        try
        {
            var reading = _reader.Read(_user.Sid);
            raw = reading.Apps;
            warnings = reading.Warnings;
        }
        catch (Exception ex)
        {
            var failed = WingetAvailability.CheckAccount(_user, _processSid, _names.NameOf)
                ?? new WingetStatus(WingetState.NotWorking, null, InstallText.ReadFailed(ex.Message));
            _winget = failed;
            return new InstallPageState(InstallPage.CatalogRows(_catalog, Array.Empty<InstalledApp>(), failed, listRead: false), failed, Array.Empty<string>())
            {
                ListReadFailed = true,
            };
        }

        var winget = WingetAvailability.Check(_user, _processSid, raw, _runner, _names.NameOf);
        _winget = winget;
        return new InstallPageState(InstallPage.CatalogRows(_catalog, raw, winget), winget, warnings);
    }

    /// <summary>
    /// <c>winget search</c> (spec 7) pod blokadą sesji. Niepoprawne zapytanie (puste, za długie, cudzysłów, znak sterujący) albo
    /// winget niegotowy = komunikat bez uruchamiania winget. Stan winget z ostatniego <see cref="Load"/> (bez niego – świeży odczyt).
    /// </summary>
    public InstallSearchResult Search(string query)
    {
        if (!WingetArguments.TryValidateQuery(query, out var error))
        {
            return new InstallSearchResult(NoRows, error, NoIds);
        }

        return _session.TryRunExclusive(() => SearchLocked(query), out var result)
            ? result
            : new InstallSearchResult(NoRows, InstallText.SearchBusy, NoIds);
    }

    /// <summary>
    /// Treść okna „Zainstaluj (N)”: pozycje w kolejności serii (<see cref="InstallOrder"/>), ostrzeżenia serii (spec 5.4 przy pozycji
    /// winget, <see cref="InstallText.SeriesWarningGitHub"/> przy pozycji z GitHuba), fakty (źródło – winget, GitHub albo oba „; ”, brak
    /// punktu przywracania, restart) i ostrzeżenia pozycji. Pusta seria, Id, którego nie da się zainstalować (obcięte „…”), albo pozycja
    /// z GitHuba spoza <see cref="GitHubSources"/> = <see cref="ArgumentException"/>.
    /// </summary>
    public SeriesConfirmation ConfirmationFor(IReadOnlyList<InstallTarget> targets)
    {
        if (targets is null || targets.Count == 0)
        {
            throw new ArgumentException("Seria instalacji musi zawierać co najmniej jedną aplikację.", nameof(targets));
        }

        if (!AllInstallable(targets))
        {
            throw new ArgumentException(InstallText.InvalidTargetRefusal, nameof(targets));
        }

        var sorted = InstallOrder.Sort(targets, _catalog);
        var anyWinget = sorted.Any(target => target.Origin == InstallOrigin.Winget);
        var anyGitHub = sorted.Any(target => target.Origin == InstallOrigin.GitHub);
        var sources = new List<string>(2);
        var warnings = new List<string>(2);
        if (anyWinget)
        {
            sources.Add(InstallText.SourceValue);
            warnings.Add(InstallText.SeriesWarning);
        }

        if (anyGitHub)
        {
            sources.Add(InstallText.GitHubSourceValue);

            // „Wymaga internetu.” raz na okno (M4): w serii mieszanej mówi to już ostrzeżenie winget.
            warnings.Add(anyWinget ? InstallText.SeriesWarningGitHub : InstallText.SeriesWarningGitHubOnly);
        }

        var facts = new[]
        {
            new SeriesFact(InstallText.SourceFact, string.Join("; ", sources)),
            new SeriesFact(InstallText.RestorePointFact, InstallText.RestorePointValue),
            new SeriesFact(InstallText.RestartFact, InstallText.RestartValue),
        };
        return new SeriesConfirmation(
            GuiOperation.InstallApps,
            InstallText.ConfirmTitle,
            InstallText.AppCount(sorted.Count),
            facts,
            sorted.Select(Item).ToList(),
            InstallText.InstallButton(sorted.Count),
            CreatesRestorePoint: false)
        {
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Seria instalacji pod blokadą sesji, w kolejności <see cref="InstallOrder"/>. Zajęta blokada albo Id, którego nie da się
    /// zainstalować = odmowa: jedna linia Warning, <see cref="GuiOperationReport.Batch"/> = null, winget nie rusza. Blokada jest
    /// zwalniana także po limicie czasu (instalator działa dalej w tle, spec 5.4).
    /// </summary>
    public GuiOperationReport Install(
        IReadOnlyList<InstallTarget> targets,
        IProgress<BatchStepEvent>? steps,
        BatchStop? stop,
        CancellationToken ct = default)
    {
        if (targets is null || targets.Count == 0)
        {
            throw new ArgumentException("Seria instalacji musi zawierać co najmniej jedną aplikację.", nameof(targets));
        }

        if (!AllInstallable(targets))
        {
            return Refusal(InstallText.InvalidTargetRefusal);
        }

        var sorted = InstallOrder.Sort(targets, _catalog);
        return _session.TryRunExclusive(() => InstallLocked(sorted, steps, stop, ct), out var report)
            ? report
            : Refusal(InstallText.InstallBusy);
    }

    private InstallSearchResult SearchLocked(string query)
    {
        var winget = _winget ?? Load().Winget;
        if (winget.State != WingetState.Ready)
        {
            return new InstallSearchResult(NoRows, winget.Text, NoIds);
        }

        WingetRun run;
        try
        {
            run = _runner.Run(WingetArguments.Search(query), InstallTimeouts.Search, null);
        }
        catch (Exception ex)
        {
            return new InstallSearchResult(NoRows, InstallText.SearchStartFailed(ex.Message), NoIds);
        }

        if (run.TimedOut || run.ExitCode is null)
        {
            return new InstallSearchResult(NoRows, InstallText.SearchTimedOut(InstallTimeouts.Search), NoIds);
        }

        var code = run.ExitCode.Value;
        if (code == 0)
        {
            var found = WingetSearchParser.Parse(run.Output);
            if (found.Count == 0)
            {
                return new InstallSearchResult(NoRows, InstallText.SearchNoResults, NoIds);
            }

            var rows = InstallPage.SearchRows(found, _catalog, winget);
            var onList = InstallPage.CatalogIdsIn(found, _catalog);
            return new InstallSearchResult(rows, rows.Count == 0 && onList.Count > 0 ? InstallText.SearchAllOnList : null, onList);
        }

        var message = WingetExitCodes.Classify(code) == WingetVerdict.NotFound
            ? InstallText.SearchNoResults
            : WingetExitCodes.IsNetwork(code) ? InstallText.SearchNoNetwork : InstallText.SearchFailed(code);
        return new InstallSearchResult(NoRows, message, NoIds);
    }

    private GuiOperationReport InstallLocked(IReadOnlyList<InstallTarget> targets, IProgress<BatchStepEvent>? steps, BatchStop? stop, CancellationToken ct)
    {
        var install = _engine.Install(targets, new InstallOptions { Steps = steps, Stop = stop }, ct);
        _winget = install.Winget;
        var batch = install.Batch;
        var lines = batch.Warnings.Select(warning => Line(GuiLogLevel.Warning, warning)).ToList();
        for (var i = 0; i < batch.Results.Count; i++)
        {
            var result = batch.Results[i];
            var title = i < install.Items.Count ? install.Items[i].Title : result.TweakId;
            if (result.Outcome == TweakOutcome.Skipped && string.Equals(result.Message, TweakEngine.StoppedMessage, StringComparison.Ordinal))
            {
                // Jedno zdanie zamiast „Nie wykonano – ” + „Pominięte – zatrzymano.” (po angielsku status powtórzyłby się dwa razy).
                lines.Add(Line(LevelOf(result.Outcome), InstallText.StoppedLine(title)));
                lines.AddRange(result.Warnings.Select(warning => Line(GuiLogLevel.Warning, warning)));
                continue;
            }

            var text = title + ": " + InstallText.OutcomeName(result.Outcome);
            if (InstallText.ResultDetail(result, capitalize: false) is { } detail)
            {
                text += " – " + GuiText.OneLine(detail, GuiText.MaxResultMessageLength);
            }

            lines.Add(Line(LevelOf(result.Outcome), text));
            lines.AddRange(result.Warnings.Select(warning => Line(GuiLogLevel.Warning, warning)));
        }

        return new GuiOperationReport
        {
            TweakId = ReportId,
            Operation = GuiOperation.InstallApps,
            Lines = lines,
            RequiresReboot = batch.Results.Any(result => result.RequiresReboot),
            SucceededIds = batch.Results.Where(result => InstallPage.CountsAsInstalled(result.Outcome)).Select(result => result.TweakId).ToList(),
            Batch = batch,
        };
    }

    private SeriesItem Item(InstallTarget target)
        => new(
            target.Id,
            target.Title,
            RiskLevel.Safe,
            _catalog.Find(target.Id)?.Summary ?? InstallText.SearchItemSummary,
            Array.Empty<string>(),
            string.IsNullOrWhiteSpace(target.Warning) ? Array.Empty<string>() : new[] { target.Warning! },
            new[]
            {
                target.Origin == InstallOrigin.GitHub
                    ? new SeriesFact(InstallText.GitHubRepositoryFact, target.GitHub ?? string.Empty)
                    : new SeriesFact(InstallText.WingetIdFact, target.WingetId ?? string.Empty),
            });

    /// <summary>
    /// Do serii trafia tylko pełne Id winget (ta sama reguła co pole wyboru wyniku wyszukiwania i <c>--id --exact</c>) albo pozycja
    /// z GitHuba dozwolona w <see cref="GitHubSources"/> (dokładnie to Id i to repozytorium).
    /// </summary>
    private static bool AllInstallable(IReadOnlyList<InstallTarget> targets)
        => targets.All(target => target is not null
            && (target.Origin == InstallOrigin.GitHub
                ? GitHubSources.IsAllowed(target.Id, target.GitHub!)
                : WingetArguments.IsValidWingetId(target.WingetId)));

    private static GuiLogLevel LevelOf(TweakOutcome outcome) => outcome switch
    {
        TweakOutcome.Installed or TweakOutcome.AlreadyInstalled => GuiLogLevel.Info,
        TweakOutcome.InstalledUnverified or TweakOutcome.NotConfirmed or TweakOutcome.Skipped or TweakOutcome.Refused => GuiLogLevel.Warning,
        _ => GuiLogLevel.Error,
    };

    private GuiOperationReport Refusal(string text) => new()
    {
        TweakId = ReportId,
        Operation = GuiOperation.InstallApps,
        Lines = new[] { Line(GuiLogLevel.Warning, text) },
    };

    private GuiLogLine Line(GuiLogLevel level, string text) => new(_clock.UtcNow, level, text);
}

