using DSOptimizer.Core.Engine;
using DSOptimizer.Tests.Unit.Fakes;

namespace DSOptimizer.Tests.Unit.Engine;

public sealed class EngineFixture
{
    public FakeRegistry Registry { get; } = new();

    public FakeServiceManager Services { get; } = new();

    public FakeScriptRunner Scripts { get; } = new();

    public FakeProcessLauncher Launcher { get; } = new();

    public FakeJournalStore Journal { get; } = new();

    public FakeRestorePointService RestorePoints { get; } = new();

    public FakeSystemInfo SystemInfo { get; } = new();

    public FakeUserContext User { get; } = new();

    public ListLog Log { get; } = new();

    public FakeClock Clock { get; } = new();

    public ActionHandlerSet Handlers { get; set; } = ActionHandlerSet.Empty;

    public EngineServices CreateServices() => new()
    {
        Registry = Registry,
        Services = Services,
        Scripts = Scripts,
        Launcher = Launcher,
        Journal = Journal,
        RestorePoints = RestorePoints,
        SystemInfo = SystemInfo,
        User = User,
        Log = Log,
        Clock = Clock,
        Handlers = Handlers,
    };

    public TweakEngine CreateEngine() => new(CreateServices(), "sha256:test");
}
