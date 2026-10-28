using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Repair;

/// <summary>
/// Jeden przebieg resetu Windows Update (wartości z pomiaru VM 26H2 Home i Pro, Task 2, sekcje 3–4). Zasada „wszystko albo nic”:
/// błąd zatrzymania albo przeniesienia folderu cofa to, co już zrobiono, i przywraca usługi w każdej ścieżce; <c>Executed</c> tylko
/// po odczycie zwrotnym (usługi działające przed działają, kopie folderów istnieją). Kroki:
/// 1 (F) odmowa w domenie; 2 odczyt usług; 2a (F) kopia polityk (<c>reg export</c>, przed jakąkolwiek zmianą – R13);
/// 3 zatrzymanie (cryptsvc razem z działającymi zależnymi); 4 przeniesienie SoftwareDistribution i catroot2 na
/// <c>&lt;nazwa&gt;.dso-&lt;rrrrMMdd-HHmmss&gt;</c>; 5 (F) usunięcie klucza polityk;
/// 6 start usług; 7 (F) kopia i zmiana typów startu; 8 diagnoza polityk (podstawowy); 9 odczyt zwrotny; 10 usunięcie starszych kopii.
/// </summary>
public sealed class WindowsUpdateReset
{
    private static string Component => L.T("Naprawy");
    private const string CryptSvc = "cryptsvc";
    private static string HistoryLine => L.T("Historia aktualizacji w Ustawieniach zostanie wyczyszczona (zostać mogą tylko wpisy definicji Microsoft Defender); aktualizacje pobiorą się od nowa.");
    private static string MdmWarning => L.T("Na komputerze zarządzanym przez Intune/MDM usunięte polityki mogą wrócić przy następnej synchronizacji.");
    private static string NothingChanged => L.T("Nic nie zmieniono.");

    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    private readonly RepairServices _s;
    private readonly bool _full;
    private readonly IProgress<string>? _output;
    private readonly CancellationToken _cancellationToken;

    private readonly List<string> _steps = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _notRestored = new();
    private readonly Dictionary<string, ServiceQueryResult> _before = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Usługi zatrzymane przez ten przebieg (i zależne cryptsvc); start w odwrotnej kolejności (wspólne z czyszczeniem dysku).</summary>
    private readonly ServiceStopRestore _stopper;

    private readonly List<(string Source, string Copy)> _moved = new();
    private readonly List<string> _diagnosis = new();
    private string _timestamp = string.Empty;
    private string? _policyBackup;
    private bool _policiesDeleted;

    internal WindowsUpdateReset(RepairServices services, bool full, IProgress<string>? output, CancellationToken cancellationToken)
    {
        _s = services;
        _full = full;
        _output = output;
        _cancellationToken = cancellationToken;
        _stopper = new ServiceStopRestore(services.Services, services.Log, Component, ServiceTimeout, Attempt);
    }

    /// <summary>Usługi zatrzymywane na czas przeniesienia folderów, w tej kolejności (R12: bez UsoSvc i msiserver).</summary>
    public static IReadOnlyList<string> StoppedServices { get; } = new[] { "wuauserv", "bits", CryptSvc };

    /// <summary>Typy startu świeżej 26H2, identyczne na Home i Pro (R10); wariant rozszerzony ustawia je po resecie.</summary>
    public static IReadOnlyList<(string Name, ServiceStartType StartType)> FreshStartTypes { get; } = new[]
    {
        ("wuauserv", ServiceStartType.Manual),
        ("bits", ServiceStartType.AutomaticDelayed),
        (CryptSvc, ServiceStartType.Automatic),
        ("UsoSvc", ServiceStartType.AutomaticDelayed),
    };

    /// <summary>Usługi chronione przez system (zapis typu startu = odmowa 5): nigdy nie zmieniane, wspominane tylko gdy są Disabled (R11).</summary>
    public static IReadOnlyList<string> ProtectedServices { get; } = new[] { "msiserver", "WaaSMedicSvc", "DoSvc" };

    private string SoftwareDistribution => Path.Combine(_s.Paths.SystemRoot, "SoftwareDistribution");

    private string Catroot2 => Path.Combine(_s.Paths.SystemRoot, "System32", "catroot2");

