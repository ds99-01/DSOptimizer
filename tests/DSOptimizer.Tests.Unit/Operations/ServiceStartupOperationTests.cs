using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Operations;

public class ServiceStartupOperationTests
{
    private readonly FakeServiceManager _services = new();

    private ServiceStartupOperation Operation(ServiceStartType? original = ServiceStartType.Automatic, bool stop = true)
        => new(new ServiceEntry { Name = "SysMain", StartType = ServiceStartType.Disabled, Original = original, Stop = stop }, _services, ServiceStartupOperation.DefaultServiceTimeout);

    [Fact]
    public void Detect_ReflectsStartType()
    {
        Assert.Equal(OperationState.NotApplicable, Operation().Detect(CancellationToken.None));
        _services.Add("SysMain", ServiceStartType.Automatic, running: true);
        Assert.Equal(OperationState.Mismatch, Operation().Detect(CancellationToken.None));
        _services.Add("SysMain", ServiceStartType.Disabled, running: false);
        Assert.Equal(OperationState.Match, Operation().Detect(CancellationToken.None));
        _services.DenyQuery("SysMain");
        Assert.Equal(OperationState.Unknown, Operation().Detect(CancellationToken.None));
    }

    [Fact]
    public void Capture_RecordsStartTypeAndRunning()
    {
        _services.Add("SysMain", ServiceStartType.AutomaticDelayed, running: true);
        var snapshot = Operation().Capture(CancellationToken.None);
        Assert.Equal(OperationKind.Service, snapshot.Op);
        Assert.True(snapshot.ServiceExisted);
        Assert.Equal(ServiceStartType.AutomaticDelayed, snapshot.StartType);
        Assert.True(snapshot.WasRunning);
    }

    [Fact]
    public void Capture_MissingService_RecordsNotExisting()
    {
        var snapshot = Operation().Capture(CancellationToken.None);
        Assert.False(snapshot.ServiceExisted);
        Assert.Null(snapshot.StartType);
        Assert.False(snapshot.WasRunning);
    }

    [Fact]
    public void Capture_AccessDenied_Throws()
    {
        _services.DenyQuery("SysMain");
        Assert.Equal(OperationErrorKind.AccessDenied, Assert.Throws<OperationException>(() => Operation().Capture(CancellationToken.None)).Kind);
    }

    [Fact]
    public void Apply_SetsStartTypeAndStops()
    {
        _services.Add("SysMain", ServiceStartType.Automatic, running: true);
        Operation().Apply(CancellationToken.None);
        Assert.Equal(ServiceStartType.Disabled, _services.StartTypeOf("SysMain"));
        Assert.False(_services.IsRunning("SysMain"));
    }

    [Fact]
    public void Apply_WithoutStop_LeavesServiceRunning()
    {
        _services.Add("SysMain", ServiceStartType.Automatic, running: true);
        Operation(stop: false).Apply(CancellationToken.None);
        Assert.True(_services.IsRunning("SysMain"));
    }

    [Fact]
    public void Apply_MissingService_IsNoOp()
    {
        // Fake rzuca NotFound przy SetStartType/Stop dla nieistniejącej usługi - brak wyjątku = brak wywołań.
        Operation().Apply(CancellationToken.None);
        Assert.Equal(ReadStatus.NotFound, _services.Query("SysMain").Status);
    }

    [Fact]
    public void Apply_StopTimeout_Throws()
    {
        _services.Add("SysMain", ServiceStartType.Automatic, running: true);
        _services.FailStop("SysMain");
        Assert.Equal(OperationErrorKind.Timeout, Assert.Throws<OperationException>(() => Operation().Apply(CancellationToken.None)).Kind);
    }

    [Fact]
    public void Restore_SetsStartTypeAndStartsIfWasRunning()
    {
        _services.Add("SysMain", ServiceStartType.Automatic, running: true);
        var operation = Operation();
        var snapshot = operation.Capture(CancellationToken.None);
        operation.Apply(CancellationToken.None);
        operation.Restore(snapshot, CancellationToken.None);
        Assert.Equal(ServiceStartType.Automatic, _services.StartTypeOf("SysMain"));
        Assert.True(_services.IsRunning("SysMain"));
    }

    [Fact]
    public void Restore_ServiceStartPendingAtCapture_RestoresStartTypeFromSnapshot()
    {
        // Review Focus 5: usługa przy capture w StartPending (typ Automatic). WindowsServiceManager raportuje wtedy IsRunning=false
        // (Status != Running), więc fake ma running: false. Undo musi przywrócić typ startu ze snapshotu.
        _services.Add("DiagTrack", ServiceStartType.Automatic, running: false);
        var operation = new ServiceStartupOperation(
            new ServiceEntry { Name = "DiagTrack", StartType = ServiceStartType.Disabled, Original = ServiceStartType.Automatic, Stop = true },
            _services,
            ServiceStartupOperation.DefaultServiceTimeout);

        var snapshot = operation.Capture(CancellationToken.None);
        Assert.Equal(ServiceStartType.Automatic, snapshot.StartType);
        Assert.False(snapshot.WasRunning);

        // Stan zmienia się między Capture a Apply (Add nadpisuje wpis w fake): Restore ma użyć snapshotu, nie typu startu widzianego przy Apply.
        _services.Add("DiagTrack", ServiceStartType.Manual, running: false);
        operation.Apply(CancellationToken.None);
        Assert.Equal(ServiceStartType.Disabled, _services.StartTypeOf("DiagTrack"));

        operation.Restore(snapshot, CancellationToken.None);
        Assert.Equal(ServiceStartType.Automatic, _services.StartTypeOf("DiagTrack"));
    }

    [Fact]
    public void Detect_DependsOnStartType_NotOnRunningState()
    {
        var operation = new ServiceStartupOperation(
            new ServiceEntry { Name = "DiagTrack", StartType = ServiceStartType.Disabled, Stop = true },
            _services,
            ServiceStartupOperation.DefaultServiceTimeout);

        // Add nadpisuje wpis usługi w fake: dwa jawne stany tej samej usługi.
        _services.Add("DiagTrack", ServiceStartType.Automatic, running: false);
        Assert.Equal(OperationState.Mismatch, operation.Detect(CancellationToken.None));
        _services.Add("DiagTrack", ServiceStartType.Disabled, running: true);
        Assert.Equal(OperationState.Match, operation.Detect(CancellationToken.None));
    }

    [Fact]
    public void Restore_NotExistingSnapshot_IsNoOp()
    {
        _services.Add("SysMain", ServiceStartType.Manual, running: false);
        Operation().Restore(SnapshotRecord.ForService("SysMain", existed: false, startType: null, wasRunning: false), CancellationToken.None);
        Assert.Equal(ServiceStartType.Manual, _services.StartTypeOf("SysMain"));
        Assert.False(_services.IsRunning("SysMain"));
    }

    [Fact]
    public void DefaultsSnapshot_StartsAutomaticServices()
    {
        Assert.Null(Operation(original: null).DefaultsSnapshot());
        Assert.True(Operation(original: ServiceStartType.Automatic).DefaultsSnapshot()!.WasRunning);
        Assert.False(Operation(original: ServiceStartType.Manual).DefaultsSnapshot()!.WasRunning);
    }

    [Fact]
    public void Plan_ShowsCurrentAndTarget()
    {
        _services.Add("SysMain", ServiceStartType.Automatic, running: true);
        var change = Operation().Plan(CancellationToken.None);
        Assert.Equal("Automatic, działa", change.Current);
        Assert.Equal("Disabled, zatrzymana", change.Target);
    }
}
