namespace DSOptimizer.Core.Cli;

/// <summary>Identyfikacja maszyny w raporcie CLI.</summary>
public sealed class CliMachineInfo
{
    public int Build { get; init; }

    public int Revision { get; init; }

    public string? Edition { get; init; }

    public string? MachineName { get; init; }
}
