namespace DSOptimizer.Core.Cli;

/// <summary>
/// Wiersz <c>winget search</c> w raporcie <c>--search-apps</c>. <see cref="Installable"/> = false przy Id obciętym „…” albo pustym:
/// takiego Id nie przyjmuje <c>--install-apps winget:&lt;id&gt;</c>.
/// </summary>
public sealed class CliSearchRow
{
    public required string Name { get; init; }

    public required string WingetId { get; init; }

    public string? Version { get; init; }

    public bool Installable { get; init; }
}
