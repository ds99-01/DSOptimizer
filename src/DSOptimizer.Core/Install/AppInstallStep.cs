using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Krok serii instalacji (jedna aplikacja) dla <see cref="AppInstallEngine"/>, wzorem <c>AppRemovalStep</c>:
/// dopasowanie przed próbą (już jest = <see cref="TweakOutcome.AlreadyInstalled"/> bez journala i bez winget) → wpis journala
/// <c>Kind="app-install"</c> ze zdarzeniem <c>Started</c> i statusem <see cref="JournalEntryStatus.Aborted"/> <b>przed</b> winget →
/// <c>winget install</c> (jedno uruchomienie, bez <c>--locale</c> – R14) → wynik z kodu (spec 5.3) i odczyt zwrotny → zamknięcie wpisu.
/// Tylko pozycje z <see cref="InstallTarget.WingetId"/>; pozycja z GitHuba idzie do <see cref="GitHubInstallStep"/>.
/// </summary>
internal sealed class AppInstallStep
{
    private const string Component = "Install";

    private readonly IWingetRunner _runner;
    private readonly IWingetLogs _logs;
    private readonly InstallReadback _readback;
    private readonly InstallJournal _journal;
    private readonly ILog _log;
    private readonly IClock _clock;

    public AppInstallStep(
        IWingetRunner runner,
        IWingetLogs logs,
        InstallReadback readback,
        InstallJournal journal,
        ILog log,
        IClock clock)
    {
        _runner = runner;
        _logs = logs;
        _readback = readback;
        _journal = journal;
        _log = log;
        _clock = clock;
    }

    /// <param name="progress">Etap („Pobieranie”/„Instalowanie”/„Sprawdzanie instalacji”) i procent; wołane tylko przy zmianie.</param>
    public InstallStepOutcome Run(InstallTarget target, InstallOptions options, Action<string?, int?> progress)
    {
        // Review Tasku 1 (M5): pozycja bez identyfikatora winget nigdy nie trafia do winget (rozgałęzienie po Origin w silniku).
        var wingetId = target.WingetId
            ?? throw new InvalidOperationException(L.F("Pozycja {0} nie ma identyfikatora winget – nie może iść do winget.", target.Id));

        if (target.Match is not null && _readback.IsMatched(target, out _) == true)
        {
            return Finish(target, TweakOutcome.AlreadyInstalled, InstallText.AlreadyInstalledBeforeRun, new List<string>(), ran: false);
        }

        var warnings = new List<string>();
        var open = _journal.Open(target, InstallText.JournalMessage(wingetId, null, null), warnings);
        if (open is null)
        {
            return Finish(target, TweakOutcome.Failed, InstallText.JournalFailed, warnings, ran: false);
        }

        var attempt = Attempt(wingetId, options.PerAppLimit, progress);
        var run = attempt.Run;
        var version = run is null ? null : WingetOutput.FoundVersion(run.Output, wingetId);
        TweakOutcome outcome;
        string message;
        var requiresReboot = false;
        var timedOut = false;
        if (attempt.Error is not null)
        {
            outcome = TweakOutcome.Failed;
            message = attempt.Error;
        }
        else if (run!.TimedOut || run.ExitCode is null)
        {
            // Spec 5.4: winget i instalator działają dalej (nigdy ich nie zabijamy), seria się kończy.
            outcome = TweakOutcome.NotConfirmed;
            message = InstallText.StillRunningInBackground(options.PerAppLimit);
            timedOut = true;
        }
        else
        {
            var code = run.ExitCode.Value;
            switch (WingetExitCodes.Classify(code))
            {
                case WingetVerdict.Success:
                case WingetVerdict.SuccessRebootRequired:
                    requiresReboot = WingetExitCodes.Classify(code) == WingetVerdict.SuccessRebootRequired;
                    (outcome, message) = Readback(target, wingetId, attempt.LogPath, progress);
                    break;
                case WingetVerdict.AlreadyInstalled:
                    outcome = TweakOutcome.AlreadyInstalled;
                    message = InstallText.AlreadyInstalledByWinget;
                    break;
                case WingetVerdict.RebootBeforeInstall:
                    outcome = TweakOutcome.Failed;
                    message = InstallText.FailedWithCode(code, attempt.LogPath);
                    requiresReboot = true;
                    break;
                case WingetVerdict.Unknown or WingetVerdict.Cancelled or WingetVerdict.BlockedByAntivirus when target.Match is not null && _readback.IsMatched(target, out _) == true:
                    // Ruling R21 (HWMonitor, 0x80004004 na Pro; spec 11.4 – wygrywa też z kodami przerwania i antywirusa): nieznany kod, a program pozycji listy widać w jednym świeżym
                    // odczycie – lustro „sukcesu bez dowodu”, nigdy Installed. Bez okna 60 s: instalator skończył przed winget.
                    outcome = TweakOutcome.NotConfirmed;
                    message = InstallText.FailedButVisible(code, attempt.LogPath);
                    break;
                default:
                    outcome = TweakOutcome.Failed;
                    message = InstallText.FailedWithCode(code, attempt.LogPath);
                    break;
            }
        }

        var reason = outcome is TweakOutcome.Failed or TweakOutcome.NotConfirmed ? InstallText.WithoutLog(message) : null;
        _journal.Close(open, outcome, requiresReboot, InstallText.JournalMessage(wingetId, version, attempt.LogPath), reason, warnings);
        return Finish(
            target,
            outcome,
            message,
            warnings,
            ran: attempt.Started,
            exitCode: run?.ExitCode,
            logPath: attempt.LogPath,
            requiresReboot: requiresReboot,
            version: version,
            timedOut: timedOut);
    }

