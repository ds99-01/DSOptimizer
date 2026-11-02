using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Autostart;

/// <summary>Ocena odczytanych pozycji: wpis z bazy, powód blokady i kolejność listy.</summary>
public static class AutostartClassifier
{
    /// <summary>
    /// Wiersze w kolejności odczytu. Blokada = pierwsza pasująca: chroniona; konto niepewne (zakres konta albo Sklep); program na innym koncie
    /// bez gałęzi konta (<c>HKU\&lt;SID&gt;</c> dla konta, <c>HKU\&lt;SID&gt;_Classes</c> dla Sklepu); stan nieczytelny; nieznany format;
    /// stan zadania z zasad albo nieznany; zapis od zera bez dowodu (<see cref="WriteFromZeroAllowed"/>); wpis bez nazwy.
    /// </summary>
    public static IReadOnlyList<AutostartRow> Classify(AutostartReading reading, AutostartKnownCatalog catalog, AutostartContext context)
    {
        if (reading is null)
        {
            throw new ArgumentNullException(nameof(reading));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return reading.Entries
            .Select(entry =>
            {
                var known = catalog.Match(entry, reading.Environment);
                return new AutostartRow(entry, known, BlockerOf(entry, known, context));
            })
            .ToList();
    }

    /// <summary>
    /// Kolejność listy: pozycje zmierzone malejąco po czasie procesora, potem niezmierzone; przy równych liczbach i wśród niezmierzonych
    /// alfabetycznie po nazwie w kulturze języka (bez wielkości liter), na końcu po id.
    /// </summary>
    public static IReadOnlyList<AutostartRow> Order(IEnumerable<AutostartRow> rows)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        var names = StringComparer.Create(L.Culture, ignoreCase: true);
        return rows
            .OrderBy(row => row.Entry.Measurement is null ? 1 : 0)
            .ThenByDescending(row => row.Entry.Measurement?.CpuMicroseconds ?? 0)
            .ThenBy(row => row.Entry.Name, names)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Bramka zapisu od zera – ta sama dla listy i dla świeżego odczytu przed zapisem. Przełącznik z wartością (<c>Format</c> ≠
    /// <see cref="AutostartSwitchFormat.Absent"/>) zawsze przechodzi. <c>StartupApproved</c> bez wartości: tylko źródło z
    /// <see cref="AutostartContext.WriteFromZeroConfirmed"/>. Zadanie startowe bez zapisanego stanu: manifest <c>Enabled="true"</c> nigdy
    /// (wyłączenie od zera niezmierzone); klucz nie istnieje – tylko z <see cref="AutostartContext.StartupTaskKeyAbsentEnable"/>;
    /// <c>State</c> 0 – tylko z <see cref="AutostartContext.StartupTaskState0Enable"/>; klucz bez <c>State</c> nigdy (niezmierzony).
    /// </summary>
    public static bool WriteFromZeroAllowed(AutostartSwitch current, AutostartSource source, AutostartContext context)
    {
        if (current is null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (current.Format != AutostartSwitchFormat.Absent)
        {
            return true;
        }

        if (current.Kind != AutostartSwitchKind.StartupTask)
        {
            return context.WriteFromZeroConfirmed.Contains(source);
        }

        if (current.EnabledInManifest)
        {
            return false;
        }

        if (!current.KeyExists)
        {
            return context.StartupTaskKeyAbsentEnable;
        }

        return current.TaskState == 0 && context.StartupTaskState0Enable;
    }

    private static AutostartBlocker BlockerOf(AutostartEntry entry, AutostartKnownEntry? known, AutostartContext context)
    {
        if (known?.Verdict == AutostartVerdict.Protected)
        {
            return AutostartBlocker.Protected;
        }

        var scope = entry.Scope;
        if (scope != AutostartScope.Machine)
        {
            if (!context.UserCertain)
            {
                return AutostartBlocker.UserUncertain;
            }

            var otherAccount = !string.Equals(context.ProcessSid, context.UserSid, StringComparison.OrdinalIgnoreCase);
            var hiveLoaded = scope == AutostartScope.User ? context.UserHiveLoaded : context.ClassesHiveLoaded;
            if (otherAccount && !hiveLoaded)
            {
                return AutostartBlocker.UserHiveMissing;
            }
        }

        switch (entry.Switch.Format)
        {
            case AutostartSwitchFormat.Unreadable:
                return AutostartBlocker.Unreadable;
            case AutostartSwitchFormat.UnknownFormat:
                return AutostartBlocker.UnknownFormat;
            case AutostartSwitchFormat.Policy:
                return AutostartBlocker.TaskStateNotSupported;
        }

        if (!WriteFromZeroAllowed(entry.Switch, entry.Source, context))
        {
            return AutostartBlocker.WriteFromZeroNotMeasured;
        }

        return entry.EntryName.Length == 0 ? AutostartBlocker.NoName : AutostartBlocker.None;
    }
}
