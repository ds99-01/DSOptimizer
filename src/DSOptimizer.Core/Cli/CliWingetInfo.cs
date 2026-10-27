using DSOptimizer.Core.Install;

namespace DSOptimizer.Core.Cli;

/// <summary>Stan winget w raporcie poleceń instalacji: odmowa konta, brak App Installera, niedziałający winget albo gotowość z wersją.</summary>
public sealed class CliWingetInfo
{
    public WingetState State { get; init; }

    /// <summary>Wersja z <c>winget --version</c> (tylko przy <see cref="WingetState.Ready"/>).</summary>
    public string? Version { get; init; }

    public required string Message { get; init; }
}
