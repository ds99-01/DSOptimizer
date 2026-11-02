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
    /// </summary>
    public static IReadOnlyList<string> RestoreBaseline(IRegistry registry, JournalEntry entry)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var failed = new List<SnapshotRecord>();
        foreach (var record in entry.Baseline)
        {
            if (record.Op != OperationKind.Registry || record.Path is null || record.Name is null)
            {
                failed.Add(record);
                continue;
            }

            try
            {
                registry.Write(record.Path, record.Name, record.Value ?? RegValue.Absent);
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
