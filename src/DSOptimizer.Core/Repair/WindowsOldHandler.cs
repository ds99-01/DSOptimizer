using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Repair;

/// <summary>
/// Handler <c>windows-old</c> (<c>repair.windows-old</c>): usuwa poprzednią instalację Windows (<c>&lt;SystemDrive&gt;\Windows.old</c>)
/// programem obsługi „Previous Installations” Oczyszczania dysku – tylko mechanizm A z pomiaru (Task 2 pkt 5, Ruling R8/R18):
/// <list type="number">
/// <item>strażnik trwającej operacji serwisowania; brak folderu (<see cref="IRepairFileSystem.ProbeDirectory"/>) =
/// <see cref="TweakOutcome.NotSupported"/> bez żadnego kroku, błąd sprawdzenia = <see cref="TweakOutcome.Failed"/> „nic nie zmieniono”;</item>
/// <item>wolne miejsce dysku systemowego przed;</item>
/// <item>wartość <see cref="StateFlagsName"/> pod którymkolwiek kluczem <see cref="VolumeCachesKey"/> (cudze ustawienie
/// <c>cleanmgr /sageset</c>), brak programu obsługi albo błąd odczytu = <see cref="TweakOutcome.Failed"/> „nic nie zmieniono”;</item>
/// <item>krok 1 z 2: <see cref="StateFlagsName"/> = DWORD 2 tylko pod <see cref="PreviousInstallationsKey"/> (<see cref="IRegistry"/>,
/// widok 64-bit), <c>cleanmgr.exe /sagerun:7351</c> przez <see cref="RepairServices.LongTools"/> (poza Job Objectem, bez zabijania),
/// czekanie na koniec procesu (VM: folder znika w kilka sekund, cleanmgr kończy się po 7 s w przebiegu handlera, po ok. 11 min w
/// pomiarze Tasku 2), a w <c>finally</c>
/// usunięcie własnej wartości – także po błędzie, wyjątku i limicie;</item>
/// <item>krok 2 z 2: odczyt zwrotny – folder nie istnieje = <see cref="TweakOutcome.Executed"/>, istnieje = <see cref="TweakOutcome.Failed"/>,
/// błąd sprawdzenia = <see cref="TweakOutcome.NotConfirmed"/> z treścią błędu (uwaga o kodzie cleanmgr i nieusunięta flaga zostają).</item>
/// </list>
/// Po limicie czekania wynik to <see cref="TweakOutcome.NotConfirmed"/> „Czyszczenie nadal działa w tle” ze stanem folderu.
/// Przebieg jest nieprzerywalny (Ruling R30): oba zdarzenia postępu mają <see cref="ActionProgress.Stoppable"/> = <c>false</c>, więc
/// okno nie oferuje „Zatrzymaj”, a handler nie czyta <c>cancellationToken</c> – token anulowany przed startem obsługuje silnik
/// (<c>TweakEngine.RunHandler</c>: „Anulowano przed uruchomieniem”), a cleanmgr po potwierdzeniu i tak nie da się przerwać.
/// Handler niczego sam nie usuwa i nie zmienia właściciela ani ACL.
/// </summary>
public sealed class WindowsOldHandler : IActionHandler
{
    /// <summary>Numer <c>/sagerun</c> (pomiar Task 2 pkt 5f: na świeżej 26H2 żaden program obsługi nie ma <c>StateFlags*</c>).</summary>
    public const int SageRunId = 7351;

    public const string StateFlagsName = "StateFlags7351";

    public const string VolumeCachesKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

    public const string PreviousInstallationsKey = VolumeCachesKey + @"\Previous Installations";

    public const string CleanupStage = "Oczyszczanie dysku";

    public const string CheckStage = "Sprawdzenie";

    public const string NothingToRemoveMessage = "Nie ma poprzedniej instalacji Windows – nie ma czego usuwać.";

    /// <summary>Limit czekania na cleanmgr, gdy katalog go nie podaje (pomiar Task 2 pkt 6: 2700 s).</summary>
    public static readonly TimeSpan DefaultWaitLimit = TimeSpan.FromSeconds(2700);

    /// <summary>Wartość „zaznaczone” programu obsługi (dokumentacja MS „Automating Disk Cleanup”).</summary>
    private const uint Selected = 2;

    private const string Component = "Naprawy";
    private const int StepCount = 2;
    private const string CleanmgrFile = "cleanmgr.exe";

    private static readonly string CleanmgrArguments = "/sagerun:" + SageRunId.ToString(CultureInfo.InvariantCulture);

    private readonly RepairServices _s;

