using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Core.Cli;

/// <summary>Wynik punktu przywracania w raporcie CLI (Code = surowy kod WMI, jeśli jest).</summary>
public sealed class CliRestorePointInfo
{
    public RestorePointStatus Status { get; init; }

    public uint? Code { get; init; }

    public string? Message { get; init; }
}
