namespace DSOptimizer.Core.Engine;

public enum RestorePointPolicy
{
    /// <summary>Bez punktu przywracania seria się nie zaczyna.</summary>
    Required,
    /// <summary>Spróbuj; przy błędzie ostrzeż i kontynuuj.</summary>
    IfAvailable,
    Skip,
}
