using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Autostart;

namespace DSOptimizer.Core.Cli;

/// <summary>
/// Usługi poleceń <c>--list-autostart</c>/<c>--set-autostart</c>/<c>--undo-autostart</c> (składa je host; testy podają fałszywy czytnik,
/// nigdy prawdziwy autostart komputera).
/// </summary>
public sealed class CliAutostartServices
{
    /// <summary>Odczyt pozycji autostartu zalogowanego konta (niczego nie zapisuje).</summary>
    public required IAutostartReader Reader { get; init; }

    /// <summary>Baza znanych pozycji (<c>autostart-known.json</c>): kłódki i sugestie.</summary>
    public required AutostartKnownCatalog Catalog { get; init; }

    /// <summary>SID konta procesu DSOptimizera (porównywany z zalogowanym: gałąź konta przy innym koncie procesu).</summary>
    public required string ProcessSid { get; init; }

    /// <summary>
    /// Kontekst konta podany z zewnątrz (szew testowy); null = <see cref="AutostartContext.Create"/> z zalogowanego użytkownika usług
    /// silnika, <see cref="ProcessSid"/> i rejestru.
    /// </summary>
    public AutostartContext? Context { get; init; }
}
