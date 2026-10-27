using DSOptimizer.Core.Apps;

namespace DSOptimizer.Core.Cli;

/// <summary>Wiersz listy aplikacji w raporcie <c>--list-apps</c> (tylko widoczne wiersze, po klasyfikacji).</summary>
public sealed class CliAppInfo
{
    public required string Id { get; init; }

    public AppKind Kind { get; init; }

    public required string Name { get; init; }

    public string? Publisher { get; init; }

    public AppGroup Group { get; init; }

    public AppRemovability Removability { get; init; }
}