    public WindowsOldHandler(RepairServices services)
    {
        _s = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string Name => ActionHandlerNames.WindowsOld;

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

        var drive = Path.GetPathRoot(_s.Paths.SystemRoot) is { Length: > 0 } root ? root : @"C:\";
        var folder = Path.Combine(drive, "Windows.old");
        bool present;
        try
        {
            present = _s.Files.ProbeDirectory(folder);
        }
        catch (OperationException ex)
        {
            // Nie wiadomo, czy folder jest – ale nic nie zostało zmienione ani uruchomione, więc wynik jest pewny: nie udało się.
            _s.Log.Warn(Component, $"windows-old: nie udało się sprawdzić {folder}: {ex.Message}");
            return Result(TweakOutcome.Failed, $"Nie udało się sprawdzić folderu {folder} ({ex.Message}) – nic nie zmieniono.");
        }

        if (!present)
        {
            _s.Log.Info(Component, $"windows-old: brak {folder} – nic do usunięcia.");
            return new ActionHandlerResult { Outcome = TweakOutcome.NotSupported, Message = NothingToRemoveMessage };
        }

        var limit = tweak.Timeout is { } timeout && timeout > TimeSpan.Zero ? timeout : DefaultWaitLimit;
        var before = _s.DiskSpace.FreeBytes(drive);

        if (CheckStateFlags() is { } blocked)
        {
            _s.Log.Warn(Component, "windows-old: " + blocked);
            return Result(TweakOutcome.Failed, blocked);
        }

        // Krok 1 z 2: flaga + cleanmgr; własna wartość zawsze usuwana w finally. Nieprzerywalny (R30) – bez „Zatrzymaj”.
        progress?.Report(new ActionProgress(CleanupStage, null, 0, StepCount, CheckStage, Stoppable: false));
        Cleanmgr run;
        string? flagLeft;
        try
        {
            run = RunCleanmgr(limit, output);
        }
        finally
        {
            flagLeft = RemoveFlag();
        }

        // Krok 2 z 2: odczyt zwrotny (tylko gdy cleanmgr wystartował; inaczej folderu nikt nie ruszał).
        progress?.Report(new ActionProgress(CheckStage, null, 1, StepCount, null, Stoppable: false));
        var after = _s.DiskSpace.FreeBytes(drive);
        bool? exists = null;
        string? probeError = null;
        if (run.Started)
        {
            try
            {
                exists = _s.Files.ProbeDirectory(folder);
            }
            catch (OperationException ex)
            {
                probeError = ex.Message;
            }
        }

        var freed = DiskSpaceText.Freed(before, after);
        var stateText = !run.Started ? "nie sprawdzano (Oczyszczanie dysku nie wystartowało)"
            : probeError is not null ? $"stan nieznany ({probeError})"
            : exists == true ? "nadal istnieje" : "nie istnieje";
        _s.Log.Info(Component, $"windows-old: {folder} {stateText}. Wolne {drive} przed: {Bytes(before)}, po: {Bytes(after)}.");

        var lines = new List<string>();
        TweakOutcome outcome;
        if (!run.Started)
        {
            outcome = TweakOutcome.Failed;
            lines.Add(run.Text!);
        }
        else if (run.TimedOut)
        {
            outcome = TweakOutcome.NotConfirmed;
            var state = probeError is not null
                ? $"Nie udało się sprawdzić folderu {folder} ({probeError}) – sprawdź go później."
                : exists == true
                    ? $"Folder {folder} jeszcze istnieje – sprawdź go później."
                    : $"Folder {folder} już nie istnieje (zwolniono: {freed}).";
            lines.Add($"Czyszczenie nadal działa w tle – program przestał czekać po {SystemFilesHandler.Duration(limit)}. {state}");
        }
        else if (probeError is not null)
        {
            outcome = TweakOutcome.NotConfirmed;
            lines.Add($"Oczyszczanie dysku zakończyło pracę, ale nie udało się sprawdzić, czy folder {folder} zniknął ({probeError}) – sprawdź go ręcznie (zwolniono: {freed}).");
        }
        else if (exists == true)
        {
            outcome = TweakOutcome.Failed;
            lines.Add($"Folder Windows.old nadal istnieje (zwolniono: {freed}).");
        }
        else
        {
            outcome = TweakOutcome.Executed;
            lines.Add($"Usunięto poprzednią instalację Windows. Zwolniono: {freed}.");
        }

        if (run.Started && !run.TimedOut && run.Text is { } note)
        {
            lines.Add(note);
        }

        var notRestored = Array.Empty<string>();
        if (flagLeft is not null)
        {
            outcome = TweakOutcome.Failed;
            lines.Add(flagLeft);
            notRestored = new[] { flagLeft };
        }

        _s.Log.Info(Component, $"windows-old: wynik {outcome}.");
        return new ActionHandlerResult { Outcome = outcome, Message = string.Join(Environment.NewLine, lines), NotRestored = notRestored };
    }

