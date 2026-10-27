using System;

namespace DSOptimizer.Core.Journal;

public sealed class JournalLoadResult
{
    public JournalLoadResult(JournalDocument document, string? warning)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Warning = warning;
    }

    public JournalDocument Document { get; }

    /// <summary>Np. „journal był uszkodzony, utworzono nowy” — do pokazania użytkownikowi.</summary>
    public string? Warning { get; }
}
