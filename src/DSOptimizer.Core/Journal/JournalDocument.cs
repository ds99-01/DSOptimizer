using System;
using System.Collections.Generic;
using System.Linq;

namespace DSOptimizer.Core.Journal;

public sealed class JournalDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Machine { get; set; } = string.Empty;

    public List<JournalEntry> Entries { get; } = new();

    /// <summary>Ostatni wpis Active/PendingReboot. Gdy <paramref name="userSid"/> != null (tweak user-scope), SID wpisu też musi pasować.</summary>
    public JournalEntry? FindActive(string tweakId, string? userSid = null)
        => Entries.LastOrDefault(entry =>
            entry.IsActive
            && string.Equals(entry.TweakId, tweakId, StringComparison.Ordinal)
            && (userSid is null || string.Equals(entry.UserSid, userSid, StringComparison.OrdinalIgnoreCase)));
}
