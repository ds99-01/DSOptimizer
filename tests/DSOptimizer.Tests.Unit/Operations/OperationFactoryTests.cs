using System.Linq;
using System.Threading;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;
using DSOptimizer.Tests.Unit.Fakes;
using Xunit;

namespace DSOptimizer.Tests.Unit.Operations;

public class OperationFactoryTests
{
    private readonly FakeRegistry _registry = new();
    private readonly OperationFactory _factory;

    public OperationFactoryTests()
        => _factory = new OperationFactory(_registry, new FakeServiceManager(), new FakeScriptRunner(), new FakeUserContext { Sid = "S-1-5-21-42" });

    private static TweakDefinition Tweak(string path, ScriptSet? script = null) => new()
    {
        Id = "test.factory",
        Category = "test",
        Kind = TweakKind.Tweak,
        Title = "F",
        Risk = RiskLevel.Safe,
        Scope = TweakScope.User,
        Registry = new[] { new RegistryEntry { Path = path, Name = "V", Value = RegValue.FromDWord(1), Original = RegValue.Absent } },
        Services = new[] { new ServiceEntry { Name = "S", StartType = ServiceStartType.Manual, Original = ServiceStartType.Automatic } },
        Script = script,
    };

    [Fact]
    public void Create_OrdersRegistryServicesScript()
    {
        var kinds = _factory.Create(Tweak(@"HKLM\X", new ScriptSet { Detect = "D" })).Select(o => o.Kind).ToList();
        Assert.Equal(new[] { OperationKind.Registry, OperationKind.Service, OperationKind.Script }, kinds);
    }

    [Fact]
    public void Create_WithoutScript_HasNoScriptOperation()
        => Assert.DoesNotContain(_factory.Create(Tweak(@"HKLM\X")), o => o.Kind == OperationKind.Script);

    [Fact]
    public void Create_MapsHkcuToInteractiveUserHive()
    {
        _factory.Create(Tweak(@"HKCU\Control Panel\Desktop"))[0].Apply(CancellationToken.None);
        Assert.Equal(RegValue.FromDWord(1), _registry.Get(@"HKU\S-1-5-21-42\Control Panel\Desktop", "V"));
    }

    [Fact]
    public void MapPath_LeavesOtherRootsUntouched()
    {
        Assert.Equal(@"HKLM\SOFTWARE\X", _factory.MapPath(@"HKLM\SOFTWARE\X"));
        Assert.Equal(@"HKU\S-1-5-21-42\X", _factory.MapPath(@"hkcu\X"));
    }
}
