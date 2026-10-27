using System;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class WindowsServiceManagerTests
{
    private const string MissingService = "DSOptimizer-nie-istnieje-12345";
    private readonly WindowsServiceManager _services = new(new WindowsRegistry());

    [Fact]
    public void Query_RpcSs_IsAutomaticAndRunning()
    {
        var result = _services.Query("RpcSs");

        Assert.Equal(ReadStatus.Found, result.Status);
        Assert.Equal(ServiceStartType.Automatic, result.StartType);
        Assert.True(result.IsRunning);
    }

    [Fact]
    public void Query_RandomMissingService_IsNotFound()
        => Assert.Equal(ReadStatus.NotFound, _services.Query("DSO_Test_NoSuchService_" + Guid.NewGuid().ToString("N")).Status);

    [Fact]
    public void Writes_OnMissingService_ThrowNotFound()
    {
        Assert.Equal(OperationErrorKind.NotFound, Assert.Throws<OperationException>(() => _services.SetStartType(MissingService, ServiceStartType.Manual)).Kind);
        Assert.Equal(OperationErrorKind.NotFound, Assert.Throws<OperationException>(() => _services.StopService(MissingService, TimeSpan.FromSeconds(1))).Kind);
        Assert.Equal(OperationErrorKind.NotFound, Assert.Throws<OperationException>(() => _services.StartService(MissingService, TimeSpan.FromSeconds(1))).Kind);
    }

    [Theory]
    [InlineData(ServiceStartType.Boot)]
    [InlineData(ServiceStartType.System)]
    public void SetStartType_BootOrSystem_IsRejected(ServiceStartType startType)
        => Assert.Equal(OperationErrorKind.Unexpected, Assert.Throws<OperationException>(() => _services.SetStartType("DSO_Test_NoSuchService_" + Guid.NewGuid().ToString("N"), startType)).Kind);

    [Fact]
    public void StopService_OnMissingRandomService_ThrowsNotFound()
        => Assert.Equal(OperationErrorKind.NotFound, Assert.Throws<OperationException>(() => _services.StopService("DSO_Test_NoSuchService_" + Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(1))).Kind);

    // Tylko odczyt: RpcSs zawsze działa i ma działające zależne (m.in. Winmgmt). Żadna usługa hosta nie jest zatrzymywana.
    [Fact]
    public void GetRunningDependents_RpcSs_ListsRunningDependentsWithoutDuplicates()
    {
        var dependents = _services.GetRunningDependents("RpcSs");

        Assert.Contains("Winmgmt", dependents, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(dependents.Count, dependents.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(dependents, dependent => Assert.Equal(ReadStatus.Found, _services.Query(dependent).Status));
    }

    [Fact]
    public void GetRunningDependents_MissingService_ThrowsNotFound()
        => Assert.Equal(OperationErrorKind.NotFound, Assert.Throws<OperationException>(() => _services.GetRunningDependents("DSO_Test_NoSuchService_" + Guid.NewGuid().ToString("N"))).Kind);

    [Fact]
    public void StopServiceWithDependents_MissingService_ThrowsNotFound()
        => Assert.Equal(OperationErrorKind.NotFound, Assert.Throws<OperationException>(() => _services.StopServiceWithDependents("DSO_Test_NoSuchService_" + Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(1))).Kind);

    [Fact]
    public void MapStopError_DependentServicesRunning_IsUnexpectedWithServiceName()
    {
        var error = WindowsServiceManager.MapStopError("Dummy", 1051);

        Assert.NotNull(error);
        Assert.Equal(OperationErrorKind.Unexpected, error!.Kind);
        Assert.Contains("Dummy", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapStopError_ServiceNotActive_IsNotAnError()
        => Assert.Null(WindowsServiceManager.MapStopError("Dummy", 1062));

    [Fact]
    public void MapStopError_AccessDeniedAndMissing_MapToKinds()
    {
        Assert.Equal(OperationErrorKind.AccessDenied, WindowsServiceManager.MapStopError("Dummy", 5)!.Kind);
        Assert.Equal(OperationErrorKind.NotFound, WindowsServiceManager.MapStopError("Dummy", 1060)!.Kind);
    }
}
