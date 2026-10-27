using System;

namespace DSOptimizer.Core.Journal;

public sealed class JournalFormatException : Exception
{
    public JournalFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
