using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Wpisy journala autostartu (<see cref="JournalEntry.AutostartKind"/>): wyszukanie aktywnego wpisu pozycji, kierunek wpisu i przywrócenie
/// stanu sprzed zmiany – wspólne dla reguły jednego aktywnego wpisu, rollbacku po nieudanym zapisie i cofania z Historii zmian.
/// </summary>
internal static class AutostartJournal
{
    /// <summary>Wartości klucza zadania startowego, które silnik czyta do baseline i zapisuje (<see cref="StartupTaskValue"/>).</summary>
    private static readonly string[] TaskValueNames =
    {
        StartupTaskValue.State, StartupTaskValue.LastDisabledTime, StartupTaskValue.UserEnabledStartupOnce, StartupTaskValue.ShowNotification,
    };

    /// <summary>
    /// Ostatni aktywny wpis autostartu pozycji (id bez rozróżniania wielkości liter, jak w całym module); dla pozycji konta
    /// (<see cref="AutostartIds.IsUserScoped"/>) także z tym samym SID, pozycja maszyny – dowolny SID.
    /// </summary>
    public static JournalEntry? FindActive(JournalDocument document, string itemId, string userSid)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (itemId is null)
        {
            throw new ArgumentNullException(nameof(itemId));
        }

        if (userSid is null)
        {
            throw new ArgumentNullException(nameof(userSid));
        }

        var userScoped = AutostartIds.IsUserScoped(itemId);
        return document.Entries.LastOrDefault(entry =>
            string.Equals(entry.Kind, JournalEntry.AutostartKind, StringComparison.Ordinal)
            && entry.IsActive
            && string.Equals(entry.TweakId, itemId, StringComparison.OrdinalIgnoreCase)
            && (!userScoped || string.Equals(entry.UserSid, userSid, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Kierunek wpisu z akcji pierwszego zdarzenia; inna akcja albo brak zdarzeń = <see cref="AutostartState.Unknown"/>.</summary>
    public static AutostartState TargetOf(JournalEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        return entry.Events.FirstOrDefault()?.Action switch
        {
            AutostartEngine.DisableAction => AutostartState.Disabled,
            AutostartEngine.EnableAction => AutostartState.Enabled,
            _ => AutostartState.Unknown,
        };
    }

    /// <summary>
    /// Zapisuje każdy rekord baseline (<see cref="RegValue.Absent"/> = usunięcie wartości), potem kasuje pusty klucz z
    /// <see cref="SnapshotRecord.CreatedKeyRoot"/>, na końcu sprawdza świeżym odczytem każdy rekord (<see cref="SnapshotComparer.SameState"/>).
    /// Zwraca opisy rekordów niezgodnych (pusta lista = przywrócone). Wyjątek zapisu albo kasowania klucza = rekord niezgodny, nie wyjątek.
    /// Przed czymkolwiek każdy rekord musi być rekordem rejestru w miejscu autostartu pozycji (<see cref="BelongsToEntry"/>); choć jeden
    /// inny = nic nie jest zapisane ani skasowane, a zwracane są opisy wszystkich rekordów.
    /// </summary>
    public static IReadOnlyList<string> RestoreBaseline(IRegistry registry, JournalEntry entry, AutostartLocations locations)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (locations is null)
        {
            throw new ArgumentNullException(nameof(locations));
        }

        // Obrona w głąb: zapis tylko w miejscach autostartu pozycji wpisu. Jeden rekord spoza nich (journal zmieniony poza programem)
        // blokuje cały wpis – nic nie jest zapisane ani skasowane, a wszystkie rekordy trafiają do niezgodnych.
        if (entry.Baseline.Any(record => !Writable(record) || !BelongsToEntry(entry, record, locations)))
        {
            return entry.Baseline.Select(record => record.Describe()).ToList();
        }

        var failed = new List<SnapshotRecord>();
        foreach (var record in entry.Baseline)
        {
            try
            {
                registry.Write(record.Path!, record.Name!, record.Value ?? RegValue.Absent);
            }
            catch (Exception)
            {
                failed.Add(record);
            }
        }

        // CreatedKeyRoot w istniejącym kodzie to „najwyższy brakujący klucz” (RegistryValueOperation). Tutaj zawsze klucz zadania startowego:
        // klucz rodziny pakietu istnieje na każdym systemie z tą aplikacją, więc brakować może tylko klucza zadania. Klucz niepusty (Windows
        // dopisał wartość) zostaje i nie jest niezgodnością; klucze StartupApproved nigdy nie dostają CreatedKeyRoot i zostają.
        foreach (var record in entry.Baseline)
        {
            if (record.CreatedKeyRoot is not { } createdKey || failed.Contains(record))
            {
                continue;
            }

            try
            {
                registry.DeleteKeyIfEmpty(createdKey);
            }
            catch (Exception)
            {
                failed.Add(record);
            }
        }

        foreach (var record in entry.Baseline)
        {
            if (!failed.Contains(record) && !Restored(registry, record))
            {
                failed.Add(record);
            }
        }

        return entry.Baseline.Where(failed.Contains).Select(record => record.Describe()).ToList();
    }

    /// <summary>
    /// Rekord baseline leży w miejscu autostartu pozycji wpisu (bez wielkości liter). Zadanie startowe: klucz zadania z
    /// <see cref="AutostartLocations.StartupTaskKey"/>, jedna z czterech wartości <see cref="StartupTaskValue"/> i
    /// <see cref="SnapshotRecord.CreatedKeyRoot"/> pusty albo równy kluczowi zadania. Pozostałe źródła: klucz
    /// <see cref="AutostartLocations.StartupApprovedKey"/>, nazwa wartości = nazwa z id, bez <see cref="SnapshotRecord.CreatedKeyRoot"/>.
    /// Id, którego <see cref="AutostartIds.TryParse"/> nie przyjmuje, nie ma miejsc – false.
    /// </summary>
    internal static bool BelongsToEntry(JournalEntry entry, SnapshotRecord record, AutostartLocations locations)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        if (locations is null)
        {
            throw new ArgumentNullException(nameof(locations));
        }

        if (!AutostartIds.TryParse(entry.TweakId, out var source, out var entryName, out var family))
        {
            return false;
        }

        if (source == AutostartSource.StartupTask)
        {
            string taskKey;
            try
            {
                taskKey = locations.StartupTaskKey(family!, entryName);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return Same(record.Path, taskKey)
                && TaskValueNames.Any(name => Same(record.Name, name))
                && (record.CreatedKeyRoot is null || Same(record.CreatedKeyRoot, taskKey));
        }

        return Same(record.Path, locations.StartupApprovedKey(source))
            && Same(record.Name, entryName)
            && record.CreatedKeyRoot is null;
    }

    private static bool Same(string? actual, string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool Writable(SnapshotRecord record) => record.Op == OperationKind.Registry && record.Path is not null && record.Name is not null;

    /// <summary>Świeży odczyt rekordu zgodny z baseline; błąd odczytu albo wyjątek = niezgodny.</summary>
    private static bool Restored(IRegistry registry, SnapshotRecord record)
    {
        var path = record.Path!;
        var name = record.Name!;
        RegistryReadResult read;
        try
        {
            read = registry.Read(path, name);
        }
        catch (Exception)
        {
            return false;
        }

        var actual = read.Status switch
        {
            ReadStatus.Found => SnapshotRecord.ForRegistry(path, name, read.Value),
            ReadStatus.NotFound => SnapshotRecord.ForRegistry(path, name, RegValue.Absent),
            _ => null,
        };
        return actual is not null && SnapshotComparer.SameState(record, actual);
    }
}
