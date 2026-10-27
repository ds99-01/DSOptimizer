namespace DSOptimizer.Core.Model;

/// <summary>Skrypty PowerShell tweaka. Kontrakt wyjścia opisuje spec, sekcja 4.</summary>
public sealed record ScriptSet
{
    public string? Apply { get; init; }

    public string? Undo { get; init; }

    public string? Detect { get; init; }

    public string? Capture { get; init; }
}
