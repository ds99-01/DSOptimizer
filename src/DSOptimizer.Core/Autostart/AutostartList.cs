using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Autostart;

/// <summary>Lista autostartu: wiersze po ocenie w kolejności listy (<see cref="AutostartClassifier.Order"/>), czas pomiaru Windows i ostrzeżenia odczytu.</summary>
public sealed record AutostartList(IReadOnlyList<AutostartRow> Rows, DateTimeOffset? MeasuredAt, IReadOnlyList<string> Warnings)
{
    /// <summary>Odczyt (niczego nie zapisuje), ocena i kolejność; ostrzeżenia odczytu przechodzą bez zmian.</summary>
    public static AutostartList Load(IAutostartReader reader, AutostartKnownCatalog catalog, AutostartContext context)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var reading = reader.Read(context);
        var rows = AutostartClassifier.Order(AutostartClassifier.Classify(reading, catalog, context));
        return new AutostartList(rows, reading.MeasuredAt, reading.Warnings);
    }
}
