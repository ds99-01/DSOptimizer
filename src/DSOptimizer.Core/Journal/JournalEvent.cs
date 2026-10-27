using System;

namespace DSOptimizer.Core.Journal;

public sealed class JournalEvent
{
    public DateTimeOffset At { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>Krótki powód porażki albo braku potwierdzenia (instalacja aplikacji); pomijany w JSON, gdy null.</summary>
    public string? Reason { get; set; }
}
