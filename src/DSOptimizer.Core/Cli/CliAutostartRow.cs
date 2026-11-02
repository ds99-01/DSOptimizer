using System;
using DSOptimizer.Core.Autostart;

namespace DSOptimizer.Core.Cli;

/// <summary>
/// Pozycja listy autostartu w raporcie <c>--list-autostart</c>. <see cref="Verdict"/> = ocena z bazy (null = bez oceny),
/// <see cref="CanToggle"/> = ta sama reguła co pole wyboru na stronie, <see cref="CpuMs"/>/<see cref="DiskBytes"/> = pomiar Windows
/// (czas procesora w milisekundach w dół, odczyt z dysku w bajtach; null = nie zmierzono).
/// </summary>
public sealed class CliAutostartRow
{
    public required string Id { get; init; }

    public AutostartSource Source { get; init; }

    public required string Name { get; init; }

    public string? Publisher { get; init; }

    public AutostartScope Scope { get; init; }

    public AutostartState State { get; init; }

    public bool MissingFile { get; init; }

    public AutostartVerdict? Verdict { get; init; }

    public bool CanToggle { get; init; }

    public long? CpuMs { get; init; }

    public long? DiskBytes { get; init; }

    public static CliAutostartRow From(AutostartRow row)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        var measurement = row.Entry.Measurement;
        return new CliAutostartRow
        {
            Id = row.Id,
            Source = row.Entry.Source,
            Name = row.Entry.Name,
            Publisher = row.Entry.Publisher,
            Scope = row.Entry.Scope,
            State = row.State,
            MissingFile = row.MissingFile,
            Verdict = row.Verdict == AutostartVerdict.None ? null : row.Verdict,
            CanToggle = row.CanToggle,
            // Pomiar jest nieujemny (parser nie przyjmuje znaku), więc dzielenie całkowite zaokrągla w dół.
            CpuMs = measurement is null ? null : measurement.CpuMicroseconds / 1000,
            DiskBytes = measurement?.DiskBytes,
        };
    }
}
