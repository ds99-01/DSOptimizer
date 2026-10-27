using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Seria instalacji aplikacji (spec 5, plus RustDesk z GitHuba): jeden odczyt listy zainstalowanych i sprawdzenie winget
/// (<see cref="WingetAvailability"/>) na serię, potem każda pozycja osobno – winget przez <see cref="AppInstallStep"/>, pozycja
/// z GitHuba przez <see cref="GitHubInstallStep"/> – w kolejności wejścia (sortuje wywołujący, <see cref="InstallOrder"/>). Bez punktu
/// przywracania i bez cofania. Seria kończy się wcześniej po limicie czasu (instalacja trwa w tle) i po braku sieci na pierwszej
/// uruchomionej pozycji; „Zatrzymaj” działa tylko między pozycjami.
/// </summary>
public sealed class AppInstallEngine
{
    /// <summary>Akcja zdarzeń journala instalacji (<c>install-app</c>: <c>Started</c>, potem nazwa wyniku).</summary>
    public const string JournalAction = "install-app";

    private const string Component = "Install";

    private readonly IWingetRunner _runner;
    private readonly IInstalledAppsReader _reader;
    private readonly IUserContext _user;
    private readonly string _processSid;
    private readonly Func<string, string?> _accountName;
    private readonly ILog _log;
    private readonly IClock _clock;
    private readonly AppInstallStep _step;
    private readonly GitHubInstallStep? _gitHubStep;

    /// <param name="gitHub">Usługi pozycji z GitHuba; <c>null</c> = taka pozycja kończy się „instalacja z GitHuba nie jest dostępna”.</param>
    public AppInstallEngine(
        IWingetRunner runner,
        IWingetLogs logs,
        IInstalledAppsReader reader,
        IJournalStore journal,
        IUserContext user,
        string processSid,
        Func<string, string?> accountName,
        ILog log,
        IClock clock,
        Action<TimeSpan>? sleep = null,
        GitHubInstallServices? gitHub = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _processSid = processSid ?? throw new ArgumentNullException(nameof(processSid));
        _accountName = accountName ?? throw new ArgumentNullException(nameof(accountName));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (logs is null)
        {
            throw new ArgumentNullException(nameof(logs));
        }

        var readback = new InstallReadback(reader, user, log, clock, sleep ?? Thread.Sleep);
        var installJournal = new InstallJournal(journal ?? throw new ArgumentNullException(nameof(journal)), user, log, clock);
        _step = new AppInstallStep(runner, logs, readback, installJournal, log, clock);
        _gitHubStep = gitHub is null ? null : new GitHubInstallStep(gitHub, logs, readback, installJournal, log, clock);
    }

