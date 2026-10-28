using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Repair;

/// <summary>
/// Handler <c>system-files</c> (<c>repair.system-files</c>): najpierw strażnik trwającej operacji serwisowania, potem
/// krok 1 z 2 <c>DISM /Online /Cleanup-Image /RestoreHealth</c> i krok 2 z 2 <c>sfc /scannow</c> przez
/// <see cref="RepairServices.LongTools"/> (poza Job Objectem, bez zabijania). Werdykty z kodów wyjścia i segmentów CBS.log od
/// offsetu sprzed startu narzędzia (<see cref="DismOutcome"/>, <see cref="SfcOutcome"/>). Po porażce DISM SFC i tak startuje
/// (spec 5.1). Po limicie DISM program przestaje czekać, a SFC nie startuje; „Zatrzymaj” działa tylko między krokami
/// i nigdy nie daje <see cref="TweakOutcome.Executed"/> (Ruling R10).
/// </summary>
public sealed class SystemFilesHandler : IActionHandler
{
    /// <summary>Limit na jedno narzędzie, gdy katalog go nie podaje (pomiar Task 2 pkt 6: 2700 s).</summary>
    public static readonly TimeSpan DefaultWaitLimit = TimeSpan.FromSeconds(2700);

    public const string DismStage = "DISM";

    public const string SfcStage = "SFC";

    public const string DismLogText = @"%SystemRoot%\Logs\DISM\dism.log";

    public const string CbsLogText = @"%SystemRoot%\Logs\CBS\CBS.log";

    public const string RebootPendingKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";

    private static string Component => L.T("Naprawy");
    private const int StepCount = 2;
    private const string DismArguments = "/Online /Cleanup-Image /RestoreHealth";
    private const string SfcArguments = "/scannow";

    private readonly RepairServices _s;

    public SystemFilesHandler(RepairServices services)
    {
        _s = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string Name => ActionHandlerNames.SystemFiles;

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
        var cbsPath = Path.Combine(_s.Paths.SystemRoot, "Logs", "CBS", "CBS.log");

        // Krok 1 z 2: DISM.
        var dism = RunTool(DismStage, 0, SfcStage, "dism.exe", DismArguments, limit, cbsPath, output, progress);
        if (dism.TimedOut)
        {
            return Result(
                TweakOutcome.NotConfirmed,
                false,
                L.F("DISM nadal działa w tle po {0} – program przestał czekać. Wynik sprawdzisz w {1}.", Duration(limit), DismLogText),
                L.T("SFC nie został uruchomiony (DISM jeszcze pracuje)."));
        }

        DismOutcome? dismOutcome = null;
        string dismLine;
        if (dism.StartError is { } dismError)
        {
            dismLine = L.F("DISM: nie udało się uruchomić – {0}", dismError);
        }
        else
        {
            dismOutcome = DismOutcome.Classify(dism.ExitCode, dism.Segment);
            dismLine = DismText(dismOutcome);
        }

        var dismOk = dismOutcome?.IsSuccess == true;
        if (cancellationToken.IsCancellationRequested)
        {
            _s.Log.Info(Component, L.T("system-files: zatrzymano między krokami – SFC pominięty."));
            return Result(dismOk ? TweakOutcome.NotConfirmed : TweakOutcome.Failed, false, dismLine, L.T("SFC pominięty – zatrzymano."));
        }

        // Krok 2 z 2: SFC.
        var rebootBefore = _s.Registry.KeyStatus(RebootPendingKey);
        var sfc = RunTool(SfcStage, 1, null, "sfc.exe", SfcArguments, limit, cbsPath, output, progress);
        if (sfc.TimedOut)
        {
            return Result(
                dismOk ? TweakOutcome.NotConfirmed : TweakOutcome.Failed,
                false,
                dismLine,
                L.F("SFC nadal działa w tle po {0} – program przestał czekać. Wynik sprawdzisz w {1}.", Duration(limit), CbsLogText));
        }

        if (sfc.StartError is { } sfcError)
        {
            return Result(TweakOutcome.Failed, false, dismLine, L.F("SFC: nie udało się uruchomić – {0}", sfcError));
        }

        var appeared = rebootBefore == ReadStatus.NotFound && _s.Registry.KeyStatus(RebootPendingKey) == ReadStatus.Found;
        var sfcOutcome = SfcOutcome.Classify(sfc.ExitCode, sfc.Segment, appeared);
        var outcome = !dismOk || sfcOutcome.Verdict == SfcVerdict.NotAllRepaired
            ? TweakOutcome.Failed
            : sfcOutcome.IsSuccess ? TweakOutcome.Executed : TweakOutcome.NotConfirmed;
        var reboot = outcome == TweakOutcome.Executed
            && (dismOutcome!.Verdict == DismVerdict.RebootRequired || sfcOutcome.Verdict == SfcVerdict.RepairPendingReboot);
        return Result(outcome, reboot, dismLine, SfcText(sfcOutcome));
    }