    /// <summary>
    /// <c>null</c> = można ustawić własną wartość; inaczej powód odmowy („… – nic nie zmieniono.”). Odczyt wszystkich programów
    /// obsługi przed jakimkolwiek zapisem.
    /// </summary>
    private string? CheckStateFlags()
    {
        IReadOnlyList<string> handlers;
        try
        {
            handlers = _s.Registry.SubKeyNames(VolumeCachesKey);
        }
        catch (OperationException ex)
        {
            return $"Nie udało się odczytać ustawień Oczyszczania dysku ({ex.Message}) – nic nie zmieniono.";
        }

        var hasPrevious = false;
        foreach (var handler in handlers)
        {
            hasPrevious |= string.Equals(handler, "Previous Installations", StringComparison.OrdinalIgnoreCase);
            var read = _s.Registry.Read(VolumeCachesKey + @"\" + handler, StateFlagsName);
            switch (read.Status)
            {
                case ReadStatus.NotFound:
                    continue;
                case ReadStatus.Found:
                    return $@"Wartość {StateFlagsName} już jest w ustawieniach Oczyszczania dysku (VolumeCaches\{handler}) – ustawił ją inny program "
                        + "albo przerwany wcześniejszy przebieg; nic nie zmieniono.";
                default:
                    return $@"Nie udało się sprawdzić ustawień Oczyszczania dysku (VolumeCaches\{handler}: {read.Error ?? read.Status.ToString()}) – nic nie zmieniono.";
            }
        }

        return hasPrevious
            ? null
            : "W tym systemie Oczyszczanie dysku nie ma programu obsługi „Previous Installations” – nic nie zmieniono.";
    }

    private Cleanmgr RunCleanmgr(TimeSpan limit, IProgress<string>? output)
    {
        try
        {
            _s.Registry.Write(PreviousInstallationsKey, StateFlagsName, RegValue.FromDWord(Selected));
        }
        catch (OperationException ex)
        {
            _s.Log.Warn(Component, $"windows-old: zapis {StateFlagsName} nieudany: {ex.Message}");
            return new Cleanmgr(false, false, $"Nie udało się zaznaczyć poprzedniej instalacji w Oczyszczaniu dysku ({ex.Message}) – nic nie zmieniono, Oczyszczanie dysku nie zostało uruchomione.");
        }

        void OnLine(string line)
        {
            output?.Report(line);
            _s.Log.Info(Component, "cleanmgr: " + line);
        }

        _s.Log.Info(Component, $"windows-old: {CleanmgrFile} {CleanmgrArguments} (limit {SystemFilesHandler.Duration(limit)})");
        var started = _s.Clock.UtcNow;
        ServicingToolRun run;
        try
        {
            run = _s.LongTools.Run(new ToolRequest { FileName = CleanmgrFile, Arguments = CleanmgrArguments, Timeout = limit }, limit, OnLine);
        }
        catch (OperationException ex)
        {
            _s.Log.Warn(Component, $"windows-old: {CleanmgrFile} nie wystartował: {ex.Message}");
            return new Cleanmgr(false, false, $"Nie udało się uruchomić Oczyszczania dysku – {ex.Message} Nic nie usunięto.");
        }

        var seconds = (_s.Clock.UtcNow - started).TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        if (run.TimedOut)
        {
            _s.Log.Warn(Component, $"windows-old: {CleanmgrFile} po limicie {SystemFilesHandler.Duration(limit)} dalej działa w tle – program przestał czekać.");
            return new Cleanmgr(true, true, null);
        }

        _s.Log.Info(Component, $"windows-old: {CleanmgrFile} zakończony kodem {Code(run.ExitCode)} po {seconds} s.");
        return new Cleanmgr(true, false, run.ExitCode is 0 ? null : $"Oczyszczanie dysku zakończyło się kodem {Code(run.ExitCode)}.");
    }

    /// <summary>Usuwa własną wartość; <c>null</c> = usunięta (albo jej nie było), inaczej tekst do wyniku.</summary>
    private string? RemoveFlag()
    {
        try
        {
            _s.Registry.Write(PreviousInstallationsKey, StateFlagsName, RegValue.Absent);
            return null;
        }
        catch (OperationException ex)
        {
            _s.Log.Warn(Component, $"windows-old: nie udało się usunąć {StateFlagsName}: {ex.Message}");
            return $@"Nie udało się usunąć wartości {StateFlagsName} z VolumeCaches\Previous Installations ({ex.Message}) – usuń ją ręcznie, "
                + "inaczej kolejne uruchomienie odmówi.";
        }
    }

    private static ActionHandlerResult Result(TweakOutcome outcome, string message) => new() { Outcome = outcome, Message = message };

    private static string Code(int? code) => code is { } value ? "0x" + value.ToString("X8", CultureInfo.InvariantCulture) : "nieznanym";

    private static string Bytes(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    /// <summary>Wynik kroku 1: czy cleanmgr wystartował, czy minął limit, tekst (błąd startu albo uwaga o kodzie).</summary>
    private sealed record Cleanmgr(bool Started, bool TimedOut, string? Text);
}
