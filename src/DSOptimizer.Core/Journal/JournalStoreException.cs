using System;

namespace DSOptimizer.Core.Journal;

public sealed class JournalStoreException : Exception
{
    public JournalStoreException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
