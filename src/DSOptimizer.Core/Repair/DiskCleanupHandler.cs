using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Repair;

/// <summary>
/// Handler <c>disk-cleanup</c> (<c>repair.disk-cleanup</c>), mechanizmy z pomiaru VM 26H2 (Task 2, pkt 4): najpierw strażnik
/// trwającej operacji serwisowania i odczyt wolnego miejsca dysku systemowego, potem sześć kroków po kolei:
/// <list type="number">
/// <item>pliki tymczasowe systemu – <c>%SystemRoot%\Temp</c> starsze niż 24 h (<see cref="IRepairFileSystem.DeleteOldFiles"/>);</item>
/// <item>pliki tymczasowe konta – <c>&lt;profil&gt;\AppData\Local\Temp</c> zalogowanego konta (profil z <c>ProfileList</c> po SID);</item>
/// <item>optymalizacja dostarczania – <see cref="DiskCleanupScripts.DeliveryOptimization"/> przez <see cref="IScriptRunner"/>;</item>
/// <item>pobrane aktualizacje – <c>SoftwareDistribution\Download</c> przy zatrzymanych <c>wuauserv</c> i <c>bits</c>
/// (<see cref="ServiceStopRestore"/>, ta sama logika co reset Windows Update), usługi wracają do stanu sprzed;</item>
/// <item>magazyn składników – <c>dism.exe /Online /Cleanup-Image /StartComponentCleanup</c> (bez <c>/ResetBase</c>) przez
/// <see cref="RepairServices.LongTools"/>, poza Job Objectem, bez zabijania;</item>
/// <item>miniatury konta – <c>thumbcache_*.db</c> w <c>&lt;profil&gt;\AppData\Local\Microsoft\Windows\Explorer</c>.</item>
/// </list>
/// Każdy krok kończy się „wykonano”, „pominięto – powód” albo „błąd – powód”; błąd kroku nie zatrzymuje pozostałych. Kroki konta
/// są pomijane, gdy nie wiadomo, kto jest zalogowany (<see cref="IUserContext.IsCertain"/>) albo profil nie ma ścieżki. Wynik:
/// <see cref="TweakOutcome.Executed"/> bez błędów i bez pracy w tle; błąd kroku = <see cref="TweakOutcome.Failed"/> (wygrywa z
/// zatrzymaniem i limitem – Ruling R20); DISM po limicie albo „Zatrzymaj” między krokami = <see cref="TweakOutcome.NotConfirmed"/>
/// (Ruling R10). Kosz, Pobrane, zrzuty pamięci, punkty przywracania i Windows.old nie są ruszane (spec 5.2).
/// </summary>
public sealed class DiskCleanupHandler : IActionHandler
{
    /// <summary>Limit czekania na DISM i skrypt DO, gdy katalog go nie podaje (pomiar Task 2 pkt 6: 1200 s).</summary>
    public static readonly TimeSpan DefaultWaitLimit = TimeSpan.FromSeconds(1200);

    /// <summary>Pliki tymczasowe młodsze niż ten wiek (<c>LastWriteTimeUtc</c>) zostają.</summary>
    public static readonly TimeSpan TempMinimumAge = TimeSpan.FromHours(24);

    public const string ProfileListKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    public const string ThumbnailPrefix = "thumbcache_";

    public const string ThumbnailExtension = ".db";

    public const string SystemTempStage = "Pliki tymczasowe systemu";

    public const string UserTempStage = "Pliki tymczasowe konta";

    public const string DeliveryOptimizationStage = "Optymalizacja dostarczania";

    public const string DownloadStage = "Pobrane aktualizacje";

    public const string ComponentStoreStage = "Magazyn składników";

    public const string ThumbnailsStage = "Miniatury konta";

