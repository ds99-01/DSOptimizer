namespace DSOptimizer.Core.Abstractions;

public interface IRestorePointService
{
    RestorePointResult Create(string description);
}
