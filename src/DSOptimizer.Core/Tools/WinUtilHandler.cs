using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Tools;

/// <summary>
/// Handler <c>winutil</c> (Etap 5, spec 3.3): drugi wyjątek od zasady „program sam niczego nie pobiera” (pierwszy: RustDesk) –
/// jeden plik (<c>winutil.ps1</c>), jedno repo (<c>ChrisTitusTech/winutil</c>), jedno źródło (<c>releases/latest</c>), reguły w
/// <see cref="WinUtilSource"/>, nie w JSON. Przebieg: API → reguły wydania → adres pobrania → pobranie do folderu pobrań z kontrolą
/// przekierowań i rozmiaru → SHA256 pliku na dysku = <c>digest</c> z API, liczony tuż przed startem → <c>powershell.exe -File</c>
/// przez <see cref="IProcessLauncher"/> (poza Job Objectem, bez czekania). Każda porażka po utworzeniu ścieżki usuwa
/// plik; po starcie plik zostaje (K4: PowerShell czyta go po uruchomieniu; usuwa go sprzątanie osieroconych pobrań przy kolejnym
/// pobraniu – WinUtil albo RustDesk – gdy ma ponad 24 h).
/// SHA256 potwierdza tylko zgodność z wydaniem GitHuba, nie bezpieczeństwo kodu – treści skryptu handler nie ocenia.
/// </summary>
public sealed class WinUtilHandler : IActionHandler
{
    public static string NoConnection => L.T("Nie wykonano – brak połączenia z GitHubem.");

    public static string RateLimited => L.T("Nie wykonano – GitHub chwilowo odmawia (limit zapytań).");

    public static string UnexpectedResponse => L.T("Nie wykonano – GitHub zwrócił nieoczekiwaną odpowiedź.");

    public static string NoAsset => L.T("Nie wykonano – najnowsze wydanie WinUtil nie ma pliku winutil.ps1.");

    public static string CheckFailed => L.T("Nie wykonano – pobrany plik nie przeszedł sprawdzenia.");

    private const string Component = "WinUtil";

