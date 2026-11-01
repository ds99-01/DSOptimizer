using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Updates;

public enum UpdateCheckOutcome
{
    UpToDate,

    NewerAvailable,

    Failed,
}

/// <summary>Wynik sprawdzenia; <see cref="LatestVersion"/> tylko przy <see cref="UpdateCheckOutcome.NewerAvailable"/>, zawsze w postaci <c>X.Y.Z</c> zbudowanej z liczb.</summary>
public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, string? LatestVersion);

/// <summary>
/// Pyta GitHuba o najnowsze stabilne wydanie i porównuje numer z bieżącą wersją. Metoda <see cref="Check"/> blokuje wątek (sieć),
/// więc woła się ją tylko przez <c>Task.Run</c>; nigdy nie rzuca. Do interfejsu i logu nie trafia nic z odpowiedzi poza zwalidowanym numerem.
/// </summary>
public sealed class UpdateChecker
{
    private static readonly UpdateCheckResult FailedResult = new(UpdateCheckOutcome.Failed, null);
    private static readonly UpdateCheckResult UpToDateResult = new(UpdateCheckOutcome.UpToDate, null);

    private readonly IWebDownloader _web;
    private readonly string _currentVersion;
    private readonly ILog _log;

    public UpdateChecker(IWebDownloader web, string currentVersion, ILog log)
    {
        _web = web;
        _currentVersion = currentVersion;
        _log = log;
    }

    public UpdateCheckResult Check()
    {
        try
        {
            return CheckCore();
        }
        catch (Exception)
        {
            // Porażka nigdy nie wpływa na program, także gdy zawiódł sam log.
            return FailedResult;
        }
    }

    private UpdateCheckResult CheckCore()
    {
        if (!Version.TryParse(_currentVersion, out var current))
        {
            return Fail(L.T("bieżąca wersja programu nie jest poprawnym numerem"));
        }

        var response = _web.GetText(UpdateSource.ReleasesLatestUrl, RustDeskSource.ApiAccept, UpdateSource.ApiTimeout);
        if (response.Failure != WebFailure.None)
        {
            var reason = response.Failure.ToString();
            return Fail(response.StatusCode is { } status ? reason + " " + status : reason);
        }

        switch (UpdateSource.Read(response.Body, out var latest))
        {
            case ReleaseReading.Skipped:
                return UpToDateResult;
            case ReleaseReading.Stable when latest is not null:
                return Compare(current, latest);
            default:
                return Fail("InvalidResponse");
        }
    }

    private static UpdateCheckResult Compare(Version current, Version latest)
    {
        var currentTriple = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        var latestTriple = new Version(latest.Major, latest.Minor, latest.Build);
        return latestTriple > currentTriple
            ? new UpdateCheckResult(UpdateCheckOutcome.NewerAvailable, latestTriple.ToString(3))
            : UpToDateResult;
    }

    private UpdateCheckResult Fail(string reason)
    {
        _log.Info(L.T("Aktualizacje"), L.F("Nie udało się sprawdzić nowej wersji: {0}", reason));
        return FailedResult;
    }
}