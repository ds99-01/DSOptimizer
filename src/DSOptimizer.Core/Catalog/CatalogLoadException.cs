using System;

namespace DSOptimizer.Core.Catalog;

public sealed class CatalogLoadException : Exception
{
    public CatalogLoadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