    public WinUtilHandler(WinUtilServices services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string Name => ActionHandlerNames.WinUtil;

    /// <summary>Usługi handlera (test składania sprawdza, że są współdzielone z instalacją z GitHuba i silnikiem).</summary>
    public WinUtilServices Services { get; }

    public static string Refused(string refusal) => L.F("Nie wykonano – {0}.", refusal);

    public static string LaunchFailed(string message) => L.F("Nie wykonano – nie udało się otworzyć okna PowerShell: {0}.", TrimDot(message));

    public static string DownloadFolderFailed(string message) => L.F("Nie wykonano – nie udało się przygotować folderu pobrań ({0}).", TrimDot(message));

    public static string Executed(string tag) => L.F("Otwarto okno WinUtil {0} (plik z wydania GitHuba, SHA256 zgodny).", tag);

    /// <summary>Jedyna linia postępu: przed pobraniem pliku (po udanym odczycie wydania).</summary>
    public static string Downloading(string tag) => L.F("Pobieranie WinUtil {0} z GitHuba…", tag);

    public ActionHandlerResult Run(TweakDefinition tweak, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        var release = FetchRelease(out var failure);
        if (release is null)
        {
            return Failed(failure!);
        }

        string path;
        try
        {
            path = Services.Files.NewDownloadPath(".ps1");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Services.Log.Fail(Component, L.T("Nie udało się przygotować folderu pobrań."), ex);
            return Failed(DownloadFolderFailed(ex.Message));
        }

        // Wynik porażki dostaje tę samą listę ostrzeżeń, którą finally uzupełnia o nieudane usunięcie pliku.
        var launched = false;
        var warnings = new List<string>();
        try
        {
            output?.Report(Downloading(release.Tag));
            if (Download(release, path, out var bytes) is { } downloadFailure)
            {
                return Failed(downloadFailure, warnings);
            }

            if (!HashMatches(path, release))
            {
                return Failed(CheckFailed, warnings);
            }

            Services.Log.Info(
                Component,
                L.F("Pobrano i sprawdzono {0} ({1}): {2}, {3} B, SHA256 {4}", WinUtilSource.FileName, release.Tag, path, bytes.ToString(CultureInfo.InvariantCulture), release.Sha256));
            try
            {
                Services.Launcher.Launch(new LaunchSpec { File = "powershell.exe", Arguments = WinUtilSource.LaunchArguments(path) });
            }
            catch (OperationException ex)
            {
                Services.Log.Warn(Component, L.F("Nie udało się otworzyć okna PowerShell: {0}", ex.Message));
                return Failed(LaunchFailed(ex.Message), warnings);
            }

            launched = true;
            return new ActionHandlerResult { Outcome = TweakOutcome.Executed, Message = Executed(release.Tag) };
        }
        finally
        {
            if (!launched)
            {
                Delete(path, warnings);
            }
        }
    }

    /// <summary>Odpowiedź <c>releases/latest</c>, reguły wydania i adres pobrania; <c>null</c> = porażka w <paramref name="failure"/>.</summary>
    private WinUtilRelease? FetchRelease(out string? failure)
    {
        var api = Services.Web.GetText(WinUtilSource.ReleasesLatestUrl, RustDeskSource.ApiAccept, WinUtilSource.ApiTimeout);
        if (api.Failure != WebFailure.None)
        {
            Services.Log.Warn(Component, L.F("GitHub API: {0}, kod {1}: {2}", api.Failure, api.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "-", api.Error));
            failure = WebFailureText(api.Failure, api.StatusCode);
            return null;
        }

        if (!WinUtilSource.TryParseRelease(api.Body ?? string.Empty, out var release, out var refusal))
        {
            failure = refusal is null ? NoAsset : Refused(refusal);
            return null;
        }

        if (!WinUtilSource.IsAllowedDownloadUrl(release!.DownloadUrl, release))
        {
            // Nic jeszcze nie pobrano, więc to nieoczekiwana odpowiedź API, nie „pobrany plik”.
            Services.Log.Warn(Component, L.F("Adres pobrania spoza wydań ChrisTitusTech/winutil: {0}", release.DownloadUrl.OriginalString));
            failure = UnexpectedResponse;
            return null;
        }

        failure = null;
        return release;
    }

    /// <summary>Pobranie pliku tym samym obiektem <see cref="Uri"/>, który sprawdziła reguła; <c>null</c> = sukces.</summary>
    private string? Download(WinUtilRelease release, string path, out long bytes)
    {
        bytes = 0;
        WebFile file;
        try
        {
            file = Services.Web.Download(release.DownloadUrl, WinUtilSource.IsAllowedRedirect, WinUtilSource.MaxBytes, path, WinUtilSource.DownloadTimeout, null);
        }
        catch (IOException ex)
        {
            // Jak w GitHubInstallStep: błąd zapisu pliku to wyjątek, nie WebFailure; plik częściowy usuwa finally.
            Services.Log.Fail(Component, L.F("Nie udało się zapisać pobranego pliku {0}.", path), ex);
            return InstallText.DownloadWriteFailed(ex.Message) + ".";
        }

        if (file.Failure == WebFailure.None)
        {
            bytes = file.Bytes;
            return null;
        }

        Services.Log.Warn(Component, L.F("Pobranie {0}: {1}, kod {2}: {3}", WinUtilSource.FileName, file.Failure, file.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "-", file.Error));
        return file.Failure == WebFailure.TooLarge ? CheckFailed : WebFailureText(file.Failure, file.StatusCode);
    }

    /// <summary>SHA256 pliku na dysku (tuż przed startem, Review Focus 2) = <c>digest</c> z API; błąd odczytu = niezgodność.</summary>
    private bool HashMatches(string path, WinUtilRelease release)
    {
        string hash;
        try
        {
            hash = Services.Files.Sha256(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Services.Log.Fail(Component, L.F("Nie udało się policzyć SHA256 pliku {0}.", path), ex);
            return false;
        }

        if (string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Services.Log.Warn(Component, L.F("SHA256 pobranego pliku {0} ≠ {1} z GitHuba.", hash, release.Sha256));
        return false;
    }

    private static string WebFailureText(WebFailure failure, int? status)
        => failure switch
        {
            WebFailure.Network or WebFailure.TimedOut => NoConnection,
            WebFailure.HttpStatus when status is 403 or 429 => RateLimited,
            _ => UnexpectedResponse,
        };

    /// <summary>Usunięcie pobranego pliku; porażka (także wyjątek) = ostrzeżenie w logu i w wyniku.</summary>
    private void Delete(string path, List<string> warnings)
    {
        bool deleted;
        try
        {
            deleted = Services.Files.TryDelete(path);
        }
        catch (Exception ex)
        {
            Services.Log.Fail(Component, L.F("Usuwanie pobranego pliku {0} rzuciło wyjątek.", path), ex);
            deleted = false;
        }

        if (!deleted)
        {
            var warning = InstallText.DownloadNotDeleted(path);
            Services.Log.Warn(Component, warning);
            warnings.Add(warning);
        }
    }

    private static ActionHandlerResult Failed(string message, IReadOnlyList<string>? warnings = null)
        => new() { Outcome = TweakOutcome.Failed, Message = message, Warnings = warnings ?? Array.Empty<string>() };

    private static string TrimDot(string message) => message.Trim().TrimEnd('.');
}
