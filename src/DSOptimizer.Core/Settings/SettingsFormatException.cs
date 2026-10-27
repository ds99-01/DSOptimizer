using System;

namespace DSOptimizer.Core.Settings;

public sealed class SettingsFormatException : Exception
{
    public SettingsFormatException(string message)
        : base(message)
    {
    }

    public SettingsFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
