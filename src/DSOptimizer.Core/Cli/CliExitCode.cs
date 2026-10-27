namespace DSOptimizer.Core.Cli;

public enum CliExitCode
{
    Success = 0,
    OperationFailed = 1,
    UsageError = 2,
    CatalogError = 3,
    Incomplete = 4,
    InternalError = 5,
    OutputError = 6,
}