    internal ActionHandlerResult Run()
    {
        if (_full && RefuseOnDomain() is { } refused)
        {
            return refused;
        }

        if (ReadServices() is { } readFailure)
        {
            return readFailure;
        }

        if (_cancellationToken.IsCancellationRequested)
        {
            return Result(TweakOutcome.Failed, L.T("Anulowano przed zmianami. Nic nie zmieniono."), withSteps: false);
        }

        ReportProtectedServices();
        if (!_full)
        {
            DiagnosePolicies();
        }

        var local = TimeZoneInfo.ConvertTime(_s.Clock.UtcNow, _s.TimeZone);
        _timestamp = local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // R13: kopia polityk powstaje przed jakąkolwiek zmianą – nieudany eksport = nic nie zmieniono.
        if (_full && ExportPolicies() is { } exportFailure)
        {
            return exportFailure;
        }

        if (StopServices() is { } stopFailure)
        {
            return stopFailure;
        }

        if (MoveFolders() is { } moveFailure)
        {
            return moveFailure;
        }

        var policyFailure = _full ? DeletePolicies() : null;
        var restoreFailure = RestoreServices();

        if (restoreFailure is not null)
        {
            var header = L.F("Nie udało się ponownie uruchomić usług: {0}.", restoreFailure);
            return Result(TweakOutcome.Failed, (policyFailure is null ? header : header + " " + policyFailure) + FullStopTail());
        }

        if (policyFailure is not null)
        {
            return Result(TweakOutcome.Failed, policyFailure + L.T(" Foldery Windows Update zresetowano (kopie niżej), usługi przywrócono."));
        }

        if (_moved.Count == 0)
        {
            return Result(TweakOutcome.Failed, L.F("Nie znaleziono folderów Windows Update ({0}, {1}) – nie było czego zresetować. Usługi przywrócono.", SoftwareDistribution, Catroot2) + FullStopTail());
        }

        if (_full)
        {
            SetFreshStartTypes();
            _warnings.Add(MdmWarning);
        }

        if (Readback() is { } readbackFailure)
        {
            return Result(TweakOutcome.Failed, L.F("Odczyt zwrotny nie potwierdził resetu: {0}.", readbackFailure));
        }

        Step(L.T("Odczyt zwrotny: usługi działają jak przed resetem, kopie folderów istnieją."));
        PruneOldCopies();
        return Result(TweakOutcome.Executed, _full ? L.T("Zresetowano składniki Windows Update i usunięto blokady aktualizacji, jeśli były.") : L.T("Zresetowano składniki Windows Update."));
    }

    /// <summary>
    /// Wariant rozszerzony zatrzymany po usunięciu polityk, a przed typami startu (brak folderów albo usług nie dało się przywrócić):
    /// nagłówek mówi, że polityki są już usunięte (z kopią) i że typy startu zostały bez zmian, a wynik niesie ostrzeżenie MDM.
    /// </summary>
    private string FullStopTail()
    {
        if (!_full)
        {
            return string.Empty;
        }

        _warnings.Add(MdmWarning);
        var removed = _policiesDeleted && _policyBackup is { } file
            ? L.F(" Polityki Windows Update zostały już usunięte (kopia: {0}; przywrócenie: reg import \"{0}\").", file)
            : string.Empty;
        return removed + L.T(" Typy startu usług nie zostały zmienione.");
    }

    // ---- 1. domena ----

    private ActionHandlerResult? RefuseOnDomain() => _s.Domain.IsDomainJoined() switch
    {
        true => Result(TweakOutcome.Refused, L.T("Komputer jest w domenie: polityki Windows Update ustawia administrator domeny."), withSteps: false),
        null => Result(TweakOutcome.Refused, L.T("Nie udało się sprawdzić, czy komputer jest w domenie."), withSteps: false),
        _ => null,
    };

    // ---- 2. odczyt usług ----

    private ActionHandlerResult? ReadServices()
    {
        var names = _full ? FreshStartTypes.Select(entry => entry.Name) : StoppedServices;
        foreach (var name in names)
        {
            var state = _s.Services.Query(name);
            switch (state.Status)
            {
                case ReadStatus.Found:
                    _before[name] = state;
                    break;
                case ReadStatus.NotFound:
                    Warn(L.F("Usługa {0} nie istnieje – pominięta.", name));
                    break;
                default:
                    return Result(TweakOutcome.Failed, L.F("Nie udało się odczytać usługi {0}: {1} Nic nie zmieniono.", name, Sentence(state.Error)), withSteps: false);
            }
        }

        return null;
    }

