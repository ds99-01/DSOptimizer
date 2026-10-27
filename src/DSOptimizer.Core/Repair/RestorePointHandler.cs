using System;
using System.Globalization;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Repair;

/// <summary>
/// Handler <c>restore-point</c>: ręczny punkt przywracania na żądanie przez <see cref="IRestorePointService"/>.
/// Wyłączona ochrona systemu to porażka z odesłaniem do pozycji „Ochrona systemu” (bez włączania jej po cichu).
/// </summary>
public sealed class RestorePointHandler : IActionHandler
{
    public const string Description = "DSOptimizer – punkt ręczny";

    private readonly IRestorePointService _service;
    private readonly IClock _clock;
    private readonly TimeZoneInfo _timeZone;

    public RestorePointHandler(IRestorePointService service, IClock clock, TimeZoneInfo? timeZone = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public string Name => ActionHandlerNames.RestorePoint;

    public ActionHandlerResult Run(TweakDefinition tweak, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        var result = _service.Create(Description);
        switch (result.Status)
        {
            case RestorePointStatus.Created:
                var local = TimeZoneInfo.ConvertTime(_clock.UtcNow, _timeZone);
                return new ActionHandlerResult
                {
                    Outcome = TweakOutcome.Executed,
                    Message = $"Utworzono punkt przywracania „{Description}” ({local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}).",
                };
            case RestorePointStatus.ProtectionDisabled:
                return new ActionHandlerResult
                {
                    Outcome = TweakOutcome.Failed,
                    Message = "Ochrona systemu na dysku C: jest wyłączona – włącz ją pozycją „Ochrona systemu” w tej kategorii.",
                };
            default:
                var message = string.IsNullOrWhiteSpace(result.Message) ? "Nie udało się utworzyć punktu przywracania." : result.Message!.Trim();
                var code = result.Code is { } value ? $" (kod {value.ToString(CultureInfo.InvariantCulture)})" : string.Empty;
                return new ActionHandlerResult { Outcome = TweakOutcome.Failed, Message = message + code };
        }
    }
}
