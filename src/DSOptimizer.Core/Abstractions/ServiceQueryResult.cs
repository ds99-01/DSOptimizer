using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Abstractions;

public sealed class ServiceQueryResult
{
    private ServiceQueryResult(ReadStatus status, ServiceStartType startType, bool isRunning, string? error)
    {
        Status = status;
        StartType = startType;
        IsRunning = isRunning;
        Error = error;
    }

    public ReadStatus Status { get; }

    public ServiceStartType StartType { get; }

    public bool IsRunning { get; }

    public string? Error { get; }

    public static ServiceQueryResult Found(ServiceStartType startType, bool isRunning) => new(ReadStatus.Found, startType, isRunning, null);

    public static ServiceQueryResult NotFound() => new(ReadStatus.NotFound, default, false, null);

    public static ServiceQueryResult AccessDenied(string error) => new(ReadStatus.AccessDenied, default, false, error);

    public static ServiceQueryResult Failed(string error) => new(ReadStatus.Error, default, false, error);
}