    private void ReportProtectedServices()
    {
        foreach (var name in ProtectedServices)
        {
            var state = _s.Services.Query(name);
            if (state.Status == ReadStatus.Found && state.StartType == ServiceStartType.Disabled)
            {
                Warn(L.F("Usługa {0} jest wyłączona (Disabled). Program jej nie zmienia (system odmawia zapisu), a wyłączona usługa może blokować aktualizacje.", name));
            }
        }
    }

    // ---- 3. zatrzymanie ----

    private ActionHandlerResult? StopServices()
    {
        foreach (var name in StoppedServices)
        {
            if (!_before.TryGetValue(name, out var state))
            {
                continue;
            }

            if (!state.IsRunning)
            {
                Step(L.F("Usługa {0} nie działała – bez zmian.", name));
                continue;
            }

            // R8: catroot2 trzymają cryptsvc i jego zależne (na 26H2 AppIDSvc i sterownik applockerfltr) – lista z systemu, nie na sztywno.
            var withDependents = string.Equals(name, CryptSvc, StringComparison.OrdinalIgnoreCase);
            var error = _stopper.Stop(name, withDependents, out var dependents);

            if (error is not null)
            {
                var restoreFailure = RestoreServices();
                var tail = restoreFailure is null ? NothingChanged : L.F("Foldery nietknięte, ale nie udało się ponownie uruchomić: {0}.", restoreFailure);
                return Result(TweakOutcome.Failed, L.F("Nie udało się zatrzymać usługi {0}: {1} {2}", name, Sentence(error), tail));
            }

            Step(dependents.Count == 0
                ? L.F("Zatrzymano usługę {0}.", name)
                : L.F("Zatrzymano usługę {0} razem z zależnymi: {1}.", name, string.Join(", ", dependents)));
        }

        return null;
    }

    // ---- 4. foldery ----

    private ActionHandlerResult? MoveFolders()
    {
        foreach (var source in new[] { SoftwareDistribution, Catroot2 })
        {
            var exists = false;
            var copy = string.Empty;
            var error = Attempt(() => exists = _s.Files.DirectoryExists(source));
            if (error is null && !exists)
            {
                Step(L.F("Pominięto {0} – folder nie istnieje.", source));
                continue;
            }

            error ??= Attempt(() => copy = FreeCopyName(source));
            error ??= Attempt(() => _s.Files.MoveDirectory(source, copy));
            if (error is null)
            {
                _moved.Add((source, copy));
                Step(L.F("Przeniesiono {0} → {1}.", source, copy));
                continue;
            }

            var header = new StringBuilder(L.F("Nie udało się przenieść folderu {0}: {1}", source, Sentence(error)));
            var rolledBack = RollBackMoves(header);
            var restoreFailure = RestoreServices();
            if (restoreFailure is null)
            {
                header.Append(L.T(" Usługi przywrócono."));
            }
            else
            {
                header.Append(L.F(" Nie udało się ponownie uruchomić: {0}.", restoreFailure));
            }

            if (rolledBack && restoreFailure is null)
            {
                header.Append(' ').Append(NothingChanged);
            }

            return Result(TweakOutcome.Failed, header.ToString());
        }

        return null;
    }

    /// <summary>Wolna nazwa kopii: <c>&lt;folder&gt;.dso-&lt;znacznik&gt;</c>, przy kolizji <c>-2</c>, <c>-3</c>…</summary>
    private string FreeCopyName(string source)
    {
        var baseName = $"{source}.dso-{_timestamp}";
        var candidate = baseName;
        for (var suffix = 2; _s.Files.DirectoryExists(candidate); suffix++)
        {
            candidate = string.Format(CultureInfo.InvariantCulture, "{0}-{1}", baseName, suffix);
        }

        return candidate;
    }

