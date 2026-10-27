namespace DSOptimizer.Core.Operations;

/// <summary>Stan pojedynczej operacji; StateAggregator składa z nich DetectionState.</summary>
public enum OperationState
{
    Match,
    Mismatch,
    Unknown,
    /// <summary>Operacja nie dotyczy tej maszyny (np. brak usługi) — pomijana w agregacji.</summary>
    NotApplicable,
    /// <summary>Skrypt detect stwierdził, że tweak nie dotyczy systemu.</summary>
    NotSupported,
}
