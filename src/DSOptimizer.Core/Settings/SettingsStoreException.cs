using System;

namespace DSOptimizer.Core.Settings;

public sealed class SettingsStoreException : Exception
{
    public SettingsStoreException(string message)
        : base(message)
    {
    }

    public SettingsStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