    private bool RollBackMoves(StringBuilder header)
    {
        var ok = true;
        for (var i = _moved.Count - 1; i >= 0; i--)
        {
            var (source, copy) = _moved[i];
            var error = Attempt(() => _s.Files.MoveDirectory(copy, source));
            if (error is null)
            {
                header.Append(L.F(" Cofnięto przeniesienie {0}.", source));
                continue;
            }

            ok = false;
            _notRestored.Add(L.F("Folder {0} jest pod nazwą {1}. Windows mógł już utworzyć nowy folder {0} – najpierw go przenieś albo usuń, potem przywróć nazwę. Kolejny reset w DSOptimizerze usunie kopię {1} jako starszą.", source, copy));
            header.Append(L.F(" Nie udało się cofnąć przeniesienia ({0}) – zmień nazwę {1} z powrotem na {2} ręcznie, przy zatrzymanych usługach.", Sentence(error), copy, source));
        }

        _moved.Clear();
        return ok;
    }

    // ---- 5. polityki (F) ----

    /// <summary>
    /// Przed jakąkolwiek zmianą (R13): gdy klucz polityk istnieje, kopia <c>.reg</c> w katalogu kopii. Każdy błąd (także nieczytelny
    /// klucz) = <c>Failed</c> „… Nic nie zmieniono.”. Plik zostaje w backups także wtedy, gdy późniejszy krok resetu się nie uda.
    /// </summary>
    private ActionHandlerResult? ExportPolicies()
    {
        var key = WindowsUpdatePolicies.KeyPath;
        var status = _s.Registry.KeyStatus(key);
        if (status == ReadStatus.NotFound)
        {
            Step(L.T("Brak polityk Windows Update."));
            return null;
        }

        if (status != ReadStatus.Found)
        {
            return Result(TweakOutcome.Failed, L.F("Nie udało się sprawdzić polityk Windows Update ({0}: {1}); polityki zostają bez zmian. Nic nie zmieniono.", key, status), withSteps: false);
        }

        var file = string.Empty;
        var exportError = Attempt(() => file = Path.Combine(_s.Paths.EnsureBackupDirectory(), $"wu-policies-{_timestamp}.reg"));
        exportError ??= Export(key, file);
        if (exportError is not null)
        {
            return Result(TweakOutcome.Failed, L.F("Nie udało się zapisać kopii polityk Windows Update ({0}); polityki zostają bez zmian. Nic nie zmieniono.", exportError), withSteps: false);
        }

        _policyBackup = file;
        Step(L.F("Zapisano kopię polityk Windows Update: {0}.", file));
        return null;
    }

    /// <summary>Po przeniesieniu folderów, przed startem usług: usunięcie klucza polityk (z AU), tylko gdy kopia powstała.</summary>
    private string? DeletePolicies()
    {
        if (_policyBackup is not { } file)
        {
            return null;
        }

        var key = WindowsUpdatePolicies.KeyPath;
        var importHint = L.F("Przywrócenie: reg import \"{0}\".", file);
        var deleteError = Attempt(() => _s.Registry.DeleteKeyTree(key));
        if (deleteError is not null)
        {
            return L.F("Kopia polityk zapisana w {0}, ale usunięcie klucza nie powiodło się: {1} {2}", file, Sentence(deleteError), importHint);
        }

        if (_s.Registry.KeyStatus(key) != ReadStatus.NotFound)
        {
            return L.F("Kopia polityk zapisana w {0}, ale klucz nadal istnieje po usunięciu ({1}). {2}", file, key, importHint);
        }

        _policiesDeleted = true;
        Step(L.F("Polityki Windows Update usunięte (z podkluczem AU); kopia: {0}. {1}", file, importHint));
        return null;
    }

    /// <summary><c>reg.exe export</c> z System32 (obejmuje podklucz AU); sukces = kod 0 i niepusty plik.</summary>
    private string? Export(string key, string file)
    {
        ToolResult? result = null;
        var error = Attempt(() => result = _s.Tools.Run(
            new ToolRequest { FileName = "reg.exe", Arguments = $"export \"{key}\" \"{file}\" /y" },
            _cancellationToken));
        if (error is not null)
        {
            return error.TrimEnd('.');
        }

        _s.Log.Info(Component, L.F("reg export {0}: kod {1}: {2}", key, result!.ExitCode, string.Join(" | ", result.Output)));
        if (result.ExitCode != 0)
        {
            return L.F("reg export zakończył się kodem {0}: {1}", result.ExitCode, string.Join(" ", result.Output).Trim());
        }

        long? length = null;
        error = Attempt(() => length = _s.Files.FileLength(file));
        if (error is not null)
        {
            return error.TrimEnd('.');
        }

        return length is > 0 ? null : L.F("plik kopii {0} jest pusty albo go nie ma", file);
    }

