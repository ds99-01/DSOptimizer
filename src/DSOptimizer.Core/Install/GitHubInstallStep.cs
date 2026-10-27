using System;
using System.Collections.Generic;
using System.IO;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Krok serii instalacji pozycji z GitHuba (RustDesk, spec RustDesk 3): dopasowanie przed próbą (już jest = bez journala i bez HTTP)
/// → wpis journala <c>Started</c>/<c>Aborted</c> <b>przed</b> HTTP → <c>releases/latest</c> i reguły <see cref="RustDeskSource"/> →
/// pobranie z kontrolą przekierowań i rozmiaru → SHA256 i podpis wydawcy (wszystko albo nic) → <c>msiexec</c> → odczyt zwrotny
/// (<see cref="InstallReadback"/>) → zamknięcie wpisu. Pobrany plik usuwany w <c>finally</c>, poza limitem czasu <c>msiexec</c>
/// (decyzja planu 7: instalator może go jeszcze czytać).
/// </summary>
internal sealed class GitHubInstallStep
{
    private const string Component = "Install";

    private readonly GitHubInstallServices _services;
    private readonly IWingetLogs _logs;
    private readonly InstallReadback _readback;
    private readonly InstallJournal _journal;
    private readonly ILog _log;
    private readonly IClock _clock;

    public GitHubInstallStep(
        GitHubInstallServices services,
        IWingetLogs logs,
        InstallReadback readback,
        InstallJournal journal,
        ILog log,
        IClock clock)
    {
        _services = services;
        _logs = logs;
        _readback = readback;
        _journal = journal;
        _log = log;
        _clock = clock;
    }

    /// <param name="progress">Etap („Pobieranie (z GitHuba)”, „Sprawdzanie podpisu”, „Instalowanie”, „Sprawdzanie instalacji”) i procent pobrania.</param>
    public InstallStepOutcome Run(InstallTarget target, InstallOptions options, Action<string?, int?> progress)
    {
        var repository = target.GitHub
            ?? throw new InvalidOperationException($"Pozycja {target.Id} nie ma repozytorium GitHuba.");
        var warnings = new List<string>();
        if (target.Match is not null && _readback.IsMatched(target, out _) == true)
        {
            return Finish(target, new Attempt(TweakOutcome.AlreadyInstalled, InstallText.AlreadyInstalledBeforeRun), warnings, ran: false);
        }

        var open = _journal.Open(target, InstallText.GitHubJournalMessage(repository, null, null), warnings);
        if (open is null)
        {
            return Finish(target, new Attempt(TweakOutcome.Failed, InstallText.JournalFailed), warnings, ran: false);
        }

        var attempt = new Attempt(TweakOutcome.Failed, string.Empty);
        try
        {
            Install(target, options, new InstallProgressTracker(progress), progress, attempt, warnings);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, $"Nieoczekiwany błąd instalacji {target.Id} z GitHuba.", ex);
            attempt.Fail($"Nieoczekiwany błąd: {ex.Message}");
        }