    private ToolStep RunTool(
        string stage,
        int stepIndex,
        string? nextStage,
        string fileName,
        string arguments,
        TimeSpan limit,
        string cbsPath,
        IProgress<string>? output,
        IProgress<ActionProgress>? progress)
    {
        progress?.Report(new ActionProgress(stage, null, stepIndex, StepCount, nextStage));
        var offset = CbsOffset(cbsPath);
        _s.Log.Info(Component, $"system-files: {fileName} {arguments} (limit {Duration(limit)})");
        var started = _s.Clock.UtcNow;
        int? lastPercent = null;
        void OnLine(string line)
        {
            output?.Report(line);
            var percent = ServicingProgressParser.TryParsePercent(line);
            if (percent is null)
            {
                // Paski postępu (dziesiątki linii) zostają w oknie; do logu idą zdania narzędzia.
                _s.Log.Info(Component, $"{stage}: {line}");
            }
            else if (percent != lastPercent)
            {
                lastPercent = percent;
                progress?.Report(new ActionProgress(stage, percent, stepIndex, StepCount, nextStage));
            }
        }

        ServicingToolRun run;
        try
        {
            run = _s.LongTools.Run(new ToolRequest { FileName = fileName, Arguments = arguments, Timeout = limit }, limit, OnLine);
        }
        catch (OperationException ex)
        {
            _s.Log.Warn(Component, L.F("system-files: {0} nie wystartował: {1}", fileName, ex.Message));
            return new ToolStep(false, 0, null, ex.Message);
        }

        var seconds = (_s.Clock.UtcNow - started).TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        if (run.TimedOut || run.ExitCode is not { } code)
        {
            _s.Log.Warn(Component, L.F("system-files: {0} po limicie {1} dalej działa w tle – program przestał czekać.", fileName, Duration(limit)));
            return new ToolStep(true, 0, null, null);
        }

        _s.Log.Info(Component, L.F("system-files: {0} zakończony kodem {1} po {2} s.", fileName, DismOutcome.Hex(code), seconds));
        return new ToolStep(false, code, ReadSegment(cbsPath, offset), null);
    }

    /// <summary>Długość CBS.log przed startem narzędzia; brak pliku = 0 (nowy plik), błąd odczytu = <c>null</c> (segment nieznany).</summary>
    private long? CbsOffset(string cbsPath)
    {
        try
        {
            return _s.Files.FileLength(cbsPath) ?? 0;
        }
        catch (OperationException ex)
        {
            _s.Log.Warn(Component, L.F("system-files: nie można odczytać rozmiaru {0}: {1}", cbsPath, ex.Message));
            return null;
        }
    }

    private string? ReadSegment(string cbsPath, long? offset)
    {
        if (offset is not { } start)
        {
            return null;
        }

        try
        {
            return _s.Files.ReadSharedFrom(cbsPath, start);
        }
        catch (OperationException ex)
        {
            _s.Log.Warn(Component, L.F("system-files: nie można odczytać {0}: {1}", cbsPath, ex.Message));
            return null;
        }
    }

    private static ActionHandlerResult Result(TweakOutcome outcome, bool reboot, string dismLine, string sfcLine)
    {
        var lines = new List<string> { dismLine, sfcLine, L.F("Logi: {0}, {1}.", DismLogText, CbsLogText) };
        return new ActionHandlerResult { Outcome = outcome, RequiresReboot = reboot, Message = string.Join(Environment.NewLine, lines) };
    }

    private static string DismText(DismOutcome outcome) => outcome.Verdict switch
    {
        DismVerdict.Healthy => L.T("DISM: magazyn składników jest zdrowy – nie wykryto uszkodzeń."),
        DismVerdict.Repaired => L.F("DISM: naprawiono uszkodzenia magazynu składników ({0} z {1}).", Number(outcome.Repaired), Number(outcome.Detected)),
        DismVerdict.HealthyOrRepaired => L.T("DISM: zakończony bez błędu (kod 0); z CBS.log nie dało się odczytać, czy coś naprawiono."),
        DismVerdict.RebootRequired => L.F("DISM: naprawa dokończy się po ponownym uruchomieniu komputera (kod {0}).", Number(outcome.ExitCode)),
        DismVerdict.SourceMissing => L.F("DISM: nie znaleziono plików do naprawy – potrzebny internet (Windows Update) albo nośnik instalacyjny (kod {0}).", DismOutcome.Hex(outcome.ExitCode)),
        _ when outcome.ExitCode == 0 => L.F("DISM: uszkodzenia magazynu składników: wykryto {0}, naprawiono {1}. Szczegóły: {2}.", Number(outcome.Detected), Number(outcome.Repaired), DismLogText),
        _ => L.F("DISM: błąd {0}. Szczegóły: {1}.", DismOutcome.Hex(outcome.ExitCode), DismLogText),
    };

    private static string SfcText(SfcOutcome outcome) => outcome.Verdict switch
    {
        SfcVerdict.NoViolations => L.T("SFC: nie znaleziono naruszeń integralności plików systemowych."),
        SfcVerdict.Repaired => L.T("SFC: znaleziono uszkodzone pliki systemowe i je naprawiono."),
        SfcVerdict.RepairPendingReboot => L.T("SFC: naprawa plików systemowych dokończy się po ponownym uruchomieniu komputera."),
        SfcVerdict.NotAllRepaired => L.F("SFC: nie udało się naprawić części plików systemowych – szczegóły (wpisy [SR]) w {0}.", CbsLogText),
        _ => L.F("SFC: zakończony kodem {0}, ale wyniku nie da się jednoznacznie odczytać – sprawdź wpisy [SR] w {1}.", Number(outcome.ExitCode), CbsLogText),
    };

    private static string Number(int? value) => (value ?? 0).ToString(CultureInfo.InvariantCulture);

    /// <summary>Pełne minuty jako „N min”, inaczej sekundy („90 s”). Wspólne z <see cref="DiskCleanupHandler"/>.</summary>
    internal static string Duration(TimeSpan limit)
    {
        var seconds = (long)Math.Ceiling(limit.TotalSeconds);
        return seconds % 60 == 0
            ? (seconds / 60).ToString(CultureInfo.InvariantCulture) + " min"
            : seconds.ToString(CultureInfo.InvariantCulture) + " s";
    }

    private sealed record ToolStep(bool TimedOut, int ExitCode, string? Segment, string? StartError);
}
