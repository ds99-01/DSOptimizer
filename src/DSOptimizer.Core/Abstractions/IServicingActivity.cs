namespace DSOptimizer.Core.Abstractions;

/// <summary>Czy w systemie trwa już operacja serwisowania (DISM, SFC, cleanmgr itd.).</summary>
public interface IServicingActivity
{
    /// <summary>Nazwa pierwszego działającego procesu serwisowania albo <c>null</c>, gdy nic nie trwa.</summary>
    string? ActiveOperation();
}
