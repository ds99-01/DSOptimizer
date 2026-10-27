namespace DSOptimizer.Core.Operations;

/// <summary>Pozycja podglądu zmian (Preview) — co, z jakiej wartości, na jaką.</summary>
public sealed class PlannedChange
{
    public required string Description { get; init; }

    public required string Current { get; init; }

    public required string Target { get; init; }

    public bool RequiresReboot { get; init; }
}
