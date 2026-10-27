namespace DSOptimizer.Core.Cli;

public sealed class CliParseResult
{
    public CliOptions? Options { get; init; }

    public string? Error { get; init; }

    /// <summary>Ustawiony zawsze, gdy --out miał wartość (także przy błędzie).</summary>
    public string? OutPath { get; init; }

    public bool IsSuccess => Options is not null;
}
