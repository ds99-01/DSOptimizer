using System.Collections.Generic;

namespace DSOptimizer.Core.Gui;

/// <summary>Wynik ładowania historii zmian: wpisy (od najnowszego) i ostrzeżenie do pokazania, jeśli było.</summary>
public sealed record HistorySnapshot(IReadOnlyList<HistoryEntry> Entries, string? Warning);
