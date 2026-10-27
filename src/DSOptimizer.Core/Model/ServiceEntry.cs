namespace DSOptimizer.Core.Model;

public sealed record ServiceEntry
{
    public required string Name { get; init; }

    public required ServiceStartType StartType { get; init; }

    public ServiceStartType? Original { get; init; }

    /// <summary>Zatrzymaj działającą usługę po zmianie typu startu.</summary>
    public bool Stop { get; init; }
}