    // ---- 6. start usług ----

    /// <summary>Uruchamia zatrzymane w odwrotnej kolejności; o wyniku decyduje odczyt stanu, nie kod Start („już działa” to nie błąd).</summary>
    private string? RestoreServices()
    {
        var restored = _stopper.Restore();
        foreach (var failure in restored.Failed)
        {
            _notRestored.Add(L.F("Usługa {0} nie działa po przywróceniu: {1}", failure.Name, Sentence(failure.Reason)));
        }

        if (restored.Started.Count > 0)
        {
            Step(L.F("Uruchomiono ponownie: {0}.", string.Join(", ", restored.Started)));
        }

        return restored.Failed.Count == 0 ? null : string.Join(", ", restored.Failed.Select(f => f.Name));
    }

    // ---- 7. typy startu (F) ----

    private void SetFreshStartTypes()
    {
        var file = string.Empty;
        var content = new StringBuilder()
            .AppendLine(L.F("# DSOptimizer: typy startu usług Windows Update przed resetem z usuwaniem blokad ({0}).", _timestamp))
            .AppendLine(L.T("# Przywrócenie ręczne: sc.exe config <nazwa> start= auto | delayed-auto | demand | disabled"));
        foreach (var (name, _) in FreshStartTypes)
        {
            if (_before.TryGetValue(name, out var state))
            {
                content.AppendLine($"{name}: {state.StartType}");
            }
        }

        var error = Attempt(() => file = Path.Combine(_s.Paths.EnsureBackupDirectory(), $"wu-services-{_timestamp}.txt"));
        error ??= Attempt(() => _s.Files.WriteAllText(file, content.ToString()));
        if (error is not null)
        {
            Warn(L.F("Nie udało się zapisać kopii typów startu ({0}); typy startu nie zostały zmienione.", error.TrimEnd('.')));
            return;
        }

        Step(L.F("Typy startu sprzed zapisane w {0}.", file));
        var unchanged = new List<string>();
        foreach (var (name, target) in FreshStartTypes)
        {
            if (!_before.TryGetValue(name, out var state))
            {
                continue;
            }

            if (state.StartType == target)
            {
                unchanged.Add(name);
                continue;
            }

            var setError = Attempt(() => _s.Services.SetStartType(name, target));
            if (setError is not null)
            {
                Warn(L.F("Nie udało się ustawić typu startu {0} na {1}: {2}", name, target, Sentence(setError)));
                continue;
            }

            var after = _s.Services.Query(name);
            if (after.Status != ReadStatus.Found || after.StartType != target)
            {
                Warn(L.F("Typ startu {0} po zmianie to {1} zamiast {2}.", name, after.Status == ReadStatus.Found ? after.StartType.ToString() : after.Status.ToString(), target));
                continue;
            }

            Step(L.F("Typ startu {0}: {1} → {2}.", name, state.StartType, target));
        }

        if (unchanged.Count > 0)
        {
            Step(L.F("Typ startu bez zmian: {0}.", string.Join(", ", unchanged)));
        }
    }

    // ---- 8. diagnoza polityk (podstawowy) ----

    private void DiagnosePolicies()
    {
        var found = false;
        foreach (var (path, name) in WindowsUpdatePolicies.DiagnosedValues)
        {
            var label = string.Equals(path, WindowsUpdatePolicies.AuKeyPath, StringComparison.OrdinalIgnoreCase) ? @"AU\" + name : name;
            var value = _s.Registry.Read(path, name);
            switch (value.Status)
            {
                case ReadStatus.Found:
                    found = true;
                    _diagnosis.Add(L.F("Polityka: {0} = {1}", label, Display(value.Value)));
                    break;
                case ReadStatus.NotFound:
                    break;
                default:
                    _diagnosis.Add(L.F("Polityka: {0} – nie udało się odczytać ({1}).", label, value.Error));
                    break;
            }
        }

        if (found)
        {
            _diagnosis.Add(L.T("Mogą być przyczyną problemu; usuwa je pozycja „Zresetuj Windows Update i usuń blokady” (z kopią)."));
        }
    }

