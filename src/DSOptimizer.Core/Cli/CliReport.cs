using System;
using System.Collections.Generic;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;
using Newtonsoft.Json;

namespace DSOptimizer.Core.Cli;

/// <summary>Raport JSON trybu CLI (czytany m.in. przez testy Pester na VM).</summary>
public sealed class CliReport
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Product { get; init; } = $"{ProductInfo.Name} {ProductInfo.Version}";

    public CliCommand? Command { get; init; }

    public string? TweakId { get; init; }

    public int ExitCode { get; init; }

    public CliExitCode Result { get; init; }

    public DetectionState? State { get; init; }

    public TweakOutcome? Outcome { get; init; }

    public DetectionState? FinalState { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> NotRestored { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public bool RequiresReboot { get; init; }

    public bool Aborted { get; init; }

    public CliRestorePointInfo? RestorePoint { get; init; }

    public CliCatalogInfo? Catalog { get; init; }

    public CliMachineInfo? Machine { get; init; }

    public CliUserInfo? User { get; init; }

    /// <summary>Lista aplikacji (tylko <c>--list-apps</c>); dla innych poleceń null i pominięta w JSON.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<CliAppInfo>? Apps { get; init; }

    /// <summary>Stan winget (polecenia instalacji); dla innych poleceń null i pominięty w JSON.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public CliWingetInfo? Winget { get; init; }

    /// <summary>Wynik każdej pozycji <c>--install-apps</c> w kolejności serii.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<CliInstallItem>? InstallItems { get; init; }

    /// <summary>Pozycje listy <c>apps.json</c> ze statusem (<c>--list-install</c>).</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<CliInstallRow>? InstallRows { get; init; }

    /// <summary>Wiersze wyszukiwania winget (<c>--search-apps</c>).</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<CliSearchRow>? SearchRows { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset FinishedAt { get; init; }
}