    /// <summary>
    /// Jedno uruchomienie <c>winget install</c>: nowa ścieżka logu, linie postępu do <paramref name="progress"/>, reszta do logu.
    /// Błąd przygotowania logu (np. <c>IOException</c> przy podłożonym junction, <c>ArgumentException</c>) = winget nie startuje.
    /// <see cref="InstallAttempt.Error"/> to gotowy tekst wyniku pozycji.
    /// </summary>
    private InstallAttempt Attempt(string wingetId, TimeSpan limit, Action<string?, int?> progress)
    {
        string logPath;
        string arguments;
        try
        {
            logPath = _logs.NewLogPath(wingetId, _clock.UtcNow);
            arguments = WingetArguments.Install(wingetId, logPath);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.F("Nie udało się przygotować pliku logu winget dla {0}.", wingetId), ex);
            return new InstallAttempt(null, null, InstallText.LogPathFailed(ex.Message), false);
        }

        try
        {
            var tracker = new InstallProgressTracker(progress);
            var run = _runner.Run(arguments, limit, line => OnLine(wingetId, line, tracker));
            return new InstallAttempt(run, logPath, null, true);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.F("Nie udało się uruchomić winget dla {0}.", wingetId), ex);
            return new InstallAttempt(null, logPath, InstallText.StartFailed(ex.Message), true);
        }
    }

    /// <summary>Linia postępu (spinner, pasek, procent, rozmiary) nie trafia do logu, tylko do etapu i procentu; reszta do logu.</summary>
    private void OnLine(string wingetId, string line, InstallProgressTracker tracker)
    {
        if (WingetOutput.IsProgressLine(line))
        {
            if (WingetOutput.TryParsePercent(line) is { } percent)
            {
                tracker.Percent(percent);
            }

            return;
        }

        if (WingetOutput.StageOf(line) is { } stage)
        {
            tracker.Stage(stage == InstallStage.Downloading ? InstallText.StageDownloading : InstallText.StageInstalling);
        }

        _log.Info(Component, $"{wingetId}: {line}");
    }

    /// <summary>
    /// Odczyt zwrotny po sukcesie winget (spec 5.3/5.4). Pozycja listy: okno <see cref="InstallReadback.WaitForMatch"/>.
    /// Pozycja z wyszukiwania: <c>winget list --id … --exact</c> z kodem 0 i identyfikatorem w wyjściu (bez wielkości liter),
    /// inaczej słabszy dowód.
    /// </summary>
    private (TweakOutcome Outcome, string Message) Readback(InstallTarget target, string wingetId, string? logPath, Action<string?, int?> progress)
    {
        if (target.Match is not null)
        {
            var matched = _readback.WaitForMatch(target, progress, out var error);
            if (matched is null)
            {
                return (TweakOutcome.NotConfirmed, InstallText.WithLog(InstallText.NotVisibleReadFailed(error!) + ".", logPath));
            }

            return matched.Value
                ? (TweakOutcome.Installed, InstallText.Installed)
                : (TweakOutcome.NotConfirmed, InstallText.WithLog(InstallText.NotVisibleAfterSuccess + ".", logPath));
        }

        try
        {
            var list = _runner.Run(WingetArguments.ListExact(wingetId), InstallTimeouts.List, null);
            var listed = !list.TimedOut
                && list.ExitCode == 0
                && list.Output.Any(line => line.IndexOf(wingetId, StringComparison.OrdinalIgnoreCase) >= 0);
            return listed ? (TweakOutcome.Installed, InstallText.Installed) : (TweakOutcome.InstalledUnverified, InstallText.InstalledUnverified);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.F("winget list dla {0} nie powiódł się.", wingetId), ex);
            return (TweakOutcome.InstalledUnverified, InstallText.InstalledUnverified);
        }
    }

    private InstallStepOutcome Finish(
        InstallTarget target,
        TweakOutcome outcome,
        string message,
        List<string> warnings,
        bool ran,
        int? exitCode = null,
        string? logPath = null,
        bool requiresReboot = false,
        string? version = null,
        bool timedOut = false)
    {
        var result = new TweakResult { TweakId = target.Id, Outcome = outcome, Message = message, Warnings = warnings, RequiresReboot = requiresReboot };
        var line = $"{target.Id} ({target.WingetId}): {outcome} – {message}";
        if (result.IsSuccess)
        {
            _log.Info(Component, line);
        }
        else
        {
            _log.Warn(Component, line);
        }

        var item = new InstallItemReport(target.Id, target.WingetId, target.Title, outcome, exitCode, message, logPath, requiresReboot, version);
        return new InstallStepOutcome(item, result, ran, timedOut);
    }

    /// <summary>Jedno uruchomienie winget: wynik (<c>null</c> przy błędzie), ścieżka logu, błąd i czy winget został wywołany.</summary>
    private sealed record InstallAttempt(WingetRun? Run, string? LogPath, string? Error, bool Started);
}

/// <summary>
/// Wynik kroku: raport pozycji, wynik serii, czy instalator (winget, a przy GitHubie – zapytanie do GitHuba) został wywołany,
/// czy minął limit czasu i czy pozycja z GitHuba nie miała połączenia (<see cref="NoNetwork"/>).
/// </summary>
internal sealed record InstallStepOutcome(InstallItemReport Item, TweakResult Result, bool Ran, bool TimedOut, bool NoNetwork = false);

/// <summary>Etap i procent pozycji; zgłasza tylko zmianę. Nowy etap zeruje procent (np. instalowanie po pobraniu).</summary>
internal sealed class InstallProgressTracker
{
    private readonly Action<string?, int?> _report;
    private string? _stage;
    private int? _percent;

    public InstallProgressTracker(Action<string?, int?> report) => _report = report;

    public void Stage(string stage)
    {
        if (string.Equals(stage, _stage, StringComparison.Ordinal))
        {
            return;
        }

        _stage = stage;
        _percent = null;
        _report(_stage, _percent);
    }

    public void Percent(int percent)
    {
        if (percent == _percent)
        {
            return;
        }

        _percent = percent;
        _report(_stage, _percent);
    }
}
