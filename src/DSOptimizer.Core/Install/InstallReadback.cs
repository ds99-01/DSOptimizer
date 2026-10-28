using System;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Install;

/// <summary>
/// Odczyt zwrotny pozycji listy (reguła <c>match</c>) wspólny dla winget i GitHuba: jeden świeży odczyt albo okno
/// <see cref="InstallTimeouts.Readback"/> po sukcesie instalatora (Ruling R16). Teksty wyniku składa wywołujący.
/// </summary>
internal sealed class InstallReadback
{
    private const string Component = "Install";

    private readonly IInstalledAppsReader _reader;
    private readonly IUserContext _user;
    private readonly ILog _log;
    private readonly IClock _clock;
    private readonly Action<TimeSpan> _sleep;

    public InstallReadback(IInstalledAppsReader reader, IUserContext user, ILog log, IClock clock, Action<TimeSpan> sleep)
    {
        _reader = reader;
        _user = user;
        _log = log;
        _clock = clock;
        _sleep = sleep;
    }

    /// <summary>Świeży odczyt listy zainstalowanych i reguła <c>match</c> (pozycja musi ją mieć); błąd odczytu = <c>null</c>.</summary>
    public bool? IsMatched(InstallTarget target, out string? error) => IsMatched(target, out error, null);

    /// <summary>
    /// Ruling R16: wpis Uninstall bywa zapisany chwilę po wyjściu instalatora (Discord) – odczyt co
    /// <see cref="InstallTimeouts.ReadbackInterval"/> do <see cref="InstallTimeouts.Readback"/>, najwyżej
    /// <see cref="InstallTimeouts.ReadbackAttempts"/> razy; gdy pierwszy odczyt nie pasuje, etap „Sprawdzanie instalacji”.
    /// Błąd odczytu w trakcie = jeszcze nie widać; dopiero ostatni odczyt decyduje (<c>null</c> = błąd, <paramref name="error"/>).
    /// Limit prób obok zegara: zamrożony albo cofnięty zegar systemowy nie zawiesi serii (M3 final review).
    /// </summary>
    public bool? WaitForMatch(InstallTarget target, Action<string?, int?> progress, out string? error)
    {
        var start = _clock.UtcNow;
        var failures = new ReadFailures();
        bool? matched;
        for (var attempt = 1; ; attempt++)
        {
            matched = IsMatched(target, out error, failures);
            if (matched == true || attempt >= InstallTimeouts.ReadbackAttempts || _clock.UtcNow - start >= InstallTimeouts.Readback)
            {
                return matched;
            }

            if (attempt == 1)
            {
                progress(InstallText.StageVerifying, null);
            }

            _sleep(InstallTimeouts.ReadbackInterval);
        }
    }

    /// <summary>
    /// W oknie odczytu zwrotnego (<paramref name="failures"/>) tylko pierwszy błąd idzie do logu ze stosem, kolejne jako
    /// ostrzeżenie bez stosu i tylko przy zmianie treści – bez ~60 stosów w logu.
    /// </summary>
    private bool? IsMatched(InstallTarget target, out string? error, ReadFailures? failures)
    {
        try
        {
            error = null;
            return _reader.Read(_user.Sid).Apps.Any(target.Match!.Matches);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (failures is null || !failures.Logged)
            {
                _log.Fail(Component, L.F("Odczyt listy zainstalowanych dla {0} nie powiódł się.", target.Id), ex);
            }
            else if (!string.Equals(failures.LastMessage, ex.Message, StringComparison.Ordinal))
            {
                _log.Warn(Component, L.F("Odczyt listy zainstalowanych dla {0} nadal się nie udaje: {1}", target.Id, ex.Message));
            }

            if (failures is not null)
            {
                failures.Logged = true;
                failures.LastMessage = ex.Message;
            }

            return null;
        }
    }

    /// <summary>Stan błędów odczytu w jednym oknie odczytu zwrotnego (do ograniczenia logu).</summary>
    private sealed class ReadFailures
    {
        public bool Logged { get; set; }

        public string? LastMessage { get; set; }
    }
}
