using System;
using System.Threading;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Repair;

/// <summary>
/// Handlery <c>wu-reset</c> (podstawowy: usługi + foldery + diagnoza polityk) i <c>wu-reset-full</c> (dodatkowo odmowa w domenie,
/// eksport i usunięcie polityk, typy startu ze świeżej 26H2). Algorytm: <see cref="WindowsUpdateReset"/>.
/// </summary>
public sealed class WindowsUpdateResetHandler : IActionHandler
{
    private readonly bool _full;
    private readonly RepairServices _services;

    public WindowsUpdateResetHandler(bool full, RepairServices services)
    {
        _full = full;
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string Name => _full ? ActionHandlerNames.WuResetFull : ActionHandlerNames.WuReset;

    public ActionHandlerResult Run(TweakDefinition tweak, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
        => new WindowsUpdateReset(_services, _full, output, cancellationToken).Run();
}