    /// <summary>
    /// Instaluje pozycje po kolei; porażka jednej nie zatrzymuje reszty (poza brakiem sieci na pierwszej uruchomionej i limitem czasu).
    /// Pusta pozycja, powtórzony <c>PackageKey</c> (bez wielkości liter) albo pozycja z GitHuba spoza <see cref="GitHubSources"/>
    /// = <see cref="ArgumentException"/> zanim cokolwiek się wykona.
    /// Odmowa konta (inne konto, niepewny użytkownik) albo błąd odczytu listy = każda pozycja <see cref="TweakOutcome.Refused"/>,
    /// także z GitHuba (decyzja planu 2). Winget niegotowy (brak App Installera, <c>--version</c> nie działa) = pozycje winget
    /// <see cref="TweakOutcome.Refused"/> z <see cref="WingetStatus.Text"/> bez uruchamiania, pozycje z GitHuba idą dalej.
    /// </summary>
    public InstallBatch Install(IReadOnlyList<InstallTarget> targets, InstallOptions options, CancellationToken ct)
    {
        if (targets is null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        Guard(targets);
        var warnings = new List<string>();
        var winget = CheckWinget(warnings, out var readFailed);
        var wingetReady = winget.State == WingetState.Ready;
        var anyGitHub = targets.Any(target => target.Origin == InstallOrigin.GitHub);
        if (readFailed || !WingetAvailability.AllowsGitHub(winget) || (!wingetReady && !anyGitHub))
        {
            if (targets.Count > 0)
            {
                _log.Warn(Component, "Instalacja nie rozpoczęta: " + winget.Text);
            }

            var refused = targets.Select(target => Unstarted(target, TweakOutcome.Refused, winget.Text)).ToList();
            return Build(refused, warnings, stopped: false, winget);
        }

        if (!wingetReady && targets.Any(target => target.Origin == InstallOrigin.Winget))
        {
            _log.Warn(Component, "Pozycje winget nie rozpoczęte: " + winget.Text);
        }

        var items = new List<(InstallItemReport Item, TweakResult Result)>(targets.Count);
        string? endReason = null;
        var anyRan = false;
        var stopped = false;
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var index = i;
            // Limit czasu, brak sieci i „Zatrzymaj” wygrywają z niedostępnością pozycji (review Tasku 2, M2): reszta serii jest
            // pominięta, a seria po „Zatrzymaj” – zatrzymana, także gdy zostały w niej tylko pozycje, które i tak by nie ruszyły.
            if (endReason is not null)
            {
                items.Add(Unstarted(target, TweakOutcome.Skipped, endReason));
                continue;
            }

            if (ct.IsCancellationRequested || options.Stop is { IsRequested: true })
            {
                stopped = true;
                items.Add(Unstarted(target, TweakOutcome.Skipped, TweakEngine.StoppedMessage));
                continue;
            }

            var unavailable = Unavailable(target, wingetReady, winget);
            if (unavailable is { } refusal)
            {
                // Pozycja, która w tym uruchomieniu nie może ruszyć: od razu wynik, żeby okno serii nie czekało na nią do końca.
                items.Add(refusal);
                Report(options.Steps, new BatchStepEvent(index, targets.Count, BatchStepKind.Item, target.Id, BatchStepPhase.Finished, _clock.UtcNow, refusal.Result));
                continue;
            }

            Report(options.Steps, new BatchStepEvent(index, targets.Count, BatchStepKind.Item, target.Id, BatchStepPhase.Started, _clock.UtcNow));
            InstallStepOutcome step;
            try
            {
                Action<string?, int?> progress = (stage, percent) => Report(
                    options.Steps,
                    new BatchStepEvent(index, targets.Count, BatchStepKind.Item, target.Id, BatchStepPhase.Progress, _clock.UtcNow, Stage: stage, Percent: percent));
                step = target.Origin == InstallOrigin.GitHub
                    ? _gitHubStep!.Run(target, options, progress)
                    : _step.Run(target, options, progress);
            }
            catch (Exception ex)
            {
                _log.Fail(Component, $"Nieoczekiwany błąd instalacji {target.Id}.", ex);
                var failed = Unstarted(target, TweakOutcome.Failed, $"Nieoczekiwany błąd: {ex.Message}");
                step = new InstallStepOutcome(failed.Item, failed.Result, false, false);
            }

            items.Add((step.Item, step.Result));
            Report(options.Steps, new BatchStepEvent(index, targets.Count, BatchStepKind.Item, target.Id, BatchStepPhase.Finished, _clock.UtcNow, step.Result));
            if (step.TimedOut)
            {
                endReason = InstallText.SkippedStillRunning;
            }
            else if (step.Ran && !anyRan && step.Item.WingetExitCode is { } code && WingetExitCodes.IsNetwork(code))
            {
                // Spec 5.1: brak sieci na pierwszej uruchomionej pozycji = nie ma sensu czekać na kolejne porażki.
                endReason = InstallText.SkippedNoNetwork;
            }
            else if (step.Ran && !anyRan && step.NoNetwork)
            {
                // Spec RustDesk 3.7: brak połączenia z GitHubem na pierwszej uruchomionej pozycji kończy serię jak w winget.
                endReason = InstallText.SkippedNoInternet;
            }

            anyRan |= step.Ran;
        }

        return Build(items, warnings, stopped, winget);
    }

