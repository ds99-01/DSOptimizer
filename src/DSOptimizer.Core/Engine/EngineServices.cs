using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Journal;

namespace DSOptimizer.Core.Engine;

/// <summary>Zależności silnika (prawdziwe z DSOptimizer.Windows albo fake'i w testach).</summary>
public sealed class EngineServices
{
    public required IRegistry Registry { get; init; }

    public required IServiceManager Services { get; init; }

    public required IScriptRunner Scripts { get; init; }

    public required IProcessLauncher Launcher { get; init; }

    public required IJournalStore Journal { get; init; }

    public required IRestorePointService RestorePoints { get; init; }

    public required ISystemInfo SystemInfo { get; init; }

    public required IUserContext User { get; init; }

    public required ILog Log { get; init; }

    public required IClock Clock { get; init; }

    /// <summary>Handlery akcji C# (pole katalogu <c>handler</c>); domyślnie pusty zestaw.</summary>
    public ActionHandlerSet Handlers { get; init; } = ActionHandlerSet.Empty;
}
