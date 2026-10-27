namespace DSOptimizer.Core.Abstractions;

public interface IDomainInfo
{
    /// <summary>Czy komputer należy do domeny (<c>Win32_ComputerSystem.PartOfDomain</c>); <c>null</c>, gdy nie udało się ustalić.</summary>
    bool? IsDomainJoined();
}