        var reason = attempt.Outcome is TweakOutcome.Failed or TweakOutcome.NotConfirmed ? InstallText.WithoutLog(attempt.Message) : null;
        _journal.Close(open, attempt.Outcome, attempt.RequiresReboot, InstallText.GitHubJournalMessage(repository, attempt.Version, attempt.LogPath), reason, warnings);
        return Finish(target, attempt, warnings, ran: true);
    }

    private void Install(
        InstallTarget target,
        InstallOptions options,
        InstallProgressTracker tracker,
        Action<string?, int?> progress,
        Attempt attempt,
        List<string> warnings)
    {
        tracker.Stage(InstallText.StageDownloadingGitHub);
        var release = FetchRelease(attempt);
        if (release is null)
        {
            return;
        }

        var path = _services.Files.NewDownloadPath(".msi");
        var keepFile = false;
        try
        {
            if (!Download(release, path, tracker, attempt))
            {
                return;
            }

            tracker.Stage(InstallText.StageCheckingSignature);
            if (CheckFile(path, release, out var signer) is { } reason)
            {
                attempt.Fail(InstallText.DownloadCheckFailed(reason));
                return;
            }

            // M3 final review: jedyny wyjątek od „program niczego nie pobiera” – log mówi, CO pobrano i zweryfikowano, także przy sukcesie.
            _log.Info(
                Component,
                $"Pobrano i sprawdzono {release.FileName} ({release.Tag}): {release.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)} B, SHA256 {release.Sha256}, podpis: {signer}");
            tracker.Stage(InstallText.StageInstalling);
            keepFile = RunMsi(target, options, path, progress, attempt);
        }
        finally
        {
            if (keepFile)
            {
                // Decyzja planu 7: msiexec nadal działa i może czytać plik – zostaje, z ostrzeżeniem w logu.
                _log.Warn(Component, $"Limit czasu msiexec: pobrany plik zostaje na dysku ({path}).");
            }
            else
            {
                Delete(path, warnings);
            }
        }
    }

    /// <summary>Odpowiedź <c>releases/latest</c> i reguły wydania; <c>null</c> = porażka zapisana w <paramref name="attempt"/>.</summary>
    private RustDeskRelease? FetchRelease(Attempt attempt)
    {
        var api = _services.Web.GetText(RustDeskSource.ReleasesLatestUrl, RustDeskSource.ApiAccept, RustDeskSource.ApiTimeout);
        if (api.Failure != WebFailure.None)
        {
            _log.Warn(Component, $"GitHub API: {api.Failure}, kod {api.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}: {api.Error}");
            switch (api.Failure)
            {
                case WebFailure.Network:
                case WebFailure.TimedOut:
                    attempt.Fail(InstallText.GitHubNoConnection);
                    attempt.NoNetwork = true;
                    break;
                case WebFailure.HttpStatus when api.StatusCode is 403 or 429:
                    attempt.Fail(InstallText.GitHubRateLimited);
                    break;
                case WebFailure.HttpStatus:
                    attempt.Fail(InstallText.GitHubHttpError(api.StatusCode ?? 0));
                    break;
                default:
                    attempt.Fail(InstallText.ReleaseRefused(RustDeskSource.RefusedResponse));
                    break;
            }

            return null;
        }

        if (!RustDeskSource.TryParseRelease(api.Body ?? string.Empty, out var release, out var refusal))
        {
            attempt.Fail(refusal is null ? InstallText.GitHubNoMsi : InstallText.ReleaseRefused(refusal));
            return null;
        }

        attempt.Version = release!.Tag;
        if (!RustDeskSource.IsAllowedDownloadUrl(release.DownloadUrl, release))
        {
            _log.Warn(Component, $"Adres pobrania spoza GitHuba RustDesk: {release.DownloadUrl.OriginalString}");
            attempt.Fail(InstallText.ReleaseRefused(InstallText.ReasonForeignDownloadUrl));
            return null;
        }

        return release;
    }

    /// <summary>Pobranie pliku z procentem z rozmiaru API (zgłaszany przy zmianie); <c>false</c> = porażka w <paramref name="attempt"/>.</summary>
    private bool Download(RustDeskRelease release, string path, InstallProgressTracker tracker, Attempt attempt)
    {
        WebFile file;
        try
        {
            // Ten sam obiekt Uri, który sprawdziła reguła – bez ponownego składania adresu (review Tasku 1).
            file = _services.Web.Download(
                release.DownloadUrl,
                RustDeskSource.IsAllowedRedirect,
                RustDeskSource.MaxBytes,
                path,
                RustDeskSource.DownloadTimeout,
                bytes => tracker.Percent((int)Math.Max(0, Math.Min(100, bytes * 100 / release.Size))));
        }
        catch (IOException ex)
        {
            // Ruling (Task 3): błąd zapisu pliku (pełny dysk, plik istnieje) to wyjątek, nie WebFailure – porażka
            // tej pozycji, nie brak sieci, więc seria idzie dalej; plik częściowy usuwa finally.
            _log.Fail(Component, $"Nie udało się zapisać pobranego pliku {path}.", ex);
            attempt.Fail(InstallText.DownloadWriteFailed(ex.Message));
            return false;
        }
        if (file.Failure != WebFailure.None)
        {
            _log.Warn(Component, $"Pobranie {release.FileName}: {file.Failure}, kod {file.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}: {file.Error}");
        }

        switch (file.Failure)
        {
            case WebFailure.None when file.Bytes == release.Size:
                return true;
            case WebFailure.None:
                attempt.Fail(InstallText.DownloadCheckFailed(InstallText.ReasonSizeMismatch));
                break;
            case WebFailure.Network:
                attempt.Fail(InstallText.GitHubNoConnection);
                attempt.NoNetwork = true;
                break;
            case WebFailure.TimedOut:
                // M2 final review: własny tekst (wolne łącze), ale seria kończy się jak przy braku sieci (decyzja planu 4).
                attempt.Fail(InstallText.GitHubDownloadTimedOut);
                attempt.NoNetwork = true;
                break;
            case WebFailure.HttpStatus:
                attempt.Fail(InstallText.GitHubHttpError(file.StatusCode ?? 0));
                break;
            case WebFailure.TooLarge:
                attempt.Fail(InstallText.DownloadCheckFailed(RustDeskSource.RefusedSize));
                break;
            default:
                attempt.Fail(InstallText.DownloadCheckFailed(InstallText.ReasonRedirect(file.RefusedUrl?.Host)));
                break;
        }

        return false;
    }

    /// <summary>
    /// SHA256 = <c>digest</c> z API i podpis wydawcy PURSLANE (spec 3.3); <c>null</c> = plik przeszedł, inaczej powód.
    /// <paramref name="signer"/> = podmiot podpisu z tej samej weryfikacji (do logu sukcesu), <c>null</c> przed odczytem podpisu.
    /// </summary>
    private string? CheckFile(string path, RustDeskRelease release, out string? signer)
    {
        signer = null;
        try
        {
            var hash = _services.Files.Sha256(path);
            if (!string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _log.Warn(Component, $"SHA256 pobranego pliku {hash} ≠ {release.Sha256} z GitHuba.");
                return InstallText.ReasonHashMismatch;
            }

            var signature = _services.Signatures.Verify(path);
            signer = signature.SignerSubject;
            switch (signature.Status)
            {
                case SignatureStatus.NotSigned:
                    return InstallText.ReasonNotSigned;
                case SignatureStatus.Invalid:
                    return InstallText.ReasonInvalidSignature(signature.Detail);
                default:
                    return RustDeskSource.IsTrustedSigner(signature.SignerSubject) ? null : InstallText.ReasonOtherPublisher(signature.SignerSubject);
            }
        }
        catch (Exception ex)
        {
            _log.Fail(Component, $"Sprawdzenie pobranego pliku {path} nie powiodło się.", ex);
            return ex.Message;
        }
    }

    /// <summary>Log MSI, <c>msiexec</c> i odczyt zwrotny. <c>true</c> = limit czasu (plik zostaje).</summary>
    private bool RunMsi(InstallTarget target, InstallOptions options, string path, Action<string?, int?> progress, Attempt attempt)
    {
        string logPath;
        try
        {
            logPath = _logs.NewLogPath(RustDeskSource.MsiLogKey, _clock.UtcNow);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, "Nie udało się przygotować pliku logu MSI.", ex);
            attempt.Fail(InstallText.MsiLogPathFailed(ex.Message));
            return false;
        }

        attempt.LogPath = logPath;
        MsiRun run;
        try
        {
            run = _services.Msi.Install(path, logPath, options.PerAppLimit);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, $"Nie udało się uruchomić msiexec dla {target.Id}.", ex);
            attempt.Fail(InstallText.MsiStartFailed(ex.Message));
            return false;
        }

        if (run.TimedOut || run.ExitCode is null)
        {
            // Spec 3.4: jak winget – msiexec działa dalej (nigdy go nie zabijamy), seria się kończy.
            attempt.Outcome = TweakOutcome.NotConfirmed;
            attempt.Message = InstallText.StillRunningInBackground(options.PerAppLimit);
            attempt.TimedOut = true;
            return true;
        }

        var code = run.ExitCode.Value;
        attempt.MsiExitCode = code;
        if (code is not (0 or 3010 or 1641))
        {
            attempt.Fail(InstallText.MsiFailed(code, logPath));
            return false;
        }

        // 3010/1641: sukces z restartem (baner), odczyt zwrotny jak przy 0 – spec 3.4.
        attempt.RequiresReboot = code != 0;
        var matched = _readback.WaitForMatch(target, progress, out var error);
        attempt.Outcome = matched == true ? TweakOutcome.Installed : TweakOutcome.NotConfirmed;
        attempt.Message = matched switch
        {
            true => InstallText.Installed,
            false => InstallText.WithMsiLog(InstallText.MsiNotVisibleAfterSuccess + ".", logPath),
            null => InstallText.WithMsiLog(InstallText.MsiNotVisibleReadFailed(error ?? string.Empty) + ".", logPath),
        };
        return false;
    }

    /// <summary>Usunięcie pobranego pliku; porażka (także wyjątek) = ostrzeżenie w logu i w wyniku pozycji.</summary>
    private void Delete(string path, List<string> warnings)
    {
        bool deleted;
        try
        {
            deleted = _services.Files.TryDelete(path);
        }
        catch (Exception ex)
        {
            _log.Fail(Component, $"Usuwanie pobranego pliku {path} rzuciło wyjątek.", ex);
            deleted = false;
        }

        if (!deleted)
        {
            var warning = InstallText.DownloadNotDeleted(path);
            _log.Warn(Component, warning);
            warnings.Add(warning);
        }
    }

    private InstallStepOutcome Finish(InstallTarget target, Attempt attempt, List<string> warnings, bool ran)
    {
        var result = new TweakResult
        {
            TweakId = target.Id,
            Outcome = attempt.Outcome,
            Message = attempt.Message,
            Warnings = warnings,
            RequiresReboot = attempt.RequiresReboot,
        };
        var line = $"{target.Id} ({InstallText.GitHubJournalMessage(target.GitHub ?? string.Empty, null, null)}): {attempt.Outcome} – {attempt.Message}";
        if (result.IsSuccess)
        {
            _log.Info(Component, line);
        }
        else
        {
            _log.Warn(Component, line);
        }

        var item = new InstallItemReport(
            target.Id,
            null,
            target.Title,
            attempt.Outcome,
            null,
            attempt.Message,
            attempt.LogPath,
            attempt.RequiresReboot,
            attempt.Version,
            InstallOrigin.GitHub,
            target.GitHub,
            attempt.MsiExitCode);
        return new InstallStepOutcome(item, result, ran, attempt.TimedOut, attempt.NoNetwork);
    }

    /// <summary>Stan jednej próby: wynik, tekst, tag wydania, log i kod MSI, restart, limit czasu i brak sieci.</summary>
    private sealed class Attempt
    {
        public Attempt(TweakOutcome outcome, string message)
        {
            Outcome = outcome;
            Message = message;
        }

        public TweakOutcome Outcome { get; set; }

        public string Message { get; set; }

        public string? Version { get; set; }

        public string? LogPath { get; set; }

        public int? MsiExitCode { get; set; }

        public bool RequiresReboot { get; set; }

        public bool TimedOut { get; set; }

        public bool NoNetwork { get; set; }

        public void Fail(string message)
        {
            Outcome = TweakOutcome.Failed;
            Message = message;
        }
    }
}