    private const string Component = "Naprawy";
    private const string DismArguments = "/Online /Cleanup-Image /StartComponentCleanup";
    private const int RebootRequiredCode = 3010;

    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] Stages =
    {
        SystemTempStage, UserTempStage, DeliveryOptimizationStage, DownloadStage, ComponentStoreStage, ThumbnailsStage,
    };

    private static readonly Regex SidPattern = new("^S-1-[0-9-]+$", RegexOptions.CultureInvariant);

    private readonly RepairServices _s;

    public DiskCleanupHandler(RepairServices services)
    {
        _s = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Usługi zatrzymywane na czas usuwania pobranych aktualizacji, w tej kolejności (pomiar Task 2 pkt 4c).</summary>
    public static IReadOnlyList<string> DownloadServices { get; } = new[] { "wuauserv", "bits" };

    public string Name => ActionHandlerNames.DiskCleanup;

    public ActionHandlerResult Run(TweakDefinition tweak, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        if (tweak is null)
        {
            throw new ArgumentNullException(nameof(tweak));
        }

        var refusal = ServicingGuard.Check(_s.Servicing);
        if (refusal is not null)
        {
            return refusal;
        }

        var limit = tweak.Timeout is { } timeout && timeout > TimeSpan.Zero ? timeout : DefaultWaitLimit;
        return new Pass(_s, limit, output, progress, cancellationToken).Run();
    }

    private enum StepKind
    {
        Done,
        Skipped,
        Error,
        Background,
    }

    private sealed record StepOutcome(StepKind Kind, string Text);

    /// <summary>Jeden przebieg czyszczenia (stan kroków, flagi wyniku).</summary>
    private sealed class Pass
    {
        private readonly RepairServices _s;
        private readonly TimeSpan _limit;
        private readonly IProgress<string>? _output;
        private readonly IProgress<ActionProgress>? _progress;
        private readonly CancellationToken _cancellationToken;
        private readonly List<string> _lines = new();
        private readonly List<string> _notRestored = new();
        private bool _profileResolved;
        private string? _profile;
        /// <summary>Powód pominięcia kroków konta; domyślny, gdy <see cref="ResolveProfile"/> przerwał wyjątek (krok konta = błąd, następny = ten powód).</summary>
        private string _profileSkip = "pominięto – nie udało się ustalić profilu konta.";
        private bool _reboot;

        public Pass(RepairServices services, TimeSpan limit, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
        {
            _s = services;
            _limit = limit;
            _output = output;
            _progress = progress;
            _cancellationToken = cancellationToken;
        }

        private string SystemDrive => Path.GetPathRoot(_s.Paths.SystemRoot) is { Length: > 0 } root ? root : @"C:\";

        public ActionHandlerResult Run()
        {
            var before = _s.DiskSpace.FreeBytes(SystemDrive);
            var steps = new Func<StepOutcome>[] { SystemTemp, UserTemp, DeliveryOptimization, DownloadedUpdates, ComponentStore, Thumbnails };
            bool error = false, background = false, stopped = false;
            for (var i = 0; i < Stages.Length; i++)
            {
                var stage = Stages[i];
                StepOutcome outcome;
                if (_cancellationToken.IsCancellationRequested)
                {
                    stopped = true;
                    outcome = new StepOutcome(StepKind.Skipped, "pominięto – zatrzymano.");
                }
                else
                {
                    _progress?.Report(new ActionProgress(stage, null, i, Stages.Length, i + 1 < Stages.Length ? Stages[i + 1] : null));
                    outcome = RunStep(stage, steps[i]);
                }

                error |= outcome.Kind == StepKind.Error;
                background |= outcome.Kind == StepKind.Background;
                var line = $"{stage}: {outcome.Text}";
                _lines.Add(line);
                _output?.Report(line);
                if (outcome.Kind is StepKind.Error or StepKind.Background)
                {
                    _s.Log.Warn(Component, line);
                }
                else
                {
                    _s.Log.Info(Component, line);
                }
            }

            var after = _s.DiskSpace.FreeBytes(SystemDrive);
            var header = $"Zwolniono: {DiskSpaceText.Freed(before, after)}.";
            var result = error
                ? TweakOutcome.Failed
                : background || stopped ? TweakOutcome.NotConfirmed : TweakOutcome.Executed;
            // Surowe odczyty do diagnozy (komunikat podaje tylko uczciwy tekst z DiskSpaceText).
            _s.Log.Info(Component, $"disk-cleanup: {header} Wolne {SystemDrive} przed: {Bytes(before)}, po: {Bytes(after)}. Wynik: {result}.");
            return new ActionHandlerResult
            {
                Outcome = result,
                RequiresReboot = result == TweakOutcome.Executed && _reboot,
                Message = string.Join(Environment.NewLine, new[] { header }.Concat(_lines)),
                NotRestored = _notRestored.ToArray(),
            };
        }

        private StepOutcome RunStep(string stage, Func<StepOutcome> step)
        {
            try
            {
                return step();
            }
            catch (OperationException ex)
            {
                return Error(ex.Message);
            }
#pragma warning disable CA1031 // Błąd jednego kroku nie może przerwać pozostałych (spec: „pozostałe kroki i tak się wykonują”).
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _s.Log.Fail(Component, $"disk-cleanup: nieoczekiwany błąd kroku „{stage}”.", ex);
                return Error(ex.Message);
            }
        }

        // ---- kroki ----

        private StepOutcome SystemTemp() => CleanOld(Path.Combine(_s.Paths.SystemRoot, "Temp"), TempThreshold());

        private StepOutcome UserTemp()
            => ResolveProfile() is { } profile
                ? CleanOld(Path.Combine(profile, "AppData", "Local", "Temp"), TempThreshold())
                : Skipped(_profileSkip);

        private StepOutcome DeliveryOptimization()
        {
            var result = _s.Scripts.Run(
                new ScriptRequest { Body = DiskCleanupScripts.DeliveryOptimization, UserSid = _s.User.Sid, Timeout = _limit },
                _output,
                CancellationToken.None);
            if (result.ExitCode != 0)
            {
                var errors = string.Join(" ", result.Errors.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()));
                return Error(errors.Length > 0 ? errors : $"skrypt zakończył się kodem {result.ExitCode}");
            }

            return result.LastLine switch
            {
                // Bez odczytu zwrotnego rozmiaru pamięci podręcznej: jedynym dowodem jest brak błędu cmdletu (final review, drobne 5).
                DiskCleanupScripts.DoneMarker => Done("wykonano – polecenie zakończyło się bez błędu (pliki pobierane w tej chwili zostają)."),
                DiskCleanupScripts.MissingMarker => Skipped("pominięto – w tym systemie nie ma polecenia Delete-DeliveryOptimizationCache."),
                _ => Error("skrypt nie potwierdził wyczyszczenia pamięci podręcznej (brak znacznika wyniku)"),
            };
        }

        private StepOutcome DownloadedUpdates()
        {
            var path = Path.Combine(_s.Paths.SystemRoot, "SoftwareDistribution", "Download");
            if (!_s.Files.DirectoryExists(path))
            {
                return Skipped($"pominięto – brak folderu {path}.");
            }

            var running = new List<string>();
            foreach (var name in DownloadServices)
            {
                var state = _s.Services.Query(name);
                if (state.Status == ReadStatus.Found)
                {
                    if (state.IsRunning)
                    {
                        running.Add(name);
                    }
                }
                else if (state.Status != ReadStatus.NotFound)
                {
                    return Skipped($"pominięto – nie udało się odczytać usługi {name} ({Clean(state.Error)}); nic nie zatrzymano.");
                }
            }

            var stopper = new ServiceStopRestore(_s.Services, _s.Log, Component, ServiceTimeout, Attempt);
            string? stopFailure = null;
            foreach (var name in running)
            {
                var stopError = stopper.Stop(name, withDependents: false, out _);
                if (stopError is not null)
                {
                    stopFailure = $"usługa {name} nie zatrzymała się ({Clean(stopError)})";
                    break;
                }
            }

            OldFilesCleanup cleaned = OldFilesCleanup.None;
            var failure = stopFailure is null ? Attempt(() => cleaned = _s.Files.DeleteOldFiles(path, DateTime.MaxValue)) : null;
            var restored = stopper.Restore();
            foreach (var notRunning in restored.Failed)
            {
                _notRestored.Add($"Usługa {notRunning.Name} nie działa po przywróceniu: {Clean(notRunning.Reason)}.");
            }

            if (restored.Failed.Count > 0)
            {
                var names = string.Join(", ", restored.Failed.Select(f => f.Name));
                var started = restored.Started.Count > 0 ? $"; uruchomiono ponownie: {string.Join(", ", restored.Started)}" : string.Empty;
                var what = stopFailure ?? (failure is not null ? Clean(failure) : cleaned.SkipReason is { } skip ? $"pominięto – {Clean(skip)}" : $"usunięto pliki ({Counts(cleaned)})");
                var joint = stopFailure is null && failure is null && cleaned.SkipReason is null ? ", ale" : ", a potem";
                return Error($"{what}{joint} nie udało się ponownie uruchomić usług: {names}{started}", raw: true);
            }

            if (stopFailure is not null)
            {
                return Skipped($"pominięto – {stopFailure}; usługi przywrócono.");
            }

            var restartedTail = restored.Started.Count > 0 ? $"; usługi uruchomiono ponownie: {string.Join(", ", restored.Started)}" : string.Empty;
            if (failure is not null)
            {
                return Error($"{Clean(failure)}{restartedTail}", raw: true);
            }

            return cleaned.SkipReason is { } reason
                ? Skipped($"pominięto – {Clean(reason)}{restartedTail}.")
                : Done($"wykonano ({Counts(cleaned)}){restartedTail}.");
        }

        private StepOutcome ComponentStore()
        {
            const string Stage = ComponentStoreStage;
            const string Next = ThumbnailsStage;
            int? lastPercent = null;
            void OnLine(string line)
            {
                _output?.Report(line);
                var percent = ServicingProgressParser.TryParsePercent(line);
                if (percent is null)
                {
                    _s.Log.Info(Component, $"{Stage}: {line}");
                }
                else if (percent != lastPercent)
                {
                    lastPercent = percent;
                    _progress?.Report(new ActionProgress(Stage, percent, 4, Stages.Length, Next));
                }
            }

            _s.Log.Info(Component, $"disk-cleanup: dism.exe {DismArguments} (limit {SystemFilesHandler.Duration(_limit)})");
            ServicingToolRun run;
            try
            {
                run = _s.LongTools.Run(new ToolRequest { FileName = "dism.exe", Arguments = DismArguments, Timeout = _limit }, _limit, OnLine);
            }
            catch (OperationException ex)
            {
                return Error($"nie udało się uruchomić DISM: {Clean(ex.Message)}");
            }

            if (run.TimedOut)
            {
                return new StepOutcome(
                    StepKind.Background,
                    $"nadal działa w tle po {SystemFilesHandler.Duration(_limit)} – program przestał czekać, a DISM pracuje dalej. Wynik sprawdzisz w {SystemFilesHandler.DismLogText}.");
            }

            if (run.ExitCode is not { } code)
            {
                return Error($"DISM nie zwrócił kodu wyjścia. Szczegóły: {SystemFilesHandler.DismLogText}");
            }

            if (code == 0)
            {
                return Done("wykonano (DISM /StartComponentCleanup, kod 0).");
            }

            if (code == RebootRequiredCode)
            {
                _reboot = true;
                return Done("wykonano – dokończy się po ponownym uruchomieniu komputera (kod 3010).");
            }

            return new StepOutcome(StepKind.Error, $"błąd {DismOutcome.Hex(code)} – szczegóły: {SystemFilesHandler.DismLogText}.");
        }

        private StepOutcome Thumbnails()
        {
            if (ResolveProfile() is not { } profile)
            {
                return Skipped(_profileSkip);
            }

            var path = Path.Combine(profile, "AppData", "Local", "Microsoft", "Windows", "Explorer");
            if (!_s.Files.DirectoryExists(path))
            {
                return Skipped($"pominięto – brak folderu {path}.");
            }

            return Cleaned(_s.Files.DeleteFilesByName(path, ThumbnailPrefix, ThumbnailExtension));
        }

        // ---- pomocnicze ----

        private DateTime TempThreshold() => _s.Clock.UtcNow.UtcDateTime - TempMinimumAge;

        private StepOutcome CleanOld(string path, DateTime olderThanUtc)
        {
            if (!_s.Files.DirectoryExists(path))
            {
                return Skipped($"pominięto – brak folderu {path}.");
            }

            return Cleaned(_s.Files.DeleteOldFiles(path, olderThanUtc));
        }

        /// <summary>„wykonano (…)”, a folder pominięty przez system plików (link, ścieżka przez link) = „pominięto – &lt;powód&gt;.”.</summary>
        private static StepOutcome Cleaned(OldFilesCleanup result)
            => result.SkipReason is { } reason ? Skipped($"pominięto – {Clean(reason)}.") : Done($"wykonano ({Counts(result)}).");

        /// <summary>
        /// Profil zalogowanego konta z <c>ProfileList\&lt;SID&gt;\ProfileImagePath</c> (raz na przebieg). <c>null</c> = kroki konta
        /// pomijane z powodem w <see cref="_profileSkip"/>: niepewne konto (Review Focus 5), zły SID, brak wartości, ścieżka nielokalna.
        /// </summary>
        private string? ResolveProfile()
        {
            if (_profileResolved)
            {
                return _profile;
            }

            _profileResolved = true;
            if (!_s.User.IsCertain)
            {
                _profileSkip = "pominięto – nie udało się ustalić zalogowanego konta (brak sesji interaktywnej); program nie czyści zamiast niego własnego konta.";
                return null;
            }

            var sid = _s.User.Sid;
            if (string.IsNullOrWhiteSpace(sid) || !SidPattern.IsMatch(sid))
            {
                _profileSkip = $"pominięto – nieprawidłowy identyfikator zalogowanego konta ({sid}).";
                return null;
            }

            var read = _s.Registry.Read($@"{ProfileListKey}\{sid}", "ProfileImagePath");
            if (read.Status == ReadStatus.Found && read.Value.Data is string raw && !string.IsNullOrWhiteSpace(raw))
            {
                var path = Environment.ExpandEnvironmentVariables(raw.Trim()).TrimEnd('\\');
                if (IsLocalAbsoluteDirectory(path))
                {
                    _profile = path;
                    return path;
                }

                _profileSkip = $"pominięto – ścieżka profilu konta {sid} nie jest pełną ścieżką lokalną ({raw.Trim()}).";
                return null;
            }

            _profileSkip = read.Status is ReadStatus.Found or ReadStatus.NotFound
                ? $"pominięto – brak ścieżki profilu konta {sid} w rejestrze (ProfileList)."
                : $"pominięto – nie udało się odczytać ścieżki profilu konta {sid} ({Clean(read.Error)}).";
            return null;
        }

        /// <summary>Pełna ścieżka na dysku lokalnym (<c>X:\folder…</c>), bez UNC, bez segmentów <c>.</c>/<c>..</c> i bez znaków niedozwolonych.</summary>
        private static bool IsLocalAbsoluteDirectory(string path)
        {
            if (path.Length < 4 || !char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return false;
            }

            return path.Substring(3).Split('\\', '/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
        }

        private static string Bytes(long? value) => value is { } bytes ? bytes.ToString(CultureInfo.InvariantCulture) + " B" : "brak odczytu";

        private static string Counts(OldFilesCleanup result)
        {
            var text = $"plików: {result.Deleted}, {DiskSpaceText.Size(result.FreedBytes)}";
            if (result.SkippedYoung > 0)
            {
                text += $"; młodszych niż 24 h zostało: {result.SkippedYoung}";
            }

            if (result.SkippedInUse > 0)
            {
                text += $"; w użyciu zostało: {result.SkippedInUse}";
            }

            return text;
        }

        /// <summary>Wywołanie zwracające komunikat błędu zamiast wyjątku (dla <see cref="ServiceStopRestore"/> i usuwania przy zatrzymanych usługach).</summary>
        private string? Attempt(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (OperationException ex)
            {
                return ex.Message;
            }
#pragma warning disable CA1031 // Każdy błąd przy zatrzymanych usługach ma skończyć się ich przywróceniem, a nie wyjątkiem w połowie kroku.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _s.Log.Fail(Component, "disk-cleanup: nieoczekiwany błąd przy zatrzymanych usługach Windows Update.", ex);
                return ex.Message;
            }
        }

        private static string Clean(string? text)
        {
            var trimmed = (text ?? string.Empty).Trim().TrimEnd('.');
            return trimmed.Length == 0 ? "nieznany błąd" : trimmed;
        }

        private static StepOutcome Done(string text) => new(StepKind.Done, text);

        private static StepOutcome Skipped(string text) => new(StepKind.Skipped, text);

        /// <summary>„błąd – &lt;powód&gt;.”; <paramref name="raw"/> = powód już oczyszczony (bez obcinania kropek w środku).</summary>
        private static StepOutcome Error(string reason, bool raw = false) => new(StepKind.Error, $"błąd – {(raw ? reason : Clean(reason))}.");
    }
}
