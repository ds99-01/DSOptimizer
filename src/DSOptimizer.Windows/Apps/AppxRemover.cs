using System;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Apps;

namespace DSOptimizer.Windows.Apps;

/// <summary>
/// Wykonawca usuwania na prawdziwym systemie (<see cref="IAppRemover"/>): AppX przez <see cref="AppxRemovalScript"/> uruchamiany
/// z <see cref="IScriptRunner"/> (<c>-File</c>, limit <see cref="AppRemovalTimeouts.Store"/>, żądanie w <c>Snapshot</c>), programy Win32
/// przez <see cref="UninstallerRunner"/>. O wyniku nie decyduje: sprawdza go <see cref="AppRemovalEngine"/> świeżym odczytem.
/// </summary>
public sealed class AppxRemover : IAppRemover
{
    private readonly IScriptRunner _runner;
    private readonly Func<UninstallPlan, TimeSpan, CancellationToken, UninstallerRun> _runUninstaller;

    public AppxRemover(IScriptRunner runner)
        : this(runner, UninstallerRunner.Run)
    {
    }

    /// <summary>Wstrzykiwany start deinstalatora (testy: żaden prawdziwy deinstalator na hoście).</summary>
    public AppxRemover(IScriptRunner runner, Func<UninstallPlan, TimeSpan, CancellationToken, UninstallerRun> runUninstaller)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _runUninstaller = runUninstaller ?? throw new ArgumentNullException(nameof(runUninstaller));
    }

    public ScriptResult RemoveStoreApp(AppxInfo appx, string userSid, StoreRemovalScope scope, bool deprovision, CancellationToken ct)
    {
        if (appx is null)
        {
            throw new ArgumentNullException(nameof(appx));
        }

        if (userSid is null)
        {
            throw new ArgumentNullException(nameof(userSid));
        }

        return _runner.Run(
            new ScriptRequest
            {
                Body = AppxRemovalScript.Body,
                UserSid = userSid,
                Snapshot = AppxRemovalScript.Request(appx.PackageName, scope, deprovision),
                Timeout = AppRemovalTimeouts.Store,
            },
            null,
            ct);
    }

    public UninstallerRun RunUninstaller(UninstallPlan plan, TimeSpan timeout, CancellationToken ct)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        return _runUninstaller(plan, timeout, ct);
    }
}