    private static void Guard(IReadOnlyList<InstallTarget> targets)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            if (target is null)
            {
                throw new ArgumentException("Pusta pozycja w serii instalacji.", nameof(targets));
            }

            if (target.Origin == InstallOrigin.GitHub && (target.GitHub is null || !GitHubSources.IsAllowed(target.Id, target.GitHub) || target.Match is null))
            {
                // Reguły pobrania są stałe dla jednej pozycji (RustDeskSource) – inna pozycja nie może ich użyć.
                throw new ArgumentException($"Pozycja {target.Id} nie może instalować się z GitHuba.", nameof(targets));
            }

            if (!seen.Add(target.PackageKey))
            {
                throw new ArgumentException($"Pakiet {target.PackageKey} występuje w serii więcej niż raz.", nameof(targets));
            }
        }
    }

    /// <summary>Pozycja winget przy niegotowym winget albo z GitHuba bez usług GitHuba: gotowy wynik bez uruchamiania; inaczej <c>null</c>.</summary>
    private (InstallItemReport Item, TweakResult Result)? Unavailable(InstallTarget target, bool wingetReady, WingetStatus winget)
    {
        if (target.Origin == InstallOrigin.Winget)
        {
            return wingetReady ? null : Unstarted(target, TweakOutcome.Refused, winget.Text);
        }

        return _gitHubStep is null ? Unstarted(target, TweakOutcome.Failed, InstallText.GitHubUnavailable) : null;
    }

    /// <summary>
    /// Jeden odczyt listy na serię i stan winget. Błąd odczytu (<paramref name="readFailed"/>): odmowa konta wygrywa, inaczej
    /// winget „nie działa” z powodem – i żadna pozycja nie rusza, także z GitHuba (bez odczytu nie ma dopasowania ani odczytu zwrotnego).
    /// </summary>
    private WingetStatus CheckWinget(List<string> warnings, out bool readFailed)
    {
        readFailed = false;
        IReadOnlyList<InstalledApp> raw;
        try
        {
            var reading = _reader.Read(_user.Sid);
            raw = reading.Apps;
            foreach (var warning in reading.Warnings)
            {
                _log.Warn(Component, warning);
                warnings.Add(warning);
            }
        }
        catch (Exception ex)
        {
            _log.Fail(Component, "Odczyt listy zainstalowanych aplikacji nie powiódł się.", ex);
            readFailed = true;
            return WingetAvailability.CheckAccount(_user, _processSid, _accountName)
                ?? new WingetStatus(WingetState.NotWorking, null, InstallText.ReadFailed(ex.Message));
        }

        return WingetAvailability.Check(_user, _processSid, raw, _runner, _accountName);
    }

    /// <summary>Odbiornik zdarzeń nie może zatrzymać serii: wyjątek ląduje w logu.</summary>
    private void Report(IProgress<BatchStepEvent>? steps, BatchStepEvent step)
    {
        if (steps is null)
        {
            return;
        }

        try
        {
            steps.Report(step);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, $"Odbiornik postępu instalacji rzucił wyjątek ({step.Kind} {step.Phase}).", ex);
        }
    }

    private static (InstallItemReport Item, TweakResult Result) Unstarted(InstallTarget target, TweakOutcome outcome, string message)
        => (new InstallItemReport(target.Id, target.WingetId, target.Title, outcome, null, message, null, false, null, target.Origin, target.GitHub),
            new TweakResult { TweakId = target.Id, Outcome = outcome, Message = message });

    private static InstallBatch Build(
        IReadOnlyList<(InstallItemReport Item, TweakResult Result)> items,
        IReadOnlyList<string> warnings,
        bool stopped,
        WingetStatus winget)
        => new(
            new BatchResult { Results = items.Select(pair => pair.Result).ToList(), Warnings = warnings, Stopped = stopped },
            items.Select(pair => pair.Item).ToList(),
            winget);
}