    private static string Display(RegValue value) => value.Data switch
    {
        uint dword => dword.ToString(CultureInfo.InvariantCulture),
        string text => text,
        _ => value.ToDisplayString(),
    };

    // ---- 9. odczyt zwrotny ----

    private string? Readback()
    {
        var problems = new List<string>();
        foreach (var name in StoppedServices)
        {
            if (_before.TryGetValue(name, out var state) && state.IsRunning)
            {
                var now = _s.Services.Query(name);
                if (now.Status != ReadStatus.Found || !now.IsRunning)
                {
                    problems.Add(L.F("usługa {0} nie działa", name));
                }
            }
        }

        foreach (var (_, copy) in _moved)
        {
            var exists = false;
            var error = Attempt(() => exists = _s.Files.DirectoryExists(copy));
            if (error is not null || !exists)
            {
                problems.Add(L.F("brak kopii {0}", copy));
            }
        }

        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // ---- 10. starsze kopie ----

    private void PruneOldCopies()
    {
        foreach (var (source, copy) in _moved)
        {
            var name = Path.GetFileName(source);
            var ownCopy = new Regex("^" + Regex.Escape(name) + @"\.dso-\d{8}-\d{6}(-\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            IReadOnlyList<string> siblings = Array.Empty<string>();
            var listError = Attempt(() => siblings = _s.Files.ListSiblingDirectories(source, name + ".dso-"));
            if (listError is not null)
            {
                Warn(L.F("Nie udało się sprawdzić starszych kopii {0}: {1}", source, Sentence(listError)));
                continue;
            }

            foreach (var old in siblings)
            {
                if (string.Equals(old, copy, StringComparison.OrdinalIgnoreCase) || !ownCopy.IsMatch(Path.GetFileName(old)))
                {
                    continue;
                }

                var error = Attempt(() => _s.Files.DeleteDirectoryTree(old));
                if (error is null)
                {
                    Step(L.F("Usunięto starszą kopię {0}.", old));
                }
                else
                {
                    Warn(L.F("Nie udało się usunąć starszej kopii {0}: {1}", old, Sentence(error)));
                }
            }
        }
    }

    // ---- pomocnicze ----

    /// <summary>
    /// Wykonuje krok i zwraca komunikat błędu zamiast wyjątku – także nieoczekiwanego, bo każda ścieżka błędu musi dojść do
    /// cofnięcia folderów i przywrócenia usług.
    /// </summary>
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
#pragma warning disable CA1031 // Każdy błąd kroku ma skończyć się przywróceniem stanu, a nie wyjątkiem w połowie resetu.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _s.Log.Fail(Component, L.T("Nieoczekiwany błąd kroku resetu Windows Update."), ex);
            return ex.Message;
        }
    }

    private static string Sentence(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().TrimEnd('.');
        return trimmed.Length == 0 ? L.T("nieznany błąd.") : trimmed + ".";
    }

    private void Step(string line)
    {
        _steps.Add(line);
        _s.Log.Info(Component, line);
        _output?.Report(line);
    }

    private void Warn(string warning)
    {
        _warnings.Add(warning);
        _s.Log.Warn(Component, warning);
    }

    private ActionHandlerResult Result(TweakOutcome outcome, string header, bool withSteps = true)
    {
        var lines = new List<string> { header };
        if (withSteps)
        {
            lines.AddRange(_steps);
            lines.AddRange(_diagnosis);
            if (outcome == TweakOutcome.Executed)
            {
                lines.Add(HistoryLine);
                _output?.Report(HistoryLine);
            }
        }

        var message = string.Join(Environment.NewLine, lines);
        if (outcome == TweakOutcome.Executed)
        {
            _s.Log.Info(Component, header);
        }
        else
        {
            _s.Log.Warn(Component, header);
        }

        return new ActionHandlerResult { Outcome = outcome, Message = message, Warnings = _warnings.ToArray(), NotRestored = _notRestored.ToArray() };
    }
}
